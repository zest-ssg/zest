using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

#nullable enable

namespace Zest.Infra.Services;

/// <summary>
/// Lightweight WebSocket server for live-reload broadcasting.
/// Accepts WebSocket clients on a dedicated port, maintains an active
/// connection pool, and broadcasts "reload"/"style" frames on demand.
/// Implements RFC 6455 handshake and frame encoding.
/// </summary>
public class SocketHub : IDisposable
{
    private readonly int _port;
    private TcpListener? _wsListener;
    private readonly List<TcpClient> _wsClients = new();
    private readonly object _wsLock = new();
    private CancellationTokenSource? _cts;
    private volatile bool _disposed;

    public SocketHub(int port)
    {
        _port = port;
    }

    public void Start(CancellationTokenSource cts)
    {
        _cts = cts;
        _wsListener = new TcpListener(IPAddress.Loopback, _port);
        _wsListener.Start();
        _ = Task.Run(() => AcceptClients(cts.Token));
    }

    public void Stop()
    {
        _disposed = true;

        try { _wsListener?.Stop(); }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }

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
        lock (_wsLock)
        {
            if (_wsClients.Count == 0) return;

            var frame = EncodeWebSocketFrame(json);
            var dead = new List<TcpClient>();

            foreach (var c in _wsClients)
            {
                try
                {
                    var stream = c.GetStream();
                    stream.Write(frame, 0, frame.Length);
                }
                catch { dead.Add(c); }
            }

            foreach (var c in dead) _wsClients.Remove(c);

            if (_wsClients.Count > 0 || dead.Count > 0)
                LogWriter.VerboseLog($"Broadcast to {_wsClients.Count} clients ({dead.Count} dead): {json}");
        }
    }

    private async Task AcceptClients(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && !_disposed)
        {
            try
            {
                var client = await _wsListener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);
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

    private async Task HandleClient(TcpClient tcpClient)
    {
        try
        {
            using var stream = tcpClient.GetStream();
            var buf = new byte[4096];
            var read = await stream.ReadAsync(buf.AsMemory(0, buf.Length));
            if (read == 0) return;

            var req = Encoding.UTF8.GetString(buf, 0, read);
            var keyMatch = Regex.Match(req, @"Sec-WebSocket-Key:\s*(.+)");
            if (!keyMatch.Success)
            {
                LogWriter.VerboseLog("WebSocket: handshake missing Sec-WebSocket-Key, closing.");
                return;
            }

            var acceptKey = ComputeAcceptKey(keyMatch.Groups[1].Value.Trim());
            var response = "HTTP/1.1 101 Switching Protocols\r\n" +
                           "Upgrade: websocket\r\n" +
                           "Connection: Upgrade\r\n" +
                           $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response));

            lock (_wsLock) _wsClients.Add(tcpClient);
            LogWriter.VerboseLog($"WebSocket client connected (total: {_wsClients.Count})");

            // Read loop: wait for close frame (opcode 0x8) or connection drop.
            // We ignore ping (0x9) — the TCP stack handles keepalive.
            try
            {
                while (_cts is { IsCancellationRequested: false } && !_disposed)
                {
                    var frame = new byte[2];
                    var n = await stream.ReadAsync(frame.AsMemory(0, 2), _cts!.Token);
                    if (n < 2) break; // connection closed

                    var opcode = frame[0] & 0x0F;
                    if (opcode == 0x08) break;  // close frame
                    if (opcode == 0x09)         // ping → respond with pong
                    {
                        var pong = new byte[2];
                        pong[0] = 0x8A; // FIN + pong opcode
                        pong[1] = 0x00; // zero-length payload
                        await stream.WriteAsync(pong.AsMemory(0, 2), _cts.Token);
                    }
                    // For data frames (0x1 text, 0x2 binary), just consume and
                    // discard — we don't expect client→server messages.
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            finally
            {
                lock (_wsLock) _wsClients.Remove(tcpClient);
            }
        }
        catch (IOException) { /* client disconnected during handshake */ }
        catch (ObjectDisposedException) { /* shutdown race */ }
        catch (Exception ex)
        {
            LogWriter.VerboseLog($"WebSocket client error: {ex.Message}");
        }
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
        for (int i = 7; i >= 0; i--)
        {
            frameLarge[2 + i] = (byte)(len & 0xFF);
            len >>= 8;
        }
        Array.Copy(payload, 0, frameLarge, 10, payload.Length);
        return frameLarge;
    }

    private static string ComputeAcceptKey(string key)
    {
        const string magic = "258EAFA5-E914-47DA-95CA-C5AB5E0285C2";
#pragma warning disable CA5350 // SHA1 required by RFC 6455
        return Convert.ToBase64String(SHA1.HashData(Encoding.UTF8.GetBytes(key + magic)));
#pragma warning restore CA5350
    }

    /// <summary>
    /// Generate the live-reload client script for injection into HTML pages.
    /// Supports full-page reload and CSS-only style injection.
    /// Falls back to SSE if WebSocket connection fails within 2 seconds.
    /// </summary>
    public string GetLiveReloadScript() => $@"
<script>
(function(){{
    var port = {_port};
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
        var ws = new WebSocket('ws://localhost:' + port + '/livereload');
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
