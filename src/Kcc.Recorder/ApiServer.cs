using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kcc.Recorder;

/// <summary>
/// Kleine Lese-Website auf Kestrel: bindet direkt einen Socket (kein <c>http.sys</c>, keine
/// URL-Freigabe nötig) und liefert die Dashboards samt JSON-API aus. Endpunkte:
/// <list type="bullet">
///   <item><c>GET /</c> — Dashboard</item>
///   <item><c>GET /api/kpis?minutes=240</c> — Kennzahlen über das Zeitfenster</item>
///   <item><c>GET /api/telegrams?minutes=240&amp;limit=2000</c> — Telegramme des Zeitfensters</item>
///   <item><c>GET /auslastung</c> — Auslastung der Ressourcenpunkte</item>
///   <item><c>GET /api/utilization?minutes=240&amp;target=200&amp;bucket=5</c> — Auslastung als JSON</item>
///   <item><c>GET /verlauf</c> — UPH-Historie je Endziel (eigene 4-Wochen-Aufbewahrung)</item>
///   <item><c>GET /api/uph-history?hours=168&amp;bucket=15&amp;rp=MA72</c> — Historie als JSON</item>
///   <item><c>GET /kontur</c> — Auswertung der Konturkontrollen (Status-Feld <c>Kxyz</c>)</item>
///   <item><c>GET /api/kontur?minutes=480</c> — Konturfehler je Kontrolle als JSON</item>
///   <item><c>GET /health</c></item>
/// </list>
/// </summary>
public static class ApiServer
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(), new UtcDateTimeConverter() },
    };

    /// <summary>
    /// Die Anlage schickt (und wir speichern) Zeitstempel in UTC, aber zeitzonenfrei
    /// (<see cref="DateTimeKind.Unspecified"/>). Dieser Konverter schreibt sie als echtes
    /// UTC-ISO (mit <c>Z</c>), damit die Dashboards sie per <c>new Date(...)</c> automatisch in
    /// die lokale Zeit des Betrachters umrechnen.
    /// </summary>
    sealed class UtcDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            reader.GetDateTime();

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            var utc = value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            };
            writer.WriteStringValue(utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    public static async Task RunAsync(KccConfig config, Action<string> log, CancellationToken ct)
    {
        var format = config.DataFormat is { Length: > 0 } spec
            ? TelegramFormat.Parse(spec)
            : TelegramFormat.Default;
        var (host, port) = ParseApiUrl(config.ApiUrl);

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();   // eigene Log-Ausgabe über 'log'
        // Kestrel bindet den Socket direkt — '+'/'*' = alle Schnittstellen, ohne URL-Freigabe.
        builder.WebHost.UseUrls($"http://{host}:{port}");

        var app = builder.Build();
        app.Run(async ctx =>
        {
            ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
            var (status, contentType, body) = Handle(
                ctx.Request.Method, ctx.Request.Path.Value ?? "/",
                key => ctx.Request.Query.TryGetValue(key, out var v) && v.Count > 0 ? v[0] : null,
                config, format, log);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = contentType;
            await ctx.Response.WriteAsync(body, ctx.RequestAborted);
        });

        var anyHost = host is "+" or "*" or "" or "0.0.0.0" or "::";
        log($"API + Dashboard: http://{(anyHost ? "<host>" : host)}:{port}/  (Strg+C beendet)");
        await app.RunAsync(ct);
    }

    const string JsonContentType = "application/json; charset=utf-8";
    const string HtmlContentType = "text/html; charset=utf-8";

    static (int Status, string ContentType, string Body) Ok(object body) =>
        (200, JsonContentType, JsonSerializer.Serialize(body, Json));

    static (int Status, string ContentType, string Body) Html(string page, string activePath) =>
        (200, HtmlContentType, DashboardNav.Inject(RbgGlossary.Inject(page), activePath));

    /// <summary>Bearbeitet eine Anfrage transportunabhängig; gibt Status, Content-Type und Rumpf zurück.</summary>
    static (int Status, string ContentType, string Body) Handle(
        string method, string path, Func<string, string?> q,
        KccConfig config, TelegramFormat format, Action<string> log)
    {
        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
            return (405, JsonContentType, JsonSerializer.Serialize(new { error = "method not allowed" }, Json));

        try
        {
            return path switch
            {
                "/" or "/index.html" => Html(Dashboard.Html, "/"),
                "/auslastung" or "/auslastung.html" => Html(UtilizationDashboard.Html, "/auslastung"),
                "/verlauf" or "/verlauf.html" => Html(UphHistoryDashboard.Html, "/verlauf"),
                "/kontur" or "/kontur.html" => Html(ContourDashboard.Html, "/kontur"),
                "/wand" or "/wand.html" => Html(WallboardDashboard.Html, "/wand"),
                "/rbg" or "/rbg.html" => Html(RbgHistoryDashboard.Html, "/rbg"),
                "/api/rbg-history" => Ok(RbgHistory(config,
                    RbgHours(q), RbgBucket(q, config), HistStamp(q, "from"), HistStamp(q, "to"))),
                "/api/kontur" => Ok(Contour(config, format, ContourMinutes(q, config))),
                "/api/utilization" => Ok(Utilization(config, format, UtilMinutes(q, config), Target(q, config), Bucket(q, config), Rate(q, config), SeriesStep(q, config))),
                "/api/uph-history" => Ok(UphHistory(config, format,
                    HistHours(q), HistBucket(q, config), HistGroupBy(q), q("rp"),
                    HistStamp(q, "from"), HistStamp(q, "to"), HistRolling(q))),
                "/health" => Ok(Health(config)),
                "/api/kpis" => Ok(Kpis(config, format, Minutes(q, config))),
                "/api/telegrams" => Ok(Telegrams(config, Minutes(q, config), Limit(q))),
                "/api/fields" => Ok(Fields(config, format, Minutes(q, config), Limit(q))),
                _ => (404, JsonContentType, JsonSerializer.Serialize(new { error = "not found", path }, Json)),
            };
        }
        catch (Exception ex)
        {
            log($"API-Fehler: {ex.Message}");
            return (500, JsonContentType, JsonSerializer.Serialize(new { error = ex.Message }, Json));
        }
    }

    static TelegramKpis Kpis(KccConfig config, TelegramFormat format, int minutes)
    {
        using var store = new TelegramStore(config.Database);
        var w = ReadWindow(store, minutes);
        return TelegramKpis.Compute(w.Rows, format, minutes, w.Start, w.End, store.SecondsSinceLastWrite());
    }

    static TelegramUtilization Utilization(
        KccConfig config, TelegramFormat format, int minutes, double target, int bucketMinutes,
        int rateMinutes, int seriesStep)
    {
        using var store = new TelegramStore(config.Database);
        var w = ReadWindow(store, minutes);
        return TelegramUtilization.Compute(
            w.Rows, format, minutes, target, w.End, config.ResourcePoints, bucketMinutes, rateMinutes,
            config.DestinationLabels, config.GroupOrder, seriesStep, RbgOptions.From(config),
            ConveyorOptions.From(config), config.CountTelegramType);
    }

    static UphHistoryReport UphHistory(
        KccConfig config, TelegramFormat format, int hours, int bucketMinutes,
        UphHistoryGroupBy groupBy, string? resourcePoint, DateTime? from, DateTime? to,
        int rollingWindowMinutes)
    {
        using var store = new TelegramStore(config.Database);
        var newest = store.MaxTelegramTime()
            ?? DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified);

        // Absolutes Fenster (Zoom per from&to) schlägt das relative 'hours'-Fenster.
        var end = to ?? newest;
        var start = from ?? end.AddHours(-hours);
        if (start >= end)
            start = end.AddHours(-1);

        // Gleitender Kurzzeit-Verlauf: direkt aus den Rohtelegrammen (die 15-min-Rollup-Tabelle
        // ist dafür zu grob). Sonst wie bisher aus der Rollup-Tabelle in feste Eimer.
        var rows = rollingWindowMinutes > 0
            ? UphHistoryReport.FromTelegrams(store.Read(start, end).ToList(), format,
                config.DestinationLabels, config.CountTelegramType)
            : store.ReadUphSamples(start, end);

        return UphHistoryReport.Compute(
            rows, start, end, bucketMinutes, groupBy,
            config.DestinationLabels, config.ResourcePoints, resourcePoint, rollingWindowMinutes);
    }

    /// <summary>
    /// Langzeitvergleich der RBG aus der eigenen Rasterreihe — reicht so weit zurück, wie
    /// aufgezeichnet wurde (<c>RbgHistoryRetentionDays</c>), unabhängig von den Rohtelegrammen.
    /// </summary>
    static RbgHistoryReport RbgHistory(
        KccConfig config, int hours, int bucketMinutes, DateTime? from, DateTime? to)
    {
        using var store = new TelegramStore(config.Database);
        var newest = store.MaxRbgBucket() ?? store.MaxTelegramTime()
            ?? DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified);

        // Das jüngste Raster soll noch hineinfallen — ReadRbgSamples schneidet rechts offen ab.
        var end = to ?? newest.AddMinutes(Math.Max(1, config.UphHistoryIntervalMinutes));
        var start = from ?? end.AddHours(-hours);
        if (start >= end)
            start = end.AddHours(-1);

        return RbgHistoryReport.Compute(
            store.ReadRbgSamples(start, end), start, end, bucketMinutes,
            config.ResourcePoints, RbgCapacity.From(config));
    }

    static ContourReport Contour(KccConfig config, TelegramFormat format, int minutes)
    {
        using var store = new TelegramStore(config.Database);
        var w = ReadWindow(store, minutes);
        return ContourReport.Compute(
            w.Rows, format, minutes, w.Start, w.End, config.ContourCheckpoints, config.ContourFlags,
            config.CountTelegramType);
    }

    static object Telegrams(KccConfig config, int minutes, int limit)
    {
        using var store = new TelegramStore(config.Database);
        var w = ReadWindow(store, minutes);
        var clipped = w.Rows.Count > limit ? w.Rows.GetRange(w.Rows.Count - limit, limit) : w.Rows;
        return new
        {
            minutes,
            from = w.Start,
            to = w.End,
            total = w.Rows.Count,
            count = clipped.Count,
            telegrams = clipped,
        };
    }

    /// <summary>
    /// Diagnose: zerlegt die letzten Telegramme des Fensters Feld für Feld nach dem Layout.
    /// Damit lässt sich prüfen, welches Feld tatsächlich den Ressourcenpunkt trägt.
    /// </summary>
    static object Fields(KccConfig config, TelegramFormat format, int minutes, int limit)
    {
        using var store = new TelegramStore(config.Database);
        var w = ReadWindow(store, minutes);
        var take = Math.Min(limit, 200);
        var rows = w.Rows.Count > take ? w.Rows.GetRange(w.Rows.Count - take, take) : w.Rows;

        return new
        {
            layout = format.Fields.Select(f => new { f.Name, f.Length, f.Type }),
            count = rows.Count,
            telegrams = rows.Select(t =>
            {
                var values = format.Slice(t.Data);
                return new
                {
                    t.Id,
                    t.DateTime,
                    data = t.Data,
                    fields = format.Fields
                        .Select((f, i) => (f.Name, Value: i < values.Count ? values[i] : ""))
                        .ToDictionary(x => x.Name, x => x.Value),
                };
            }),
        };
    }

    static object Health(KccConfig config)
    {
        using var store = new TelegramStore(config.Database);
        return new
        {
            status = "ok",
            database = Path.GetFullPath(config.Database),
            telegrams = store.Count(),
            lastSeenId = store.GetLastSeenId(),
            newestTelegram = store.MaxTelegramTime(),
            uphSamples = store.UphSampleCount(),
            rbgSamples = store.RbgSampleCount(),
            rbgHistoryFrom = store.MinRbgBucket(),
            secondsSinceLastWrite = store.SecondsSinceLastWrite(),
            serverTime = DateTime.Now,
        };
    }

    /// <summary>
    /// Telegramme des Fensters, aufsteigend. Der rechte Rand ist der Zeitstempel des jüngsten
    /// Telegramms (<see cref="TelegramStore.MaxTelegramTime"/>), nicht die Host-Uhr — so bleibt
    /// „letzte N Minuten" richtig, egal in welcher Zeitzone die Anlage ihre Stempel schickt.
    /// </summary>
    static (List<Telegram> Rows, DateTime Start, DateTime End) ReadWindow(TelegramStore store, int minutes)
    {
        var end = store.MaxTelegramTime()
            ?? DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified);
        var start = end.AddMinutes(-minutes);
        return (store.Read(start, null).ToList(), start, end);
    }

    static int Minutes(Func<string, string?> q, KccConfig config) =>
        Clamp(q("minutes"), fallback: config.WindowMinutes, min: 1, max: 1440);

    static int UtilMinutes(Func<string, string?> q, KccConfig config) =>
        Clamp(q("minutes"), fallback: config.UtilizationWindowMinutes, min: 1, max: 1440);

    static int ContourMinutes(Func<string, string?> q, KccConfig config) =>
        Clamp(q("minutes"), fallback: config.ContourWindowMinutes, min: 1, max: 60 * 24 * 30);

    static int Limit(Func<string, string?> q) =>
        Clamp(q("limit"), fallback: 2000, min: 1, max: 20000);

    static int Bucket(Func<string, string?> q, KccConfig config) =>
        Clamp(q("bucket"), fallback: config.UtilizationBucketMinutes, min: 1, max: 120);

    static int Rate(Func<string, string?> q, KccConfig config) =>
        Clamp(q("rate"), fallback: config.UtilizationRateMinutes, min: 1, max: 240);

    static int SeriesStep(Func<string, string?> q, KccConfig config) =>
        Clamp(q("step"), fallback: config.UtilizationSeriesStepMinutes, min: 1, max: 30);

    // UPH-Historie: Zeitraum bis 4 Wochen, Anzeigeraster bis 1 Tag.
    static int HistHours(Func<string, string?> q) =>
        Clamp(q("hours"), fallback: 168, min: 1, max: 24 * 28);

    static int HistBucket(Func<string, string?> q, KccConfig config) =>
        Clamp(q("bucket"), fallback: config.UphHistoryIntervalMinutes, min: 1, max: 1440);

    // > 0 ⇒ gleitendes Fenster (Minuten) aus Rohtelegrammen statt fester Eimer aus der Rollup-Tabelle.
    static int HistRolling(Func<string, string?> q) =>
        Clamp(q("rolling"), fallback: 0, min: 0, max: 240);

    // RBG-Langzeitvergleich: Zeitraum bis ein Jahr, Anzeigeraster bis 1 Tag.
    static int RbgHours(Func<string, string?> q) =>
        Clamp(q("hours"), fallback: 168, min: 1, max: 24 * 366);

    static int RbgBucket(Func<string, string?> q, KccConfig config) =>
        Clamp(q("bucket"), fallback: Math.Max(60, config.UphHistoryIntervalMinutes), min: 1, max: 1440);

    static UphHistoryGroupBy HistGroupBy(Func<string, string?> q) =>
        string.Equals(q("groupBy"), "resourcePoint", StringComparison.OrdinalIgnoreCase)
            ? UphHistoryGroupBy.ResourcePoint
            : UphHistoryGroupBy.Destination;

    // Zeitzonenfrei geparst — passend zur Ablage in Anlagenzeit.
    static DateTime? HistStamp(Func<string, string?> q, string key) =>
        DateTime.TryParse(q(key),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var v)
            ? DateTime.SpecifyKind(v, DateTimeKind.Unspecified)
            : null;

    static double Target(Func<string, string?> q, KccConfig config) =>
        double.TryParse(q("target"),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0
            ? v
            : config.UtilizationTargetUph;

    static int Clamp(string? raw, int fallback, int min, int max) =>
        int.TryParse(raw, out var v) ? Math.Clamp(v, min, max) : fallback;

    /// <summary>Zerlegt <c>ApiUrl</c> in Host (ggf. <c>+</c>/<c>*</c> für „alle") und Port.</summary>
    internal static (string Host, int Port) ParseApiUrl(string? url)
    {
        var u = string.IsNullOrWhiteSpace(url) ? "http://+:8082/" : url.Trim();
        var m = System.Text.RegularExpressions.Regex.Match(u, @"^https?://([^/:]*)(?::(\d+))?");
        var host = m.Success && m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : "+";
        var port = m.Success && m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 8082;
        return (host, port);
    }
}
