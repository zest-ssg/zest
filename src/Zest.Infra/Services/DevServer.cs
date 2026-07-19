using System.Net;
using System.Text;
using Zest.Engine;

#nullable enable

namespace Zest.Infra.Services;

/// <summary>
/// Development HTTP server with live-reload via WebSocket.
/// Monitors file changes, triggers rebuilds via F# BuildEngine,
/// and broadcasts reload to all connected browsers.
/// </summary>
public class DevServer : HttpServer
{
    private readonly SiteConfig _config;
    private readonly BuildService _buildService = new();
    private readonly SocketHub _wsServer;
    private string? _outputDir;
    private FileSystemWatcher? _watcher;
    private long _rebuildCount;

    // SSE fallback for environments where WebSocket is blocked
    private readonly List<Stream> _sseClients = new();
    private readonly object _sseLock = new();

    // Keep debounce timer as field to prevent GC from collecting it
    private System.Timers.Timer? _debounceTimer;
    // Prevent concurrent rebuilds
    private readonly object _rebuildLock = new();
    // Track whether the current change batch is CSS-only (for style injection vs full reload)
    private bool _cssOnlyChanges = true;
    private readonly object _changeLock = new();

    protected override string ServerName => "Development";
    protected override int Port => _config.DevServerPort;

    public DevServer(SiteConfig config, string host = "localhost", bool openBrowser = false, bool spaFallback = false, bool dirListing = false)
        : base(host, openBrowser)
    {
        _config = config;
        _wsServer = new SocketHub(config.LiveReloadPort);
        EnableSpaFallback = spaFallback;
        EnableDirectoryListing = dirListing;
    }

