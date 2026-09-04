namespace Kcc.Recorder;

/// <summary>
/// Verdichtet die Rohtelegramme laufend zu <see cref="UphSampleRow"/>-Zeilen (Zeitraster ×
/// Ressourcenpunkt × Endziel) und hält sie mit eigener Aufbewahrung. So beantwortet
/// <c>/verlauf</c> Wochen-Zeiträume ohne Millionen Telegrammzeilen zu lesen.
///
/// Im Poll-Takt aufgerufen (<see cref="Tick"/>), rechnet aber höchstens alle
/// <c>UphHistoryIntervalMinutes</c>. Wiederholbar: es wird stets ab dem zuletzt verdichteten
/// Raster neu gerechnet, das dabei ggf. noch unvollständige jüngste Raster inklusive.
/// </summary>
public sealed class UphHistorySampler
{
    readonly TelegramStore _store;
    readonly TelegramFormat _format;
    readonly DestinationMap _destinations;
    readonly HashSet<string> _points;
    readonly int _intervalMinutes;
    readonly int _retentionDays;
    readonly Action<string> _log;

    DateTime _nextRun = DateTime.MinValue;
    DateTime _nextRetention = DateTime.MinValue;

    public UphHistorySampler(
        TelegramStore store,
        TelegramFormat format,
        IReadOnlyList<ResourcePointConfig> resourcePoints,
        IReadOnlyDictionary<string, string>? destinationLabels,
        int intervalMinutes,
        int retentionDays,
        Action<string> log)
    {
        _store = store;
        _format = format;
        _destinations = new DestinationMap(destinationLabels);
        var defs = resourcePoints is { Count: > 0 } ? resourcePoints : TelegramUtilization.DefaultResourcePoints;
        _points = defs
            .Select(p => p.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _intervalMinutes = Math.Max(1, intervalMinutes);
        _retentionDays = retentionDays;
        _log = log;
    }

    /// <summary>Im Poll-Takt aufgerufen; verdichtet und räumt höchstens im Intervall-Takt.</summary>
    public void Tick()
    {
        if (DateTime.UtcNow < _nextRun)
            return;
        _nextRun = DateTime.UtcNow.AddMinutes(_intervalMinutes);

        try
        {
            SampleNow();
            ApplyRetention();
        }
        catch (Exception ex)
        {
            _log($"UPH-Historie: {ex.Message}");
        }
    }

    /// <summary>Verdichtet ab dem zuletzt gespeicherten Raster bis zum letzten vollständigen Raster.</summary>
    public void SampleNow()
    {
        var newest = _store.MaxTelegramTime();
        if (newest is null)
            return;

        var step = TimeSpan.FromMinutes(_intervalMinutes);
        var completeUpTo = Floor(newest.Value, step);   // das laufende Raster ist noch unvollständig
        var start = _store.MaxUphBucket()
            ?? Floor(newest.Value.AddDays(-Math.Max(1, _retentionDays)), step);

        if (start >= completeUpTo)
            return;

        var rows = Aggregate(start, completeUpTo, step);
        _store.ReplaceUphSamplesFrom(start, rows);

        _log($"UPH-Historie: {rows.Count} Rasterzeilen ab {start:yyyy-MM-dd HH:mm} verdichtet " +
             $"(bis {completeUpTo:yyyy-MM-dd HH:mm}).");
    }

    void ApplyRetention()
    {
        if (_retentionDays <= 0 || DateTime.UtcNow < _nextRetention)
            return;
        _nextRetention = DateTime.UtcNow.AddHours(24);

        var newest = _store.MaxUphBucket() ?? _store.MaxTelegramTime();
        if (newest is null)
            return;

        var cutoff = Floor(newest.Value.AddDays(-_retentionDays), TimeSpan.FromMinutes(_intervalMinutes));
        var removed = _store.DeleteUphSamplesOlderThan(cutoff);
        if (removed > 0)
            _log($"UPH-Historie: {removed} Rasterzeilen vor {cutoff:yyyy-MM-dd} gelöscht " +
                 $"(Aufbewahrung {_retentionDays} Tage).");
    }

    List<UphSampleRow> Aggregate(DateTime from, DateTime to, TimeSpan step)
    {
        var messageCode = FieldIndex("MessageCode");
        var resourcePoint = FieldIndex("ResourcePoint");

        var acc = new Dictionary<(long Slot, string Point, string Destination), int>();

        foreach (var telegram in _store.Read(from, to))
        {
            if (telegram.DateTime < from || telegram.DateTime >= to)
                continue;

            var fields = _format.Slice(telegram.Data);
            if (!Eq(Field(fields, messageCode), TelegramUtilization.MessageCode))
                continue;

            var point = Field(fields, resourcePoint);
            if (point.Length == 0 || !_points.Contains(point))
                continue;

            var slot = (long)((telegram.DateTime - from).Ticks / step.Ticks);
            var dest = _destinations.CanonicalFromData(telegram.Data);
            var key = (slot, point, dest);
            acc[key] = acc.GetValueOrDefault(key) + 1;
        }

        return acc
            .Select(kv => new UphSampleRow
            {
                Bucket = from + TimeSpan.FromTicks(kv.Key.Slot * step.Ticks),
                ResourcePoint = kv.Key.Point,
                Destination = kv.Key.Destination,
                Orders = kv.Value,
            })
            .OrderBy(r => r.Bucket)
            .ToList();
    }

    static DateTime Floor(DateTime value, TimeSpan step) =>
        new(value.Ticks - value.Ticks % step.Ticks, DateTimeKind.Unspecified);

    static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    static string Field(IReadOnlyList<string> fields, int index) =>
        index >= 0 && index < fields.Count ? fields[index].Trim() : "";

    int FieldIndex(string name)
    {
        for (var i = 0; i < _format.Fields.Count; i++)
        {
            if (string.Equals(_format.Fields[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }
}
