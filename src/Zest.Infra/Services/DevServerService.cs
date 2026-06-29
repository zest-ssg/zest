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
public class DevServerService : HttpServerBase
{
    private readonly SiteConfig _config;
    private readonly BuildService _buildService = new();
    private readonly WebSocketServer _wsServer;
    private string? _outputDir;
    private FileSystemWatcher? _watcher;
    private long _rebuildCount;

    // Keep debounce timer as field to prevent GC from collecting it
    private System.Timers.Timer? _debounceTimer;
    // Prevent concurrent rebuilds
    private readonly object _rebuildLock = new();

    protected override string ServerName => "Development";
    protected override int Port => _config.DevServerPort;

    public DevServerService(SiteConfig config, string host = "localhost", bool openBrowser = false)
        : base(host, openBrowser)
    {
        _config = config;
        _wsServer = new WebSocketServer(config.LiveReloadPort);
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
        _wsServer.Start(_cts!);

        StartFileWatcher();
    }

    protected override string? GetLiveReloadScript() => _wsServer.GetLiveReloadScript();

    protected override async Task<bool> TryHandleSpecialFile(HttpListenerContext ctx, string filePath, string ext)
    {
        if (ext != ".zcss") return false;

        await ServeZcssFile(ctx, filePath);
        return true;
    }

    public override void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _wsServer.Stop();
        _watcher?.Dispose();
        _debounceTimer?.Dispose();

        Logger.Info($"Total requests: {_totalRequests}, rebuilds: {_rebuildCount}");
    }

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

                _wsServer.BroadcastReload();
            }
            catch (Exception ex)
            {
                Logger.Error("DevServer", $"Rebuild failed: {ex.Message}");
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
            HttpResponseHelper.AddCorsHeaders(ctx.Response);
            ctx.Response.ContentLength64 = cssBytes.Length;
            await ctx.Response.OutputStream.WriteAsync(cssBytes);
            await ctx.Response.OutputStream.FlushAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("ZCSS", $"Failed to compile {filePath}: {ex.Message}");
            await HttpResponseHelper.WriteFileResponseAsync(ctx, filePath);
        }
    }
}
