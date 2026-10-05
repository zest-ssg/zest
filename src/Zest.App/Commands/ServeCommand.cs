using Zest.App.CommandLine;
using Zest.Compiler;
using Zest.Compiler.Scripting;
using Zest.App.Configuration;
using Zest.App.Services;

namespace Zest.App.Commands;

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
            opts = CommandParser.ParseServe(args);
        }
        catch (ArgumentException ex)
        {
            LogWriter.WriteError($"  Error: {ex.Message}");
            return 1;
        }

        if (opts.ShowHelp)
        {
            CommandParser.PrintCommandHelp("serve");
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

        using var server = new DevServer(config, opts.Host, opts.OpenBrowser, opts.SPA, opts.DirectoryListing);
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
            CommandParser.PrintCommandHelp("preview");
            return 0;
        }

        LogWriter.SetVerbose(opts.Verbose);
        LogWriter.SetQuiet(opts.Quiet);

        var config = ConfigLoader.Load();
        using var server = new PreviewService(config, opts.Port, opts.Host, opts.OpenBrowser,
            watch: opts.Watch, liveReload: opts.LiveReload, spaFallback: opts.SPA, dirListing: opts.DirectoryListing);
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
