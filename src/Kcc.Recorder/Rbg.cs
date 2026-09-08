namespace Kcc.Recorder;

/// <summary>Spielauswertung einer RBG-Verbindung über das Auslastungsfenster.</summary>
public sealed record RbgCycleStats
{
    public required string Connection { get; init; }

    /// <summary>Abgeschlossene Einlagerungen (Bringen) im Fenster.</summary>
    public required int Puts { get; init; }

    /// <summary>Abgeschlossene Auslagerungen (Holen) im Fenster.</summary>
    public required int Gets { get; init; }

    /// <summary>Doppelspiele = <c>min(Puts, Gets)</c> — gepaarte Ein- und Auslagerung.</summary>
    public required int DoubleCycles { get; init; }

    /// <summary>Einzelspiele = <c>|Puts − Gets|</c> — ungepaarte Einzelfahrten.</summary>
    public required int SingleCycles { get; init; }

    /// <summary>Doppelspiele pro Stunde laut Auslegung — die Bezugsgröße des Leistungsgrads.</summary>
    public required int MaxCyclesPerHour { get; init; }

    /// <summary>Reine Ein- bzw. Auslagerungen pro Stunde laut Auslegung.</summary>
    public required int MaxPutsPerHour { get; init; }
    public required int MaxGetsPerHour { get; init; }

    /// <summary>
    /// Erreichte Spiele pro Stunde in Doppelspiel-Äquivalent. Gerechnet über den Zeitbedarf laut
    /// Auslegung (<see cref="RbgCapacity.DemandSeconds"/>), nicht über die Faustformel
    /// „Einzelspiel = halbes Doppelspiel" — bewusst aus den Fahraufträgen des RBG, nicht aus den
    /// TSPORD-Telegrammen des zugehörigen Ressourcenpunkts.
    /// </summary>
    public required double CyclesPerHour { get; init; }

    /// <summary>
    /// Leistungsgrad gegen die Auslegung: Zeitbedarf der gefahrenen Spiele ÷ Fenster · 100.
    /// Ein Doppelspiel zählt mit <c>3600/DoppelspieleProStunde</c> Sekunden, eine ungepaarte
    /// Ein- oder Auslagerung mit ihrer eigenen Spielzeit. 100 % = das Gerät hat genau seine
    /// Auslegungsleistung erbracht; darüber liegt es über der Auslegung.
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
    public required double AvgGetSeconds { get; init; }

    public required DateTime? LatestAt { get; init; }

    /// <summary>
    /// Gleitender Verlauf über das Fenster: <see cref="UtilizationBucket.Uph"/> = Spiele/h
    /// (Doppelspiel-Äquivalent, also <c>Doppel + Einzel/2</c> je Stunde), <see cref="UtilizationBucket.Count"/>
    /// = abgeschlossene Fahrten im Fenster. Der letzte Punkt endet bei <c>to</c>.
    /// </summary>
    public required IReadOnlyList<UtilizationBucket> Series { get; init; }
}

/// <summary>
/// Auslegungsleistung eines RBG: wie viele Spiele es je Betriebsart in der Stunde schafft.
/// Daraus ergibt sich die Zeit, die ein einzelnes Spiel das Gerät belegt — und damit ein
/// belastbares Verhältnis zwischen Doppel- und Einzelspiel. Ein Einzelspiel ist gerade
/// <em>nicht</em> ein halbes Doppelspiel: laut Datenblatt der HRL-RBG (30 Doppelspiele/h = 120 s,
/// 48 Einlagerungen/h = 75 s) kostet es 62,5 % davon. Dieses Verhältnis ist Gerätekinematik;
/// das Niveau kommt aus der Konfiguration.
/// </summary>
public sealed record RbgCapacity
{
    /// <summary>Doppelspiele pro Stunde laut Auslegung (kombinierte Ein- und Auslagerung).</summary>
    public required int DoubleCyclesPerHour { get; init; }

    /// <summary>Reine Einlagerungen pro Stunde laut Auslegung.</summary>
    public required int PutsPerHour { get; init; }

    /// <summary>Reine Auslagerungen pro Stunde laut Auslegung.</summary>
    public required int GetsPerHour { get; init; }

    public double DoubleSeconds => 3600.0 / Math.Max(1, DoubleCyclesPerHour);
    public double PutSeconds => 3600.0 / Math.Max(1, PutsPerHour);
    public double GetSeconds => 3600.0 / Math.Max(1, GetsPerHour);

    /// <summary>
    /// Zeitbedarf laut Auslegung für die gefahrenen Spiele. Ungepaarte Ein- und Auslagerungen
    /// werden mit ihrer eigenen Spielzeit bewertet, nicht als halbes Doppelspiel.
    /// </summary>
    public double DemandSeconds(int puts, int gets)
    {
        var doubles = Math.Min(puts, gets);
        return doubles * DoubleSeconds
             + (puts - doubles) * PutSeconds
             + (gets - doubles) * GetSeconds;
    }

