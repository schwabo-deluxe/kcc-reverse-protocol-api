using System.Text.RegularExpressions;

namespace Kcc.Recorder;

/// <summary>Spielauswertung einer RBG-Verbindung über das Auslastungsfenster.</summary>
public sealed record RbgCycleStats
{
    public required string Connection { get; init; }

    /// <summary>Abgeschlossene Einlagerungen im Fenster — Transporte, die im Regal enden.</summary>
    public required int Stores { get; init; }

    /// <summary>Abgeschlossene Auslagerungen im Fenster — Transporte, die aus dem Regal kommen.</summary>
    public required int Retrievals { get; init; }

    /// <summary>Abgeschlossene Transporte gesamt = <see cref="Stores"/> + <see cref="Retrievals"/>.</summary>
    public required int Transports { get; init; }

    /// <summary>
    /// Doppelspiele = <c>min(Stores, Retrievals)</c>: eine Ein- und eine Auslagerung, die sich zu
    /// einer kombinierten Fahrt paaren lassen. Ein Doppelspiel besteht aus <b>zwei</b> Transporten.
    /// </summary>
    public required int DoubleCycles { get; init; }

    /// <summary>Einzelspiele = <c>|Stores − Retrievals|</c> — Transporte ohne Gegenstück.</summary>
    public required int SingleCycles { get; init; }

    /// <summary>Doppelspiele pro Stunde laut Auslegung — die Bezugsgröße des Leistungsgrads.</summary>
    public required int MaxCyclesPerHour { get; init; }

    /// <summary>Reine Ein- bzw. Auslagerungen pro Stunde laut Auslegung.</summary>
    public required int MaxStoresPerHour { get; init; }
    public required int MaxRetrievalsPerHour { get; init; }

    /// <summary>
    /// Erreichte Spiele pro Stunde in Doppelspiel-Äquivalent. Gerechnet über den Zeitbedarf laut
    /// Auslegung (<see cref="RbgCapacity.DemandSeconds"/>), nicht über die Faustformel
    /// „Einzelspiel = halbes Doppelspiel" — aus den Fahraufträgen des RBG, nicht aus den
    /// TSPORD-Telegrammen des zugehörigen Ressourcenpunkts.
    /// </summary>
    public required double CyclesPerHour { get; init; }

    /// <summary>
    /// Leistungsgrad gegen die Auslegung: Zeitbedarf der gefahrenen Spiele ÷ Fenster · 100.
    /// Ein Doppelspiel zählt mit <c>3600/DoppelspieleProStunde</c> Sekunden, ein Einzelspiel mit
    /// seiner eigenen Spielzeit. 100 % = das Gerät hat genau seine Auslegungsleistung erbracht.
    /// </summary>
    public required double Percent { get; init; }

    /// <summary>
    /// Zeitbasierter Auslastungsgrad in Prozent = Summe der Transportdauern ÷ Fenster. Die
    /// Transporte eines RBG laufen nacheinander, der Wert ist damit ein echtes Zeitmaß.
    /// </summary>
    public required double BusyPercent { get; init; }

    /// <summary>Leerlaufzeit in Sekunden im Fenster = Fenster − Summe der Transportdauern.</summary>
    public required double IdleSeconds { get; init; }

    /// <summary>Ø Dauer eines Einlagertransports (Auftrag bis Abgabe im Regal).</summary>
    public required double AvgStoreSeconds { get; init; }

    /// <summary>Ø Dauer eines Auslagertransports (Auftrag bis Abgabe an der Station).</summary>
    public required double AvgRetrieveSeconds { get; init; }

    public required DateTime? LatestAt { get; init; }

    /// <summary>
    /// Gleitender Verlauf über das Fenster: <see cref="UtilizationBucket.Uph"/> = Spiele/h in
    /// Doppelspiel-Äquivalent, <see cref="UtilizationBucket.Count"/> = abgeschlossene Transporte
    /// im gleitenden Fenster. Der letzte Punkt endet bei <c>to</c>.
    /// </summary>
    public required IReadOnlyList<UtilizationBucket> Series { get; init; }
}

