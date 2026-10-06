using Zest.App.Cli;
using Zest.Compiler.Model;
using Zest.Compiler.Build;
using Zest.App.Config;
using Zest.App.Runtime;

namespace Zest.App.Command;

/// <summary>
/// Handles `zest clean [path] [--cache] [--output]`
/// Clears build artifacts. By default clears both cache and output.
///   --cache   Remove .zest-cache.log / .zest-deps.log (and in-process state)
///   --output  Remove the _site output directory
/// </summary>
public static class CleanCommand
{
    /// <summary>Legacy and current on-disk cache artifact names.</summary>
    private static readonly string[] CacheFileNames =
    {
        ".zest-cache.log", ".zest-deps.log",          // current format
        ".zest-cache.toml", ".zest-deps.toml",        // legacy (v1 transitional)
        ".zest-cache.json", ".zest-deps.json",        // legacy (pre-upgrade)
        ".zest-cache", ".zest-deps",                  // legacy (bare files)
        ".zcss-cache"
    };

    public static int Execute(string[] args)
    {
        var clearCache = false;
        var clearOutput = false;
        var showHelp = false;
        var quiet = false;
        string? projectDirArg = null;

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--cache":
                    clearCache = true;
                    break;
                case "--output":
                    clearOutput = true;
                    break;
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                case "--quiet":
                case "-q":
                    quiet = true;
                    break;
                case "--verbose":
                case "-v":
                    break;
                default:
                    if (args[i].StartsWith('-'))
                    {
                        LogWriter.WriteError($"  Error: Unknown option: {args[i]}");
                        return 1;
                    }
                    if (projectDirArg != null)
                    {
                        LogWriter.WriteError($"  Error: Unexpected argument: {args[i]}");
                        return 1;
                    }
                    projectDirArg = args[i];
                    break;
            }
        }

        if (showHelp)
        {
            CliParser.PrintCommandHelp("clean");
            return 0;
        }

        LogWriter.SetQuiet(quiet);

        // Default: clear both when no flag given.
        if (!clearCache && !clearOutput) { clearCache = true; clearOutput = true; }

        var projectDir = projectDirArg is null
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(projectDirArg);

        if (!Directory.Exists(projectDir))
        {
            LogWriter.WriteError($"  Directory not found: {projectDir}");
            return 1;
        }

        // Resolve the output directory from site config (default "_site"),
        // tolerating a missing or invalid _config.toml so `zest clean` always
        // works — it is the escape hatch for a broken project.
        var outputDirName = "_site";
        try
        {
            outputDirName = ConfigLoader.Load(projectDir).OutputDir;
        }
        catch (ConfigException ex)
        {
            LogWriter.Warn("Clean", $"Using the default output directory: {ex.Message}");
        }

        var outputDir = Path.GetFullPath(
            Path.Combine(projectDir, outputDirName.TrimStart('.', '/', '\\')));

        if (clearCache)
            ClearCacheArtifacts(projectDir, outputDir);

        if (clearOutput)
            ClearOutputDirectory(projectDir, outputDir);

        return 0;
    }

    private static void ClearCacheArtifacts(string projectDir, string outputDir)
    {
        // Clear in-process cache (mtime index + dependency graph) via the
        // BuildDriver wrapper, then delete on-disk cache artifacts.
        BuildDriver.ClearCache();

        foreach (var name in CacheFileNames)
        {
            // Search in project root AND in the output directory.
            foreach (var dir in new[] { projectDir, outputDir })
            {
                var path = Path.Combine(dir, name);
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        LogWriter.WriteDim($"  Removed {path}");
                    }
                    else if (Directory.Exists(path))
                    {
                        Directory.Delete(path, recursive: true);
                        LogWriter.WriteDim($"  Removed {path}");
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    LogWriter.WriteError($"  Could not remove {path}: {ex.Message}");
                }
            }
        }

        LogWriter.WriteSuccess("  [Zest] Build cache cleared.");
    }

    private static void ClearOutputDirectory(string projectDir, string outputDir)
    {
        if (!Directory.Exists(outputDir))
        {
            LogWriter.WriteDim("  [Zest] No output directory to remove.");
            return;
        }

        // Recursive deletion is the most destructive thing the CLI does, so
        // re-check containment here: the config loader already rejects an
        // escaping path, but this guard also covers the default we fall back
        // to when the config could not be read.
        if (!IsInsideProject(projectDir, outputDir))
        {
            LogWriter.WriteError(
                $"  Refusing to remove '{outputDir}': it is outside the project root '{projectDir}'.");
            return;
        }

        try
        {
            Directory.Delete(outputDir, recursive: true);
            LogWriter.WriteSuccess($"  [Zest] Removed output directory '{outputDir}'.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file locked by another process (an editor, a running server)
            // must not surface as an unhandled fatal error.
            LogWriter.WriteError($"  Could not remove '{outputDir}': {ex.Message}");
        }
    }

    /// <summary>
    /// True when <paramref name="candidate"/> is strictly inside
    /// <paramref name="projectDir"/>.
    /// </summary>
    private static bool IsInsideProject(string projectDir, string candidate)
    {
        var root = Path.GetFullPath(projectDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var target = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(root, target, PathComparison)) return false;

        return target.StartsWith(root + Path.DirectorySeparatorChar, PathComparison);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
}
