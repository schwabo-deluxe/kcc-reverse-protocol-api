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
    /// Belegungsgrad in Prozent = belegte Zeit ÷ Fenster. Belegt ist der Punkt von
    /// <c>TSPORD</c> bis <c>RPFREE</c>; <c>ENDTSP</c> liegt innerhalb dieser Spanne.
    /// </summary>
    public required double BusyPercent { get; init; }

    /// <summary>Belegte Zeit in Sekunden im Fenster.</summary>
    public required double BusySeconds { get; init; }

    /// <summary>Effektiv leere Zeit in Sekunden = Fenster − belegte Zeit.</summary>
    public required double IdleSeconds { get; init; }

    /// <summary>Ø Dauer einer Belegung: <c>TSPORD</c> → <c>RPFREE</c>.</summary>
    public required double AvgOccupiedSeconds { get; init; }

    /// <summary>Ø Dauer des Fahrauftrags: <c>TSPORD</c> → <c>ENDTSP</c>.</summary>
    public required double AvgTransportSeconds { get; init; }

    /// <summary>
    /// Ø Zeit vom Transportende bis zur Frei-Meldung: <c>ENDTSP</c> → <c>RPFREE</c>. Der Auftrag
    /// ist fertig, der Platz aber noch belegt — lange Zeiten deuten auf Rückstau dahinter.
    /// </summary>
    public required double AvgClearSeconds { get; init; }

    /// <summary>
    /// Ø Leerzeit zwischen zwei Belegungen: <c>RPFREE</c> → nächstes <c>TSPORD</c>. Lange
    /// Leerzeiten heißen, dass die Zuführung davor bremst, nicht dieser Punkt.
    /// </summary>
    public required double AvgIdleSeconds { get; init; }

    public required DateTime? LatestAt { get; init; }

    /// <summary>
    /// Gleitender Verlauf des Belegungsgrads: <see cref="UtilizationBucket.Uph"/> = Prozent,
    /// <see cref="UtilizationBucket.Count"/> = Transportaufträge im Fenster.
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
///   <item><c>TSPORD</c> — Transportauftrag erteilt, der Platz ist ab jetzt belegt</item>
///   <item><c>ENDTSP</c> — Fahrauftrag beendet; der Platz ist noch <em>nicht</em> frei</item>
///   <item><c>RPFREE</c> — der Platz ist effektiv frei</item>
/// </list>
///
/// Belegt ist der Punkt also von <c>TSPORD</c> bis <c>RPFREE</c>, leer von <c>RPFREE</c> bis zum
/// nächsten <c>TSPORD</c>. Statt Ereignisse zu paaren läuft ein Zustandsautomat über den
/// Zeitstrahl — Belegung ist eine Eigenschaft des <em>Platzes</em>, nicht einer LE, und das hält
/// die Rechnung auch bei fehlenden oder doppelten Meldungen stabil.
///
/// Reine Funktion über einem Zeitfenster.
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
        var events = Events(window, format, resourcePoint, options);
        var inWindow = events.Where(e => e.At >= from && e.At < to).ToList();

        var (spans, transports, clears, idles) = Walk(events, from, to);

        var windowSeconds = Math.Max(1e-9, (to - from).TotalSeconds);
        var busy = spans.Sum(s => (s.End - s.Start).TotalSeconds);

        return new ConveyorStats
        {
            ResourcePoint = resourcePoint,
            Orders = inWindow.Count(e => e.Kind == Kind.Order),
            Completed = inWindow.Count(e => e.Kind == Kind.End),
            FreeSignals = inWindow.Count(e => e.Kind == Kind.Free),
            BusySeconds = Math.Round(busy, 1),
            BusyPercent = Math.Round(Math.Min(100, busy / windowSeconds * 100), 1),
            IdleSeconds = Math.Round(Math.Max(0, windowSeconds - busy), 1),
            AvgOccupiedSeconds = Avg(spans.Select(s => (s.End - s.Start).TotalSeconds)),
            AvgTransportSeconds = Avg(transports),
            AvgClearSeconds = Avg(clears),
            AvgIdleSeconds = Avg(idles),
            LatestAt = inWindow.Count > 0 ? inWindow.Max(e => e.At) : null,
            Series = RollingSeries(spans, inWindow, from, to, bucketMinutes, stepMinutes),
        };
    }

    /// <summary>Liest die Ereignisse des Punkts und führt die DM/AK-Doppel zusammen.</summary>
    static List<Ev> Events(
        IReadOnlyList<Telegram> window, TelegramFormat format, string resourcePoint,
        ConveyorOptions options)
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

            var label = Field(f, labelIdx);
            var key = (code, label);
            if (lastSeen.TryGetValue(key, out var prev) && (t.DateTime - prev).TotalSeconds < DedupWindowSeconds)
                continue;
            lastSeen[key] = t.DateTime;
            events.Add(new Ev(t.DateTime, label, kind));
        }

        return events;
    }

    /// <summary>
    /// Läuft über den Zeitstrahl und schneidet Belegungsintervalle heraus. Nebenbei fallen die
    /// Teilzeiten an: Fahrauftrag (Order→End), Räumen (End→Free) und Leerzeit (Free→Order).
    /// </summary>
    static (List<(DateTime Start, DateTime End)> Spans, List<double> Transports,
            List<double> Clears, List<double> Idles)
        Walk(List<Ev> events, DateTime from, DateTime to)
    {
        var spans = new List<(DateTime, DateTime)>();
        List<double> transports = [], clears = [], idles = [];

        DateTime? occupiedSince = null, lastEnd = null, freeSince = null;

        void Release(DateTime at)
        {
            occupiedSince ??= from;              // war schon vor dem Fenster belegt
            AddSpan(spans, occupiedSince.Value, at, from, to);
            occupiedSince = null;
            lastEnd = null;
            freeSince = at;
        }

        foreach (var e in events)
        {
            if (e.At >= to)
                break;

            switch (e.Kind)
            {
                case Kind.Order:
                    if (freeSince is { } free && e.At > free)
                        idles.Add((e.At - free).TotalSeconds);
                    freeSince = null;
                    // Ein zweiter Auftrag ohne zwischenzeitliches RPFREE verlängert dieselbe
                    // Belegung, statt eine neue zu beginnen.
                    occupiedSince ??= e.At;
                    lastEnd = null;
                    break;

                case Kind.End:
                    // Transportende ohne bekannten Beginn: der Punkt war schon vor dem Fenster belegt.
                    occupiedSince ??= from;
                    if (e.At >= from)
                        transports.Add((e.At - Max(occupiedSince.Value, from)).TotalSeconds);
                    lastEnd = e.At;
                    break;

                case Kind.Free:
                    if (lastEnd is { } end && e.At >= end)
                        clears.Add((e.At - end).TotalSeconds);
                    Release(e.At);
                    break;
            }
        }

        // Am Fensterende noch belegt: bis zum rechten Rand zählen.
        if (occupiedSince is { } open)
            AddSpan(spans, open, to, from, to);

        return (spans, transports, clears, idles);
    }

    static void AddSpan(
        List<(DateTime, DateTime)> spans, DateTime start, DateTime end, DateTime from, DateTime to)
    {
        var s = Max(start, from);
        var e = Min(end, to);
        if (e > s)
            spans.Add((s, e));
    }

    /// <summary>
    /// Gleitender Verlauf des Belegungsgrads: je Stützpunkt der Anteil der letzten
    /// <paramref name="bucketMinutes"/> Minuten, in denen der Punkt belegt war. Ein
    /// Belegungsintervall wird dabei anteilig auf alle berührten Raster verteilt.
    /// </summary>
    static List<UtilizationBucket> RollingSeries(
        List<(DateTime Start, DateTime End)> spans, List<Ev> inWindow,
        DateTime from, DateTime to, int bucketMinutes, int stepMinutes)
    {
        var step = Math.Max(1, stepMinutes);
        var win = Math.Max(step, bucketMinutes);
        var fineCount = Math.Max(1, (int)Math.Ceiling((to - from).TotalMinutes / step));
        var winSteps = Math.Max(1, (int)Math.Round(win / (double)step));

        var fineBusy = new double[fineCount];
        var fineOrders = new int[fineCount];

        foreach (var (start, end) in spans)
        {
            var first = (int)((start - from).TotalMinutes / step);
            var last = (int)((end - from).TotalMinutes / step);
            for (var i = Math.Max(0, first); i <= Math.Min(fineCount - 1, last); i++)
            {
                var binStart = from.AddMinutes(i * step);
                var binEnd = binStart.AddMinutes(step);
                var overlap = (Min(end, binEnd) - Max(start, binStart)).TotalSeconds;
                if (overlap > 0)
                    fineBusy[i] += overlap;
            }
        }

        foreach (var e in inWindow.Where(e => e.Kind == Kind.Order))
        {
            var slot = (int)((e.At - from).TotalMinutes / step);
            if (slot >= 0 && slot < fineCount)
                fineOrders[slot]++;
        }

        var winSeconds = winSteps * step * 60.0;
        var series = new List<UtilizationBucket>(fineCount);
        double accBusy = 0;
        var accOrders = 0;

        for (var i = 0; i < fineCount; i++)
        {
            accBusy += fineBusy[i];
            accOrders += fineOrders[i];
            if (i >= winSteps)
            {
                accBusy -= fineBusy[i - winSteps];
                accOrders -= fineOrders[i - winSteps];
            }
            series.Add(new UtilizationBucket
            {
                At = from.AddMinutes((i + 1) * step),
                Count = accOrders,
                Uph = Math.Round(Math.Min(100, accBusy / winSeconds * 100), 1),
            });
        }

        return series;
    }

    static double Avg(IEnumerable<double> values)
    {
        var list = values as IList<double> ?? values.ToList();
        return list.Count == 0 ? 0 : Math.Round(list.Average(), 1);
    }

    static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
    static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

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
