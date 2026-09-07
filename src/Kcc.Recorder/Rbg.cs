namespace Kcc.Recorder;

/// <summary>Spielauswertung einer RBG-Verbindung über das Auslastungsfenster.</summary>
public sealed record RbgCycleStats
{
    public required string Connection { get; init; }

    /// <summary>Abgeschlossene Einlagerungen (Bringen) im Fenster.</summary>
    public required int Puts { get; init; }

    /// <summary>Abgeschlossene Auslagerungen (Holen) im Fenster.</summary>
    public required int Fetches { get; init; }

    /// <summary>Vollspiele = <c>min(Puts, Fetches)</c> — gepaarte Ein- und Auslagerung.</summary>
    public required int FullCycles { get; init; }

    /// <summary>Halbspiele = <c>|Puts − Fetches|</c> — ungepaarte Einzelfahrten.</summary>
    public required int HalfCycles { get; init; }

    public required int MaxCyclesPerHour { get; init; }

    /// <summary>Auslastung: <c>(Vollspiele + Halbspiele/2) / (MaxCyclesPerHour · Stunden) · 100</c>.</summary>
    public required double Percent { get; init; }

    /// <summary>Leerlaufzeit in Sekunden im Fenster = Fenster − belegte Auftragszeit.</summary>
    public required double IdleSeconds { get; init; }

    /// <summary>Ø Sekunden von der Auftragserteilung bis zum Abschluss — Einlagerung.</summary>
    public required double AvgPutSeconds { get; init; }

    /// <summary>Ø Sekunden von der Auftragserteilung bis zum Abschluss — Auslagerung.</summary>
    public required double AvgFetchSeconds { get; init; }

    public required DateTime? LatestAt { get; init; }
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
/// Wertet die Fahraufträge einer RBG-Verbindung aus: zählt abgeschlossene Ein-/Auslagerungen
/// (<c>ENDDEP</c>/<c>ENDPUP</c>), bildet daraus Voll- und Halbspiele und die Auslastung gegen die
/// Kapazität, und misst über die Auftragspaare (<c>DEPORD</c>→<c>ENDDEP</c>, <c>PUPORD</c>→<c>ENDPUP</c>)
/// die Ø Ausführungsdauer und die Leerlaufzeit. Reine Funktion über einem Zeitfenster.
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

    public static RbgCycleStats Compute(
        IReadOnlyList<Telegram> window,
        TelegramFormat format,
        string connection,
        int maxCyclesPerHour,
        DateTime from,
        DateTime to,
        RbgOptions options)
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

        var events = new List<Ev>();
        var lastSeen = new Dictionary<(string, string, string, string), DateTime>();

        foreach (var t in window
                     .Where(t => string.Equals(t.ConnectionName, connection, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(t => t.DateTime))
        {
            var f = format.Slice(t.Data);
            var code = Field(f, mcIdx);
            if (Classify(code) is not { } kind)
                continue;

            var label = Field(f, labelIdx);
            var key = (code, label, Field(f, srcIdx), Field(f, dstIdx));
            if (lastSeen.TryGetValue(key, out var prev) && (t.DateTime - prev).TotalSeconds < DedupWindowSeconds)
                continue;
            lastSeen[key] = t.DateTime;
            events.Add(new Ev(t.DateTime, label, kind));
        }

        var doneInWindow = events.Where(e => e.At >= from && e.At < to).ToList();
        var puts = doneInWindow.Count(e => e.Kind == Kind.PutDone);
        var fetches = doneInWindow.Count(e => e.Kind == Kind.FetchDone);
        var full = Math.Min(puts, fetches);
        var half = Math.Abs(puts - fetches);

        var hours = Math.Max(1e-9, (to - from).TotalHours);
        var percent = maxCyclesPerHour > 0
            ? Math.Round((full + half / 2.0) / (maxCyclesPerHour * hours) * 100, 1)
            : 0;

        var (avgPut, busyPut) = PairDurations(events, Kind.PutDone, Kind.PutOrder, from, to);
        var (avgFetch, busyFetch) = PairDurations(events, Kind.FetchDone, Kind.FetchOrder, from, to);
        var idle = Math.Max(0, (to - from).TotalSeconds - (busyPut + busyFetch));

        return new RbgCycleStats
        {
            Connection = connection,
            Puts = puts,
            Fetches = fetches,
            FullCycles = full,
            HalfCycles = half,
            MaxCyclesPerHour = maxCyclesPerHour,
            Percent = percent,
            IdleSeconds = Math.Round(idle, 1),
            AvgPutSeconds = avgPut,
            AvgFetchSeconds = avgFetch,
            LatestAt = doneInWindow.Count > 0 ? doneInWindow.Max(e => e.At) : null,
        };
    }

    /// <summary>
    /// Paart jedes Abschluss-Ereignis im Fenster mit dem jüngsten passenden Auftrag davor
    /// (gleiches <c>ResourceLabel</c>, sonst FIFO). Gibt Ø-Dauer und Summe der Dauern zurück.
    /// </summary>
    static (double AvgSeconds, double BusySeconds) PairDurations(
        List<Ev> events, Kind doneKind, Kind orderKind, DateTime from, DateTime to)
    {
        var orders = events.Where(e => e.Kind == orderKind && e.At >= from.AddMinutes(-30)).ToList();
        var used = new bool[orders.Count];
        var durations = new List<double>();

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
            durations.Add((done.At - orders[idx].At).TotalSeconds);
        }

        return durations.Count == 0
            ? (0, 0)
            : (Math.Round(durations.Average(), 1), durations.Sum());
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
