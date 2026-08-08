using Microsoft.FSharp.Collections;
using Zest.Engine;

#nullable enable

namespace Zest.Infra.Services;

/// <summary>
/// C# wrapper around the F# BuildEngine for CLI consumption.
/// Tracks build state for incremental builds and drives the animated
/// build indicator (BuildAnimator) during execution.
/// </summary>
public class BuildService
{
    private BuildResult? _lastResult;

    /// <summary>
    /// Execute the full build pipeline. Starts the animated indicator
    /// before the build. The summary is printed when PrintResult is called
    /// (which stops the animator and displays the final line).
    /// When forceRefresh is true, all incremental caches (in-memory + on-disk)
    /// and template caches are cleared first so every page regenerates — this
    /// is how dev/preview servers guarantee a fresh start.
    /// </summary>
    public BuildResult Execute(SiteConfig config, bool forceRefresh = false)
    {
        if (forceRefresh)
        {
            var outDir = Path.GetFullPath(Path.Combine(
                Directory.GetCurrentDirectory(), config.OutputDir.TrimStart('.', '\\', '/')));
            BuildCache.clearDiskCache(outDir);
            try { Zest.Engine.Template.TemplateManager.clearCaches(); }
            catch { /* non-fatal */ }
        }

        // Start the animated build indicator. It polls BuildProgress
        // (set by BuildEngine.execute) every 80ms for live counts.
        BuildAnimator.Start();

        try
        {
            _lastResult = BuildEngine.execute(config);
            return _lastResult;
        }
        catch (Exception ex)
        {
            // Build threw before finishing — stop the animator with an error result.
            var errResult = new BuildResult(
                0, 0, 0, 0, 0, 0, "",
                ListModule.OfArray(new[] { ex.Message }));
            BuildAnimator.Stop(errResult);
            _lastResult = errResult;
            throw;
        }
    }

    /// <summary>
    /// The result of the most recent build (null if never built).
    /// </summary>
    public BuildResult? LastResult => _lastResult;

    /// <summary>
    /// Clear the in-process build cache (mtime index, content hashes, and the
    /// page→dependency graph). Used by `zest clean --cache`. On-disk cache
    /// files (.zest-cache.log / .zest-deps.log) are removed separately by
    /// the CleanController.
    /// </summary>
    public static void ClearCache() => BuildCache.clearCache();

    /// <summary>
    /// Stop the animated indicator and print the build result summary.
    /// After the summary, any error details are printed individually.
    /// </summary>
    public static void PrintResult(BuildResult result, SiteConfig config)
    {
        // Stop the animator (or print a plain summary if it was never started).
        BuildAnimator.Stop(result);

        // Print full error details below the summary line.
        foreach (var err in result.Errors)
            LogWriter.Error("Build", err);

        if (LogWriter.Verbose && result.Errors.IsEmpty)
        {
            var outputDir = !string.IsNullOrEmpty(result.OutputDir)
                ? result.OutputDir
                : Path.GetFullPath(Path.Combine(
                    Directory.GetCurrentDirectory(), config.OutputDir.TrimStart('.', '\\', '/')));
            LogWriter.VerboseLog($"  Output: {outputDir}");
        }
    }
}
