using Kcc.Recorder;
using Xunit;

namespace Kcc.Recorder.Tests;

public class TelegramFormatTests
{
    [Fact]
    public void Standard_Layout_hat_22_Felder_und_166_Zeichen()
    {
        Assert.Equal(22, TelegramFormat.Default.Fields.Count);
        Assert.Equal(166, TelegramFormat.Default.Length);
        Assert.Equal("TelegramType", TelegramFormat.Default.Fields[0].Name);
        Assert.Equal(2, TelegramFormat.Default.Fields[0].Length);
        Assert.Equal(("Reserve", 33), (TelegramFormat.Default.Fields[^1].Name, TelegramFormat.Default.Fields[^1].Length));
    }

    [Fact]
    public void Parse_liest_Name_Laenge_Typ_und_setzt_Typ_default_A()
    {
        var format = TelegramFormat.Parse("Kopf,3,N|Rumpf,5");

        Assert.Equal(new[] { "Kopf", "Rumpf" }, format.Fields.Select(f => f.Name));
        Assert.Equal(new[] { 3, 5 }, format.Fields.Select(f => f.Length));
        Assert.Equal(new[] { "N", "A" }, format.Fields.Select(f => f.Type));
        Assert.Equal(8, format.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("NurName")]
    [InlineData("Name,xyz")]
    [InlineData("Name,0")]
    [InlineData("Name,-4")]
    public void Parse_meldet_ungueltige_Layouts(string spec)
    {
        Assert.Throws<FormatException>(() => TelegramFormat.Parse(spec));
    }

    [Fact]
    public void Slice_schneidet_Padding_ab_und_verkraftet_kurze_Bloecke()
    {
        var format = TelegramFormat.Parse("A,2|B,4|C,4");

        Assert.Equal(new[] { "12", "AB", "" }, format.Slice("12AB  "));   // B rechts gepaddet, C fehlt
        Assert.Equal(new[] { "MB", "11", "" }, format.Slice("MB11......"));  // Anlage füllt mit Punkten
        Assert.Equal(new[] { "", "", "" }, format.Slice(null));
        Assert.Equal(new[] { "99", "3333", "44" }, format.Slice("99333344"));
    }

    [Fact]
    public void Slice_ignoriert_ueberzaehlige_Zeichen()
    {
        var format = TelegramFormat.Parse("A,2");

        Assert.Equal(new[] { "ab" }, format.Slice("abcdef"));
    }
}
