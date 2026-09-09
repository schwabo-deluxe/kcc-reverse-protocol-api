namespace Kcc.Recorder;

/// <summary>
/// Kurz-Definitionen der Kennzahlen für die Seiten, die noch als C#-Konstante ausgeliefert werden.
/// Der Server ersetzt <see cref="Placeholder"/> durch ein Skript, das <c>window.RBG_HELP</c>
/// definiert. Aufgetrennte Seiten laden stattdessen <c>wwwroot/js/glossary.js</c> — synchron halten.
/// </summary>
public static class RbgGlossary
{
    public const string Placeholder = "<!--rbghelp-->";

    static readonly (string Key, string Text)[] Entries =
    [
        ("busy", "Σ Transportdauern ÷ Fenster (PUPORD→ENDDEP). Fenster: „Tacho RBG“."),
        ("load", "Zeitbedarf laut Auslegung ÷ Fenster. Doppelspiel = 3600/DS-pro-h s, Einzelspiel = 3600/Ein-pro-h s."),
        ("double", "min(Ein, Aus) ÷ Fensterstunden. Ein Doppelspiel = zwei Transporte in einer Fahrt."),
        ("single", "|Ein − Aus| ÷ Fensterstunden. Kostet 62,5 % eines Doppelspiels."),
        ("idle", "Fenster − Σ Transportdauern."),
        ("inout", "Ein-/Auslagerungen ÷ Fensterstunden. Richtung aus dem ENDDEP-Ziel: numerisch = Regalplatz = Einlagerung."),
        ("avgdur", "Ø PUPORD → ENDDEP, über die LE-Nummer gepaart."),
        ("cycles", "Doppelspiele + Einzelspiele/2, je Raster bestimmt und dann summiert."),
        ("share", "Anteil an den Spielen aller RBG. Gleichverteilung = 100 % ÷ Anzahl Geräte."),
        ("spread", "(meiste − wenigste Spiele) ÷ meiste. Geräte ohne Bewegung zählen nicht."),
        ("avgcycles", "Spiele ÷ Betriebsstunden im Zeitraum."),
        ("peak", "Höchster Stützpunkt der Kurve in Spiele/h."),
        ("cbusy", "Σ(ENDTSP → RPFREE) ÷ Fenster. Fenster: „Tacho FT“."),
        ("coccupied", "Ø ENDTSP → RPFREE."),
        ("corderwait", "Ø ENDTSP → TSPORD (Wartezeit auf die MFR-Entscheidung)."),
        ("cdepart", "Ø TSPORD → RPFREE (Abtransport; lang = Rückstau)."),
        ("cwait", "Ø RPFREE → nächstes ENDTSP (Platz leer)."),
        ("cidle", "Fenster − belegte Zeit."),
        ("cfree", "Anzahl RPFREE im Fenster."),
        ("ccount", "ENDTSP / TSPORD / RPFREE ÷ Fensterstunden. Fenster: „Tacho FT“."),
        ("active", "Stunden mit mindestens einer Fahrt."),
    ];

    /// <summary>Skriptblock, der <c>window.RBG_HELP</c> definiert.</summary>
    public static string Script =>
        "<script>window.RBG_HELP={" +
        string.Join(",", Entries.Select(e => $"{e.Key}:\"{Escape(e.Text)}\"")) +
        "};</script>";

    static string Escape(string text) =>
        text.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>Setzt das Glossar an der Stelle <see cref="Placeholder"/> ein.</summary>
    public static string Inject(string html) =>
        html.Replace(Placeholder, Script);
}
