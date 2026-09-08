using Kcc.Recorder;
using Xunit;

namespace Kcc.Recorder.Tests;

public class ConveyorReportTests
{
    static readonly DateTime T0 = new(2026, 9, 7, 8, 0, 0, DateTimeKind.Unspecified);
    static readonly TelegramFormat Fmt = TelegramFormat.Default;
    static readonly ConveyorOptions Opts = ConveyorOptions.From(new KccConfig());

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

    static string Data(string mc, string rp, string label)
    {
        var buf = new char[Fmt.Length];
        Array.Fill(buf, '.');
        void Put(string field, string v)
        {
            var a = Off(field);
            for (var i = 0; i < v.Length && a + i < buf.Length; i++) buf[a + i] = v[i];
        }
        Put("MessageCode", mc);
        Put("ResourcePoint", rp);
        Put("ResourceLabel", label);
        return new string(buf);
    }

    sealed class Feed
    {
        long _id = 1;
        public readonly List<Telegram> Rows = [];

        /// <summary>Ereignis als DM/AK-Doppel wie von der Anlage — prüft nebenbei die Dedup.</summary>
        public void Add(double sec, string mc, string rp, string label)
        {
            var t = T0.AddSeconds(sec);
            Rows.Add(new(_id++, t, TelegramDirection.FromPlc, "L1", Data(mc, rp, label), null));
            Rows.Add(new(_id++, t.AddSeconds(0.1), TelegramDirection.FromPlc, "L1", Data(mc, rp, label), null));
        }
    }

    [Fact]
    public void Belegt_ist_von_der_Ankunft_bis_zum_Verlassen()
    {
        var feed = new Feed();
        // Doku-Ablauf je Ladeeinheit: ENDTSP = Ankunft, TSPORD = Auftrag zum Weitertransport,
        // RPFREE = Verlassen. Zwei Durchläufe, dazwischen 30 s leer.
        feed.Add(0, "ENDTSP", "LB41", "LE1");
        feed.Add(20, "TSPORD", "LB41", "LE1");
        feed.Add(30, "RPFREE", "LB41", "LE1");
        feed.Add(60, "ENDTSP", "LB41", "LE2");
        feed.Add(80, "TSPORD", "LB41", "LE2");
        feed.Add(90, "RPFREE", "LB41", "LE2");
        feed.Add(45, "ENDTSP", "EA21", "LE9");   // anderer Punkt — bleibt draußen

        var r = ConveyorReport.Compute(feed.Rows, Fmt, "LB41", T0, T0.AddSeconds(120), Opts);

        Assert.Equal(2, r.Completed);      // Ankünfte
        Assert.Equal(2, r.Orders);         // Weitertransport-Aufträge
        Assert.Equal(2, r.FreeSignals);

        // 2 × 30 s belegt (0–30 und 60–90) — der TSPORD dazwischen beendet nichts.
        Assert.Equal(60, r.BusySeconds);
        Assert.Equal(50, r.BusyPercent);
        Assert.Equal(60, r.IdleSeconds);

        Assert.Equal(30, r.AvgOccupiedSeconds);    // ENDTSP -> RPFREE
        Assert.Equal(20, r.AvgOrderWaitSeconds);   // ENDTSP -> TSPORD
        Assert.Equal(10, r.AvgDepartSeconds);      // TSPORD -> RPFREE
        Assert.Equal(30, r.AvgIdleSeconds);        // RPFREE(30) -> ENDTSP(60)
        Assert.Equal(T0.AddSeconds(90), r.LatestAt);
    }

    [Fact]
    public void Ohne_Ereignisse_ist_der_Punkt_komplett_frei()
    {
        var r = ConveyorReport.Compute([], Fmt, "LB41", T0, T0.AddHours(1), Opts);

        Assert.Equal(0, r.Orders);
        Assert.Equal(0, r.BusyPercent);
        Assert.Equal(3600, r.IdleSeconds);
        Assert.Equal(0, r.AvgOccupiedSeconds);
        Assert.Null(r.LatestAt);
    }

    [Fact]
    public void Noch_belegt_am_Fensterende_zaehlt_bis_zum_Rand()
    {
        var feed = new Feed();
        feed.Add(0, "ENDTSP", "LB41", "LE1");
        feed.Add(20, "TSPORD", "LB41", "LE1");
        // Kein RPFREE: die Ladeeinheit steht noch auf dem Platz.

        var r = ConveyorReport.Compute(feed.Rows, Fmt, "LB41", T0, T0.AddSeconds(100), Opts);

        Assert.Equal(100, r.BusySeconds);
        Assert.Equal(100, r.BusyPercent);
        Assert.Equal(0, r.IdleSeconds);
        Assert.Equal(20, r.AvgOrderWaitSeconds);
    }

