using Kcc.Recorder;
using Xunit;

namespace Kcc.Recorder.Tests;

public class TelegramStoreTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"kcc-test-{Guid.NewGuid():N}.db");

    static Telegram Telegram(long id) =>
        new(id, new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc).AddSeconds(id),
            TelegramDirection.FromPlc, "PLC1", $"data-{id}", "fmt");

    [Fact]
    public void Schreibt_und_liest_Telegramme()
    {
        using var store = new TelegramStore(_path);

        var written = store.Insert([Telegram(1), Telegram(2)]);

        Assert.Equal(2, written);
        Assert.Equal(2, store.Count());
        var rows = store.Read(null, null).ToList();
        Assert.Equal([1L, 2L], rows.Select(r => r.Id));
        Assert.Equal("data-1", rows[0].Data);
    }

    [Fact]
    public void Doppelte_Ids_werden_uebersprungen()
    {
        using var store = new TelegramStore(_path);
        store.Insert([Telegram(1)]);

        var written = store.Insert([Telegram(1), Telegram(2)]);

        Assert.Equal(1, written);
        Assert.Equal(2, store.Count());
    }

    [Fact]
    public void Merkt_sich_die_zuletzt_gesehene_Id_ueber_Neustarts()
    {
        using (var store = new TelegramStore(_path))
        {
            Assert.Null(store.GetLastSeenId());
            store.SetLastSeenId(4711);
        }

        using var reopened = new TelegramStore(_path);
        Assert.Equal(4711, reopened.GetLastSeenId());
    }

    [Fact]
    public void DeleteOlderThan_entfernt_nur_Zeilen_vor_dem_Stichtag()
    {
        using var store = new TelegramStore(_path);
        var alt = new Telegram(1, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
            TelegramDirection.FromPlc, "PLC1", "alt", null);
        var neu = new Telegram(2, DateTime.Now,
            TelegramDirection.FromPlc, "PLC1", "neu", null);
        store.Insert([alt, neu]);

        var removed = store.DeleteOlderThan(new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Unspecified));

        Assert.Equal(1, removed);
        Assert.Equal([2L], store.Read(null, null).Select(r => r.Id));
    }

    [Fact]
    public void RetentionCutoff_liegt_RetentionDays_in_der_Vergangenheit_und_ohne_Zeitzone()
    {
        var cutoff = TelegramStore.RetentionCutoff(365);

        Assert.Equal(DateTimeKind.Unspecified, cutoff.Kind);
        Assert.InRange((DateTime.Now - cutoff).TotalDays, 364.9, 365.1);
    }

    [Fact]
    public void Liest_einen_Zeitraum()
    {
        using var store = new TelegramStore(_path);
        store.Insert([Telegram(1), Telegram(2), Telegram(3)]);

        var rows = store.Read(Telegram(2).DateTime, Telegram(2).DateTime).ToList();

        Assert.Single(rows);
        Assert.Equal(2, rows[0].Id);
    }

    public void Dispose()
    {
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
            if (File.Exists(file))
                File.Delete(file);
    }
}
