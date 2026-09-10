using Kcc.Recorder;
using Xunit;

namespace Kcc.Recorder.Tests;

public class RbgGlossaryTests
{
    [Fact]
    public void Script_definiert_alle_Kennzahlen_als_JS_Objekt()
    {
        var script = RbgGlossary.Script;

        Assert.StartsWith("<script>window.RBG_HELP={", script);
        Assert.EndsWith("};</script>", script);
        foreach (var key in new[] { "busy", "load", "double", "single", "idle", "inout", "avgdur" })
            Assert.Contains($"{key}:\"", script);
    }

    [Fact]
    public void Texte_enthalten_keine_unmaskierten_Anfuehrungszeichen()
    {
        // Sonst bricht das eingebettete JS-Objekt auf.
        var inner = RbgGlossary.Script
            .Replace("<script>window.RBG_HELP={", "")
            .Replace("};</script>", "");

        var quotes = 0;
        for (var i = 0; i < inner.Length; i++)
            if (inner[i] == '"' && (i == 0 || inner[i - 1] != '\\'))
                quotes++;

        Assert.Equal(0, quotes % 2);   // jede Zeichenkette sauber geöffnet und geschlossen
    }

    [Fact]
    public void Inject_ersetzt_den_Platzhalter_in_allen_RBG_Ansichten()
    {
        // /auslastung und /rbg laden das Glossar als glossary.js; nur das Dashboard injiziert
        // es noch serverseitig.
        foreach (var html in new[]
        {
            WallboardDashboard.Html,
        })
        {
            Assert.Contains(RbgGlossary.Placeholder, html);

            var injected = RbgGlossary.Inject(html);
            Assert.DoesNotContain(RbgGlossary.Placeholder, injected);
            Assert.Contains("window.RBG_HELP", injected);
        }
    }
}
