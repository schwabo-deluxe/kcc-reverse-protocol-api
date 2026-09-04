using Kcc.Recorder;
using Xunit;

namespace Kcc.Recorder.Tests;

public class DestinationMapTests
{
    static string Data(string destination) =>
        new string('x', 40) + (destination + new string('.', 33))[..33];

    [Theory]
    [InlineData("GA51", "GA51")]
    [InlineData("DLL13", "DLL13")]
    [InlineData("WA0", "WA0")]
    public void RawTarget_liest_das_fuehrende_Token_bis_fuenf_Zeichen(string dest, string expected) =>
        Assert.Equal(expected, DestinationMap.RawTarget(Data(dest)));

    [Fact]
    public void Exakter_Treffer_ergibt_Klartext_ohne_Schluessel_zu_aendern()
    {
        var map = new DestinationMap(new Dictionary<string, string> { ["GA51"] = "Kommissionierung" });

        Assert.Equal("GA51", map.Canonical("GA51"));
        Assert.Equal("GA51 (Kommissionierung)", map.Label("GA51"));
        Assert.Equal("XX99", map.Label("XX99"));
    }

    [Fact]
    public void Praefixmuster_fasst_zusammen_und_beschriftet()
    {
        var map = new DestinationMap(new Dictionary<string, string> { ["DLL*"] = "Auslagerung DLL" });

        Assert.Equal("DLL*", map.Canonical("DLL13"));
        Assert.Equal("DLL*", map.Canonical("DLL07"));
        Assert.Equal("DLL*", map.CanonicalFromData(Data("DLL42")));
        Assert.Equal("DLL* (Auslagerung DLL)", map.Label("DLL*"));
        Assert.Equal("WA01", map.Canonical("WA01"));
    }

    [Fact]
    public void Exakt_schlaegt_Muster_und_laengstes_Praefix_gewinnt()
    {
        var map = new DestinationMap(new Dictionary<string, string>
        {
            ["D*"] = "alle D",
            ["DLL*"] = "nur DLL",
            ["DLL13"] = "genau 13",
        });

        Assert.Equal("DLL13", map.Canonical("DLL13"));   // exakt
        Assert.Equal("DLL*", map.Canonical("DLL99"));    // längeres Präfix
        Assert.Equal("D*", map.Canonical("DA21"));       // nur das kurze Präfix passt
    }

    [Fact]
    public void Leeres_Ziel_heisst_ohne_Ziel()
    {
        var map = new DestinationMap(null);
        Assert.Equal("", map.CanonicalFromData(""));
        Assert.Equal("ohne Ziel", map.Label(""));
    }
}
