using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

#nullable enable

namespace Zest.App.Runtime;

/// <summary>
/// Lightweight WebSocket server for live-reload broadcasting.
/// Accepts WebSocket clients on a dedicated port, maintains an active
/// connection pool, and broadcasts "reload"/"style" frames on demand.
/// Implements the RFC 6455 handshake and frame encoding.
/// </summary>
/// <remarks>
/// The hub is optional: when its port cannot be bound (another process holds
/// it) the caller keeps serving and the injected client script falls back to
/// the SSE endpoint. Live reload degrades, it never takes the dev server down.
/// </remarks>
public class LiveReloadHub : IDisposable
{
    /// <summary>Handshake requests larger than this are treated as garbage.</summary>
    private const int MaxHandshakeBytes = 16 * 1024;

    /// <summary>Upper bound on concurrent live-reload sockets.</summary>
    private const int MaxClients = 64;

    /// <summary>Upper bound on a client frame payload we are willing to drain.</summary>
    private const long MaxInboundPayload = 16 * 1024 * 1024;

    private readonly int _port;
    private TcpListener? _wsListener;
    private readonly List<TcpClient> _wsClients = new();
    private readonly object _wsLock = new();
    private CancellationTokenSource? _cts;
    private volatile bool _disposed;

    public LiveReloadHub(int port)
    {
        _port = port;
    }

    /// <summary>True once the socket is accepting clients.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Begin accepting clients. Returns false (without throwing) when the port
    /// is unavailable, so the caller can continue with SSE only.
    /// </summary>
    public bool Start(CancellationTokenSource cts)
    {
        _cts = cts;
        try
        {
            _wsListener = new TcpListener(IPAddress.Loopback, _port);
            _wsListener.Start();
        }
        catch (SocketException ex)
        {
            _wsListener = null;
            LogWriter.Warn("WebSocket",
                $"Live-reload port {_port} unavailable ({ex.SocketErrorCode}). " +
                "Falling back to server-sent events.");
            return false;
        }

        IsRunning = true;
        _ = Task.Run(() => AcceptClients(cts.Token));
        return true;
    }

    public void Stop()
    {
        _disposed = true;
        IsRunning = false;

        try { _wsListener?.Stop(); }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }

        _wsListener = null;

        lock (_wsLock)
        {
            foreach (var c in _wsClients)
            {
                try { c.Close(); } catch { }
            }
            _wsClients.Clear();
        }
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    public void BroadcastReload()
    {
        BroadcastJson("{\"type\":\"reload\"}");
    }

    /// <summary>
    /// Broadcast a CSS style update. Clients reload external stylesheets
    /// without a full page refresh.
    /// </summary>
    public void BroadcastStyleUpdate()
    {
        BroadcastJson("{\"type\":\"style\"}");
    }

    private void BroadcastJson(string json)
    {
        // Snapshot the client list under the lock, then write outside it so a
        // slow client's blocking Write can never stall the rebuild loop.
        TcpClient[] snapshot;
        lock (_wsLock)
        {
            if (_wsClients.Count == 0) return;
            snapshot = _wsClients.ToArray();
        }

        var frame = EncodeWebSocketFrame(json);
        _ = Task.Run(() =>
        {
            var dead = new List<TcpClient>();
            foreach (var c in snapshot)
            {
                try
                {
                    var stream = c.GetStream();
                    stream.Write(frame, 0, frame.Length);
                }
                catch { dead.Add(c); }
            }

            if (dead.Count > 0)
            {
                lock (_wsLock)
                {
                    foreach (var c in dead) _wsClients.Remove(c);
                }
            }

            LogWriter.VerboseLog($"Broadcast to {snapshot.Length} clients ({dead.Count} dead): {json}");
        });
    }