/// <summary>
/// Auslegungsleistung eines RBG je Betriebsart. Daraus folgt die Zeit, die ein Spiel das Gerät
/// belegt, und damit das Verhältnis Einzel- zu Doppelspiel: laut Datenblatt der HRL-RBG
/// (30 Doppelspiele/h = 120 s, 48 Einlagerungen/h = 75 s) kostet ein Einzelspiel 62,5 % eines
/// Doppelspiels — nicht 50 %.
/// </summary>
public sealed record RbgCapacity
{
    /// <summary>Doppelspiele pro Stunde laut Auslegung (kombinierte Ein- und Auslagerung).</summary>
    public required int DoubleCyclesPerHour { get; init; }

    /// <summary>Reine Einlagerungen pro Stunde laut Auslegung.</summary>
    public required int StoresPerHour { get; init; }

    /// <summary>Reine Auslagerungen pro Stunde laut Auslegung.</summary>
    public required int RetrievalsPerHour { get; init; }

    public double DoubleSeconds => 3600.0 / Math.Max(1, DoubleCyclesPerHour);
    public double StoreSeconds => 3600.0 / Math.Max(1, StoresPerHour);
    public double RetrieveSeconds => 3600.0 / Math.Max(1, RetrievalsPerHour);

    /// <summary>
    /// Zeitbedarf laut Auslegung für die gefahrenen Spiele. Ungepaarte Ein- und Auslagerungen
    /// werden mit ihrer eigenen Spielzeit bewertet, nicht als halbes Doppelspiel.
    /// </summary>
    public double DemandSeconds(int stores, int retrievals)
    {
        var doubles = Math.Min(stores, retrievals);
        return doubles * DoubleSeconds
             + (stores - doubles) * StoreSeconds
             + (retrievals - doubles) * RetrieveSeconds;
    }

    public static RbgCapacity From(KccConfig c) => new()
    {
        DoubleCyclesPerHour = c.RbgMaxCyclesPerHour,
        StoresPerHour = c.RbgMaxStoresPerHour,
        RetrievalsPerHour = c.RbgMaxRetrievalsPerHour,
    };

