// Erklärtexte der RBG- und Fördertechnik-Kennzahlen; die Seiten hängen sie über
// help('<schlüssel>') als title an die Zahl. Serverseitiges Gegenstück: RbgGlossary.cs.
window.RBG_HELP = {
  busy:
    'Auslastung = Σ Transportdauern ÷ Fenster. Transport = PUPORD → ENDDEP; die Fahrten eines ' +
    'RBG laufen nacheinander. Fenster: "Tacho RBG (min)".',
  load:
    'Leistung = Zeitbedarf laut Auslegung ÷ Fenster. Doppelspiel = 3600/DS-pro-h Sekunden, ' +
    'Einzelspiel = 3600/Einlagerungen-pro-h Sekunden. Fenster: "Tacho RBG (min)"; die ' +
    'Verlaufskurve nutzt das größere Glättungsfenster.',
  double:
    'Doppelspiele/h = min(Ein, Aus) ÷ Fensterstunden. Ein Doppelspiel = zwei Transporte in ' +
    'einer Fahrt (FEM 9.851). Fenster: "Tacho RBG (min)".',
  single:
    'Einzelspiele/h = |Ein − Aus| ÷ Fensterstunden (FEM 9.851). Laut Auslegung 75 s gegen 120 s ' +
    'beim Doppelspiel, zählt bei der Leistung also mit 62,5 %.',
  idle:
    'Leerlauf = Fenster − Σ Transportdauern.',
  inout:
    'Ein-/Auslagerungen pro Stunde. Transport = Aufnehmen (PUPORD→ENDPUP) + Abgeben ' +
    '(DEPORD→ENDDEP). Richtung aus dem Ziel des ENDDEP: rein numerisch = Regalplatz = ' +
    'Einlagerung, sonst Auslagerung.',
  avgdur:
    'Ø Transportdauer, über die LE-Nummer gepaart: PUPORD → ENDDEP.',
  cycles:
    'Spiele = Doppelspiele + Einzelspiele/2, je Aufzeichnungsraster bestimmt und dann summiert ' +
    '(unabhängig von der Zoomstufe).',
  share:
    'Anteil an den Spielen aller RBG im Zeitraum. Gleichverteilung = 100 % ÷ Anzahl Geräte.',
  spread:
    'Spreizung = (meiste − wenigste Spiele) ÷ meiste. Geräte ohne Bewegung zählen nicht mit.',
  avgcycles:
    'Spiele ÷ Betriebsstunden im Zeitraum, Stillstände innerhalb der Nutzungszeit eingerechnet.',
  peak:
    'Höchster Stützpunkt der Verlaufskurve in Spiele/h.',
  cbusy:
    'Belegung = Σ(ENDTSP → RPFREE) ÷ Fenster. ENDTSP = Ankunft, RPFREE = Verlassen, TSPORD ' +
    'liegt dazwischen. Fenster: "Tacho FT (min)".',
  coccupied:
    'Ø Verweildauer ENDTSP → RPFREE.',
  corderwait:
    'Ø ENDTSP → TSPORD: Wartezeit auf die MFR-Entscheidung, keine Fahrzeit.',
  cdepart:
    'Ø TSPORD → RPFREE: der Abtransport. Lange Zeiten = Rückstau dahinter.',
  cwait:
    'Ø RPFREE → nächstes ENDTSP: Platz leer, Zuführung davor bremst.',
  cidle:
    'Leer gesamt = Fenster − belegte Zeit.',
  cfree:
    'Anzahl RPFREE im Fenster.',
  ccount:
    'Ankünfte (ENDTSP) / Aufträge (TSPORD) / Frei-Meldungen (RPFREE) pro Stunde. Fenster: ' +
    '"Tacho FT (min)". Klaffen die Zahlen auseinander, reicht eine Belegung über den Fensterrand.',
  active:
    'Stunden mit mindestens einer Fahrt.',
};
