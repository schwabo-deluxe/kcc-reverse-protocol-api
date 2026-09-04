namespace Kcc.Recorder;

/// <summary>
/// Schneidet Telegramme fortlaufend mit.
///
/// Es gibt keinen Server-Push für Protokolldaten — auch die Weboberfläche pollt. Da Id monoton
/// vergeben wird, fragt der Recorder wiederholt "alles mit Id &gt; zuletzt gesehen", aufsteigend
/// sortiert. Das ist lückenlos und wiederholbar: ein Neustart setzt exakt dort wieder an.
/// </summary>
public sealed class TelegramRecorder
{
    readonly KccQuery _query;
    readonly TelegramStore _store;
    readonly RecordFilter _filter;
    readonly KccConfig _config;
    readonly Action<string> _log;
    readonly TelegramCsvWriter? _csv;
    readonly UphHistorySampler? _uph;

    public TelegramRecorder(
        KccQuery query,
        TelegramStore store,
        RecordFilter filter,
        KccConfig config,
        Action<string> log,
        TelegramCsvWriter? csv = null,
        UphHistorySampler? uph = null)
    {
        _query = query;
        _store = store;
        _filter = filter;
        _config = config;
        _log = log;
        _csv = csv;
        _uph = uph;
    }

    DateTime _nextRetentionCheck = DateTime.MinValue;

    public async Task RunAsync(CancellationToken ct)
    {
        ApplyRetention();

        var lastSeenId = _store.GetLastSeenId();
        if (lastSeenId is null)
        {
            lastSeenId = await GetCurrentMaxIdAsync(ct);
            _store.SetLastSeenId(lastSeenId.Value);
            _log($"Erster Start — setze am aktuellen Ende an (Id {lastSeenId}).");

            // Damit das Dashboard nicht mit einem leeren Fenster startet, wird die zuletzt
            // sichtbare Zeitspanne einmalig nachgeladen. Ältere Daten holt 'kcc backfill'.
            if (_config.StartupBackfillMinutes > 0)
                await BackfillSinceAsync(DateTime.Now.AddMinutes(-_config.StartupBackfillMinutes), ct);
        }
        else
        {
            _log($"Setze fort ab Id {lastSeenId}.");
        }

        _uph?.Tick();

        var recorded = 0L;
        var seen = 0L;

        while (!ct.IsCancellationRequested)
        {
            ApplyRetention();
            _uph?.Tick();

            var batch = await FetchBatchAsync(lastSeenId.Value, ct);

            if (batch.Count > 0)
            {
                lastSeenId = batch[^1].Id;
                seen += batch.Count;

                var keep = batch.Where(_filter.ShouldRecord).ToList();
                if (keep.Count > 0)
                {
                    recorded += _store.Insert(keep);
                    _csv?.Append(keep);
                }

                // Auch für verworfene Zeilen weiterzählen, sonst werden sie endlos erneut geholt.
                _store.SetLastSeenId(lastSeenId.Value);
                _log($"{batch.Count} gelesen, {keep.Count} aufgezeichnet (gesamt {recorded} von {seen}), Id bis {lastSeenId}.");
            }

            // Volles Batch heisst: es liegt noch mehr an — ohne Pause weiter aufholen.
            if (batch.Count >= _config.BatchSize)
                continue;

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_config.PollIntervalSeconds), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _log($"Beendet. {recorded} von {seen} gelesenen Telegrammen aufgezeichnet.");
    }

    /// <summary>
    /// Wendet die Aufbewahrungsregel an — höchstens einmal alle 24 h, damit die Aufrufe im
    /// Poll-Takt billig bleiben. <c>RetentionDays &lt;= 0</c> schaltet die Regel ab.
    /// </summary>
    void ApplyRetention()
    {
        if (_config.RetentionDays <= 0 || DateTime.UtcNow < _nextRetentionCheck)
            return;

        _nextRetentionCheck = DateTime.UtcNow.AddHours(24);

        var cutoff = TelegramStore.RetentionCutoff(_config.RetentionDays);
        var removed = _store.DeleteOlderThan(cutoff);
        if (removed > 0)
            _log($"Aufbewahrung: {removed} Telegramme vor {cutoff:yyyy-MM-dd} gelöscht " +
                 $"(RetentionDays {_config.RetentionDays}).");
    }

