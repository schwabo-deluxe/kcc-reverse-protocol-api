# kcc — Telegramm-Recorder für Kardex MCC/KCC

Schneidet die SPS-Telegramme (`PlcProtocolExtendedDTO`) einer Kardex MCC/KCC-Anlage über deren
WebSocket-API mit und legt sie in einer lokalen SQLite-Datenbank ab. Eine Filterfunktion sorgt
dafür, dass nur Telegramme mit tatsächlichem Dateninhalt aufgezeichnet werden.

Das Werkzeug ist eine **self-contained Single-File-EXE für Windows** — kein installiertes .NET
nötig, einfach `kcc.exe` kopieren und starten.

## Schnellstart

```
kcc login-test --url wss://10.20.220.33/ws --user MEINUSER --insecure
kcc
```

`kcc` ohne Argumente ist der Normalbetrieb: Aufzeichnung und Website (Dashboards + API) laufen
gemeinsam in einem Prozess, gesteuert über `appsettings.json` (`Record`, `Serve`, `ApiUrl`). Die
Website läuft auf **Kestrel** und hört ab Werk auf **`http://+:8082/`** (alle Schnittstellen) —
aus dem Netz unter `http://<host>:8082/`, **ohne Admin oder URL-Freigabe**. Für nur lokal:
`ApiUrl` auf `http://localhost:8082/` setzen.

Beim ersten Lauf setzt der Recorder am aktuellen Ende der Protokolltabelle an und lädt die
letzten `StartupBackfillMinutes` (Standard 4 Stunden) einmalig nach, damit das Dashboard sofort
Historie zeigt. Weiter zurück holt `kcc backfill`; ein Neustart setzt ohnehin lückenlos bei der
zuletzt gesehenen Id wieder an.

## Kommandos

| Kommando | Zweck |
|---|---|
| `kcc login-test` | Verbindung, Zertifikat und Anmeldung prüfen |
| `kcc` (ohne Argumente) | Normalbetrieb: Aufzeichnung + Lese-API/Dashboard |
| `kcc query --take 100 [--json]` | Einmalabfrage auf stdout — zum Abgleich mit dem Web-Grid |
| `kcc backfill --from-id N [--to-id M]` | Ältere Telegramme nachladen (baut anschließend die UPH-Historie neu auf) |
| `kcc prune [--days N]` | Telegramme älter als N Tage löschen (Standard: `RetentionDays`) |
| `kcc uph-rebuild` | UPH- und RBG-Historie (`/verlauf`, `/rbg`) aus den vorhandenen Telegrammen neu aufbauen — nach einem separaten `backfill` oder Änderung der `UphHistory*`-/`Rbg*`-Optionen. RBG-Rasterzeilen aus der Zeit vor dem ältesten Telegramm bleiben dabei erhalten |
| `kcc export --out datei.csv [--from …] [--to …]` | Aufgezeichnete Telegramme als CSV |

`kcc help` listet alle Optionen.

## Konfiguration

Alles wird über `appsettings.json` konfiguriert. Die Datei liegt neben der EXE (und wird beim
Build dorthin kopiert). Werte werden in dieser Reihenfolge übernommen (später schlägt früher):

1. `appsettings.json` — neben der EXE, danach im aktuellen Arbeitsverzeichnis
2. `appsettings.local.json` — ebenda, für Zugangsdaten; **gehört nicht ins Repository** (`.gitignore`)
3. eine per `--config datei.json` angegebene Datei
4. Umgebungsvariablen mit Präfix `KCC_` — flach (`KCC_URL`, `KCC_USER`, `KCC_PASSWORD`,
   `KCC_DATABASE`) oder geschachtelt (`KCC_Filter__MinDataLength`)
5. Kommandozeile (`--url`, `--user`, `--password`, `--db`, `--poll-interval`, `--batch-size`,
   `--insecure`)

Fehlt das Passwort, wird es verdeckt abgefragt. Für den Normalbetrieb `appsettings.json` anpassen
und die Zugangsdaten in ein `appsettings.local.json` daneben schreiben:

```json
{ "User": "MEINUSER", "Password": "geheim" }
```

### Ausgabe