    private async Task AcceptClients(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && !_disposed)
        {
            try
            {
                var client = await _wsListener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);

                if (ClientCount >= MaxClients)
                {
                    // Refuse rather than grow without bound.
                    try { client.Close(); } catch { }
                    LogWriter.VerboseLog($"WebSocket client refused (limit {MaxClients}).");
                    continue;
                }

                _ = Task.Run(() => HandleClient(client), CancellationToken.None);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted) { break; }
            catch (Exception ex)
            {
                // Log non-cancellation errors so we notice port conflicts etc.
                LogWriter.Warn("WebSocket", $"Accept error: {ex.Message}");
                // Brief delay before retry to avoid tight spin on persistent errors.
                try { await Task.Delay(500, ct); } catch { break; }
            }
        }
    }

    private int ClientCount
    {
        get { lock (_wsLock) return _wsClients.Count; }
    }

    private async Task HandleClient(TcpClient tcpClient)
    {
        try
        {
            using var stream = tcpClient.GetStream();

            var request = await ReadHandshakeAsync(stream, _cts?.Token ?? CancellationToken.None);
            if (request is null) return;

            if (!HasWebSocketUpgrade(request))
            {
                LogWriter.VerboseLog("WebSocket: handshake missing Upgrade header, closing.");
                return;
            }

            var key = HeaderValue(request, "Sec-WebSocket-Key");
            if (key is null)
            {
                LogWriter.VerboseLog("WebSocket: handshake missing Sec-WebSocket-Key, closing.");
                return;
            }

            var acceptKey = ComputeAcceptKey(key);
            var response = "HTTP/1.1 101 Switching Protocols\r\n" +
                           "Upgrade: websocket\r\n" +
                           "Connection: Upgrade\r\n" +
                           $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response));

            lock (_wsLock) _wsClients.Add(tcpClient);
            LogWriter.VerboseLog($"WebSocket client connected (total: {ClientCount})");

            await PumpClientFramesAsync(stream);
        }
        catch (IOException) { /* client disconnected during handshake */ }
        catch (ObjectDisposedException) { /* shutdown race */ }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (Exception ex)
        {
            LogWriter.VerboseLog($"WebSocket client error: {ex.Message}");
        }
        finally
        {
            lock (_wsLock) _wsClients.Remove(tcpClient);
        }
    }

    /// <summary>
    /// Read up to the end of the HTTP request header block.
    /// Returns null when the client closed before sending a full header.
    /// </summary>
    private static async Task<string?> ReadHandshakeAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[4096];
        var accumulated = new StringBuilder();

        while (accumulated.Length < MaxHandshakeBytes)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
            if (read == 0) return null;

            accumulated.Append(Encoding.UTF8.GetString(buffer, 0, read));
            if (accumulated.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                return accumulated.ToString();
        }

        return null;
    }

    private static bool HasWebSocketUpgrade(string request) =>
        HeaderValue(request, "Upgrade")?.Contains("websocket", StringComparison.OrdinalIgnoreCase) == true;

    private static string? HeaderValue(string request, string headerName)
    {
        foreach (var line in request.Split("\r\n"))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;

            var name = line[..colon].Trim();
            if (string.Equals(name, headerName, StringComparison.OrdinalIgnoreCase))
                return line[(colon + 1)..].Trim();
        }
        return null;
    }

    /// <summary>
    /// Consume client frames until close or disconnect. Browsers do not send
    /// data to this hub, but the payload of every frame must still be read:
    /// leaving it in the socket desynchronises the next frame header.
    /// </summary>
    private async Task PumpClientFramesAsync(NetworkStream stream)
    {
        var token = _cts?.Token ?? CancellationToken.None;
        var header = new byte[2];

        while (!token.IsCancellationRequested && !_disposed)
        {
            if (!await ReadExactAsync(stream, header, token)) break;

            var opcode = header[0] & 0x0F;
            var masked = (header[1] & 0x80) != 0;
            long payloadLength = header[1] & 0x7F;

            if (payloadLength == 126)
            {
                var extended = new byte[2];
                if (!await ReadExactAsync(stream, extended, token)) break;
                payloadLength = (extended[0] << 8) | extended[1];
            }
            else if (payloadLength == 127)
            {
                var extended = new byte[8];
                if (!await ReadExactAsync(stream, extended, token)) break;
                payloadLength = 0;
                for (var i = 0; i < 8; i++)
                    payloadLength = (payloadLength << 8) | extended[i];
            }

            if (masked)
            {
                var maskKey = new byte[4];
                if (!await ReadExactAsync(stream, maskKey, token)) break;
            }

            if (payloadLength > MaxInboundPayload) break;
            if (payloadLength > 0 && !await DrainAsync(stream, payloadLength, token)) break;

            if (opcode == 0x08) break;      // close frame
            if (opcode == 0x09)             // ping → respond with pong
            {
                var pong = new byte[] { 0x8A, 0x00 };
                await stream.WriteAsync(pong.AsMemory(0, 2), token);
            }
            // Other data frames are consumed and discarded.
        }
    }

    private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct);
            if (read == 0) return false;
            offset += read;
        }
        return true;
    }

    private static async Task<bool> DrainAsync(NetworkStream stream, long length, CancellationToken ct)
    {
        var scratch = new byte[4096];
        var remaining = length;
        while (remaining > 0)
        {
            var want = (int)Math.Min(scratch.Length, remaining);
            var read = await stream.ReadAsync(scratch.AsMemory(0, want), ct);
            if (read == 0) return false;
            remaining -= read;
        }
        return true;
    }

    // ── RFC 6455 helpers ──

    private static byte[] EncodeWebSocketFrame(string text)
    {
        var payload = Encoding.UTF8.GetBytes(text);

        if (payload.Length <= 125)
        {
            var frame = new byte[payload.Length + 2];
            frame[0] = 0x81; // FIN + text opcode
            frame[1] = (byte)payload.Length;
            Array.Copy(payload, 0, frame, 2, payload.Length);
            return frame;
        }

        if (payload.Length <= 65535)
        {
            var frame = new byte[payload.Length + 4];
            frame[0] = 0x81;
            frame[1] = 126;
            frame[2] = (byte)(payload.Length >> 8);
            frame[3] = (byte)(payload.Length & 0xFF);
            Array.Copy(payload, 0, frame, 4, payload.Length);
            return frame;
        }

        // Extended payload (> 65535 bytes)
        var frameLarge = new byte[payload.Length + 10];
        frameLarge[0] = 0x81;
        frameLarge[1] = 127;
        var len = (ulong)payload.Length;
        for (var i = 7; i >= 0; i--)
        {
            frameLarge[2 + i] = (byte)(len & 0xFF);
            len >>= 8;
        }
        Array.Copy(payload, 0, frameLarge, 10, payload.Length);
        return frameLarge;
    }

    private static string ComputeAcceptKey(string key)
    {
        const string magic = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
#pragma warning disable CA5350 // SHA1 required by RFC 6455
        return Convert.ToBase64String(SHA1.HashData(Encoding.UTF8.GetBytes(key + magic)));
#pragma warning restore CA5350
    }

    private string? _cachedLiveReloadScript;

    /// <summary>
    /// Generate the live-reload client script for injection into HTML pages.
    /// Supports full-page reload and CSS-only style injection.
    /// Falls back to SSE if WebSocket cannot connect within 2 seconds.
    /// The script is identical for every page, so it is built once and cached.
    /// </summary>
    public string GetLiveReloadScript() => _cachedLiveReloadScript ??= BuildLiveReloadScript();

    private string BuildLiveReloadScript() => $@"
