using Zest.Compiler.Model;
using Zest.Compiler.Build;

#nullable enable

namespace Zest.App.Runtime;

/// <summary>
/// Relevant file extensions for content watching.
/// </summary>
public static class WatchConstants
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        FileTypes.FSharpScript, FileTypes.ZestScript,
        FileTypes.Markdown, FileTypes.MarkdownLong,
        FileTypes.Html, FileTypes.Css, FileTypes.Zcss,
        FileTypes.JavaScript, FileTypes.Toml,
        FileTypes.Png, FileTypes.Jpg, FileTypes.Jpeg,
        FileTypes.Svg, FileTypes.Gif, FileTypes.Webp
    };
}

/// <summary>
/// Standalone file watcher for <c>zest build --watch</c>.
/// Monitors the content directory for changes and triggers a full site
/// rebuild after a 300ms debounce. Filters by relevant extensions and
/// excludes hidden/system directories.
/// </summary>
public static class BuildWatcher
{
    public static void StartWatcher(SiteConfig config)
    {
        var excludedDirs = ExcludedPaths.For(config);
        var contentDir = SitePaths.resolveContentDir(Directory.GetCurrentDirectory(), config);

        LogWriter.WriteAccent($"  Watching for changes in '{contentDir}'...");
        LogWriter.WriteDim("  Press Ctrl+C to stop.");

        using var watcher = new FileSystemWatcher(contentDir, "*.*")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            InternalBufferSize = 65536 // .NET default (8 KB) overflows on large projects
        };

        using var debounceTimer = new System.Timers.Timer(300) { AutoReset = false };
        debounceTimer.Elapsed += (_, _) =>
        {
            try
            {
                var svc = new BuildDriver();
                var r = svc.Execute(config);
                // PrintResult stops the animator and prints summary + errors.
                // No need to re-iterate errors here.
                BuildDriver.PrintResult(r, config);
            }
            catch (Exception ex)
            {
                LogWriter.Error("Watch", $"Rebuild failed: {ex.Message}");
                if (ex.InnerException != null)
                    LogWriter.Error("Watch", $"  → {ex.InnerException.Message}");
            }
        };

        void OnChange(object sender, FileSystemEventArgs e)
        {
            if (!ShouldWatchFile(e.FullPath, e.Name, excludedDirs))
                return;

            debounceTimer.Stop();
            debounceTimer.Start();
        }

        watcher.Changed += OnChange;
        watcher.Created += OnChange;
        watcher.Deleted += OnChange;
        watcher.Renamed += (_, _) =>
        {
            debounceTimer.Stop();
            debounceTimer.Start();
        };

        // Handle FileSystemWatcher.Error (buffer overflow, etc.)
        watcher.Error += (_, e) =>
        {
            var ex = e.GetException();
            LogWriter.Warn("Watch", $"FileSystemWatcher error: {ex.Message}. Triggering rebuild as safety measure.");
            debounceTimer.Stop();
            debounceTimer.Start();
        };

        watcher.EnableRaisingEvents = true;

        var evt = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, args) => { evt.Set(); args.Cancel = true; };
        evt.Wait();
    }

    private static bool ShouldWatchFile(string fullPath, string? fileName, HashSet<string> excludedDirs)
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
            if (excludedDirs.Contains(p) || p.StartsWith('_') || p.StartsWith('.'))
                return false;
        }

        return true;
    }
}
