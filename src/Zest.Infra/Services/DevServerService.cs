using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Zest.Engine;

namespace Zest.Infra.Services;

/// <summary>
/// Development HTTP server with live-reload via WebSocket.
/// Monitors file changes, triggers rebuilds via F# BuildEngine,
/// and broadcasts reload to all connected browsers.
/// </summary>
public class DevServerService : IDisposable
{
    private readonly SiteConfig _config;
    private HttpListener? _listener;
    private TcpListener? _wsListener;
    private readonly List<TcpClient> _wsClients = new();
    private readonly object _wsLock = new();
    private CancellationTokenSource? _cts;
    private FileSystemWatcher? _watcher;
    private readonly BuildService _buildService = new();
    private DateTime _lastBuildTime;
    private int _totalBuilds;

    public int Port => _config.DevServerPort;
    public int TotalBuilds => _totalBuilds;
    public DateTime LastBuildTime => _lastBuildTime;

    public DevServerService(SiteConfig config) => _config = config;

    public void Start()
    {
        _cts = new();

        // Initial build
        Console.WriteLine("[Zest] Building site for development...");
        var result = _buildService.Execute(_config);
        _lastBuildTime = DateTime.Now;
        _totalBuilds = 1;
        BuildService.PrintResult(result, _config);

        // HTTP server
        _listener = new();
        _listener.Prefixes.Add($"http://localhost:{_config.DevServerPort}/");
        _listener.Start();
        _ = Task.Run(() => ServeHttp(_cts.Token));

        // WebSocket server for live reload
        _wsListener = new(IPAddress.Loopback, _config.LiveReloadPort);
        _wsListener.Start();
        _ = Task.Run(() => AcceptWebSocketClients(_cts.Token));

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"[Zest] Development server running at http://localhost:{_config.DevServerPort}");
        Console.ResetColor();
        Console.WriteLine("       Press Ctrl+C to stop.");

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
            // Skip output directory changes
            if (e.FullPath.Contains($"\\{_config.OutputDir.TrimStart('.', '\\', '/')}\\") ||
                e.FullPath.Contains($"/{_config.OutputDir.TrimStart('.', '/')}/"))
                return;

            var ext = Path.GetExtension(e.Name).ToLowerInvariant();
            var relevant = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".fsx", ".zest.fsx", ".md", ".markdown",
                ".html", ".css", ".zss", ".js", ".toml", ".json", ".yaml", ".yml",
                ".png", ".jpg", ".jpeg", ".svg", ".gif"
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
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("[Zest] Change detected, rebuilding...");
        Console.ResetColor();

        var sw = Stopwatch.StartNew();
        var result = _buildService.Execute(_config);
        sw.Stop();

        _lastBuildTime = DateTime.Now;
        _totalBuilds++;

        BuildService.PrintResult(result, _config);
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
        try
        {
            var outputDir = Path.GetFullPath(Path.Combine(
                Directory.GetCurrentDirectory(), _config.OutputDir.TrimStart('.', '\\', '/')));

            var filePath = ResolveFilePath(outputDir, ctx.Request.Url?.AbsolutePath ?? "/");

            if (!File.Exists(filePath))
            {
                ctx.Response.StatusCode = 404;
                var msg = Encoding.UTF8.GetBytes("<h1>404 - Not Found</h1>");
                ctx.Response.ContentType = "text/html; charset=utf-8";
                ctx.Response.ContentLength64 = msg.Length;
                await ctx.Response.OutputStream.WriteAsync(msg);
            }
            else
            {
                var ext = Path.GetExtension(filePath).ToLowerInvariant();

                // Handle .zss files: compile to CSS on-the-fly for dev server
                if (ext == ".zss")
                {
                    try
                    {
                        // Check if .zss file exists in the assets directory
                        var assetsDir = Path.GetFullPath(Path.Combine(
                            Directory.GetCurrentDirectory(), _config.AssetsDir.TrimStart('.', '\\', '/')));
                        var zssPath = Path.Combine(assetsDir, ctx.Request.Url?.AbsolutePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar) ?? "");

                        if (File.Exists(zssPath))
                        {
                            var css = Zest.Engine.Zss.Processor.processFile(zssPath);
                            var cssBytes = Encoding.UTF8.GetBytes(css);
                            ctx.Response.ContentType = "text/css; charset=utf-8";
                            ctx.Response.ContentLength64 = cssBytes.Length;
                            await ctx.Response.OutputStream.WriteAsync(cssBytes);
                            return;
                        }
                    }
                    catch { }
                }

                ctx.Response.ContentType = ext switch
                {
                    ".html" => "text/html; charset=utf-8",
                    ".css" => "text/css; charset=utf-8",
                    ".zss" => "text/css; charset=utf-8",
                    ".js" => "application/javascript; charset=utf-8",
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".gif" => "image/gif",
                    ".svg" => "image/svg+xml",
                    ".ico" => "image/x-icon",
                    ".woff" => "font/woff",
                    ".woff2" => "font/woff2",
                    ".json" => "application/json",
                    _ => "application/octet-stream"
                };

                var bytes = await File.ReadAllBytesAsync(filePath);

                if (ext == ".html")
                {
                    var html = Encoding.UTF8.GetString(bytes);
                    html = html.Replace("</body>", GetLiveReloadScript() + "\n</body>");
                    bytes = Encoding.UTF8.GetBytes(html);
                }

                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes);
            }
        }
        catch { }
        finally { ctx.Response.OutputStream.Close(); }
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
        }
    }

    private static byte[] EncodeWebSocketFrame(string text)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        var frame = new byte[payload.Length + 2];
        frame[0] = 0x81;
        frame[1] = (byte)payload.Length;
        Array.Copy(payload, 0, frame, 2, payload.Length);
        return frame;
    }

    private static string ComputeWebSocketAcceptKey(string key)
    {
        const string magic = "258EAFA5-E914-47DA-95CA-5AB5E0285C";
        using var sha1 = System.Security.Cryptography.SHA1.Create();
        return Convert.ToBase64String(sha1.ComputeHash(Encoding.UTF8.GetBytes(key + magic)));
    }

    private string GetLiveReloadScript() => $@"
<script>
(function(){{
    var ws = new WebSocket('ws://localhost:{_config.LiveReloadPort}/livereload');
    ws.onmessage = function(e) {{ if (e.data === 'reload') window.location.reload(); }};
    ws.onclose = function() {{ setTimeout(function(){{ window.location.reload(); }}, 2000); }};
}})();
</script>";

    /// <summary>
    /// Resolve a URL path to a physical file path, handling directory-style routes.
    /// e.g. "/guide/" → "/guide/index.html", "/guide" → "/guide/index.html"
    /// </summary>
    private static string ResolveFilePath(string outputDir, string urlPath)
    {
        if (urlPath == "/") urlPath = "/index.html";

        var relative = urlPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);

        if (relative.EndsWith(Path.DirectorySeparatorChar))
            relative = relative + "index.html";

        var fullPath = Path.Combine(outputDir, relative);

        if (!File.Exists(fullPath) && string.IsNullOrEmpty(Path.GetExtension(relative)))
            fullPath = Path.Combine(outputDir, relative, "index.html");

        return fullPath;
    }
}
