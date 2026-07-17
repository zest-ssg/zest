using Zest.Engine;
using Zest.Infra.Configuration;
using Zest.Infra.Services;

namespace Zest.App.Controllers;

/// <summary>
/// Handles `zest clean [--cache] [--output]`
/// Clears build artifacts. By default clears both cache and output.
///   --cache   Remove .zest-cache.json / .zest-deps.json (and in-process state)
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
            BuildService.ClearCache();
            foreach (var name in new[] { ".zest-cache.json", ".zest-deps.json", ".zcss-cache" })
            {
                var p = Path.Combine(projectDir, name);
                if (File.Exists(p)) { File.Delete(p); LogWriter.WriteDim($"  Removed {p}"); }
                else if (Directory.Exists(p)) { Directory.Delete(p, recursive: true); LogWriter.WriteDim($"  Removed {p}"); }
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
