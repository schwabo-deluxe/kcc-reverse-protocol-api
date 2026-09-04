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

`kcc` ohne Argumente ist der Normalbetrieb: Aufzeichnung und Lese-API samt Dashboard laufen
gemeinsam in einem Prozess, gesteuert über `appsettings.json` (`Record`, `Serve`, `ApiUrl`).

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
| `kcc uph-rebuild` | UPH-Historie (`/verlauf`) aus den vorhandenen Telegrammen neu aufbauen — nach einem separaten `backfill` oder Änderung der `UphHistory*`-Optionen |
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
| `WindowMinutes` | Zeitfenster der Dashboards und der API ohne `minutes`-Parameter (Standard: `240`, also 4 Stunden) |
| `StartupBackfillMinutes` | Beim ersten Start einmalig nachgeladene Zeitspanne, damit das Dashboard sofort Historie zeigt (Standard: `240`). `0` schaltet das ab. |
| `UtilizationTargetUph` | Richtwert in Einheiten/Stunde, auf den sich die Auslastung in Prozent bezieht |
| `UtilizationRateMinutes` | Trailing-Fenster (Standard `5`), aus dem UPH und Prozent hochgerechnet werden. Klein = reagiert sofort auf kurze Stöße; groß = geglättet. `Count` und der Verlauf bleiben über das ganze Fenster. |
| `ResourcePoints` | Liste der ausgewerteten Ressourcenpunkte, je Eintrag `{ "Name": "MA72", "Group": "Auslagerung RBG", "Label": "RBG A" }`. `Group`/`Label` optional. Das Dashboard bündelt die Kacheln und die Tabelle nach `Group` und zeigt je Gruppe eine Summe. Leere Liste ⇒ eingebaute Vorgabe. |
| `DestinationLabels` | Klartext für Endziele, z. B. `{ "GA51": "Kommissionierung" }`. Das Endziel ist das führende Token des letzten 33er-Blocks im `Data`-Feld (4 oder 5 Zeichen, z. B. `GA51` oder `DLL13`); je Kachel zeigt eine kleine Tabelle den %-Anteil je Ziel. Gemappte Ziele erscheinen als `GA51 (Kommissionierung)`, unbekannte roh. Ein Schlüssel mit `*` am Ende ist ein Präfixmuster: `{ "DLL*": "Auslagerung DLL" }` fasst alle `DLL…` zu einem Ziel `DLL*` zusammen (exakte Treffer schlagen Muster, längstes Präfix gewinnt). |
| `UphHistoryIntervalMinutes` | Rasterweite der UPH-Historie in Minuten (Standard `15`) — wie fein `/verlauf` auflöst. |
| `UphHistoryRetentionDays` | Aufbewahrung der UPH-Historie in Tagen (Standard `28` = 4 Wochen), getrennt von `RetentionDays` der Rohtelegramme. `0`/negativ = unbegrenzt. `/verlauf` zeigt nur diesen Zeitraum — für weiter zurück den Wert erhöhen und `kcc uph-rebuild` laufen lassen. Der Recorder baut die Historie bei jedem Start und nach `backfill` neu auf. |
| `RetentionDays` | Aufbewahrungsdauer in Tagen (Standard: `365`). Normalbetrieb/`backfill` löschen beim Start und danach täglich Telegramme mit älterem `DateTime`; `kcc prune` tut es einmalig. `0`/negativ = unbegrenzt. |

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

Der Normalbetrieb (`kcc` ohne Argumente) stellt neben der Aufzeichnung eine kleine **Lese-API**
(auf `HttpListener`, keine zusätzliche Abhängigkeit) samt eingebettetem Dashboard bereit.
Adresse und Betriebsart stehen in `appsettings.json`:

```json
"ApiUrl": "http://localhost:8080/",
"Record": true,
"Serve": true
```

