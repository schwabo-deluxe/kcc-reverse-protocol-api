namespace Kcc.Recorder;

/// <summary>
/// Eine verdichtete Rasterzeile der RBG-Langzeitaufzeichnung: was eine Verbindung in einem
/// Zeitraster gefahren hat. Veränderlich, weil sie beim Verdichten schrittweise gefüllt wird.
/// </summary>
public sealed class RbgSampleRow
{
    public required DateTime Bucket { get; init; }
    public required string Connection { get; init; }

    /// <summary>Abgeschlossene Einlagerungen (Bringen) im Raster.</summary>
    public int Puts { get; set; }

    /// <summary>Abgeschlossene Auslagerungen (Holen) im Raster.</summary>
    public int Fetches { get; set; }

    /// <summary>Belegte Auftragszeit im Raster (Summe der Auftragsdauern, dem Abschluss zugeschlagen).</summary>
    public double BusySeconds { get; set; }

    /// <summary>Doppelspiele = <c>min(Puts, Fetches)</c>.</summary>
    public int DoubleCycles => Math.Min(Puts, Fetches);

    /// <summary>Einzelspiele = <c>|Puts − Fetches|</c>.</summary>
    public int SingleCycles => Math.Abs(Puts - Fetches);

    /// <summary>
    /// Grobes Doppelspiel-Äquivalent ohne Auslegungsdaten (<c>Doppel + Einzel/2</c>). Die
    /// Auswertung rechnet stattdessen über <see cref="RbgCapacity.DemandSeconds"/> — dort steckt
    /// das echte Verhältnis von Einzel- zu Doppelspiel.
    /// </summary>
    public double Cycles => DoubleCycles + SingleCycles / 2.0;
}

/// <summary>Ein Stützpunkt der Langzeitkurve: je Verbindung Spiele/h und Auslastungsgrad.</summary>
public sealed record RbgHistoryBucket
{
    public required DateTime At { get; init; }

    /// <summary>Spiele/h (Doppelspiel-Äquivalent) je Verbindung.</summary>
    public required IReadOnlyDictionary<string, double> CyclesPerHour { get; init; }

    /// <summary>
    /// Leistungsgrad in Prozent je Verbindung: erreichte Spiele/h gegen die Kapazität des Geräts.
    /// Die Mengenseite — was wurde geschafft, gemessen am Möglichen.
    /// </summary>
    public required IReadOnlyDictionary<string, double> LoadPercent { get; init; }

    /// <summary>
    /// Zeitbasierter Auslastungsgrad in Prozent je Verbindung — die Zeitseite: wie lange lagen
    /// überhaupt Aufträge an. Bei Doppelspielen laufen Ein- und Auslagerauftrag gleichzeitig,
    /// deren Dauern werden addiert; der Wert ist deshalb eine Obergrenze (auf 100 begrenzt).
    /// </summary>
    public required IReadOnlyDictionary<string, double> BusyPercent { get; init; }

    /// <summary>Leerlaufminuten im Raster je Verbindung = Rasterdauer − belegte Auftragszeit.</summary>
    public required IReadOnlyDictionary<string, double> IdleMinutes { get; init; }
}

/// <summary>Summen einer RBG-Verbindung über den ganzen Zeitraum — die Vergleichszeile.</summary>
public sealed record RbgHistorySeries
{
    public required string Connection { get; init; }
    public required string Label { get; init; }
    public required int MaxCyclesPerHour { get; init; }

    public required int Puts { get; init; }
    public required int Fetches { get; init; }
    public required int DoubleCycles { get; init; }
    public required int SingleCycles { get; init; }

    /// <summary>Spiele gesamt in Doppelspiel-Äquivalent.</summary>
    public required double Cycles { get; init; }

    /// <summary>Spiele/h über den gesamten Zeitraum (Stillstand eingerechnet).</summary>
    public required double AvgCyclesPerHour { get; init; }

    /// <summary>Höchster Stützpunkt der Kurve in Spiele/h.</summary>
    public required double PeakCyclesPerHour { get; init; }

    /// <summary><see cref="AvgCyclesPerHour"/> gegen <see cref="MaxCyclesPerHour"/> in Prozent.</summary>
    public required double AvgLoadPercent { get; init; }

    /// <summary>Belegte Auftragszeit ÷ Zeitraum in Prozent.</summary>
    public required double AvgBusyPercent { get; init; }

    /// <summary>Stunden mit mindestens einer Fahrt — macht Stillstände sichtbar.</summary>
    public required double ActiveHours { get; init; }

