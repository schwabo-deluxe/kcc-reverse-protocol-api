using Kcc.Recorder;
using Xunit;

namespace Kcc.Recorder.Tests;

public class TelegramUtilizationTests
{
    static readonly DateTime Now = new(2026, 9, 2, 12, 0, 0, DateTimeKind.Unspecified);

    // TelegramType(2) SequenceNumber(2) Sender(4) Receiver(4) TelegramCount(2) ErrorCode(2)
    // MessageCode(6) Length(4) ResourcePoint(10) …
    // Die Anlage füllt Felder rechts mit Punkten auf.
    static string Data(string messageCode, string resourcePoint, string errorCode = "00",
        string destination = "WA01") =>
        "DM" + "01" + "MFC1" + "CS01" + "01" + errorCode +
        messageCode.PadRight(6, '.') + "0166" + resourcePoint.PadRight(10, '.') +
        // Letzter 33er-Block beginnt mit dem Endziel (4 oder 5 Zeichen), Rest Füllzeichen.
        (destination + new string('.', 33))[..33];

    static Telegram T(long id, int minutesAgo, string resourcePoint,
        string messageCode = "TSPORD", string errorCode = "00", string destination = "WA01") =>
        new(id, Now.AddMinutes(-minutesAgo), TelegramDirection.FromPlc, "L1",
            Data(messageCode, resourcePoint, errorCode, destination), null);

    static ResourcePointUtilization Point(TelegramUtilization u, string name) =>
        u.Points.Single(p => p.ResourcePoint == name);

    static ResourcePointConfig RP(string name, string? group = null, string? label = null) =>
        new() { Name = name, Group = group, Label = label };

    [Fact]
    public void Uph_und_Prozent_kommen_aus_dem_Rate_Fenster()
    {
        // 6 Telegramme in den letzten 5 min -> 6 / (5/60) = 72 UPH/h = 36 % von 200.
        // Die zwei älteren zählen für Count, nicht für UPH.
        var window = new[]
        {
            T(1, 1, "DA21"), T(2, 2, "DA21"), T(3, 2, "DA21"),
            T(4, 3, "DA21"), T(5, 4, "DA21"), T(6, 4, "DA21"),
            T(7, 20, "DA21"), T(8, 40, "DA21"),
        };

        var u = TelegramUtilization.Compute(
            window, TelegramFormat.Default, windowMinutes: 60, targetUph: 200, windowEnd: Now,
            rateMinutes: 5);

        var da21 = Point(u, "DA21");
        Assert.Equal(5, u.RateMinutes);
        Assert.Equal(8, da21.Count);
        Assert.Equal(6, da21.RateCount);
        Assert.Equal(72, da21.Uph);
        Assert.Equal(36, da21.Percent);
        Assert.Equal(8, u.TotalOrders);
        Assert.Equal(200, u.TargetUph);
    }

    [Fact]
    public void Zaehlt_nur_TSPORD_und_gelistete_Punkte()
    {
        var window = new[]
        {
            T(1, 10, "DA21"),
            T(2, 9, "DA21", messageCode: "TSSTAT"),  // anderer MessageCode
            T(3, 8, "XX99"),                         // nicht gelisteter Punkt
            T(4, 7, "LB41"),
        };

        var u = TelegramUtilization.Compute(
            window, TelegramFormat.Default, windowMinutes: 60, targetUph: 200, windowEnd: Now);

        Assert.Equal(3, u.TotalOrders);
        Assert.Equal(1, Point(u, "DA21").Count);
        Assert.Equal(1, Point(u, "LB41").Count);
        Assert.Equal(0, Point(u, "EA21").Count);
        Assert.DoesNotContain(u.Points, p => p.ResourcePoint == "XX99");
    }

    [Fact]
    public void Meldet_Fehler_und_juengsten_Zeitpunkt()
    {
        var window = new[]
        {
            T(1, 30, "EA21"),
            T(2, 20, "EA21", errorCode: "07"),
            T(3, 5, "EA21"),
        };

        var u = TelegramUtilization.Compute(
            window, TelegramFormat.Default, windowMinutes: 60, targetUph: 200, windowEnd: Now);

        var ea21 = Point(u, "EA21");
        Assert.Equal(1, ea21.Errors);
        Assert.Equal(Now.AddMinutes(-5), ea21.LatestAt);
    }

    [Fact]
    public void Rastert_den_Verlauf_in_Fuenf_Minuten_Eimer()
    {
        // Fenster 12:00–13:00, Raster 5 min: zwei Telegramme im Eimer 12:00, eines um 12:55.
        var window = new[]
        {
            T(1, 59, "DA21"),
            T(2, 57, "DA21"),
            T(3, 3, "DA21"),
        };

        var u = TelegramUtilization.Compute(
            window, TelegramFormat.Default, windowMinutes: 60, targetUph: 200, windowEnd: Now,
            bucketMinutes: 5);

        Assert.Equal(5, u.BucketMinutes);
        var series = Point(u, "DA21").Series;
        Assert.Equal(12, series.Count);
        Assert.Equal(Now.AddMinutes(-60), series[0].At);
        Assert.Equal(2, series[0].Count);
        Assert.Equal(24, series[0].Uph);   // 2 je 5 min = 24/h
        Assert.Equal(0, series[1].Count);
        Assert.Equal(1, series[11].Count);
        Assert.Equal(3, series.Sum(b => b.Count));
    }

