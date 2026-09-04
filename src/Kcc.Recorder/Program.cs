using System.Text;
using System.Text.Json;
using Kcc.Recorder;

var cli = CommandLine.Parse(args);

if (cli.Command is "help" or "--help" || cli.HasFlag("help"))
{
    PrintUsage();
    return 0;
}

var verbose = cli.HasFlag("verbose");
void Log(string message)
{
    Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
}
void Trace(string message)
{
    if (verbose)
        Log(message);
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Log("Abbruch angefordert — beende sauber …");
    cancellation.Cancel();
};

try
{
    var config = KccConfig.Load(cli);
    return cli.Command switch
    {
        "" => await RunAsync(config, cancellation.Token),
        "login-test" => await LoginTestAsync(config, cancellation.Token),
        "query" => await QueryAsync(config, cli, cancellation.Token),
        "backfill" => await BackfillAsync(config, cli, cancellation.Token),
        "prune" => Prune(config, cli),
        "export" => Export(config, cli),
        "dump-dashboards" => DumpDashboards(cli),
        _ => UnknownCommand(cli.Command),
    };
}
catch (OperationCanceledException)
{
    Log("Abgebrochen.");
    return 130;
}
catch (Exception ex)
{
    Log($"Fehler: {ex.Message}");
    if (verbose)
        Console.Error.WriteLine(ex);
    return 1;
}

int UnknownCommand(string command)
{
    Log($"Unbekanntes Kommando '{command}'.");
    PrintUsage();
    return 2;
}

// Baut Verbindung auf und meldet an; der Aufrufer erhält alles, was er für Abfragen braucht.
async Task<(KccConnection Connection, KccSession Session, KccQuery Query)> ConnectAsync(
    KccConfig config, CancellationToken ct)
{
    var user = config.User ?? Prompt("Benutzername: ");
    var password = config.Password ?? PromptPassword("Passwort: ");

    var connection = new KccConnection(new Uri(config.Url), config.AllowUntrustedCertificate, Trace);
    await connection.ConnectAsync(ct);

    var session = new KccSession(connection, Log);
    await session.LogonAsync(user, password, ct);

    return (connection, session, new KccQuery(connection, session));
}

async Task<int> LoginTestAsync(KccConfig config, CancellationToken ct)
{
    var (connection, session, query) = await ConnectAsync(config, ct);
    await using (connection)
    {
        var newest = await query.QueryTelegramsAsync(
            null, [new QueryOrderBy { column = "Id", sort = "desc" }], 1, null, ct);

        if (newest.Count == 0)
            Log("Anmeldung erfolgreich, es sind aber keine Telegramme lesbar. " +
                "Fehlt dem Account die Rolle CommonPlcProtocol?");
        else
            Log($"Anmeldung erfolgreich. Neuestes Telegramm: Id {newest[0].Id} vom {newest[0].DateTime:G}.");

        await session.LogoffAsync(ct);
    }
    return 0;
}

async Task<int> QueryAsync(KccConfig config, CommandLine cli, CancellationToken ct)
{
    var take = cli.GetInt("take") ?? 100;
    var asJson = cli.HasFlag("json");

    var (connection, session, query) = await ConnectAsync(config, ct);
    await using (connection)
    {
        // Ohne Filter, damit sich das Ergebnis 1:1 mit dem Grid der Weboberfläche vergleichen lässt.
        var rows = await query.QueryTelegramsAsync(
            null, [new QueryOrderBy { column = "Id", sort = "desc" }], take, cli.GetInt("skip"), ct);

        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine("Id\tDateTime\tDirection\tConnection\tData");
            foreach (var t in rows)
                Console.WriteLine(
                    $"{t.Id}\t{t.DateTime:yyyy-MM-dd HH:mm:ss.fff}\t{t.TelegramDirection}\t{t.ConnectionName}\t{t.Data}");
        }

        Log($"{rows.Count} Telegramme abgerufen.");
        await session.LogoffAsync(ct);
    }
    return 0;
}

