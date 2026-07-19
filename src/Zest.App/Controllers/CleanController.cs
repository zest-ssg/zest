using Zest.Engine;
using Zest.Infra.Configuration;
using Zest.Infra.Services;

namespace Zest.App.Controllers;

/// <summary>
/// Handles `zest clean [--cache] [--output]`
/// Clears build artifacts. By default clears both cache and output.
///   --cache   Remove .zest-cache.toml / .zest-deps.toml (and in-process state)
///   --output  Remove the _site output directory
/// </summary>
public static class CleanController
{
    public static int Execute(string[] args)
    {
        var clearCache = args.Contains("--cache");
        var clearOutput = args.Contains("--output");
        // Default: clear both when no flag given.
        if (!clearCache && !clearOutput) { clearCache = true; clearOutput = true; }

        var projectDir = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))
                          ?? Directory.GetCurrentDirectory();

        if (clearCache)
        {
            // Clear in-process cache (mtime index + dependency graph) via the
            // BuildService wrapper, then delete on-disk cache artifacts.
            // Include legacy .json names for migration from older Zest versions.
            BuildService.ClearCache();
            var cacheNames = new[] {
                ".zest-cache.toml", ".zest-deps.toml",      // current format
                ".zest-cache.json", ".zest-deps.json",      // legacy (pre-upgrade)
                ".zcss-cache"
            };
            foreach (var name in cacheNames)
            {
                // Search in project root AND in the output directory.
                var searchDirs = new[] { projectDir };
                try
                {
                    var cfg = ConfigLoader.Load(projectDir);
                    var outDir = Path.Combine(projectDir, cfg.OutputDir.TrimStart('.', '/', '\\'));
                    searchDirs = new[] { projectDir, outDir };
                }
                catch { /* config may be missing */ }

                foreach (var dir in searchDirs)
                {
                    var p = Path.Combine(dir, name);
                    if (File.Exists(p)) { File.Delete(p); LogWriter.WriteDim($"  Removed {p}"); }
                    else if (Directory.Exists(p)) { Directory.Delete(p, recursive: true); LogWriter.WriteDim($"  Removed {p}"); }
                }
            }
            LogWriter.WriteSuccess("  [Zest] Build cache cleared.");
        }

        if (clearOutput)
        {
            // Resolve output dir from site config (default "_site"); tolerate a
            // corrupted/missing _config.toml so `zest clean` always works.
            var outDirName = "_site";
            try
            {
                outDirName = ConfigLoader.Load(projectDir).OutputDir.TrimStart('.', '/', '\\');
            }
            catch { /* fall back to default */ }

            var outDir = Path.Combine(projectDir, outDirName);
            if (Directory.Exists(outDir))
            {
                Directory.Delete(outDir, recursive: true);
                LogWriter.WriteSuccess($"  [Zest] Removed output directory '{outDir}'.");
            }
            else
            {
                LogWriter.WriteDim("  [Zest] No output directory to remove.");
            }
        }

        return 0;
    }
}
