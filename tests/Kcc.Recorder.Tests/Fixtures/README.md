# Test-Fixtures

Diese Dateien sichern die Login-Verschlüsselung gegen die **echte** Client-Implementierung ab,
ohne dass dafür eine Anlage erreichbar sein muss.

| Datei | Inhalt |
|---|---|
| `throwaway-test-key.pem` | **Wegwerf-RSA-Schlüsselpaar, nur für Tests.** Wurde ausschliesslich lokal mit `openssl genrsa` erzeugt, ist nirgends im Einsatz und schützt nichts. |
| `key.xml` | Der öffentliche Teil desselben Schlüssels im .NET-XML-Format — genau die Form, in der der Server seinen `RsaXmlKey` liefert. |
| `cipher.bin` | Der String `GeheimesPasswort123`, verschlüsselt von der **originalen** Kardex-Client-Krypto-Bibliothek (`libs/netencryption/System.Security.Cryptography.RSA.min.js`, in Node ausgeführt) mit `Encrypt(bytes, true)`. |

`cipher.bin` ist der eigentliche Wert hier: lässt es sich mit `RSAEncryptionPadding.OaepSHA1`
entschlüsseln, erzeugt unsere C#-Seite bitgleich das, was der Browser-Client erzeugt — inklusive
der Frage nach der Byte-Reihenfolge, die damit beantwortet ist (kein Reversal).
