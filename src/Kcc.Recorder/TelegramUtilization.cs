namespace Kcc.Recorder;

/// <summary>Ein Zeitraster-Eimer der Auslastung: Menge und hochgerechnete UPH.</summary>
public sealed record UtilizationBucket
{
    /// <summary>Beginn des Intervalls.</summary>
    public required DateTime At { get; init; }

    public required int Count { get; init; }

    /// <summary>Auf eine Stunde hochgerechnete Menge dieses Intervalls.</summary>
    public required double Uph { get; init; }
}

/// <summary>Anteil eines Endziels an den Aufträgen eines Ressourcenpunkts.</summary>
public sealed record DestinationShare
{
    /// <summary>Erste 4 Zeichen des letzten 33-Zeichen-Blocks im Datenfeld (Excel: <c>LINKS(RECHTS(D;33);4)</c>).</summary>
    public required string Target { get; init; }

    /// <summary>Anzeigetext inkl. Klartext aus <c>DestinationLabels</c>, z. B. <c>GA51 (Kommissionierung)</c>.</summary>
    public required string Label { get; init; }

    public required int Count { get; init; }

    /// <summary>Anteil an den <c>TSPORD</c>-Telegrammen des Punkts über das ganze Fenster in Prozent.</summary>
    public required double Percent { get; init; }
}

/// <summary>Auslastung eines Ressourcenpunkts über das betrachtete Zeitfenster.</summary>
public sealed record ResourcePointUtilization
{
    public required string ResourcePoint { get; init; }

    /// <summary>Klartext-Bezeichnung fürs Dashboard (Fallback: <see cref="ResourcePoint"/>).</summary>
    public required string Label { get; init; }

    /// <summary>Gruppe zur Strukturierung im Dashboard.</summary>
    public required string Group { get; init; }

    /// <summary>Anzahl der <c>TSPORD</c>-Telegramme dieses Punkts über das ganze Fenster.</summary>
    public required int Count { get; init; }

    /// <summary>Anzahl im jüngsten <see cref="TelegramUtilization.RateMinutes"/>-Fenster — Basis für <see cref="Uph"/>.</summary>
    public required int RateCount { get; init; }

    /// <summary>
    /// Auf eine Stunde hochgerechnete Menge (Units per Hour) — aus <see cref="RateCount"/> der
    /// letzten Minuten, damit kleine Stöße sofort durchschlagen.
    /// </summary>
    public required double Uph { get; init; }

    /// <summary>UPH-Richtwert dieses Punkts (eigener aus der Konfiguration oder der Vorgabewert).</summary>
    public required double TargetUph { get; init; }

    /// <summary>Anteil an <see cref="TargetUph"/> in Prozent.</summary>
    public required double Percent { get; init; }

    public required int Errors { get; init; }
    public required DateTime? LatestAt { get; init; }

    /// <summary>Verlauf über das Zeitraster, lückenlos vom Fensterbeginn bis jetzt.</summary>
    public required IReadOnlyList<UtilizationBucket> Series { get; init; }

    /// <summary>Endziele mit ihrem Anteil, absteigend nach Menge.</summary>
    public required IReadOnlyList<DestinationShare> Destinations { get; init; }
}

/// <summary>Zusammenfassung einer Gruppe von Ressourcenpunkten.</summary>
public sealed record UtilizationGroup
{
    public required string Name { get; init; }
    public required int Count { get; init; }
    public required int RateCount { get; init; }
    public required double Uph { get; init; }

    /// <summary>Summe der Richtwerte der Punkte dieser Gruppe.</summary>
    public required double TargetUph { get; init; }

    /// <summary>Mittlere Auslastung der Punkte dieser Gruppe in Prozent (jeder gegen seinen Richtwert).</summary>
    public required double Percent { get; init; }

    public required int Errors { get; init; }

    /// <summary>Namen der Punkte dieser Gruppe, in Eingabereihenfolge.</summary>
    public required IReadOnlyList<string> Points { get; init; }

    /// <summary>Summierter Verlauf über alle Punkte der Gruppe.</summary>
    public required IReadOnlyList<UtilizationBucket> Series { get; init; }
}

/// <summary>
/// Auslastung je Ressourcenpunkt, gemessen an den Auftragstelegrammen (<c>MessageCode</c>
/// <c>TSPORD</c>). Reine Funktion über einem Zeitfenster — die API und die Tests rechnen damit
/// gleichermaßen, ohne Datenbank.
/// </summary>
public sealed record TelegramUtilization
{
    /// <summary>Auf diesen <c>MessageCode</c> stützt sich die Messung.</summary>
    public const string MessageCode = "TSPORD";

