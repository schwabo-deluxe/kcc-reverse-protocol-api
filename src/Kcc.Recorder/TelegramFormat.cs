using System.Globalization;

namespace Kcc.Recorder;

/// <summary>Ein Feld im Fixed-Width-Layout des <see cref="Telegram.Data"/>-Blocks.</summary>
public sealed record TelegramField(string Name, int Length, string Type);

/// <summary>
/// Fixed-Width-Layout des Telegramm-Data-Blocks.
///
/// Layout-Syntax wie im <c>Format</c>-Feld der Anlage: pipe-getrennte Tripel
/// <c>Name,Länge,Typ</c>, z. B. <c>"TelegramType,2,A|SequenceNumber,2,A|…"</c>.
/// </summary>
public sealed class TelegramFormat
{
    TelegramFormat(IReadOnlyList<TelegramField> fields)
    {
        Fields = fields;
        Length = fields.Sum(f => f.Length);
    }

    public IReadOnlyList<TelegramField> Fields { get; }

    /// <summary>Summe aller Feldlängen — die erwartete Länge eines vollständigen Data-Blocks.</summary>
    public int Length { get; }

    /// <summary>Von der Anlage dokumentiertes Standard-Layout (166 Zeichen).</summary>
    public static TelegramFormat Default { get; } = Parse(
        "TelegramType,2,A|SequenceNumber,2,A|Sender,4,A|Receiver,4,A|TelegramCount,2,A|" +
        "ErrorCode,2,A|MessageCode,6,A|Length,4,A|ResourcePoint,10,A|ResourceLabel,20,A|" +
        "Source,10,A|Destination,10,A|Type,3,A|TechnicalValues,20,A|WrapperProgram,4,A|" +
        "LabelingProgramm,4,A|Command,8,A|Weight,6,A|Status,4,A|PlaceConfig,4,A|FinishId,4,A|" +
        "Reserve,33,A");

    public static TelegramFormat Parse(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
            throw new FormatException("Leeres Telegramm-Layout.");

        var fields = new List<TelegramField>();
        foreach (var raw in spec.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = raw.Split(',');
            if (parts.Length is < 2 or > 3
                || string.IsNullOrWhiteSpace(parts[0])
                || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var len)
                || len <= 0)
            {
                throw new FormatException(
                    $"Ungültiges Feld im Telegramm-Layout: '{raw}'. Erwartet 'Name,Länge[,Typ]'.");
            }

            fields.Add(new TelegramField(parts[0].Trim(), len, parts.Length == 3 ? parts[2].Trim() : "A"));
        }

        if (fields.Count == 0)
            throw new FormatException("Telegramm-Layout ohne Felder.");

        return new TelegramFormat(fields);
    }

    /// <summary>
    /// Zerlegt einen Data-Block anhand des Layouts — ein Wert je Feld, gleiche Reihenfolge wie
    /// <see cref="Fields"/>. Fehlende Zeichen ergeben leere Werte, überzählige werden ignoriert.
    /// Padding (Leerzeichen, NUL) wird rechts abgeschnitten.
    /// </summary>
    public IReadOnlyList<string> Slice(string? data)
    {
        var text = data ?? "";
        var values = new string[Fields.Count];
        var pos = 0;

        for (var i = 0; i < Fields.Count; i++)
        {
            if (pos >= text.Length)
            {
                values[i] = "";
                continue;
            }

            var take = Math.Min(Fields[i].Length, text.Length - pos);
            values[i] = text.Substring(pos, take).TrimEnd(' ', '\0');
            pos += Fields[i].Length;
        }

        return values;
    }
}
