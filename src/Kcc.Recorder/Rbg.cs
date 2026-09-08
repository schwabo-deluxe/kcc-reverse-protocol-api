namespace Kcc.Recorder;

/// <summary>Spielauswertung einer RBG-Verbindung über das Auslastungsfenster.</summary>
public sealed record RbgCycleStats
{
    public required string Connection { get; init; }

    /// <summary>Abgeschlossene Einlagerungen (Bringen) im Fenster.</summary>
    public required int Puts { get; init; }

    /// <summary>Abgeschlossene Auslagerungen (Holen) im Fenster.</summary>
    public required int Fetches { get; init; }

    /// <summary>Doppelspiele = <c>min(Puts, Fetches)</c> — gepaarte Ein- und Auslagerung.</summary>
    public required int DoubleCycles { get; init; }

    /// <summary>Einzelspiele = <c>|Puts − Fetches|</c> — ungepaarte Einzelfahrten.</summary>
    public required int SingleCycles { get; init; }

    public required int MaxCyclesPerHour { get; init; }

    /// <summary>
    /// Leistungsgrad gegen die Kapazität: <c>(Doppelspiele + Einzelspiele/2) / (MaxCyclesPerHour ·
    /// Stunden) · 100</c>. Entspricht den erreichten Lagerspielen relativ zu den nominalen
    /// Doppelspielen/h (FEM 9.851: ein Doppelspiel = 2 Lagerbewegungen).
    /// </summary>
    public required double Percent { get; init; }

    /// <summary>
    /// Zeitbasierter Auslastungsgrad in Prozent = belegte Auftragszeit ÷ Fenster (FEM-üblicher
    /// Auslastungsgrad; auf 100 begrenzt bei überlappenden Aufträgen).
    /// </summary>
    public required double BusyPercent { get; init; }

    /// <summary>Leerlaufzeit in Sekunden im Fenster = Fenster − belegte Auftragszeit.</summary>
    public required double IdleSeconds { get; init; }

    /// <summary>Ø Sekunden von der Auftragserteilung bis zum Abschluss — Einlagerung.</summary>
    public required double AvgPutSeconds { get; init; }

    /// <summary>Ø Sekunden von der Auftragserteilung bis zum Abschluss — Auslagerung.</summary>
    public required double AvgFetchSeconds { get; init; }

    public required DateTime? LatestAt { get; init; }

    /// <summary>
    /// Gleitender Verlauf über das Fenster: <see cref="UtilizationBucket.Uph"/> = Spiele/h
    /// (Doppelspiel-Äquivalent, also <c>Doppel + Einzel/2</c> je Stunde), <see cref="UtilizationBucket.Count"/>
    /// = abgeschlossene Fahrten im Fenster. Der letzte Punkt endet bei <c>to</c>.
    /// </summary>
    public required IReadOnlyList<UtilizationBucket> Series { get; init; }
}

/// <summary>MessageCode-Sätze der RBG-Spielauswertung (aus <see cref="KccConfig"/>).</summary>
public sealed record RbgOptions
{
    public required int MaxCyclesPerHour { get; init; }
    public required IReadOnlyList<string> PutDoneCodes { get; init; }
    public required IReadOnlyList<string> FetchDoneCodes { get; init; }
    public required IReadOnlyList<string> PutOrderCodes { get; init; }
    public required IReadOnlyList<string> FetchOrderCodes { get; init; }

    static IReadOnlyList<string> Or(List<string> configured, IReadOnlyList<string> fallback) =>
        configured is { Count: > 0 } ? configured : fallback;

    public static RbgOptions From(KccConfig c) => new()
    {
        MaxCyclesPerHour = c.RbgMaxCyclesPerHour,
        PutDoneCodes = Or(c.RbgPutDoneCodes, RbgReport.DefaultPutDone),
        FetchDoneCodes = Or(c.RbgFetchDoneCodes, RbgReport.DefaultFetchDone),
        PutOrderCodes = Or(c.RbgPutOrderCodes, RbgReport.DefaultPutOrder),
        FetchOrderCodes = Or(c.RbgFetchOrderCodes, RbgReport.DefaultFetchOrder),
    };
}

