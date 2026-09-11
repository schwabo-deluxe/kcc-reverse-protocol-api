using System.Text;
using Kcc.Recorder;
using Xunit;

namespace Kcc.Recorder.Tests;

public class TelegramCsvTests
{
    static readonly TelegramCsv Csv = new(TelegramFormat.Default);

    static Telegram Sample(long id, string? data, string? connection = "PLC1") =>
        new(id, new DateTime(2026, 9, 2, 8, 39, 5, DateTimeKind.Utc),
            TelegramDirection.FromPlc, connection, data, null);

    [Fact]
    public void Header_hat_Stammspalten_plus_ein_Feld_je_Layout_Eintrag()
    {
        var columns = Csv.Header.Split(';');

        Assert.Equal("Id;DateTime;TelegramDirection;ConnectionName;Data", string.Join(';', columns[..5]));
        Assert.Equal(TelegramFormat.Default.Fields.Count, columns.Length - 5);
        Assert.Equal("TelegramType", columns[5]);
        Assert.Equal("Reserve", columns[^1]);
    }

    [Fact]
    public void Row_trennt_mit_Semikolon_und_maskiert_Sonderzeichen()
    {
        var row = Csv.Row(Sample(42, data: null, connection: "L1\nL2")).Split(';');

        Assert.Equal("42", row[0]);
        Assert.Equal("2026-09-02T08:39:05.0000000Z", row[1]);
        Assert.Equal("FromPlc", row[2]);
        Assert.Equal("\"L1\nL2\"", row[3]);
        Assert.Equal("", row[4]);
    }

    [Fact]
    public void Row_zerlegt_den_Data_Block_in_die_Layout_Spalten()
    {
        // TelegramType(2) SequenceNumber(2) Sender(4) Receiver(4) TelegramCount(2) ErrorCode(2)
        // MessageCode(6) Length(4) ResourcePoint(10) …
        var row = Csv.Row(Sample(1, "DM52MFC1CS010100TSPORD0150MB11      ")).Split(';');

        Assert.Equal("DM", row[5]);
        Assert.Equal("52", row[6]);
        Assert.Equal("MFC1", row[7]);
        Assert.Equal("CS01", row[8]);
        Assert.Equal("01", row[9]);
        Assert.Equal("00", row[10]);
        Assert.Equal("TSPORD", row[11]);
        Assert.Equal("0150", row[12]);
        Assert.Equal("MB11", row[13]); // ResourcePoint, rechts-Padding abgeschnitten
    }

    [Fact]
    public void Writer_schreibt_Kopf_nur_bei_neuer_Datei_und_haengt_sonst_an()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kcc-csv-{Guid.NewGuid():N}.csv");
        try
        {
            using (var w = new TelegramCsvWriter(path, Csv))
                w.Append([Sample(1, "0150-eins")]);

            // Neustart: dieselbe Datei fortführen, kein zweiter Kopf.
            using (var w = new TelegramCsvWriter(path, Csv))
                w.Append([Sample(2, "0150-zwei")]);

            var lines = File.ReadAllLines(path, Encoding.UTF8);

            Assert.Equal(Csv.Header, lines[0]);
            Assert.StartsWith("1;", lines[1]);
            Assert.StartsWith("2;", lines[2]);
            Assert.Equal(3, lines.Length);
            Assert.Equal(new UTF8Encoding(true).GetPreamble(), File.ReadAllBytes(path)[..3]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MonthlyWriter_sortiert_nach_dem_Zeitstempel_des_Telegramms_nicht_nach_Systemzeit()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"kcc-csv-{Guid.NewGuid():N}");
        try
        {
            var july = new DateTime(2026, 7, 20, 10, 0, 0, DateTimeKind.Utc);
            var september = new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc);

            using (var w = new MonthlyCsvWriter(dir, Csv))
            {
                // Ein Backfill alter Telegramme, während der Betrieb längst im September ist —
                // beide müssen trotzdem in ihren jeweils eigenen Monat einsortiert werden.
                w.Append([new Telegram(1, july, TelegramDirection.FromPlc, "L1", "0150-alt", null)]);
                w.Append([new Telegram(2, september, TelegramDirection.FromPlc, "L1", "0150-neu", null)]);
            }

            var julyPath = Path.Combine(dir, "kcc-telegrams-2026-07.csv");
            var septemberPath = Path.Combine(dir, "kcc-telegrams-2026-09.csv");
            Assert.True(File.Exists(julyPath));
            Assert.True(File.Exists(septemberPath));

            var julyLines = File.ReadAllLines(julyPath);
            Assert.Equal(Csv.Header, julyLines[0]);
            Assert.StartsWith("1;", julyLines[1]);
            Assert.Equal(2, julyLines.Length);

            var septemberLines = File.ReadAllLines(septemberPath);
            Assert.StartsWith("2;", septemberLines[1]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MonthlyWriter_haengt_bei_erneutem_Schreiben_im_selben_Monat_an()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"kcc-csv-{Guid.NewGuid():N}");
        try
        {
            var at = new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc);

            using (var w = new MonthlyCsvWriter(dir, Csv))
            {
                w.Append([new Telegram(1, at, TelegramDirection.FromPlc, "L1", "eins", null)]);
                w.Append([new Telegram(2, at, TelegramDirection.FromPlc, "L1", "zwei", null)]);
            }

            var lines = File.ReadAllLines(Path.Combine(dir, "kcc-telegrams-2026-09.csv"));
            Assert.Equal(3, lines.Length);   // Kopf + 2 Zeilen, kein doppelter Kopf
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