// Standardbetrieb ohne Argumente: Aufzeichnung und Lese-API laufen im selben Prozess.
// Was davon startet, steht in der Konfiguration (Record, Serve).
async Task<int> RunAsync(KccConfig config, CancellationToken ct)
{
    if (!config.Record && !config.Serve)
    {
        Log("Record und Serve sind beide abgeschaltet — nichts zu tun.");
        return 2;
    }

    // Fällt eine der beiden Aufgaben aus, endet auch die andere, statt halb weiterzulaufen.
    using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
    var tasks = new List<Task<int>>();
    if (config.Serve)
        tasks.Add(ServeAsync(config, stop.Token));
    if (config.Record)
        tasks.Add(RecordAsync(config, stop.Token));

    var exit = 0;
    try
    {
        while (tasks.Count > 0)
        {
            var done = await Task.WhenAny(tasks);
            tasks.Remove(done);
            exit = Math.Max(exit, await done);
            stop.Cancel();
        }
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        // Strg+C — regulärer Weg nach draußen.
    }

    return exit;
}

async Task<int> ServeAsync(KccConfig config, CancellationToken ct)
{
    await ApiServer.RunAsync(config, Log, ct);
    return 0;
}

async Task<int> RecordAsync(KccConfig config, CancellationToken ct)
{
    using var store = new TelegramStore(config.Database);
    using var csv = OpenCsv(config);
    var filter = new RecordFilter(config.Filter);

    var (connection, session, query) = await ConnectAsync(config, ct);
    await using (connection)
    {
        Log($"Zeichne auf nach {Path.GetFullPath(config.Database)} " +
            $"(bereits {store.Count()} Telegramme). Beenden mit Strg+C.");
        if (csv is not null)
            Log($"Parallele CSV-Mitschrift (Data nach Layout zerlegt): {csv.FilePath}");

        var uph = new UphHistorySampler(
            store, ResolveFormat(config), config.ResourcePoints, config.DestinationLabels,
            config.UphHistoryIntervalMinutes, config.UphHistoryRetentionDays, Log);

        var recorder = new TelegramRecorder(query, store, filter, config, Log, csv, uph);
        await recorder.RunAsync(ct);

        // Nach Strg+C nicht unbegrenzt auf die Abmelde-Antwort des Servers warten.
        using var logoffTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await session.LogoffAsync(logoffTimeout.Token);
    }
    return 0;
}

async Task<int> BackfillAsync(KccConfig config, CommandLine cli, CancellationToken ct)
{
    if (cli.GetLong("from-id") is not { } fromId)
    {
        Log("backfill benötigt --from-id.");
        return 2;
    }

    using var store = new TelegramStore(config.Database);
    using var csv = OpenCsv(config);
    var filter = new RecordFilter(config.Filter);

    var (connection, session, query) = await ConnectAsync(config, ct);
    await using (connection)
    {
        if (csv is not null)
            Log($"Parallele CSV-Mitschrift: {csv.FilePath}");

        var recorder = new TelegramRecorder(query, store, filter, config, Log, csv);
        await recorder.BackfillAsync(fromId, cli.GetLong("to-id"), ct);

        using var logoffTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await session.LogoffAsync(logoffTimeout.Token);
    }
    return 0;
}

int Prune(KccConfig config, CommandLine cli)
{
    var days = cli.GetInt("days") ?? config.RetentionDays;
    if (days <= 0)
    {
        Log($"Aufbewahrung unbegrenzt (RetentionDays {days}) — nichts zu löschen.");
        return 0;
    }

    using var store = new TelegramStore(config.Database);
    var before = store.Count();
    var cutoff = TelegramStore.RetentionCutoff(days);
    var removed = store.DeleteOlderThan(cutoff);

    if (config.UphHistoryRetentionDays > 0)
    {
        var uphCutoff = TelegramStore.RetentionCutoff(config.UphHistoryRetentionDays);
        var uphRemoved = store.DeleteUphSamplesOlderThan(uphCutoff);
        if (uphRemoved > 0)
            Log($"{uphRemoved} UPH-Rasterzeilen vor {uphCutoff:yyyy-MM-dd} gelöscht.");
    }

    store.Vacuum();

    Log($"{removed} Telegramme vor {cutoff:yyyy-MM-dd} gelöscht ({before} → {store.Count()}).");
    return 0;
}

// Schreibt die eingebetteten Dashboards als eigenständige HTML-Dateien heraus — u. a. fürs Release-Zip.
int DumpDashboards(CommandLine cli)
{
    var dir = cli.GetString("out") ?? ".";
    Directory.CreateDirectory(dir);

    var files = new[]
    {
        ("dashboard.html", Dashboard.Html),
        ("auslastung.html", UtilizationDashboard.Html),
        ("verlauf.html", UphHistoryDashboard.Html),
    };
    foreach (var (name, html) in files)
        File.WriteAllText(Path.Combine(dir, name), html, new UTF8Encoding(false));

    Log($"{string.Join(", ", files.Select(f => f.Item1))} nach {Path.GetFullPath(dir)} geschrieben.");
    return 0;
}

