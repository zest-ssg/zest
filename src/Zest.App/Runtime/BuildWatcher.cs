using Zest.Compiler.Model;
using Zest.Compiler.Build;
using Zest.App.Config;

#nullable enable

namespace Zest.App.Runtime;

/// <summary>
/// Relevant file extensions for content watching.
///
/// Derived from the compiler's own extension registry rather than a hand-kept
/// list: Zestucks layouts and includes (<c>.ztk</c>/<c>.njk</c>) are the files
/// an author edits most, and forgetting one here means "save has no effect"
/// with no error to explain it.
/// </summary>
public static class WatchConstants
{
    public static readonly HashSet<string> Extensions = BuildExtensions();

    private static HashSet<string> BuildExtensions()
    {
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // NOTE: Path.GetExtension("page.zest.fsx") returns ".fsx", so the
            // plain script extension also covers Zest pages.
            FileTypes.FSharpScript,
            FileTypes.Markdown,
            FileTypes.MarkdownLong,
            FileTypes.Html,
            FileTypes.HtmlLong,
            FileTypes.Zestucks,
            FileTypes.Nunjucks,
            FileTypes.Css,
            FileTypes.Zcss,
            FileTypes.JavaScript,
            FileTypes.Toml,

            // Assets copied verbatim.
            FileTypes.Png,
            FileTypes.Jpg,
            FileTypes.Jpeg,
            FileTypes.Svg,
            FileTypes.Gif,
            FileTypes.Webp,
            ".ico",
            ".avif",
            ".woff",
            ".woff2",
            ".ttf",
            ".otf"
        };

        return extensions;
    }
}

/// <summary>
/// Standalone file watcher for <c>zest build --watch</c>.
///
/// Delegates to <see cref="ContentWatcher"/> so <c>build --watch</c> and
/// <c>zest serve</c> react to exactly the same set of files — previously the
/// standalone watcher only saw the content directory and silently ignored
/// layout, include, asset and config edits. Rebuilds are serialised so a burst
/// of changes cannot start two builds at once.
/// </summary>
public static class BuildWatcher
{
    public static void StartWatcher(SiteConfig config)
    {
        var projectDir = Directory.GetCurrentDirectory();
        var outputDir = Path.GetFullPath(Path.Combine(projectDir, config.OutputDir.TrimStart('.', '\\', '/')));

        LogWriter.WriteAccent($"  Watching for changes in '{projectDir}'...");
        LogWriter.WriteDim("  Press Ctrl+C to stop.");

        var rebuildLock = new object();
        using var watcher = new ContentWatcher(
            projectDir,
            outputDir,
            ExcludedPaths.For(config),
            _ => Rebuild(config, rebuildLock));

        var evt = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, args) => { evt.Set(); args.Cancel = true; };
        evt.Wait();
    }

    private static void Rebuild(SiteConfig config, object rebuildLock)
    {
        lock (rebuildLock)
        {
            try
            {
                // Re-read _config.toml so config edits apply without a restart.
                var effective = ReloadedConfig(config);
                var svc = new BuildDriver();
                var r = svc.Execute(effective);
                // PrintResult stops the animator and prints summary + errors.
                // No need to re-iterate errors here.
                BuildDriver.PrintResult(r, effective);
            }
            catch (Exception ex)
            {
                LogWriter.Error("Watch", $"Rebuild failed: {ex.Message}");
                if (ex.InnerException != null)
                    LogWriter.Error("Watch", $"  → {ex.InnerException.Message}");
            }
        }
    }

    private static SiteConfig ReloadedConfig(SiteConfig fallback)
    {
        try
        {
            return ConfigLoader.Load();
        }
        catch (ConfigException ex)
        {
            LogWriter.Error("Watch", $"Ignoring invalid _config.toml: {ex.Message}");
            return fallback;
        }
    }
}
