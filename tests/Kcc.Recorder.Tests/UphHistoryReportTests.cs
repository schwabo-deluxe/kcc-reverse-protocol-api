using Kcc.Recorder;
using Xunit;

namespace Kcc.Recorder.Tests;

public class UphHistoryReportTests
{
    static readonly DateTime T0 = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Unspecified);

    static UphSampleRow Row(int minutesAfterT0, string rp, string dest, int orders) =>
        new() { Bucket = T0.AddMinutes(minutesAfterT0), ResourcePoint = rp, Destination = dest, Orders = orders };

    [Fact]
    public void Rechnet_Mengen_je_Raster_in_UPH_um()
    {
        var report = UphHistoryReport.Compute(
            [Row(0, "MA72", "WA01", 30), Row(30, "MA72", "WA01", 15)],
            T0, T0.AddMinutes(60), bucketMinutes: 15);

        Assert.Equal("destination", report.GroupBy);
        Assert.Equal(4, report.Buckets.Count);
        Assert.Equal(30, report.Buckets[0].Total);
        Assert.Equal(120, report.Buckets[0].Uph);          // 30 / (15/60)
        Assert.Equal(120, report.Buckets[0].Series["WA01"]);
        Assert.Equal(0, report.Buckets[1].Total);
        Assert.Equal(60, report.Buckets[2].Uph);           // 15 / (15/60)
        Assert.Equal(45, report.TotalOrders);
    }

    [Fact]
    public void Fasst_auf_ein_groeberes_Anzeigeraster_zusammen()
    {
        var report = UphHistoryReport.Compute(
            [Row(0, "MA72", "WA01", 10), Row(15, "MA72", "WA01", 10), Row(45, "MA72", "WA01", 10)],
            T0, T0.AddMinutes(60), bucketMinutes: 60);

        Assert.Single(report.Buckets);
        Assert.Equal(30, report.Buckets[0].Total);
        Assert.Equal(30, report.Buckets[0].Uph);           // 30 / (60/60)
    }

    [Fact]
    public void Sortiert_Reihen_nach_Menge_und_rechnet_Anteil_und_Klartext()
    {
        var report = UphHistoryReport.Compute(
            [Row(0, "MA72", "WA01", 10), Row(0, "MA72", "GA51", 30), Row(20, "MA72", "DLL*", 10)],
            T0, T0.AddMinutes(60), bucketMinutes: 15,
            destinationLabels: new Dictionary<string, string>
            {
                ["GA51"] = "Kommissionierung",
                ["DLL*"] = "Auslagerung DLL",
            });

        Assert.Equal(["GA51", "DLL*", "WA01"], report.Keys);   // Gleichstand: nach Name
        var ga51 = report.Totals[0];
        Assert.Equal("GA51 (Kommissionierung)", ga51.Label);
        Assert.Equal(30, ga51.Orders);
        Assert.Equal(60, ga51.Share);                          // 30 von 50
        Assert.Equal(30, ga51.AvgUph);                         // 30 / 1 h Fenster
        Assert.Equal("DLL* (Auslagerung DLL)", report.Totals[1].Label);
    }

    [Fact]
    public void Stapelt_nach_Ressourcenpunkt()
    {
        var report = UphHistoryReport.Compute(
            [Row(0, "MA72", "WA01", 10), Row(0, "MB72", "WA01", 30), Row(15, "MA72", "GA51", 10)],
            T0, T0.AddMinutes(60), bucketMinutes: 15,
            groupBy: UphHistoryGroupBy.ResourcePoint,
            resourcePoints: [new ResourcePointConfig { Name = "MB72", Label = "RBG B" }]);

        Assert.Equal("resourcePoint", report.GroupBy);
        Assert.Equal(["MB72", "MA72"], report.Keys);           // MB72 30 vor MA72 20
        Assert.Equal("RBG B", report.Totals[0].Label);         // Klartext aus ResourcePoints
        Assert.Equal("MA72", report.Totals[1].Label);          // ohne Eintrag: roh
        Assert.Equal(40, report.Buckets[0].Series["MA72"]);     // 10 / (15/60)
        Assert.Equal(120, report.Buckets[0].Series["MB72"]);
    }

    [Fact]
    public void Grenzt_auf_einen_Ressourcenpunkt_ein()
    {
        var report = UphHistoryReport.Compute(
            [Row(0, "MA72", "WA01", 10), Row(0, "DA21", "WA01", 40)],
            T0, T0.AddMinutes(60), bucketMinutes: 15, resourcePoint: "MA72");

        Assert.Equal(["MA72"], report.ResourcePoints);
        Assert.Equal(10, report.TotalOrders);
    }

    [Fact]
    public void Gleitendes_Fenster_endet_beim_aktuellen_Wert()
    {
        // Fenster 60 min, gleitend 5 min, Schritt 1 -> 60 Stützpunkte; letzter endet bei 'to'.
        // Ein Auftrag vor 30 min taucht in genau 5 aufeinanderfolgenden Punkten auf (72 UPH).
        var report = UphHistoryReport.Compute(
            [Row(30, "MA72", "WA01", 1),
             Row(58, "MA72", "GA51", 1), Row(59, "MA72", "GA51", 1)],   // 2 in den letzten 5 min
            T0, T0.AddMinutes(60), bucketMinutes: 1, rollingWindowMinutes: 5);

        Assert.Equal(5, report.RollingMinutes);
        Assert.Equal(60, report.Buckets.Count);
        Assert.Equal(T0.AddMinutes(60), report.Buckets[^1].At);

        var wa01Points = report.Buckets.Count(b => (b.Series.GetValueOrDefault("WA01")) > 0);
        Assert.Equal(5, wa01Points);
        Assert.Equal(12, report.Buckets.First(b => b.Series.ContainsKey("WA01")).Series["WA01"]);  // 1 / (5/60)

        var last = report.Buckets[^1];
        Assert.Equal(24, last.Series["GA51"]);   // 2 / (5/60)
        Assert.Equal(3, report.TotalOrders);     // Summen bleiben absolut
    }

    [Fact]
    public void FromTelegrams_macht_je_TSPORD_eine_Zeile_mit_exaktem_Zeitstempel()
    {
        var fmt = TelegramFormat.Default;
        int Off(string n) { var p = 0; foreach (var f in fmt.Fields) { if (f.Name == n) return p; p += f.Length; } return -1; }
        string Data(string rp, string mc, string dest)
        {
            var buf = new char[fmt.Length];
            Array.Fill(buf, '.');
            void Put(string field, string v) { var a = Off(field); for (var i = 0; i < v.Length; i++) buf[a + i] = v[i]; }
            Put("MessageCode", mc);
            Put("ResourcePoint", rp);
            Put("Reserve", dest);   // Endziel steht am Anfang des letzten 33er-Blocks
            return new string(buf);
        }
        Telegram T(long id, int min, string rp, string mc, string dest) =>
            new(id, T0.AddMinutes(min), TelegramDirection.FromPlc, "L1", Data(rp, mc, dest), null);

        var rows = UphHistoryReport.FromTelegrams(
            [T(1, 1, "MA72", "TSPORD", "DLL13"),
             T(2, 2, "MA72", "TSSTAT", "WA01"),   // kein TSPORD
             T(3, 3, "MA72", "TSPORD", "WA01")],
            fmt, new Dictionary<string, string> { ["DLL*"] = "Auslagerung DLL" });

        Assert.Equal(2, rows.Count);
        Assert.Equal(T0.AddMinutes(1), rows[0].Bucket);
        Assert.Equal("DLL*", rows[0].Destination);   // Muster zusammengefasst
        Assert.Equal(1, rows[0].Orders);
        Assert.Equal("WA01", rows[1].Destination);
    }

    [Fact]
    public void Leeres_Fenster_liefert_ein_Raster_ohne_Reihen()
    {
        var report = UphHistoryReport.Compute([], T0, T0.AddMinutes(60), bucketMinutes: 15);

        Assert.Equal(4, report.Buckets.Count);
        Assert.All(report.Buckets, b => Assert.Equal(0, b.Total));
        Assert.Empty(report.Totals);
        Assert.Equal(0, report.TotalOrders);
    }
}
