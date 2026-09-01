using System.Globalization;

namespace Kcc.Recorder;

/// <summary>
/// Schlichter Parser für "kcc &lt;kommando&gt; --option wert --flag".
/// Bewusst handgeschrieben, damit das Werkzeug ohne zusätzliche Abhängigkeit auskommt.
/// </summary>
public sealed class CommandLine
{
    readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);

    public string Command { get; private set; } = "";

    public static CommandLine Parse(string[] args)
    {
        var cli = new CommandLine();
        var i = 0;

        if (args.Length > 0 && !args[0].StartsWith('-'))
        {
            cli.Command = args[0];
            i = 1;
        }

        for (; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
                continue;

            var name = arg[2..];
            string? value = null;

            var equals = name.IndexOf('=');
            if (equals >= 0)
            {
                value = name[(equals + 1)..];
                name = name[..equals];
            }
            else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[++i];
            }

            cli._options[name] = value;
        }

        return cli;
    }

    public bool HasFlag(string name) => _options.ContainsKey(name);

    public string? GetString(string name) =>
        _options.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value) ? value : null;

    public int? GetInt(string name) =>
        int.TryParse(GetString(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    public long? GetLong(string name) =>
        long.TryParse(GetString(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    public DateTime? GetDateTime(string name) =>
        DateTime.TryParse(GetString(name), CultureInfo.InvariantCulture, DateTimeStyles.None, out var v)
            ? v
            : null;
}
