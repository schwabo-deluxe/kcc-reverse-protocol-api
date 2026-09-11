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

/// <summary>Nimmt aufgezeichnete Telegramme entgegen und schreibt sie irgendwohin als CSV.</summary>
public interface ITelegramCsvSink : IDisposable
{
    /// <summary>Für die Startmeldung: wohin geschrieben wird.</summary>
    string Description { get; }

    void Append(IEnumerable<Telegram> telegrams);
}

/// <summary>
/// Hängt aufgezeichnete Telegramme fortlaufend an eine CSV-Datei an — parallel zur SQLite-Ablage.
/// Öffnet die Datei im Anhänge-Modus; die Kopfzeile wird nur bei einer neuen/leeren Datei geschrieben.
/// </summary>
public sealed class TelegramCsvWriter : ITelegramCsvSink
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
    public string Description => FilePath;

    public void Append(IEnumerable<Telegram> telegrams)
    {
        foreach (var t in telegrams)
            _writer.WriteLine(_csv.Row(t));
    }

    public void Dispose() => _writer.Dispose();
}

/// <summary>
/// Wie <see cref="TelegramCsvWriter"/>, aber eine Datei je Kalendermonat (nach
/// <see cref="Telegram.DateTime"/>, nicht nach Systemzeit — damit ein Backfill alter Monate in
/// deren eigene Datei einsortiert wird). Für Archiv-/Backup-Zwecke: einmal geschrieben, ändert
/// sich eine Monatsdatei nicht mehr rückwirkend.
/// </summary>
public sealed class MonthlyCsvWriter : ITelegramCsvSink
{
    readonly string _folder;
    readonly TelegramCsv _csv;
    readonly Dictionary<(int Year, int Month), TelegramCsvWriter> _writers = [];

    public MonthlyCsvWriter(string folder, TelegramCsv csv)
    {
        _folder = Path.GetFullPath(folder);
        _csv = csv;
        Directory.CreateDirectory(_folder);
    }

    public string Description => $"{_folder} (je Kalendermonat eine Datei)";

    public void Append(IEnumerable<Telegram> telegrams)
    {
        foreach (var t in telegrams)
            WriterFor(t.DateTime).Append([t]);
    }

    TelegramCsvWriter WriterFor(DateTime at)
    {
        var key = (at.Year, at.Month);
        if (_writers.TryGetValue(key, out var writer))
            return writer;

        var path = Path.Combine(_folder, $"kcc-telegrams-{at:yyyy-MM}.csv");
        return _writers[key] = new TelegramCsvWriter(path, _csv);
    }

    public void Dispose()
    {
        foreach (var writer in _writers.Values)
            writer.Dispose();
        _writers.Clear();
    }
}
