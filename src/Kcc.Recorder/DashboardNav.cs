namespace Kcc.Recorder;

/// <summary>
/// Gemeinsame Navigationsleiste, die der Server beim Ausliefern in jede Dashboard-Seite an der
/// Stelle <see cref="Placeholder"/> einsetzt. <c>kcc dump-dashboards</c> entfernt den Platzhalter,
/// damit die losen HTML-Dateien eigenständig bleiben.
/// </summary>
public static class DashboardNav
{
    public const string Placeholder = "<!--nav-->";

    static readonly (string Href, string Label)[] Links =
    [
        ("/", "KPIs"),
        ("/auslastung", "Auslastung"),
        ("/verlauf", "Verlauf"),
        ("/kontur", "Kontur"),
        ("/rbg", "RBG"),
        ("/dashboard", "Dashboard"),
    ];

    /// <summary>Navigationsleiste mit hervorgehobenem <paramref name="activePath"/>.</summary>
    public static string For(string activePath)
    {
        var items = string.Concat(Links.Select(l =>
            $"<a href=\"{l.Href}\"{(l.Href == activePath ? " class=\"on\"" : "")}>{l.Label}</a>"));

        var ver = BuildInfo.Version;
        var verText = char.IsDigit(ver.FirstOrDefault()) ? "v" + ver : ver;

        return "<nav class=\"kcc-nav\">" + items +
            $"<span class=\"kcc-ver\" title=\"kcc {ver}\">{verText}</span></nav>" +
            "<style>" +
            ".kcc-nav{display:flex;flex-wrap:wrap;gap:2px;padding:6px 12px;background:#10141a;" +
            "border-bottom:1px solid #2a2f37;font:13px system-ui,sans-serif}" +
            ".kcc-nav a{color:#9aa4b2;text-decoration:none;padding:5px 12px;border-radius:6px}" +
            ".kcc-nav a:hover{color:#e6e6e6;background:#1c2128}" +
            ".kcc-nav a.on{color:#fff;background:#1f6feb}" +
            ".kcc-nav .kcc-ver{margin-left:auto;align-self:center;color:#7a8494;padding:5px 4px;" +
            "font-variant-numeric:tabular-nums}" +
            "</style>";
    }

    /// <summary>Setzt die Leiste für <paramref name="activePath"/> in <paramref name="html"/> ein.</summary>
    public static string Inject(string html, string activePath) =>
        html.Replace(Placeholder, For(activePath));

    /// <summary>Entfernt den Platzhalter (für die losen Dateien aus <c>dump-dashboards</c>).</summary>
    public static string Strip(string html) =>
        html.Replace(Placeholder + "\n", "").Replace(Placeholder, "");
}
