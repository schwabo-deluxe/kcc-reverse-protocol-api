using Kcc.Recorder;
using Xunit;

namespace Kcc.Recorder.Tests;

public class RbgHistoryTests
{
    static readonly DateTime T0 = new(2026, 9, 7, 8, 0, 0, DateTimeKind.Unspecified);
    static readonly TelegramFormat Fmt = TelegramFormat.Default;
    static readonly RbgOptions Opts = RbgOptions.From(new KccConfig());

    static int Off(string name)
    {
        var p = 0;
        foreach (var f in Fmt.Fields)
        {
            if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))
                return p;
            p += f.Length;
        }
        throw new ArgumentException(name);
    }

    static string Data(string mc, string label, string type = "DM")
    {
        var buf = new char[Fmt.Length];
        Array.Fill(buf, '.');
        void Put(string field, string v)
        {
            var a = Off(field);
            for (var i = 0; i < v.Length && a + i < buf.Length; i++) buf[a + i] = v[i];
        }
        Put("TelegramType", type);
        Put("MessageCode", mc);
        Put("ResourceLabel", label);
        Put("Source", "SRC");
        Put("Destination", "DST");
        return new string(buf);
    }

    sealed class Feed
    {
        long _id = 1;
        public readonly List<Telegram> Rows = [];

        /// <summary>Ereignis als DM/AK-Doppel wie von der Anlage — prüft nebenbei die Dedup.</summary>
        public void Add(double minutes, string mc, string label, string conn)
        {
            var t = T0.AddMinutes(minutes);
            Rows.Add(new(_id++, t, TelegramDirection.FromPlc, conn, Data(mc, label), null));
            Rows.Add(new(_id++, t.AddSeconds(0.1), TelegramDirection.FromPlc, conn, Data(mc, label), null));
        }
    }

    static List<ResourcePointConfig> Points() =>
    [
        new() { Name = "MA72", Label = "RBG 1", Connection = "RBG01" },
        new() { Name = "MB72", Label = "RBG 2", Connection = "RBG02" },
    ];

    [Fact]
    public void Aggregate_verdichtet_je_Raster_und_Verbindung()
    {
        var feed = new Feed();
        // RBG01: im ersten 15-min-Raster 2 Ein + 2 Aus, im zweiten 1 Aus.
        feed.Add(1, "ENDDEP", "A1", "RBG01");
        feed.Add(2, "ENDPUP", "A2", "RBG01");
        feed.Add(3, "ENDDEP", "A3", "RBG01");
        feed.Add(4, "ENDPUP", "A4", "RBG01");
        feed.Add(20, "ENDPUP", "A5", "RBG01");
        // RBG02: nur eine Einlagerung, mit Auftrag 60 s davor.
        feed.Add(4, "DEPORD", "B1", "RBG02");
        feed.Add(5, "ENDDEP", "B1", "RBG02");
        // Fremde Verbindung bleibt draußen.
        feed.Add(6, "ENDDEP", "C1", "RBG99");

        var rows = RbgReport.Aggregate(
            feed.Rows, Fmt, ["RBG01", "RBG02"], T0, T0.AddMinutes(30),
            TimeSpan.FromMinutes(15), Opts);

        Assert.DoesNotContain(rows, r => r.Connection == "RBG99");

        var first = rows.Single(r => r.Connection == "RBG01" && r.Bucket == T0);
        Assert.Equal(2, first.Puts);
        Assert.Equal(2, first.Fetches);
        Assert.Equal(2, first.DoubleCycles);
        Assert.Equal(0, first.SingleCycles);
        Assert.Equal(2, first.Cycles);

        var second = rows.Single(r => r.Connection == "RBG01" && r.Bucket == T0.AddMinutes(15));
        Assert.Equal(0, second.Puts);
        Assert.Equal(1, second.Fetches);
        Assert.Equal(0.5, second.Cycles);   // Einzelspiel zählt halb

        var other = rows.Single(r => r.Connection == "RBG02");
        Assert.Equal(1, other.Puts);
        Assert.Equal(60, other.BusySeconds, 1);   // DEPORD → ENDDEP
    }

    [Fact]
    public void Compute_vergleicht_die_Geraete_und_nennt_die_Spreizung()
    {
        // RBG01 fährt doppelt so viel wie RBG02.
        var rows = new List<RbgSampleRow>
        {
            new() { Bucket = T0, Connection = "RBG01", Puts = 10, Fetches = 10, BusySeconds = 1800 },
            new() { Bucket = T0.AddHours(1), Connection = "RBG01", Puts = 10, Fetches = 10 },
            new() { Bucket = T0, Connection = "RBG02", Puts = 5, Fetches = 5, BusySeconds = 900 },
            new() { Bucket = T0.AddHours(1), Connection = "RBG02", Puts = 5, Fetches = 5 },
        };

        var r = RbgHistoryReport.Compute(rows, T0, T0.AddHours(2), 60, Points());

        Assert.Equal("RBG01", r.Busiest);
        Assert.Equal("RBG02", r.Quietest);
        Assert.Equal(30, r.TotalCycles);        // 20 + 10
        Assert.Equal(50, r.SpreadPercent);      // (20 - 10) / 20

        var a = r.Totals.Single(t => t.Connection == "RBG01");
        Assert.Equal("RBG 1", a.Label);
        Assert.Equal(20, a.DoubleCycles);
        Assert.Equal(0, a.SingleCycles);
        Assert.Equal(10, a.AvgCyclesPerHour);   // 20 Spiele in 2 h
        Assert.Equal(66.7, a.Share);
        Assert.Equal(25, a.AvgBusyPercent);     // 1800 s von 7200 s
        Assert.Equal(2, a.ActiveHours);

        var b = r.Totals.Single(t => t.Connection == "RBG02");
        Assert.Equal(33.3, b.Share);
        Assert.Equal(5, b.AvgCyclesPerHour);

        // Zwei Stützpunkte à 60 min, beide mit Werten für beide Geräte.
        Assert.Equal(2, r.Buckets.Count);
        Assert.Equal(10, r.Buckets[0].CyclesPerHour["RBG01"]);
        Assert.Equal(5, r.Buckets[0].CyclesPerHour["RBG02"]);
        Assert.Equal(50, r.Buckets[0].BusyPercent["RBG01"]);

        // Leistung = Spiele/h gegen die Bezugsleistung (Standard 60 DS/h), Leerlauf = Rest.
        Assert.Equal(16.7, r.Buckets[0].LoadPercent["RBG01"]);   // 10 von 60
        Assert.Equal(8.3, r.Buckets[0].LoadPercent["RBG02"]);    // 5 von 60
        Assert.Equal(30, r.Buckets[0].IdleMinutes["RBG01"]);     // 60 min − 1800 s
        Assert.Equal(60, r.Buckets[1].IdleMinutes["RBG01"]);     // zweite Stunde ohne Auftragszeit
        Assert.Equal(1.5, a.IdleHours);                          // 2 h − 1800 s
    }

    [Fact]
    public void Spiele_werden_im_Aufzeichnungsraster_bestimmt_nicht_im_Anzeigeraster()
    {
        // Eine Stunde nur Einlagerungen, die nächste nur Auslagerungen: das sind Einzelspiele,
        // auch wenn die Anzeige beide Stunden zu einem Balken zusammenfasst.
        var rows = new List<RbgSampleRow>
        {
            new() { Bucket = T0, Connection = "RBG01", Puts = 10, Fetches = 0 },
            new() { Bucket = T0.AddHours(1), Connection = "RBG01", Puts = 0, Fetches = 10 },
        };

        var r = RbgHistoryReport.Compute(rows, T0, T0.AddHours(2), 120, Points());
        var t = r.Totals.Single(x => x.Connection == "RBG01");

        Assert.Equal(0, t.DoubleCycles);
        Assert.Equal(20, t.SingleCycles);
        // Bewertet über die Spielzeiten: 20 Einzelspiele à 37,5 s = 750 s, das sind 12,5
        // Doppelspiele à 60 s — nicht 10, wie die Faustformel „Einzel = halbes Doppel" ergäbe.
        Assert.Equal(12.5, t.Cycles);
        Assert.Single(r.Buckets);
    }

    [Fact]
    public void Stillstehendes_Geraet_erscheint_ohne_die_Spreizung_zu_verfaelschen()
    {
        var rows = new List<RbgSampleRow>
        {
            new() { Bucket = T0, Connection = "RBG01", Puts = 4, Fetches = 4 },
        };

        var r = RbgHistoryReport.Compute(rows, T0, T0.AddHours(1), 60, Points());

        Assert.Contains("RBG02", r.Connections);                      // konfiguriert, also sichtbar
        Assert.Equal(0, r.Totals.Single(t => t.Connection == "RBG02").Cycles);
        Assert.Equal(0, r.SpreadPercent);                             // nur ein Gerät hat gefahren
        Assert.Equal("RBG01", r.Busiest);
        Assert.Null(r.Quietest);
    }
}
