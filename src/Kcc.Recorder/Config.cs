using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Kcc.Recorder;

/// <summary>Regeln, welche Telegramme tatsächlich aufgezeichnet werden.</summary>
public sealed class FilterConfig
{
    /// <summary>Verwirft Telegramme, deren Data-Feld kürzer als dieser Wert ist (nach Trim).</summary>
    public int MinDataLength { get; set; } = 1;

    /// <summary>Verwirft Data-Felder, die nur aus Nullen/Leerzeichen bestehen — typische Leerlauf-Frames.</summary>
    public bool IgnoreAllZeroData { get; set; } = true;

    /// <summary>Wenn befüllt: nur diese Verbindungen aufzeichnen.</summary>
    public List<string> ConnectionWhitelist { get; set; } = [];

    /// <summary>Diese Verbindungen nie aufzeichnen.</summary>
    public List<string> ConnectionBlacklist { get; set; } = [];

    /// <summary>Wenn befüllt: nur diese Richtungen aufzeichnen (ToPlc, FromPlc, Unknown).</summary>
    public List<TelegramDirection> Directions { get; set; } = [];

    /// <summary>Wenn gesetzt: Data muss diesem regulären Ausdruck entsprechen.</summary>
    public string? DataMatchRegex { get; set; }

    /// <summary>Wenn gesetzt: Data darf diesem regulären Ausdruck nicht entsprechen.</summary>
    public string? DataIgnoreRegex { get; set; }

    /// <summary>
    /// Lässt den Server bereits leere Data-Felder herausfiltern. Spart Bandbreite, kann aber
    /// abgeschaltet werden, um zu prüfen, wie viel dadurch wegfällt.
    /// </summary>
    public bool FilterEmptyDataOnServer { get; set; } = true;
}

/// <summary>
/// Ein Ressourcenpunkt der Auslastungsauswertung. In <c>appsettings.json</c> entweder als
/// Objekt <c>{ "Name": "MA72", "Group": "Auslagerung RBG", "Label": "RBG 2" }</c> oder als
/// bloßer String <c>"MA72"</c> (Gruppe/Label leer).
/// </summary>
public sealed class ResourcePointConfig
{
    /// <summary>Code wie im Telegramm-Feld <c>ResourcePoint</c>, z. B. <c>MA72</c>.</summary>
    public string Name { get; set; } = "";

    /// <summary>Gruppe zur Strukturierung im Dashboard. Leer ⇒ „Ohne Gruppe".</summary>
    public string? Group { get; set; }

    /// <summary>Klartext fürs Dashboard. Leer ⇒ <see cref="Name"/>.</summary>
    public string? Label { get; set; }

    /// <summary>
    /// Sortierschlüssel im Dashboard (aufsteigend, kleiner zuerst). Ohne Angabe zählt die
    /// Reihenfolge in der Liste. Innerhalb einer Gruppe wirksam.
    /// </summary>
    public int? Order { get; set; }

    /// <summary>
    /// Eigener UPH-Richtwert dieses Punkts, auf den sich seine Prozent-Auslastung bezieht.
    /// Ohne Angabe gilt <see cref="KccConfig.UtilizationTargetUph"/>.
    /// </summary>
    public double? TargetUph { get; set; }

    public string DisplayLabel => string.IsNullOrWhiteSpace(Label) ? Name : Label!;
    public string GroupOrDefault => string.IsNullOrWhiteSpace(Group) ? "Ohne Gruppe" : Group!;
}

/// <summary>
/// Gesamtkonfiguration. Wird aus <c>appsettings.json</c> gebunden und von lokaler Datei,
/// Umgebungsvariablen und Kommandozeile überschrieben — siehe <see cref="Load"/>.
/// </summary>
public sealed class KccConfig
{
    public string Url { get; set; } = "wss://10.20.220.33/ws";
    public string? User { get; set; }
    public string? Password { get; set; }

    /// <summary>Erlaubt selbstsignierte Zertifikate der Anlage.</summary>
    public bool AllowUntrustedCertificate { get; set; }

    public string Database { get; set; } = "kcc-telegrams.db";

    /// <summary>
    /// Wenn gesetzt: aufgezeichnete Telegramme zusätzlich fortlaufend an diese CSV-Datei anhängen.
    /// <c>null</c> schaltet die CSV-Mitschrift ab.
    /// </summary>
    public string? CsvPath { get; set; }

