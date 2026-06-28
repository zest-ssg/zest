using Zest.App.CommandLine;
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
        BuildCommandOptions opts;
        try
        {
            opts = CliParser.ParseBuild(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }

        if (opts.ShowHelp)
        {
            CliParser.PrintBuildHelp();
            return 0;
        }

        // If project path specified, change working directory
        if (opts.ProjectPath != null)
        {
            var fullPath = Path.GetFullPath(opts.ProjectPath);
            if (!Directory.Exists(fullPath))
            {
                Logger.Error("Build", $"Directory not found: {fullPath}");
                return 1;
            }
            Directory.SetCurrentDirectory(fullPath);
        }

        var config = SiteConfigLoader.Load();

        // Initialize logger from CLI flags overriding config
        var effectiveLevel = opts.Verbose ? "Debug" : config.LogLevel;
        if (opts.Quiet) effectiveLevel = "Warn";
        Logger.Configure(effectiveLevel, config.LogToFile, config.LogTimestamps);
        Logger.Debug("Build", $"Log level: {Logger.MinLevel}, file logging: {config.LogToFile}");
        Logger.Debug("Build", $"Project: {config.Title} (rootDir={config.RootDir})");

        var buildSvc = new BuildService();
        var result = buildSvc.Execute(config);

        BuildService.PrintResult(result, config);

        if (opts.Watch)
            FileWatcherService.StartWatcher(config);

        return result.Success ? 0 : 1;
    }
}