    /// <summary>Ressourcenpunkte samt Gruppe, die das Dashboard ohne Konfiguration ausweist.</summary>
    public static readonly IReadOnlyList<ResourcePointConfig> DefaultResourcePoints =
    [
        new() { Name = "MA72", Group = "Auslagerung RBG" },
        new() { Name = "MB72", Group = "Auslagerung RBG" },
        new() { Name = "MC72", Group = "Auslagerung RBG" },
        new() { Name = "MD72", Group = "Auslagerung RBG" },
        new() { Name = "DA21", Group = "Fördertechnik" },
        new() { Name = "LB41", Group = "Fördertechnik" },
        new() { Name = "EA21", Group = "Fördertechnik" },
        new() { Name = "LD51", Group = "Fördertechnik" },
        new() { Name = "MFA1", Group = "Fördertechnik" },
        new() { Name = "ME71", Group = "Fördertechnik" },
        new() { Name = "EB31", Group = "Fördertechnik" },
    ];

    public required int WindowMinutes { get; init; }
    public required DateTime From { get; init; }
    public required DateTime To { get; init; }

    /// <summary>Richtwert in Einheiten pro Stunde, auf den sich die Prozentwerte beziehen.</summary>
    public required double TargetUph { get; init; }

    /// <summary>Alle <c>TSPORD</c>-Telegramme im Fenster — auch die ohne gelisteten Punkt.</summary>
    public required int TotalOrders { get; init; }

    /// <summary>Breite des gleitenden Fensters der Verlaufskurven in Minuten (Glättung).</summary>
    public required int BucketMinutes { get; init; }

    /// <summary>Abtastschritt der Verlaufskurven in Minuten — ein Stützpunkt je Schritt.</summary>
    public required int SeriesStepMinutes { get; init; }

    /// <summary>Trailing-Fenster in Minuten, aus dem <see cref="ResourcePointUtilization.Uph"/> hochgerechnet wird.</summary>
    public required int RateMinutes { get; init; }

    public required IReadOnlyList<ResourcePointUtilization> Points { get; init; }

    /// <summary>Dieselben Punkte nach <see cref="ResourcePointConfig.Group"/> zusammengefasst.</summary>
    public required IReadOnlyList<UtilizationGroup> Groups { get; init; }

