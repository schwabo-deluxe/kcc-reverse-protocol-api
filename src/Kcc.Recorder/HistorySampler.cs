namespace Kcc.Recorder;

/// <summary>
/// Verdichtet die Rohtelegramme laufend zu zwei Langzeitreihen und hält beide mit eigener
/// Aufbewahrung — so beantworten die Ansichten Wochen- und Monatszeiträume, ohne Millionen
/// Telegrammzeilen zu lesen, und überleben das Löschen der Rohtelegramme:
/// <list type="bullet">
///   <item><see cref="UphSampleRow"/> (Zeitraster × Ressourcenpunkt × Endziel) für <c>/verlauf</c></item>
///   <item><see cref="RbgSampleRow"/> (Zeitraster × RBG-Verbindung) für <c>/rbg</c></item>
/// </list>
///
/// Im Poll-Takt aufgerufen (<see cref="Tick"/>), rechnet aber höchstens alle
/// <c>UphHistoryIntervalMinutes</c>. Wiederholbar: es wird stets ab dem zuletzt verdichteten
/// Raster neu gerechnet, das dabei ggf. noch unvollständige jüngste Raster inklusive.
/// </summary>
public sealed class HistorySampler
{
    readonly TelegramStore _store;
    readonly TelegramFormat _format;
    readonly DestinationMap _destinations;
    readonly HashSet<string> _points;
    readonly List<string> _connections;
    readonly RbgOptions? _rbg;
    readonly TelegramTypeFilter _typeFilter;
    readonly int _intervalMinutes;
    readonly int _retentionDays;
    readonly int _rbgRetentionDays;
    readonly Action<string> _log;

    DateTime _nextRun = DateTime.MinValue;
    DateTime _nextRetention = DateTime.MinValue;

