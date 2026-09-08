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
    public void Misst_Belegung_Transportdauer_und_Wartezeit()
    {
        var feed = new Feed();
        // Fenster 200 s. Drei Transporte à 30 s, dazwischen 20 s Warten.
        feed.Add(0, "TSPORD", "LB41", "LE1");
        feed.Add(30, "ENDTSP", "LB41", "LE1");
        feed.Add(31, "RPFREE", "LB41", "LE1");
        feed.Add(50, "TSPORD", "LB41", "LE2");
        feed.Add(80, "ENDTSP", "LB41", "LE2");
        feed.Add(100, "TSPORD", "LB41", "LE3");
        feed.Add(130, "ENDTSP", "LB41", "LE3");
        feed.Add(60, "ENDTSP", "EA21", "LE9");   // anderer Punkt — muss draußen bleiben

        var r = ConveyorReport.Compute(feed.Rows, Fmt, "LB41", T0, T0.AddSeconds(200), Opts);

        Assert.Equal(3, r.Orders);
        Assert.Equal(3, r.Completed);
        Assert.Equal(1, r.FreeSignals);
        Assert.Equal(90, r.BusySeconds);            // 3 × 30 s
        Assert.Equal(45, r.BusyPercent);            // 90 von 200 s
        Assert.Equal(110, r.IdleSeconds);
        Assert.Equal(30, r.AvgTransportSeconds);
        Assert.Equal(20, r.AvgWaitSeconds);         // 50−30 und 100−80
        Assert.Equal(T0.AddSeconds(130), r.LatestAt);
    }

    [Fact]
    public void Ohne_Ereignisse_ist_der_Punkt_komplett_frei()
    {
        var r = ConveyorReport.Compute([], Fmt, "LB41", T0, T0.AddHours(1), Opts);

        Assert.Equal(0, r.Orders);
        Assert.Equal(0, r.BusyPercent);
        Assert.Equal(3600, r.IdleSeconds);
        Assert.Equal(0, r.AvgTransportSeconds);
        Assert.Null(r.LatestAt);
    }

    [Fact]
    public void Lueckenlose_Transporte_ergeben_keine_Wartezeit()
    {
        var feed = new Feed();
        // Der nächste Auftrag beginnt, sobald der vorige endet.
        for (var i = 0; i < 4; i++)
        {
            feed.Add(i * 50, "TSPORD", "LB41", $"LE{i}");
            feed.Add(i * 50 + 50, "ENDTSP", "LB41", $"LE{i}");
        }

        var r = ConveyorReport.Compute(feed.Rows, Fmt, "LB41", T0, T0.AddSeconds(200), Opts);

        Assert.Equal(0, r.AvgWaitSeconds);
        // Das Fenster ist rechts offen: das ENDTSP genau bei 200 s zählt erst im nächsten
        // Fenster, es bleiben drei abgeschlossene Transporte à 50 s.
        Assert.Equal(3, r.Completed);
        Assert.Equal(75, r.BusyPercent);
    }

    [Fact]
    public void Transport_von_vor_dem_Fenster_zaehlt_nur_anteilig()
    {
        var feed = new Feed();
        feed.Add(-100, "TSPORD", "LB41", "LE1");   // Auftrag lief schon vor dem Fenster
        feed.Add(199, "ENDTSP", "LB41", "LE1");

        var r = ConveyorReport.Compute(feed.Rows, Fmt, "LB41", T0, T0.AddSeconds(200), Opts);

        Assert.Equal(199, r.BusySeconds);   // nicht 299 — auf den Fensteranfang beschnitten
        Assert.Equal(99.5, r.BusyPercent);
        Assert.Equal(1, r.IdleSeconds);
    }

    [Fact]
    public void Verlauf_endet_am_rechten_Fensterrand_und_bleibt_in_Prozent()
    {
        var feed = new Feed();
        feed.Add(0, "TSPORD", "LB41", "LE1");
        feed.Add(60, "ENDTSP", "LB41", "LE1");

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
        feed.Add(0, "TSPORD", "MA72", "LE1");
        feed.Add(30, "ENDTSP", "MA72", "LE1");

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
