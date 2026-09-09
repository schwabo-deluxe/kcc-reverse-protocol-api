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
    /// Belegungsgrad in Prozent = belegte Zeit ÷ Fenster. Belegt ist der Punkt von der Ankunft
    /// (<c>ENDTSP</c>) bis zum Verlassen (<c>RPFREE</c>); der Weitertransport-Auftrag
    /// (<c>TSPORD</c>) wird innerhalb dieser Spanne erteilt.
    /// </summary>
    public required double BusyPercent { get; init; }

    /// <summary>Belegte Zeit in Sekunden im Fenster.</summary>
    public required double BusySeconds { get; init; }

    /// <summary>Effektiv leere Zeit in Sekunden = Fenster − belegte Zeit.</summary>
    public required double IdleSeconds { get; init; }

    /// <summary>Ø Verweildauer einer Ladeeinheit: <c>ENDTSP</c> → <c>RPFREE</c>.</summary>
    public required double AvgOccupiedSeconds { get; init; }

    /// <summary>
    /// Ø Zeit von der Ankunft bis zum Weitertransport-Auftrag: <c>ENDTSP</c> → <c>TSPORD</c>.
    /// Die Ladeeinheit steht und wartet auf die Entscheidung des MFR — Steuerungszeit, keine
    /// Fahrzeit.
    /// </summary>
    public required double AvgOrderWaitSeconds { get; init; }

    /// <summary>
    /// Ø Zeit vom Auftrag bis zum Verlassen des Punkts: <c>TSPORD</c> → <c>RPFREE</c>. Lange
    /// Zeiten deuten auf Rückstau dahinter — die Ladeeinheit kommt nicht weg.
    /// </summary>
    public required double AvgDepartSeconds { get; init; }

    /// <summary>
    /// Ø Leerzeit zwischen zwei Ladeeinheiten: <c>RPFREE</c> → nächstes <c>ENDTSP</c>. Lange
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

    /// <summary>Telegrammtyp, der gezählt wird — sonst zählt jedes DM/AK-Paar doppelt.</summary>
    public required string CountTelegramType { get; init; }

    static IReadOnlyList<string> Or(List<string> configured, IReadOnlyList<string> fallback) =>
        configured is { Count: > 0 } ? configured : fallback;

    public static ConveyorOptions From(KccConfig c) => new()
    {
        CountTelegramType = c.CountTelegramType,
        OrderCodes = Or(c.ConveyorOrderCodes, ConveyorReport.DefaultOrder),
        EndCodes = Or(c.ConveyorEndCodes, ConveyorReport.DefaultEnd),
        FreeCodes = Or(c.ConveyorFreeCodes, ConveyorReport.DefaultFree),
    };
}

