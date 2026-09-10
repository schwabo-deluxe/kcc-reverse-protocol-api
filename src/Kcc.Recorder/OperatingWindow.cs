namespace Kcc.Recorder;

/// <summary>
/// Rechnet den Anteil eines Zeitfensters aus, der auf die Hauptnutzungszeit der Anlage fällt —
/// der Nenner der Langzeit-Durchschnitte. Regeln (<see cref="OperatingHoursConfig"/>):
/// <list type="bullet">
///   <item>Ein Kalendertag ohne jede Bewegung im Fenster zählt gar nicht (Wochenende, Feiertag).</item>
///   <item>Grundintervall je Tag mit Bewegung: <c>Start</c>–<c>End</c>.</item>
///   <item>Wird davor oder danach gefahren, dehnt sich das Intervall bis zur ersten bzw. letzten
///         Bewegung — die tatsächliche Betriebszeit wird nie kleiner als die Bewegungsspanne.</item>
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

        // Erste und letzte Bewegung je Kalendertag im Fenster.
        var spanByDay = new Dictionary<DateTime, (DateTime First, DateTime Last)>();
        foreach (var a in activity)
        {
            if (a < from || a >= to)
                continue;
            var day = a.Date;
            if (!spanByDay.TryGetValue(day, out var sp))
                spanByDay[day] = (a, a);
            else
                spanByDay[day] = (a < sp.First ? a : sp.First, a > sp.Last ? a : sp.Last);
        }

        double total = 0;
        foreach (var (day, sp) in spanByDay)
        {
            // Grundintervall Start–End, aber mindestens die Bewegungsspanne dieses Tages
            // (Früh-/Spätschicht außerhalb der Hauptnutzungszeit).
            var s = Min(Max(day + start, from), sp.First);
            var dayEnd = Max(day + end, sp.Last);
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
