namespace Kcc.Recorder;

/// <summary>Belegungsauswertung eines Fördertechnik-Ressourcenpunkts über das Zeitfenster.</summary>
public sealed record ConveyorStats
{
    public required string ResourcePoint { get; init; }

    /// <summary>Erteilte Transportaufträge (<c>TSPORD</c>) im Fenster.</summary>
    public required int Orders { get; init; }

    /// <summary>Beendete Transporte (<c>ENDTSP</c>) im Fenster.</summary>
    public required int Completed { get; init; }

    /// <summary>Frei-Meldungen des Punkts (<c>RPFREE</c>) im Fenster.</summary>
    public required int FreeSignals { get; init; }

    /// <summary>
    /// Belegungsgrad in Prozent = Summe der Transportdauern ÷ Fenster. Die Transporte eines
    /// Punkts laufen nacheinander, deshalb ist dies — anders als beim RBG — ein echtes
    /// Zeitmaß; auf 100 begrenzt.
    /// </summary>
    public required double BusyPercent { get; init; }

    /// <summary>Belegte Zeit in Sekunden im Fenster.</summary>
    public required double BusySeconds { get; init; }

    /// <summary>Freie Zeit in Sekunden im Fenster = Fenster − belegte Zeit.</summary>
    public required double IdleSeconds { get; init; }

    /// <summary>Ø Sekunden von <c>TSPORD</c> bis <c>ENDTSP</c> — Dauer eines Transports.</summary>
    public required double AvgTransportSeconds { get; init; }

    /// <summary>
    /// Ø Sekunden von <c>ENDTSP</c> bis zum nächsten <c>TSPORD</c> — wie lange der Punkt auf
    /// den nächsten Auftrag wartete. Lange Wartezeit heißt: der Punkt ist nicht der Engpass.
    /// </summary>
    public required double AvgWaitSeconds { get; init; }

    public required DateTime? LatestAt { get; init; }

    /// <summary>
    /// Gleitender Verlauf des Belegungsgrads: <see cref="UtilizationBucket.Uph"/> = Prozent,
    /// <see cref="UtilizationBucket.Count"/> = beendete Transporte im Fenster.
    /// </summary>
    public required IReadOnlyList<UtilizationBucket> Series { get; init; }
}

/// <summary>MessageCode-Sätze der Fördertechnik-Auswertung (aus <see cref="KccConfig"/>).</summary>
public sealed record ConveyorOptions
{
    public required IReadOnlyList<string> OrderCodes { get; init; }
    public required IReadOnlyList<string> EndCodes { get; init; }
    public required IReadOnlyList<string> FreeCodes { get; init; }

    static IReadOnlyList<string> Or(List<string> configured, IReadOnlyList<string> fallback) =>
        configured is { Count: > 0 } ? configured : fallback;

    public static ConveyorOptions From(KccConfig c) => new()
    {
        OrderCodes = Or(c.ConveyorOrderCodes, ConveyorReport.DefaultOrder),
        EndCodes = Or(c.ConveyorEndCodes, ConveyorReport.DefaultEnd),
        FreeCodes = Or(c.ConveyorFreeCodes, ConveyorReport.DefaultFree),
    };
}

/// <summary>
/// Wertet aus, wie stark ein Fördertechnik-Ressourcenpunkt belegt ist. Der Ablauf an einem Punkt:
/// <list type="bullet">
///   <item><c>TSPORD</c> — Transportauftrag erteilt, der Punkt beginnt zu arbeiten</item>
///   <item><c>ENDTSP</c> — Transport beendet, der Punkt wartet auf den nächsten Auftrag</item>
///   <item><c>RPFREE</c> — der Ressourcenpunkt meldet sich frei</item>
/// </list>
///
/// Daraus: belegte Zeit = Summe der Dauern <c>TSPORD</c>→<c>ENDTSP</c>, freie Zeit = Rest des
/// Fensters, dazu die mittlere Wartezeit <c>ENDTSP</c>→nächster <c>TSPORD</c>. Reine Funktion
/// über einem Zeitfenster; gepaart wird über die LE-Nummer (<c>ResourceLabel</c>), sonst FIFO.
/// </summary>
public static class ConveyorReport
{
    public static readonly IReadOnlyList<string> DefaultOrder = ["TSPORD"];
    public static readonly IReadOnlyList<string> DefaultEnd = ["ENDTSP"];
    public static readonly IReadOnlyList<string> DefaultFree = ["RPFREE"];

    const double DedupWindowSeconds = 10;

    enum Kind { Order, End, Free }

    readonly record struct Ev(DateTime At, string Label, Kind Kind);