| Feld | Bedeutung |
|---|---|
| `Database` | SQLite-Datei (Standard: `kcc-telegrams.db`) |
| `Record` / `Serve` | Was der Aufruf ohne Argumente startet: Aufzeichnung bzw. Lese-API + Dashboard (Standard: beides `true`) |
| `CsvPath` | Wenn gesetzt, werden aufgezeichnete Telegramme im Normalbetrieb und bei `backfill` **zusätzlich** fortlaufend an diese CSV angehängt (Standard: `kcc-telegrams.csv`). `null` schaltet die CSV ab. |
| `DataFormat` | Fixed-Width-Layout des `Data`-Blocks für die CSV-Spalten. `null` = eingebautes Standard-Layout. |
| `WindowMinutes` | Zeitfenster des KPI-Dashboards (`/`) und der API ohne `minutes`-Parameter (Standard: `240`, also 4 Stunden) |
| `StartupBackfillMinutes` | Beim ersten Start einmalig nachgeladene Zeitspanne, damit das Dashboard sofort Historie zeigt (Standard: `240`). `0` schaltet das ab. |
| `UtilizationWindowMinutes` | Zeitfenster der Auslastungsansicht (`/auslastung`) ohne `minutes`-Parameter (Standard: `60`) |
| `UtilizationBucketMinutes` | Breite des **gleitenden** Fensters der Verlaufskurven in der Auslastungsansicht ohne `bucket`-Parameter (Standard: `10`). Der Verlauf gleitet, statt in feste Eimer zu springen — der letzte Punkt ist der aktuell laufende Trailing-Wert, deckungsgleich mit der Tacho-Anzeige. |
| `UtilizationSeriesStepMinutes` | Abtastschritt der Verlaufskurven ohne `step`-Parameter (Standard: `1`) — ein Stützpunkt je Schritt |
| `UtilizationTargetUph` | Vorgabe-Richtwert in Einheiten/Stunde, auf den sich die Auslastung in Prozent bezieht (Standard: `200`). Je Punkt über `ResourcePoints[].TargetUph` überschreibbar. |
| `UtilizationRateMinutes` | **Trailing-Fenster aller Tachos** (Standard `5`): UPH, RBG-Spiele/h und Fördertechnik-Belegung werden daraus auf eine Stunde hochgerechnet. Klein = reagiert sofort auf kurze Stöße, springt aber (bei wenigen Ereignissen je Fenster wird ein einzelnes Spiel zu mehreren Prozentpunkten); groß = träger, aber ruhiger. In der Kopfzeile als „Tacho aus (min)" änderbar. `Count`, die Zieltabelle und die **Verlaufskurve** bleiben davon unberührt — die Kurve glättet über `UtilizationBucketMinutes`. |
| `ResourcePoints` | Liste der ausgewerteten Ressourcenpunkte, je Eintrag `{ "Name": "MA72", "Group": "Auslagerung RBG", "Label": "RBG A", "Order": 1, "TargetUph": 250, "Connection": "RBG01" }`. `Group`/`Label`/`Order`/`TargetUph`/`Connection`/`MaxCyclesPerHour` optional. Das Dashboard bündelt die Kacheln und die Tabelle nach `Group` und zeigt je Gruppe eine Summe. Innerhalb einer Gruppe wird nach `Order` (aufsteigend) sortiert, ohne Angabe nach Listenposition. `TargetUph` setzt den UPH-Richtwert dieses Punkts; ohne Angabe gilt `UtilizationTargetUph`. `Connection` (Telegramm-Feld `ConnectionName`, z. B. `RBG01`) blendet in der Kachel zusätzlich die RBG-Spielauswertung ein; `MaxCyclesPerHour` überschreibt dafür `RbgMaxCyclesPerHour`. Leere Liste ⇒ eingebaute Vorgabe. |
| `GroupOrder` | Reihenfolge der Gruppen im Auslastungs-Dashboard, z. B. `[ "Auslagerung RBG", "Fördertechnik" ]`. Nicht genannte Gruppen folgen nach erstem Auftreten in `ResourcePoints`. |
| `DestinationLabels` | Klartext für Endziele, z. B. `{ "GA51": "Kommissionierung" }`. Das Endziel ist das führende Token des letzten 33er-Blocks im `Data`-Feld (4 oder 5 Zeichen, z. B. `GA51` oder `DLL13`); je Kachel zeigt eine kleine Tabelle den %-Anteil je Ziel. Gemappte Ziele erscheinen als `GA51 (Kommissionierung)`, unbekannte roh. Ein Schlüssel mit `*` am Ende ist ein Präfixmuster: `{ "DLL*": "Auslagerung DLL" }` fasst alle `DLL…` zu einem Ziel `DLL*` zusammen (exakte Treffer schlagen Muster, längstes Präfix gewinnt). |
| `UphHistoryIntervalMinutes` | Rasterweite **beider** Langzeitreihen in Minuten (Standard `5`): UPH-Historie (`/verlauf`) und RBG-Aufzeichnung (`/rbg`). Das ist die feinste Auflösung, die diese Ansichten je zeigen können — was einmal gröber verdichtet wurde, lässt sich nachträglich nicht mehr auftrennen. Nach einer Änderung `kcc uph-rebuild` laufen lassen, sonst bleiben ältere Zeilen im alten Raster. |
| `UphHistoryRetentionDays` | Aufbewahrung der UPH-Historie in Tagen (Standard `28` = 4 Wochen), getrennt von `RetentionDays` der Rohtelegramme. `0`/negativ = unbegrenzt. `/verlauf` zeigt nur diesen Zeitraum — für weiter zurück den Wert erhöhen und `kcc uph-rebuild` laufen lassen. Der Recorder baut die Historie bei jedem Start und nach `backfill` neu auf. |
| `RbgHistoryRetentionDays` | Aufbewahrung der RBG-Langzeitaufzeichnung (`/rbg`) in Tagen (Standard `365`). Bewusst länger als `UphHistoryRetentionDays`: der Belastungsvergleich der Geräte lebt von langen Zeiträumen, und die Rasterzeilen sind winzig (eine je Raster und Verbindung, ~1.400/Tag bei 5 RBG und 5-min-Raster). `0`/negativ = unbegrenzt. Aufgezeichnet wird für jede unter `ResourcePoints` konfigurierte `Connection`. |
| `ContourCheckpoints` | Konturkontrollen für `/kontur`, je Eintrag `{ "ResourcePoint": "LB21", "MessageCode": "ENDTSP", "Label": "…" }`. Ausgewertet wird das `Status`-Feld (`Kxyz`) dieser Telegramme. Leere Liste ⇒ eingebaute Vorgabe (LB21 ENDTSP, DA91/AA41/NA41 TSPREG). |
| `ContourFlags` | Bedeutung der Fehlerbits im Konturergebnis `Kxyz`, je Eintrag `{ "Nibble": 0, "Bit": 2, "Label": "Profil links" }` — `Nibble` 0 = `x`, 1 = `y`, 2 = `z`; `Bit` 0…3. Leere Liste ⇒ eingebaute Tabelle laut Doku „Konturenfehler (Kxyz)". `Status = "…."` (leer) = kein Konturfehler. |
| `ContourWindowMinutes` | Zeitfenster der Konturauswertung ohne `minutes`-Parameter (Standard: `480` = 8 h) |
| `RbgMaxCyclesPerHour` / `RbgMaxPutsPerHour` / `RbgMaxFetchesPerHour` | **Auslegungsleistung eines RBG** in Doppelspielen, reinen Einlagerungen bzw. reinen Auslagerungen pro Stunde (Standard `30` / `48` / `48` — HRL RBG 1–5). Daraus folgt die Spielzeit je Betriebsart (120 s / 75 s / 75 s) und damit der **Leistungsgrad**: Zeitbedarf der gefahrenen Spiele ÷ Fenster. Ein Einzelspiel ist dabei ausdrücklich **nicht** ein halbes Doppelspiel — bei 30 DS/h und 48 E/h kostet es 62,5 % davon. Begriffe nach FEM 9.851: **Einzelspiel** = reine Ein- oder Auslagerung, **Doppelspiel** = beides in einer Fahrt. Je Gerät über `ResourcePoints[].MaxCyclesPerHour` / `MaxPutsPerHour` / `MaxFetchesPerHour` überschreibbar (z. B. RBG 11 Versandpuffer: 56 / 105 / 105). |
| `CountTelegramType` | Telegrammtyp, der bei **allen** Zählungen gewertet wird (Standard `DM`). Die Anlage schickt jedes Ereignis als Paar: `DM` (Data Message — die Meldung selbst) und `AK` (Acknowledge der Gegenstelle), dazu `LM` als leere Lebensmeldung. Bei Fördertechnik-Verbindungen ist der `AK` leer, bei RBG-Verbindungen trägt er denselben Inhalt wie das `DM`. Ohne diese Einschränkung hinge die Entdopplung allein an der inhaltlichen Zusammenführung; mit ihr ist sie eindeutig. `AK` wäre gleichwertig, leer = alle zählen. |
| `RbgPutDoneCodes` / `RbgFetchDoneCodes` | MessageCodes einer abgeschlossenen Ein- bzw. Auslagerung. Leer ⇒ `["ENDDEP"]` / `["ENDPUP"]`. Diese Ende-Telegramme zählen für Doppel-/Einzelspiele und die Auslastung. |
| `ConveyorOrderCodes` / `ConveyorEndCodes` / `ConveyorFreeCodes` | MessageCodes der Fördertechnik-Belegung. Leer ⇒ `["TSPORD"]` / `["ENDTSP"]` / `["RPFREE"]`. Ablauf einer Ladeeinheit laut Kardex-Doku „Transportverwaltung Paletten-Fördertechnik": `ENDTSP` meldet die **Ankunft** auf dem Punkt (ab hier belegt), `TSPORD` ist der Auftrag zum **Weitertransport** (der Punkt bleibt dabei belegt), `RPFREE` meldet das **Verlassen** (ab hier frei). **Belegt = ENDTSP → RPFREE**, leer = RPFREE → nächste Ankunft. Die Belegung zerfällt in zwei Hälften: `ENDTSP`→`TSPORD` ist Wartezeit auf die Entscheidung des MFR (Steuerungszeit, keine Fahrzeit), `TSPORD`→`RPFREE` der eigentliche Abtransport (langer Wert = Rückstau dahinter). Ausgewertet über einen Zustandsautomaten auf dem Zeitstrahl, nicht über Ereignispaare — Belegung ist eine Eigenschaft des Platzes, nicht der Ladeeinheit. Ein Punkt, der kein `RPFREE` meldet, gilt durchgehend als belegt; der Zählerstand `Ankunft/Auftrag/Frei` in der Kachel macht das sichtbar. |
| `RbgPutOrderCodes` / `RbgFetchOrderCodes` | MessageCodes der Auftragserteilung. Leer ⇒ `["DEPORD"]` / `["PUPORD"]`. Auftrag→Ende-Paare (per `ResourceLabel`) liefern die Ø Ausführungsdauer und die Leerlaufzeit. Die Anlage doppelt jedes Ereignis (`DM`/`AK`); das wird zusammengeführt. |
| `RetentionDays` | Aufbewahrungsdauer in Tagen (Standard: `365`). Normalbetrieb/`backfill` löschen beim Start und danach täglich Telegramme mit älterem `DateTime`; `kcc prune` tut es einmalig. `0`/negativ = unbegrenzt. |
| `ReconnectDelaySeconds` | Wartezeit vor dem ersten Reconnect nach Verbindungsabbruch (Standard `5`); verdoppelt sich je Fehlversuch bis `ReconnectMaxDelaySeconds`. Nach dem Reconnect wird bis zum aktuellen Ende nachgeholt. |
| `ReconnectMaxDelaySeconds` | Obergrenze der Reconnect-Wartezeit (Standard `60`). |