    protected override string GetOutputDir()
    {
        _outputDir ??= Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            _config.OutputDir.TrimStart('.', '\\', '/')));
        return _outputDir;
    }

    protected override void OnStarted()
    {
        _outputDir = GetOutputDir();

        // Initial build
        var result = _buildService.Execute(_config);
        BuildService.PrintResult(result, _config);

        // WebSocket server for live reload
        _wsServer.Start(Cts!);

        StartFileWatcher();
    }

    protected override string? GetLiveReloadScript() => _wsServer.GetLiveReloadScript();

    protected override async Task<bool> TryHandleVirtualPath(HttpListenerContext ctx, string urlPath)
    {
        if (urlPath != "/__zest_livereload_events") return false;

        await HandleSseConnection(ctx);
        return true;
    }

    protected override async Task<bool> TryHandleSpecialFile(HttpListenerContext ctx, string filePath, string ext)
    {
        if (ext != FileExtensions.Zcss) return false;

        await ServeZcssFile(ctx, filePath);
        return true;
    }

    public override void Shutdown()
    {
        Cts?.Cancel();
        Listener?.Stop();
        _wsServer.Stop();
        _watcher?.Dispose();
        _debounceTimer?.Dispose();

        // Close all SSE connections
        lock (_sseLock)
        {
            foreach (var s in _sseClients)
            {
                try { s.Close(); } catch { }
            }
            _sseClients.Clear();
        }

        LogWriter.Info($"Total requests: {TotalRequests}, rebuilds: {_rebuildCount}");
    }

    /// Directories whose contents should NOT trigger rebuilds.
    /// Initialized from <see cref="ExcludedPaths.For"/> in the constructor so
    /// the configured <c>OutputDir</c> (default <c>_site</c>) is respected.
    private readonly HashSet<string> _ignoredDirNames;

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
            // Guard against null args / disposed state during shutdown.
            // FileSystemWatcher can fire with null EventArgs in rare races, and
            // the debounce timer may still be pending when the server stops.
            // Fixes MIGRATION_NOTES §1.9 (NullReferenceException in OnChange).
            if (e == null || _debounceTimer == null || _watcher == null) return;
            if (string.IsNullOrEmpty(e.FullPath)) return;

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
                    if (_ignoredDirNames.Contains(p))
                        return;
                }
                else if (p.StartsWith('.'))
                {
                    // Only skip hidden directories (starting with .)
                    return;
                }
            }

            var ext = (e.Name != null ? Path.GetExtension(e.Name) : null)?.ToLowerInvariant() ?? "";
            if (!WatchConstants.Extensions.Contains(ext)) return;

            // Track whether this change batch is CSS-only
            var isCss = ext is FileExtensions.Css or FileExtensions.Zcss;
            lock (_changeLock)
            {
                if (!isCss) _cssOnlyChanges = false;
            }

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
        // Guard against rebuild firing after the server has been disposed
        // (timer callback races during shutdown). Fixes MIGRATION_NOTES §1.9.
        if (_buildService == null || _config == null || _wsServer == null) return;
        lock (_rebuildLock)
        {
            try
            {
                var result = _buildService.Execute(_config);
                BuildService.PrintResult(result, _config);

                if (result.Errors.Length > 0)
                {
                    foreach (var err in result.Errors)
                        LogWriter.Error("Build", err);
                }

                Interlocked.Increment(ref _rebuildCount);

                // Choose broadcast type based on changed file types
                bool cssOnly;
                lock (_changeLock)
                {
                    cssOnly = _cssOnlyChanges;
                    _cssOnlyChanges = true; // reset for next batch
                }

                if (cssOnly)
                {
                    _wsServer.BroadcastStyleUpdate();
                    BroadcastSse("{\"type\":\"style\"}");
                }
                else
                {
                    _wsServer.BroadcastReload();
                    BroadcastSse("{\"type\":\"reload\"}");
                }
            }
            catch (Exception ex)
            {
                LogWriter.Error("DevServer", $"Rebuild failed: {ex.Message}");
            }
        }
    }

    private static async Task ServeZcssFile(HttpListenerContext ctx, string filePath)
    {
        try
        {
            var css = Zest.Engine.Zcss.Processor.processFile(filePath);
            var cssBytes = Encoding.UTF8.GetBytes(css);
            ctx.Response.ContentType = "text/css; charset=utf-8";
            HttpHelper.AddCorsHeaders(ctx.Response);
            ctx.Response.ContentLength64 = cssBytes.Length;
            await ctx.Response.OutputStream.WriteAsync(cssBytes);
            await ctx.Response.OutputStream.FlushAsync();
        }
        catch (Exception ex)
        {
            LogWriter.Error("ZCSS", $"Failed to compile {filePath}: {ex.Message}");
            await HttpHelper.WriteFileResponseAsync(ctx, filePath);
        }
    }

    /// <summary>
    /// Handle an SSE (Server-Sent Events) connection for live reload.
    /// Keeps the connection open and streams reload events.
    /// </summary>
    private async Task HandleSseConnection(HttpListenerContext ctx)
    {
        var response = ctx.Response;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers["Cache-Control"] = "no-cache";
        response.Headers["Connection"] = "keep-alive";
        HttpHelper.AddCorsHeaders(response);
        response.SendChunked = true;

        var stream = response.OutputStream;

        lock (_sseLock) _sseClients.Add(stream);
        LogWriter.VerboseLog($"SSE client connected (total: {_sseClients.Count})");

        try
        {
            // Send initial comment to establish connection
            var initBytes = Encoding.UTF8.GetBytes(": connected\n\n");
            await stream.WriteAsync(initBytes);
            await stream.FlushAsync();

            // Keep connection alive until cancelled
            while (Cts is { IsCancellationRequested: false })
            {
                await Task.Delay(15_000, Cts.Token);
                // Send keepalive comment
                var keepalive = Encoding.UTF8.GetBytes(": keepalive\n\n");
                await stream.WriteAsync(keepalive);
                await stream.FlushAsync();
            }
        }
        catch { }
        finally
        {
            lock (_sseLock) _sseClients.Remove(stream);
            try { stream.Close(); } catch { }
        }
    }

    /// <summary>
    /// Broadcast an SSE event to all connected SSE clients.
    /// </summary>
    private void BroadcastSse(string jsonData)
    {
        lock (_sseLock)
        {
            if (_sseClients.Count == 0) return;

            var payload = Encoding.UTF8.GetBytes($"data: {jsonData}\n\n");
            var dead = new List<Stream>();

            foreach (var s in _sseClients)
            {
                try
                {
                    s.Write(payload, 0, payload.Length);
                    s.Flush();
                }
                catch { dead.Add(s); }
            }

            foreach (var s in dead) _sseClients.Remove(s);
        }
    }
}
