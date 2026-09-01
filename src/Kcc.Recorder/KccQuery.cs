using System.Text.Json;

namespace Kcc.Recorder;

/// <summary>
/// Vergleichsoperatoren der Server-Query
/// (MCC.ControlPanel.SharedClasses.Controls.FilterDataGrid.FilterDataGridFilterType).
/// </summary>
public static class FilterType
{
    public const string IsEqual = "isequal";
    public const string IsNotEqual = "isnotequal";
    public const string Contains = "contains";
    public const string ContainsIgnoreCase = "containsignorecase";
    public const string StartsWith = "startswith";
    public const string EndsWith = "endswith";
    public const string Like = "like";
    public const string GreaterThan = "greaterthan";
    public const string GreaterThanOrEquals = "greaterthanorequals";
    public const string LessThan = "lessthan";
    public const string LessThanOrEquals = "lessthanorequals";
    public const string IsNull = "isnull";
    public const string IsNotNull = "isnotnull";
}

/// <summary>Eine Filterbedingung, so wie sie dist/query.js aufbaut.</summary>
public sealed class QueryFilter
{
    public string FilterField { get; set; } = "";
    public string FilterType { get; set; } = "";
    public object? Filter { get; set; }
    public bool OrConnection { get; set; }
    public int Level { get; set; }

    /// <summary>Der .NET-Typname des Feldes; der Server nutzt ihn, um den Filterwert zu konvertieren.</summary>
    public string? filterValueType { get; set; }
}

/// <summary>Sortierung; die Feldnamen sind bewusst klein geschrieben — so sendet sie der Client.</summary>
public sealed class QueryOrderBy
{
    public string column { get; set; } = "";
    public string sort { get; set; } = "asc";
}

/// <summary>
/// Baut und sendet Query- bzw. QueryCount-Aufrufe.
/// Nachbau von dist/query.js: optionale Parameter werden nur gesetzt, wenn sie befüllt sind.
/// </summary>
public sealed class KccQuery
{
    readonly KccConnection _connection;
    readonly KccSession _session;

    public KccQuery(KccConnection connection, KccSession session)
    {
        _connection = connection;
        _session = session;
    }

    public Task<IReadOnlyList<Telegram>> QueryTelegramsAsync(
        IReadOnlyList<QueryFilter>? filters,
        IReadOnlyList<QueryOrderBy>? orderBys,
        int? take,
        int? skip,
        CancellationToken ct) =>
        QueryAsync(Telegram.DtoType, filters, orderBys, take, skip, Telegram.FromJson, ct);

    public async Task<IReadOnlyList<T>> QueryAsync<T>(
        string dtoType,
        IReadOnlyList<QueryFilter>? filters,
        IReadOnlyList<QueryOrderBy>? orderBys,
        int? take,
        int? skip,
        Func<JsonElement, T> map,
        CancellationToken ct)
    {
        var response = await SendAsync("Query", dtoType, filters, orderBys, take, skip, ct);

        // Query liefert das DTO-Array direkt als Response, ohne Data-Hülle.
        if (response.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<T>(response.GetArrayLength());
        foreach (var item in response.EnumerateArray())
            result.Add(map(item));
        return result;
    }

    public async Task<long> CountAsync(
        string dtoType,
        IReadOnlyList<QueryFilter>? filters,
        CancellationToken ct)
    {
        var response = await SendAsync("QueryCount", dtoType, filters, null, null, null, ct);
        return response.ValueKind == JsonValueKind.Number ? response.GetInt64() : 0;
    }

    Task<JsonElement> SendAsync(
        string functionName,
        string dtoType,
        IReadOnlyList<QueryFilter>? filters,
        IReadOnlyList<QueryOrderBy>? orderBys,
        int? take,
        int? skip,
        CancellationToken ct)
    {
        var session = _session.SessionInformation
                      ?? throw new InvalidOperationException("Nicht angemeldet.");

        var parameters = new Dictionary<string, object?>
        {
            ["Type"] = dtoType,
            ["OrderBys"] = orderBys ?? [],
            ["SessionInformation"] = session,
            ["Parameters"] = Array.Empty<object>(),
        };

        if (filters is { Count: > 0 })
            parameters["Filters"] = filters;
        if (take.HasValue)
            parameters["Take"] = take.Value;
        if (skip.HasValue)
            parameters["Skip"] = skip.Value;

        return _connection.CallAsync("VisuService", functionName, parameters, ct);
    }
}
