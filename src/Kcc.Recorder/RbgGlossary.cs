namespace Kcc.Recorder;

/// <summary>
/// Erklärtexte der RBG-Kennzahlen, an einer Stelle gepflegt und in jede Ansicht eingesetzt, die
/// sie anzeigt (<c>/auslastung</c>, <c>/rbg</c>, <c>/wand</c>). Der Server ersetzt beim Ausliefern
/// <see cref="Placeholder"/> durch ein Skript, das <c>window.RBG_HELP</c> definiert; die Seiten
/// hängen die Texte als <c>title</c> an die jeweilige Kennzahl.
/// </summary>
public static class RbgGlossary
{
    public const string Placeholder = "<!--rbghelp-->";

    /// <summary>Kennzahl → Erklärung. Bewusst ausführlich; erscheint nur beim Überfahren.</summary>
    static readonly (string Key, string Text)[] Entries =
    [
        ("busy",
            "Auslastung — die Zeitseite: Anteil des Zeitraums, in dem Aufträge anlagen " +
            "(Summe der Auftragsdauern ÷ Zeitraum). Achtung: bei einem Doppelspiel laufen Ein- " +
            "und Auslagerauftrag gleichzeitig, beide Dauern werden addiert. Der Wert ist deshalb " +
            "eine Obergrenze und bei 100 % gedeckelt — er sagt „es lagen durchgehend Aufträge an“, " +
            "nicht „das Gerät war zu X % in Fahrt“."),
        ("load",
            "Leistung — die Mengenseite: geschaffte Spiele gegen die Kapazität des Geräts. " +
            "Gerechnet als (Doppelspiele + Einzelspiele/2) ÷ (Kapazität × Stunden). Das ist die " +
            "belastbare Durchsatzzahl. Hohe Auslastung bei niedriger Leistung heißt: es fehlt " +
            "nicht an Aufträgen, die einzelne Fahrt dauert zu lang."),
        ("double",
            "Doppelspiel (kombiniertes Spiel, FEM 9.851): Ein- und Auslagerung in einer Fahrt — " +
            "die wirtschaftliche Betriebsart, weil keine Leerfahrt anfällt. Gezählt als " +
            "min(Einlagerungen, Auslagerungen) im Raster."),
        ("single",
            "Einzelspiel (FEM 9.851): reine Ein- oder Auslagerung, die Gegenrichtung bleibt leer. " +
            "Gezählt als |Einlagerungen − Auslagerungen|. Zählt bei der Leistung nur halb, weil " +
            "es den Fahrweg eines Doppelspiels für die halbe Menge braucht."),
        ("idle",
            "Leerlauf: Zeit ohne offenen Auftrag = Zeitraum − belegte Auftragszeit. Viel Leerlauf " +
            "heißt, dem Gerät fehlt Arbeit (Versorgung oder Vorgelagertes bremst). Wenig Leerlauf " +
            "bei niedriger Leistung heißt umgekehrt: die Spielzeit selbst ist der Engpass."),
        ("inout",
            "Abgeschlossene Fahrten im Zeitraum: Einlagerungen (ENDDEP) / Auslagerungen (ENDPUP). " +
            "Die Anlage meldet jedes Ereignis doppelt (DM/AK) — das ist herausgerechnet."),
        ("avgdur",
            "Mittlere Zeit von der Auftragserteilung bis zum Abschluss, gepaart über die " +
            "LE-Nummer: DEPORD→ENDDEP für Einlagerungen, PUPORD→ENDPUP für Auslagerungen."),
        ("cycles",
            "Spiele in Doppelspiel-Äquivalent: Doppelspiele + Einzelspiele/2. Im " +
            "Aufzeichnungsraster bestimmt und erst dann summiert — die Zahl hängt damit nicht " +
            "davon ab, wie grob die Ansicht gerade zusammenfasst."),
        ("share",
            "Anteil dieses Geräts an den Spielen aller RBG im Zeitraum. Bei gleichmäßiger " +
            "Verteilung läge jedes Gerät bei 100 % ÷ Anzahl der Geräte."),
        ("spread",
            "Spreizung der Belastung: (meiste − wenigste Spiele) ÷ meiste. 0 % = alle RBG gleich " +
            "belastet. Geräte ohne jede Bewegung bleiben außen vor, damit ein abgeschaltetes RBG " +
            "die Zahl nicht auf 100 % zieht."),
        ("avgcycles",
            "Spiele pro Stunde über den gesamten Zeitraum, Stillstände eingerechnet — das ist " +
            "die tatsächliche Belastung. Zum Einordnen daneben „Aktive Std.“."),
        ("peak",
            "Höchster Stützpunkt der Verlaufskurve in Spiele/h — die Spitzenlast, die das Gerät " +
            "in einem Raster tatsächlich gefahren hat."),
        ("cbusy",
            "Belegung — Zeitseite eines Fördertechnikpunkts: Anteil des Zeitraums, in dem ein " +
            "Transport lief. Gemessen von der Auftragserteilung (TSPORD) bis zum Transportende " +
            "(ENDTSP). Die Transporte eines Punkts laufen nacheinander, der Wert ist deshalb " +
            "ein echtes Zeitmaß — anders als beim RBG wird hier nichts doppelt gezählt."),
        ("ctransport",
            "Ø Dauer eines Transports an diesem Punkt: von TSPORD bis ENDTSP, gepaart über die " +
            "LE-Nummer. Steigt dieser Wert bei gleichbleibender Menge, wird der Punkt langsamer."),
        ("cwait",
            "Ø Wartezeit von ENDTSP bis zum nächsten TSPORD — wie lange der Punkt ohne Auftrag " +
            "dastand. Lange Wartezeiten heißen: nicht dieser Punkt ist der Engpass, sondern die " +
            "Zuführung davor."),
        ("cfree",
            "Frei-Meldungen (RPFREE) des Ressourcenpunkts im Zeitraum: wie oft er sich als frei " +
            "gemeldet hat."),
        ("ccount",
            "Transportaufträge (TSPORD) / beendete Transporte (ENDTSP) im Zeitraum. Klaffen die " +
            "Zahlen auseinander, laufen Transporte über den Fensterrand hinaus oder stehen noch aus."),
        ("active",
            "Stunden mit mindestens einer Fahrt. Zeigt, ob ein niedriger Durchschnitt von " +
            "gleichmäßig wenig Arbeit kommt oder von längerem Stillstand."),
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