    /// <summary>Lädt historische Telegramme in einem Id-Bereich nach.</summary>
    public async Task BackfillAsync(long fromId, long? toId, CancellationToken ct)
    {
        ApplyRetention();

        var cursor = fromId - 1;
        var recorded = 0L;
        var seen = 0L;

        while (!ct.IsCancellationRequested)
        {
            var batch = await FetchBatchAsync(cursor, ct);
            if (batch.Count == 0)
                break;

            if (toId.HasValue)
                batch = batch.Where(t => t.Id <= toId.Value).ToList();
            if (batch.Count == 0)
                break;

            cursor = batch[^1].Id;
            seen += batch.Count;

            var keep = batch.Where(_filter.ShouldRecord).ToList();
            if (keep.Count > 0)
            {
                recorded += _store.Insert(keep);
                _csv?.Append(keep);
            }

            _log($"Backfill bis Id {cursor}: {recorded} von {seen} aufgezeichnet.");

            if (toId.HasValue && cursor >= toId.Value)
                break;
        }

        _log($"Backfill fertig. {recorded} von {seen} gelesenen Telegrammen aufgezeichnet.");
    }

    /// <summary>
    /// Holt die Telegramme ab <paramref name="since"/> nach — von der neuesten Id rückwärts,
    /// bis der Zeitpunkt unterschritten ist. Für den Start, damit das Dashboard sofort
    /// Historie zeigt, statt erst mit dem Mitschnitt zu füllen.
    /// </summary>
    async Task BackfillSinceAsync(DateTime since, CancellationToken ct)
    {
        var recorded = 0L;
        var seen = 0L;
        var skip = 0;

        while (!ct.IsCancellationRequested)
        {
            var batch = await FetchNewestAsync(skip, ct);
            if (batch.Count == 0)
                break;

            skip += batch.Count;
            seen += batch.Count;

            var inWindow = batch.Where(t => t.DateTime >= since).ToList();
            var keep = inWindow.Where(_filter.ShouldRecord).ToList();
            if (keep.Count > 0)
            {
                // Aufsteigend einfügen, damit die CSV in derselben Reihenfolge wächst wie im Betrieb.
                keep.Reverse();
                recorded += _store.Insert(keep);
                _csv?.Append(keep);
            }

            // Der älteste Datensatz des Stapels liegt vor dem Fenster — weiter zurück muss nicht.
            if (inWindow.Count < batch.Count)
                break;
        }

        _log($"Start-Nachladung ab {since:yyyy-MM-dd HH:mm}: " +
             $"{recorded} von {seen} gelesenen Telegrammen aufgezeichnet.");
    }

    /// <summary>Neueste Telegramme absteigend, seitenweise über <paramref name="skip"/>.</summary>
    Task<IReadOnlyList<Telegram>> FetchNewestAsync(int skip, CancellationToken ct) =>
        _query.QueryTelegramsAsync(
            _filter.ServerSideFilters().ToList(),
            [new QueryOrderBy { column = "Id", sort = "desc" }],
            _config.BatchSize,
            skip: skip,
            ct);

    Task<IReadOnlyList<Telegram>> FetchBatchAsync(long afterId, CancellationToken ct)
    {
        var filters = new List<QueryFilter>
        {
            new()
            {
                FilterField = "Id",
                FilterType = FilterType.GreaterThan,
                Filter = afterId,
                filterValueType = "Int64",
            },
        };
        filters.AddRange(_filter.ServerSideFilters());

        return _query.QueryTelegramsAsync(
            filters,
            [new QueryOrderBy { column = "Id", sort = "asc" }],
            _config.BatchSize,
            skip: null,
            ct);
    }

    async Task<long> GetCurrentMaxIdAsync(CancellationToken ct)
    {
        var newest = await _query.QueryTelegramsAsync(
            filters: null,
            [new QueryOrderBy { column = "Id", sort = "desc" }],
            take: 1,
            skip: null,
            ct);
        return newest.Count > 0 ? newest[0].Id : 0;
    }
}
