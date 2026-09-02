using Kcc.Recorder;
using Xunit;

namespace Kcc.Recorder.Tests;

public class TelegramKpisTests
{
    static readonly DateTime Now = new(2026, 9, 2, 12, 0, 0, DateTimeKind.Unspecified);

    // TelegramType(2) SequenceNumber(2) Sender(4) Receiver(4) TelegramCount(2) ErrorCode(2) MessageCode(6) …
    static string Data(string errorCode, string messageCode) =>
        "DM" + "01" + "MFC1" + "CS01" + "01" + errorCode + messageCode.PadRight(6);

    static Telegram T(long id, int minutesAgo, string connection, TelegramDirection dir,
        string errorCode = "00", string messageCode = "TSPORD") =>
        new(id, Now.AddMinutes(-minutesAgo), dir, connection, Data(errorCode, messageCode), null);

    [Fact]
    public void Zaehlt_Fenster_Rate_und_Lag()
    {
        var window = new[]
        {
            T(1, 50, "L1", TelegramDirection.FromPlc),
            T(2, 20, "L2", TelegramDirection.ToPlc),
            T(3, 4, "L1", TelegramDirection.FromPlc),
        };

        var k = TelegramKpis.Compute(window, TelegramFormat.Default, windowMinutes: 60, now: Now);

        Assert.Equal(60, k.WindowMinutes);
        Assert.Equal(3, k.Count);
        Assert.Equal(0.05, k.PerMinute);
        Assert.Equal(3, k.LatestId);
        Assert.Equal(Now.AddMinutes(-4), k.LatestAt);
        Assert.Equal(240, k.LagSeconds);
        Assert.Equal(2, k.DistinctConnections);
    }

    [Fact]
    public void Gruppiert_nach_Richtung_Verbindung_und_MessageCode()
    {
        var window = new[]
        {
            T(1, 10, "L1", TelegramDirection.FromPlc, messageCode: "TSPORD"),
            T(2, 9, "L1", TelegramDirection.FromPlc, messageCode: "RPFREE"),
            T(3, 8, "L2", TelegramDirection.ToPlc, messageCode: "TSPORD"),
        };

        var k = TelegramKpis.Compute(window, TelegramFormat.Default, 60, Now);

        Assert.Equal(2, k.ByDirection["FromPlc"]);
        Assert.Equal(1, k.ByDirection["ToPlc"]);
        Assert.Equal(2, k.ByConnection["L1"]);
        Assert.Equal(2, k.ByMessageCode["TSPORD"]);
        Assert.Equal(1, k.ByMessageCode["RPFREE"]);
    }

    [Fact]
    public void Zaehlt_nur_Telegramme_mit_echtem_Fehlercode()
    {
        var window = new[]
        {
            T(1, 5, "L1", TelegramDirection.FromPlc, errorCode: "00"),
            T(2, 4, "L1", TelegramDirection.FromPlc, errorCode: "  "),
            T(3, 3, "L1", TelegramDirection.FromPlc, errorCode: "E7"),
        };

        var k = TelegramKpis.Compute(window, TelegramFormat.Default, 60, Now);

        Assert.Equal(1, k.Errors);
    }

    [Fact]
    public void Leeres_Fenster_liefert_Nullen()
    {
        var k = TelegramKpis.Compute([], TelegramFormat.Default, 60, Now);

        Assert.Equal(0, k.Count);
        Assert.Null(k.LatestId);
        Assert.Null(k.LagSeconds);
        Assert.Empty(k.ByDirection);
        Assert.Empty(k.ByMessageCode);
    }
}