    /// <param name="windowEnd">
    /// Rechter Rand des Fensters — üblicherweise der Zeitstempel des jüngsten Telegramms, nicht
    /// die Host-Uhr, damit das Fenster unabhängig von der Zeitzone der Anlage sitzt.
    /// </param>
    public static TelegramUtilization Compute(
        IReadOnlyList<Telegram> window,
        TelegramFormat format,
        int windowMinutes,
        double targetUph,
        DateTime windowEnd,
        IReadOnlyList<ResourcePointConfig>? resourcePoints = null,
        int bucketMinutes = 5,
        int rateMinutes = 5,
        IReadOnlyDictionary<string, string>? destinationLabels = null,
        IReadOnlyList<string>? groupOrder = null,
        int seriesStepMinutes = 1)
    {
        var destMap = new DestinationMap(destinationLabels);
        var now = windowEnd;
        var rate = Math.Max(1, rateMinutes);
        var rateFrom = now.AddMinutes(-rate);

        // Nur Einträge mit Namen; je Name der erste gewinnt.
        var defs = (resourcePoints is { Count: > 0 } ? resourcePoints : DefaultResourcePoints)
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        if (defs.Count == 0)
            defs = DefaultResourcePoints.ToList();

        // Reihenfolge: erst nach optionalem Order-Schlüssel, dann nach Listenposition (stabil).
        defs = defs
            .Select((Def, Index) => (Def, Index))
            .OrderBy(x => x.Def.Order ?? int.MaxValue)
            .ThenBy(x => x.Index)
            .Select(x => x.Def)
            .ToList();

        var names = defs.Select(d => d.Name).ToList();
        var messageCode = FieldIndex(format, "MessageCode");
        var resourcePoint = FieldIndex(format, "ResourcePoint");
        var errorCode = FieldIndex(format, "ErrorCode");

        var stats = names.ToDictionary(
            n => n,
            _ => (Count: 0, Recent: 0, Errors: 0, Latest: (DateTime?)null),
            StringComparer.OrdinalIgnoreCase);

        // Endziel je Punkt (kanonisch, Muster wie DLL* zusammengefasst).
        var destinations = names.ToDictionary(
            n => n,
            _ => new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        // Verlauf als gleitendes Fenster: fein abtasten (Schritt), dann je Stützpunkt die
        // Menge der letzten 'bucketWidth' Minuten summieren. Der letzte Stützpunkt endet bei
        // 'now' und zeigt damit den aktuell laufenden Trailing-Wert („developing").
        var step = Math.Max(1, seriesStepMinutes);
        var bucketWidth = Math.Max(step, bucketMinutes);        // Glättungsfenster
        var fineCount = Math.Max(1, (int)Math.Ceiling(windowMinutes / (double)step));
        var winSteps = Math.Max(1, (int)Math.Round(bucketWidth / (double)step));   // Fenster in Feinschritten
        var from = now.AddMinutes(-windowMinutes);
        var buckets = names.ToDictionary(n => n, _ => new int[fineCount], StringComparer.OrdinalIgnoreCase);

        var orders = 0;
        foreach (var telegram in window)
        {
            var fields = format.Slice(telegram.Data);
            if (!string.Equals(Field(fields, messageCode), MessageCode, StringComparison.OrdinalIgnoreCase))
                continue;

            orders++;

            var point = Field(fields, resourcePoint);
            if (point.Length == 0 || !stats.TryGetValue(point, out var current))
                continue;

            var slot = (int)((telegram.DateTime - from).TotalMinutes / step);
            if (slot >= 0 && slot < fineCount)
                buckets[point][slot]++;

            var dest = destMap.CanonicalFromData(telegram.Data);
            if (dest.Length > 0)
            {
                var byDest = destinations[point];
                byDest[dest] = byDest.TryGetValue(dest, out var dc) ? dc + 1 : 1;
            }

            var error = Field(fields, errorCode);
            stats[point] = (
                current.Count + 1,
                current.Recent + (telegram.DateTime > rateFrom ? 1 : 0),
                current.Errors + (error.Length > 0 && !RecordFilter.IsAllZero(error) ? 1 : 0),
                current.Latest is { } latest && latest > telegram.DateTime ? latest : telegram.DateTime);
        }

        var winHours = winSteps * step / 60d;
        var rateHours = rate / 60d;   // Basis: die letzten paar Minuten, auf 1 h hochgerechnet

        // Gleitende Summe über die letzten 'winSteps' Feinschritte, ein Stützpunkt je Schritt.
        List<UtilizationBucket> RollingSeries(int[] fine)
        {
            var outp = new List<UtilizationBucket>(fineCount);
            var acc = 0;
            for (var i = 0; i < fineCount; i++)
            {
                acc += fine[i];
                if (i >= winSteps)
                    acc -= fine[i - winSteps];
                outp.Add(new UtilizationBucket
                {
                    At = from.AddMinutes((i + 1) * step),   // Fensterende
                    Count = acc,
                    Uph = Math.Round(acc / winHours, 1),
                });
            }
            return outp;
        }

        var pointResults = defs.Select(d =>
        {
            var name = d.Name;
            var s = stats[name];
            var uph = s.Recent / rateHours;
            var pointTarget = d.TargetUph is > 0 ? d.TargetUph.Value : targetUph;
            return new ResourcePointUtilization
            {
                ResourcePoint = name,
                Label = d.DisplayLabel,
                Group = d.GroupOrDefault,
                Count = s.Count,
                RateCount = s.Recent,
                Uph = Math.Round(uph, 1),
                TargetUph = pointTarget,
                Percent = pointTarget > 0 ? Math.Round(uph / pointTarget * 100, 1) : 0,
                Errors = s.Errors,
                LatestAt = s.Latest,
                Series = RollingSeries(buckets[name]),
                Destinations = destinations[name]
                    .OrderByDescending(kv => kv.Value)
                    .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => new DestinationShare
                    {
                        Target = kv.Key,
                        Label = destMap.Label(kv.Key),
                        Count = kv.Value,
                        Percent = s.Count > 0 ? Math.Round(kv.Value * 100.0 / s.Count, 1) : 0,
                    })
                    .ToList(),
            };
        }).ToList();

        // Gruppen: konfigurierte Reihenfolge zuerst, der Rest nach erstem Auftreten.
        var seenGroups = defs.Select(d => d.GroupOrDefault).Distinct().ToList();
        var orderedGroups = groupOrder is { Count: > 0 }
            ? groupOrder.Where(seenGroups.Contains)
                .Concat(seenGroups.Where(g => !groupOrder.Contains(g)))
                .ToList()
            : seenGroups;
        var resultByName = pointResults.ToDictionary(p => p.ResourcePoint, StringComparer.OrdinalIgnoreCase);
        var groups = orderedGroups.Select(gName =>
        {
            var members = defs.Where(d => d.GroupOrDefault == gName).Select(d => d.Name).ToList();
            var count = members.Sum(n => stats[n].Count);
            var recent = members.Sum(n => stats[n].Recent);
            var uph = recent / rateHours;
            var sumBuckets = new int[fineCount];
            foreach (var n in members)
                for (var i = 0; i < fineCount; i++)
                    sumBuckets[i] += buckets[n][i];

            return new UtilizationGroup
            {
                Name = gName,
                Count = count,
                RateCount = recent,
                Uph = Math.Round(uph, 1),
                TargetUph = members.Sum(n => resultByName[n].TargetUph),
                Percent = members.Count > 0
                    ? Math.Round(members.Average(n => resultByName[n].Percent), 1)
                    : 0,
                Errors = members.Sum(n => stats[n].Errors),
                Points = members,
                Series = RollingSeries(sumBuckets),
            };
        }).ToList();

        return new TelegramUtilization
        {
            WindowMinutes = windowMinutes,
            From = from,
            To = now,
            TargetUph = targetUph,
            TotalOrders = orders,
            BucketMinutes = bucketWidth,
            SeriesStepMinutes = step,
            RateMinutes = rate,
            Points = pointResults,
            Groups = groups,
        };
    }

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