    [Fact]
    public void Rastert_auch_bei_unteilbarem_Fenster_lueckenlos()
    {
        var u = TelegramUtilization.Compute(
            [T(1, 1, "DA21")], TelegramFormat.Default, windowMinutes: 7, targetUph: 200, windowEnd: Now,
            bucketMinutes: 5);

        var series = Point(u, "DA21").Series;
        Assert.Equal(2, series.Count);
        Assert.Equal(1, series.Sum(b => b.Count));
    }

    [Fact]
    public void Nimmt_eigene_Punktliste_und_leeres_Fenster()
    {
        var u = TelegramUtilization.Compute(
            [], TelegramFormat.Default, windowMinutes: 60, targetUph: 100, windowEnd: Now,
            resourcePoints: [RP("ME71")]);

        var me71 = Assert.Single(u.Points);
        Assert.Equal("ME71", me71.ResourcePoint);
        Assert.Equal("ME71", me71.Label);
        Assert.Equal("Ohne Gruppe", me71.Group);
        Assert.Equal(0, me71.Count);
        Assert.Equal(0, me71.RateCount);
        Assert.Equal(0, me71.Uph);
        Assert.Null(me71.LatestAt);
        Assert.Empty(me71.Destinations);
    }

    [Fact]
    public void Schluesselt_Endziele_je_Ressourcenpunkt_auf()
    {
        var window = new[]
        {
            T(1, 10, "DA21", destination: "WA01"),
            T(2, 9, "DA21", destination: "WA01"),
            T(3, 8, "DA21", destination: "WA01"),
            T(4, 7, "DA21", destination: "DA07"),
        };

        var u = TelegramUtilization.Compute(
            window, TelegramFormat.Default, windowMinutes: 60, targetUph: 200, windowEnd: Now);

        var d = Point(u, "DA21").Destinations;
        Assert.Equal(["WA01", "DA07"], d.Select(x => x.Target));
        Assert.Equal(3, d[0].Count);
        Assert.Equal(75, d[0].Percent);
        Assert.Equal(25, d[1].Percent);
        Assert.Equal("WA01", d[0].Label);  // ohne Mapping bleibt das Ziel roh
    }

    [Fact]
    public void Erkennt_Endziele_mit_vier_oder_fuenf_Zeichen()
    {
        var window = new[]
        {
            T(1, 10, "DA21", destination: "DLL13"),
            T(2, 9, "DA21", destination: "DLL13"),
            T(3, 8, "DA21", destination: "GA51"),
        };

        var u = TelegramUtilization.Compute(
            window, TelegramFormat.Default, windowMinutes: 60, targetUph: 200, windowEnd: Now);

        var d = Point(u, "DA21").Destinations;
        Assert.Equal(["DLL13", "GA51"], d.Select(x => x.Target));
        Assert.Equal(2, d[0].Count);
    }

    [Fact]
    public void Setzt_Klartext_aus_dem_Ziel_Mapping()
    {
        var u = TelegramUtilization.Compute(
            [T(1, 5, "DA21", destination: "GA51"), T(2, 4, "DA21", destination: "XX99")],
            TelegramFormat.Default, windowMinutes: 60, targetUph: 200, windowEnd: Now,
            destinationLabels: new Dictionary<string, string> { ["GA51"] = "Kommissionierung" });

        var d = Point(u, "DA21").Destinations;
        Assert.Equal("GA51 (Kommissionierung)", d.Single(x => x.Target == "GA51").Label);
        Assert.Equal("XX99", d.Single(x => x.Target == "XX99").Label);
    }

    [Fact]
    public void Fasst_Punkte_zu_Gruppen_zusammen()
    {
        var window = new[]
        {
            T(1, 1, "MA72"), T(2, 3, "MA72"), T(3, 4, "MB72"),  // Auslagerung RBG: 3 in den letzten 5 min
            T(4, 2, "DA21"),                                     // Fördertechnik: 1
            T(5, 30, "MA72"),                                    // alt: nur Count, nicht UPH
        };

        var u = TelegramUtilization.Compute(
            window, TelegramFormat.Default, windowMinutes: 60, targetUph: 200, windowEnd: Now,
            rateMinutes: 5,
            resourcePoints:
            [
                RP("MA72", "Auslagerung RBG", "RBG A"),
                RP("MB72", "Auslagerung RBG", "RBG B"),
                RP("DA21", "Fördertechnik"),
            ]);

        Assert.Equal(["Auslagerung RBG", "Fördertechnik"], u.Groups.Select(g => g.Name));
        Assert.Equal("RBG A", Point(u, "MA72").Label);
        Assert.Equal("Auslagerung RBG", Point(u, "MA72").Group);

        var rbg = u.Groups.Single(g => g.Name == "Auslagerung RBG");
        Assert.Equal(["MA72", "MB72"], rbg.Points);
        Assert.Equal(4, rbg.Count);               // inkl. altes MA72
        Assert.Equal(3, rbg.RateCount);
        Assert.Equal(36, rbg.Uph);                // 3 / (5/60)
        Assert.Equal(9, rbg.Percent);            // Math.Round(36 / (200*2) * 100, 1)
        Assert.Equal(1, u.Groups.Single(g => g.Name == "Fördertechnik").Count);
    }

    [Fact]
    public void Doppelte_Namen_werden_zusammengefuehrt()
    {
        var u = TelegramUtilization.Compute(
            [T(1, 5, "MA72")], TelegramFormat.Default, windowMinutes: 60, targetUph: 200, windowEnd: Now,
            resourcePoints: [RP("MA72", "A"), RP("MA72", "B")]);

        Assert.Single(u.Points);
        Assert.Equal("A", Point(u, "MA72").Group);
    }
}
