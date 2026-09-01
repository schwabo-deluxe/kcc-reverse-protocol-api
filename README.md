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

Werte werden in dieser Reihenfolge übernommen (später schlägt früher):
`kcc.json` → Umgebungsvariablen (`KCC_URL`, `KCC_USER`, `KCC_PASSWORD`, `KCC_DATABASE`) →
Kommandozeile. Fehlt das Passwort, wird es verdeckt abgefragt.

`kcc.example.json` als `kcc.json` neben die EXE legen und anpassen. **`kcc.json` gehört nicht ins
Repository** — sie steht bereits in `.gitignore`.

### Filter

Standardmäßig wird ein Telegramm aufgezeichnet, wenn sein `Data`-Feld nicht leer ist und nicht nur
aus Nullen besteht (typische Leerlauf-Frames). Anpassbar über den `Filter`-Abschnitt:

| Feld | Bedeutung |
|---|---|
| `MinDataLength` | Mindestlänge von `Data` nach Trim |
| `IgnoreAllZeroData` | Verwirft Frames aus lauter Nullen |
| `ConnectionWhitelist` / `ConnectionBlacklist` | Verbindungen ein-/ausschliessen |
| `Directions` | `ToPlc`, `FromPlc`, `Unknown` |
| `DataMatchRegex` / `DataIgnoreRegex` | Feinsteuerung über reguläre Ausdrücke |
| `FilterEmptyDataOnServer` | Lässt schon den Server leere Frames aussortieren (spart Bandbreite) |

Sobald die realen Telegramme vorliegen, lässt sich das ohne Codeänderung nachschärfen.

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