/// <summary>
/// Wertet die Fahraufträge einer RBG-Verbindung aus. Begriffe nach FEM 9.851 / Wikipedia
/// „Regalbediengerät": ein <b>Einzelspiel</b> ist eine reine Ein- oder Auslagerung, ein
/// <b>Doppelspiel</b> (kombiniertes Spiel) eine Ein- <em>und</em> Auslagerung in einer Fahrt.
///
/// Gezählt werden die abgeschlossenen Fahrten (<c>ENDDEP</c>/<c>ENDPUP</c>); daraus
/// Doppelspiele = <c>min(Ein, Aus)</c>, Einzelspiele = <c>|Ein − Aus|</c> und die Auslastung
/// gegen die Kapazität. Über die Auftragspaare (<c>DEPORD</c>→<c>ENDDEP</c>,
/// <c>PUPORD</c>→<c>ENDPUP</c>) werden Ø Ausführungsdauer und Leerlaufzeit gemessen. Reine
/// Funktion über einem Zeitfenster.
///
/// Die Anlage sendet jedes Ereignis doppelt (TelegramType <c>DM</c>/<c>AK</c>, ~0,1&#160;s Abstand,
/// gleiche Felder) — das wird hier zusammengeführt.
/// </summary>
public static class RbgReport
{
    public static readonly IReadOnlyList<string> DefaultPutDone = ["ENDDEP"];
    public static readonly IReadOnlyList<string> DefaultFetchDone = ["ENDPUP"];
    public static readonly IReadOnlyList<string> DefaultPutOrder = ["DEPORD"];
    public static readonly IReadOnlyList<string> DefaultFetchOrder = ["PUPORD"];

    const double DedupWindowSeconds = 10;

    enum Kind { PutDone, FetchDone, PutOrder, FetchOrder }

    readonly record struct Ev(DateTime At, string Label, Kind Kind);

    /// <summary>
    /// Liest die Fahrauftrags-Ereignisse je Verbindung aus dem Telegrammstrom: klassifiziert nach
    /// den MessageCode-Sätzen und führt die DM/AK-Doppel zusammen. Die Ereignisse je Verbindung
    /// sind zeitlich aufsteigend.
    /// </summary>
    static Dictionary<string, List<Ev>> EventsByConnection(
        IEnumerable<Telegram> window, TelegramFormat format, RbgOptions options,
        Func<string, bool> wanted)
    {
        var mcIdx = FieldIndex(format, "MessageCode");
        var labelIdx = FieldIndex(format, "ResourceLabel");
        var srcIdx = FieldIndex(format, "Source");
        var dstIdx = FieldIndex(format, "Destination");

        var putDone = Set(options.PutDoneCodes);
        var fetchDone = Set(options.FetchDoneCodes);
        var putOrder = Set(options.PutOrderCodes);
        var fetchOrder = Set(options.FetchOrderCodes);

        Kind? Classify(string code) =>
            putDone.Contains(code) ? Kind.PutDone
            : fetchDone.Contains(code) ? Kind.FetchDone
            : putOrder.Contains(code) ? Kind.PutOrder
            : fetchOrder.Contains(code) ? Kind.FetchOrder
            : null;

        var byConnection = new Dictionary<string, List<Ev>>(StringComparer.OrdinalIgnoreCase);
        var lastSeen = new Dictionary<(string, string, string, string, string), DateTime>();

        foreach (var t in window.OrderBy(t => t.DateTime))
        {
            var connection = (t.ConnectionName ?? "").Trim();
            if (connection.Length == 0 || !wanted(connection))
                continue;

            var f = format.Slice(t.Data);
            var code = Field(f, mcIdx);
            if (Classify(code) is not { } kind)
                continue;

            var label = Field(f, labelIdx);
            var key = (connection, code, label, Field(f, srcIdx), Field(f, dstIdx));
            if (lastSeen.TryGetValue(key, out var prev) && (t.DateTime - prev).TotalSeconds < DedupWindowSeconds)
                continue;
            lastSeen[key] = t.DateTime;

            if (!byConnection.TryGetValue(connection, out var list))
                byConnection[connection] = list = [];
            list.Add(new Ev(t.DateTime, label, kind));
        }

        return byConnection;
    }

