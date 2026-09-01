using System.Text.Json;
using System.Text.Json.Serialization;

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

/// <summary>Gesamtkonfiguration; wird aus kcc.json gelesen und von CLI/Umgebung überschrieben.</summary>
public sealed class KccConfig
{
    public string Url { get; set; } = "wss://10.20.220.33/ws";
    public string? User { get; set; }
    public string? Password { get; set; }

    /// <summary>Erlaubt selbstsignierte Zertifikate der Anlage.</summary>
    public bool AllowUntrustedCertificate { get; set; }

    public string Database { get; set; } = "kcc-telegrams.db";

    /// <summary>Wartezeit zwischen zwei Abfragen, wenn der Recorder aufgeholt hat.</summary>
    public int PollIntervalSeconds { get; set; } = 3;

    /// <summary>Zeilen pro Abfrage.</summary>
    public int BatchSize { get; set; } = 500;

    public FilterConfig Filter { get; set; } = new();

    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Lädt die Konfiguration in aufsteigender Priorität: Datei → Umgebungsvariablen → CLI-Argumente.
    /// Ohne Pfadangabe wird kcc.json neben der ausführbaren Datei gesucht.
    /// </summary>
    public static KccConfig Load(CommandLine cli)
    {
        var path = cli.GetString("config") ?? DefaultConfigPath();
        var config = File.Exists(path)
            ? JsonSerializer.Deserialize<KccConfig>(File.ReadAllText(path), Options) ?? new KccConfig()
            : new KccConfig();

        config.Url = Environment.GetEnvironmentVariable("KCC_URL") ?? config.Url;
        config.User = Environment.GetEnvironmentVariable("KCC_USER") ?? config.User;
        config.Password = Environment.GetEnvironmentVariable("KCC_PASSWORD") ?? config.Password;
        config.Database = Environment.GetEnvironmentVariable("KCC_DATABASE") ?? config.Database;

        config.Url = cli.GetString("url") ?? config.Url;
        config.User = cli.GetString("user") ?? config.User;
        config.Password = cli.GetString("password") ?? config.Password;
        config.Database = cli.GetString("db") ?? config.Database;
        config.AllowUntrustedCertificate = cli.HasFlag("insecure") || config.AllowUntrustedCertificate;

        if (cli.GetInt("poll-interval") is { } poll)
            config.PollIntervalSeconds = poll;
        if (cli.GetInt("batch-size") is { } batch)
            config.BatchSize = batch;

        return config;
    }

    static string DefaultConfigPath()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "kcc.json");
        return File.Exists(beside) ? beside : "kcc.json";
    }
}
