using System.Net;
using System.Text;
using Zest.Engine;

#nullable enable

namespace Zest.Infra.Services;

/// <summary>
/// Preview server — serves _site/ static files directly without an initial
/// build. Optionally supports file watching with auto-rebuild and live reload
/// via WebSocket + SSE fallback.
/// </summary>
public class PreviewService : HttpServer
{
    private readonly SiteConfig _config;
    private readonly int _port;
    private readonly bool _watch;
    private readonly bool _liveReload;
    private string? _outputDir;
    private SocketHub? _wsServer;
    private FileWatchController? _fileWatcher;
    private readonly BuildService _buildService = new();
    private readonly object _rebuildLock = new();
    private long _rebuildCount;

    // SSE fallback for environments where WebSocket is blocked
    private readonly List<Stream> _sseClients = new();
    private readonly object _sseLock = new();

    protected override string ServerName => "Preview";
    protected override int Port => _port;

    public PreviewService(SiteConfig config, int port, string host = "localhost", bool openBrowser = false,
        bool watch = false, bool liveReload = false, bool spaFallback = false, bool dirListing = false)
        : base(host, openBrowser)
    {
        _config = config;
        IgnoredDirNames = ExcludedPaths.For(config);
        _port = port;
        _watch = watch;
        _liveReload = liveReload;
        EnableSpaFallback = spaFallback;
        EnableDirectoryListing = dirListing;
    }

    protected override string GetOutputDir()
    {
        if (_outputDir != null) return _outputDir;

        _outputDir = Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            _config.OutputDir.TrimStart('.', '\\', '/')));

        if (!Directory.Exists(_outputDir))
            Directory.CreateDirectory(_outputDir);

        return _outputDir;
    }

    protected override void OnStarted()
    {
        var outputDir = GetOutputDir();

        // Verify output directory has content
        if (!Directory.EnumerateFileSystemEntries(outputDir).Any())
        {
            LogWriter.Warn("Preview", $"Output directory '{outputDir}' is empty. Run 'zest build' first.");
        }

        // Set up live reload WebSocket server
        if (_liveReload)
        {
            _wsServer = new SocketHub(_config.LiveReloadPort);
            _wsServer.Start(Cts!);
        }

        // Set up file watcher + auto-rebuild
        if (_watch)
        {
            _fileWatcher = new FileWatchController(
                Directory.GetCurrentDirectory(),
                outputDir,
                IgnoredDirNames!,
                cssOnly => Rebuild(cssOnly));
        }
    }

    protected override string? GetLiveReloadScript() => _wsServer?.GetLiveReloadScript();

    protected override async Task<bool> TryHandleVirtualPath(HttpListenerContext ctx, string urlPath)
    {
        if (urlPath != "/__zest_livereload_events" || !_liveReload) return false;
        await HandleSseConnection(ctx);
        return true;
    }

    protected override async Task<bool> TryHandleSpecialFile(HttpListenerContext ctx, string filePath, string ext)
    {
        if (ext != FileExtensions.Zcss) return false;

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
        return true;
    }

    public override void Shutdown()
    {
        Cts?.Cancel();
        Listener?.Stop();
        _wsServer?.Stop();
        _fileWatcher?.Dispose();

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

    private void Rebuild(bool cssOnly)
    {
        lock (_rebuildLock)
        {
            // Engine upgrade detection — consistent with DevServer behavior.
            if (BuildCache.hasEngineChanged())
            {
                LogWriter.WriteDim("  [Zest] Engine changed — forcing full rebuild.");
                BuildCache.clearCache();
            }

            // Output directory resilience — recreate if deleted externally.
            var outDir = GetOutputDir();
            if (!Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
                LogWriter.WriteDim("  [Zest] Output directory recreated.");
            }

            // Reset in-process template caches.
            try { Zest.Engine.Template.TemplateManager.clearCaches(); }
            catch { /* non-fatal */ }

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

                if (_liveReload && _wsServer != null)
                {
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
            }
            catch (Exception ex)
            {
                LogWriter.Error("PreviewService", $"Rebuild failed: {ex.Message}");
                if (ex.InnerException != null)
                    LogWriter.Error("PreviewService", $"  → {ex.InnerException.Message}");
            }
        }
    }

    // ── SSE (Server-Sent Events) fallback ──

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
            var initBytes = Encoding.UTF8.GetBytes(": connected\n\n");
            await stream.WriteAsync(initBytes);
            await stream.FlushAsync();

            while (Cts is { IsCancellationRequested: false })
            {
                await Task.Delay(15_000, Cts.Token);
                var keepalive = Encoding.UTF8.GetBytes(": keepalive\n\n");
                await stream.WriteAsync(keepalive);
                await stream.FlushAsync();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            LogWriter.VerboseLog($"SSE client disconnected: {ex.Message}");
        }
        finally
        {
            lock (_sseLock) _sseClients.Remove(stream);
            try { stream.Close(); } catch { }
        }
    }

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
