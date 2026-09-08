namespace Kcc.Recorder;

/// <summary>
/// Entscheidet, welche Telegramme bei Zählungen gewertet werden.
///
/// Die Anlage schickt jedes Ereignis als Paar: <c>DM</c> (Data Message — die Meldung selbst,
/// z. B. MFC1→SR01) und unmittelbar darauf <c>AK</c> (Acknowledge der Gegenstelle, SR01→MFC1).
/// Beide tragen denselben <c>MessageCode</c>, dieselbe LE-Nummer, Quelle und Ziel; nur
/// <c>TelegramType</c>, <c>Sender</c> und <c>Receiver</c> unterscheiden sich. Wer beide zählt,
/// zählt jede Fahrt, jeden Transportauftrag und jede Konturprüfung doppelt.
///
/// <c>LM</c> (Life Message) sind leere Lebenszeichen und tragen ohnehin keine Nutzdaten.
/// </summary>
public sealed class TelegramTypeFilter
{
    /// <summary>Feldname im Telegramm-Layout, der den Typ trägt.</summary>
    public const string FieldName = "TelegramType";

    readonly int _index;
    readonly string? _wanted;

    public TelegramTypeFilter(TelegramFormat format, string? countTelegramType)
    {
        _wanted = string.IsNullOrWhiteSpace(countTelegramType) ? null : countTelegramType.Trim();
        _index = _wanted is null ? -1 : IndexOf(format, FieldName);
    }

    /// <summary>Ob überhaupt gefiltert wird (Typ konfiguriert und Feld im Layout vorhanden).</summary>
    public bool Active => _wanted is not null && _index >= 0;

    /// <summary>Zählt dieses bereits zerlegte Telegramm?</summary>
    public bool Accepts(IReadOnlyList<string> fields)
    {
        if (!Active)
            return true;
        var value = _index < fields.Count ? fields[_index].Trim() : "";
        return string.Equals(value, _wanted, StringComparison.OrdinalIgnoreCase);
    }

    static int IndexOf(TelegramFormat format, string name)
    {
        for (var i = 0; i < format.Fields.Count; i++)
        {
            if (string.Equals(format.Fields[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }
}
