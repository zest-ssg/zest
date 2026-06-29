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
            opts = CliParser.ParseServe(args);
        }
        catch (ArgumentException ex)
        {
            Logger.WriteError($"  Error: {ex.Message}");
            return 1;
        }

        if (opts.ShowHelp)
        {
            CliParser.PrintServeHelp();
            return 0;
        }

        Logger.SetVerbose(opts.Verbose);
        Logger.SetQuiet(opts.Quiet);

        // Enable FSI verbose output
        if (opts.Verbose)
            ScriptRunner.setVerbose(true);

        var config = SiteConfigLoader.Load();
        if (opts.PortOverride.HasValue)
        {
            config = config.WithDevServerPort(opts.PortOverride.Value);
        }

        using var server = new DevServerService(config, opts.Host, opts.OpenBrowser);
        server.Start();

        var evt = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, args) =>
        {
            Console.WriteLine();
            Logger.WriteSuccess("  Shutting down...");
            server.Stop();
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
            opts = CliParser.ParsePreview(args);
        }
        catch (ArgumentException ex)
        {
            Logger.WriteError($"  Error: {ex.Message}");
            return 1;
        }

        if (opts.ShowHelp)
        {
            CliParser.PrintPreviewHelp();
            return 0;
        }

        Logger.SetVerbose(opts.Verbose);
        Logger.SetQuiet(opts.Quiet);

        var config = SiteConfigLoader.Load();
        using var server = new PreviewService(config, opts.Port, opts.Host, opts.OpenBrowser);
        server.Start();

        var evt = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, args) =>
        {
            Console.WriteLine();
            Logger.WriteSuccess("  Shutting down preview server...");
            server.Stop();
            evt.Set();
            args.Cancel = true;
        };
        evt.Wait();
        return 0;
    }
}
