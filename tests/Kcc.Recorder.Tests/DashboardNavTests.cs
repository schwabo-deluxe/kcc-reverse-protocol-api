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
        Assert.Contains("<a href=\"/rbg\">RBG</a>", nav);
        Assert.Contains("<a href=\"/dashboard\">Dashboard</a>", nav);
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
    public void Noch_eingebettete_Dashboards_tragen_den_Platzhalter()
    {
        // /auslastung ist als eigene wwwroot-Datei aufgetrennt und nutzt nav.js statt der
        // serverseitigen Injektion; die übrigen Seiten sind weiterhin C#-Konstanten.
        foreach (var html in new[]
        {
            Dashboard.Html, ContourDashboard.Html, WallboardDashboard.Html,
        })
            Assert.Contains(DashboardNav.Placeholder, html);
    }

    [Theory]
    [InlineData(null, "+", 8082)]
    [InlineData("http://+:8082/", "+", 8082)]
    [InlineData("http://0.0.0.0:9000", "0.0.0.0", 9000)]
    [InlineData("http://localhost:8082/", "localhost", 8082)]
    [InlineData("  http://192.168.1.5:80/  ", "192.168.1.5", 80)]
    public void ParseApiUrl_trennt_Host_und_Port(string? url, string host, int port) =>
        Assert.Equal((host, port), ApiServer.ParseApiUrl(url));
}
