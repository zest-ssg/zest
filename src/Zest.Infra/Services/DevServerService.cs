using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Zest.Engine;

#nullable enable

namespace Zest.Infra.Services;

/// <summary>
/// Development HTTP server with live-reload via WebSocket.
/// Monitors file changes, triggers rebuilds via F# BuildEngine,
/// and broadcasts reload to all connected browsers.
/// Features: request logging, ETag caching, CORS, --verbose FSI output,
/// --open browser, --host binding, live reload via WebSocket.
/// </summary>
public class DevServerService : IDisposable
{
    private readonly SiteConfig _config;
    private readonly string _host;
    private readonly bool _openBrowser;
    private HttpListener? _listener;
    private TcpListener? _wsListener;
    private readonly List<TcpClient> _wsClients = new();
    private readonly object _wsLock = new();
    private CancellationTokenSource? _cts;
    private FileSystemWatcher? _watcher;
    private readonly BuildService _buildService = new();
    private string? _outputDir;
    private long _totalRequests;
    private long _rebuildCount;

    public int Port => _config.DevServerPort;
    public string Host => _host;

    public DevServerService(SiteConfig config, string host = "localhost", bool openBrowser = false)
    {
        _config = config;
        _host = host;
        _openBrowser = openBrowser;
    }

    public void Start()
    {
        _cts = new();

        _outputDir = Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            _config.OutputDir.TrimStart('.', '\\', '/')));

        // Initial build
        Logger.Info("Build", "Building site for development...");
        var result = _buildService.Execute(_config);
        BuildService.PrintResult(result, _config);

        if (result.Errors.Length > 0)
        {
            foreach (var err in result.Errors)
                Logger.Error("Build", err);
        }

        // HTTP server
        _listener = new();
        _listener.Prefixes.Add($"http://{_host}:{_config.DevServerPort}/");
        _listener.Start();
        _ = Task.Run(() => ServeHttp(_cts.Token));

        // WebSocket server for live reload
        _wsListener = new(IPAddress.Loopback, _config.LiveReloadPort);
        _wsListener.Start();
        _ = Task.Run(() => AcceptWebSocketClients(_cts.Token));

        Logger.Banner(
            "Zest Development Server",
            $"http://{_host}:{_config.DevServerPort}/",
            ("Host", _host),
            ("Port", _config.DevServerPort.ToString()),
            ("Reload", $"ws://localhost:{_config.LiveReloadPort}"),
            ("Root", _outputDir),
            ("Verbose", Logger.Verbose ? "ON" : "off")
        );

        if (_openBrowser)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"http://{_host}:{_config.DevServerPort}/",
                    UseShellExecute = true
                });
                Logger.Info("Browser", "Opened in default browser");
            }
            catch (Exception ex)
            {
                Logger.Warn("Browser", $"Could not open browser: {ex.Message}");
            }
        }

        Logger.Info("Press Ctrl+C to stop.");

        StartFileWatcher();
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _wsListener?.Stop();
        _watcher?.Dispose();
        lock (_wsLock)
        {
            foreach (var c in _wsClients) c.Close();
            _wsClients.Clear();
        }

        Logger.Info($"Total requests: {_totalRequests}, rebuilds: {_rebuildCount}");
    }

    public void Dispose() => Stop();

    private void StartFileWatcher()
    {
        var watchDir = Directory.GetCurrentDirectory();
        _watcher = new(watchDir, "*.*")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime
        };

        var debounceTimer = new System.Timers.Timer(300) { AutoReset = false };
        debounceTimer.Elapsed += (_, _) => Rebuild();

        void OnChange(object sender, FileSystemEventArgs e)
        {
            // Skip output directory changes (use normalized path comparison)
            if (_outputDir != null && e.FullPath.StartsWith(_outputDir, StringComparison.OrdinalIgnoreCase))
                return;

            // Skip hidden/system directories (starting with _ or .) and build artifacts
            var parts = e.FullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            for (int i = 0; i < parts.Length - 1; i++)
            {
                var p = parts[i];
                if (p.StartsWith("_") || p.StartsWith(".") ||
                    string.Equals(p, "bin", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p, "obj", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p, "node_modules", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p, "packages", StringComparison.OrdinalIgnoreCase))
                    return;
            }

            var ext = Path.GetExtension(e.Name!)?.ToLowerInvariant() ?? "";
            var relevant = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".fsx", ".zpage.fsx", ".md", ".markdown",
                ".html", ".css", ".zcss", ".js", ".toml",
                ".json", ".yaml", ".yml",
                ".png", ".jpg", ".jpeg", ".svg", ".gif", ".webp"
            };
            if (!relevant.Contains(ext)) return;

            debounceTimer.Stop();
            debounceTimer.Start();
        }

        _watcher.Changed += OnChange;
        _watcher.Created += OnChange;
        _watcher.Deleted += OnChange;
        _watcher.Renamed += (_, _) => { debounceTimer.Stop(); debounceTimer.Start(); };
        _watcher.EnableRaisingEvents = true;
    }

    private void Rebuild()
    {
        Logger.Warn("Watch", "Change detected, rebuilding...");

        var sw = Stopwatch.StartNew();
        var result = _buildService.Execute(_config);
        sw.Stop();

        BuildService.PrintResult(result, _config);

        if (result.Errors.Length > 0)
        {
            foreach (var err in result.Errors)
                Logger.Error("Build", err);
        }
        else
        {
            Logger.Info("Build", $"Rebuilt in {sw.ElapsedMilliseconds}ms ({result.ProcessedPages} pages)");
        }

        Interlocked.Increment(ref _rebuildCount);
        BroadcastReload();
    }

    private async Task ServeHttp(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener!.IsListening)
        {
            try
            {
                var ctx = await _listener.GetContextAsync().WaitAsync(ct);
                _ = Task.Run(() => HandleRequest(ctx));
            }
            catch { break; }
        }
    }

    private async Task HandleRequest(HttpListenerContext ctx)
    {
        var sw = Stopwatch.StartNew();
        var urlPath = ctx.Request.Url?.AbsolutePath ?? "/";
        var method = ctx.Request.HttpMethod;

        try
        {
            // Handle OPTIONS preflight
            if (method == "OPTIONS")
            {
                ctx.Response.StatusCode = 204;
                HttpResponseUtility.AddCorsHeaders(ctx.Response);
                ctx.Response.OutputStream.Close();
                sw.Stop();
                Logger.Request(method, urlPath, 204, sw.ElapsedMilliseconds);
                return;
            }

            var outputDir = _outputDir!;

            string filePath;
            try
            {
                filePath = HttpResponseUtility.ResolveFilePath(outputDir, urlPath);
            }
            catch (UnauthorizedAccessException)
            {
                ctx.Response.StatusCode = 403;
                await HttpResponseUtility.WriteStringResponse(ctx, 403, "<h1>403 — Forbidden</h1>");
                sw.Stop();
                Logger.Request(method, urlPath, 403, sw.ElapsedMilliseconds);
                Logger.Warn("Security", $"Path traversal blocked: {urlPath}");
                return;
            }

            if (!File.Exists(filePath))
            {
                await HttpResponseUtility.WriteNotFoundResponse(ctx, outputDir, urlPath);
                sw.Stop();
                Logger.Request(method, urlPath, 404, sw.ElapsedMilliseconds);
                return;
            }

            var ext = Path.GetExtension(filePath).ToLowerInvariant();

            // Handle .zcss files: compile to CSS on-the-fly
            if (ext == ".zcss")
            {
                await ServeZssFile(ctx, filePath);
                sw.Stop();
                Logger.Request(method, urlPath, 200, sw.ElapsedMilliseconds);
                return;
            }

            // Read file
            var bytes = await File.ReadAllBytesAsync(filePath);

            // Inject live reload script into HTML
            if (ext == ".html")
            {
                var html = Encoding.UTF8.GetString(bytes);
                html = html.Replace("</body>", GetLiveReloadScript() + "\n</body>");

                // Also inject into head-less fragments
                if (!html.Contains("</body>"))
                    html += GetLiveReloadScript();

                ctx.Response.ContentType = "text/html; charset=utf-8";
                HttpResponseUtility.AddCorsHeaders(ctx.Response);
                bytes = Encoding.UTF8.GetBytes(html);
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes);
            }
            else
            {
                ctx.Response.ContentType = HttpResponseUtility.GetMimeType(filePath);
                HttpResponseUtility.AddCorsHeaders(ctx.Response);
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes);
            }

            await ctx.Response.OutputStream.FlushAsync();

            Interlocked.Increment(ref _totalRequests);
            sw.Stop();
            Logger.Request(method, urlPath, 200, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            try
            {
                ctx.Response.StatusCode = 500;
                await HttpResponseUtility.WriteStringResponse(ctx, 500,
                    $"<h1>500 — Internal Server Error</h1><p>{System.Net.WebUtility.HtmlEncode(ex.Message)}</p>");
            }
            catch { }
            sw.Stop();
            Logger.Request(method, urlPath, 500, sw.ElapsedMilliseconds);
            Logger.Error("Server", ex.Message);
        }
        finally
        {
            try { ctx.Response.OutputStream.Close(); } catch { }
        }
    }

    private async Task ServeZssFile(HttpListenerContext ctx, string filePath)
    {
        try
        {
            var css = Zest.Engine.Zcss.Processor.processFile(filePath);
            var cssBytes = Encoding.UTF8.GetBytes(css);
            ctx.Response.ContentType = "text/css; charset=utf-8";
            HttpResponseUtility.AddCorsHeaders(ctx.Response);
            ctx.Response.ContentLength64 = cssBytes.Length;
            await ctx.Response.OutputStream.WriteAsync(cssBytes);
            await ctx.Response.OutputStream.FlushAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("ZSS", $"Failed to compile {filePath}: {ex.Message}");
            // Fallback: serve .zss as-is if ZSS compilation fails
            await HttpResponseUtility.WriteFileResponse(ctx, filePath);
        }
    }

    private async Task AcceptWebSocketClients(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _wsListener!.AcceptTcpClientAsync().WaitAsync(ct);
                _ = Task.Run(() => HandleWsClient(client));
            }
            catch { break; }
        }
    }

    private async Task HandleWsClient(TcpClient tcpClient)
    {
        try
        {
            using var stream = tcpClient.GetStream();
            var buf = new byte[4096];
            var read = await stream.ReadAsync(buf, 0, buf.Length);
            var req = Encoding.UTF8.GetString(buf, 0, read);
            var keyMatch = Regex.Match(req, @"Sec-WebSocket-Key:\s*(.+)");
            if (!keyMatch.Success) return;

            var acceptKey = ComputeWebSocketAcceptKey(keyMatch.Groups[1].Value.Trim());
            var response = "HTTP/1.1 101 Switching Protocols\r\n" +
                          "Upgrade: websocket\r\n" +
                          "Connection: Upgrade\r\n" +
                          $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response));

            lock (_wsLock) _wsClients.Add(tcpClient);
            Logger.VerboseLog($"WebSocket client connected (total: {_wsClients.Count})");

            try
            {
                while (!_cts!.IsCancellationRequested)
                {
                    var frame = new byte[2];
                    var n = await stream.ReadAsync(frame, 0, 2, _cts.Token);
                    if (n < 2 || (frame[0] & 0x0F) == 0x08) break;
                }
            }
            catch { }
            finally { lock (_wsLock) _wsClients.Remove(tcpClient); }
        }
        catch { }
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
            Logger.VerboseLog($"Broadcast reload to {_wsClients.Count} clients");
        }
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

    private static string ComputeWebSocketAcceptKey(string key)
    {
        const string magic = "258EAFA5-E914-47DA-95CA-C5AB5E0285C2";
        using var sha1 = System.Security.Cryptography.SHA1.Create();
        return Convert.ToBase64String(sha1.ComputeHash(Encoding.UTF8.GetBytes(key + magic)));
    }

    private string GetLiveReloadScript() => $@"
<script>
(function(){{
    var port = {_config.LiveReloadPort};
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
                // Was once connected — a real disconnect, reload after delay
                setTimeout(function(){{ window.location.reload(); }}, 1000);
            }} else {{
                // Never connected — retry silently after a delay
                setTimeout(connect, 3000);
            }}
        }};
        ws.onerror = function() {{}};
    }}
    connect();
}})();
</script>";
}
