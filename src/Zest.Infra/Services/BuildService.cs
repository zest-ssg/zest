using Zest.Engine;

namespace Zest.Infra.Services;

/// <summary>
/// Thin C# wrapper around the F# BuildEngine for CLI consumption.
/// </summary>
public class BuildService
{
    /// <summary>
    /// Execute the full build pipeline.
    /// </summary>
    public BuildResult Execute(SiteConfig config)
    {
        return BuildEngine.execute(config);
    }

    /// <summary>
    /// Print build result to console.
    /// </summary>
    public static void PrintResult(BuildResult result, SiteConfig config)
    {
        var totalPages = result.TotalPages;
        var processed = result.ProcessedPages;
        var cached = result.CachedPages;
        var assetsCopied = result.AssetsCopied;
        var assetsMinified = result.AssetsMinified;
        var errors = result.Errors;
        var errorsList = errors.ToArray();

        var color = errorsList.Length == 0 ? ConsoleColor.Green : ConsoleColor.Red;
        Console.ForegroundColor = color;
        Console.WriteLine($"[Zest] Build complete in {result.DurationMs}ms");
        Console.ResetColor();
        Console.WriteLine($"       Pages: {totalPages} total, {processed} processed, {cached} cached");

        if (assetsCopied > 0 || assetsMinified > 0)
            Console.WriteLine($"       Assets: {assetsCopied} copied, {assetsMinified} minified");

        if (errorsList.Length > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"       Errors: {errorsList.Length}");
            foreach (var err in errorsList)
                Console.WriteLine($"         - {err}");
            Console.ResetColor();
        }

        var outputDir = Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(), config.OutputDir.TrimStart('.', '\\', '/')));
        Console.WriteLine($"       Output: {outputDir}");
    }

    /// <summary>
    /// Start a file watcher that triggers rebuilds on content changes.
    /// </summary>
    public static void StartWatcher(SiteConfig config)
    {
        var contentDir = Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(), config.ContentDir.TrimStart('.', '\\', '/')));

        Console.WriteLine($"[Zest] Watching for changes in '{contentDir}'...");
        Console.WriteLine("       Press Ctrl+C to stop.");

        using var watcher = new FileSystemWatcher(contentDir, "*.*")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime
        };

        var debounceTimer = new System.Timers.Timer(300) { AutoReset = false };
        debounceTimer.Elapsed += (_, _) =>
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[Zest] Change detected, rebuilding...");
            Console.ResetColor();
            var svc = new BuildService();
            var r = svc.Execute(config);
            PrintResult(r, config);
        };

        void OnChange(object sender, FileSystemEventArgs e)
        {
            var ext = Path.GetExtension(e.Name).ToLowerInvariant();
            var relevant = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".fsx", ".zest.fsx", ".md", ".markdown",
                ".html", ".css", ".js", ".toml", ".json", ".yaml", ".yml"
            };
            if (!relevant.Contains(ext)) return;
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
}
