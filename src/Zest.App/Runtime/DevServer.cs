using System.Net;
using System.Text;
using Zest.Compiler.Model;
using Zest.Compiler.Build;
using Zest.App.Config;

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

    protected override string ServerName => "Development";
    protected override int Port => _config.DevServerPort;

    /// <summary>Live reload always has the SSE fallback available.</summary>
    protected override bool EnableSse => true;

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

        // WebSocket server for live reload. A busy port is not fatal: the
        // injected script falls back to the SSE endpoint above.
        _wsServer.Start(Cts!);
    }

    protected override string? GetLiveReloadScript() => _wsServer.GetLiveReloadScript();

    protected override async Task<bool> TryHandleSpecialFile(HttpListenerContext ctx, string filePath, string ext)
    {
        if (ext != FileTypes.Zcss) return false;
        await ServeZcssFile(ctx, filePath);
        return true;
    }

    protected override void OnShutdown()
    {
        _wsServer.Stop();
        _fileWatcher?.Dispose();
        // Kill the long-running FSI child so it cannot keep the terminal
        // open after the serve process exits.
        Zest.Compiler.Execution.FsiSession.shutdown();

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
            try { Zest.Compiler.Zestucks.EngineHost.clearCaches(); }
            catch { /* non-fatal */ }

            try
            {
                var result = _buildDriver.Execute(ReloadedConfig());
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
            LogWriter.Error("DevServer", $"Ignoring invalid _config.toml: {ex.Message}");
            return _config;
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
}