    public static RbgCapacity From(KccConfig c) => new()
    {
        DoubleCyclesPerHour = c.RbgMaxCyclesPerHour,
        PutsPerHour = c.RbgMaxPutsPerHour,
        GetsPerHour = c.RbgMaxGetsPerHour,
    };

    /// <summary>Kapazität dieses Geräts: Werte am Ressourcenpunkt schlagen die Vorgabe.</summary>
    public RbgCapacity For(ResourcePointConfig? point) => point is null ? this : new()
    {
        DoubleCyclesPerHour = point.MaxCyclesPerHour is > 0 ? point.MaxCyclesPerHour.Value : DoubleCyclesPerHour,
        PutsPerHour = point.MaxPutsPerHour is > 0 ? point.MaxPutsPerHour.Value : PutsPerHour,
        GetsPerHour = point.MaxGetsPerHour is > 0 ? point.MaxGetsPerHour.Value : GetsPerHour,
    };
}

/// <summary>MessageCode-Sätze der RBG-Spielauswertung (aus <see cref="KccConfig"/>).</summary>
public sealed record RbgOptions
{
    /// <summary>Auslegungsleistung als Vorgabe; je Gerät über den Ressourcenpunkt überschreibbar.</summary>
    public required RbgCapacity Capacity { get; init; }

    /// <summary>
    /// Telegrammtyp, der gezählt wird. Die Anlage schickt jedes Ereignis als Paar — <c>DM</c>
    /// (Data Message, die Meldung selbst) und <c>AK</c> (Acknowledge der Gegenstelle) mit
    /// identischem Inhalt. Ohne diese Einschränkung zählt jede Fahrt doppelt.
    /// </summary>
    public required string CountTelegramType { get; init; }

    public required IReadOnlyList<string> PutDoneCodes { get; init; }
    public required IReadOnlyList<string> GetDoneCodes { get; init; }
    public required IReadOnlyList<string> StoreOrderCodes { get; init; }
    public required IReadOnlyList<string> RetrieveOrderCodes { get; init; }

    static IReadOnlyList<string> Or(List<string> configured, IReadOnlyList<string> fallback) =>
        configured is { Count: > 0 } ? configured : fallback;

    public static RbgOptions From(KccConfig c) => new()
    {
        Capacity = RbgCapacity.From(c),
        CountTelegramType = c.CountTelegramType,
        PutDoneCodes = Or(c.RbgPutDoneCodes, RbgReport.DefaultPutDone),
        GetDoneCodes = Or(c.RbgGetDoneCodes, RbgReport.DefaultGetDone),
        StoreOrderCodes = Or(c.RbgStoreOrderCodes, RbgReport.DefaultStoreOrder),
        RetrieveOrderCodes = Or(c.RbgRetrieveOrderCodes, RbgReport.DefaultRetrieveOrder),
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
    public static readonly IReadOnlyList<string> DefaultGetDone = ["ENDPUP"];
    public static readonly IReadOnlyList<string> DefaultStoreOrder = ["DEPORD"];
    public static readonly IReadOnlyList<string> DefaultRetrieveOrder = ["PUPORD"];

    const double DedupWindowSeconds = 10;

    enum Kind { PutDone, GetDone, StoreOrder, RetrieveOrder }

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
        var seqIdx = FieldIndex(format, "SequenceNumber");

        var putDone = Set(options.PutDoneCodes);
        var getDone = Set(options.GetDoneCodes);
        var storeOrder = Set(options.StoreOrderCodes);
        var retrieveOrder = Set(options.RetrieveOrderCodes);

        Kind? Classify(string code) =>
            putDone.Contains(code) ? Kind.PutDone
            : getDone.Contains(code) ? Kind.GetDone
            : storeOrder.Contains(code) ? Kind.StoreOrder
            : retrieveOrder.Contains(code) ? Kind.RetrieveOrder
            : null;

        var byConnection = new Dictionary<string, List<Ev>>(StringComparer.OrdinalIgnoreCase);
        var lastSeen = new Dictionary<(string, string, string, string, string), DateTime>();
        var typeFilter = new TelegramTypeFilter(format, options.CountTelegramType);

        foreach (var t in window.OrderBy(t => t.DateTime))
        {
            var connection = (t.ConnectionName ?? "").Trim();
            if (connection.Length == 0 || !wanted(connection))
                continue;

            var f = format.Slice(t.Data);
            if (!typeFilter.Accepts(f))    // DM/AK-Paar: nur eines der beiden zählen
                continue;

            var code = Field(f, mcIdx);
            if (Classify(code) is not { } kind)
                continue;

            // Die SequenceNumber identifiziert das Ereignis: DM und AK teilen sie sich, zwei
            // echte Fahrten haben verschiedene. Damit trennt der Schlüssel Wiederholungen von
            // gleichartigen Folgeereignissen.
            var label = Field(f, labelIdx);
            var key = (connection, Field(f, seqIdx) + "|" + code, label,
                       Field(f, srcIdx), Field(f, dstIdx));
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
                if (e.At < from || e.At >= to || e.Kind is not (Kind.PutDone or Kind.GetDone))
                    continue;
                var row = Row(e.At, connection);
                if (e.Kind == Kind.PutDone) row.Puts++;
                else row.Gets++;
            }

            foreach (var kind in new[] { Kind.PutDone, Kind.GetDone })
            {
                var order = kind == Kind.PutDone ? Kind.StoreOrder : Kind.RetrieveOrder;
                foreach (var (doneAt, seconds) in PairDurations(events, kind, order, from, to))
                    Row(doneAt, connection).BusySeconds += seconds;
            }
        }

