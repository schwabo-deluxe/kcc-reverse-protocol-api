using Xunit;

namespace Kcc.Recorder.Tests;

/// <summary>
/// Die aufgetrennten Ansichten (<c>/auslastung</c>, <c>/verlauf</c>, <c>/rbg</c>) liegen als
/// lose Dateien unter <c>wwwroot/</c> und werden per <c>CopyToOutputDirectory</c> neben die EXE
/// (und in die Testausgabe) gelegt.
/// </summary>
public class WwwrootTests
{
    static string Root => Path.Combine(AppContext.BaseDirectory, "wwwroot");

    static string Read(string rel) => File.ReadAllText(Path.Combine(Root, rel.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void Alle_erwarteten_Dateien_werden_ausgeliefert()
    {
        foreach (var rel in new[]
        {
            "auslastung.html", "verlauf.html", "rbg.html",
            "css/nav.css", "css/auslastung.css", "css/charts.css", "css/verlauf.css", "css/rbg.css",
            "js/nav.js", "js/glossary.js", "js/auslastung.js", "js/verlauf.js", "js/rbg.js",
            "vendor/echarts.min.js",
        })
            Assert.True(File.Exists(Path.Combine(Root, rel.Replace('/', Path.DirectorySeparatorChar))),
                $"fehlt: wwwroot/{rel}");
    }

    [Fact]
    public void Echarts_ist_die_vendorte_Apache_Version()
    {
        var js = Read("vendor/echarts.min.js");
        Assert.True(js.Length > 500_000, "echarts.min.js wirkt zu klein");
        Assert.Contains("Apache", js);
    }

    [Fact]
    public void Aufgetrennte_Seiten_binden_die_geteilten_Skripte_ein()
    {
        foreach (var page in new[] { "auslastung.html", "verlauf.html", "rbg.html" })
        {
            var html = Read(page);
            Assert.Contains("/js/nav.js", html);
            Assert.Contains("/css/nav.css", html);
            Assert.DoesNotContain("<!--nav-->", html);
            Assert.DoesNotContain("<!--rbghelp-->", html);
        }
        Assert.Contains("/vendor/echarts.min.js", Read("verlauf.html"));
        Assert.Contains("/vendor/echarts.min.js", Read("rbg.html"));
        Assert.Contains("/js/glossary.js", Read("rbg.html"));
    }

    [Fact]
    public void Nav_js_kennt_alle_sechs_Ansichten()
    {
        var js = Read("js/nav.js");
        foreach (var href in new[] { "'/'", "'/auslastung'", "'/verlauf'", "'/kontur'", "'/rbg'", "'/dashboard'" })
            Assert.Contains(href, js);
    }

    [Fact]
    public void Glossary_js_deckt_dieselben_Schluessel_wie_der_Server_ab()
    {
        var js = Read("js/glossary.js");
        // RbgGlossary.Script liefert "<script>window.RBG_HELP={key:"…",key:"…"};</script>".
        var server = RbgGlossary.Script;
        foreach (var key in new[] { "busy", "load", "double", "single", "idle", "inout", "avgdur", "cbusy", "cdepart" })
        {
            Assert.Contains($"{key}:", server);
            Assert.Contains($"{key}:", js);
        }
    }

    [Fact]
    public void Auslastung_js_bringt_die_freie_Anordnung_mit()
    {
        var js = Read("js/auslastung.js");
        Assert.Contains("kcc.auslastung.layout.v1", js);   // localStorage-Schlüssel
        Assert.Contains("function layoutTiles", js);
        Assert.Contains("pointerdown", js);
        Assert.Contains("resetLayout", js);
    }

    [Fact]
    public void Verlauf_und_rbg_zeichnen_mit_echarts()
    {
        foreach (var js in new[] { "js/verlauf.js", "js/rbg.js" })
        {
            var body = Read(js);
            Assert.Contains("echarts.init", body);
            Assert.Contains("dataZoom", body);
        }
        Assert.Contains("/api/uph-history", Read("js/verlauf.js"));
        Assert.Contains("/api/rbg-history", Read("js/rbg.js"));
    }
}