Die CSV wird im Anhänge-Modus geführt: ein Neustart schreibt weiter, die Kopfzeile nur einmal.
Gleiches Semikolon-Format wie `kcc export` (UTF-8 mit BOM, für Excel im deutschen Gebietsschema).

Neben den Stammspalten (`Id;DateTime;TelegramDirection;ConnectionName;Data`) wird der `Data`-Block
anhand von `DataFormat` in **je eine Spalte pro Feld** zerlegt. Die Syntax entspricht dem
`Format`-Feld der Anlage — pipe-getrennte Tripel `Name,Länge,Typ`:

```
TelegramType,2,A|SequenceNumber,2,A|Sender,4,A|Receiver,4,A|TelegramCount,2,A|ErrorCode,2,A|
MessageCode,6,A|Length,4,A|ResourcePoint,10,A|ResourceLabel,20,A|Source,10,A|Destination,10,A|
Type,3,A|TechnicalValues,20,A|WrapperProgram,4,A|LabelingProgramm,4,A|Command,8,A|Weight,6,A|
Status,4,A|PlaceConfig,4,A|FinishId,4,A|Reserve,33,A
```

Das ist das ab Werk in `appsettings.json` hinterlegte Standard-Layout (166 Zeichen). Die Anlage
füllt Felder rechts mit **Punkten** auf (`MB11......`); Füllzeichen (`.`, Leerzeichen, NUL) werden
je Feld abgeschnitten. Zu kurze Blöcke ergeben leere Felder, überzählige
Zeichen werden ignoriert.

