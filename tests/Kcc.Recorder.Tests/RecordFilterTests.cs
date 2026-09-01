using Kcc.Recorder;
using Xunit;

namespace Kcc.Recorder.Tests;

public class RecordFilterTests
{
    static Telegram Telegram(string? data, string? connection = "PLC1",
        TelegramDirection direction = TelegramDirection.FromPlc) =>
        new(1, DateTime.UtcNow, direction, connection, data, "fmt");

    [Fact]
    public void Standard_nimmt_Telegramme_mit_Inhalt()
    {
        var filter = new RecordFilter(new FilterConfig());

        Assert.True(filter.ShouldRecord(Telegram("1234ABCD")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Standard_verwirft_leere_Daten(string? data)
    {
        var filter = new RecordFilter(new FilterConfig());

        Assert.False(filter.ShouldRecord(Telegram(data)));
    }

    [Fact]
    public void Standard_verwirft_reine_Nulltelegramme()
    {
        var filter = new RecordFilter(new FilterConfig());

        Assert.False(filter.ShouldRecord(Telegram("0000 0000 0000")));
    }

    [Fact]
    public void Nulltelegramme_koennen_zugelassen_werden()
    {
        var filter = new RecordFilter(new FilterConfig { IgnoreAllZeroData = false });

        Assert.True(filter.ShouldRecord(Telegram("0000")));
    }

    [Fact]
    public void MinDataLength_greift()
    {
        var filter = new RecordFilter(new FilterConfig { MinDataLength = 5 });

        Assert.False(filter.ShouldRecord(Telegram("1234")));
        Assert.True(filter.ShouldRecord(Telegram("12345")));
    }

    [Fact]
    public void Whitelist_beschraenkt_auf_genannte_Verbindungen()
    {
        var filter = new RecordFilter(new FilterConfig { ConnectionWhitelist = ["PLC1"] });

        Assert.True(filter.ShouldRecord(Telegram("abc", "PLC1")));
        Assert.False(filter.ShouldRecord(Telegram("abc", "PLC2")));
    }

    [Fact]
    public void Blacklist_schliesst_Verbindungen_aus()
    {
        var filter = new RecordFilter(new FilterConfig { ConnectionBlacklist = ["PLC2"] });

        Assert.True(filter.ShouldRecord(Telegram("abc", "PLC1")));
        Assert.False(filter.ShouldRecord(Telegram("abc", "PLC2")));
    }

    [Fact]
    public void Richtung_kann_eingeschraenkt_werden()
    {
        var filter = new RecordFilter(new FilterConfig { Directions = [TelegramDirection.ToPlc] });

        Assert.True(filter.ShouldRecord(Telegram("abc", direction: TelegramDirection.ToPlc)));
        Assert.False(filter.ShouldRecord(Telegram("abc", direction: TelegramDirection.FromPlc)));
    }

    [Fact]
    public void Regeln_ueber_regulaere_Ausdruecke_greifen()
    {
        var filter = new RecordFilter(new FilterConfig
        {
            DataMatchRegex = "^TEL",
            DataIgnoreRegex = "HEARTBEAT",
        });

        Assert.True(filter.ShouldRecord(Telegram("TEL0001")));
        Assert.False(filter.ShouldRecord(Telegram("XYZ0001")));
        Assert.False(filter.ShouldRecord(Telegram("TEL_HEARTBEAT")));
    }

    [Fact]
    public void Unbrauchbarer_regulaerer_Ausdruck_wird_klar_gemeldet()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new RecordFilter(new FilterConfig { DataMatchRegex = "([" }));

        Assert.Contains("DataMatchRegex", ex.Message);
    }

    [Fact]
    public void Serverfilter_schliesst_leere_Daten_aus()
    {
        var filters = new RecordFilter(new FilterConfig()).ServerSideFilters();

        Assert.Equal(2, filters.Count);
        Assert.All(filters, f => Assert.Equal("Data", f.FilterField));
        Assert.Contains(filters, f => f.FilterType == FilterType.IsNotNull);
    }

    [Fact]
    public void Serverfilter_kann_abgeschaltet_werden()
    {
        var filters = new RecordFilter(new FilterConfig { FilterEmptyDataOnServer = false })
            .ServerSideFilters();

        Assert.Empty(filters);
    }
}
