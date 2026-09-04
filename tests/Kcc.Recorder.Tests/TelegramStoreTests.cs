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
    public void MaxTelegramTime_liefert_den_juengsten_Zeitstempel_ohne_Zeitzone()
    {
        using var store = new TelegramStore(_path);
        Assert.Null(store.MaxTelegramTime());

        // Bewusst mit UTC-Kind gespeichert — Store legt zeitzonenfrei ab.
        store.Insert([
            new Telegram(1, new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc),
                TelegramDirection.FromPlc, "PLC1", "a", null),
            new Telegram(2, new DateTime(2026, 9, 2, 9, 30, 0, DateTimeKind.Utc),
                TelegramDirection.FromPlc, "PLC1", "b", null),
        ]);

        var max = store.MaxTelegramTime();
        Assert.Equal(new DateTime(2026, 9, 2, 9, 30, 0), max);
        Assert.Equal(DateTimeKind.Unspecified, max!.Value.Kind);
    }

    [Fact]
    public void SecondsSinceLastWrite_misst_ab_dem_letzten_Insert()
    {
        using var store = new TelegramStore(_path);
        Assert.Null(store.SecondsSinceLastWrite());

        store.Insert([Telegram(1)]);

        var seconds = store.SecondsSinceLastWrite();
        Assert.NotNull(seconds);
        Assert.InRange(seconds!.Value, 0, 30);
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

    [Fact]
    public void Uph_Historie_wird_ab_Stichtag_ersetzt_und_zeitraumweise_gelesen()
    {
        using var store = new TelegramStore(_path);
        var t0 = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Unspecified);

        UphSampleRow Row(int min, string rp, string dest, int orders) =>
            new() { Bucket = t0.AddMinutes(min), ResourcePoint = rp, Destination = dest, Orders = orders };

        store.ReplaceUphSamplesFrom(t0, [Row(0, "MA72", "WA01", 5), Row(15, "MA72", "WA01", 7)]);
        Assert.Equal(2, store.UphSampleCount());
        Assert.Equal(t0.AddMinutes(15), store.MaxUphBucket());

        // Erneut ab Minute 15 verdichten — die frühere Zeile bei 0 bleibt, die bei 15 wird ersetzt.
        store.ReplaceUphSamplesFrom(t0.AddMinutes(15), [Row(15, "MA72", "WA01", 9), Row(30, "MA72", "GA51", 3)]);

        var rows = store.ReadUphSamples(t0, t0.AddMinutes(60));
        Assert.Equal([5, 9, 3], rows.Select(r => r.Orders));
        Assert.Equal(DateTimeKind.Unspecified, rows[0].Bucket.Kind);

        Assert.Equal(1, store.DeleteUphSamplesOlderThan(t0.AddMinutes(15)));
        Assert.Equal(2, store.UphSampleCount());
    }

    public void Dispose()
    {
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
            if (File.Exists(file))
                File.Delete(file);
    }
}