    /// <summary>Auslegung dieses Geräts: Werte am Ressourcenpunkt schlagen die Vorgabe.</summary>
    public RbgCapacity For(ResourcePointConfig? point) => point is null ? this : new()
    {
        DoubleCyclesPerHour = point.MaxCyclesPerHour is > 0 ? point.MaxCyclesPerHour.Value : DoubleCyclesPerHour,
        StoresPerHour = point.MaxStoresPerHour is > 0 ? point.MaxStoresPerHour.Value : StoresPerHour,
        RetrievalsPerHour = point.MaxRetrievalsPerHour is > 0 ? point.MaxRetrievalsPerHour.Value : RetrievalsPerHour,
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
    /// identischem Inhalt.
    /// </summary>
    public required string CountTelegramType { get; init; }

    /// <summary>Pickup Order (<c>PUPORD</c>) — Auftrag, eine Ladeeinheit aufs RBG zu nehmen.</summary>
    public required IReadOnlyList<string> PickupOrderCodes { get; init; }

    /// <summary>Pickup abgeschlossen (<c>ENDPUP</c>).</summary>
    public required IReadOnlyList<string> PickupDoneCodes { get; init; }

    /// <summary>Depot Order (<c>DEPORD</c>) — Auftrag, eine Ladeeinheit vom RBG abzugeben.</summary>
    public required IReadOnlyList<string> DepotOrderCodes { get; init; }

    /// <summary>Depot abgeschlossen (<c>ENDDEP</c>) — beendet den Transport.</summary>
    public required IReadOnlyList<string> DepotDoneCodes { get; init; }

    /// <summary>
    /// Muster eines Regalplatzes in <c>Source</c>/<c>Destination</c>. Regalplätze sind rein
    /// numerisch (z. B. <c>010361211</c>), Übergabestationen tragen Buchstaben (<c>MA41</c>,
    /// <c>SR01LU11</c>). Daran hängt die Richtung des Transports.
    /// </summary>
    public required string RackLocationPattern { get; init; }

    static IReadOnlyList<string> Or(List<string> configured, IReadOnlyList<string> fallback) =>
        configured is { Count: > 0 } ? configured : fallback;

    public static RbgOptions From(KccConfig c) => new()
    {
        Capacity = RbgCapacity.From(c),
        CountTelegramType = c.CountTelegramType,
        PickupOrderCodes = Or(c.RbgPickupOrderCodes, RbgReport.DefaultPickupOrder),
        PickupDoneCodes = Or(c.RbgPickupDoneCodes, RbgReport.DefaultPickupDone),
        DepotOrderCodes = Or(c.RbgDepotOrderCodes, RbgReport.DefaultDepotOrder),
        DepotDoneCodes = Or(c.RbgDepotDoneCodes, RbgReport.DefaultDepotDone),
        RackLocationPattern = string.IsNullOrWhiteSpace(c.RbgRackLocationPattern)
            ? RbgReport.DefaultRackLocationPattern
            : c.RbgRackLocationPattern,
    };
}

/// <summary>
/// Wertet die Fahraufträge einer RBG-Verbindung aus.
///
/// <b>Ablauf eines Transports</b> (aus den Anlagentelegrammen belegt): eine <c>Pickup Order</c>
/// (<c>PUPORD</c> → <c>ENDPUP</c>) nimmt eine Ladeeinheit aufs RBG, eine <c>Depot Order</c>
/// (<c>DEPORD</c> → <c>ENDDEP</c>) gibt sie wieder ab. Beides passiert bei <em>jedem</em>
/// Transport — die Aufträge sind kein Ein- bzw. Auslagerauftrag. Die Richtung steckt in
/// Quelle und Ziel:
/// <list type="bullet">
///   <item><b>Einlagerung</b>: von einer Station ins Regal — das <c>ENDDEP</c> hat einen
///         Regalplatz als Ziel (rein numerisch, z. B. <c>010361211</c>)</item>
///   <item><b>Auslagerung</b>: aus dem Regal an eine Station — das <c>ENDDEP</c> hat eine
///         Station als Ziel (<c>MA62</c>)</item>
/// </list>
///
/// Begriffe nach FEM 9.851: ein <b>Einzelspiel</b> ist eine reine Ein- oder Auslagerung, ein
/// <b>Doppelspiel</b> beides in einer Fahrt. Ein Doppelspiel besteht damit aus <b>zwei</b>
/// Transporten — daraus Doppelspiele = <c>min(Ein, Aus)</c>, Einzelspiele = <c>|Ein − Aus|</c>.
///
/// Die Anlage sendet jedes Ereignis doppelt (<c>DM</c>/<c>AK</c>) — das wird zusammengeführt.
/// Reine Funktion über einem Zeitfenster.
/// </summary>
public static class RbgReport
{
    public static readonly IReadOnlyList<string> DefaultPickupOrder = ["PUPORD"];
    public static readonly IReadOnlyList<string> DefaultPickupDone = ["ENDPUP"];
    public static readonly IReadOnlyList<string> DefaultDepotOrder = ["DEPORD"];
    public static readonly IReadOnlyList<string> DefaultDepotDone = ["ENDDEP"];

    /// <summary>Regalplätze sind rein numerisch; Stationen tragen Buchstaben.</summary>
    public const string DefaultRackLocationPattern = "^[0-9]+$";

    const double DedupWindowSeconds = 10;

    enum Kind { PickupOrder, PickupDone, DepotOrder, DepotDone }

    readonly record struct Ev(DateTime At, string Label, Kind Kind, string Source, string Destination);

    /// <summary>Ein abgeschlossener Transport: Beginn, Ende und Richtung.</summary>
    readonly record struct Move(DateTime Start, DateTime End, bool IsStore)
    {
        public double Seconds => (End - Start).TotalSeconds;
    }

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

        var pickupOrder = Set(options.PickupOrderCodes);
        var pickupDone = Set(options.PickupDoneCodes);
        var depotOrder = Set(options.DepotOrderCodes);
        var depotDone = Set(options.DepotDoneCodes);