| Endpunkt | Zweck |
|---|---|
| `GET /` | Dashboard (eine HTML-Datei, pollt `/api/kpis` jede Minute) |
| `GET /api/kpis?minutes=240` | Kennzahlen über das Zeitfenster |
| `GET /api/telegrams?minutes=240&limit=2000` | Telegramme des Zeitfensters (aufsteigend) |
| `GET /api/fields?minutes=5&limit=20` | Diagnose: die letzten Telegramme Feld für Feld nach `DataFormat` zerlegt — zeigt, welches Feld den Ressourcenpunkt trägt |
| `GET /auslastung` | Auslastung der Ressourcenpunkte aus `TSPORD`-Telegrammen (UPH, % vom Richtwert, Verlauf je Punkt), gebündelt nach `Group` |
| `GET /api/utilization?minutes=240&target=200&bucket=5&rate=5` | Dieselbe Auswertung als JSON (`rate` = UPH-Fenster in Minuten) |
| `GET /verlauf` | UPH-Historie als gestapelte Fläche, wahlweise **je Endziel oder je Ressourcenpunkt** (Umschalter „Stapeln nach"), plus Mengenverhältnis und Tabelle. Zeitbereich per Maus aufziehen zoomt hinein (Doppelklick / „Zoom zurück" setzt zurück). Speist sich aus einer verdichteten Rollup-Tabelle mit **eigener Aufbewahrung** `UphHistoryRetentionDays` (Standard 4 Wochen), die der Recorder laufend aus den Rohtelegrammen bildet — weiter zurück als dieser Zeitraum reicht `/verlauf` nicht, auch nach `backfill` nicht |
| `GET /api/uph-history?hours=168&bucket=15&groupBy=destination&rp=MA72` | Historie als JSON: Buckets je Reihe (Menge + UPH), Summen mit Ø UPH und Anteil. `groupBy` = `destination` (Vorgabe) oder `resourcePoint`; `hours` bis 672 (4 W) **oder** absolutes Fenster `from=…&to=…` (ISO, Anlagenzeit); `bucket` frei wählbar; `rp` grenzt zusätzlich auf einen Ressourcenpunkt ein |
| `GET /health` | Status, DB-Pfad, Gesamtzahl, `lastSeenId`, jüngster Telegramm-Zeitstempel, Sekunden seit letztem Schreibvorgang, Server-Uhr |

Ohne `minutes` gilt `WindowMinutes` (Standard 4 Stunden); der Parameter wird auf 1…1440 begrenzt, `limit` auf 1…20000. Die KPIs (`/api/kpis`): Anzahl,
Telegramme/Minute, Fehler (`ErrorCode`-Feld ≠ 0), Sekunden seit dem letzten Schreibvorgang, aktive
Verbindungen sowie Verteilung nach Richtung, Verbindung und `MessageCode`.

**Zeitfenster:** Der rechte Rand ist der Zeitstempel des **jüngsten Telegramms in der DB**, nicht
die Uhr des API-Hosts. So bleibt „letzte N Minuten" richtig, auch wenn die Anlage ihre
Zeitstempel in einer anderen Zeitzone (z. B. UTC) schickt als der Rechner, auf dem `kcc` läuft.
`GET /health` zeigt beide Zeiten, um einen solchen Versatz sichtbar zu machen.

`http://localhost:PORT/` läuft unter Windows ohne Sonderrechte. Für `http://+:PORT/` oder einen
festen Hostnamen ist einmalig `netsh http add urlacl url=http://+:PORT/ user=<DOMAIN\User>` nötig.

## Wie es funktioniert

Die Anlage bietet **keinen Push** für Protokolldaten — auch die Weboberfläche pollt. Da `Id`
monoton vergeben wird, fragt der Recorder wiederholt „alles mit `Id > zuletzt gesehen`", aufsteigend
sortiert. Das ist lückenlos und wiederholbar; ein Neustart setzt exakt dort wieder an, weil der
Stand in der Datenbank liegt.

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

Ein Tag `vX.Y.Z` löst den Release-Workflow aus: er baut die EXE und hängt das ZIP
(`kcc.exe`, `appsettings.json`, `README.md`, `dashboard.html`, `auslastung.html`, `verlauf.html`)
samt Prüfsumme an ein GitHub-Release. Die HTML-Dateien sind dieselben Dashboards, die die API
unter `/`, `/auslastung` bzw. `/verlauf` ausliefert — `kcc dump-dashboards [--out verz]` schreibt
sie jederzeit heraus.
Als lose Datei geöffnet fragen sie fest `http://localhost:8080` ab; mit `?api=http://host:port`
lässt sich ein anderer Endpunkt vorgeben. Über die API selbst ausgeliefert zählt deren Herkunft.

## Hinweis

Es handelt sich um eine nachgebaute, nicht offiziell dokumentierte Schnittstelle. Zugriff nur mit
entsprechender Freigabe und mit einem Account, der Leserechte auf die Rolle `CommonPlcProtocol` hat.