    /// <summary>
    /// Verdichtet den Telegrammstrom zu Rasterzeilen je Zeitraster × RBG-Verbindung — die
    /// Grundlage der Langzeitaufzeichnung (<c>/rbg</c>). Ein Durchlauf für alle Verbindungen;
    /// die belegte Auftragszeit wird dem Raster des Abschlusses zugeschlagen.
    /// </summary>
    public static List<RbgSampleRow> Aggregate(
        IEnumerable<Telegram> window,
        TelegramFormat format,
        IReadOnlyCollection<string> connections,
        DateTime from,
        DateTime to,
        TimeSpan step,
        RbgOptions options)
    {
        var wanted = new HashSet<string>(
            connections.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()),
            StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0 || step <= TimeSpan.Zero || from >= to)
            return [];

        var rows = new Dictionary<(long Slot, string Connection), RbgSampleRow>();

        RbgSampleRow Row(DateTime at, string connection)
        {
            var slot = (long)((at - from).Ticks / step.Ticks);
            var key = (slot, connection);
            if (!rows.TryGetValue(key, out var row))
                rows[key] = row = new RbgSampleRow
                {
                    Bucket = from + TimeSpan.FromTicks(slot * step.Ticks),
                    Connection = connection,
                };
            return row;
        }

        foreach (var (connection, events) in EventsByConnection(window, format, options, wanted.Contains))
        {
            foreach (var e in events)
            {
                if (e.At < from || e.At >= to || e.Kind is not (Kind.PutDone or Kind.FetchDone))
                    continue;
                var row = Row(e.At, connection);
                if (e.Kind == Kind.PutDone) row.Puts++;
                else row.Fetches++;
            }

            foreach (var kind in new[] { Kind.PutDone, Kind.FetchDone })
            {
                var order = kind == Kind.PutDone ? Kind.PutOrder : Kind.FetchOrder;
                foreach (var (doneAt, seconds) in PairDurations(events, kind, order, from, to))
                    Row(doneAt, connection).BusySeconds += seconds;
            }
        }