### Filter

Ein Telegramm wird aufgezeichnet, wenn sein `Data`-Feld nicht leer ist, nicht nur aus Nullen
besteht und dem `DataMatchRegex` entspricht. Anpassbar über den `Filter`-Abschnitt:

| Feld | Bedeutung |
|---|---|
| `MinDataLength` | Mindestlänge von `Data` nach Trim |
| `IgnoreAllZeroData` | Verwirft Frames aus lauter Nullen |
| `ConnectionWhitelist` / `ConnectionBlacklist` | Verbindungen ein-/ausschliessen |
| `Directions` | `ToPlc`, `FromPlc`, `Unknown` |
| `DataMatchRegex` / `DataIgnoreRegex` | Feinsteuerung über reguläre Ausdrücke |
| `FilterEmptyDataOnServer` | Lässt schon den Server leere Frames aussortieren (spart Bandbreite) |

`DataMatchRegex` ist ab Werk auf `0150` gesetzt. Die Anlage schickt laufend Handshake-Frames, die
nur aus dem 16 Zeichen langen Kopf plus Füllbytes bestehen — echte Nutztelegramme
(`…TSPORD0150…`, `…RPFREE0150…`, `…0150…END.`) tragen dagegen alle das Feld `0150`. So landen nur
Telegramme mit tatsächlichem Inhalt in der Datenbank. Auf `null` setzen, um wieder alles
aufzuzeichnen.