    [Fact]
    public void Belegung_von_vor_dem_Fenster_zaehlt_ab_Fensteranfang()
    {
        var feed = new Feed();
        feed.Add(-100, "ENDTSP", "LB41", "LE1");   // Ankunft lag vor dem Fenster
        feed.Add(40, "RPFREE", "LB41", "LE1");

        var r = ConveyorReport.Compute(feed.Rows, Fmt, "LB41", T0, T0.AddSeconds(200), Opts);

        Assert.Equal(40, r.BusySeconds);   // nicht 140 — auf den Fensteranfang beschnitten
        Assert.Equal(20, r.BusyPercent);
        Assert.Equal(160, r.IdleSeconds);
    }

    [Fact]
    public void Zweite_Ankunft_ohne_Freimeldung_verlaengert_dieselbe_Belegung()
    {
        var feed = new Feed();
        feed.Add(0, "ENDTSP", "LB41", "LE1");
        feed.Add(30, "ENDTSP", "LB41", "LE2");     // Nachrücker ohne zwischenzeitliches RPFREE
        feed.Add(60, "RPFREE", "LB41", "LE2");

        var r = ConveyorReport.Compute(feed.Rows, Fmt, "LB41", T0, T0.AddSeconds(120), Opts);

        Assert.Equal(60, r.BusySeconds);           // eine durchgehende Belegung 0–60
        Assert.Equal(60, r.AvgOccupiedSeconds);
    }

    [Fact]
    public void Punkt_ohne_Freimeldungen_bleibt_belegt_und_zeigt_das_an()
    {
        var feed = new Feed();
        // Meldet ein Punkt kein RPFREE, gilt er durchgehend als belegt — nur RPFREE gibt frei.
        // Der Zählerstand Frei=0 macht das in der Kachel sichtbar, statt es zu kaschieren.
        feed.Add(0, "ENDTSP", "LB41", "LE1");
        feed.Add(20, "TSPORD", "LB41", "LE1");
        feed.Add(60, "ENDTSP", "LB41", "LE2");
        feed.Add(80, "TSPORD", "LB41", "LE2");

        var r = ConveyorReport.Compute(feed.Rows, Fmt, "LB41", T0, T0.AddSeconds(120), Opts);

        Assert.Equal(0, r.FreeSignals);
        Assert.Equal(2, r.Completed);
        Assert.Equal(100, r.BusyPercent);
        Assert.Equal(0, r.AvgIdleSeconds);
    }

    [Fact]
    public void Verlauf_endet_am_rechten_Fensterrand_und_bleibt_in_Prozent()
    {
        var feed = new Feed();
        feed.Add(0, "ENDTSP", "LB41", "LE1");
        feed.Add(60, "TSPORD", "LB41", "LE1");
        feed.Add(90, "RPFREE", "LB41", "LE1");

        var r = ConveyorReport.Compute(
            feed.Rows, Fmt, "LB41", T0, T0.AddMinutes(10), Opts, bucketMinutes: 5, stepMinutes: 1);

        Assert.Equal(T0.AddMinutes(10), r.Series[^1].At);
        Assert.All(r.Series, b => Assert.InRange(b.Uph, 0, 100));
        Assert.Contains(r.Series, b => b.Uph > 0);
    }

    [Fact]
    public void Punkt_mit_RBG_Verbindung_bekommt_keine_Foerdertechnik_Auswertung()
    {
        var feed = new Feed();
        feed.Add(0, "ENDTSP", "MA72", "LE1");
        feed.Add(30, "RPFREE", "MA72", "LE1");

        var report = TelegramUtilization.Compute(
            feed.Rows, Fmt, 60, 200, T0.AddMinutes(60),
            [new ResourcePointConfig { Name = "MA72", Connection = "RBG01" },
             new ResourcePointConfig { Name = "LB41" }],
            rbg: RbgOptions.From(new KccConfig()),
            conveyor: Opts);

        Assert.Null(report.Points.Single(p => p.ResourcePoint == "MA72").Conveyor);
        Assert.NotNull(report.Points.Single(p => p.ResourcePoint == "LB41").Conveyor);
    }
}
