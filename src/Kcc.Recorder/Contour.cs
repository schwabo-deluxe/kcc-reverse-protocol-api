using System.Globalization;

namespace Kcc.Recorder;

/// <summary>Häufigkeit eines Konturfehler-Typs.</summary>
public sealed record ContourFlagCount
{
    public required string Label { get; init; }
    public required int Count { get; init; }

    /// <summary>Anteil an den Telegrammen <em>mit</em> Konturfehler in Prozent.</summary>
    public required double Percent { get; init; }
}

/// <summary>Auswertung einer einzelnen Konturkontrolle über das Zeitfenster.</summary>
public sealed record ContourCheckpoint
{
    public required string ResourcePoint { get; init; }
    public required string MessageCode { get; init; }
    public required string Label { get; init; }

    /// <summary>Geprüfte Telegramme (Status lesbar).</summary>
    public required int Total { get; init; }

    /// <summary>Davon ohne Konturfehler (<c>Status</c> leer oder <c>K000</c>).</summary>
    public required int Ok { get; init; }

    public required int Errors { get; init; }

    /// <summary><see cref="Errors"/> / <see cref="Total"/> in Prozent.</summary>
    public required double ErrorRate { get; init; }

    public required DateTime? LatestAt { get; init; }

    /// <summary>Zahl je Fehlertyp (Spaltenreihenfolge wie <see cref="ContourReport.FlagLabels"/>).</summary>
    public required IReadOnlyDictionary<string, int> Flags { get; init; }
}

/// <summary>
/// Wertet die Konturkontrollen aus: für die konfigurierten <c>(ResourcePoint, MessageCode)</c>
/// wird das <c>Status</c>-Feld (<c>Kxyz</c>) in benannte Fehlerbits zerlegt. Reine Funktion über
/// einem Zeitfenster.
/// </summary>
public sealed record ContourReport
{
    public required DateTime From { get; init; }
    public required DateTime To { get; init; }
    public required int WindowMinutes { get; init; }

    public required int Total { get; init; }
    public required int Ok { get; init; }
    public required int Errors { get; init; }
    public required double ErrorRate { get; init; }

    /// <summary>Alle Fehlertypen in Tabellen-Spaltenreihenfolge (Nibble, dann Bit).</summary>
    public required IReadOnlyList<string> FlagLabels { get; init; }

    /// <summary>Fehlertypen mit Häufigkeit, absteigend — die Balken im Dashboard.</summary>
    public required IReadOnlyList<ContourFlagCount> Flags { get; init; }

    public required IReadOnlyList<ContourCheckpoint> Checkpoints { get; init; }

    /// <summary>Telegramme an einer Kontrolle, deren <c>Status</c> nicht als <c>Kxyz</c> lesbar war.</summary>
    public required int Unreadable { get; init; }

    /// <summary>Konturkontrollen, die das Dashboard ohne Konfiguration auswertet.</summary>
    public static readonly IReadOnlyList<ContourCheckpointConfig> DefaultCheckpoints =
    [
        new() { ResourcePoint = "LB21", MessageCode = "ENDTSP" },
        new() { ResourcePoint = "DA91", MessageCode = "TSPREG" },
        new() { ResourcePoint = "AA41", MessageCode = "TSPREG" },
        new() { ResourcePoint = "NA41", MessageCode = "TSPREG" },
    ];

    /// <summary>
    /// Bedeutung der Fehlerbits laut Doku „Ergebnis der Konturenkontrolle / Konturenfehler (Kxyz)".
    /// <c>Kxyz</c>: <c>x</c> = Nibble 0, <c>y</c> = Nibble 1, <c>z</c> = Nibble 2.
    /// </summary>
    public static readonly IReadOnlyList<ContourFlagConfig> DefaultFlags =
    [
        new() { Nibble = 0, Bit = 0, Label = "Daten" },
        new() { Nibble = 0, Bit = 1, Label = "Profil hinten" },
        new() { Nibble = 0, Bit = 2, Label = "Profil links" },
        new() { Nibble = 0, Bit = 3, Label = "Reserve 2" },
        new() { Nibble = 1, Bit = 0, Label = "Höhe" },
        new() { Nibble = 1, Bit = 1, Label = "Fuß-Freiraumkontrolle" },
        new() { Nibble = 1, Bit = 2, Label = "Gewicht" },
        new() { Nibble = 1, Bit = 3, Label = "Reserve 1" },
        new() { Nibble = 2, Bit = 0, Label = "Profil vorne" },
        new() { Nibble = 2, Bit = 1, Label = "Profil rechts" },
        new() { Nibble = 2, Bit = 2, Label = "Scanner" },
        new() { Nibble = 2, Bit = 3, Label = "Unterbrett" },
    ];

    static readonly char[] Padding = ['.', ' ', '\0'];

