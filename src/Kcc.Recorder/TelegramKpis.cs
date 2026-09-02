namespace Kcc.Recorder;

/// <summary>
/// Kennzahlen über ein Zeitfenster aufgezeichneter Telegramme — bewusst als reine Funktion,
/// damit sie sowohl die API als auch Tests ohne Datenbank berechnen können.
/// </summary>
public sealed record TelegramKpis
{
    public required int WindowMinutes { get; init; }
    public required DateTime From { get; init; }
    public required DateTime To { get; init; }

    public required int Count { get; init; }
    public required double PerMinute { get; init; }

    public required long? LatestId { get; init; }
    public required DateTime? LatestAt { get; init; }

    /// <summary>Sekunden seit dem letzten Schreibvorgang — zeigt, ob der Recorder noch läuft.</summary>
    public required double? LagSeconds { get; init; }

    public required int DistinctConnections { get; init; }

    /// <summary>Telegramme mit gesetztem, von Null verschiedenem <c>ErrorCode</c>-Feld.</summary>
    public required int Errors { get; init; }

    public required IReadOnlyDictionary<string, int> ByDirection { get; init; }
    public required IReadOnlyDictionary<string, int> ByConnection { get; init; }
    public required IReadOnlyDictionary<string, int> ByMessageCode { get; init; }

    /// <param name="from">Linker Rand des Fensters (in der Zeit-Basis der gespeicherten Telegramme).</param>
    /// <param name="to">Rechter Rand — üblicherweise der Zeitstempel des jüngsten Telegramms.</param>
    /// <param name="lagSeconds">Sekunden seit dem letzten Schreibvorgang; von der API durchgereicht.</param>
    public static TelegramKpis Compute(
        IReadOnlyList<Telegram> window, TelegramFormat format, int windowMinutes,
        DateTime from, DateTime to, double? lagSeconds)
    {
        var index = FieldIndex(format);
        var sliced = window
            .Select(t => (Telegram: t, Fields: format.Slice(t.Data)))
            .ToList();

        string Field(IReadOnlyList<string> fields, string name) =>
            index.TryGetValue(name, out var i) && i < fields.Count ? fields[i] : "";

        var latest = window.Count > 0 ? window[^1] : null;

        return new TelegramKpis
        {
            WindowMinutes = windowMinutes,
            From = from,
            To = to,
            Count = window.Count,
            PerMinute = windowMinutes > 0 ? Math.Round((double)window.Count / windowMinutes, 2) : 0,
            LatestId = latest?.Id,
            LatestAt = latest?.DateTime,
            LagSeconds = lagSeconds,
            DistinctConnections = window
                .Select(t => t.ConnectionName ?? "")
                .Where(c => c.Length > 0)
                .Distinct()
                .Count(),
            Errors = sliced.Count(s =>
            {
                var code = Field(s.Fields, "ErrorCode").Trim();
                return code.Length > 0 && !RecordFilter.IsAllZero(code);
            }),
            ByDirection = window
                .GroupBy(t => t.TelegramDirection.ToString())
                .OrderByDescending(g => g.Count())
                .ToDictionary(g => g.Key, g => g.Count()),
            ByConnection = window
                .Where(t => !string.IsNullOrEmpty(t.ConnectionName))
                .GroupBy(t => t.ConnectionName!)
                .OrderByDescending(g => g.Count())
                .Take(10)
                .ToDictionary(g => g.Key, g => g.Count()),
            ByMessageCode = sliced
                .Select(s => Field(s.Fields, "MessageCode").Trim())
                .Where(c => c.Length > 0)
                .GroupBy(c => c)
                .OrderByDescending(g => g.Count())
                .Take(10)
                .ToDictionary(g => g.Key, g => g.Count()),
        };
    }

    static Dictionary<string, int> FieldIndex(TelegramFormat format)
    {
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < format.Fields.Count; i++)
            index[format.Fields[i].Name] = i;
        return index;
    }
}
