#nullable disable

using Zest.Engine;

namespace Zest.Infra.Services;

/// <summary>
/// C# wrapper around the F# BuildEngine for CLI consumption.
/// Tracks build state for incremental builds and provides
/// debounced file watching for auto-rebuild.
/// </summary>
public class BuildService
{
    private BuildResult? _lastResult;

    /// <summary>
    /// Execute the full build pipeline.
    /// </summary>
    public BuildResult Execute(SiteConfig config)
    {
        _lastResult = BuildEngine.execute(config);
        return _lastResult;
    }

    /// <summary>
    /// The result of the most recent build (null if never built).
    /// </summary>
    public BuildResult? LastResult => _lastResult;

    /// <summary>
    /// Print build result to console using the Logger.
    /// </summary>
    public static void PrintResult(BuildResult result, SiteConfig config)
    {
        var errorsList = result.Errors.ToArray();

        if (errorsList.Length == 0)
        {
            Logger.BuildSummary("Build", result.TotalPages, result.ProcessedPages, result.CachedPages, result.AssetsCopied, result.DurationMs);
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  ✗ Build  ({result.ProcessedPages} pages, {result.CachedPages} cached) — {errorsList.Length} error(s)");
            Console.ResetColor();
        }

        foreach (var err in errorsList)
            Logger.Error("Build", err);

        if (Logger.Verbose)
        {
            var outputDir = Path.GetFullPath(Path.Combine(
                Directory.GetCurrentDirectory(), config.OutputDir.TrimStart('.', '\\', '/')));
            Logger.VerboseLog($"Output: {outputDir}");
        }
    }

    /// <summary>
    /// Relevant file extensions for content watching.
    /// </summary>
    private static readonly HashSet<string> WatchExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".fsx", ".zpage.fsx", ".zhtml", ".md", ".markdown",
        ".html", ".css", ".zcss", ".js", ".toml",
        ".json", ".yaml", ".yml",
        ".png", ".jpg", ".jpeg", ".svg", ".gif", ".webp"
    };

    /// <summary>
    /// Directories to exclude from file watching.
    /// </summary>
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "_site", ".git", "node_modules", "bin", "obj", "packages"
    };

    /// <summary>
    /// Start a file watcher that triggers rebuilds on content changes.
    /// </summary>
    public static void StartWatcher(SiteConfig config)
    {
        var contentDir = Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(), config.EffectiveContentDir.TrimStart('.', '\\', '/')));

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"  Watching for changes in '{contentDir}'...");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  Press Ctrl+C to stop.");
        Console.ResetColor();

        using var watcher = new FileSystemWatcher(contentDir, "*.*")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime
        };

        var debounceTimer = new System.Timers.Timer(300) { AutoReset = false };
        debounceTimer.Elapsed += (_, _) =>
        {
            try
            {
                var svc = new BuildService();
                var r = svc.Execute(config);
                PrintResult(r, config);
            }
            catch (Exception ex)
            {
                Logger.Error("Watch", $"Rebuild failed: {ex.Message}");
            }
        };

        void OnChange(object sender, FileSystemEventArgs e)
        {
            if (!ShouldWatchFile(e.FullPath, e.Name))
                return;

            debounceTimer.Stop();
            debounceTimer.Start();
        }

        watcher.Changed += OnChange;
        watcher.Created += OnChange;
        watcher.Deleted += OnChange;
        watcher.Renamed += (_, _) => { debounceTimer.Stop(); debounceTimer.Start(); };

        watcher.EnableRaisingEvents = true;
        var evt = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, args) => { evt.Set(); args.Cancel = true; };
        evt.Wait();
    }

    /// <summary>
    /// Determine whether a file change event should trigger a rebuild.
    /// Filters by extension and excludes hidden/system/build directories.
    /// </summary>
    private static bool ShouldWatchFile(string fullPath, string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return false;

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (!WatchExtensions.Contains(ext))
            return false;

        // Skip hidden/system/build directories
        var parts = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var p = parts[i];
            if (ExcludedDirectories.Contains(p) || p.StartsWith("_") || p.StartsWith("."))
                return false;
        }

        return true;
    }
}