        Kind? Classify(string code) =>
            depotDone.Contains(code) ? Kind.DepotDone
            : pickupDone.Contains(code) ? Kind.PickupDone
            : depotOrder.Contains(code) ? Kind.DepotOrder
            : pickupOrder.Contains(code) ? Kind.PickupOrder
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
            // echte Fahrten haben verschiedene.
            var label = Field(f, labelIdx);
            var source = Field(f, srcIdx);
            var destination = Field(f, dstIdx);
            var key = (connection, Field(f, seqIdx) + "|" + code, label, source, destination);
            if (lastSeen.TryGetValue(key, out var prev) && (t.DateTime - prev).TotalSeconds < DedupWindowSeconds)
                continue;
            lastSeen[key] = t.DateTime;

            if (!byConnection.TryGetValue(connection, out var list))
                byConnection[connection] = list = [];
            list.Add(new Ev(t.DateTime, label, kind, source, destination));
        }

        return byConnection;
    }

    /// <summary>
    /// Bildet aus den Ereignissen die abgeschlossenen Transporte. Jedes <c>ENDDEP</c> beendet
    /// genau einen Transport; sein Ziel sagt die Richtung. Der Beginn ist der jüngste
    /// <c>PUPORD</c> derselben Ladeeinheit davor — sonst der <c>DEPORD</c>, sonst das Ende selbst.
    /// </summary>
    static List<Move> Moves(List<Ev> events, Regex rack)
    {
        var moves = new List<Move>();
        var pickOrders = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        var dropOrders = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        foreach (var e in events)
        {
            switch (e.Kind)
            {
                case Kind.PickupOrder:
                    pickOrders[e.Label] = e.At;
                    break;
                case Kind.DepotOrder:
                    dropOrders[e.Label] = e.At;
                    break;
                case Kind.DepotDone:
                    var start = pickOrders.TryGetValue(e.Label, out var p) ? p
                        : dropOrders.TryGetValue(e.Label, out var d) ? d
                        : e.At;
                    pickOrders.Remove(e.Label);
                    dropOrders.Remove(e.Label);
                    moves.Add(new Move(start, e.At, rack.IsMatch(e.Destination)));
                    break;
            }
        }

        return moves;
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

        var rack = new Regex(options.RackLocationPattern, RegexOptions.CultureInvariant);
        var moves = Moves(events, rack);

        // Die Kennzahlen (Tacho) zählen nur das Trailing-Fenster; der Verlauf behält die volle
        // Historie von 'from' bis 'to'.
        var mFrom = metricsFrom is { } m && m > from && m < to ? m : from;
        var inWindow = moves.Where(v => v.End >= mFrom && v.End < to).ToList();

        var stores = inWindow.Count(v => v.IsStore);
        var retrievals = inWindow.Count - stores;
        var doubles = Math.Min(stores, retrievals);
        var singles = Math.Abs(stores - retrievals);

        // Leistungsgrad über den Zeitbedarf laut Auslegung, auf eine Stunde hochgerechnet.
        var metricSeconds = Math.Max(1e-9, (to - mFrom).TotalSeconds);
        var load = capacity.DemandSeconds(stores, retrievals) / metricSeconds;

        // Transporte laufen nacheinander — die Summe ihrer Dauern ist die belegte Zeit.
        var busy = inWindow.Sum(v => Math.Max(0, (v.End - Max(v.Start, mFrom)).TotalSeconds));

        return new RbgCycleStats
        {
            Connection = connection,
            Stores = stores,
            Retrievals = retrievals,
            Transports = inWindow.Count,
            DoubleCycles = doubles,
            SingleCycles = singles,
            MaxCyclesPerHour = capacity.DoubleCyclesPerHour,
            MaxStoresPerHour = capacity.StoresPerHour,
            MaxRetrievalsPerHour = capacity.RetrievalsPerHour,
            CyclesPerHour = Math.Round(load * capacity.DoubleCyclesPerHour, 1),
            Percent = Math.Round(load * 100, 1),
            BusyPercent = Math.Round(Math.Min(100, busy / metricSeconds * 100), 1),
            IdleSeconds = Math.Round(Math.Max(0, metricSeconds - busy), 1),
            AvgStoreSeconds = Avg(inWindow.Where(v => v.IsStore).Select(v => v.Seconds)),
            AvgRetrieveSeconds = Avg(inWindow.Where(v => !v.IsStore).Select(v => v.Seconds)),
            LatestAt = inWindow.Count > 0 ? inWindow.Max(v => v.End) : null,
            Series = RollingSeries(moves, capacity, from, to, bucketMinutes, stepMinutes),
        };
    }

    /// <summary>
    /// Verdichtet den Telegrammstrom zu Rasterzeilen je Zeitraster × RBG-Verbindung — die
    /// Grundlage der Langzeitaufzeichnung (<c>/rbg</c>). Ein Durchlauf für alle Verbindungen;
    /// die Transportdauer wird dem Raster des Abschlusses zugeschlagen.
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

        var rack = new Regex(options.RackLocationPattern, RegexOptions.CultureInvariant);
        var rows = new Dictionary<(long Slot, string Connection), RbgSampleRow>();

        foreach (var (connection, events) in EventsByConnection(window, format, options, wanted.Contains))
        {
            foreach (var move in Moves(events, rack))
            {
                if (move.End < from || move.End >= to)
                    continue;

                var slot = (long)((move.End - from).Ticks / step.Ticks);
                var key = (slot, connection);
                if (!rows.TryGetValue(key, out var row))
                    rows[key] = row = new RbgSampleRow
                    {
                        Bucket = from + TimeSpan.FromTicks(slot * step.Ticks),
                        Connection = connection,
                    };

                if (move.IsStore) row.Stores++;
                else row.Retrievals++;
                row.BusySeconds += Math.Max(0, (move.End - Max(move.Start, from)).TotalSeconds);
            }
        }

        return rows.Values
            .Where(r => r.Stores > 0 || r.Retrievals > 0)
            .OrderBy(r => r.Bucket).ThenBy(r => r.Connection, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Gleitender Verlauf der Spiele/h: feine Abtastung der abgeschlossenen Transporte (Schritt),
    /// je Stützpunkt die letzten <paramref name="bucketMinutes"/> Minuten, umgerechnet auf
    /// Doppelspiel-Äquivalent pro Stunde. Letzter Stützpunkt endet bei <paramref name="to"/>.
    /// </summary>
    static List<UtilizationBucket> RollingSeries(
        List<Move> moves, RbgCapacity capacity, DateTime from, DateTime to,
        int bucketMinutes, int stepMinutes)
    {
        var step = Math.Max(1, stepMinutes);
        var win = Math.Max(step, bucketMinutes);
        var fineCount = Math.Max(1, (int)Math.Ceiling((to - from).TotalMinutes / step));
        var winSteps = Math.Max(1, (int)Math.Round(win / (double)step));

        var fineStores = new int[fineCount];
        var fineRetrievals = new int[fineCount];
        foreach (var v in moves)
        {
            if (v.End < from || v.End >= to)
                continue;
            var slot = (int)((v.End - from).TotalMinutes / step);
            if (slot < 0 || slot >= fineCount)
                continue;
            if (v.IsStore) fineStores[slot]++;
            else fineRetrievals[slot]++;
        }

        var winSeconds = winSteps * step * 60.0;
        var series = new List<UtilizationBucket>(fineCount);
        int accStores = 0, accRetrievals = 0;

        for (var i = 0; i < fineCount; i++)
        {
            accStores += fineStores[i];
            accRetrievals += fineRetrievals[i];
            if (i >= winSteps)
            {
                accStores -= fineStores[i - winSteps];
                accRetrievals -= fineRetrievals[i - winSteps];
            }
            var load = capacity.DemandSeconds(accStores, accRetrievals) / winSeconds;
            series.Add(new UtilizationBucket
            {
                At = from.AddMinutes((i + 1) * step),
                Count = accStores + accRetrievals,
                Uph = Math.Round(load * capacity.DoubleCyclesPerHour, 1),
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
