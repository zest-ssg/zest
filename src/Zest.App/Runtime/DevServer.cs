using System.Net;
using System.Text;
using Zest.Compiler.Model;
using Zest.Compiler.Build;

#nullable enable

namespace Zest.App.Runtime;

/// <summary>
/// Development HTTP server with initial build, file watching, incremental
/// rebuild, and live-reload via WebSocket + SSE fallback.
/// </summary>
public class DevServer : HttpServerBase
{
    private readonly SiteConfig _config;
    private readonly BuildDriver _buildDriver = new();
    private readonly LiveReloadHub _wsServer;
    private string? _outputDir;
    private ContentWatcher? _fileWatcher;
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
        _wsServer = new LiveReloadHub(config.LiveReloadPort);
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
        var result = _buildDriver.Execute(_config, forceRefresh: true);
        BuildDriver.PrintResult(result, _config);

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
        if (ext != FileTypes.Zcss) return false;
        await ServeZcssFile(ctx, filePath);
        return true;
    }

    public override void Shutdown()
    {
        // Base cancels the listener and waits for in-flight requests.
        base.Shutdown();
        _wsServer.Stop();
        _fileWatcher?.Dispose();
        // Kill the long-running FSI child so it cannot keep the terminal
        // open after the serve process exits.
        Zest.Compiler.Execution.FsiSession.shutdown();

        lock (_sseLock)
        {
            foreach (var s in _sseClients)
            {
                try { s.Close(); } catch { }
            }
            _sseClients.Clear();
        }

        LogWriter.Info($"Rebuilds: {_rebuildCount}");
    }

    private void StartFileWatcher()
    {
        _fileWatcher = new ContentWatcher(
            Directory.GetCurrentDirectory(),
            GetOutputDir(),
            IgnoredDirNames!,
            cssOnly => Rebuild(cssOnly));
    }

    private void Rebuild(bool cssOnly)
    {
        lock (_rebuildLock)
        {
            // Engine upgrade detection — if Zest.Compiler.dll was replaced
            // mid-serve, clear caches and force a full rebuild.
            if (IncrementalCache.hasEngineChanged())
            {
                LogWriter.WriteDim("  [Zest] Engine changed — forcing full rebuild.");
                IncrementalCache.clearCache();
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
            try { Zest.Compiler.Zealucks.EngineHost.clearCaches(); }
            catch { /* non-fatal */ }

            try
            {
                var result = _buildDriver.Execute(_config);
                // PrintResult stops the animator and prints summary + errors.
                BuildDriver.PrintResult(result, _config);

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
            var css = Zest.Compiler.Zcss.Zcss.processFile(filePath);
            var cssBytes = Encoding.UTF8.GetBytes(css);
            ctx.Response.ContentType = "text/css; charset=utf-8";
            HttpResponses.AddCorsHeaders(ctx.Response);
            ctx.Response.ContentLength64 = cssBytes.Length;
            await ctx.Response.OutputStream.WriteAsync(cssBytes);
            await ctx.Response.OutputStream.FlushAsync();
        }
        catch (Exception ex)
        {
            LogWriter.Error("ZCSS", $"Failed to compile {filePath}: {ex.Message}");
            await HttpResponses.WriteFileResponseAsync(ctx, filePath);
        }
    }

    // ── SSE (Server-Sent Events) fallback ──

    private async Task HandleSseConnection(HttpListenerContext ctx)
    {
        var response = ctx.Response;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers["Cache-Control"] = "no-cache";
        response.Headers["Connection"] = "keep-alive";
        HttpResponses.AddCorsHeaders(response);
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
        // Snapshot under the lock, write outside it — a stalled SSE client
        // must never block the rebuild loop.
        Stream[] snapshot;
        lock (_sseLock)
        {
            if (_sseClients.Count == 0) return;
            snapshot = _sseClients.ToArray();
        }

        var payload = Encoding.UTF8.GetBytes($"data: {jsonData}\n\n");
        _ = Task.Run(() =>
        {
            var dead = new List<Stream>();
            foreach (var s in snapshot)
            {
                try
                {
                    s.Write(payload, 0, payload.Length);
                    s.Flush();
                }
                catch { dead.Add(s); }
            }

            if (dead.Count > 0)
            {
                lock (_sseLock)
                {
                    foreach (var s in dead) _sseClients.Remove(s);
                }
            }
        });
    }
}