<script>
(function(){{
    var port = {_port};
    // Use the page's own host so live reload also works when the dev server
    // is reached from another device (--host 0.0.0.0) instead of the machine
    // the browser runs on.
    var host = window.location.hostname || 'localhost';
    var connected = false;
    var wsFallbackTimer = null;

    function handleMessage(data) {{
        try {{
            var msg = typeof data === 'string' ? JSON.parse(data) : JSON.parse(data.data);
            if (msg.type === 'style') {{
                connected = true;
                var links = document.querySelectorAll('link[rel=""stylesheet""]');
                links.forEach(function(link) {{
                    try {{
                        var url = new URL(link.href);
                        url.searchParams.set('_t', Date.now());
                        link.href = url.toString();
                    }} catch(_) {{}}
                }});
                return;
            }}
            if (msg.type === 'reload') {{
                connected = true;
                window.location.reload();
                return;
            }}
        }} catch(_) {{}}
        if (data === 'reload') {{
            connected = true;
            window.location.reload();
        }}
    }}

    function tryWebSocket() {{
        var ws = new WebSocket('ws://' + host + ':' + port + '/livereload');
        // Fallback to SSE if WebSocket doesn't connect within 2 seconds
        wsFallbackTimer = setTimeout(function() {{
            ws.close();
            tryEventSource();
        }}, 2000);

        ws.onopen = function() {{
            if (wsFallbackTimer) clearTimeout(wsFallbackTimer);
        }};
        ws.onmessage = function(e) {{
            if (wsFallbackTimer) clearTimeout(wsFallbackTimer);
            handleMessage(e.data);
        }};
        ws.onclose = function() {{
            if (wsFallbackTimer) clearTimeout(wsFallbackTimer);
            if (connected) {{
                setTimeout(function(){{ window.location.reload(); }}, 1000);
            }} else {{
                setTimeout(tryWebSocket, 3000);
            }}
        }};
        ws.onerror = function() {{}};
    }}

    function tryEventSource() {{
        var es = new EventSource('/__zest_livereload_events');
        es.onmessage = function(e) {{
            handleMessage(e);
        }};
        es.onerror = function() {{
            es.close();
            if (connected) {{
                setTimeout(function(){{ window.location.reload(); }}, 1000);
            }} else {{
                setTimeout(tryWebSocket, 3000);
            }}
        }};
    }}

    tryWebSocket();
}})();
</script>";
}