    /// <summary>
    /// Zerlegt den <c>Status</c>-Wert. Gibt <c>false</c> zurück, wenn ein nicht-leerer Wert nicht
    /// als <c>Kxyz</c> lesbar ist. <paramref name="nibbles"/> sind <c>[x, y, z]</c>; alle 0 ⇒
    /// kein Konturfehler.
    /// </summary>
    public static bool TryDecodeStatus(string? raw, out int[] nibbles)
    {
        nibbles = [0, 0, 0];
        var s = (raw ?? "").Trim(Padding);
        if (s.Length == 0)
            return true;
        if (s[0] is 'K' or 'k')
            s = s[1..];
        s = s.Trim(Padding);
        if (s.Length == 0 || s.All(c => c == '0'))
            return true;

        s = s.Length >= 3 ? s[^3..] : s.PadLeft(3, '0');
        for (var i = 0; i < 3; i++)
        {
            if (!int.TryParse(s.AsSpan(i, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
                return false;
            nibbles[i] = v;
        }
        return true;
    }

    public static ContourReport Compute(
        IReadOnlyList<Telegram> window,
        TelegramFormat format,
        int windowMinutes,
        DateTime from,
        DateTime to,
        IReadOnlyList<ContourCheckpointConfig>? checkpoints = null,
        IReadOnlyList<ContourFlagConfig>? flags = null)
    {
        var defs = (checkpoints is { Count: > 0 } ? checkpoints : DefaultCheckpoints)
            .Where(c => !string.IsNullOrWhiteSpace(c.ResourcePoint) && !string.IsNullOrWhiteSpace(c.MessageCode))
            .GroupBy(c => (c.ResourcePoint.Trim().ToUpperInvariant(), c.MessageCode.Trim().ToUpperInvariant()))
            .Select(g => g.First())
            .ToList();
        if (defs.Count == 0)
            defs = DefaultCheckpoints.ToList();

        var flagDefs = (flags is { Count: > 0 } ? flags : DefaultFlags)
            .Where(f => f is { Nibble: >= 0 and <= 2, Bit: >= 0 and <= 3 } && !string.IsNullOrWhiteSpace(f.Label))
            .ToList();
        if (flagDefs.Count == 0)
            flagDefs = DefaultFlags.ToList();
        var flagLabels = flagDefs.Select(f => f.Label).Distinct().ToList();

        var mcIdx = FieldIndex(format, "MessageCode");
        var rpIdx = FieldIndex(format, "ResourcePoint");
        var stIdx = FieldIndex(format, "Status");

        // Ein Datensatz je Kontrolle.
        var acc = defs.Select(d => new
        {
            Def = d,
            Flags = flagLabels.ToDictionary(l => l, _ => 0, StringComparer.Ordinal),
            Box = new int[3],   // Total, Ok, Unreadable
            Latest = new DateTime?[1],
        }).ToList();

        var byKey = acc.ToDictionary(
            a => (a.Def.ResourcePoint, a.Def.MessageCode),
            a => a,
            new KeyComparer());

        foreach (var telegram in window)
        {
            var fields = format.Slice(telegram.Data);
            var rp = Field(fields, rpIdx);
            var mc = Field(fields, mcIdx);
            if (!byKey.TryGetValue((rp, mc), out var a))
                continue;

            if (!TryDecodeStatus(Field(fields, stIdx), out var nib))
            {
                a.Box[2]++;   // unlesbar
                continue;
            }

            a.Box[0]++;   // total
            a.Latest[0] = a.Latest[0] is { } l && l > telegram.DateTime ? l : telegram.DateTime;

            var hasError = false;
            foreach (var f in flagDefs)
            {
                if ((nib[f.Nibble] & (1 << f.Bit)) == 0)
                    continue;
                a.Flags[f.Label]++;
                hasError = true;
            }
            if (!hasError)
                a.Box[1]++;   // ok
        }

        var checkpointResults = acc.Select(a =>
        {
            var total = a.Box[0];
            var ok = a.Box[1];
            var errors = total - ok;
            return new ContourCheckpoint
            {
                ResourcePoint = a.Def.ResourcePoint,
                MessageCode = a.Def.MessageCode,
                Label = a.Def.DisplayLabel,
                Total = total,
                Ok = ok,
                Errors = errors,
                ErrorRate = total > 0 ? Math.Round(errors * 100.0 / total, 1) : 0,
                LatestAt = a.Latest[0],
                Flags = a.Flags,
            };
        }).ToList();

        var grandTotal = checkpointResults.Sum(c => c.Total);
        var grandOk = checkpointResults.Sum(c => c.Ok);
        var grandErrors = grandTotal - grandOk;

        var flagTotals = flagLabels
            .Select(l => new { Label = l, Count = checkpointResults.Sum(c => c.Flags.GetValueOrDefault(l)) })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Label, StringComparer.Ordinal)
            .Select(x => new ContourFlagCount
            {
                Label = x.Label,
                Count = x.Count,
                Percent = grandErrors > 0 ? Math.Round(x.Count * 100.0 / grandErrors, 1) : 0,
            })
            .ToList();

        return new ContourReport
        {
            From = from,
            To = to,
            WindowMinutes = windowMinutes,
            Total = grandTotal,
            Ok = grandOk,
            Errors = grandErrors,
            ErrorRate = grandTotal > 0 ? Math.Round(grandErrors * 100.0 / grandTotal, 1) : 0,
            FlagLabels = flagLabels,
            Flags = flagTotals,
            Checkpoints = checkpointResults,
            Unreadable = acc.Sum(a => a.Box[2]),
        };
    }

    static string Field(IReadOnlyList<string> fields, int index) =>
        index >= 0 && index < fields.Count ? fields[index].Trim() : "";

    static int FieldIndex(TelegramFormat format, string name)
    {
        for (var i = 0; i < format.Fields.Count; i++)
        {
            if (string.Equals(format.Fields[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    sealed class KeyComparer : IEqualityComparer<(string Rp, string Mc)>
    {
        public bool Equals((string Rp, string Mc) a, (string Rp, string Mc) b) =>
            string.Equals(a.Rp, b.Rp, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.Mc, b.Mc, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Rp, string Mc) k) =>
            HashCode.Combine(
                k.Rp.ToUpperInvariant(),
                k.Mc.ToUpperInvariant());
    }
}
