namespace Kcc.Recorder;

/// <summary>
/// Löst rohe Endziele auf Klartext und Sammelschlüssel auf. Konfiguriert über
/// <see cref="KccConfig.DestinationLabels"/>:
/// <list type="bullet">
///   <item><c>"GA51": "Kommissionierung"</c> — exakter Treffer, Anzeige <c>GA51 (Kommissionierung)</c></item>
///   <item><c>"DLL*": "Auslagerung DLL"</c> — Präfixmuster: alle <c>DLL…</c> werden zu einem Ziel
///         <c>DLL*</c> zusammengefasst</item>
/// </list>
/// Exakte Treffer schlagen Muster; unter den Mustern gewinnt das längste Präfix.
/// </summary>
public sealed class DestinationMap
{
    static readonly char[] Padding = ['.', ' ', '\0'];

    readonly Dictionary<string, string> _exact = new(StringComparer.OrdinalIgnoreCase);
    readonly (string Prefix, string Key, string Text)[] _patterns;

    public DestinationMap(IReadOnlyDictionary<string, string>? labels)
    {
        var patterns = new List<(string Prefix, string Key, string Text)>();
        foreach (var (key, text) in labels ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;
            if (key.EndsWith('*'))
                patterns.Add((key[..^1], key, text));
            else
                _exact[key] = text;
        }
        _patterns = patterns.OrderByDescending(p => p.Prefix.Length).ToArray();
    }

    /// <summary>
    /// Rohes Endziel eines Telegramms: das führende Token des letzten 33-Zeichen-Blocks
    /// (Excel-Näherung <c>LINKS(RECHTS(D;33);4)</c>), 4 <em>oder</em> 5 Zeichen, ohne Füllzeichen.
    /// </summary>
    public static string RawTarget(string? data)
    {
        if (string.IsNullOrEmpty(data))
            return "";
        var right = data.Length <= 33 ? data : data[^33..];
        var end = 0;
        while (end < right.Length && end < 5 && Array.IndexOf(Padding, right[end]) < 0)
            end++;
        return right[..end];
    }

    /// <summary>Sammelschlüssel für ein rohes Ziel — Muster wie <c>DLL*</c> fassen zusammen.</summary>
    public string Canonical(string target)
    {
        if (target.Length == 0 || _exact.ContainsKey(target))
            return target;
        foreach (var p in _patterns)
        {
            if (target.StartsWith(p.Prefix, StringComparison.OrdinalIgnoreCase))
                return p.Key;
        }
        return target;
    }

    /// <summary>Sammelschlüssel direkt aus dem Rohdatenfeld.</summary>
    public string CanonicalFromData(string? data) => Canonical(RawTarget(data));

    /// <summary>Anzeigetext: <c>GA51 (Kommissionierung)</c>, <c>DLL* (…)</c>, sonst roh bzw. „ohne Ziel".</summary>
    public string Label(string canonical)
    {
        if (canonical.Length == 0)
            return "ohne Ziel";
        if (_exact.TryGetValue(canonical, out var text))
            return string.IsNullOrWhiteSpace(text) ? canonical : $"{canonical} ({text})";
        foreach (var p in _patterns)
        {
            if (string.Equals(canonical, p.Key, StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrWhiteSpace(p.Text) ? canonical : $"{canonical} ({p.Text})";
        }
        return canonical;
    }
}