    /// <summary>
    /// Fixed-Width-Layout des <c>Data</c>-Blocks für die CSV-Spalten (Syntax "Name,Länge,Typ|…").
    /// <c>null</c> nutzt das eingebaute Standard-Layout (<see cref="TelegramFormat.Default"/>).
    /// </summary>
    public string? DataFormat { get; set; }

    /// <summary>
    /// Aufbewahrungsdauer in Tagen. <c>record</c>/<c>backfill</c> löschen beim Start und danach
    /// täglich ältere Telegramme; <c>prune</c> tut es einmalig. <c>0</c> oder negativ = unbegrenzt.
    /// </summary>
    public int RetentionDays { get; set; } = 365;

    /// <summary>
    /// Adresse der Lese-API samt Dashboard im Normalbetrieb. HttpListener-Präfix;
    /// <c>http://localhost:PORT/</c> braucht unter Windows keine Rechte.
    /// </summary>
    public string ApiUrl { get; set; } = "http://localhost:8080/";

    /// <summary>Ob der Standardlauf Telegramme aufzeichnet.</summary>
    public bool Record { get; set; } = true;

    /// <summary>Ob der Standardlauf die Lese-API samt Dashboard bereitstellt.</summary>
    public bool Serve { get; set; } = true;

    /// <summary>
    /// Beim ersten Start einmalig nachgeladene Zeitspanne in Minuten, damit die Dashboards
    /// sofort Historie zeigen. <c>0</c> schaltet die Nachladung ab.
    /// </summary>
    public int StartupBackfillMinutes { get; set; } = 240;

    /// <summary>Zeitfenster des KPI-Dashboards und der API, wenn kein <c>minutes</c> mitgegeben wird.</summary>
    public int WindowMinutes { get; set; } = 240;

    /// <summary>Zeitfenster der Auslastungsansicht (<c>/auslastung</c>), wenn kein <c>minutes</c> mitgegeben wird.</summary>
    public int UtilizationWindowMinutes { get; set; } = 60;

    /// <summary>
    /// Breite des gleitenden Fensters der Verlaufskurven auf <c>/auslastung</c> in Minuten
    /// (Glättung), wenn kein <c>bucket</c> mitgegeben wird. Der Verlauf gleitet, statt in feste
    /// Eimer zu springen; der letzte Punkt zeigt den aktuell laufenden Wert.
    /// </summary>
    public int UtilizationBucketMinutes { get; set; } = 5;

    /// <summary>Abtastschritt der Verlaufskurven in Minuten — ein Stützpunkt je Schritt (Standard <c>1</c>).</summary>
    public int UtilizationSeriesStepMinutes { get; set; } = 1;

    /// <summary>Richtwert in Einheiten pro Stunde, auf den sich die Auslastung in Prozent bezieht.</summary>
    public double UtilizationTargetUph { get; set; } = 200;

    /// <summary>
    /// Trailing-Fenster in Minuten, aus dem UPH und Prozent der Auslastung hochgerechnet werden
    /// (Standard: <c>15</c>). Klein = reagiert schnell auf kurze Stöße; groß = geglättet.
    /// </summary>
    public int UtilizationRateMinutes { get; set; } = 15;

    /// <summary>
    /// Ressourcenpunkte der Auslastungsauswertung, je Eintrag <c>{ "Name", "Group", "Label" }</c>.
    /// Leer ⇒ eingebaute Liste (<see cref="TelegramUtilization.DefaultResourcePoints"/>).
    /// </summary>
    public List<ResourcePointConfig> ResourcePoints { get; set; } = [];

    /// <summary>
    /// Reihenfolge der Gruppen im Auslastungs-Dashboard, z. B. <c>[ "Auslagerung RBG", "Fördertechnik" ]</c>.
    /// Nicht genannte Gruppen folgen in der Reihenfolge ihres ersten Auftretens in
    /// <see cref="ResourcePoints"/>. Leer ⇒ komplett nach erstem Auftreten.
    /// </summary>
    public List<string> GroupOrder { get; set; } = [];

    /// <summary>
    /// Klartext für Endziele (führendes Token des letzten 33er-Blocks, 4–5 Zeichen), z. B.
    /// <c>{ "GA51": "Kommissionierung" }</c> ⇒ Anzeige <c>GA51 (Kommissionierung)</c>. Ein Schlüssel
    /// mit <c>*</c> am Ende ist ein Präfixmuster: <c>{ "DLL*": "Auslagerung DLL" }</c> fasst alle
    /// <c>DLL…</c> zu einem Ziel zusammen. Unbekannte Ziele bleiben roh.
    /// </summary>
    public Dictionary<string, string> DestinationLabels { get; set; } = [];