## Dashboard / API

Der Normalbetrieb (`kcc` ohne Argumente) stellt neben der Aufzeichnung eine kleine **Website**
(Lese-API + Dashboards) auf **Kestrel** bereit. Adresse und Betriebsart stehen in `appsettings.json`:

```json
"ApiUrl": "http://+:8082/",
"Record": true,
"Serve": true
```

`ApiUrl` gibt Host und Port vor. `+` / `*` / `0.0.0.0` = **alle Netzwerk-Schnittstellen** —
die Seiten sind dann unter `http://<host>:8082/` erreichbar, **ohne Admin oder
`netsh`-URL-Freigabe** (Kestrel bindet den Socket direkt, kein Windows-`http.sys`). `localhost`
bzw. `127.0.0.1` beschränkt auf lokal, eine feste IP bindet nur diese. Jede Seite hat oben eine
Navigationsleiste (KPIs · Auslastung · Verlauf · Kontur).

| Endpunkt | Zweck |
|---|---|
| `GET /` | Dashboard (eine HTML-Datei, pollt `/api/kpis` jede Minute) |
| `GET /api/kpis?minutes=240` | Kennzahlen über das Zeitfenster |
| `GET /api/telegrams?minutes=240&limit=2000` | Telegramme des Zeitfensters (aufsteigend) |
| `GET /api/fields?minutes=5&limit=20` | Diagnose: die letzten Telegramme Feld für Feld nach `DataFormat` zerlegt — zeigt, welches Feld den Ressourcenpunkt trägt |
| `GET /auslastung` | Auslastung der Ressourcenpunkte, gebündelt nach `Group`. Jede Kachel zeigt **zwei Tachos — Zeitseite und Mengenseite**. Punkte mit `Connection` (RBG): **Auslastung** (Zeit mit offenem Auftrag) und **Leistung** (Spiele/h gegen `RbgMaxCyclesPerHour`); Tacho, Linienchart und Kopfzeile rechnen dort in Doppel-/Einzelspielen, die `TSPORD`-Zähler dienen nur der Ziel-Identifikation. Punkte ohne `Connection` (Fördertechnik): **Belegung** (`ENDTSP`→`RPFREE` ÷ Fenster) und **Leistung** (% vom UPH-Richtwert), darunter Ø Verweildauer, Ø bis Auftrag, Ø Abtransport und Ø Leerzeit. Alle Kennzahlen tragen einen Erklär-Tooltip; die Tachos blenden zwischen zwei Abrufen über, statt zu springen |
| `GET /api/utilization?minutes=60&target=200&bucket=5&rate=1&step=1` | Dieselbe Auswertung als JSON. `rate` = Trailing-Fenster für UPH/Prozent; `bucket` = Breite des gleitenden Verlaufsfensters; `step` = Abtastschritt des Verlaufs |
| `GET /verlauf` | UPH-Historie als gestapelte Fläche, wahlweise **je Endziel oder je Ressourcenpunkt** (Umschalter „Stapeln nach"), plus Mengenverhältnis und Tabelle. Zeitbereich per Maus aufziehen zoomt hinein (Doppelklick / „Zoom zurück" setzt zurück). Bereich **8 h** ist ein gleitender Kurzzeit-Verlauf (5-min-Fenster, direkt aus den Rohtelegrammen, letzter Punkt = aktueller Wert wie bei `/auslastung`); die längeren Bereiche speisen sich aus einer verdichteten Rollup-Tabelle mit **eigener Aufbewahrung** `UphHistoryRetentionDays` (Standard 4 Wochen) — weiter zurück als dieser Zeitraum reicht `/verlauf` nicht, auch nach `backfill` nicht |
| `GET /api/uph-history?hours=168&bucket=15&groupBy=destination&rp=MA72` | Historie als JSON: Buckets je Reihe (Menge + UPH), Summen mit Ø UPH und Anteil. `groupBy` = `destination` (Vorgabe) oder `resourcePoint`; `hours` bis 672 (4 W) **oder** absolutes Fenster `from=…&to=…` (ISO, UTC); `bucket` = Stützpunktabstand; `rolling=<min>` schaltet auf ein gleitendes Fenster aus den Rohtelegrammen um (`bucket` wird dann der Abtastschritt); `rp` grenzt zusätzlich auf einen Ressourcenpunkt ein |
| `GET /kontur` | Auswertung der Konturkontrollen: welche Konturfehler an welchem Kontrollpunkt auflaufen. Zerlegt das `Status`-Feld (`Kxyz`) der Telegramme aus `ContourCheckpoints` in benannte Fehlerbits (`ContourFlags`). KPIs, Balken je Fehlerart, Kreuztabelle Kontrollpunkt × Fehlerart, sowie je Kontrollpunkt die letzten 10 Fehler mit Zeit, LE-/ID-Nummer und aufgelösten Fehlern |
| `GET /api/kontur?minutes=480` | Dieselbe Auswertung als JSON |
| `GET /rbg` | **Langzeitvergleich der RBG**: welches Gerät wird stärker belastet? Spreizung (der schwächste RBG fährt X % weniger als der stärkste), Anteil je Gerät an allen Spielen, Verlauf der Spiele/h bzw. des Auslastungsgrads als Mehrlinien-Diagramm (Legende schaltet Geräte ab, Fadenkreuz zeigt alle Werte eines Zeitpunkts) und eine Kennzahlentabelle. Zeiträume 24 h bis 1 Jahr. Speist sich aus der eigenen Rasterreihe (`RbgHistoryRetentionDays`) und reicht damit weiter zurück als die Rohtelegramme |
| `GET /api/rbg-history?hours=168&bucket=60` | Derselbe Vergleich als JSON. `hours` bis 8784 (1 Jahr) **oder** absolutes Fenster `from=…&to=…` (ISO, UTC); `bucket` = Stützpunktabstand in Minuten |
| `GET /wand` | Wandansicht derselben Auslastungsdaten (`/api/utilization`): erkennt per `orientation: landscape` das Querformat und legt jede `Group` (RBG, Fördertechnik …) als eigene, klar getrennte, formatfüllende Spalte ohne Seiten-Scroll ab. Kompakte Kacheln mit Tacho, %, Verlauf; bei RBG zusätzlich Auslastung/Leistung/Doppel-/Einzelspiele/Leerlauf. Vollbild-Schaltfläche. Im Hochformat stapeln sich die Spalten |
| `GET /health` | Status, DB-Pfad, Gesamtzahl, `lastSeenId`, jüngster Telegramm-Zeitstempel, Sekunden seit letztem Schreibvorgang, Server-Uhr |