/// <summary>
/// Wertet aus, wie stark ein Fördertechnik-Ressourcenpunkt belegt ist. Ablauf einer Ladeeinheit
/// an einem Punkt (Kardex-Doku „Transportverwaltung Paletten-Fördertechnik"):
/// <list type="number">
///   <item><c>ENDTSP</c> — die SPS meldet die <b>Ankunft</b> auf dem Punkt; ab jetzt belegt</item>
///   <item><c>TSPORD</c> — der MFR erteilt den Auftrag zum <b>Weitertransport</b>; der Punkt ist
///         dabei weiterhin belegt</item>
///   <item><c>RPFREE</c> — die SPS meldet das <b>Verlassen</b> des Punkts; ab jetzt frei</item>
/// </list>
///
/// Belegt = <c>ENDTSP</c> → <c>RPFREE</c> (mit dem <c>TSPORD</c> dazwischen), leer =
/// <c>RPFREE</c> → nächste Ankunft. Teilzeiten: <c>ENDTSP</c>→<c>TSPORD</c> = Wartezeit auf die
/// MFR-Entscheidung, <c>TSPORD</c>→<c>RPFREE</c> = Abtransport.
///
/// Ausgewertet über einen Zustandsautomaten auf dem Zeitstrahl statt über Ereignispaare:
/// Belegung ist eine Eigenschaft des <em>Platzes</em>, nicht der LE; das bleibt auch bei
/// fehlenden oder doppelten Meldungen stabil. Reine Funktion über einem Zeitfenster.
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
        int stepMinutes = 1,
        DateTime? metricsFrom = null)
    {
        var events = Events(window, format, resourcePoint, options);

        // Die Kennzahlen (Tacho) zählen nur das Trailing-Fenster; der Verlauf behält die volle
        // Historie von 'from' bis 'to'. Zwei Läufe über dieselbe kleine Ereignisliste.
        var mFrom = metricsFrom is { } m && m > from && m < to ? m : from;
        var inWindow = events.Where(e => e.At >= mFrom && e.At < to).ToList();

        var (metricSpans, orderWaits, departs, idles) = Walk(events, mFrom, to);
        var (seriesSpans, _, _, _) = mFrom == from ? (metricSpans, orderWaits, departs, idles)
            : Walk(events, from, to);

        var windowSeconds = Math.Max(1e-9, (to - mFrom).TotalSeconds);
        var busy = metricSpans.Sum(s => (s.End - s.Start).TotalSeconds);

        return new ConveyorStats
        {
            ResourcePoint = resourcePoint,
            Orders = inWindow.Count(e => e.Kind == Kind.Order),
            Completed = inWindow.Count(e => e.Kind == Kind.End),
            FreeSignals = inWindow.Count(e => e.Kind == Kind.Free),
            BusySeconds = Math.Round(busy, 1),
            BusyPercent = Math.Round(Math.Min(100, busy / windowSeconds * 100), 1),
            IdleSeconds = Math.Round(Math.Max(0, windowSeconds - busy), 1),
            AvgOccupiedSeconds = Avg(metricSpans.Select(s => (s.End - s.Start).TotalSeconds)),
            AvgOrderWaitSeconds = Avg(orderWaits),
            AvgDepartSeconds = Avg(departs),
            AvgIdleSeconds = Avg(idles),
            LatestAt = inWindow.Count > 0 ? inWindow.Max(e => e.At) : null,
            Series = RollingSeries(seriesSpans, events.Where(e => e.At >= from && e.At < to).ToList(),
                from, to, bucketMinutes, stepMinutes),
        };
    }

    /// <summary>
    /// Belegungsintervalle (Ankunft <c>ENDTSP</c> → Verlassen <c>RPFREE</c>) des Punkts im
    /// Fenster, auf <c>[from, to)</c> beschnitten. Grundlage der Ressourcenpunkt-Langzeitreihe
    /// (<see cref="PointSampleRow"/>) — dort wird die Belegzeit den Rastern anteilig zugeschlagen.
    /// </summary>
    public static IReadOnlyList<(DateTime Start, DateTime End)> OccupancySpans(
        IReadOnlyList<Telegram> window, TelegramFormat format, string resourcePoint,
        DateTime from, DateTime to, ConveyorOptions options)
    {
        var events = Events(window, format, resourcePoint, options);
        var (spans, _, _, _) = Walk(events, from, to);
        return spans;
    }

    /// <summary>Liest die Ereignisse des Punkts und führt die DM/AK-Doppel zusammen.</summary>
    static List<Ev> Events(
        IReadOnlyList<Telegram> window, TelegramFormat format, string resourcePoint,
        ConveyorOptions options)
    {
        var mcIdx = FieldIndex(format, "MessageCode");
        var rpIdx = FieldIndex(format, "ResourcePoint");
        var labelIdx = FieldIndex(format, "ResourceLabel");
        var seqIdx = FieldIndex(format, "SequenceNumber");

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
        var typeFilter = new TelegramTypeFilter(format, options.CountTelegramType);

        foreach (var t in window.OrderBy(t => t.DateTime))
        {
            var f = format.Slice(t.Data);
            if (!typeFilter.Accepts(f))    // DM/AK-Paar: nur eines der beiden zählen
                continue;
            if (!string.Equals(Field(f, rpIdx), resourcePoint, StringComparison.OrdinalIgnoreCase))
                continue;

            var code = Field(f, mcIdx);
            if (Classify(code) is not { } kind)
                continue;

            // Die SequenceNumber identifiziert das Ereignis: DM und AK teilen sie sich, zwei
            // echte Vorgänge haben verschiedene.
            var label = Field(f, labelIdx);
            var key = (Field(f, seqIdx) + "|" + code, label);
            if (lastSeen.TryGetValue(key, out var prev) && (t.DateTime - prev).TotalSeconds < DedupWindowSeconds)
                continue;
            lastSeen[key] = t.DateTime;
            events.Add(new Ev(t.DateTime, label, kind));
        }

        return events;
    }

    /// <summary>
    /// Läuft über den Zeitstrahl und schneidet Belegungsintervalle heraus (Ankunft → Verlassen).
    /// Nebenbei fallen die Teilzeiten an: Wartezeit auf den Auftrag (End→Order), Abtransport
    /// (Order→Free) und Leerzeit (Free→End).
    /// </summary>
    static (List<(DateTime Start, DateTime End)> Spans, List<double> OrderWaits,
            List<double> Departs, List<double> Idles)
        Walk(List<Ev> events, DateTime from, DateTime to)
    {
        var spans = new List<(DateTime, DateTime)>();
        List<double> orderWaits = [], departs = [], idles = [];

        DateTime? occupiedSince = null, orderAt = null, freeSince = null;

        foreach (var e in events)
        {
            if (e.At >= to)
                break;

            switch (e.Kind)
            {
                // Ankunft auf dem Punkt — ab hier belegt.
                case Kind.End:
                    if (freeSince is { } free && e.At > free)
                        idles.Add((e.At - free).TotalSeconds);
                    freeSince = null;
                    // Zweite Ankunft ohne zwischenzeitliches RPFREE: dieselbe Belegung läuft
                    // weiter, statt neu zu beginnen.
                    occupiedSince ??= e.At;
                    orderAt = null;
                    break;

                // Weitertransport-Auftrag — der Punkt bleibt belegt.
                case Kind.Order:
                    // Auftrag ohne bekannte Ankunft: der Punkt war schon vor dem Fenster belegt.
                    occupiedSince ??= from;
                    if (orderAt is null && e.At >= from)
                        orderWaits.Add((e.At - Max(occupiedSince.Value, from)).TotalSeconds);
                    orderAt ??= e.At;
                    break;

                // Verlassen des Punkts — ab hier frei.
                case Kind.Free:
                    occupiedSince ??= from;      // war vor dem Fenster schon belegt
                    if (orderAt is { } ord && e.At >= ord)
                        departs.Add((e.At - ord).TotalSeconds);
                    AddSpan(spans, occupiedSince.Value, e.At, from, to);
                    occupiedSince = null;
                    orderAt = null;
                    freeSince = e.At;
                    break;
            }
        }

        // Am Fensterende noch belegt: bis zum rechten Rand zählen.
        if (occupiedSince is { } open)
            AddSpan(spans, open, to, from, to);

        return (spans, orderWaits, departs, idles);
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
