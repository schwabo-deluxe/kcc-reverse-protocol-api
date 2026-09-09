namespace Kcc.Recorder;

/// <summary>
/// Eine verdichtete Rasterzeile der Ressourcenpunkt-Langzeitaufzeichnung: belegte Zeit und
/// Auftragsmenge eines in <c>appsettings.json</c> angelegten Ressourcenpunkts in einem
/// Zeitraster. Quelle für den zweiten Chart auf <c>/verlauf</c> (Belegung &amp; Leistung),
/// auch für Fördertechnikpunkte ohne RBG-Verbindung — für die es sonst keine Historie gibt.
/// Nur die konfigurierten Ressourcenpunkte werden aufgezeichnet.
/// </summary>
public sealed class PointSampleRow
{
    public required DateTime Bucket { get; init; }
    public required string ResourcePoint { get; init; }

    /// <summary>Belegte Zeit im Raster in Sekunden (<c>ENDTSP</c>→<c>RPFREE</c>, den berührten Rastern anteilig zugeschlagen).</summary>
    public double BusySeconds { get; set; }

    /// <summary>Transportaufträge (<c>TSPORD</c>) im Raster.</summary>
    public int Orders { get; set; }
}

/// <summary>Ein Stützpunkt der Punkt-Langzeitkurve: je Ressourcenpunkt Belegungs- und Leistungsgrad in Prozent.</summary>
public sealed record PointHistoryBucket
{
    public required DateTime At { get; init; }

    /// <summary>Zeitbasierter Belegungsgrad je Ressourcenpunkt: belegte Zeit ÷ Rasterdauer, auf 100 begrenzt.</summary>
    public required IReadOnlyDictionary<string, double> BusyPercent { get; init; }

    /// <summary>Mengenbasierter Leistungsgrad je Ressourcenpunkt: erreichte UPH ÷ Richtwert.</summary>
    public required IReadOnlyDictionary<string, double> LoadPercent { get; init; }

    /// <summary>Transportaufträge im Raster je Ressourcenpunkt.</summary>
    public required IReadOnlyDictionary<string, int> Orders { get; init; }
}

/// <summary>Summenzeile eines Ressourcenpunkts über den ganzen Zeitraum.</summary>
public sealed record PointHistorySeries
{
    public required string ResourcePoint { get; init; }
    public required string Label { get; init; }
    public required double TargetUph { get; init; }
    public required int Orders { get; init; }
    public required double AvgBusyPercent { get; init; }
    public required double AvgLoadPercent { get; init; }
    public required double BusyHours { get; init; }
}

/// <summary>
/// Reine Auswertung der Ressourcenpunkt-Langzeitreihe über einem Zeitfenster — verdichtet die
/// gespeicherten Rasterzeilen (<see cref="PointSampleRow"/>) auf ein Anzeigeraster und rechnet
/// belegte Zeit in Belegungsgrad, Auftragsmenge in Leistungsgrad um.
/// </summary>
public sealed record PointHistoryReport
{
    public required DateTime From { get; init; }
    public required DateTime To { get; init; }
    public required int BucketMinutes { get; init; }

    /// <summary>Gefilterter Ressourcenpunkt oder <c>null</c> für „alle".</summary>
    public required string? ResourcePoint { get; init; }

    /// <summary>Ressourcenpunkte, die im Fenster vorkommen (aufsteigend).</summary>
    public required IReadOnlyList<string> ResourcePoints { get; init; }

    public required IReadOnlyList<PointHistoryBucket> Buckets { get; init; }
    public required IReadOnlyList<PointHistorySeries> Totals { get; init; }

    /// <summary>Betriebsstunden im Zeitraum — Nenner der Durchschnitte, sonst die Kalenderdauer.</summary>
    public required double OperatingHours { get; init; }

