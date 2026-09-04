using Kcc.Recorder;
using Xunit;

namespace Kcc.Recorder.Tests;

public class UphHistorySamplerTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"kcc-uph-{Guid.NewGuid():N}.db");

    static string Data(string resourcePoint, string destination, string messageCode = "TSPORD") =>
        "DM" + "01" + "MFC1" + "CS01" + "01" + "00" +
        messageCode.PadRight(6, '.') + "0166" + resourcePoint.PadRight(10, '.') +
        (destination + new string('.', 33))[..33];

    static Telegram T(long id, DateTime at, string rp, string dest, string mc = "TSPORD") =>
        new(id, at, TelegramDirection.FromPlc, "L1", Data(rp, dest, mc), null);

    UphHistorySampler Sampler(TelegramStore store, IReadOnlyDictionary<string, string>? labels = null) =>
        new(store, TelegramFormat.Default,
            [new ResourcePointConfig { Name = "MA72" }],
            labels, intervalMinutes: 15, retentionDays: 28, _ => { });

    [Fact]
    public void Verdichtet_nur_abgeschlossene_Raster_gelisteter_Punkte()
    {
        using var store = new TelegramStore(_path);
        var day = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Unspecified);
        store.Insert(
        [
            T(1, day.AddHours(8).AddMinutes(5), "MA72", "WA01"),
            T(2, day.AddHours(8).AddMinutes(10), "MA72", "WA01"),
            T(3, day.AddHours(8).AddMinutes(20), "MA72", "GA51"),
            T(4, day.AddHours(8).AddMinutes(22), "ZZ99", "WA01"),          // nicht gelistet
            T(5, day.AddHours(8).AddMinutes(24), "MA72", "WA01", "TSSTAT"), // anderer MessageCode
            T(6, day.AddHours(9).AddMinutes(3), "MA72", "WA01"),           // laufendes Raster (unvollständig)
        ]);

        Sampler(store).SampleNow();

        var rows = store.ReadUphSamples(day, day.AddDays(1));
        Assert.Equal(2, rows.Count);
        Assert.Equal((day.AddHours(8), "MA72", "WA01", 2),
            (rows[0].Bucket, rows[0].ResourcePoint, rows[0].Destination, rows[0].Orders));
        Assert.Equal((day.AddHours(8).AddMinutes(15), "MA72", "GA51", 1),
            (rows[1].Bucket, rows[1].ResourcePoint, rows[1].Destination, rows[1].Orders));
    }

    [Fact]
    public void Rechnet_beim_naechsten_Lauf_ab_dem_zuletzt_verdichteten_Raster_weiter()
    {
        using var store = new TelegramStore(_path);
        var day = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Unspecified);
        store.Insert(
        [
            T(1, day.AddHours(8).AddMinutes(5), "MA72", "WA01"),
            T(2, day.AddHours(8).AddMinutes(20), "MA72", "GA51"),
            T(3, day.AddHours(9).AddMinutes(3), "MA72", "WA01"),
        ]);
        Sampler(store).SampleNow();
        Assert.Equal(day.AddHours(8).AddMinutes(15), store.MaxUphBucket());

        // Neue Telegramme schieben den rechten Rand weiter — jetzt wird auch 09:00 abgeschlossen.
        store.Insert([T(4, day.AddHours(9).AddMinutes(25), "MA72", "WA01")]);
        Sampler(store).SampleNow();

        var rows = store.ReadUphSamples(day, day.AddDays(1));
        Assert.Equal(1, rows.Single(r => r.Bucket == day.AddHours(8)).Orders);       // unverändert
        Assert.Equal(1, rows.Single(r => r.Bucket == day.AddHours(9)).Orders);       // neu
    }

    [Fact]
    public void Fasst_Zielmuster_beim_Verdichten_zusammen()
    {
        using var store = new TelegramStore(_path);
        var day = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Unspecified);
        store.Insert(
        [
            T(1, day.AddHours(8).AddMinutes(2), "MA72", "DLL13"),
            T(2, day.AddHours(8).AddMinutes(4), "MA72", "DLL07"),
            T(3, day.AddHours(9).AddMinutes(1), "MA72", "WA01"),
        ]);

        Sampler(store, new Dictionary<string, string> { ["DLL*"] = "Auslagerung DLL" }).SampleNow();

        var rows = store.ReadUphSamples(day, day.AddDays(1));
        Assert.Equal(("MA72", "DLL*", 2), (rows[0].ResourcePoint, rows[0].Destination, rows[0].Orders));
    }

    public void Dispose()
    {
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
            if (File.Exists(file))
                File.Delete(file);
    }
}
