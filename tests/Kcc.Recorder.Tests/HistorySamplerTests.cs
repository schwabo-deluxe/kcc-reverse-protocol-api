using Kcc.Recorder;
using Xunit;

namespace Kcc.Recorder.Tests;

public class HistorySamplerTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"kcc-uph-{Guid.NewGuid():N}.db");

    static string Data(string resourcePoint, string destination, string messageCode = "TSPORD") =>
        "DM" + "01" + "MFC1" + "CS01" + "01" + "00" +
        messageCode.PadRight(6, '.') + "0166" + resourcePoint.PadRight(10, '.') +
        (destination + new string('.', 33))[..33];

    static Telegram T(long id, DateTime at, string rp, string dest, string mc = "TSPORD") =>
        new(id, at, TelegramDirection.FromPlc, "L1", Data(rp, dest, mc), null);

    HistorySampler Sampler(TelegramStore store, IReadOnlyDictionary<string, string>? labels = null) =>
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
    public void Rebuild_holt_nachtraeglich_eingespielte_aeltere_Telegramme_dazu()
    {
        using var store = new TelegramStore(_path);
        var day = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Unspecified);

        // Erst „neue" Telegramme, verdichten.
        store.Insert([T(10, day.AddHours(9).AddMinutes(5), "MA72", "WA01"),
                      T(11, day.AddHours(10).AddMinutes(1), "MA72", "WA01")]);
        Sampler(store).SampleNow();
        Assert.Equal(day.AddHours(9), store.ReadUphSamples(day, day.AddDays(1))[0].Bucket);

        // Jetzt ältere Telegramme per „backfill" — SampleNow würde sie übergehen, Rebuild nicht.
        store.Insert([T(1, day.AddHours(6).AddMinutes(3), "MA72", "GA51"),
                      T(2, day.AddHours(6).AddMinutes(10), "MA72", "GA51")]);
        Sampler(store).SampleNow();
        Assert.Equal(day.AddHours(9), store.ReadUphSamples(day, day.AddDays(1))[0].Bucket);  // unverändert

        Sampler(store).Rebuild();
        var rows = store.ReadUphSamples(day, day.AddDays(1));
        Assert.Equal(day.AddHours(6), rows[0].Bucket);
        Assert.Equal(2, rows.Single(r => r.Bucket == day.AddHours(6)).Orders);
    }

    [Fact]
    public void Rebuild_haelt_sich_an_die_Aufbewahrung()
    {
        using var store = new TelegramStore(_path);
        var newest = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Unspecified);
        store.Insert([
            T(1, newest.AddDays(-40), "MA72", "WA01"),   // außerhalb der 28 Tage
            T(2, newest.AddDays(-2), "MA72", "WA01"),
            T(3, newest, "MA72", "WA01"),
        ]);

        new HistorySampler(store, TelegramFormat.Default,
            [new ResourcePointConfig { Name = "MA72" }], null,
            intervalMinutes: 60, retentionDays: 28, _ => { }).Rebuild();

        var rows = store.ReadUphSamples(newest.AddDays(-60), newest.AddDays(1));
        Assert.All(rows, r => Assert.True(r.Bucket >= newest.AddDays(-28)));
        Assert.Contains(rows, r => r.Bucket == new DateTime(2026, 9, 28, 12, 0, 0));
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

    // ---- RBG-Langzeitaufzeichnung ---------------------------------------------------------------

    /// <summary>
    /// Ein RBG-Transport: aufnehmen und abgeben. Die Richtung steckt in Quelle und Ziel des
    /// ENDDEP - ein Regalplatz (numerisch) bedeutet Einlagerung, eine Station Auslagerung.
    /// </summary>
    static string RbgData(string mc, string src, string dst) =>
        "DM" + "01" + "MFC1" + "SR01" + "01" + "00" +
        mc.PadRight(6, '.') + "0150" + "SR01LU11".PadRight(10, '.') + "LE1".PadRight(20, '.') +
        src.PadRight(10, '.') + dst.PadRight(10, '.') + new string('.', 33);

    /// <summary>Sammelt Transporte und vergibt dabei fortlaufende Ids.</summary>
    sealed class RbgFeed
    {
        long _id = 1;
        public readonly List<Telegram> Rows = [];

        public void Transport(DateTime start, DateTime end, bool store, string conn)
        {
            var pickFrom = store ? "MA41" : "010361211";
            var dropTo = store ? "010361211" : "MA62";
            Rows.Add(new(_id++, start, TelegramDirection.FromPlc, conn,
                RbgData("PUPORD", pickFrom, "SR01LU11"), null));
            Rows.Add(new(_id++, end, TelegramDirection.FromPlc, conn,
                RbgData("ENDDEP", "SR01LU11", dropTo), null));
        }
    }

    static HistorySampler RbgSampler(TelegramStore store, int rbgRetentionDays = 365) =>
        new(store, TelegramFormat.Default,
            [new ResourcePointConfig { Name = "MA72", Connection = "RBG01" },
             new ResourcePointConfig { Name = "MB72", Connection = "RBG02" }],
            null, intervalMinutes: 60, retentionDays: 28, _ => { },
            RbgOptions.From(new KccConfig()), rbgRetentionDays);

    [Fact]
    public void Verdichtet_die_RBG_Spiele_je_Verbindung()
    {
        using var store = new TelegramStore(_path);
        var day = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var feed = new RbgFeed();
        feed.Transport(day.AddHours(8), day.AddHours(8).AddMinutes(2), true, "RBG01");
        feed.Transport(day.AddHours(8).AddMinutes(10), day.AddHours(8).AddMinutes(12), false, "RBG01");
        feed.Transport(day.AddHours(8).AddMinutes(20), day.AddHours(8).AddMinutes(22), true, "RBG01");
        feed.Transport(day.AddHours(8).AddMinutes(30), day.AddHours(8).AddMinutes(32), false, "RBG02");
        feed.Transport(day.AddHours(9), day.AddHours(9).AddMinutes(5), true, "RBG01");   // laufendes Raster
        store.Insert(feed.Rows);

        RbgSampler(store).SampleRbgNow();

        var samples = store.ReadRbgSamples(day, day.AddDays(1));
        var one = samples.Single(r => r.Connection == "RBG01");
        Assert.Equal(day.AddHours(8), one.Bucket);
        Assert.Equal(2, one.Stores);
        Assert.Equal(1, one.Retrievals);
        Assert.Equal(1, one.DoubleCycles);
        Assert.Equal(1, one.SingleCycles);

        var two = samples.Single(r => r.Connection == "RBG02");
        Assert.Equal(0, two.Stores);
        Assert.Equal(1, two.Retrievals);
    }

    [Fact]
    public void Rebuild_laesst_RBG_Zeilen_vor_den_Rohtelegrammen_stehen()
    {
        using var store = new TelegramStore(_path);
        var day = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Unspecified);

        // Alte Aufzeichnung, deren Rohtelegramme längst geprunt sind.
        var old = day.AddDays(-120);
        store.ReplaceRbgSamplesFrom(old,
            [new RbgSampleRow { Bucket = old, Connection = "RBG01", Stores = 7, Retrievals = 7 }]);

        var fresh = new RbgFeed();
        fresh.Transport(day.AddHours(8), day.AddHours(8).AddMinutes(2), true, "RBG01");
        fresh.Transport(day.AddHours(9), day.AddHours(9).AddMinutes(2), false, "RBG01");
        store.Insert(fresh.Rows);

        RbgSampler(store).Rebuild();

        var rows = store.ReadRbgSamples(old.AddDays(-1), day.AddDays(1));
        Assert.Equal(7, rows.Single(r => r.Bucket == old).Stores);       // Langzeitreihe überlebt
        Assert.Contains(rows, r => r.Bucket == day.AddHours(8));       // neu verdichtet
    }

    [Fact]
    public void Rebuild_verdichtet_RBG_Historie_ueber_die_kuerzere_UPH_Aufbewahrung_hinaus()
    {
        using var store = new TelegramStore(_path);
        var newest = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Unspecified);
        var old = newest.AddDays(-40);   // außerhalb der 28-tägigen UPH-, innerhalb der 365-tägigen RBG-Aufbewahrung

        var feed = new RbgFeed();
        feed.Transport(old, old.AddMinutes(2), true, "RBG01");
        feed.Transport(newest, newest.AddMinutes(2), true, "RBG01");
        store.Insert(feed.Rows);

        RbgSampler(store).Rebuild();

        var rows = store.ReadRbgSamples(old.AddDays(-1), newest.AddDays(1));
        Assert.Contains(rows, r => r.Bucket == old);   // wäre am UPH-Fenster (28 Tage) hängengeblieben
    }

    // ---- Ressourcenpunkt-Langzeitaufzeichnung (Belegung & Leistung) -----------------------------

    static int FieldOffset(string name)
    {
        var p = 0;
        foreach (var f in TelegramFormat.Default.Fields)
        {
            if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))
                return p;
            p += f.Length;
        }
        throw new ArgumentException(name);
    }

    static string ConvData(string mc, string rp)
    {
        var buf = new char[TelegramFormat.Default.Length];
        Array.Fill(buf, '.');
        void Put(string field, string v)
        {
            var a = FieldOffset(field);
            for (var i = 0; i < v.Length && a + i < buf.Length; i++) buf[a + i] = v[i];
        }
        Put("TelegramType", "DM");
        Put("MessageCode", mc);
        Put("ResourcePoint", rp);
        return new string(buf);
    }

    static HistorySampler PointSampler(TelegramStore store, int pointRetentionDays = 365) =>
        new(store, TelegramFormat.Default,
            [new ResourcePointConfig { Name = "EA21", TargetUph = 60 }],
            null, intervalMinutes: 60, retentionDays: 28, _ => { },
            rbg: null, rbgRetentionDays: 0,
            ConveyorOptions.From(new KccConfig()), pointRetentionDays);

    Telegram Conv(long id, DateTime at, string mc, string rp) =>
        new(id, at, TelegramDirection.FromPlc, "L1", ConvData(mc, rp), null);

    [Fact]
    public void Verdichtet_Belegzeit_und_Auftraege_je_Ressourcenpunkt()
    {
        using var store = new TelegramStore(_path);
        var day = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Unspecified);
        store.Insert(
        [
            // Ankunft 08:10, Auftrag 08:20, Verlassen 08:30 → 20 min = 1200 s belegt im 08:00-Raster.
            Conv(1, day.AddHours(8).AddMinutes(10), "ENDTSP", "EA21"),
            Conv(2, day.AddHours(8).AddMinutes(20), "TSPORD", "EA21"),
            Conv(3, day.AddHours(8).AddMinutes(30), "RPFREE", "EA21"),
            Conv(4, day.AddHours(9).AddMinutes(5), "ENDTSP", "EA21"),   // laufendes Raster, unvollständig
        ]);

        PointSampler(store).SamplePointsNow();

        var rows = store.ReadPointSamples(day, day.AddDays(1));
        var r = Assert.Single(rows);
        Assert.Equal(day.AddHours(8), r.Bucket);
        Assert.Equal("EA21", r.ResourcePoint);
        Assert.Equal(1200, r.BusySeconds, 0);
        Assert.Equal(1, r.Orders);   // ein TSPORD
    }

    [Fact]
    public void Rebuild_laesst_Ressourcenpunkt_Zeilen_vor_den_Rohtelegrammen_stehen()
    {
        using var store = new TelegramStore(_path);
        var day = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Unspecified);

        var old = day.AddDays(-120);
        store.ReplacePointSamplesFrom(old,
            [new PointSampleRow { Bucket = old, ResourcePoint = "EA21", BusySeconds = 500, Orders = 9 }]);

        store.Insert(
        [
            Conv(1, day.AddHours(8), "ENDTSP", "EA21"),
            Conv(2, day.AddHours(8).AddMinutes(2), "RPFREE", "EA21"),
            Conv(3, day.AddHours(9).AddMinutes(1), "ENDTSP", "EA21"),   // schiebt den rechten Rand über 08:00
        ]);

        PointSampler(store).Rebuild();

        var rows = store.ReadPointSamples(old.AddDays(-1), day.AddDays(1));
        Assert.Equal(9, rows.Single(r => r.Bucket == old).Orders);          // Langzeitreihe überlebt
        Assert.Contains(rows, r => r.Bucket == day.AddHours(8));            // neu verdichtet
    }

    [Fact]
    public void Rebuild_verdichtet_Ressourcenpunkt_Historie_ueber_die_kuerzere_UPH_Aufbewahrung_hinaus()
    {
        using var store = new TelegramStore(_path);
        var newest = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Unspecified);
        var old = newest.AddDays(-40);   // außerhalb der 28-tägigen UPH-, innerhalb der 365-tägigen Punkt-Aufbewahrung

        store.Insert(
        [
            Conv(1, old, "ENDTSP", "EA21"),
            Conv(2, old.AddMinutes(10), "RPFREE", "EA21"),
            Conv(3, newest, "ENDTSP", "EA21"),
        ]);

        PointSampler(store).Rebuild();

        var rows = store.ReadPointSamples(old.AddDays(-1), newest.AddDays(1));
        Assert.Contains(rows, r => r.Bucket == old);   // wäre am UPH-Fenster (28 Tage) hängengeblieben
    }

    public void Dispose()
    {
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
            if (File.Exists(file))
                File.Delete(file);
    }
}
