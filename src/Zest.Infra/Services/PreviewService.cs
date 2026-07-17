using Zest.Engine;

#nullable enable

namespace Zest.Infra.Services;

/// <summary>
/// Preview server — serves _site/ static files directly, no initial build triggered.
/// Optionally supports file watching with auto-rebuild and live reload via WebSocket.
/// </summary>
public class PreviewService : HttpServer
{
    private readonly SiteConfig _config;
    private readonly int _port;
    private readonly bool _watch;
    private readonly bool _liveReload;
    private string? _outputDir;
    private SocketHub? _wsServer;
    private FileSystemWatcher? _watcher;
    private System.Timers.Timer? _debounceTimer;
    private readonly BuildService _buildService = new();
    private readonly object _rebuildLock = new();
    private long _rebuildCount;
    private bool _cssOnlyChanges = true;
    private readonly object _changeLock = new();

    protected override string ServerName => "Preview";
    protected override int Port => _port;

    public PreviewService(SiteConfig config, int port, string host = "localhost", bool openBrowser = false,
        bool watch = false, bool liveReload = false, bool spaFallback = false, bool dirListing = false)
        : base(host, openBrowser)
    {
        _config = config;
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
            StartFileWatcher();
        }
    }

    protected override string? GetLiveReloadScript() => _wsServer?.GetLiveReloadScript();

    public override void Shutdown()
    {
        Cts?.Cancel();
        Listener?.Stop();
        _wsServer?.Stop();
        _watcher?.Dispose();
        _debounceTimer?.Dispose();

        LogWriter.Info($"Total requests: {TotalRequests}, rebuilds: {_rebuildCount}");
    }

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
            if (_outputDir != null && e.FullPath.StartsWith(_outputDir, StringComparison.OrdinalIgnoreCase))
                return;

            var parts = e.FullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            for (int i = 0; i < parts.Length - 1; i++)
            {
                var p = parts[i];
                if (i == parts.Length - 2)
                {
                    if (IgnoredDirNames.Contains(p))
                        return;
                }
                else if (p.StartsWith('.'))
                {
                    return;
                }
            }

            var ext = Path.GetExtension(e.Name!)?.ToLowerInvariant() ?? "";
            if (!WatchConstants.Extensions.Contains(ext)) return;

            var isCss = ext is ".css" or ".zcss";
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

                if (_liveReload && _wsServer != null)
                {
                    bool cssOnly;
                    lock (_changeLock)
                    {
                        cssOnly = _cssOnlyChanges;
                        _cssOnlyChanges = true;
                    }

                    if (cssOnly)
                        _wsServer.BroadcastStyleUpdate();
                    else
                        _wsServer.BroadcastReload();
                }
            }
            catch (Exception ex)
            {
                LogWriter.Error("PreviewService", $"Rebuild failed: {ex.Message}");
            }
        }
    }
}
