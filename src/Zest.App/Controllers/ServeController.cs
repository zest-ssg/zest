using Zest.App.CommandLine;
using Zest.Engine;
using Zest.Engine.Scripting;
using Zest.Infra.Configuration;
using Zest.Infra.Services;

namespace Zest.App.Controllers;

/// <summary>
/// Handles zest serve / zest preview commands.
/// Supports: --port, --host, --open, --verbose, --quiet
/// </summary>
public static class ServeController
{
    /// <summary>
    /// Build + start dev server with live reload.
    /// </summary>
    public static int Execute(string[] args)
    {
        ServeCommandOptions opts;
        try
        {
            opts = CommandParser.ParseServe(args);
        }
        catch (ArgumentException ex)
        {
            LogWriter.WriteError($"  Error: {ex.Message}");
            return 1;
        }

        if (opts.ShowHelp)
        {
            CommandParser.PrintServeHelp();
            return 0;
        }

        LogWriter.SetVerbose(opts.Verbose);
        LogWriter.SetQuiet(opts.Quiet);

        // Enable FSI verbose output
        if (opts.Verbose)
            PageQuery.setVerbose(true);

        var config = ConfigLoader.Load();
        if (opts.PortOverride.HasValue)
        {
            config = config.WithDevServerPort(opts.PortOverride.Value);
        }

        using var server = new DevServer(config, opts.Host, opts.OpenBrowser);
        server.Start();

        var evt = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, args) =>
        {
            Console.WriteLine();
            LogWriter.WriteSuccess("  Shutting down...");
            server.Shutdown();
            evt.Set();
            args.Cancel = true;
        };
        evt.Wait();
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
            opts = CommandParser.ParsePreview(args);
        }
        catch (ArgumentException ex)
        {
            LogWriter.WriteError($"  Error: {ex.Message}");
            return 1;
        }

        if (opts.ShowHelp)
        {
            CommandParser.PrintPreviewHelp();
            return 0;
        }

        LogWriter.SetVerbose(opts.Verbose);
        LogWriter.SetQuiet(opts.Quiet);

        var config = ConfigLoader.Load();
        using var server = new PreviewService(config, opts.Port, opts.Host, opts.OpenBrowser);
        server.Start();

        var evt = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, args) =>
        {
            Console.WriteLine();
            LogWriter.WriteSuccess("  Shutting down preview server...");
            server.Shutdown();
            evt.Set();
            args.Cancel = true;
        };
        evt.Wait();
        return 0;
    }
}
