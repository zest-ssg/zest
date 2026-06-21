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
        int? portOverride = null;
        string host = "localhost";
        bool openBrowser = false;
        bool verbose = false;
        bool quiet = false;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--port":
                case "-p":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var p))
                        portOverride = p;
                    else
                    {
                        Console.Error.WriteLine("Error: --port requires a numeric value");
                        return 1;
                    }
                    break;
                case "--host":
                    if (i + 1 < args.Length)
                        host = args[++i];
                    else
                    {
                        Console.Error.WriteLine("Error: --host requires a value");
                        return 1;
                    }
                    break;
                case "--open":
                case "-o":
                    openBrowser = true;
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
                case "-h":
                    PrintServeHelp();
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown option: {args[i]}");
                    Console.Error.WriteLine("Run 'zest serve --help' for usage.");
                    return 1;
            }
        }

        Logger.SetVerbose(verbose);
        Logger.SetQuiet(quiet);

        // Enable FSI verbose output
        if (verbose)
            ScriptRunner.setVerbose(true);

        var config = SiteConfigLoader.Load();
        if (portOverride.HasValue)
        {
            config = config.WithDevServerPort(portOverride.Value);
        }

        using var server = new DevServerService(config, host, openBrowser);
        server.Start();

        var evt = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, args) =>
        {
            Console.WriteLine();
            Logger.Info("Shutting down...");
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
        int port = 8080;
        string host = "localhost";
        bool openBrowser = false;
        bool verbose = false;
        bool quiet = false;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--port":
                case "-p":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var p))
                        port = p;
                    else
                    {
                        Console.Error.WriteLine("Error: --port requires a numeric value");
                        return 1;
                    }
                    break;
                case "--host":
                    if (i + 1 < args.Length)
                        host = args[++i];
                    else
                    {
                        Console.Error.WriteLine("Error: --host requires a value");
                        return 1;
                    }
                    break;
                case "--open":
                case "-o":
                    openBrowser = true;
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
                case "-h":
                    PrintPreviewHelp();
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown option: {args[i]}");
                    Console.Error.WriteLine("Run 'zest preview --help' for usage.");
                    return 1;
            }
        }

        Logger.SetVerbose(verbose);
        Logger.SetQuiet(quiet);

        var config = SiteConfigLoader.Load();
        using var server = new PreviewService(config, port, host, openBrowser);
        server.Start();

        var evt = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, args) =>
        {
            Console.WriteLine();
            Logger.Info("Shutting down preview server...");
            server.Stop();
            evt.Set();
            args.Cancel = true;
        };
        evt.Wait();
        return 0;
    }

    private static void PrintServeHelp()
    {
        Console.WriteLine("Usage: zest serve [options]");
        Console.WriteLine();
        Console.WriteLine("Start the development server with live reload.");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --port, -p PORT     Dev server port (default: 8080)");
        Console.WriteLine("  --host HOST         Bind to host (default: localhost)");
        Console.WriteLine("  --open, -o          Open browser on start");
        Console.WriteLine("  --verbose, -v       Show detailed FSI output");
        Console.WriteLine("  --quiet, -q         Suppress INFO logs");
        Console.WriteLine("  --help, -h          Show this help");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  zest serve --open");
        Console.WriteLine("  zest serve --port 3000 --verbose");
        Console.WriteLine("  zest serve --host 0.0.0.0 --port 8080");
    }

    private static void PrintPreviewHelp()
    {
        Console.WriteLine("Usage: zest preview [options]");
        Console.WriteLine();
        Console.WriteLine("Preview the built _site/ directory without rebuilding.");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --port, -p PORT     Preview server port (default: 8080)");
        Console.WriteLine("  --host HOST         Bind to host (default: localhost)");
        Console.WriteLine("  --open, -o          Open browser on start");
        Console.WriteLine("  --verbose, -v       Show detailed request info");
        Console.WriteLine("  --quiet, -q         Suppress INFO logs");
        Console.WriteLine("  --help, -h          Show this help");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  zest preview --open");
        Console.WriteLine("  zest preview --port 3000 --host 0.0.0.0");
    }
}
