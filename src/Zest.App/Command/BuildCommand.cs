using Zest.App.Cli;
using Zest.Compiler.Model;
using Zest.Compiler.Build;
using Zest.Compiler.Execution;
using Zest.App.Config;
using Zest.App.Runtime;

namespace Zest.App.Command;

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
            opts = CliParser.ParseBuild(args);
        }
        catch (ArgumentException ex)
        {
            LogWriter.WriteError($"  Error: {ex.Message}");
            return 1;
        }

        if (opts.ShowHelp)
        {
            CliParser.PrintCommandHelp("build");
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

        SiteConfig config;
        try
        {
            config = ConfigLoader.Load();
        }
        catch (ConfigException ex)
        {
            LogWriter.WriteError($"  Config error: {ex.Message}");
            return 1;
        }

        // --no-incremental forces a full rebuild for this run only.
        if (opts.NoIncremental)
            config = config.WithIncrementalBuild(false);

        // Initialize logger from config, with CLI flags applied on top.
        LogWriter.Configure(config.LogLevel, config.LogToFile, config.LogTimestamps, opts.Verbose, opts.Quiet);
        LogWriter.Debug("Build", $"Log level: {LogWriter.MinLevel}, file logging: {config.LogToFile}");
        LogWriter.Debug("Build", $"Project: {config.Title}");

        try
        {
            var buildSvc = new BuildDriver();
            var result = buildSvc.Execute(config);

            BuildDriver.PrintResult(result, config);

            if (opts.Watch)
            {
                // Blocks until Ctrl+C; runs periodic full rebuilds on change.
                BuildWatcher.StartWatcher(config);
                return 0;
            }

            return result.Success ? 0 : 1;
        }
        finally
        {
            // A one-shot build must not leave the long-running `dotnet fsi`
            // child holding the terminal open after we exit. This also covers
            // the failure path, where the child would otherwise outlive us.
            FsiSession.shutdown();
        }
    }
}
