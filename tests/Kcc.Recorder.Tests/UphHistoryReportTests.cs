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

        Assert.Equal(4, report.Buckets.Count);
        Assert.Equal(30, report.Buckets[0].Total);
        Assert.Equal(120, report.Buckets[0].Uph);         // 30 / (15/60)
        Assert.Equal(120, report.Buckets[0].Uph2["WA01"]);
        Assert.Equal(0, report.Buckets[1].Total);
        Assert.Equal(60, report.Buckets[2].Uph);          // 15 / (15/60)
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
        Assert.Equal(30, report.Buckets[0].Uph);          // 30 / (60/60)
    }

    [Fact]
    public void Sortiert_Ziele_nach_Menge_und_rechnet_Anteil_und_Klartext()
    {
        var report = UphHistoryReport.Compute(
            [Row(0, "MA72", "WA01", 10), Row(0, "MA72", "GA51", 30), Row(20, "MA72", "DLL*", 10)],
            T0, T0.AddMinutes(60), bucketMinutes: 15,
            destinationLabels: new Dictionary<string, string>
            {
                ["GA51"] = "Kommissionierung",
                ["DLL*"] = "Auslagerung DLL",
            });

        Assert.Equal(["GA51", "DLL*", "WA01"], report.Destinations);   // Gleichstand: nach Name
        var ga51 = report.Totals[0];
        Assert.Equal("GA51 (Kommissionierung)", ga51.Label);
        Assert.Equal(30, ga51.Orders);
        Assert.Equal(60, ga51.Share);                     // 30 von 50
        Assert.Equal(30, ga51.AvgUph);                    // 30 / 1 h Fenster
        Assert.Equal("DLL* (Auslagerung DLL)", report.Totals[1].Label);
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
    public void Leeres_Fenster_liefert_ein_Raster_ohne_Ziele()
    {
        var report = UphHistoryReport.Compute([], T0, T0.AddMinutes(60), bucketMinutes: 15);

        Assert.Equal(4, report.Buckets.Count);
        Assert.All(report.Buckets, b => Assert.Equal(0, b.Total));
        Assert.Empty(report.Totals);
        Assert.Equal(0, report.TotalOrders);
    }
}
