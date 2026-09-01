using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Kcc.Recorder;

/// <summary>Anmeldung wurde vom Server abgelehnt.</summary>
public sealed class KccLogonException(string message) : Exception(message);

/// <summary>
/// Meldet sich an und hält die SessionInformation, die jeder weitere Aufruf mitführen muss.
/// Bildet den Ablauf aus dist/commonClient/MccCommonClient.js nach: Subscribe → RSA → LogonUser.
/// </summary>
public sealed class KccSession
{
    const string CommonService = "CommonService";

    readonly KccConnection _connection;
    readonly Action<string> _log;

    public KccSession(KccConnection connection, Action<string> log)
    {
        _connection = connection;
        _log = log;
    }

    public SessionInformation? SessionInformation { get; private set; }

    public async Task LogonAsync(string userName, string password, CancellationToken ct)
    {
        var subscribe = await _connection.CallAsync(
            CommonService,
            "Subscribe",
            new { dateTimeUtcOffset = GetUtcOffsetMinutes() },
            ct);

        var rsaXmlKey = subscribe.TryGetProperty("RsaXmlKey", out var key) ? key.GetString() : null;
        if (string.IsNullOrWhiteSpace(rsaXmlKey))
            throw new KccLogonException("Subscribe lieferte keinen RsaXmlKey.");

        var session = new SessionInformation
        {
            SessionId = subscribe.GetProperty("SessionId").GetString()
                        ?? throw new KccLogonException("Subscribe lieferte keine SessionId."),
            ClientMachineName = GetString(subscribe, "ClientMachineName"),
            ClientMachineIp = GetString(subscribe, "ClientMachineIp"),
        };
        _log($"Subscribe ok — Client {session.ClientMachineName} / {session.ClientMachineIp}");

        var encrypted = EncryptPassword(rsaXmlKey!, password);

        var response = await _connection.CallAsync(
            CommonService,
            "LogonUser",
            new
            {
                sessionId = session.SessionId,
                userName,
                password = encrypted,
            },
            ct);

        EnsureLogonSucceeded(response, userName);

        SessionInformation = session;
        _log($"Angemeldet als {userName}.");
    }

    public async Task LogoffAsync(CancellationToken ct)
    {
        if (SessionInformation is null)
            return;
        try
        {
            await _connection.CallAsync(CommonService, "LogoffUser",
                new { sessionId = SessionInformation.SessionId }, ct);
        }
        catch (Exception ex)
        {
            _log($"Abmelden fehlgeschlagen (ohne Folgen): {ex.Message}");
        }
        SessionInformation = null;
    }

    static void EnsureLogonSucceeded(JsonElement response, string userName)
    {
        // Der Server antwortet mit einem SecurityAnswerCode; alles ausser "Allowed" ist eine Ablehnung.
        if (response.ValueKind == JsonValueKind.Undefined || response.ValueKind == JsonValueKind.Null)
            throw new KccLogonException($"Anmeldung als '{userName}' lieferte keine Antwort.");

        if (!response.TryGetProperty("SecurityAnswerCode", out var code))
            return; // Kein Code vorhanden und kein Error-Feld gesetzt: als Erfolg werten.

        var value = code.ValueKind == JsonValueKind.Number
            ? (SecurityAnswerCode)code.GetInt32()
            : Enum.TryParse<SecurityAnswerCode>(code.GetString(), out var parsed)
                ? parsed
                : SecurityAnswerCode.Undefined;

        if (value != SecurityAnswerCode.Allowed)
            throw new KccLogonException(
                $"Anmeldung als '{userName}' abgelehnt (SecurityAnswerCode: {code}). " +
                "Benutzername/Passwort prüfen und ob der Account die Rolle CommonPlcProtocol besitzt.");
    }

    /// <summary>
    /// Verschlüsselt das Passwort so, wie es der Browser-Client tut:
    /// RSA-2048 mit OAEP-SHA1 über den öffentlichen Schlüssel aus dem .NET-XML.
    ///
    /// Die Client-Bibliothek dreht intern mehrfach Byte-Reihenfolgen um; diese Umkehrungen heben
    /// sich auf. Verifiziert wurde das gegen einen von der Originalbibliothek erzeugten Ciphertext
    /// (siehe tests/Kcc.Recorder.Tests/Fixtures) — es ist also kein Reversal nötig.
    /// </summary>
    internal static byte[] EncryptPassword(string rsaXmlKey, string password)
    {
        using var rsa = RSA.Create();
        rsa.ImportParameters(ParseRsaXmlPublicKey(rsaXmlKey));
        return rsa.Encrypt(Encoding.UTF8.GetBytes(password), RSAEncryptionPadding.OaepSHA1);
    }

    /// <summary>
    /// Liest Modulus und Exponent aus dem .NET-XML-Format.
    /// Bewusst von Hand statt über RSA.FromXmlString, dessen Verhalten plattformabhängig ist.
    /// </summary>
    internal static RSAParameters ParseRsaXmlPublicKey(string xml)
    {
        XElement root;
        try
        {
            root = XElement.Parse(xml);
        }
        catch (Exception ex)
        {
            throw new KccLogonException($"RsaXmlKey ist kein gültiges XML: {ex.Message}");
        }

        var modulus = root.Element("Modulus")?.Value;
        var exponent = root.Element("Exponent")?.Value;
        if (string.IsNullOrWhiteSpace(modulus) || string.IsNullOrWhiteSpace(exponent))
            throw new KccLogonException("RsaXmlKey enthält kein Modulus/Exponent-Paar.");

        return new RSAParameters
        {
            Modulus = Convert.FromBase64String(modulus),
            Exponent = Convert.FromBase64String(exponent),
        };
    }

    /// <summary>Entspricht getUTCOffset() des Browser-Clients.</summary>
    static int GetUtcOffsetMinutes() => (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes;

    static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
