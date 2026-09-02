using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kcc.Recorder;

/// <summary>
/// Kleine Lese-API auf Basis von <see cref="HttpListener"/> (keine zusätzliche Abhängigkeit) plus
/// eingebettetem Dashboard. Endpunkte:
/// <list type="bullet">
///   <item><c>GET /</c> — Dashboard</item>
///   <item><c>GET /api/kpis?minutes=60</c> — Kennzahlen über das Zeitfenster</item>
///   <item><c>GET /api/telegrams?minutes=60&amp;limit=2000</c> — Telegramme des Zeitfensters</item>
///   <item><c>GET /auslastung</c> — Auslastung der Ressourcenpunkte</item>
///   <item><c>GET /api/utilization?minutes=60&amp;target=200</c> — Auslastung als JSON</item>
///   <item><c>GET /health</c></item>
/// </list>
/// </summary>
public static class ApiServer
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task RunAsync(KccConfig config, Action<string> log, CancellationToken ct)
    {
        var prefix = NormalizePrefix(config.ApiUrl);
        var format = config.DataFormat is { Length: > 0 } spec
            ? TelegramFormat.Parse(spec)
            : TelegramFormat.Default;

        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            throw new InvalidOperationException(
                $"API-Endpunkt {prefix} konnte nicht geöffnet werden: {ex.Message}. " +
                "Bei '+' oder festem Hostnamen ist unter Windows 'netsh http add urlacl' nötig; " +
                "'http://localhost:PORT/' geht ohne Rechte.", ex);
        }

        log($"API + Dashboard: {prefix}  (Strg+C beendet)");
        using var stopOnCancel = ct.Register(listener.Stop);

        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }

            _ = Task.Run(() => HandleAsync(context, config, format, log), CancellationToken.None);
        }
    }

    static async Task HandleAsync(
        HttpListenerContext ctx, KccConfig config, TelegramFormat format, Action<string> log)
    {
        var res = ctx.Response;
        res.AddHeader("Access-Control-Allow-Origin", "*");
        try
        {
            if (ctx.Request.HttpMethod != "GET")
            {
                await WriteJsonAsync(res, 405, new { error = "method not allowed" });
                return;
            }

            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            switch (path)
            {
                case "/":
                case "/index.html":
                    await WriteTextAsync(res, 200, "text/html; charset=utf-8", Dashboard.Html);
                    break;
                case "/auslastung":
                case "/auslastung.html":
                    await WriteTextAsync(res, 200, "text/html; charset=utf-8", UtilizationDashboard.Html);
                    break;
                case "/api/utilization":
                    await WriteJsonAsync(res, 200, Utilization(config, format, Minutes(ctx), Target(ctx, config)));
                    break;
                case "/health":
                    await WriteJsonAsync(res, 200, Health(config));
                    break;
                case "/api/kpis":
                    await WriteJsonAsync(res, 200, Kpis(config, format, Minutes(ctx)));
                    break;
                case "/api/telegrams":
                    await WriteJsonAsync(res, 200, Telegrams(config, Minutes(ctx), Limit(ctx)));
                    break;
                default:
                    await WriteJsonAsync(res, 404, new { error = "not found", path });
                    break;
            }
        }
        catch (Exception ex)
        {
            log($"API-Fehler: {ex.Message}");
            try { await WriteJsonAsync(res, 500, new { error = ex.Message }); }
            catch { /* Antwort ggf. schon geschlossen */ }
        }
        finally
        {
            res.Close();
        }
    }

    static TelegramKpis Kpis(KccConfig config, TelegramFormat format, int minutes)
    {
        using var store = new TelegramStore(config.Database);
        var now = DateTime.Now;
        return TelegramKpis.Compute(ReadWindow(store, minutes, out _), format, minutes, now);
    }

    static TelegramUtilization Utilization(
        KccConfig config, TelegramFormat format, int minutes, double target)
    {
        using var store = new TelegramStore(config.Database);
        return TelegramUtilization.Compute(
            ReadWindow(store, minutes, out _), format, minutes, target, DateTime.Now, config.ResourcePoints);
    }

    static object Telegrams(KccConfig config, int minutes, int limit)
    {
        using var store = new TelegramStore(config.Database);
        var rows = ReadWindow(store, minutes, out var since);
        var clipped = rows.Count > limit ? rows.GetRange(rows.Count - limit, limit) : rows;
        return new
        {
            minutes,
            since,
            total = rows.Count,
            count = clipped.Count,
            telegrams = clipped,
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
        };
    }

    /// <summary>Telegramme mit <c>DateTime</c> ab jetzt minus <paramref name="minutes"/> (aufsteigend).</summary>
    static List<Telegram> ReadWindow(TelegramStore store, int minutes, out DateTime since)
    {
        since = DateTime.SpecifyKind(DateTime.Now.AddMinutes(-minutes), DateTimeKind.Unspecified);
        return store.Read(since, null).ToList();
    }

    static int Minutes(HttpListenerContext ctx) =>
        Clamp(ctx.Request.QueryString["minutes"], fallback: 60, min: 1, max: 1440);

    static int Limit(HttpListenerContext ctx) =>
        Clamp(ctx.Request.QueryString["limit"], fallback: 2000, min: 1, max: 20000);

    static double Target(HttpListenerContext ctx, KccConfig config) =>
        double.TryParse(ctx.Request.QueryString["target"],
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0
            ? v
            : config.UtilizationTargetUph;

    static int Clamp(string? raw, int fallback, int min, int max) =>
        int.TryParse(raw, out var v) ? Math.Clamp(v, min, max) : fallback;

    static string NormalizePrefix(string? url)
    {
        var u = string.IsNullOrWhiteSpace(url) ? "http://localhost:8080/" : url.Trim();
        return u.EndsWith('/') ? u : u + "/";
    }

    static async Task WriteJsonAsync(HttpListenerResponse res, int status, object body)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(body, Json);
        res.StatusCode = status;
        res.ContentType = "application/json; charset=utf-8";
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
    }

    static async Task WriteTextAsync(HttpListenerResponse res, int status, string contentType, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        res.StatusCode = status;
        res.ContentType = contentType;
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
    }
}
