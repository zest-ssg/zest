using Zest.Engine;

namespace Zest.Infra.Services;

/// <summary>
/// C# wrapper around the F# BuildEngine for CLI consumption.
/// Tracks build state for incremental builds.
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
}
