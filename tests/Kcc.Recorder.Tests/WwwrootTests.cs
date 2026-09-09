using Xunit;

namespace Kcc.Recorder.Tests;

/// <summary>
/// Die aufgetrennte Auslastungsansicht liegt als lose Dateien unter <c>wwwroot/</c> und wird
/// per <c>CopyToOutputDirectory</c> neben die EXE (und in die Testausgabe) gelegt.
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
            "auslastung.html",
            "css/nav.css", "css/auslastung.css",
            "js/nav.js", "js/glossary.js", "js/auslastung.js",
        })
            Assert.True(File.Exists(Path.Combine(Root, rel.Replace('/', Path.DirectorySeparatorChar))),
                $"fehlt: wwwroot/{rel}");
    }

    [Fact]
    public void Auslastung_html_bindet_die_geteilten_Skripte_ein()
    {
        var html = Read("auslastung.html");
        Assert.Contains("/js/glossary.js", html);
        Assert.Contains("/js/nav.js", html);
        Assert.Contains("/js/auslastung.js", html);
        Assert.Contains("/css/nav.css", html);
        // Kein serverseitiger Platzhalter mehr.
        Assert.DoesNotContain("<!--nav-->", html);
        Assert.DoesNotContain("<!--rbghelp-->", html);
    }

    [Fact]
    public void Nav_js_kennt_alle_sechs_Ansichten()
    {
        var js = Read("js/nav.js");
        foreach (var href in new[] { "'/'", "'/auslastung'", "'/verlauf'", "'/kontur'", "'/rbg'", "'/wand'" })
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
}
