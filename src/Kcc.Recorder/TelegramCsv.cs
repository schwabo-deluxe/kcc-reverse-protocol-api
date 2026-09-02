using System.Globalization;
using System.Text;

namespace Kcc.Recorder;

/// <summary>
/// CSV-Format der Telegramme — geteilt zwischen dem <c>export</c>-Kommando und dem
/// mitlaufenden <see cref="TelegramCsvWriter"/>, damit beide identische Dateien erzeugen.
/// Semikolon-getrennt, damit Excel im deutschen Gebietsschema die Spalten direkt erkennt.
///
/// Neben den Stammspalten wird der <see cref="Telegram.Data"/>-Block anhand des übergebenen
/// <see cref="TelegramFormat"/> in je eine Spalte pro Feld zerlegt.
/// </summary>
public sealed class TelegramCsv
{
    static readonly string[] BaseColumns =
        ["Id", "DateTime", "TelegramDirection", "ConnectionName", "Data"];

    readonly TelegramFormat _format;

    public TelegramCsv(TelegramFormat format) => _format = format;

    public string Header =>
        string.Join(';', BaseColumns.Concat(_format.Fields.Select(f => f.Name)));

    public string Row(Telegram t)
    {
        var columns = new List<string>(BaseColumns.Length + _format.Fields.Count)
        {
            t.Id.ToString(CultureInfo.InvariantCulture),
            t.DateTime.ToString("O", CultureInfo.InvariantCulture),
            t.TelegramDirection.ToString(),
            Field(t.ConnectionName),
            Field(t.Data),
        };
        columns.AddRange(_format.Slice(t.Data).Select(Field));
        return string.Join(';', columns);
    }

    public static string Field(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        if (value.Contains('"') || value.Contains(';') || value.Contains('\n') || value.Contains('\r'))
            return '"' + value.Replace("\"", "\"\"") + '"';
        return value;
    }
}

/// <summary>
/// Hängt aufgezeichnete Telegramme fortlaufend an eine CSV-Datei an — parallel zur SQLite-Ablage.
/// Öffnet die Datei im Anhänge-Modus; die Kopfzeile wird nur bei einer neuen/leeren Datei geschrieben.
/// </summary>
public sealed class TelegramCsvWriter : IDisposable
{
    readonly TelegramCsv _csv;
    readonly StreamWriter _writer;

    public TelegramCsvWriter(string path, TelegramCsv csv)
    {
        _csv = csv;
        FilePath = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath) ?? ".");

        var fresh = !File.Exists(FilePath) || new FileInfo(FilePath).Length == 0;

        // UTF-8 mit BOM, damit Excel Umlaute erkennt; append, damit ein Neustart die Datei fortführt.
        _writer = new StreamWriter(
            new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: fresh))
        {
            AutoFlush = true,
        };

        if (fresh)
            _writer.WriteLine(_csv.Header);
    }

    public string FilePath { get; }

    public void Append(IEnumerable<Telegram> telegrams)
    {
        foreach (var t in telegrams)
            _writer.WriteLine(_csv.Row(t));
    }

    public void Dispose() => _writer.Dispose();
}
