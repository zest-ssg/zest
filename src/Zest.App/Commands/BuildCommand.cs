using Zest.App.CommandLine;
using Zest.Compiler;
using Zest.Compiler.Scripting;
using Zest.App.Configuration;
using Zest.App.Services;

namespace Zest.App.Commands;

/// <summary>
/// Handles `zest build [path] [--watch] [--no-incremental]`
/// </summary>
public static class BuildCommand
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
            CommandParser.PrintCommandHelp("build");
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
        LogWriter.Debug("Build", $"Project: {config.Title}");

        var buildSvc = new BuildService();
        var result = buildSvc.Execute(config);

        BuildService.PrintResult(result, config);

        if (opts.Watch)
            WatchAgent.StartWatcher(config);
        else
            // A one-shot build must not leave the long-running `dotnet fsi`
            // child holding the terminal open after we exit. Watch mode keeps
            // it alive for reuse on subsequent rebuilds.
            FsiSession.shutdown();

        return result.Success ? 0 : 1;
    }
}
