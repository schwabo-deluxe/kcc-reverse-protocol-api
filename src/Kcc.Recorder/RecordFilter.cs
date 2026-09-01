using System.Text.RegularExpressions;

namespace Kcc.Recorder;

/// <summary>
/// Entscheidet, ob ein Telegramm aufgezeichnet wird.
///
/// Zweistufig: was schon der Server aussortieren kann, wird über <see cref="ServerSideFilters"/>
/// in die Query gehängt (spart Bandbreite); alles Feinere prüft <see cref="ShouldRecord"/> lokal.
/// </summary>
public sealed class RecordFilter
{
    readonly FilterConfig _config;
    readonly Regex? _match;
    readonly Regex? _ignore;
    readonly HashSet<string> _whitelist;
    readonly HashSet<string> _blacklist;

    public RecordFilter(FilterConfig config)
    {
        _config = config;
        _match = Compile(config.DataMatchRegex, nameof(config.DataMatchRegex));
        _ignore = Compile(config.DataIgnoreRegex, nameof(config.DataIgnoreRegex));
        _whitelist = new HashSet<string>(config.ConnectionWhitelist, StringComparer.OrdinalIgnoreCase);
        _blacklist = new HashSet<string>(config.ConnectionBlacklist, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Filter, die direkt in die Server-Query eingebaut werden.</summary>
    public IReadOnlyList<QueryFilter> ServerSideFilters()
    {
        if (!_config.FilterEmptyDataOnServer)
            return [];

        return
        [
            new QueryFilter
            {
                FilterField = "Data",
                FilterType = FilterType.IsNotNull,
                Filter = null,
                filterValueType = "String",
            },
            new QueryFilter
            {
                FilterField = "Data",
                FilterType = FilterType.IsNotEqual,
                Filter = "",
                filterValueType = "String",
            },
        ];
    }

    public bool ShouldRecord(Telegram telegram)
    {
        if (_config.Directions.Count > 0 && !_config.Directions.Contains(telegram.TelegramDirection))
            return false;

        var connection = telegram.ConnectionName ?? "";
        if (_whitelist.Count > 0 && !_whitelist.Contains(connection))
            return false;
        if (_blacklist.Contains(connection))
            return false;

        var data = telegram.Data;
        if (data is null)
            return false;

        var trimmed = data.Trim();
        if (trimmed.Length < _config.MinDataLength)
            return false;

        if (_config.IgnoreAllZeroData && IsAllZero(trimmed))
            return false;

        if (_match is not null && !_match.IsMatch(data))
            return false;

        if (_ignore is not null && _ignore.IsMatch(data))
            return false;

        return true;
    }

    /// <summary>Trifft auf Frames zu, die nur aus '0' und Trennzeichen bestehen — reine Leerlauf-Telegramme.</summary>
    internal static bool IsAllZero(string value)
    {
        var sawZero = false;
        foreach (var c in value)
        {
            if (c == '0')
            {
                sawZero = true;
                continue;
            }
            if (c is ' ' or '\t' or '\r' or '\n')
                continue;
            return false;
        }
        return sawZero;
    }

    static Regex? Compile(string? pattern, string name)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return null;
        try
        {
            return new Regex(pattern, RegexOptions.Compiled);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException($"{name} ist kein gültiger regulärer Ausdruck: {ex.Message}");
        }
    }
}