int Export(KccConfig config, CommandLine cli)
{
    var output = cli.GetString("out");
    if (output is null)
    {
        Log("export benötigt --out.");
        return 2;
    }

    var csv = new TelegramCsv(ResolveFormat(config));
    using var store = new TelegramStore(config.Database);
    using var writer = new StreamWriter(output, false, new UTF8Encoding(true));
    writer.WriteLine(csv.Header);

    var count = 0;
    foreach (var t in store.Read(cli.GetDateTime("from"), cli.GetDateTime("to")))
    {
        writer.WriteLine(csv.Row(t));
        count++;
    }

    Log($"{count} Telegramme nach {Path.GetFullPath(output)} exportiert.");
    return 0;
}

static TelegramCsvWriter? OpenCsv(KccConfig config) =>
    config.CsvPath is { Length: > 0 } path
        ? new TelegramCsvWriter(path, new TelegramCsv(ResolveFormat(config)))
        : null;

static TelegramFormat ResolveFormat(KccConfig config) =>
    config.DataFormat is { Length: > 0 } spec ? TelegramFormat.Parse(spec) : TelegramFormat.Default;

static string Prompt(string label)
{
    Console.Error.Write(label);
    return Console.ReadLine() ?? "";
}

static string PromptPassword(string label)
{
    Console.Error.Write(label);

    // Ohne Konsole (Pipe, Dienst) gibt es keine Tastendrücke abzufangen.
    if (Console.IsInputRedirected)
        return Console.ReadLine() ?? "";

    var password = new StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
            break;
        if (key.Key == ConsoleKey.Backspace)
        {
            if (password.Length > 0)
                password.Length--;
            continue;
        }
        if (!char.IsControl(key.KeyChar))
            password.Append(key.KeyChar);
    }
    Console.Error.WriteLine();
    return password.ToString();
}

static void PrintUsage() => Console.WriteLine(
    """
    kcc — Mitschnitt der SPS-Telegramme einer Kardex MCC/KCC-Anlage.

    Aufruf ohne Argumente: Aufzeichnung und Lese-API + Dashboard laufen gemeinsam,
    konfiguriert über appsettings.json (Record, Serve, ApiUrl, Database, CsvPath).

    Kommandos:
      login-test                       Verbindung und Anmeldung prüfen
      query   [--take N] [--skip N]    Einmalabfrage auf stdout (--json für JSON)
              [--json]
      backfill --from-id N [--to-id M] Ältere Telegramme nachladen
      prune   [--days N]               Telegramme älter als N Tage löschen (Standard: RetentionDays)
      export  --out datei.csv          Aufgezeichnete Telegramme als CSV ausgeben
              [--from ...] [--to ...]
      dump-dashboards [--out verz]     dashboard.html + auslastung.html herausschreiben

    Optionen:
      --config datei    Zusätzliche JSON-Konfiguration (überschreibt appsettings.json)
      --url wss://...   Endpunkt der Anlage
      --user name       Benutzername
      --password wert   Passwort (besser: KCC_PASSWORD oder interaktiv)
      --db datei        SQLite-Datei (Standard: kcc-telegrams.db)
      --insecure        Selbstsigniertes Zertifikat der Anlage akzeptieren
      --poll-interval N Sekunden zwischen zwei Abfragen
      --batch-size N    Zeilen pro Abfrage
      --days N          Aufbewahrungsdauer für 'prune' (überschreibt RetentionDays)
      --verbose         Protokolliert die gesendeten und empfangenen Rahmen

    Konfiguration in aufsteigender Priorität (später schlägt früher):
      appsettings.json -> appsettings.local.json (neben der EXE bzw. im Arbeitsverzeichnis)
      -> --config-Datei -> Umgebungsvariablen KCC_* (KCC_URL, KCC_USER, KCC_PASSWORD,
      KCC_DATABASE, geschachtelt KCC_Filter__MinDataLength) -> Kommandozeile.
    Fehlt das Passwort, wird es verdeckt abgefragt.
    """);
