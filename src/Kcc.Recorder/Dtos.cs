using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kcc.Recorder;

/// <summary>Richtung eines SPS-Telegramms (MCC.VISU.Services.VisuServiceInterfaces.Enum.TelegramDirection).</summary>
public enum TelegramDirection
{
    Unknown = 0,
    ToPlc = 1,
    FromPlc = 2,
}

/// <summary>
/// Ein Datensatz aus MCC.VISU.Services.VisuServiceInterfaces.DTO.PlcProtocolExtendedDTO.
/// Feldnamen und Reihenfolge entsprechen der Typbeschreibung des Servers.
/// </summary>
public sealed record Telegram(
    long Id,
    DateTime DateTime,
    TelegramDirection TelegramDirection,
    string? ConnectionName,
    string? Data,
    string? Format)
{
    public const string DtoType =
        "MCC.VISU.Services.VisuServiceInterfaces.DTO.PlcProtocolExtendedDTO, MCC.VISU.Services.VisuServiceInterfaces";

    public static Telegram FromJson(JsonElement e) => new(
        Id: e.GetProperty("Id").GetInt64(),
        DateTime: e.GetProperty("DateTime").GetDateTime(),
        TelegramDirection: (TelegramDirection)GetInt(e, "TelegramDirection"),
        ConnectionName: GetString(e, "ConnectionName"),
        Data: GetString(e, "Data"),
        Format: GetString(e, "Format"));

    static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static int GetInt(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
}

/// <summary>
/// Wird von Subscribe geliefert und muss danach in jeden weiteren Aufruf eingebettet werden.
/// Der Server erwartet exakt diese drei Felder.
/// </summary>
public sealed class SessionInformation
{
    public string SessionId { get; set; } = "";
    public string? ClientMachineName { get; set; }
    public string? ClientMachineIp { get; set; }
}

/// <summary>Ergebnis eines Logon-Versuchs (MCC.Common.ServiceInterfaces.Enum.SecurityAnswerCode).</summary>
public enum SecurityAnswerCode
{
    Undefined = 0,
    Allowed = 1,
}

/// <summary>
/// Serialisiert byte[] als JSON-Array einzelner Zahlen statt als Base64.
/// Der Browser-Client sendet das verschlüsselte Passwort so (JS-Number-Array durch JSON.stringify),
/// und der Server erwartet genau diese Form.
/// </summary>
public sealed class ByteArrayAsNumbersConverter : JsonConverter<byte[]>
{
    public override byte[] Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        var bytes = new List<byte>();
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Erwartet wurde ein Byte-Array.");
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            bytes.Add(reader.GetByte());
        return bytes.ToArray();
    }

    public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var b in value)
            writer.WriteNumberValue(b);
        writer.WriteEndArray();
    }
}
