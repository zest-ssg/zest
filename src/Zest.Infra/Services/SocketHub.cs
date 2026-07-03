using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

#nullable enable

namespace Zest.Infra.Services;

/// <summary>
/// WebSocket server for live-reload broadcasting.
/// Accepts WebSocket clients, maintains connection pool, and broadcasts "reload" frames.
/// </summary>
public class SocketHub : IDisposable
{
    private readonly int _port;
    private TcpListener? _wsListener;
    private readonly List<TcpClient> _wsClients = new();
    private readonly object _wsLock = new();
    private CancellationTokenSource? _cts;

    public SocketHub(int port)
    {
        _port = port;
    }

    public void Start(CancellationTokenSource cts)
    {
        _cts = cts;
        _wsListener = new(IPAddress.Loopback, _port);
        _wsListener.Start();
        _ = Task.Run(() => AcceptClients(cts.Token));
    }

    public void Stop()
    {
        _wsListener?.Stop();
        lock (_wsLock)
        {
            foreach (var c in _wsClients) c.Close();
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
        lock (_wsLock)
        {
            if (_wsClients.Count == 0) return;
            var dead = new List<TcpClient>();
            foreach (var c in _wsClients)
            {
                try
                {
                    var stream = c.GetStream();
                    var frame = EncodeWebSocketFrame("reload");
                    stream.Write(frame, 0, frame.Length);
                }
                catch { dead.Add(c); }
            }
            foreach (var c in dead) _wsClients.Remove(c);
            LogWriter.VerboseLog($"Broadcast reload to {_wsClients.Count} clients");
        }
    }

    private async Task AcceptClients(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
#pragma warning disable CA2016 // WaitAsync handles cancellation; AcceptTcpClientAsync has no CT overload returning Task
                var client = await _wsListener!.AcceptTcpClientAsync().WaitAsync(ct);
#pragma warning restore CA2016
                _ = Task.Run(() => HandleClient(client), CancellationToken.None);
            }
            catch { break; }
        }
    }

    private async Task HandleClient(TcpClient tcpClient)
    {
        try
        {
            using var stream = tcpClient.GetStream();
            var buf = new byte[4096];
            var read = await stream.ReadAsync(buf.AsMemory(0, buf.Length));
            var req = Encoding.UTF8.GetString(buf, 0, read);
            var keyMatch = Regex.Match(req, @"Sec-WebSocket-Key:\s*(.+)");
            if (!keyMatch.Success) return;

            var acceptKey = ComputeAcceptKey(keyMatch.Groups[1].Value.Trim());
            var response = "HTTP/1.1 101 Switching Protocols\r\n" +
                          "Upgrade: websocket\r\n" +
                          "Connection: Upgrade\r\n" +
                          $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response));

            lock (_wsLock) _wsClients.Add(tcpClient);
            LogWriter.VerboseLog($"WebSocket client connected (total: {_wsClients.Count})");

            try
            {
                while (_cts is { IsCancellationRequested: false })
                {
                    var frame = new byte[2];
                    var n = await stream.ReadAsync(frame.AsMemory(0, 2), _cts.Token);
                    if (n < 2 || (frame[0] & 0x0F) == 0x08) break;
                }
            }
            catch { }
            finally { lock (_wsLock) _wsClients.Remove(tcpClient); }
        }
        catch { }
    }

    private static byte[] EncodeWebSocketFrame(string text)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        byte[] frame;
        if (payload.Length <= 125)
        {
            frame = new byte[payload.Length + 2];
            frame[0] = 0x81;
            frame[1] = (byte)payload.Length;
            Array.Copy(payload, 0, frame, 2, payload.Length);
        }
        else if (payload.Length <= 65535)
        {
            frame = new byte[payload.Length + 4];
            frame[0] = 0x81;
            frame[1] = 126;
            frame[2] = (byte)(payload.Length >> 8);
            frame[3] = (byte)(payload.Length & 0xFF);
            Array.Copy(payload, 0, frame, 4, payload.Length);
        }
        else
        {
            frame = new byte[payload.Length + 10];
            frame[0] = 0x81;
            frame[1] = 127;
            var len = (ulong)payload.Length;
            for (int i = 7; i >= 0; i--) { frame[2 + i] = (byte)(len & 0xFF); len >>= 8; }
            Array.Copy(payload, 0, frame, 10, payload.Length);
        }
        return frame;
    }

    private static string ComputeAcceptKey(string key)
    {
        const string magic = "258EAFA5-E914-47DA-95CA-C5AB5E0285C2";
#pragma warning disable CA5350 // SHA1 is required by RFC 6455 for WebSocket handshake
        return Convert.ToBase64String(SHA1.HashData(Encoding.UTF8.GetBytes(key + magic)));
#pragma warning restore CA5350
    }

    /// <summary>
    /// Generate the live-reload WebSocket script for injection into HTML pages.
    /// </summary>
    public string GetLiveReloadScript() => $@"
<script>
(function(){{
    var port = {_port};
    var connected = false;
    function connect() {{
        var ws = new WebSocket('ws://localhost:' + port + '/livereload');
        ws.onmessage = function(e) {{
            if (e.data === 'reload') {{
                connected = true;
                window.location.reload();
            }}
        }};
        ws.onclose = function() {{
            if (connected) {{
                setTimeout(function(){{ window.location.reload(); }}, 1000);
            }} else {{
                setTimeout(connect, 3000);
            }}
        }};
        ws.onerror = function() {{}};
    }}
    connect();
}})();
</script>";
}