    public static PointHistoryReport Compute(
        IReadOnlyList<PointSampleRow> rows,
        DateTime from,
        DateTime to,
        int bucketMinutes,
        IReadOnlyList<ResourcePointConfig>? resourcePoints = null,
        double defaultTargetUph = 0,
        string? resourcePoint = null,
        OperatingHoursConfig? operatingHours = null)
    {
        var defs = (resourcePoints ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        string LabelOf(string rp) => defs.TryGetValue(rp, out var d) ? d.DisplayLabel : rp;
        double TargetOf(string rp) =>
            defs.TryGetValue(rp, out var d) && d.TargetUph is { } t && t > 0 ? t : defaultTargetUph;

        var step = Math.Max(1, bucketMinutes);
        if (to <= from)
            to = from.AddMinutes(step);

        var rp = string.IsNullOrWhiteSpace(resourcePoint) ? null : resourcePoint.Trim();
        var scoped = rows
            .Where(r => r.Bucket >= from && r.Bucket < to)
            .Where(r => rp is null || string.Equals(r.ResourcePoint, rp, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var count = Math.Max(1, (int)Math.Ceiling((to - from).TotalMinutes / step));
        var slotBusy = new Dictionary<string, double>[count];
        var slotOrders = new Dictionary<string, int>[count];
        for (var i = 0; i < count; i++)
        {
            slotBusy[i] = new Dictionary<string, double>(StringComparer.Ordinal);
            slotOrders[i] = new Dictionary<string, int>(StringComparer.Ordinal);
        }

        var points = new SortedSet<string>(StringComparer.Ordinal);
        var sumBusy = new Dictionary<string, double>(StringComparer.Ordinal);
        var sumOrders = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var r in scoped)
        {
            var slot = (int)((r.Bucket - from).TotalMinutes / step);
            if (slot < 0 || slot >= count)
                continue;
            slotBusy[slot][r.ResourcePoint] = slotBusy[slot].GetValueOrDefault(r.ResourcePoint) + r.BusySeconds;
            slotOrders[slot][r.ResourcePoint] = slotOrders[slot].GetValueOrDefault(r.ResourcePoint) + r.Orders;
            sumBusy[r.ResourcePoint] = sumBusy.GetValueOrDefault(r.ResourcePoint) + r.BusySeconds;
            sumOrders[r.ResourcePoint] = sumOrders.GetValueOrDefault(r.ResourcePoint) + r.Orders;
            points.Add(r.ResourcePoint);
        }

        var slotSeconds = step * 60.0;
        var slotHours = step / 60.0;
        var buckets = new PointHistoryBucket[count];
        for (var i = 0; i < count; i++)
        {
            var busyPct = new Dictionary<string, double>(StringComparer.Ordinal);
            var loadPct = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var (p, sec) in slotBusy[i])
                busyPct[p] = Math.Round(Math.Min(100, sec / slotSeconds * 100), 1);
            foreach (var (p, ord) in slotOrders[i])
            {
                var target = TargetOf(p);
                loadPct[p] = target > 0 ? Math.Round(ord / slotHours / target * 100, 1) : 0;
            }
            buckets[i] = new PointHistoryBucket
            {
                At = from.AddMinutes(i * step),
                BusyPercent = busyPct,
                LoadPercent = loadPct,
                Orders = slotOrders[i],
            };
        }

        // Nenner der Durchschnitte: Betriebszeit statt Kalenderzeit, wenn eine Hauptnutzungszeit
        // gilt. Bewegung = jedes Raster mit Auftrag oder Belegzeit, über alle Punkte.
        var activity = scoped.Where(r => r.Orders > 0 || r.BusySeconds > 0).Select(r => r.Bucket);
        var windowSeconds = Math.Max(1e-9, OperatingWindow.EffectiveSeconds(from, to, activity, operatingHours));
        var windowHours = Math.Max(1e-9, windowSeconds / 3600.0);
        var totals = points.Select(p =>
        {
            var target = TargetOf(p);
            return new PointHistorySeries
            {
                ResourcePoint = p,
                Label = LabelOf(p),
                TargetUph = target,
                Orders = sumOrders.GetValueOrDefault(p),
                AvgBusyPercent = Math.Round(Math.Min(100, sumBusy.GetValueOrDefault(p) / windowSeconds * 100), 1),
                AvgLoadPercent = target > 0
                    ? Math.Round(sumOrders.GetValueOrDefault(p) / windowHours / target * 100, 1)
                    : 0,
                BusyHours = Math.Round(sumBusy.GetValueOrDefault(p) / 3600.0, 2),
            };
        }).ToList();

        return new PointHistoryReport
        {
            From = from,
            To = to,
            BucketMinutes = step,
            ResourcePoint = rp,
            ResourcePoints = points.ToList(),
            Buckets = buckets,
            Totals = totals,
            OperatingHours = Math.Round(windowSeconds / 3600.0, 2),
        };
    }
}
