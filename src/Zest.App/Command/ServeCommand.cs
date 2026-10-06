using Zest.App.Cli;
using Zest.Compiler.Model;
using Zest.Compiler.Build;
using Zest.Compiler.Execution;
using Zest.App.Config;
using Zest.App.Runtime;

namespace Zest.App.Command;

/// <summary>
/// Handles zest serve / zest preview commands.
/// Supports: --port, --host, --open, --verbose, --quiet
/// </summary>
public static class ServeCommand
{
    /// <summary>
    /// Build + start dev server with live reload.
    /// </summary>
    public static int Execute(string[] args)
    {
        ServeCommandOptions opts;
        try
        {
            opts = CliParser.ParseServe(args);
        }
        catch (ArgumentException ex)
        {
            LogWriter.WriteError($"  Error: {ex.Message}");
            return 1;
        }

        if (opts.ShowHelp)
        {
            CliParser.PrintCommandHelp("serve");
            return 0;
        }

        var config = LoadConfig();
        if (config is null) return 1;

        if (opts.PortOverride.HasValue)
        {
            config = config.WithDevServerPort(opts.PortOverride.Value);
        }

        LogWriter.Configure(config.LogLevel, config.LogToFile, config.LogTimestamps, opts.Verbose, opts.Quiet);

        // Enable FSI verbose output
        if (opts.Verbose)
            PageStore.setVerbose(true);

        using var server = new DevServer(config, opts.Host, opts.OpenBrowser, opts.SPA, opts.DirectoryListing);
        try
        {
            server.Start();
        }
        catch (InvalidOperationException ex)
        {
            LogWriter.WriteError($"  Error: {ex.Message}");
            return 1;
        }

        WaitForShutdown(server, "  Shutting down...");
        return 0;
    }

    /// <summary>
    /// Preview mode: serve _site/ directory directly without building.
    /// </summary>
    public static int ExecutePreview(string[] args)
    {
        PreviewCommandOptions opts;
        try
        {
            opts = CliParser.ParsePreview(args);
        }
        catch (ArgumentException ex)
        {
            LogWriter.WriteError($"  Error: {ex.Message}");
            return 1;
        }

        if (opts.ShowHelp)
        {
            CliParser.PrintCommandHelp("preview");
            return 0;
        }

        var config = LoadConfig();
        if (config is null) return 1;

        LogWriter.Configure(config.LogLevel, config.LogToFile, config.LogTimestamps, opts.Verbose, opts.Quiet);

        using var server = new PreviewServer(config, opts.Port, opts.Host, opts.OpenBrowser,
            watch: opts.Watch, liveReload: opts.LiveReload, spaFallback: opts.SPA, dirListing: opts.DirectoryListing);
        try
        {
            server.Start();
        }
        catch (InvalidOperationException ex)
        {
            LogWriter.WriteError($"  Error: {ex.Message}");
            return 1;
        }

        WaitForShutdown(server, "  Shutting down preview server...");
        return 0;
    }

    /// <summary>
    /// Load configuration, reporting a broken <c>_config.toml</c> as a normal
    /// command failure rather than an unhandled fatal error.
    /// </summary>
    private static SiteConfig? LoadConfig()
    {
        try
        {
            return ConfigLoader.Load();
        }
        catch (ConfigException ex)
        {
            LogWriter.WriteError($"  Config error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Block until Ctrl+C, then shut the server down. Shutdown is idempotent,
    /// so the <c>using</c> block's Dispose is a no-op afterwards.
    /// </summary>
    private static void WaitForShutdown(HttpServerBase server, string message)
    {
        var evt = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, args) =>
        {
            Console.WriteLine();
            LogWriter.WriteSuccess(message);
            server.Shutdown();
            evt.Set();
            args.Cancel = true;
        };

        evt.Wait();
    }
}
