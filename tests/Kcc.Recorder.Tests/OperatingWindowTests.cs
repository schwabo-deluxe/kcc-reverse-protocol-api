using Kcc.Recorder;
using Xunit;

namespace Kcc.Recorder.Tests;

public class OperatingWindowTests
{
    static readonly DateTime Mon = new(2026, 9, 7, 0, 0, 0, DateTimeKind.Unspecified);   // Montag
    static OperatingHoursConfig Cfg => new() { Enabled = true, Start = "06:00", End = "15:15" };

    [Fact]
    public void Nur_die_Hauptnutzungszeit_zaehlt()
    {
        var sec = OperatingWindow.EffectiveSeconds(
            Mon, Mon.AddDays(1), [Mon.AddHours(8), Mon.AddHours(14)], Cfg);

        Assert.Equal(9.25 * 3600, sec, 0);   // 06:00–15:15
    }

    [Fact]
    public void Betrieb_nach_Schluss_verlaengert_bis_zur_letzten_Bewegung()
    {
        var sec = OperatingWindow.EffectiveSeconds(
            Mon, Mon.AddDays(1), [Mon.AddHours(8), Mon.AddHours(17)], Cfg);

        Assert.Equal(11 * 3600, sec, 0);   // 06:00–17:00
    }

    [Fact]
    public void Tag_ohne_Bewegung_zaehlt_nicht()
    {
        // Fenster über zwei Tage, nur am ersten wird gefahren.
        var sec = OperatingWindow.EffectiveSeconds(
            Mon, Mon.AddDays(2), [Mon.AddHours(9)], Cfg);

        Assert.Equal(9.25 * 3600, sec, 0);
    }

    [Fact]
    public void Abgeschaltet_gibt_die_volle_Fensterdauer()
    {
        var cfg = new OperatingHoursConfig { Enabled = false };
        var sec = OperatingWindow.EffectiveSeconds(Mon, Mon.AddDays(1), [Mon.AddHours(8)], cfg);

        Assert.Equal(24 * 3600, sec, 0);
    }

    [Fact]
    public void Fenster_schneidet_die_Betriebszeit_ab()
    {
        // Fenster beginnt erst 10:00 und endet 12:00 — nur dieser Ausschnitt der Betriebszeit.
        var sec = OperatingWindow.EffectiveSeconds(
            Mon.AddHours(10), Mon.AddHours(12), [Mon.AddHours(11)], Cfg);

        Assert.Equal(2 * 3600, sec, 0);
    }
}