    /// <summary>Leerlaufstunden im Zeitraum = Zeitraum − belegte Auftragszeit.</summary>
    public required double IdleHours { get; init; }

    /// <summary>Anteil an den Spielen aller Verbindungen in Prozent.</summary>
    public required double Share { get; init; }

    public required DateTime? LatestAt { get; init; }
}

/// <summary>
/// Langzeitauswertung der RBG-Verbindungen aus den verdichteten Rasterzeilen: Verlauf der
/// Spiele/h und des Auslastungsgrads je RBG sowie der direkte Vergleich untereinander.
/// Beantwortet, ob ein RBG dauerhaft stärker belastet wird als ein anderes.
///
/// Doppel- und Einzelspiele werden im <em>Aufzeichnungsraster</em> bestimmt und erst danach
/// aufsummiert — so hängt die Zahl nicht davon ab, wie grob die Ansicht gerade zusammenfasst.
/// </summary>
public sealed record RbgHistoryReport
{
    public required DateTime From { get; init; }
    public required DateTime To { get; init; }
    public required int BucketMinutes { get; init; }

    /// <summary>Verbindungen in Anzeigereihenfolge.</summary>
    public required IReadOnlyList<string> Connections { get; init; }

    public required IReadOnlyList<RbgHistoryBucket> Buckets { get; init; }
    public required IReadOnlyList<RbgHistorySeries> Totals { get; init; }

    public required double TotalCycles { get; init; }

    /// <summary>
    /// Spreizung der Belastung: <c>(meiste − wenigste Spiele) ÷ meiste · 100</c>. 0 % = alle RBG
    /// gleich belastet; 40 % ⇒ das schwächste fährt 40 % weniger als das stärkste.
    /// </summary>
    public required double SpreadPercent { get; init; }

    public required string? Busiest { get; init; }
    public required string? Quietest { get; init; }

    public static RbgHistoryReport Compute(
        IReadOnlyList<RbgSampleRow> rows,
        DateTime from,
        DateTime to,
        int bucketMinutes,
        IReadOnlyList<ResourcePointConfig>? resourcePoints = null,
        RbgCapacity? defaultCapacity = null)
    {
        var step = Math.Max(1, bucketMinutes);
        if (to <= from)
            to = from.AddMinutes(step);

        var fallback = defaultCapacity
            ?? new RbgCapacity { DoubleCyclesPerHour = 60, PutsPerHour = 96, FetchesPerHour = 96 };

        // Verbindung → Anzeigename und Auslegung; mehrere Punkte auf derselben Verbindung: erster gewinnt.
        var meta = new Dictionary<string, (string Label, RbgCapacity Cap)>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in resourcePoints ?? [])
        {
            var connection = (p.Connection ?? "").Trim();
            if (connection.Length == 0 || meta.ContainsKey(connection))
                continue;
            var label = string.IsNullOrWhiteSpace(p.Label) ? p.Name : p.Label!;
            meta[connection] = (
                string.IsNullOrWhiteSpace(label) ? connection : label.Trim(),
                fallback.For(p));
        }

        var used = rows
            .Where(r => r.Bucket >= from && r.Bucket < to)
            .ToList();

        // Angezeigt werden alle konfigurierten Verbindungen (auch stillstehende) plus alles,
        // was in den Daten sonst noch auftaucht.
        var connections = meta.Keys
            .Concat(used.Select(r => r.Connection))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var count = Math.Max(1, (int)Math.Ceiling((to - from).TotalMinutes / step));
        var bucketHours = step / 60.0;
        var bucketSeconds = step * 60.0;

