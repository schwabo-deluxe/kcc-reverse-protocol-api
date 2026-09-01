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

    public TelegramRecorder(
        KccQuery query,
        TelegramStore store,
        RecordFilter filter,
        KccConfig config,
        Action<string> log)
    {
        _query = query;
        _store = store;
        _filter = filter;
        _config = config;
        _log = log;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var lastSeenId = _store.GetLastSeenId();
        if (lastSeenId is null)
        {
            lastSeenId = await GetCurrentMaxIdAsync(ct);
            _store.SetLastSeenId(lastSeenId.Value);
            _log($"Erster Start — setze am aktuellen Ende an (Id {lastSeenId}). " +
                 "Ältere Telegramme holt 'kcc backfill'.");
        }
        else
        {
            _log($"Setze fort ab Id {lastSeenId}.");
        }

        var recorded = 0L;
        var seen = 0L;

        while (!ct.IsCancellationRequested)
        {
            var batch = await FetchBatchAsync(lastSeenId.Value, ct);

            if (batch.Count > 0)
            {
                lastSeenId = batch[^1].Id;
                seen += batch.Count;

                var keep = batch.Where(_filter.ShouldRecord).ToList();
                if (keep.Count > 0)
                    recorded += _store.Insert(keep);

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

    /// <summary>Lädt historische Telegramme in einem Id-Bereich nach.</summary>
    public async Task BackfillAsync(long fromId, long? toId, CancellationToken ct)
    {
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
                recorded += _store.Insert(keep);

            _log($"Backfill bis Id {cursor}: {recorded} von {seen} aufgezeichnet.");

            if (toId.HasValue && cursor >= toId.Value)
                break;
        }

        _log($"Backfill fertig. {recorded} von {seen} gelesenen Telegrammen aufgezeichnet.");
    }

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
