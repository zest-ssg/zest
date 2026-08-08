using System.Net;
using System.Text;
using Zest.Engine;

#nullable enable

namespace Zest.Infra.Services;

/// <summary>
/// Development HTTP server with initial build, file watching, incremental
/// rebuild, and live-reload via WebSocket + SSE fallback.
/// </summary>
public class DevServer : HttpServer
{
    private readonly SiteConfig _config;
    private readonly BuildService _buildService = new();
    private readonly SocketHub _wsServer;
    private string? _outputDir;
    private FileWatchController? _fileWatcher;
    private long _rebuildCount;
    private readonly object _rebuildLock = new();

    // SSE fallback for environments where WebSocket is blocked
    private readonly List<Stream> _sseClients = new();
    private readonly object _sseLock = new();

    protected override string ServerName => "Development";
    protected override int Port => _config.DevServerPort;

    public DevServer(SiteConfig config, string host = "localhost", bool openBrowser = false,
        bool spaFallback = false, bool dirListing = false)
        : base(host, openBrowser)
    {
        _config = config;
        _wsServer = new SocketHub(config.LiveReloadPort);
        IgnoredDirNames = ExcludedPaths.For(config);
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
        StartFileWatcher();

        // Initial build — force a full refresh so a freshly started server
        // never serves pages skipped by the incremental cache from a prior run.
        var result = _buildService.Execute(_config, forceRefresh: true);
        BuildService.PrintResult(result, _config);

        // WebSocket server for live reload
        _wsServer.Start(Cts!);
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

    private void StartFileWatcher()
    {
        _fileWatcher = new FileWatchController(
            Directory.GetCurrentDirectory(),
            GetOutputDir(),
            IgnoredDirNames!,
            cssOnly => Rebuild(cssOnly));
    }

    private void Rebuild(bool cssOnly)
    {
        lock (_rebuildLock)
        {
            // Engine upgrade detection — if Zest.Engine.dll was replaced
            // mid-serve, clear caches and force a full rebuild.
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

            // Reset in-process template caches so layout/include changes
            // are picked up immediately.
            try { Zest.Engine.Template.TemplateManager.clearCaches(); }
            catch { /* non-fatal */ }

            try
            {
                var result = _buildService.Execute(_config);
                // PrintResult stops the animator and prints summary + errors.
                BuildService.PrintResult(result, _config);

                Interlocked.Increment(ref _rebuildCount);

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
                if (ex.InnerException != null)
                    LogWriter.Error("DevServer", $"  → {ex.InnerException.Message}");
                // Keep server alive — next file-save triggers another rebuild.
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
