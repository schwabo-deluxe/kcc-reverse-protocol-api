namespace Kcc.Recorder;

/// <summary>Auslastung eines Ressourcenpunkts über das betrachtete Zeitfenster.</summary>
public sealed record ResourcePointUtilization
{
    public required string ResourcePoint { get; init; }

    /// <summary>Anzahl der <c>TSPORD</c>-Telegramme dieses Punkts im Fenster.</summary>
    public required int Count { get; init; }

    /// <summary>Auf eine Stunde hochgerechnete Menge (Units per Hour).</summary>
    public required double Uph { get; init; }

    /// <summary>Anteil an <see cref="TelegramUtilization.TargetUph"/> in Prozent.</summary>
    public required double Percent { get; init; }

    public required int Errors { get; init; }
    public required DateTime? LatestAt { get; init; }
}

/// <summary>
/// Auslastung je Ressourcenpunkt, gemessen an den Auftragstelegrammen (<c>MessageCode</c>
/// <c>TSPORD</c>). Reine Funktion über einem Zeitfenster — die API und die Tests rechnen damit
/// gleichermaßen, ohne Datenbank.
/// </summary>
public sealed record TelegramUtilization
{
    /// <summary>Auf diesen <c>MessageCode</c> stützt sich die Messung.</summary>
    public const string MessageCode = "TSPORD";

    /// <summary>Ressourcenpunkte, die das Dashboard standardmäßig ausweist.</summary>
    public static readonly IReadOnlyList<string> DefaultResourcePoints =
    [
        "DA21", "LB41", "EA21", "LD51", "MFA1", "MA72", "MB72", "MC72", "MD72", "ME71", "EB31",
    ];

    public required int WindowMinutes { get; init; }
    public required DateTime From { get; init; }
    public required DateTime To { get; init; }

    /// <summary>Richtwert in Einheiten pro Stunde, auf den sich die Prozentwerte beziehen.</summary>
    public required double TargetUph { get; init; }

    /// <summary>Alle <c>TSPORD</c>-Telegramme im Fenster — auch die ohne gelisteten Punkt.</summary>
    public required int TotalOrders { get; init; }

    public required IReadOnlyList<ResourcePointUtilization> Points { get; init; }

    public static TelegramUtilization Compute(
        IReadOnlyList<Telegram> window,
        TelegramFormat format,
        int windowMinutes,
        double targetUph,
        DateTime now,
        IReadOnlyList<string>? resourcePoints = null)
    {
        var points = resourcePoints is { Count: > 0 } ? resourcePoints : DefaultResourcePoints;
        var messageCode = FieldIndex(format, "MessageCode");
        var resourcePoint = FieldIndex(format, "ResourcePoint");
        var errorCode = FieldIndex(format, "ErrorCode");

        var stats = points.ToDictionary(
            p => p,
            _ => (Count: 0, Errors: 0, Latest: (DateTime?)null),
            StringComparer.OrdinalIgnoreCase);

        var orders = 0;
        foreach (var telegram in window)
        {
            var fields = format.Slice(telegram.Data);
            if (!string.Equals(Field(fields, messageCode), MessageCode, StringComparison.OrdinalIgnoreCase))
                continue;

            orders++;

            var point = Field(fields, resourcePoint);
            if (point.Length == 0 || !stats.TryGetValue(point, out var current))
                continue;

            var error = Field(fields, errorCode);
            stats[point] = (
                current.Count + 1,
                current.Errors + (error.Length > 0 && !RecordFilter.IsAllZero(error) ? 1 : 0),
                current.Latest is { } latest && latest > telegram.DateTime ? latest : telegram.DateTime);
        }

        var hours = windowMinutes / 60d;
        return new TelegramUtilization
        {
            WindowMinutes = windowMinutes,
            From = now.AddMinutes(-windowMinutes),
            To = now,
            TargetUph = targetUph,
            TotalOrders = orders,
            Points = points.Select(p =>
            {
                var s = stats[p];
                var uph = hours > 0 ? s.Count / hours : 0;
                return new ResourcePointUtilization
                {
                    ResourcePoint = p,
                    Count = s.Count,
                    Uph = Math.Round(uph, 1),
                    Percent = targetUph > 0 ? Math.Round(uph / targetUph * 100, 1) : 0,
                    Errors = s.Errors,
                    LatestAt = s.Latest,
                };
            }).ToList(),
        };
    }

    static string Field(IReadOnlyList<string> fields, int index) =>
        index >= 0 && index < fields.Count ? fields[index].Trim() : "";

    static int FieldIndex(TelegramFormat format, string name)
    {
        for (var i = 0; i < format.Fields.Count; i++)
        {
            if (string.Equals(format.Fields[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }
}
