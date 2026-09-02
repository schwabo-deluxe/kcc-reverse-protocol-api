# kcc — Telegramm-Recorder für Kardex MCC/KCC

Schneidet die SPS-Telegramme (`PlcProtocolExtendedDTO`) einer Kardex MCC/KCC-Anlage über deren
WebSocket-API mit und legt sie in einer lokalen SQLite-Datenbank ab. Eine Filterfunktion sorgt
dafür, dass nur Telegramme mit tatsächlichem Dateninhalt aufgezeichnet werden.

Das Werkzeug ist eine **self-contained Single-File-EXE für Windows** — kein installiertes .NET
nötig, einfach `kcc.exe` kopieren und starten.

## Schnellstart

```
kcc login-test --url wss://10.20.220.33/ws --user MEINUSER --insecure
kcc record
```

Beim ersten `record` setzt der Recorder am aktuellen Ende der Protokolltabelle an, damit nicht
versehentlich Millionen historischer Zeilen gezogen werden. Ältere Telegramme holt `kcc backfill`.

## Kommandos

| Kommando | Zweck |
|---|---|
| `kcc login-test` | Verbindung, Zertifikat und Anmeldung prüfen |
| `kcc record` | Telegramme fortlaufend in die Datenbank schreiben |
| `kcc query --take 100 [--json]` | Einmalabfrage auf stdout — zum Abgleich mit dem Web-Grid |
| `kcc backfill --from-id N [--to-id M]` | Ältere Telegramme nachladen |
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
| `CsvPath` | Wenn gesetzt, werden aufgezeichnete Telegramme bei `record`/`backfill` **zusätzlich** fortlaufend an diese CSV angehängt (Standard: `kcc-telegrams.csv`). `null` schaltet die CSV ab. |

Die CSV wird im Anhänge-Modus geführt: ein Neustart schreibt weiter, die Kopfzeile nur einmal.
Gleiches Semikolon-Format wie `kcc export` (UTF-8 mit BOM, für Excel im deutschen Gebietsschema).

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

Ein Tag `vX.Y.Z` löst den Release-Workflow aus: er baut die EXE und hängt sie samt Prüfsumme an
ein GitHub-Release.

## Hinweis

Es handelt sich um eine nachgebaute, nicht offiziell dokumentierte Schnittstelle. Zugriff nur mit
entsprechender Freigabe und mit einem Account, der Leserechte auf die Rolle `CommonPlcProtocol` hat.