    /// <summary>Rasterweite der UPH-Historie in Minuten (Standard <c>15</c>) — wie fein <c>/verlauf</c> auflöst.</summary>
    public int UphHistoryIntervalMinutes { get; set; } = 15;

    /// <summary>
    /// Aufbewahrung der UPH-Historie in Tagen (Standard <c>28</c> = 4 Wochen), getrennt von
    /// <see cref="RetentionDays"/> der Rohtelegramme. <c>0</c>/negativ = unbegrenzt.
    /// </summary>
    public int UphHistoryRetentionDays { get; set; } = 28;

    /// <summary>Wartezeit zwischen zwei Abfragen, wenn der Recorder aufgeholt hat.</summary>
    public int PollIntervalSeconds { get; set; } = 3;

    /// <summary>Zeilen pro Abfrage.</summary>
    public int BatchSize { get; set; } = 500;

    public FilterConfig Filter { get; set; } = new();

    /// <summary>
    /// Baut die Konfiguration in aufsteigender Priorität (später schlägt früher):
    /// <list type="number">
    ///   <item><c>appsettings.json</c> — neben der EXE, dann im aktuellen Verzeichnis</item>
    ///   <item><c>appsettings.local.json</c> — ebenda, für Zugangsdaten; gehört nicht ins Repo</item>
    ///   <item>eine per <c>--config</c> angegebene JSON-Datei</item>
    ///   <item>Umgebungsvariablen mit Präfix <c>KCC_</c> (z. B. <c>KCC_URL</c>, <c>KCC_PASSWORD</c>,
    ///         geschachtelt <c>KCC_Filter__MinDataLength</c>)</item>
    ///   <item>Kommandozeilen-Optionen (<c>--url</c>, <c>--user</c>, <c>--password</c>, <c>--db</c>,
    ///         <c>--poll-interval</c>, <c>--batch-size</c>, <c>--insecure</c>)</item>
    /// </list>
    /// </summary>
    public static KccConfig Load(CommandLine cli)
    {
        var builder = new ConfigurationBuilder();

        foreach (var dir in SearchDirectories())
        {
            builder.AddJsonFile(Path.Combine(dir, "appsettings.json"), optional: true, reloadOnChange: false);
            builder.AddJsonFile(Path.Combine(dir, "appsettings.local.json"), optional: true, reloadOnChange: false);
        }

        if (cli.GetString("config") is { } explicitPath)
            builder.AddJsonFile(Path.GetFullPath(explicitPath), optional: false, reloadOnChange: false);

        builder.AddEnvironmentVariables("KCC_");
        builder.AddInMemoryCollection(CommandLineOverrides(cli));

        var config = new KccConfig();
        builder.Build().Bind(config);
        return config;
    }

    /// <summary>EXE-Verzeichnis zuerst, dann das aktuelle Arbeitsverzeichnis (falls abweichend).</summary>
    static IEnumerable<string> SearchDirectories()
    {
        var beside = Path.GetFullPath(AppContext.BaseDirectory);
        yield return beside;

        var cwd = Path.GetFullPath(Directory.GetCurrentDirectory());
        if (!string.Equals(cwd, beside, StringComparison.OrdinalIgnoreCase))
            yield return cwd;
    }

    static IEnumerable<KeyValuePair<string, string?>> CommandLineOverrides(CommandLine cli)
    {
        if (cli.GetString("url") is { } url)
            yield return new("Url", url);
        if (cli.GetString("user") is { } user)
            yield return new("User", user);
        if (cli.GetString("password") is { } password)
            yield return new("Password", password);
        if (cli.GetString("db") is { } db)
            yield return new("Database", db);
        if (cli.GetInt("poll-interval") is { } poll)
            yield return new("PollIntervalSeconds", poll.ToString(CultureInfo.InvariantCulture));
        if (cli.GetInt("batch-size") is { } batch)
            yield return new("BatchSize", batch.ToString(CultureInfo.InvariantCulture));
        if (cli.HasFlag("insecure"))
            yield return new("AllowUntrustedCertificate", "true");
    }
}
