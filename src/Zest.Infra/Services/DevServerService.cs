using System.Diagnostics;
using System.Net;
using System.Text;
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
    private CancellationTokenSource? _cts;
    private FileSystemWatcher? _watcher;
    private readonly BuildService _buildService = new();
    private readonly WebSocketServer _wsServer;
    private string? _outputDir;
    private long _totalRequests;
    private long _rebuildCount;

    // Keep debounce timer as field to prevent GC from collecting it
    private System.Timers.Timer? _debounceTimer;
    // Prevent concurrent rebuilds
    private readonly object _rebuildLock = new();

    public int Port => _config.DevServerPort;
    public string Host => _host;

    public DevServerService(SiteConfig config, string host = "localhost", bool openBrowser = false)
    {
        _config = config;
        _host = host;
        _openBrowser = openBrowser;
        _wsServer = new WebSocketServer(config.LiveReloadPort);
    }

    public void Start()
    {
        _cts = new();

        _outputDir = Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            _config.OutputDir.TrimStart('.', '\\', '/')));

        // Initial build
        var result = _buildService.Execute(_config);
        BuildService.PrintResult(result, _config);

        // HTTP server
        _listener = new();
        _listener.Prefixes.Add($"http://{_host}:{_config.DevServerPort}/");
        _listener.Start();
        _ = Task.Run(() => ServeHttp(_cts.Token));

        // WebSocket server for live reload
        _wsServer.Start(_cts);

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

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  Press Ctrl+C to stop.");
        Console.ResetColor();

        StartFileWatcher();
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _wsServer.Stop();
        _watcher?.Dispose();
        _debounceTimer?.Dispose();

        Logger.Info($"Total requests: {_totalRequests}, rebuilds: {_rebuildCount}");
    }

    public void Dispose() => Stop();

    /// Directories whose contents should NOT trigger rebuilds.
    private static readonly HashSet<string> IgnoredDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "_site", ".git", ".svn", ".hg",
        "bin", "obj", "node_modules", "packages", ".vs"
    };

    private void StartFileWatcher()
    {
        var watchDir = Directory.GetCurrentDirectory();
        _watcher = new(watchDir, "*.*")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime
        };

        _debounceTimer = new System.Timers.Timer(300) { AutoReset = false };
        _debounceTimer.Elapsed += (_, _) => Rebuild();

        void OnChange(object sender, FileSystemEventArgs e)
        {
            // Skip changes in the output directory
            if (_outputDir != null && e.FullPath.StartsWith(_outputDir, StringComparison.OrdinalIgnoreCase))
                return;

            // Check each directory component — only skip known ignore-worthy dirs
            var parts = e.FullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            for (int i = 0; i < parts.Length - 1; i++)
            {
                var p = parts[i];
                // Only check the LAST directory component (the file's parent dir)
                if (i == parts.Length - 2)
                {
                    if (IgnoredDirNames.Contains(p))
                        return;
                }
                else if (p.StartsWith("."))
                {
                    // Only skip hidden directories (starting with .)
                    return;
                }
            }

            var ext = Path.GetExtension(e.Name!)?.ToLowerInvariant() ?? "";
            if (!WatchConstants.Extensions.Contains(ext)) return;

            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        _watcher.Changed += OnChange;
        _watcher.Created += OnChange;
        _watcher.Deleted += OnChange;
        _watcher.Renamed += (_, _) => { _debounceTimer.Stop(); _debounceTimer.Start(); };
        _watcher.EnableRaisingEvents = true;
    }

    private void Rebuild()
    {
        lock (_rebuildLock)
        {
            try
            {
                var result = _buildService.Execute(_config);
                BuildService.PrintResult(result, _config);

                if (result.Errors.Length > 0)
                {
                    foreach (var err in result.Errors)
                        Logger.Error("Build", err);
                }

                Interlocked.Increment(ref _rebuildCount);

                // Small delay to ensure all file writes are flushed to disk
                // before the browser reloads and requests the updated files
                Thread.Sleep(100);

                _wsServer.BroadcastReload();
            }
            catch (Exception ex)
            {
                Logger.Error("DevServer", $"Rebuild failed: {ex.Message}");
            }
        }
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
            if (method == "OPTIONS")
            {
                ctx.Response.StatusCode = 204;
                HttpResponseHelper.AddCorsHeaders(ctx.Response);
                ctx.Response.OutputStream.Close();
                sw.Stop();
                Logger.Request(method, urlPath, 204, sw.ElapsedMilliseconds);
                return;
            }

            var outputDir = _outputDir!;

            string filePath;
            try
            {
                filePath = FilePathResolver.ResolveFilePath(outputDir, urlPath);
            }
            catch (UnauthorizedAccessException)
            {
                ctx.Response.StatusCode = 403;
                await HttpResponseHelper.WriteStringResponse(ctx, 403, "<h1>403 — Forbidden</h1>");
                sw.Stop();
                Logger.Request(method, urlPath, 403, sw.ElapsedMilliseconds);
                Logger.Warn("Security", $"Path traversal blocked: {urlPath}");
                return;
            }

            if (!File.Exists(filePath))
            {
                await NotFoundResponse.WriteNotFound(ctx, outputDir, urlPath);
                sw.Stop();
                Logger.Request(method, urlPath, 404, sw.ElapsedMilliseconds);
                return;
            }

            var ext = Path.GetExtension(filePath).ToLowerInvariant();

            if (ext == ".zcss")
            {
                await ServeZssFile(ctx, filePath);
                sw.Stop();
                Logger.Request(method, urlPath, 200, sw.ElapsedMilliseconds);
                return;
            }

            var bytes = await File.ReadAllBytesAsync(filePath);

            if (ext == ".html")
            {
                var html = Encoding.UTF8.GetString(bytes);
                var script = _wsServer.GetLiveReloadScript();
                html = html.Replace("</body>", script + "\n</body>");
                if (!html.Contains("</body>"))
                    html += script;

                ctx.Response.ContentType = "text/html; charset=utf-8";
                HttpResponseHelper.AddCorsHeaders(ctx.Response);
                bytes = Encoding.UTF8.GetBytes(html);
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes);
            }
            else
            {
                ctx.Response.ContentType = MimeTypeMap.GetMimeType(filePath);
                HttpResponseHelper.AddCorsHeaders(ctx.Response);
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
                await HttpResponseHelper.WriteStringResponse(ctx, 500,
                    $"<h1>500 — Internal Server Error</h1><p>{WebUtility.HtmlEncode(ex.Message)}</p>");
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

    private static async Task ServeZssFile(HttpListenerContext ctx, string filePath)
    {
        try
        {
            var css = Zest.Engine.Zcss.Processor.processFile(filePath);
            var cssBytes = Encoding.UTF8.GetBytes(css);
            ctx.Response.ContentType = "text/css; charset=utf-8";
            HttpResponseHelper.AddCorsHeaders(ctx.Response);
            ctx.Response.ContentLength64 = cssBytes.Length;
            await ctx.Response.OutputStream.WriteAsync(cssBytes);
            await ctx.Response.OutputStream.FlushAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("ZSS", $"Failed to compile {filePath}: {ex.Message}");
            await HttpResponseHelper.WriteFileResponseAsync(ctx, filePath);
        }
    }
}