        var cycles = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
        var busy = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in connections)
        {
            cycles[c] = new double[count];
            busy[c] = new double[count];
        }

        var sums = connections.ToDictionary(
            c => c,
            _ => (Puts: 0, Fetches: 0, Double: 0, Single: 0, Cycles: 0.0, Busy: 0.0,
                  Active: new HashSet<long>(), Latest: (DateTime?)null),
            StringComparer.OrdinalIgnoreCase);

        foreach (var r in used)
        {
            var slot = (int)((r.Bucket - from).TotalMinutes / step);
            if (slot < 0 || slot >= count || !cycles.TryGetValue(r.Connection, out var line))
                continue;

            // Doppelspiel-Äquivalent über den Zeitbedarf laut Auslegung, damit /rbg und
            // /auslastung dieselbe Leistung ausweisen. Ein Einzelspiel ist nicht pauschal ein
            // halbes Doppelspiel — das Verhältnis kommt aus den Auslegungsdaten des Geräts.
            var rowCap = meta.TryGetValue(r.Connection, out var rm) ? rm.Cap : fallback;
            var equivalent = rowCap.DemandSeconds(r.Puts, r.Fetches) / rowCap.DoubleSeconds;

            line[slot] += equivalent;
            busy[r.Connection][slot] += r.BusySeconds;

            var s = sums[r.Connection];
            if (r.Puts + r.Fetches > 0)
                s.Active.Add(slot);
            sums[r.Connection] = (
                s.Puts + r.Puts, s.Fetches + r.Fetches,
                s.Double + r.DoubleCycles, s.Single + r.SingleCycles,
                s.Cycles + equivalent, s.Busy + r.BusySeconds,
                s.Active, s.Latest is { } prev && prev >= r.Bucket ? prev : r.Bucket);
        }

        var buckets = new List<RbgHistoryBucket>(count);
        for (var i = 0; i < count; i++)
        {
            var slot = i;
            double PerHour(string c) => cycles[c][slot] / bucketHours;
            RbgCapacity Cap(string c) => meta.TryGetValue(c, out var m) ? m.Cap : fallback;

            buckets.Add(new RbgHistoryBucket
            {
                At = from.AddMinutes(slot * step),
                CyclesPerHour = connections.ToDictionary(c => c, c => Math.Round(PerHour(c), 1)),
                LoadPercent = connections.ToDictionary(
                    c => c, c => Math.Round(PerHour(c) / Cap(c).DoubleCyclesPerHour * 100, 1)),
                BusyPercent = connections.ToDictionary(
                    c => c, c => Math.Round(Math.Min(100, busy[c][slot] / bucketSeconds * 100), 1)),
                IdleMinutes = connections.ToDictionary(
                    c => c, c => Math.Round(Math.Max(0, bucketSeconds - busy[c][slot]) / 60, 1)),
            });
        }

        var windowHours = Math.Max(1e-9, (to - from).TotalHours);
        var totalCycles = sums.Values.Sum(s => s.Cycles);

        var totals = connections.Select(c =>
        {
            var s = sums[c];
            var cap = meta.TryGetValue(c, out var m) ? m.Cap : fallback;
            var max = cap.DoubleCyclesPerHour;
            var avgPerHour = s.Cycles / windowHours;
            return new RbgHistorySeries
            {
                Connection = c,
                Label = meta.TryGetValue(c, out var m2) ? m2.Label : c,
                MaxCyclesPerHour = max,
                Puts = s.Puts,
                Fetches = s.Fetches,
                DoubleCycles = s.Double,
                SingleCycles = s.Single,
                Cycles = Math.Round(s.Cycles, 1),
                AvgCyclesPerHour = Math.Round(avgPerHour, 1),
                PeakCyclesPerHour = cycles[c].Length == 0 ? 0 : Math.Round(cycles[c].Max() / bucketHours, 1),
                AvgLoadPercent = max > 0 ? Math.Round(avgPerHour / max * 100, 1) : 0,
                AvgBusyPercent = Math.Round(Math.Min(100, s.Busy / Math.Max(1e-9, (to - from).TotalSeconds) * 100), 1),
                ActiveHours = Math.Round(s.Active.Count * bucketHours, 2),
                IdleHours = Math.Round(Math.Max(0, (to - from).TotalSeconds - s.Busy) / 3600, 2),
                Share = totalCycles > 0 ? Math.Round(s.Cycles / totalCycles * 100, 1) : 0,
                LatestAt = s.Latest,
            };
        })
        .OrderByDescending(t => t.Cycles)
        .ThenBy(t => t.Connection, StringComparer.Ordinal)
        .ToList();

        // Spreizung nur über Verbindungen, die überhaupt gefahren sind — ein abgeschaltetes RBG
        // würde sonst jede Auswertung auf 100 % ziehen.
        var moved = totals.Where(t => t.Cycles > 0).ToList();
        var spread = moved.Count > 1 && moved[0].Cycles > 0
            ? Math.Round((moved[0].Cycles - moved[^1].Cycles) / moved[0].Cycles * 100, 1)
            : 0;

        return new RbgHistoryReport
        {
            From = from,
            To = to,
            BucketMinutes = step,
            Connections = totals.Select(t => t.Connection).ToList(),
            Buckets = buckets,
            Totals = totals,
            TotalCycles = Math.Round(totalCycles, 1),
            SpreadPercent = spread,
            Busiest = moved.Count > 0 ? moved[0].Connection : null,
            Quietest = moved.Count > 1 ? moved[^1].Connection : null,
        };
    }
}
