using Kcc.Recorder;
using Xunit;

namespace Kcc.Recorder.Tests;

public class PointHistoryReportTests
{
    static readonly DateTime Day = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Unspecified);

    static PointSampleRow Row(DateTime bucket, string rp, double busy, int orders) =>
        new() { Bucket = bucket, ResourcePoint = rp, BusySeconds = busy, Orders = orders };

    [Fact]
    public void Belegungsgrad_ist_belegte_Zeit_durch_Rasterdauer()
    {
        var rows = new[]
        {
            Row(Day.AddHours(8), "EA21", busy: 300, orders: 4),   // 300 s von 600 s = 50 %
            Row(Day.AddHours(8).AddMinutes(10), "EA21", busy: 600, orders: 2),  // voll belegt = 100 %
        };

        var r = PointHistoryReport.Compute(
            rows, Day.AddHours(8), Day.AddHours(8).AddMinutes(20), bucketMinutes: 10,
            resourcePoints: [new ResourcePointConfig { Name = "EA21", TargetUph = 60 }]);

        Assert.Equal(2, r.Buckets.Count);
        Assert.Equal(50, r.Buckets[0].BusyPercent["EA21"]);
        Assert.Equal(100, r.Buckets[1].BusyPercent["EA21"]);
        // Leistung: 4 Aufträge in 10 min = 24/h gegen Richtwert 60 = 40 %.
        Assert.Equal(40, r.Buckets[0].LoadPercent["EA21"]);
    }

    [Fact]
    public void Verdichtet_feine_Zeilen_auf_das_Anzeigeraster()
    {
        var rows = new[]
        {
            Row(Day.AddHours(8), "EA21", 150, 1),
            Row(Day.AddHours(8).AddMinutes(5), "EA21", 150, 1),
            Row(Day.AddHours(8).AddMinutes(10), "EA21", 300, 2),
        };

        // 15-min-Anzeigeraster über drei 5-min-Zeilen: 600 s belegt von 900 s = 66,7 %.
        var r = PointHistoryReport.Compute(
            rows, Day.AddHours(8), Day.AddHours(8).AddMinutes(15), bucketMinutes: 15,
            resourcePoints: [new ResourcePointConfig { Name = "EA21" }]);

        Assert.Single(r.Buckets);
        Assert.Equal(66.7, r.Buckets[0].BusyPercent["EA21"]);
        Assert.Equal(4, r.Buckets[0].Orders["EA21"]);
    }

    [Fact]
    public void Filtert_auf_einen_Ressourcenpunkt()
    {
        var rows = new[]
        {
            Row(Day.AddHours(8), "EA21", 300, 2),
            Row(Day.AddHours(8), "LB41", 600, 5),
        };

        var r = PointHistoryReport.Compute(
            rows, Day.AddHours(8), Day.AddHours(9), bucketMinutes: 60,
            resourcePoints: [new ResourcePointConfig { Name = "EA21" }, new ResourcePointConfig { Name = "LB41" }],
            resourcePoint: "EA21");

        Assert.Equal(["EA21"], r.ResourcePoints);
        Assert.Equal("EA21", r.Totals.Single().ResourcePoint);
    }
}