Ohne `minutes` gilt `WindowMinutes` (Standard 4 Stunden); der Parameter wird auf 1…1440 begrenzt, `limit` auf 1…20000. Die KPIs (`/api/kpis`): Anzahl,
Telegramme/Minute, Fehler (`ErrorCode`-Feld ≠ 0), Sekunden seit dem letzten Schreibvorgang, aktive
Verbindungen sowie Verteilung nach Richtung, Verbindung und `MessageCode`.

**Zeitfenster:** Der rechte Rand ist der Zeitstempel des **jüngsten Telegramms in der DB**, nicht
die Uhr des API-Hosts. So bleibt „letzte N Minuten" richtig, auch wenn die Anlage ihre
Zeitstempel in einer anderen Zeitzone schickt als der Rechner, auf dem `kcc` läuft.
`GET /health` zeigt beide Zeiten, um einen solchen Versatz sichtbar zu machen.

**Zeitzone:** Die Anlage liefert die Zeitstempel in **UTC**; die DB legt sie zeitzonenfrei ab. Die
API gibt sie als echtes UTC-ISO (`…Z`) aus, sodass die Dashboards sie in die **lokale Zeit des
Betrachters** umrechnen. `GET /api/telegrams` liefert dieselben Werte, ebenfalls als UTC.

