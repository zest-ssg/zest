using System.Net;
using System.Text;
using Zest.Compiler.Model;
using Zest.Compiler.Build;
using Zest.App.Config;

#nullable enable

namespace Zest.App.Runtime;

/// <summary>
/// Preview server — serves _site/ static files directly without an initial
/// build. Optionally supports file watching with auto-rebuild and live reload
/// via WebSocket + SSE fallback.
/// </summary>
public class PreviewServer : HttpServerBase
{
    private readonly SiteConfig _config;
    private readonly int _port;
    private readonly bool _watch;
    private readonly bool _liveReload;
    private string? _outputDir;
    private LiveReloadHub? _wsServer;
    private ContentWatcher? _fileWatcher;
    private readonly BuildDriver _buildDriver = new();
    private readonly object _rebuildLock = new();
    private long _rebuildCount;

    protected override string ServerName => "Preview";
    protected override int Port => _port;

    /// <summary>SSE is only exposed when live reload is requested.</summary>
    protected override bool EnableSse => _liveReload;

    public PreviewServer(SiteConfig config, int port, string host = "localhost", bool openBrowser = false,
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

    /// <summary>
    /// Resolve the output directory. Pure: creating it here would be a side
    /// effect hidden inside a getter, and the banner reads this before the
    /// server is really up.
    /// </summary>
    protected override string GetOutputDir()
    {
        _outputDir ??= Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            _config.OutputDir.TrimStart('.', '\\', '/')));
        return _outputDir;
    }

    protected override void OnStarted()
    {
        var outputDir = GetOutputDir();
        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        // Verify output directory has content
        if (!Directory.EnumerateFileSystemEntries(outputDir).Any())
        {
            LogWriter.Warn("Preview", $"Output directory '{outputDir}' is empty. Run 'zest build' first.");
        }

        // Set up live reload WebSocket server. A busy port is not fatal — the
        // injected script falls back to the SSE endpoint.
        if (_liveReload)
        {
            _wsServer = new LiveReloadHub(_config.LiveReloadPort);
            _wsServer.Start(Cts!);
        }

        // Set up file watcher + auto-rebuild
        if (_watch)
        {
            _fileWatcher = new ContentWatcher(
                Directory.GetCurrentDirectory(),
                outputDir,
                IgnoredDirNames!,
                cssOnly => Rebuild(cssOnly));

            // Force a full refresh at startup so the served site reflects the
            // latest sources even when the incremental cache thinks nothing changed.
            Rebuild(cssOnly: false, forceRefresh: true);
        }
    }

    protected override string? GetLiveReloadScript() => _wsServer?.GetLiveReloadScript();

    protected override async Task<bool> TryHandleSpecialFile(HttpListenerContext ctx, string filePath, string ext)
    {
        if (ext != FileTypes.Zcss) return false;

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
        return true;
    }

    protected override void OnShutdown()
    {
        _wsServer?.Stop();
        _fileWatcher?.Dispose();
        // Kill the long-running FSI child so it cannot keep the terminal
        // open after the preview process exits.
        Zest.Compiler.Execution.FsiSession.shutdown();

        LogWriter.Info($"Rebuilds: {_rebuildCount}");
    }

    private void Rebuild(bool cssOnly, bool forceRefresh = false)
    {
        lock (_rebuildLock)
        {
            if (forceRefresh)
            {
                // Startup refresh — wipe incremental caches so the build
                // regenerates every page instead of reusing previous output.
                IncrementalCache.clearDiskCache(GetOutputDir());
            }

            // Engine upgrade detection — consistent with DevServer behavior.
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

            // Reset in-process template caches.
            try { Zest.Compiler.Zestucks.EngineHost.clearCaches(); }
            catch { /* non-fatal */ }

            try
            {
                var result = _buildDriver.Execute(ReloadedConfig());
                // PrintResult stops the animator and prints summary + errors.
                BuildDriver.PrintResult(result, _config);

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
                LogWriter.Error("PreviewServer", $"Rebuild failed: {ex.Message}");
                if (ex.InnerException != null)
                    LogWriter.Error("PreviewServer", $"  → {ex.InnerException.Message}");
            }
        }
    }

    /// <summary>
    /// Re-read <c>_config.toml</c> so edits take effect without a restart.
    /// A broken config must not stop the server, so the previous instance is
    /// kept and the problem is reported on the next rebuild.
    /// </summary>
    private SiteConfig ReloadedConfig()
    {
        try
        {
            return ConfigLoader.Load();
        }
        catch (ConfigException ex)
        {
            LogWriter.Error("PreviewServer", $"Ignoring invalid _config.toml: {ex.Message}");
            return _config;
        }
    }
}
