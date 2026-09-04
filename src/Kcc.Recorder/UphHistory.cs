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

/// <summary>Ein Zeitraster der Historie mit Gesamtmenge und Aufschlüsselung je Endziel.</summary>
public sealed record UphHistoryBucket
{
    public required DateTime At { get; init; }
    public required int Total { get; init; }
    public required double Uph { get; init; }
    public required IReadOnlyDictionary<string, int> Orders { get; init; }
    public required IReadOnlyDictionary<string, double> Uph2 { get; init; }
}

/// <summary>Summe eines Endziels über das Historienfenster: Menge, Ø UPH und Anteil.</summary>
public sealed record UphHistoryDestination
{
    public required string Target { get; init; }
    public required string Label { get; init; }
    public required int Orders { get; init; }

    /// <summary>Durchschnittliche UPH über das ganze Fenster (Menge ÷ Fensterstunden).</summary>
    public required double AvgUph { get; init; }

    /// <summary>Anteil an allen Aufträgen des Fensters in Prozent.</summary>
    public required double Share { get; init; }
}

/// <summary>
/// Reine Auswertung der UPH-Historie über einem Zeitfenster — verdichtet die gespeicherten
/// Rasterzeilen auf ein (gröberes) Anzeigeraster und rechnet Mengen in UPH um.
/// </summary>
public sealed record UphHistoryReport
{
    public required DateTime From { get; init; }
    public required DateTime To { get; init; }
    public required int BucketMinutes { get; init; }

    /// <summary>Gefilterter Ressourcenpunkt oder <c>null</c> für „alle".</summary>
    public required string? ResourcePoint { get; init; }

    /// <summary>Ressourcenpunkte, die im Fenster vorkommen (aufsteigend).</summary>
    public required IReadOnlyList<string> ResourcePoints { get; init; }

    /// <summary>Endziele im Fenster, absteigend nach Menge — die Reihenfolge der Bänder.</summary>
    public required IReadOnlyList<string> Destinations { get; init; }

    public required IReadOnlyList<UphHistoryBucket> Buckets { get; init; }
    public required IReadOnlyList<UphHistoryDestination> Totals { get; init; }
    public required int TotalOrders { get; init; }

    /// <summary>Platzhalter-Schlüssel für Telegramme ohne erkanntes Endziel.</summary>
    public const string NoDestination = "";

    public static UphHistoryReport Compute(
        IReadOnlyList<UphSampleRow> rows,
        DateTime from,
        DateTime to,
        int bucketMinutes,
        IReadOnlyDictionary<string, string>? destinationLabels = null,
        string? resourcePoint = null)
    {
        var map = new DestinationMap(destinationLabels);

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
        var slotByDest = new Dictionary<string, int>[count];
        for (var i = 0; i < count; i++)
            slotByDest[i] = new Dictionary<string, int>(StringComparer.Ordinal);

        var destTotals = new Dictionary<string, int>(StringComparer.Ordinal);
        var points = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var r in scoped)
        {
            var slot = (int)((r.Bucket - from).TotalMinutes / step);
            if (slot < 0 || slot >= count)
                continue;

            slotOrders[slot] += r.Orders;
            slotByDest[slot][r.Destination] = slotByDest[slot].GetValueOrDefault(r.Destination) + r.Orders;
            destTotals[r.Destination] = destTotals.GetValueOrDefault(r.Destination) + r.Orders;
            points.Add(r.ResourcePoint);
        }

        var totalOrders = destTotals.Values.Sum();
        var bucketHours = step / 60d;
        var windowHours = Math.Max(1e-9, (to - from).TotalHours);

        var destOrder = destTotals
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Key)
            .ToList();

        var buckets = new UphHistoryBucket[count];
        for (var i = 0; i < count; i++)
        {
            var orders = slotByDest[i];
            buckets[i] = new UphHistoryBucket
            {
                At = from.AddMinutes(i * step),
                Total = slotOrders[i],
                Uph = Math.Round(slotOrders[i] / bucketHours, 1),
                Orders = orders,
                Uph2 = orders.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value / bucketHours, 1), StringComparer.Ordinal),
            };
        }

        var totals = destOrder.Select(d => new UphHistoryDestination
        {
            Target = d,
            Label = map.Label(d),
            Orders = destTotals[d],
            AvgUph = Math.Round(destTotals[d] / windowHours, 1),
            Share = totalOrders > 0 ? Math.Round(destTotals[d] * 100.0 / totalOrders, 1) : 0,
        }).ToList();

        return new UphHistoryReport
        {
            From = from,
            To = to,
            BucketMinutes = step,
            ResourcePoint = rp,
            ResourcePoints = points.ToList(),
            Destinations = destOrder,
            Buckets = buckets,
            Totals = totals,
            TotalOrders = totalOrders,
        };
    }
}
