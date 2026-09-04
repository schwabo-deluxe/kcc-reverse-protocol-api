using Kcc.Recorder;
using Xunit;

namespace Kcc.Recorder.Tests;

public class ContourReportTests
{
    static readonly DateTime Now = new(2026, 9, 4, 12, 0, 0, DateTimeKind.Unspecified);
    static readonly TelegramFormat Fmt = TelegramFormat.Default;

    static int Offset(string name)
    {
        var pos = 0;
        foreach (var f in Fmt.Fields)
        {
            if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))
                return pos;
            pos += f.Length;
        }
        throw new ArgumentException(name);
    }

    static string Data(string resourcePoint, string messageCode, string status)
    {
        var buf = new char[Fmt.Length];
        Array.Fill(buf, '.');
        void Put(string field, string value)
        {
            var at = Offset(field);
            for (var i = 0; i < value.Length && at + i < buf.Length; i++)
                buf[at + i] = value[i];
        }
        Put("MessageCode", messageCode);
        Put("ResourcePoint", resourcePoint);
        Put("Status", status);
        return new string(buf);
    }

    static Telegram T(long id, string rp, string mc, string status, int minutesAgo = 1) =>
        new(id, Now.AddMinutes(-minutesAgo), TelegramDirection.FromPlc, "L1", Data(rp, mc, status), null);

    static ContourReport Run(params Telegram[] rows) =>
        ContourReport.Compute(rows, Fmt, 480, Now.AddMinutes(-480), Now);

    [Theory]
    [InlineData("....", true, new int[0])]
    [InlineData("", true, new int[0])]
    [InlineData("K000", true, new int[0])]
    [InlineData("K411", true, new[] { 4, 1, 1 })]
    [InlineData("K00F", true, new[] { 0, 0, 15 })]
    [InlineData("KZZZ", false, new int[0])]
    public void Zerlegt_das_Status_Feld(string raw, bool okParse, int[] expectedNibbles)
    {
        var parsed = ContourReport.TryDecodeStatus(raw, out var nibbles);
        Assert.Equal(okParse, parsed);
        if (okParse && expectedNibbles.Length == 3)
            Assert.Equal(expectedNibbles, nibbles);
    }

    [Fact]
    public void K411_meldet_Profil_links_Hoehe_und_Profil_vorne()
    {
        var r = Run(T(1, "LB21", "ENDTSP", "K411"));

        var lb21 = r.Checkpoints.Single(c => c.ResourcePoint == "LB21");
        Assert.Equal(1, lb21.Total);
        Assert.Equal(0, lb21.Ok);
        Assert.Equal(1, lb21.Errors);
        Assert.Equal(1, lb21.Flags["Profil links"]);
        Assert.Equal(1, lb21.Flags["Höhe"]);
        Assert.Equal(1, lb21.Flags["Profil vorne"]);
        Assert.Equal(0, lb21.Flags["Gewicht"]);
    }

    [Fact]
    public void Kein_Konturfehler_bei_leerem_Status()
    {
        var r = Run(
            T(1, "LB21", "ENDTSP", "...."),
            T(2, "LB21", "ENDTSP", "K000"),
            T(3, "LB21", "ENDTSP", "K002"));   // Profil rechts

        var lb21 = r.Checkpoints.Single(c => c.ResourcePoint == "LB21");
        Assert.Equal(3, lb21.Total);
        Assert.Equal(2, lb21.Ok);
        Assert.Equal(1, lb21.Errors);
        Assert.Equal(1, lb21.Flags["Profil rechts"]);
        Assert.Equal(33.3, lb21.ErrorRate);
    }

    [Fact]
    public void Nur_konfigurierte_Ressourcenpunkt_MessageCode_Paare_zaehlen()
    {
        var r = Run(
            T(1, "LB21", "ENDTSP", "K400"),
            T(2, "LB21", "RPFREE", "K400"),   // falscher MessageCode
            T(3, "DA91", "TSPREG", "K010"),
            T(4, "XX99", "ENDTSP", "K400"));  // nicht gelistet

        Assert.Equal(2, r.Total);
        Assert.Equal(1, r.Checkpoints.Single(c => c.ResourcePoint == "LB21").Total);
        Assert.Equal(1, r.Checkpoints.Single(c => c.ResourcePoint == "DA91").Total);
        Assert.Equal(0, r.Checkpoints.Single(c => c.ResourcePoint == "AA41").Total);
    }

    [Fact]
    public void Summiert_Fehlertypen_ueber_alle_Kontrollen()
    {
        var r = Run(
            T(1, "LB21", "ENDTSP", "K400"),   // Profil links
            T(2, "DA91", "TSPREG", "K400"),   // Profil links
            T(3, "DA91", "TSPREG", "K100"));  // Profil hinten

        Assert.Equal(3, r.Errors);
        var links = r.Flags.Single(f => f.Label == "Profil links");
        Assert.Equal(2, links.Count);
        Assert.Equal(66.7, links.Percent);   // 2 von 3 Fehler-Telegrammen
        Assert.Equal("Profil links", r.Flags[0].Label);   // häufigster zuerst
    }

    [Fact]
    public void Zaehlt_unlesbare_Status_getrennt()
    {
        var r = Run(
            T(1, "LB21", "ENDTSP", "KZZ9"),
            T(2, "LB21", "ENDTSP", "K400"));

        Assert.Equal(1, r.Unreadable);
        Assert.Equal(1, r.Total);
        Assert.Equal(1, r.Errors);
    }

    [Fact]
    public void Eigene_Fehlerbit_Tabelle()
    {
        var r = ContourReport.Compute(
            [T(1, "LB21", "ENDTSP", "K001")], Fmt, 480, Now.AddMinutes(-480), Now,
            checkpoints: [new ContourCheckpointConfig { ResourcePoint = "LB21", MessageCode = "ENDTSP" }],
            flags: [new ContourFlagConfig { Nibble = 2, Bit = 0, Label = "Kamera A" }]);

        Assert.Equal(["Kamera A"], r.FlagLabels);
        Assert.Equal(1, r.Checkpoints.Single().Flags["Kamera A"]);
    }
}
