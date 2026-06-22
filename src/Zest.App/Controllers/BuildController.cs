using Zest.Engine;
using Zest.Infra.Configuration;
using Zest.Infra.Services;

namespace Zest.App.Controllers;

/// <summary>
/// Handles `zest build [path] [--watch] [--no-incremental]`
/// </summary>
public static class BuildController
{
    public static int Execute(string[] args)
    {
        var watch = false;
        string? projectPath = null;
        var verbose = false;
        var quiet = false;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--watch":
                case "-w":
                    watch = true;
                    break;
                case "--verbose":
                case "-v":
                    verbose = true;
                    break;
                case "--quiet":
                case "-q":
                    quiet = true;
                    break;
                case "--help":
                    Console.WriteLine("Usage: zest build [path] [options]");
                    Console.WriteLine();
                    Console.WriteLine("Arguments:");
                    Console.WriteLine("  path              Project directory (default: current directory)");
                    Console.WriteLine();
                    Console.WriteLine("Options:");
                    Console.WriteLine("  --watch, -w       Watch for changes and auto-rebuild");
                    Console.WriteLine("  --verbose, -v     Enable Debug-level logging");
                    Console.WriteLine("  --quiet, -q       Suppress Info-level logs");
                    return 0;
                default:
                    // Treat non-flag arguments as project path
                    if (projectPath == null && !args[i].StartsWith("-"))
                        projectPath = args[i];
                    break;
            }
        }

        // If project path specified, change working directory
        if (projectPath != null)
        {
            var fullPath = Path.GetFullPath(projectPath);
            if (!Directory.Exists(fullPath))
            {
                Logger.Error("Build", $"Directory not found: {fullPath}");
                return 1;
            }
            Directory.SetCurrentDirectory(fullPath);
        }

        var config = SiteConfigLoader.Load();

        // Initialize logger from CLI flags overriding config
        var effectiveLevel = verbose ? "Debug" : config.LogLevel;
        if (quiet) effectiveLevel = "Warn";
        Logger.Configure(effectiveLevel, config.LogToFile, config.LogTimestamps);
        Logger.Debug("Build", $"Log level: {Logger.MinLevel}, file logging: {config.LogToFile}");
        Logger.Debug("Build", $"Project: {config.Title} (rootDir={config.RootDir})");

        var buildSvc = new BuildService();
        var result = buildSvc.Execute(config);

        BuildService.PrintResult(result, config);

        if (watch)
            BuildService.StartWatcher(config);

        return result.Success ? 0 : 1;
    }
}
