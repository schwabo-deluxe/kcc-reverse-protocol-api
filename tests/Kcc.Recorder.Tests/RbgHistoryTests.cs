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

    static string Data(string mc, string label, string src, string dst, string type = "DM")
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
        Put("Source", src);
        Put("Destination", dst);
        return new string(buf);
    }

    const string Rack = "010361211";
    const string Infeed = "MA41";
    const string Outfeed = "MA62";
    const string Crane = "SR01LU11";

    sealed class Feed
    {
        long _id = 1;
        public readonly List<Telegram> Rows = [];

        public void Add(double minutes, string mc, string label, string src, string dst, string conn)
        {
            var t = T0.AddMinutes(minutes);
            Rows.Add(new(_id++, t, TelegramDirection.FromPlc, conn, Data(mc, label, src, dst), null));
            Rows.Add(new(_id++, t.AddSeconds(0.1), TelegramDirection.FromPlc, conn,
                Data(mc, label, src, dst, "AK"), null));
        }

        /// <summary>Ein Transport: aufnehmen, dann abgeben. Ziel des ENDDEP bestimmt die Richtung.</summary>
        public void Transport(double startMin, double endMin, string label, bool store, string conn)
        {
            var pickFrom = store ? Infeed : Rack;
            var dropTo = store ? Rack : Outfeed;
            Add(startMin, "PUPORD", label, pickFrom, Crane, conn);
            Add(endMin, "ENDDEP", label, Crane, dropTo, conn);
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
        // RBG01: im ersten 15-min-Raster 2 Ein- und 2 Auslagerungen, im zweiten 1 Auslagerung.
        feed.Transport(0, 1, "A1", store: true, conn: "RBG01");
        feed.Transport(1, 2, "A2", store: false, conn: "RBG01");
        feed.Transport(2, 3, "A3", store: true, conn: "RBG01");
        feed.Transport(3, 4, "A4", store: false, conn: "RBG01");
        feed.Transport(19, 20, "A5", store: false, conn: "RBG01");
        // RBG02: eine Einlagerung, Auftrag 1 min vor dem Abschluss.
        feed.Transport(4, 5, "B1", store: true, conn: "RBG02");
        // Fremde Verbindung bleibt draussen.
        feed.Transport(5, 6, "C1", store: true, conn: "RBG99");

        var rows = RbgReport.Aggregate(
            feed.Rows, Fmt, ["RBG01", "RBG02"], T0, T0.AddMinutes(30),
            TimeSpan.FromMinutes(15), Opts);

        Assert.DoesNotContain(rows, r => r.Connection == "RBG99");

        var first = rows.Single(r => r.Connection == "RBG01" && r.Bucket == T0);
        Assert.Equal(2, first.Stores);
        Assert.Equal(2, first.Retrievals);
        Assert.Equal(2, first.DoubleCycles);
        Assert.Equal(0, first.SingleCycles);

        var second = rows.Single(r => r.Connection == "RBG01" && r.Bucket == T0.AddMinutes(15));
        Assert.Equal(0, second.Stores);
        Assert.Equal(1, second.Retrievals);

        var other = rows.Single(r => r.Connection == "RBG02");
        Assert.Equal(1, other.Stores);
        Assert.Equal(60, other.BusySeconds, 1);   // PUPORD -> ENDDEP
    }

    [Fact]
    public void Nutzungszeit_nimmt_die_Totzeiten_aus_dem_Nenner()
    {
        var rows = new List<RbgSampleRow>
        {
            new() { Bucket = T0, Connection = "RBG01", Stores = 10, Retrievals = 10, BusySeconds = 1800 },
            new() { Bucket = T0.AddHours(1), Connection = "RBG01", Stores = 10, Retrievals = 10, BusySeconds = 1800 },
        };
        var cfg = new OperatingHoursConfig { Enabled = true, Start = "06:00", End = "15:15" };

        // Fenster über den ganzen Tag; ohne Nutzungszeit verwässern ~22 stille Stunden alles.
        var full = RbgHistoryReport.Compute(rows, T0, T0.AddHours(24), 60, Points());
        var op = RbgHistoryReport.Compute(rows, T0, T0.AddHours(24), 60, Points(), operatingHours: cfg);

        Assert.Equal(24, full.OperatingHours);
        Assert.Equal(7.25, op.OperatingHours);   // Fenster ab 08:00 bis Betriebsschluss 15:15

        var withOp = op.Totals.Single(t => t.Connection == "RBG01").AvgBusyPercent;
        var withoutOp = full.Totals.Single(t => t.Connection == "RBG01").AvgBusyPercent;
        Assert.True(withOp > withoutOp);
        Assert.Equal(Math.Round(3600.0 / (7.25 * 3600) * 100, 1), withOp, 1);   // 3600 s belegt / 7,25 h
    }

    [Fact]
    public void Compute_vergleicht_die_Geraete_und_nennt_die_Spreizung()
    {
        // RBG01 fährt doppelt so viel wie RBG02.
        var rows = new List<RbgSampleRow>
        {
            new() { Bucket = T0, Connection = "RBG01", Stores = 10, Retrievals = 10, BusySeconds = 1800 },
            new() { Bucket = T0.AddHours(1), Connection = "RBG01", Stores = 10, Retrievals = 10 },
            new() { Bucket = T0, Connection = "RBG02", Stores = 5, Retrievals = 5, BusySeconds = 900 },
            new() { Bucket = T0.AddHours(1), Connection = "RBG02", Stores = 5, Retrievals = 5 },
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

        // Leistung = Spiele/h gegen die Auslegung (30 DS/h), Leerlauf = Rest des Rasters.
        Assert.Equal(33.3, r.Buckets[0].LoadPercent["RBG01"]);   // 10 von 30
        Assert.Equal(16.7, r.Buckets[0].LoadPercent["RBG02"]);   // 5 von 30
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
            new() { Bucket = T0, Connection = "RBG01", Stores = 10, Retrievals = 0 },
            new() { Bucket = T0.AddHours(1), Connection = "RBG01", Stores = 0, Retrievals = 10 },
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
            new() { Bucket = T0, Connection = "RBG01", Stores = 4, Retrievals = 4 },
        };

        var r = RbgHistoryReport.Compute(rows, T0, T0.AddHours(1), 60, Points());

        Assert.Contains("RBG02", r.Connections);                      // konfiguriert, also sichtbar
        Assert.Equal(0, r.Totals.Single(t => t.Connection == "RBG02").Cycles);
        Assert.Equal(0, r.SpreadPercent);                             // nur ein Gerät hat gefahren
        Assert.Equal("RBG01", r.Busiest);
        Assert.Null(r.Quietest);
    }
}
