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
            opts = CommandParser.ParseBuild(args);
        }
        catch (ArgumentException ex)
        {
            LogWriter.WriteError($"  Error: {ex.Message}");
            return 1;
        }

        if (opts.ShowHelp)
        {
            CommandParser.PrintBuildHelp();
            return 0;
        }

        // If project path specified, change working directory
        if (opts.ProjectPath != null)
        {
            var fullPath = Path.GetFullPath(opts.ProjectPath);
            if (!Directory.Exists(fullPath))
            {
                LogWriter.WriteError($"  Directory not found: {fullPath}");
                return 1;
            }
            Directory.SetCurrentDirectory(fullPath);
        }

        var config = ConfigLoader.Load();

        // Initialize logger from CLI flags overriding config
        var effectiveLevel = opts.Verbose ? "Debug" : config.LogLevel;
        if (opts.Quiet) effectiveLevel = "Warn";
        LogWriter.Configure(effectiveLevel, config.LogToFile, config.LogTimestamps);
        LogWriter.Debug("Build", $"Log level: {LogWriter.MinLevel}, file logging: {config.LogToFile}");
        LogWriter.Debug("Build", $"Project: {config.Title} (rootDir={config.RootDir})");

        var buildSvc = new BuildService();
        var result = buildSvc.Execute(config);

        BuildService.PrintResult(result, config);

        if (opts.Watch)
            WatchAgent.StartWatcher(config);

        return result.Success ? 0 : 1;
    }
}