## Wie es funktioniert

Die Anlage bietet **keinen Push** für Protokolldaten — auch die Weboberfläche pollt. Da `Id`
monoton vergeben wird, fragt der Recorder wiederholt „alles mit `Id > zuletzt gesehen`", aufsteigend
sortiert. Das ist lückenlos und wiederholbar; ein Neustart setzt exakt dort wieder an, weil der
Stand in der Datenbank liegt.

**Reconnect:** Bricht die WebSocket-Verbindung ab, beendet sich der Recorder nicht, sondern
verbindet neu — mit wachsender Wartezeit (`ReconnectDelaySeconds`, verdoppelt bis
`ReconnectMaxDelaySeconds`). Nach dem Reconnect holt er aus derselben `Id`-Logik alle in der
Auszeit angefallenen Telegramme bis zum aktuellen Ende nach. Beenden weiterhin mit Strg+C.

Die Anmeldung folgt dem Browser-Client: `Subscribe` liefert `SessionId` und den öffentlichen
RSA-Schlüssel im .NET-XML-Format, das Passwort wird mit **RSA-2048/OAEP-SHA1** verschlüsselt und als
Byte-Array (nicht Base64) an `LogonUser` geschickt. Dass dabei **keine** Byte-Umkehrung nötig ist,
sichert ein Test gegen einen Ciphertext ab, den die originale Client-Bibliothek erzeugt hat —
siehe `tests/Kcc.Recorder.Tests/Fixtures/README.md`.

## Bauen

```
dotnet test
dotnet publish src/Kcc.Recorder/Kcc.Recorder.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Die EXE bündelt das .NET- **und** das ASP.NET-Core-Runtime (für Kestrel) — self-contained, kein
installiertes Framework nötig, dafür ~90–100 MB. `RollForward=Major` erlaubt dem framework-
abhängigen `dotnet test`/`run` auch neuere Runtimes (der Publish bleibt bei net8).

Ein Tag `vX.Y.Z` löst den Release-Workflow aus: er baut die EXE und hängt das ZIP
(`kcc.exe`, `appsettings.json`, `README.md`, `dashboard.html`, `auslastung.html`, `verlauf.html`,
`kontur.html`, `rbg.html`, `wand.html`) samt Prüfsumme an ein GitHub-Release. Die HTML-Dateien sind dieselben
Dashboards, die die API unter `/`, `/auslastung`, `/verlauf`, `/kontur`, `/rbg` bzw. `/wand` ausliefert —
`kcc dump-dashboards [--out verz]` schreibt sie jederzeit heraus.
Als lose Datei geöffnet fragen sie fest `http://localhost:8082` ab; mit `?api=http://host:port`
lässt sich ein anderer Endpunkt vorgeben. Über die API selbst ausgeliefert zählt deren Herkunft.

## Hinweis

Es handelt sich um eine nachgebaute, nicht offiziell dokumentierte Schnittstelle. Zugriff nur mit
entsprechender Freigabe und mit einem Account, der Leserechte auf die Rolle `CommonPlcProtocol` hat.
