using System.Text;
using Kcc.Recorder;
using Xunit;

namespace Kcc.Recorder.Tests;

public class TelegramCsvTests
{
    static Telegram Sample(long id, string? data, string? connection = "PLC1") =>
        new(id, new DateTime(2026, 9, 2, 8, 39, 5, DateTimeKind.Utc),
            TelegramDirection.FromPlc, connection, data, null);

    [Fact]
    public void Row_trennt_mit_Semikolon_und_maskiert_Sonderzeichen()
    {
        var row = TelegramCsv.Row(Sample(42, "a;b\"c", connection: "L1\nL2"));

        Assert.Equal(
            "42;2026-09-02T08:39:05.0000000Z;FromPlc;\"L1\nL2\";\"a;b\"\"c\";",
            row);
    }

    [Fact]
    public void Writer_schreibt_Kopf_nur_bei_neuer_Datei_und_haengt_sonst_an()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kcc-csv-{Guid.NewGuid():N}.csv");
        try
        {
            using (var w = new TelegramCsvWriter(path))
                w.Append([Sample(1, "0150-eins")]);

            // Neustart: dieselbe Datei fortführen, kein zweiter Kopf.
            using (var w = new TelegramCsvWriter(path))
                w.Append([Sample(2, "0150-zwei")]);

            var lines = File.ReadAllLines(path, Encoding.UTF8);

            Assert.Equal(TelegramCsv.Header, lines[0]);
            Assert.StartsWith("1;", lines[1]);
            Assert.StartsWith("2;", lines[2]);
            Assert.Equal(3, lines.Length);
            Assert.Equal(new UTF8Encoding(true).GetPreamble(),
                File.ReadAllBytes(path)[..3]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
