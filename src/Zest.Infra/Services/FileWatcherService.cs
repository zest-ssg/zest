using Zest.Engine;

#nullable enable

namespace Zest.Infra.Services;

/// <summary>
/// Relevant file extensions for content watching.
/// </summary>
public static class WatchConstants
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".fsx", ".zpage.fsx", ".zhtml", ".md", ".markdown",
        ".html", ".css", ".zcss", ".js", ".toml",
        ".json", ".yaml", ".yml",
        ".png", ".jpg", ".jpeg", ".svg", ".gif", ".webp"
    };

    public static readonly HashSet<string> ExcludedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "_site", ".git", "node_modules", "bin", "obj", "packages"
    };
}

/// <summary>
/// File watcher that triggers site rebuild on content changes.
/// Filters by relevant extensions and excludes hidden/system directories.
/// </summary>
public static class FileWatcherService
{
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
                BuildService.PrintResult(r, config);
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

    private static bool ShouldWatchFile(string fullPath, string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return false;

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (!WatchConstants.Extensions.Contains(ext))
            return false;

        var parts = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var p = parts[i];
            if (WatchConstants.ExcludedDirs.Contains(p) || p.StartsWith("_") || p.StartsWith("."))
                return false;
        }

        return true;
    }
}
