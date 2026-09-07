using Kcc.Recorder;
using Xunit;

namespace Kcc.Recorder.Tests;

public class DashboardNavTests
{
    [Fact]
    public void For_markiert_die_aktive_Seite_und_listet_alle()
    {
        var nav = DashboardNav.For("/verlauf");

        Assert.Contains("<a href=\"/\">KPIs</a>", nav);
        Assert.Contains("<a href=\"/auslastung\">Auslastung</a>", nav);
        Assert.Contains("<a href=\"/verlauf\" class=\"on\">Verlauf</a>", nav);
        Assert.Contains("<a href=\"/kontur\">Kontur</a>", nav);
    }

    [Fact]
    public void Inject_ersetzt_den_Platzhalter_Strip_entfernt_ihn()
    {
        const string page = "<body>\n<!--nav-->\n<header>x</header>";

        var injected = DashboardNav.Inject(page, "/kontur");
        Assert.DoesNotContain("<!--nav-->", injected);
        Assert.Contains("class=\"kcc-nav\"", injected);

        var stripped = DashboardNav.Strip(page);
        Assert.DoesNotContain("<!--nav-->", stripped);
        Assert.DoesNotContain("kcc-nav", stripped);
        Assert.Contains("<header>x</header>", stripped);
    }

    [Fact]
    public void Alle_Dashboards_tragen_den_Platzhalter()
    {
        foreach (var html in new[]
        {
            Dashboard.Html, UtilizationDashboard.Html, UphHistoryDashboard.Html, ContourDashboard.Html,
        })
            Assert.Contains(DashboardNav.Placeholder, html);
    }

    [Fact]
    public void NormalizePrefix_setzt_Vorgabe_und_abschliessenden_Slash()
    {
        Assert.Equal("http://+:8082/", ApiServer.NormalizePrefix(null));
        Assert.Equal("http://+:8082/", ApiServer.NormalizePrefix("http://+:8082"));
        Assert.Equal("http://host:9000/", ApiServer.NormalizePrefix("  http://host:9000/  "));
    }
}