        return rows.Values
            .Where(r => r.Puts > 0 || r.Gets > 0)
            .OrderBy(r => r.Bucket).ThenBy(r => r.Connection, StringComparer.Ordinal)
            .ToList();
    }

    public static RbgCycleStats Compute(
        IReadOnlyList<Telegram> window,
        TelegramFormat format,
        string connection,
        RbgCapacity capacity,
        DateTime from,
        DateTime to,
        RbgOptions options,
        int bucketMinutes = 5,
        int stepMinutes = 1,
        DateTime? metricsFrom = null)
    {
        var events = EventsByConnection(window, format, options,
            c => string.Equals(c, connection, StringComparison.OrdinalIgnoreCase))
            .Values.FirstOrDefault() ?? [];

        // Die Kennzahlen (Tacho) zählen nur das Trailing-Fenster; der Verlauf behält die volle
        // Historie von 'from' bis 'to'.
        var mFrom = metricsFrom is { } m && m > from && m < to ? m : from;
        var doneInWindow = events.Where(e => e.At >= mFrom && e.At < to).ToList();
        var puts = doneInWindow.Count(e => e.Kind == Kind.PutDone);
        var gets = doneInWindow.Count(e => e.Kind == Kind.GetDone);
        var full = Math.Min(puts, gets);
        var half = Math.Abs(puts - gets);

        // Leistungsgrad über den Zeitbedarf laut Auslegung: ein Doppelspiel belegt das Gerät
        // 3600/DS-pro-Stunde Sekunden, eine ungepaarte Ein-/Auslagerung ihre eigene Spielzeit.
        // Auf eine Stunde hochgerechnet, auch wenn das Messfenster kürzer ist.
        var metricSeconds = Math.Max(1e-9, (to - mFrom).TotalSeconds);
        var demand = capacity.DemandSeconds(puts, gets);
        var load = demand / metricSeconds;
        var cyclesPerHour = load * capacity.DoubleCyclesPerHour;
        var percent = Math.Round(load * 100, 1);

        var putPairs = PairDurations(events, Kind.PutDone, Kind.StoreOrder, mFrom, to);
        var getPairs = PairDurations(events, Kind.GetDone, Kind.RetrieveOrder, mFrom, to);
        var avgPut = putPairs.Count == 0 ? 0 : Math.Round(putPairs.Average(p => p.Seconds), 1);
        var avgGet = getPairs.Count == 0 ? 0 : Math.Round(getPairs.Average(p => p.Seconds), 1);
        var windowSeconds = Math.Max(1e-9, (to - mFrom).TotalSeconds);
        var busy = putPairs.Sum(p => p.Seconds) + getPairs.Sum(p => p.Seconds);
        var idle = Math.Max(0, windowSeconds - busy);

        return new RbgCycleStats
        {
            BusyPercent = Math.Round(Math.Min(100, busy / windowSeconds * 100), 1),
            Series = RollingSeries(events, from, to, bucketMinutes, stepMinutes),
            Connection = connection,
            Puts = puts,
            Gets = gets,
            DoubleCycles = full,
            SingleCycles = half,
            MaxCyclesPerHour = capacity.DoubleCyclesPerHour,
            MaxPutsPerHour = capacity.PutsPerHour,
            MaxGetsPerHour = capacity.GetsPerHour,
            CyclesPerHour = Math.Round(cyclesPerHour, 1),
            Percent = percent,
            IdleSeconds = Math.Round(idle, 1),
            AvgPutSeconds = avgPut,
            AvgGetSeconds = avgGet,
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
        var fineGet = new int[fineCount];
        foreach (var e in events)
        {
            if (e.At < from || e.At >= to || e.Kind is not (Kind.PutDone or Kind.GetDone))
                continue;
            var slot = (int)((e.At - from).TotalMinutes / step);
            if (slot < 0 || slot >= fineCount)
                continue;
            if (e.Kind == Kind.PutDone) finePut[slot]++;
            else fineGet[slot]++;
        }

        var winHours = winSteps * step / 60.0;
        var series = new List<UtilizationBucket>(fineCount);
        int accPut = 0, accGet = 0;
        for (var i = 0; i < fineCount; i++)
        {
            accPut += finePut[i];
            accGet += fineGet[i];
            if (i >= winSteps)
            {
                accPut -= finePut[i - winSteps];
                accGet -= fineGet[i - winSteps];
            }
            var full = Math.Min(accPut, accGet);
            var half = Math.Abs(accPut - accGet);
            series.Add(new UtilizationBucket
            {
                At = from.AddMinutes((i + 1) * step),
                Count = accPut + accGet,
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