        return rows.Values
            .Where(r => r.Puts > 0 || r.Fetches > 0)
            .OrderBy(r => r.Bucket).ThenBy(r => r.Connection, StringComparer.Ordinal)
            .ToList();
    }

    public static RbgCycleStats Compute(
        IReadOnlyList<Telegram> window,
        TelegramFormat format,
        string connection,
        int maxCyclesPerHour,
        DateTime from,
        DateTime to,
        RbgOptions options,
        int bucketMinutes = 5,
        int stepMinutes = 1)
    {
        var events = EventsByConnection(window, format, options,
            c => string.Equals(c, connection, StringComparison.OrdinalIgnoreCase))
            .Values.FirstOrDefault() ?? [];

        var doneInWindow = events.Where(e => e.At >= from && e.At < to).ToList();
        var puts = doneInWindow.Count(e => e.Kind == Kind.PutDone);
        var fetches = doneInWindow.Count(e => e.Kind == Kind.FetchDone);
        var full = Math.Min(puts, fetches);
        var half = Math.Abs(puts - fetches);

        var hours = Math.Max(1e-9, (to - from).TotalHours);
        var percent = maxCyclesPerHour > 0
            ? Math.Round((full + half / 2.0) / (maxCyclesPerHour * hours) * 100, 1)
            : 0;

        var putPairs = PairDurations(events, Kind.PutDone, Kind.PutOrder, from, to);
        var fetchPairs = PairDurations(events, Kind.FetchDone, Kind.FetchOrder, from, to);
        var avgPut = putPairs.Count == 0 ? 0 : Math.Round(putPairs.Average(p => p.Seconds), 1);
        var avgFetch = fetchPairs.Count == 0 ? 0 : Math.Round(fetchPairs.Average(p => p.Seconds), 1);
        var windowSeconds = Math.Max(1e-9, (to - from).TotalSeconds);
        var busy = putPairs.Sum(p => p.Seconds) + fetchPairs.Sum(p => p.Seconds);
        var idle = Math.Max(0, windowSeconds - busy);

        return new RbgCycleStats
        {
            BusyPercent = Math.Round(Math.Min(100, busy / windowSeconds * 100), 1),
            Series = RollingSeries(events, from, to, bucketMinutes, stepMinutes),
            Connection = connection,
            Puts = puts,
            Fetches = fetches,
            DoubleCycles = full,
            SingleCycles = half,
            MaxCyclesPerHour = maxCyclesPerHour,
            Percent = percent,
            IdleSeconds = Math.Round(idle, 1),
            AvgPutSeconds = avgPut,
            AvgFetchSeconds = avgFetch,
            LatestAt = doneInWindow.Count > 0 ? doneInWindow.Max(e => e.At) : null,
        };
    }

    /// <summary>
    /// Gleitender Verlauf der Spiele/h: feine Abtastung der Abschluss-Ereignisse (Schritt), je
    /// Stützpunkt die Summe der letzten <paramref name="bucketMinutes"/> Minuten, umgerechnet auf
    /// Doppelspiel-Äquivalent pro Stunde. Letzter Stützpunkt endet bei <paramref name="to"/>.
    /// </summary>
    static List<UtilizationBucket> RollingSeries(
        List<Ev> events, DateTime from, DateTime to, int bucketMinutes, int stepMinutes)
    {
        var step = Math.Max(1, stepMinutes);
        var win = Math.Max(step, bucketMinutes);
        var fineCount = Math.Max(1, (int)Math.Ceiling((to - from).TotalMinutes / step));
        var winSteps = Math.Max(1, (int)Math.Round(win / (double)step));

        var finePut = new int[fineCount];
        var fineFetch = new int[fineCount];
        foreach (var e in events)
        {
            if (e.At < from || e.At >= to || e.Kind is not (Kind.PutDone or Kind.FetchDone))
                continue;
            var slot = (int)((e.At - from).TotalMinutes / step);
            if (slot < 0 || slot >= fineCount)
                continue;
            if (e.Kind == Kind.PutDone) finePut[slot]++;
            else fineFetch[slot]++;
        }

        var winHours = winSteps * step / 60.0;
        var series = new List<UtilizationBucket>(fineCount);
        int accPut = 0, accFetch = 0;
        for (var i = 0; i < fineCount; i++)
        {
            accPut += finePut[i];
            accFetch += fineFetch[i];
            if (i >= winSteps)
            {
                accPut -= finePut[i - winSteps];
                accFetch -= fineFetch[i - winSteps];
            }
            var full = Math.Min(accPut, accFetch);
            var half = Math.Abs(accPut - accFetch);
            series.Add(new UtilizationBucket
            {
                At = from.AddMinutes((i + 1) * step),
                Count = accPut + accFetch,
                Uph = Math.Round((full + half / 2.0) / winHours, 1),
            });
        }
        return series;
    }

    /// <summary>
    /// Paart jedes Abschluss-Ereignis im Fenster mit dem jüngsten passenden Auftrag davor
    /// (gleiches <c>ResourceLabel</c>, sonst FIFO). Gibt je Paar den Abschlusszeitpunkt und die
    /// Dauer zurück, damit der Aufrufer mitteln oder auf Zeitraster verteilen kann.
    /// </summary>
    static List<(DateTime DoneAt, double Seconds)> PairDurations(
        List<Ev> events, Kind doneKind, Kind orderKind, DateTime from, DateTime to)
    {
        var orders = events.Where(e => e.Kind == orderKind && e.At >= from.AddMinutes(-30)).ToList();
        var used = new bool[orders.Count];
        var durations = new List<(DateTime, double)>();

        foreach (var done in events.Where(e => e.Kind == doneKind && e.At >= from && e.At < to))
        {
            var idx = -1;
            for (var i = orders.Count - 1; i >= 0; i--)
            {
                if (used[i] || orders[i].At > done.At)
                    continue;
                if (!string.IsNullOrEmpty(done.Label) && orders[i].Label != done.Label)
                    continue;
                idx = i;
                break;
            }
            if (idx < 0)
                for (var i = 0; i < orders.Count; i++)
                    if (!used[i] && orders[i].At <= done.At) { idx = i; break; }
            if (idx < 0)
                continue;

            used[idx] = true;
            durations.Add((done.At, (done.At - orders[idx].At).TotalSeconds));
        }

        return durations;
    }

    static HashSet<string> Set(IReadOnlyList<string> codes) =>
        new(codes.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()), StringComparer.OrdinalIgnoreCase);

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
