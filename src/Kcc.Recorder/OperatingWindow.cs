namespace Kcc.Recorder;

/// <summary>
/// Rechnet den Anteil eines Zeitfensters aus, der auf die Hauptnutzungszeit der Anlage fällt —
/// der Nenner der Langzeit-Durchschnitte. Regeln (<see cref="OperatingHoursConfig"/>):
/// <list type="bullet">
///   <item>Vor <c>Start</c> und nach <c>End</c> zählt nicht.</item>
///   <item>Ein Kalendertag ohne jede Bewegung im Fenster zählt gar nicht (Wochenende, Feiertag).</item>
///   <item>Wird nach <c>End</c> noch gefahren, verlängert sich der Betrieb dieses Tages bis zur
///         letzten Bewegung.</item>
/// </list>
/// </summary>
public static class OperatingWindow
{
    /// <summary>
    /// Betriebssekunden im Halbbereich <c>[from, to)</c>. <paramref name="activity"/> sind
    /// Zeitstempel mit Bewegung (Rasteranfänge genügen). Ist die Nutzungszeit abgeschaltet oder
    /// unplausibel konfiguriert, kommt die volle Fensterdauer zurück.
    /// </summary>
    public static double EffectiveSeconds(
        DateTime from, DateTime to, IEnumerable<DateTime> activity, OperatingHoursConfig? config)
    {
        var full = Math.Max(0, (to - from).TotalSeconds);
        if (config is not { Enabled: true } || to <= from)
            return full;
        if (!TimeSpan.TryParse(config.Start, out var start) ||
            !TimeSpan.TryParse(config.End, out var end) ||
            end <= start || start < TimeSpan.Zero || end > TimeSpan.FromHours(24))
            return full;

        // Letzte Bewegung je Kalendertag im Fenster.
        var lastByDay = new Dictionary<DateTime, DateTime>();
        foreach (var a in activity)
        {
            if (a < from || a >= to)
                continue;
            var day = a.Date;
            if (!lastByDay.TryGetValue(day, out var prev) || a > prev)
                lastByDay[day] = a;
        }

        double total = 0;
        foreach (var (day, last) in lastByDay)
        {
            var s = Max(day + start, from);
            var dayEnd = day + end;
            if (last > dayEnd)      // nach Betriebsschluss noch gefahren
                dayEnd = last;
            var e = Min(dayEnd, to);
            if (e > s)
                total += (e - s).TotalSeconds;
        }
        return total;
    }

    /// <summary>Betriebsstunden — <see cref="EffectiveSeconds"/> ÷ 3600.</summary>
    public static double EffectiveHours(
        DateTime from, DateTime to, IEnumerable<DateTime> activity, OperatingHoursConfig? config) =>
        EffectiveSeconds(from, to, activity, config) / 3600.0;

    static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
    static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}