    public HistorySampler(
        TelegramStore store,
        TelegramFormat format,
        IReadOnlyList<ResourcePointConfig> resourcePoints,
        IReadOnlyDictionary<string, string>? destinationLabels,
        int intervalMinutes,
        int retentionDays,
        Action<string> log,
        RbgOptions? rbg = null,
        int rbgRetentionDays = 0)
    {
        _store = store;
        _format = format;
        _destinations = new DestinationMap(destinationLabels);
        var defs = resourcePoints is { Count: > 0 } ? resourcePoints : TelegramUtilization.DefaultResourcePoints;
        _points = defs
            .Select(p => p.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _connections = defs
            .Select(p => (p.Connection ?? "").Trim())
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _rbg = rbg;
        _typeFilter = new TelegramTypeFilter(format, rbg?.CountTelegramType);
        _intervalMinutes = Math.Max(1, intervalMinutes);
        _retentionDays = retentionDays;
        _rbgRetentionDays = rbgRetentionDays;
        _log = log;
    }

    bool SamplesRbg => _rbg is not null && _connections.Count > 0;

    /// <summary>Im Poll-Takt aufgerufen; verdichtet und räumt höchstens im Intervall-Takt.</summary>
    public void Tick()
    {
        if (DateTime.UtcNow < _nextRun)
            return;
        _nextRun = DateTime.UtcNow.AddMinutes(_intervalMinutes);

        try
        {
            SampleNow();
            SampleRbgNow();
            ApplyRetention();
        }
        catch (Exception ex)
        {
            _log($"Historie: {ex.Message}");
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

    /// <summary>Wie <see cref="SampleNow"/>, aber für die RBG-Spiele je Verbindung.</summary>
    public void SampleRbgNow()
    {
        if (!SamplesRbg)
            return;

        var newest = _store.MaxTelegramTime();
        if (newest is null)
            return;

        var step = TimeSpan.FromMinutes(_intervalMinutes);
        var completeUpTo = Floor(newest.Value, step);
        var start = _store.MaxRbgBucket()
            ?? Floor(newest.Value.AddDays(-Math.Max(1, _retentionDays)), step);

        if (start >= completeUpTo)
            return;

        var rows = AggregateRbg(start, completeUpTo, step);
        _store.ReplaceRbgSamplesFrom(start, rows);

        _log($"RBG-Historie: {rows.Count} Rasterzeilen ab {start:yyyy-MM-dd HH:mm} verdichtet " +
             $"(bis {completeUpTo:yyyy-MM-dd HH:mm}).");
    }

    /// <summary>
    /// Baut die gesamte Historie aus den Rohtelegrammen neu auf — nötig, nachdem ältere
    /// Telegramme per <c>backfill</c> nachgeladen wurden, denn <see cref="SampleNow"/> rechnet
    /// nur vorwärts ab dem zuletzt gespeicherten Raster. Fenster: das Kürzere aus
    /// Aufbewahrungszeitraum und ältestem vorhandenen Telegramm.
    /// </summary>
    public void Rebuild()
    {
        var newest = _store.MaxTelegramTime();
        if (newest is null)
            return;

        var step = TimeSpan.FromMinutes(_intervalMinutes);
        var completeUpTo = Floor(newest.Value, step);
        var retentionStart = _retentionDays > 0
            ? Floor(newest.Value.AddDays(-_retentionDays), step)
            : DateTime.MinValue;
        var oldest = _store.MinTelegramTime() is { } min ? Floor(min, step) : completeUpTo;
        var start = oldest > retentionStart ? oldest : retentionStart;

        if (start >= completeUpTo)
            return;

        var rows = Aggregate(start, completeUpTo, step);
        _store.ReplaceUphSamplesFrom(DateTime.MinValue, rows);   // alles verwerfen, komplett neu
        _nextRun = DateTime.UtcNow.AddMinutes(_intervalMinutes);

        _log($"UPH-Historie neu aufgebaut: {rows.Count} Rasterzeilen " +
             $"{start:yyyy-MM-dd HH:mm}–{completeUpTo:yyyy-MM-dd HH:mm}.");

        if (!SamplesRbg)
            return;

        // Nur das Fenster ersetzen, für das es Rohtelegramme gibt: die RBG-Aufzeichnung reicht
        // typischerweise weiter zurück als die Telegramme und darf dabei nicht verloren gehen.
        var rbgRows = AggregateRbg(start, completeUpTo, step);
        _store.ReplaceRbgSamplesFrom(start, rbgRows);

        _log($"RBG-Historie neu aufgebaut: {rbgRows.Count} Rasterzeilen " +
             $"{start:yyyy-MM-dd HH:mm}–{completeUpTo:yyyy-MM-dd HH:mm} " +
             $"(ältere bleiben erhalten).");
    }

    void ApplyRetention()
    {
        if (DateTime.UtcNow < _nextRetention)
            return;
        _nextRetention = DateTime.UtcNow.AddHours(24);

        var step = TimeSpan.FromMinutes(_intervalMinutes);

        if (_retentionDays > 0 && (_store.MaxUphBucket() ?? _store.MaxTelegramTime()) is { } newestUph)
        {
            var cutoff = Floor(newestUph.AddDays(-_retentionDays), step);
            var removed = _store.DeleteUphSamplesOlderThan(cutoff);
            if (removed > 0)
                _log($"UPH-Historie: {removed} Rasterzeilen vor {cutoff:yyyy-MM-dd} gelöscht " +
                     $"(Aufbewahrung {_retentionDays} Tage).");
        }

        if (_rbgRetentionDays > 0 && (_store.MaxRbgBucket() ?? _store.MaxTelegramTime()) is { } newestRbg)
        {
            var cutoff = Floor(newestRbg.AddDays(-_rbgRetentionDays), step);
            var removed = _store.DeleteRbgSamplesOlderThan(cutoff);
            if (removed > 0)
                _log($"RBG-Historie: {removed} Rasterzeilen vor {cutoff:yyyy-MM-dd} gelöscht " +
                     $"(Aufbewahrung {_rbgRetentionDays} Tage).");
        }
    }

    List<RbgSampleRow> AggregateRbg(DateTime from, DateTime to, TimeSpan step) =>
        RbgReport.Aggregate(_store.Read(from, to), _format, _connections, from, to, step, _rbg!);

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
            if (!_typeFilter.Accepts(fields))    // DM/AK-Paar: nur eines der beiden zählen
                continue;
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
