using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Kcc.Recorder;

/// <summary>Ein Aufruf ist fehlgeschlagen — entweder fachlich (Error) oder serverintern (InternalError).</summary>
public sealed class KccProtocolException(string message) : Exception(message);

/// <summary>
/// WebSocket-Transport zur MCC/KCC-Gegenstelle.
///
/// Bildet dist/communication/DefaultCommunication.js nach: Anfragen tragen eine GUID, Antworten
/// werden über dieselbe GUID zugeordnet. Eingehende Rahmen können Text-JSON, gzip-komprimiertes
/// JSON oder ein proprietäres Binärformat sein.
/// </summary>
public sealed class KccConnection : IAsyncDisposable
{
    /// <summary>Erstes Byte eines Rahmens im proprietären Binärformat (Tag-Werte der Visu — für uns ohne Belang).</summary>
    const byte BinaryFrameMarker = 0xFF;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new ByteArrayAsNumbersConverter() },
    };

    readonly Uri _uri;
    readonly bool _allowUntrustedCertificate;
    readonly Action<string> _log;
    readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();

    ClientWebSocket? _socket;
    Task? _receiveLoop;
    CancellationTokenSource? _cts;

    public KccConnection(Uri uri, bool allowUntrustedCertificate, Action<string> log)
    {
        _uri = uri;
        _allowUntrustedCertificate = allowUntrustedCertificate;
        _log = log;
    }

    public bool IsOpen => _socket?.State == WebSocketState.Open;

    public async Task ConnectAsync(CancellationToken ct)
    {
        await CloseSocketAsync();

        var socket = new ClientWebSocket();
        if (_allowUntrustedCertificate)
            socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;

        _log($"Verbinde zu {_uri} …");
        await socket.ConnectAsync(_uri, ct);
        _log("WebSocket verbunden.");

        _socket = socket;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(socket, _cts.Token), CancellationToken.None);
    }

    /// <summary>Sendet einen Aufruf und wartet auf die Antwort mit derselben Id.</summary>
    public async Task<JsonElement> CallAsync(
        string serviceType,
        string functionName,
        object parameters,
        CancellationToken ct)
    {
        var socket = _socket ?? throw new InvalidOperationException("Nicht verbunden.");
        var id = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        try
        {
            var request = new Dictionary<string, object?>
            {
                ["Id"] = id,
                ["ServiceType"] = serviceType,
                ["ServiceName"] = null,
                ["FunctionName"] = functionName,
                ["Parameters"] = parameters,
            };
            var json = JsonSerializer.Serialize(request, JsonOptions);
            _log($"→ {functionName} {Redact(json)}");

            await socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, ct);
            return await tcs.Task.WaitAsync(ct);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        FailAllPending(new KccProtocolException("Die Gegenstelle hat die Verbindung geschlossen."));
                        return;
                    }
                    message.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                var payload = message.ToArray();
                if (!TryDecodeFrame(payload, result.MessageType, out var json))
                    continue;

                Dispatch(json);
            }
        }
        catch (OperationCanceledException)
        {
            // Regulärer Abbruch.
        }
        catch (Exception ex)
        {
            FailAllPending(ex);
        }
    }

    /// <summary>
    /// Dekodiert einen eingehenden Rahmen. Liefert false, wenn er zu verwerfen ist
    /// (leer oder proprietäres Binärformat).
    /// </summary>
    internal static bool TryDecodeFrame(byte[] payload, WebSocketMessageType type, out string json)
    {
        json = "";
        if (payload.Length == 0)
            return false;

        if (type == WebSocketMessageType.Text)
        {
            json = DecodeUtf8(payload);
            return true;
        }

        if (payload[0] == BinaryFrameMarker)
            return false;

        using var input = new MemoryStream(payload);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        json = DecodeUtf8(output.ToArray());
        return true;
    }

    /// <summary>
    /// UTF-8 nach string, ohne führende BOM. <see cref="Encoding.UTF8"/> entfernt die BOM nicht;
    /// <see cref="JsonDocument.Parse(string, JsonDocumentOptions)"/> würde sonst an "0xEF" scheitern.
    /// </summary>
    static string DecodeUtf8(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
    }

    void Dispatch(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("Id", out var idElement) || idElement.ValueKind != JsonValueKind.String)
            return; // Push-Nachricht ohne Id — für den Recorder ohne Bedeutung.

        var id = idElement.GetString()!;
        if (!_pending.TryRemove(id, out var tcs))
            return;

        if (root.TryGetProperty("InternalError", out var internalError) &&
            internalError.ValueKind == JsonValueKind.True)
        {
            var detail = root.TryGetProperty("Error", out var e) ? e.ToString() : "kein Detail";
            tcs.TrySetException(new KccProtocolException($"Serverinterner Fehler (Server-Log prüfen): {detail}"));
            return;
        }

        if (root.TryGetProperty("Error", out var error) &&
            error.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            tcs.TrySetException(new KccProtocolException($"Fehler vom Server: {error}"));
            return;
        }

        // Clone(), weil das JsonDocument mit diesem using-Block verworfen wird.
        var response = root.TryGetProperty("Response", out var r) ? r.Clone() : default;
        tcs.TrySetResult(response);
    }

    void FailAllPending(Exception ex)
    {
        foreach (var key in _pending.Keys)
            if (_pending.TryRemove(key, out var tcs))
                tcs.TrySetException(ex);
    }

    /// <summary>Hält Passwort und SessionId aus dem Protokoll heraus.</summary>
    internal static string Redact(string json)
    {
        json = System.Text.RegularExpressions.Regex.Replace(
            json, "\"password\"\\s*:\\s*\\[[^\\]]*\\]", "\"password\":[…]");
        return System.Text.RegularExpressions.Regex.Replace(
            json, "(\"[Ss]essionId\"\\s*:\\s*\")[^\"]*\"", "$1…\"");
    }

    async Task CloseSocketAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
            _cts = null;
        }

        if (_receiveLoop is not null)
        {
            // Der Loop endet mit dem abgebrochenen Token; die Frist verhindert ein Hängen,
            // falls ReceiveAsync den Abbruch nicht sofort quittiert.
            try { await _receiveLoop.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch { /* beim Schliessen ohne Belang */ }
            _receiveLoop = null;
        }

        _socket?.Dispose();
        _socket = null;
    }

    public async ValueTask DisposeAsync() => await CloseSocketAsync();
}
