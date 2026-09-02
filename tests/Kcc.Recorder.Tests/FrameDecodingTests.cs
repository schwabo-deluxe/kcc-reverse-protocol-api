using System.IO.Compression;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Kcc.Recorder;
using Xunit;

namespace Kcc.Recorder.Tests;

public class FrameDecodingTests
{
    static byte[] Gzip(string text)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(Encoding.UTF8.GetBytes(text));
        return output.ToArray();
    }

    [Fact]
    public void Textrahmen_wird_direkt_gelesen()
    {
        var payload = Encoding.UTF8.GetBytes("""{"Id":"x"}""");

        var decoded = KccConnection.TryDecodeFrame(payload, WebSocketMessageType.Text, out var json);

        Assert.True(decoded);
        Assert.Equal("""{"Id":"x"}""", json);
    }

    [Fact]
    public void Textrahmen_mit_UTF8_BOM_wird_ohne_BOM_geliefert()
    {
        // Manche Gegenstellen stellen Text-Frames eine UTF-8-BOM voran; JsonDocument.Parse
        // scheitert daran sonst mit "'0xEF' is an invalid start of a value".
        var payload = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes("""{"Id":"x"}"""))
            .ToArray();

        var decoded = KccConnection.TryDecodeFrame(payload, WebSocketMessageType.Text, out var json);

        Assert.True(decoded);
        Assert.Equal("""{"Id":"x"}""", json);
        JsonDocument.Parse(json).Dispose();
    }

    [Fact]
    public void Binaerrahmen_mit_UTF8_BOM_wird_ohne_BOM_geliefert()
    {
        var expected = """{"Id":"abc"}""";
        var gzipped = Gzip("\uFEFF" + expected);

        var decoded = KccConnection.TryDecodeFrame(gzipped, WebSocketMessageType.Binary, out var json);

        Assert.True(decoded);
        Assert.Equal(expected, json);
    }

    [Fact]
    public void Binaerrahmen_wird_entpackt()
    {
        const string expected = """{"Id":"abc","Response":[]}""";

        var decoded = KccConnection.TryDecodeFrame(Gzip(expected), WebSocketMessageType.Binary, out var json);

        Assert.True(decoded);
        Assert.Equal(expected, json);
    }

    [Fact]
    public void Proprietaerer_Binaerrahmen_wird_verworfen()
    {
        // Rahmen mit 0xFF tragen Visu-Tagwerte im Eigenformat; der Recorder ignoriert sie.
        var payload = new byte[] { 0xFF, 0x01, 0x02, 0x03 };

        Assert.False(KccConnection.TryDecodeFrame(payload, WebSocketMessageType.Binary, out _));
    }

    [Fact]
    public void Leerer_Rahmen_wird_verworfen()
    {
        Assert.False(KccConnection.TryDecodeFrame([], WebSocketMessageType.Binary, out _));
    }

    [Fact]
    public void Redact_entfernt_Passwort_und_SessionId()
    {
        const string json = """{"password":[1,2,3],"sessionId":"abc-def","Take":10}""";

        var redacted = KccConnection.Redact(json);

        Assert.DoesNotContain("1,2,3", redacted);
        Assert.DoesNotContain("abc-def", redacted);
        Assert.Contains("\"Take\":10", redacted);
    }

    [Fact]
    public void Telegramm_wird_aus_JSON_gelesen()
    {
        using var document = JsonDocument.Parse(
            """
            {"Id":42,"DateTime":"2026-09-01T10:11:12","TelegramDirection":2,
             "ConnectionName":"PLC1","Data":"0815","Format":null}
            """);

        var telegram = Telegram.FromJson(document.RootElement);

        Assert.Equal(42, telegram.Id);
        Assert.Equal(TelegramDirection.FromPlc, telegram.TelegramDirection);
        Assert.Equal("PLC1", telegram.ConnectionName);
        Assert.Equal("0815", telegram.Data);
        Assert.Null(telegram.Format);
    }

    [Fact]
    public void Passwort_wird_als_Zahlenarray_serialisiert()
    {
        // Der Server erwartet das verschlüsselte Passwort als Byte-Array, nicht als Base64.
        var options = new JsonSerializerOptions { Converters = { new ByteArrayAsNumbersConverter() } };

        var json = JsonSerializer.Serialize(new { password = new byte[] { 1, 2, 255 } }, options);

        Assert.Equal("""{"password":[1,2,255]}""", json);
    }
}
