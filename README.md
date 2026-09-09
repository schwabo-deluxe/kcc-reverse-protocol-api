# kcc — Telegramm-Recorder für Kardex MCC/KCC

Schneidet die SPS-Telegramme (`PlcProtocolExtendedDTO`) einer Kardex MCC/KCC-Anlage über deren
WebSocket-API mit, legt sie in SQLite ab und wertet sie über Dashboards aus. Ein Filter hält
Handshake-Frames ohne Nutzdaten heraus.

Self-contained Single-File-EXE für Windows, kein installiertes .NET nötig.

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
| `UtilizationBucketMinutes` | Breite des gleitenden Fensters der Verlaufskurven, ohne `bucket`-Parameter (Standard: `10`). Letzter Stützpunkt = laufender Trailing-Wert. |
| `UtilizationSeriesStepMinutes` | Abtastschritt der Verlaufskurven, ein Stützpunkt je Schritt (Standard: `1`) |
| `UtilizationTargetUph` | Vorgabe-Richtwert in Einheiten/Stunde für die Prozentwerte (Standard: `200`). Je Punkt über `ResourcePoints[].TargetUph` überschreibbar. |
| `UtilizationSRMRateMinutes` | Trailing-Fenster der RBG-Tachos (Standard `15`); Auslastung, Leistung und Spiele/h werden daraus auf 1 h hochgerechnet. Klein = reaktiv und sprunghaft, groß = träge und ruhig. Kopfzeile: „Tacho RBG (min)". Verlaufskurve und Zieltabelle bleiben unberührt. |
| `UtilizationConveyorRateMinutes` | Dasselbe für die Fördertechnik-Tachos (Punkte ohne `Connection`), Standard `5`. `0` ⇒ es gilt `UtilizationSRMRateMinutes`. Kopfzeile: „Tacho FT (min)". |
| `ResourcePoints` | Ausgewertete Ressourcenpunkte, je Eintrag `{ "Name": "MA72", "Group": "Auslagerung RBG", "Label": "RBG 1", "Order": 1, "TargetUph": 60, "Connection": "RBG01", "MaxCyclesPerHour": 30 }`. Alles außer `Name` optional. Kacheln und Tabelle werden nach `Group` gebündelt, innerhalb der Gruppe nach `Order` sortiert (ohne Angabe: Listenposition). `Connection` = Telegrammfeld `ConnectionName`; gesetzt ⇒ die Kachel bekommt die RBG-Spielauswertung statt der Fördertechnik-Belegung. `MaxCyclesPerHour`/`MaxStoresPerHour`/`MaxRetrievalsPerHour` überschreiben die `Rbg*`-Vorgaben. Leere Liste ⇒ eingebaute Vorgabe. |
| `GroupOrder` | Reihenfolge der Gruppen, z. B. `[ "Auslagerung RBG", "Fördertechnik" ]`. Nicht genannte Gruppen folgen nach erstem Auftreten in `ResourcePoints`. |
| `DestinationLabels` | Klartext für Endziele, z. B. `{ "GA51": "Kommissionierung" }`. Endziel = führendes Token (4–5 Zeichen) des letzten 33er-Blocks im `Data`-Feld. Ein Schlüssel mit `*` am Ende ist ein Präfixmuster: `{ "DLL*": "…" }` fasst alle `DLL…` zu `DLL*` zusammen. Exakter Treffer schlägt Muster, längstes Präfix gewinnt; unbekannte Ziele bleiben roh. |
| `UphHistoryIntervalMinutes` | Rasterweite **aller drei** Langzeitreihen (`uph_samples`, `rbg_samples`, `point_samples`) in Minuten (Standard `5`) — die feinste je darstellbare Auflösung. Gröber Verdichtetes lässt sich nicht wieder auftrennen. Nach einer Änderung `kcc uph-rebuild` laufen lassen. |
| `UphHistoryRetentionDays` | Aufbewahrung von `uph_samples` (`/verlauf`) in Tagen (Standard `28`), getrennt von `RetentionDays`. `0`/negativ = unbegrenzt. Weiter zurück: Wert erhöhen und `kcc uph-rebuild` laufen lassen. |
| `RbgHistoryRetentionDays` | Aufbewahrung von `rbg_samples` (`/rbg`) in Tagen (Standard `365`). Eine Zeile je Raster und `Connection`, ~1.400/Tag bei 5 RBG und 5-min-Raster. `0`/negativ = unbegrenzt. |
| `PointHistoryRetentionDays` | Aufbewahrung von `point_samples` (zweiter `/verlauf`-Chart) in Tagen (Standard `365`): belegte Zeit (`ENDTSP`→`RPFREE`) und `TSPORD`-Menge je Raster, nur für die unter `ResourcePoints` angelegten Punkte. `0`/negativ = unbegrenzt. Bestandsdatenbanken legen die Tabelle beim Start an und füllen sie über den Recorder bzw. `kcc uph-rebuild`. |
| `OperatingHours` | Hauptnutzungszeit, `{ "Enabled": true, "Start": "06:00", "End": "15:15" }`. Nenner der Langzeit-Durchschnitte auf `/rbg` und im zweiten `/verlauf`-Chart (Ø Auslastung, Ø Leistung, Leerlauf, Anteil) ist dann die Betriebszeit statt des Kalenderzeitraums. Ein Kalendertag ohne Bewegung zählt gar nicht; wird nach `End` noch gefahren, verlängert der Tag bis zur letzten Bewegung. `Enabled: false` ⇒ voller Kalenderzeitraum. Die per-Raster-Kurven bleiben unberührt. |
| `ContourCheckpoints` | Konturkontrollen für `/kontur`, je Eintrag `{ "ResourcePoint": "LB21", "MessageCode": "ENDTSP", "Label": "…" }`. Ausgewertet wird das `Status`-Feld (`Kxyz`) dieser Telegramme. Leere Liste ⇒ eingebaute Vorgabe (LB21 ENDTSP, DA91/AA41/NA41 TSPREG). |
| `ContourFlags` | Bedeutung der Fehlerbits im Konturergebnis `Kxyz`, je Eintrag `{ "Nibble": 0, "Bit": 2, "Label": "Profil links" }` — `Nibble` 0 = `x`, 1 = `y`, 2 = `z`; `Bit` 0…3. Leere Liste ⇒ eingebaute Tabelle laut Doku „Konturenfehler (Kxyz)". `Status = "…."` (leer) = kein Konturfehler. |
| `ContourWindowMinutes` | Zeitfenster der Konturauswertung ohne `minutes`-Parameter (Standard: `480` = 8 h) |
| `RbgMaxCyclesPerHour` / `RbgMaxStoresPerHour` / `RbgMaxRetrievalsPerHour` | Auslegungsleistung eines RBG in Doppelspielen bzw. reinen Ein-/Auslagerungen pro Stunde (Standard `30` / `48` / `48` = HRL RBG 1–5, also 120 s bzw. 75 s je Spiel). Leistungsgrad = Zeitbedarf der gefahrenen Spiele ÷ Fenster; ein Einzelspiel kostet damit 62,5 % eines Doppelspiels, nicht 50 %. Begriffe nach FEM 9.851: Einzelspiel = reine Ein- oder Auslagerung, Doppelspiel = beides in einer Fahrt (zwei Transporte). Je Gerät über `ResourcePoints[]` überschreibbar (RBG 11 Versandpuffer: 56 / 105 / 105). |
| `CountTelegramType` | Telegrammtyp, der bei allen Zählungen gewertet wird (Standard `DM`). Jedes Ereignis kommt als Paar `DM` (Data Message) + `AK` (Acknowledge), dazu `LM` als leere Lebensmeldung. Bei Fördertechnik-Verbindungen ist der `AK` leer, bei RBG-Verbindungen inhaltsgleich zum `DM` — ohne die Einschränkung würde doppelt gezählt. `AK` wäre gleichwertig, leer = alle zählen. |
| `RbgPickupOrderCodes` / `RbgPickupDoneCodes` / `RbgDepotOrderCodes` / `RbgDepotDoneCodes` | MessageCodes eines RBG-Transports. Leer ⇒ `["PUPORD"]` / `["ENDPUP"]` / `["DEPORD"]` / `["ENDDEP"]`. *Pickup Order* = aufs RBG nehmen, *Depot Order* = vom RBG abgeben; jeder Transport besteht aus beidem — es sind **keine** Ein-/Auslageraufträge. `ENDDEP` schließt einen Transport ab und wird gezählt, `PUPORD` markiert den Beginn für die Dauermessung. |
| `ConveyorOrderCodes` / `ConveyorEndCodes` / `ConveyorFreeCodes` | MessageCodes der Fördertechnik-Belegung. Leer ⇒ `["TSPORD"]` / `["ENDTSP"]` / `["RPFREE"]`. Ablauf laut Kardex-Doku „Transportverwaltung Paletten-Fördertechnik": `ENDTSP` = Ankunft (ab hier belegt), `TSPORD` = Auftrag zum Weitertransport (weiterhin belegt), `RPFREE` = Verlassen (ab hier frei). Belegt = `ENDTSP → RPFREE`, leer = `RPFREE →` nächste Ankunft. Teilzeiten: `ENDTSP→TSPORD` = Wartezeit auf die MFR-Entscheidung, `TSPORD→RPFREE` = Abtransport (langer Wert = Rückstau dahinter). Ausgewertet über einen Zustandsautomaten auf dem Zeitstrahl, nicht über Ereignispaare — Belegung gehört zum Platz, nicht zur Ladeeinheit. Ein Punkt ohne `RPFREE` gilt durchgehend als belegt; der Zähler `Ankunft/Auftrag/Frei` macht das sichtbar. |
| `RbgRackLocationPattern` | Muster eines Regalplatzes in `Source`/`Destination` (Standard `^[0-9]+$`). Bestimmt die Richtung: Transport endet auf einem Regalplatz (rein numerisch, z. B. `010361211`) = Einlagerung, an einer Station (`MA62`) = Auslagerung. Daraus Doppelspiele = `min(Ein, Aus)`, Einzelspiele = `\|Ein − Aus\|`. |
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
| `GET /auslastung` | Auslastung der Ressourcenpunkte, nach `Group` gebündelt. Je Kachel zwei Tachos: Zeitseite und Mengenseite. Punkte mit `Connection` (RBG): **Auslastung** (Σ Transportdauern ÷ Fenster) und **Leistung** (Spiele/h gegen `RbgMaxCyclesPerHour`); gerechnet wird in Doppel-/Einzelspielen, die `TSPORD`-Zähler dienen nur der Ziel-Identifikation. Punkte ohne `Connection` (Fördertechnik): **Belegung** (`ENDTSP`→`RPFREE` ÷ Fenster) und **Leistung** (% vom UPH-Richtwert), dazu Ø Verweildauer, Ø bis Auftrag, Ø Abtransport, Ø Leerzeit. Jede Kennzahl hat einen Tooltip; die Tachos blenden zwischen zwei Abrufen über. „Anordnen" macht Kacheln ziehbar (Kopf) und skalierbar (Griff unten rechts), gespeichert je Browser in `localStorage`. Im Querformat volle Breite als Wandraster, im Hochformat eine Lesespalte |
| `GET /api/utilization?minutes=60&target=200&bucket=5&rate=1&rateFt=5&step=1` | Dieselbe Auswertung als JSON. `rate` = Trailing-Fenster der RBG-Tachos, `rateFt` = das der Fördertechnik-Tachos; `bucket` = Breite des gleitenden Verlaufsfensters; `step` = Abtastschritt des Verlaufs |
| `GET /verlauf` | UPH-Historie als gestapelte Fläche, je Endziel oder je Ressourcenpunkt (Umschalter „Stapeln nach"), plus Mengenverhältnis und Tabelle. ECharts: ziehen oder Mausrad zoomt, der Balken unten verschiebt den Ausschnitt, Doppelklick setzt zurück. Bereich 8 h = gleitender Kurzzeit-Verlauf (5-min-Fenster direkt aus den Rohtelegrammen); längere Bereiche aus `uph_samples` mit eigener Aufbewahrung `UphHistoryRetentionDays` — weiter zurück reicht `/verlauf` auch nach `backfill` nicht. Bei Auswahl eines Ressourcenpunkts kommt darunter ein zweiter Chart mit Belegung & Leistung (RBG-Punkte aus `/api/rbg-history`, sonst `/api/point-history`) |
| `GET /api/uph-history?hours=168&bucket=15&groupBy=destination&rp=MA72` | Historie als JSON: Buckets je Reihe (Menge + UPH), Summen mit Ø UPH und Anteil. `groupBy` = `destination` (Vorgabe) oder `resourcePoint`; `hours` bis 672 (4 W) **oder** absolutes Fenster `from=…&to=…` (ISO, UTC); `bucket` = Stützpunktabstand; `rolling=<min>` schaltet auf ein gleitendes Fenster aus den Rohtelegrammen um (`bucket` wird dann der Abtastschritt); `rp` grenzt zusätzlich auf einen Ressourcenpunkt ein |
| `GET /kontur` | Auswertung der Konturkontrollen: welche Konturfehler an welchem Kontrollpunkt auflaufen. Zerlegt das `Status`-Feld (`Kxyz`) der Telegramme aus `ContourCheckpoints` in benannte Fehlerbits (`ContourFlags`). KPIs, Balken je Fehlerart, Kreuztabelle Kontrollpunkt × Fehlerart, sowie je Kontrollpunkt die letzten 10 Fehler mit Zeit, LE-/ID-Nummer und aufgelösten Fehlern |
| `GET /api/kontur?minutes=480` | Dieselbe Auswertung als JSON |
| `GET /rbg` | Langzeitvergleich der RBG. Spreizung `(meiste − wenigste Spiele) ÷ meiste`, Anteil je Gerät (Balken in Ein-/Auslagerung geteilt), Mehrlinien-Chart der Spiele/h bzw. Prozentwerte, je Gerät ein Mini-Chart mit Auslastung/Leistung/Leerlauf, Kennzahlentabelle. Zeiträume 24 h bis 1 Jahr aus `rbg_samples` — reicht weiter zurück als die Rohtelegramme |
| `GET /api/rbg-history?hours=168&bucket=60` | Derselbe Vergleich als JSON. `hours` bis 8784 (1 Jahr) **oder** absolutes Fenster `from=…&to=…` (ISO, UTC); `bucket` = Stützpunktabstand in Minuten |
| `GET /api/point-history?hours=168&bucket=30&rp=EA21` | Langzeitverlauf je Ressourcenpunkt aus `point_samples`: Belegungsgrad (belegte Zeit ÷ Raster) und Leistungsgrad (Aufträge/h ÷ Richtwert). Auch für Fördertechnikpunkte ohne RBG. `hours` **oder** `from=…&to=…`; `bucket` = Stützpunktabstand; `rp` grenzt auf einen Punkt ein |
| `GET /wand` | Wandansicht derselben Daten (`/api/utilization`). Im Querformat je `Group` eine formatfüllende Spalte ohne Seiten-Scroll, im Hochformat gestapelt. Kompakte Kacheln mit Tacho, %, Verlauf; bei RBG zusätzlich Auslastung/Leistung/Spiele/Leerlauf. Vollbild-Schaltfläche |
| `GET /health` | Status, Version, DB-Pfad, Gesamtzahl, `lastSeenId`, jüngster Telegramm-Zeitstempel, Sekunden seit letztem Schreibvorgang, Server-Uhr |
| `GET /api/version` | `{ "version": "0.3.0" }` — der Release-Tag, lokale Builds `dev`. Speist die Versionsanzeige rechts in der Navigationsleiste |

Ohne `minutes` gilt `WindowMinutes` (Standard 4 h); der Parameter wird auf 1…1440 begrenzt,
`limit` auf 1…20000. `/api/kpis` liefert Anzahl, Telegramme/Minute, Fehler (`ErrorCode` ≠ 0),
Sekunden seit dem letzten Schreibvorgang, aktive Verbindungen und die Verteilung nach Richtung,
Verbindung und `MessageCode`.

**Zeitfenster:** Rechter Rand ist der Zeitstempel des jüngsten Telegramms in der DB, nicht die Uhr
des API-Hosts — „letzte N Minuten" bleibt damit auch bei abweichender Anlagen-Zeitzone richtig.
`GET /health` zeigt beide Zeiten.

**Zeitzone:** Die Anlage liefert UTC, die DB legt zeitzonenfrei ab, die API gibt UTC-ISO (`…Z`)
aus; die Dashboards rechnen daraus die lokale Zeit des Betrachters.

## Wie es funktioniert

Die Anlage bietet keinen Push für Protokolldaten; auch die Weboberfläche pollt. `Id` wird monoton
vergeben, also fragt der Recorder wiederholt „alles mit `Id > zuletzt gesehen`", aufsteigend
sortiert — lückenlos und wiederholbar. Der Stand liegt in der DB, ein Neustart setzt dort an.

**Reconnect:** Nach einem Verbindungsabbruch verbindet der Recorder neu, Wartezeit
`ReconnectDelaySeconds` verdoppelt bis `ReconnectMaxDelaySeconds`. Anschließend holt dieselbe
`Id`-Logik die Auszeit nach. Beenden mit Strg+C.

**Anmeldung** wie im Browser-Client: `Subscribe` liefert `SessionId` und den öffentlichen
RSA-Schlüssel im .NET-XML-Format; das Passwort geht mit RSA-2048/OAEP-SHA1 verschlüsselt als
Byte-Array (nicht Base64) an `LogonUser`. Eine Byte-Umkehrung ist nicht nötig — abgesichert durch
einen Test gegen einen Ciphertext der originalen Client-Bibliothek
(`tests/Kcc.Recorder.Tests/Fixtures/README.md`).

## Bauen

```
dotnet test
dotnet publish src/Kcc.Recorder/Kcc.Recorder.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Die EXE bündelt .NET und ASP.NET Core (für Kestrel), self-contained, ~90–100 MB.
`RollForward=Major` erlaubt `dotnet test`/`run` auch neuere Runtimes; der Publish bleibt bei net8.
Ein Tag `vX.Y.Z` startet den Release-Workflow: EXE bauen, ZIP (`kcc.exe`, `appsettings.json`,
`README.md`, `dashboard.html`, `kontur.html`, `wand.html`, Ordner `wwwroot/`) samt Prüfsumme an
ein GitHub-Release hängen. Die Version aus dem Tag geht als `-p:Version=` in den Build und
erscheint unter `/api/version` sowie in der Navigationsleiste.

`/auslastung`, `/verlauf` und `/rbg` liegen als lose Dateien unter `wwwroot/` (`*.html` + `css/`,
`js/`, `vendor/`), ausgeliefert über `UseStaticFiles`. Der Ordner gehört neben die EXE — für diese
Seiten reicht die EXE allein nicht. `/verlauf` und `/rbg` zeichnen mit ECharts (Apache-2.0,
vendort unter `wwwroot/vendor/echarts.min.js`, ~1 MB); `js/nav.js` baut die Navigationsleiste,
`js/glossary.js` trägt die Tooltip-Texte. `/`, `/kontur` und `/wand` sind weiterhin eingebettete
C#-Konstanten mit serverseitiger Injektion (`DashboardNav`, `RbgGlossary`);
`kcc dump-dashboards [--out verz]` schreibt sie heraus und kopiert `wwwroot/` dazu.
Als lose Datei geöffnet fragen die Seiten `http://localhost:8082` ab, überschreibbar mit
`?api=http://host:port`; über die API ausgeliefert zählt deren Herkunft.

## Hinweis

Es handelt sich um eine nachgebaute, nicht offiziell dokumentierte Schnittstelle. Zugriff nur mit
entsprechender Freigabe und mit einem Account, der Leserechte auf die Rolle `CommonPlcProtocol` hat.