    public static ConveyorStats Compute(
        IReadOnlyList<Telegram> window,
        TelegramFormat format,
        string resourcePoint,
        DateTime from,
        DateTime to,
        ConveyorOptions options,
        int bucketMinutes = 5,
        int stepMinutes = 1)
    {
        var mcIdx = FieldIndex(format, "MessageCode");
        var rpIdx = FieldIndex(format, "ResourcePoint");
        var labelIdx = FieldIndex(format, "ResourceLabel");

        var order = Set(options.OrderCodes);
        var end = Set(options.EndCodes);
        var free = Set(options.FreeCodes);

        Kind? Classify(string code) =>
            order.Contains(code) ? Kind.Order
            : end.Contains(code) ? Kind.End
            : free.Contains(code) ? Kind.Free
            : null;

        var events = new List<Ev>();
        var lastSeen = new Dictionary<(string, string), DateTime>();

        foreach (var t in window.OrderBy(t => t.DateTime))
        {
            var f = format.Slice(t.Data);
            if (!string.Equals(Field(f, rpIdx), resourcePoint, StringComparison.OrdinalIgnoreCase))
                continue;

            var code = Field(f, mcIdx);
            if (Classify(code) is not { } kind)
                continue;

            // Die Anlage schickt jedes Ereignis als DM/AK-Doppel — zusammenführen.
            var label = Field(f, labelIdx);
            var key = (code, label);
            if (lastSeen.TryGetValue(key, out var prev) && (t.DateTime - prev).TotalSeconds < DedupWindowSeconds)
                continue;
            lastSeen[key] = t.DateTime;
            events.Add(new Ev(t.DateTime, label, kind));
        }

        var inWindow = events.Where(e => e.At >= from && e.At < to).ToList();
        var transports = Pair(events, from, to);

        var windowSeconds = Math.Max(1e-9, (to - from).TotalSeconds);
        var busy = transports.Sum(p => p.Seconds);
        var waits = Waits(events, from, to);

        return new ConveyorStats
        {
            ResourcePoint = resourcePoint,
            Orders = inWindow.Count(e => e.Kind == Kind.Order),
            Completed = inWindow.Count(e => e.Kind == Kind.End),
            FreeSignals = inWindow.Count(e => e.Kind == Kind.Free),
            BusySeconds = Math.Round(busy, 1),
            BusyPercent = Math.Round(Math.Min(100, busy / windowSeconds * 100), 1),
            IdleSeconds = Math.Round(Math.Max(0, windowSeconds - busy), 1),
            AvgTransportSeconds = transports.Count == 0 ? 0 : Math.Round(transports.Average(p => p.Seconds), 1),
            AvgWaitSeconds = waits.Count == 0 ? 0 : Math.Round(waits.Average(), 1),
            LatestAt = inWindow.Count > 0 ? inWindow.Max(e => e.At) : null,
            Series = RollingSeries(transports, events, from, to, bucketMinutes, stepMinutes),
        };
    }

    /// <summary>
    /// Paart jedes <c>ENDTSP</c> im Fenster mit dem jüngsten passenden <c>TSPORD</c> davor
    /// (gleiche LE-Nummer, sonst FIFO) und gibt Ende und Dauer je Transport zurück.
    /// </summary>
    static List<(DateTime EndAt, double Seconds)> Pair(List<Ev> events, DateTime from, DateTime to)
    {
        // Aufträge etwas vor dem Fenster zulassen: ein Transport darf hineinragen.
        var orders = events.Where(e => e.Kind == Kind.Order && e.At >= from.AddMinutes(-30)).ToList();
        var used = new bool[orders.Count];
        var result = new List<(DateTime, double)>();

        foreach (var done in events.Where(e => e.Kind == Kind.End && e.At >= from && e.At < to))
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
            // Nur den Anteil zählen, der ins Fenster fällt.
            var start = orders[idx].At < from ? from : orders[idx].At;
            result.Add((done.At, (done.At - start).TotalSeconds));
        }

        return result;
    }

    /// <summary>Wartezeiten <c>ENDTSP</c> → nächster <c>TSPORD</c> innerhalb des Fensters.</summary>
    static List<double> Waits(List<Ev> events, DateTime from, DateTime to)
    {
        var waits = new List<double>();
        DateTime? lastEnd = null;

        foreach (var e in events.Where(e => e.At >= from && e.At < to && e.Kind is Kind.Order or Kind.End))
        {
            if (e.Kind == Kind.End)
                lastEnd = e.At;
            else if (lastEnd is { } end)
            {
                waits.Add((e.At - end).TotalSeconds);
                lastEnd = null;
            }
        }

        return waits;
    }

    /// <summary>
    /// Gleitender Verlauf des Belegungsgrads: je Stützpunkt der Anteil der letzten
    /// <paramref name="bucketMinutes"/> Minuten, der belegt war. Letzter Punkt endet bei
    /// <paramref name="to"/>.
    /// </summary>
    static List<UtilizationBucket> RollingSeries(
        List<(DateTime EndAt, double Seconds)> transports, List<Ev> events,
        DateTime from, DateTime to, int bucketMinutes, int stepMinutes)
    {
        var step = Math.Max(1, stepMinutes);
        var win = Math.Max(step, bucketMinutes);
        var fineCount = Math.Max(1, (int)Math.Ceiling((to - from).TotalMinutes / step));
        var winSteps = Math.Max(1, (int)Math.Round(win / (double)step));

        var fineBusy = new double[fineCount];
        var fineDone = new int[fineCount];

        foreach (var (endAt, seconds) in transports)
        {
            var slot = (int)((endAt - from).TotalMinutes / step);
            if (slot < 0 || slot >= fineCount)
                continue;
            fineBusy[slot] += seconds;
            fineDone[slot]++;
        }

        var winSeconds = winSteps * step * 60.0;
        var series = new List<UtilizationBucket>(fineCount);
        double accBusy = 0;
        var accDone = 0;

        for (var i = 0; i < fineCount; i++)
        {
            accBusy += fineBusy[i];
            accDone += fineDone[i];
            if (i >= winSteps)
            {
                accBusy -= fineBusy[i - winSteps];
                accDone -= fineDone[i - winSteps];
            }
            series.Add(new UtilizationBucket
            {
                At = from.AddMinutes((i + 1) * step),
                Count = accDone,
                Uph = Math.Round(Math.Min(100, accBusy / winSeconds * 100), 1),
            });
        }

        return series;
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
