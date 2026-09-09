// Erklärtexte der RBG- und Fördertechnik-Kennzahlen. Die Seiten hängen sie über
// help('<schlüssel>') als title an die jeweilige Zahl. Früher serverseitig aus RbgGlossary
// injiziert; für aufgetrennte Seiten hier als eigene Datei.
window.RBG_HELP = {
  busy:
    'Auslastung — die Zeitseite: Anteil des Zeitraums, in dem das Gerät Transporte gefahren ' +
    'hat (Summe der Transportdauern ÷ Zeitraum). Die Transporte eines RBG laufen nacheinander, ' +
    'der Wert ist damit ein echtes Zeitmaß. Gemessen vom Auftrag zum Aufnehmen bis zum ' +
    'abgeschlossenen Abgeben, über das Tacho-Fenster aus der Kopfzeile.',
  load:
    'Leistung — die Mengenseite: geschaffte Spiele gegen die Kapazität des Geräts. Gerechnet ' +
    'über den Zeitbedarf laut Auslegung: jedes Doppelspiel kostet 3600/DS-pro-Stunde Sekunden, ' +
    'jedes Einzelspiel seine eigene Spielzeit. Das ist die belastbare Durchsatzzahl. Hohe ' +
    'Auslastung bei niedriger Leistung heißt: es fehlt nicht an Aufträgen, die einzelne Fahrt ' +
    'dauert zu lang. Gemessen über das Tacho-Fenster aus der Kopfzeile und auf eine Stunde ' +
    'hochgerechnet — die Verlaufskurve daneben nutzt ein eigenes, meist größeres Glättungsfenster.',
  double:
    'Doppelspiele pro Stunde (kombiniertes Spiel, FEM 9.851): Ein- und Auslagerung in einer ' +
    'Fahrt — die wirtschaftliche Betriebsart, weil keine Leerfahrt anfällt. Ein Doppelspiel ' +
    'besteht aus ZWEI Transporten, gezählt als min(Einlagerungen, Auslagerungen). Hochgerechnet ' +
    'auf die Stunde aus dem Trailing-Fenster der Tachos ("Tacho aus (min)" in der Kopfzeile).',
  single:
    'Einzelspiele pro Stunde (FEM 9.851): reine Ein- oder Auslagerung, die Gegenrichtung bleibt ' +
    'leer. Gezählt als |Einlagerungen − Auslagerungen|. Kostet laut Auslegung 75 s gegen 120 s ' +
    'beim Doppelspiel, zählt bei der Leistung mit 62,5 % — nicht mit der Hälfte. Hochgerechnet ' +
    'auf die Stunde aus dem Trailing-Fenster der Tachos ("Tacho aus (min)").',
  idle:
    'Leerlauf: Zeit ohne offenen Auftrag = Zeitraum − belegte Auftragszeit. Viel Leerlauf ' +
    'heißt, dem Gerät fehlt Arbeit (Versorgung oder Vorgelagertes bremst). Wenig Leerlauf bei ' +
    'niedriger Leistung heißt umgekehrt: die Spielzeit selbst ist der Engpass.',
  inout:
    'Abgeschlossene Transporte pro Stunde: Einlagerungen / Auslagerungen, hochgerechnet aus dem ' +
    'Trailing-Fenster der Tachos ("Tacho aus (min)"). Jeder Transport besteht aus Aufnehmen ' +
    '(PUPORD→ENDPUP) und Abgeben (DEPORD→ENDDEP); die Richtung steht im Ziel des ENDDEP — ein ' +
    'Regalplatz (rein numerisch) bedeutet Einlagerung, eine Station Auslagerung.',
  avgdur:
    'Mittlere Dauer eines Transports, gepaart über die LE-Nummer: vom Auftrag zum Aufnehmen ' +
    '(PUPORD) bis zum abgeschlossenen Abgeben (ENDDEP). Zwei solche Transporte ergeben ein ' +
    'Doppelspiel.',
  cycles:
    'Spiele in Doppelspiel-Äquivalent: Doppelspiele + Einzelspiele/2. Im Aufzeichnungsraster ' +
    'bestimmt und erst dann summiert — die Zahl hängt damit nicht davon ab, wie grob die ' +
    'Ansicht gerade zusammenfasst.',
  share:
    'Anteil dieses Geräts an den Spielen aller RBG im Zeitraum. Bei gleichmäßiger Verteilung ' +
    'läge jedes Gerät bei 100 % ÷ Anzahl der Geräte.',
  spread:
    'Spreizung der Belastung: (meiste − wenigste Spiele) ÷ meiste. 0 % = alle RBG gleich ' +
    'belastet. Geräte ohne jede Bewegung bleiben außen vor, damit ein abgeschaltetes RBG die ' +
    'Zahl nicht auf 100 % zieht.',
  avgcycles:
    'Spiele pro Stunde über den gesamten Zeitraum, Stillstände eingerechnet — das ist die ' +
    'tatsächliche Belastung. Zum Einordnen daneben „Aktive Std.".',
  peak:
    'Höchster Stützpunkt der Verlaufskurve in Spiele/h — die Spitzenlast, die das Gerät in ' +
    'einem Raster tatsächlich gefahren hat.',
  cbusy:
    'Belegung — Zeitseite eines Fördertechnikpunkts: Anteil des Zeitraums, in dem der Platz ' +
    'besetzt war. Belegt ist er von der Ankunft der Ladeeinheit (ENDTSP) bis zu ihrem ' +
    'Verlassen (RPFREE); der Weitertransport-Auftrag (TSPORD) wird dazwischen erteilt.',
  coccupied:
    'Ø Verweildauer einer Ladeeinheit auf dem Punkt: von der Ankunft (ENDTSP) bis zum ' +
    'Verlassen (RPFREE) — Warten auf den Auftrag und Abtransport zusammen.',
  corderwait:
    'Ø Zeit von der Ankunft (ENDTSP) bis zum Weitertransport-Auftrag (TSPORD): die Ladeeinheit ' +
    'steht und wartet auf die Entscheidung des MFR. Das ist Steuerungszeit, keine Fahrzeit — ' +
    'hohe Werte hier kosten Durchsatz, ohne dass die Mechanik langsamer wäre.',
  cdepart:
    'Ø Zeit vom Auftrag (TSPORD) bis zum Verlassen des Punkts (RPFREE): der eigentliche ' +
    'Abtransport. Lange Zeiten deuten auf Rückstau dahinter — die Ladeeinheit kommt nicht weg.',
  cwait:
    'Ø Leerzeit von RPFREE bis zur nächsten Ankunft (ENDTSP) — wie lange der Platz effektiv ' +
    'leer dastand. Lange Leerzeiten heißen: nicht dieser Punkt ist der Engpass, sondern die ' +
    'Zuführung davor.',
  cidle:
    'Gesamte effektiv leere Zeit im Zeitraum = Zeitraum − belegte Zeit, also die Summe aller ' +
    'Spannen zwischen dem Verlassen und der nächsten Ankunft.',
  cfree:
    'Frei-Meldungen (RPFREE) des Ressourcenpunkts im Zeitraum: wie oft eine Ladeeinheit den ' +
    'Platz verlassen hat.',
  ccount:
    'Ankünfte (ENDTSP) / Weitertransport-Aufträge (TSPORD) / Frei-Meldungen (RPFREE) pro ' +
    'Stunde, hochgerechnet aus dem Trailing-Fenster der Tachos ("Tacho aus (min)"). Klaffen die ' +
    'Zahlen auseinander, reicht eine Belegung über den Fensterrand hinaus oder steht noch aus.',
  active:
    'Stunden mit mindestens einer Fahrt. Zeigt, ob ein niedriger Durchschnitt von gleichmäßig ' +
    'wenig Arbeit kommt oder von längerem Stillstand.',
};
