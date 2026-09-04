namespace Kcc.Recorder;

/// <summary>
/// Eine verdichtete Zeile der UPH-Historie: die Zahl der <c>TSPORD</c>-Aufträge in einem
/// Zeitraster, aufgeschlüsselt nach Ressourcenpunkt und Endziel. Wird laufend aus den
/// Rohtelegrammen gebildet (<see cref="UphHistorySampler"/>) und mit eigener Aufbewahrung
/// gehalten, damit <c>/verlauf</c> auch über Wochen schnell antwortet.
/// </summary>
public sealed record UphSampleRow
{
    /// <summary>Rasteranfang, zeitzonenfrei in Anlagenzeit (wie Spalte <c>DateTime</c>).</summary>
    public required DateTime Bucket { get; init; }

    public required string ResourcePoint { get; init; }

    /// <summary>Endziel (4–5 Zeichen), <c>""</c> wenn keins erkannt wurde.</summary>
    public required string Destination { get; init; }

    public required int Orders { get; init; }
}

/// <summary>Wonach die Historie gestapelt wird.</summary>
public enum UphHistoryGroupBy
{
    /// <summary>Ein Band je Endziel.</summary>
    Destination,

    /// <summary>Ein Band je Ressourcenpunkt.</summary>
    ResourcePoint,
}

/// <summary>Ein Zeitraster der Historie mit Gesamtmenge und Aufschlüsselung je Reihe.</summary>
public sealed record UphHistoryBucket
{
    public required DateTime At { get; init; }
    public required int Total { get; init; }
    public required double Uph { get; init; }

    /// <summary>Menge je Reihenschlüssel.</summary>
    public required IReadOnlyDictionary<string, int> Orders { get; init; }

    /// <summary>UPH je Reihenschlüssel.</summary>
    public required IReadOnlyDictionary<string, double> Series { get; init; }
}

/// <summary>Summe einer Reihe (Endziel oder Ressourcenpunkt) über das Fenster.</summary>
public sealed record UphHistorySeries
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required int Orders { get; init; }

    /// <summary>Durchschnittliche UPH über das ganze Fenster (Menge ÷ Fensterstunden).</summary>
    public required double AvgUph { get; init; }

    /// <summary>Anteil an allen Aufträgen des Fensters in Prozent.</summary>
    public required double Share { get; init; }
}

/// <summary>
/// Reine Auswertung der UPH-Historie über einem Zeitfenster — verdichtet die gespeicherten
/// Rasterzeilen auf ein (gröberes) Anzeigeraster und rechnet Mengen in UPH um. Gestapelt
/// wird wahlweise nach Endziel oder nach Ressourcenpunkt (<see cref="UphHistoryGroupBy"/>).
/// </summary>
public sealed record UphHistoryReport
{
    public required DateTime From { get; init; }
    public required DateTime To { get; init; }
    public required int BucketMinutes { get; init; }

    /// <summary>Aufteilung der Bänder: <c>destination</c> oder <c>resourcePoint</c>.</summary>
    public required string GroupBy { get; init; }

    /// <summary>Gefilterter Ressourcenpunkt oder <c>null</c> für „alle".</summary>
    public required string? ResourcePoint { get; init; }

    /// <summary>Ressourcenpunkte, die im Fenster vorkommen (aufsteigend) — für die Auswahl.</summary>
    public required IReadOnlyList<string> ResourcePoints { get; init; }

    /// <summary>Reihenschlüssel im Fenster, absteigend nach Menge — die Reihenfolge der Bänder.</summary>
    public required IReadOnlyList<string> Keys { get; init; }

    public required IReadOnlyList<UphHistoryBucket> Buckets { get; init; }
    public required IReadOnlyList<UphHistorySeries> Totals { get; init; }
    public required int TotalOrders { get; init; }

    public static UphHistoryReport Compute(
        IReadOnlyList<UphSampleRow> rows,
        DateTime from,
        DateTime to,
        int bucketMinutes,
        UphHistoryGroupBy groupBy = UphHistoryGroupBy.Destination,
        IReadOnlyDictionary<string, string>? destinationLabels = null,
        IReadOnlyList<ResourcePointConfig>? resourcePoints = null,
        string? resourcePoint = null)
    {
        var map = new DestinationMap(destinationLabels);
        var rpLabels = (resourcePoints ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().DisplayLabel, StringComparer.OrdinalIgnoreCase);

        var byResourcePoint = groupBy == UphHistoryGroupBy.ResourcePoint;
        Func<UphSampleRow, string> keyOf = byResourcePoint ? r => r.ResourcePoint : r => r.Destination;
        Func<string, string> labelOf = byResourcePoint
            ? k => rpLabels.GetValueOrDefault(k, k)
            : map.Label;

        var step = Math.Max(1, bucketMinutes);
        if (to <= from)
            to = from.AddMinutes(step);

        var rp = string.IsNullOrWhiteSpace(resourcePoint) ? null : resourcePoint.Trim();
        var scoped = rows
            .Where(r => r.Bucket >= from && r.Bucket < to)
            .Where(r => rp is null || string.Equals(r.ResourcePoint, rp, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var count = Math.Max(1, (int)Math.Ceiling((to - from).TotalMinutes / step));
        var slotOrders = new int[count];
        var slotByKey = new Dictionary<string, int>[count];
        for (var i = 0; i < count; i++)
            slotByKey[i] = new Dictionary<string, int>(StringComparer.Ordinal);

        var keyTotals = new Dictionary<string, int>(StringComparer.Ordinal);
        var points = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var r in scoped)
        {
            var slot = (int)((r.Bucket - from).TotalMinutes / step);
            if (slot < 0 || slot >= count)
                continue;

            var key = keyOf(r);
            slotOrders[slot] += r.Orders;
            slotByKey[slot][key] = slotByKey[slot].GetValueOrDefault(key) + r.Orders;
            keyTotals[key] = keyTotals.GetValueOrDefault(key) + r.Orders;
            points.Add(r.ResourcePoint);
        }

        var totalOrders = keyTotals.Values.Sum();
        var bucketHours = step / 60d;
        var windowHours = Math.Max(1e-9, (to - from).TotalHours);

        var keyOrder = keyTotals
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Key)
            .ToList();

        var buckets = new UphHistoryBucket[count];
        for (var i = 0; i < count; i++)
        {
            var orders = slotByKey[i];
            buckets[i] = new UphHistoryBucket
            {
                At = from.AddMinutes(i * step),
                Total = slotOrders[i],
                Uph = Math.Round(slotOrders[i] / bucketHours, 1),
                Orders = orders,
                Series = orders.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value / bucketHours, 1), StringComparer.Ordinal),
            };
        }

        var totals = keyOrder.Select(k => new UphHistorySeries
        {
            Key = k,
            Label = labelOf(k),
            Orders = keyTotals[k],
            AvgUph = Math.Round(keyTotals[k] / windowHours, 1),
            Share = totalOrders > 0 ? Math.Round(keyTotals[k] * 100.0 / totalOrders, 1) : 0,
        }).ToList();

        return new UphHistoryReport
        {
            From = from,
            To = to,
            BucketMinutes = step,
            GroupBy = byResourcePoint ? "resourcePoint" : "destination",
            ResourcePoint = rp,
            ResourcePoints = points.ToList(),
            Keys = keyOrder,
            Buckets = buckets,
            Totals = totals,
            TotalOrders = totalOrders,
        };
    }
}
