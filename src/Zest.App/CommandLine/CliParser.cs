namespace Zest.App.CommandLine;

/// <summary>
/// Unified CLI argument parser for Zest commands.
/// Eliminates duplicate inline parsing across controllers.
/// </summary>
internal static class CliParser
{
    /// <summary>
    /// Parse `zest build` arguments.
    /// </summary>
    public static BuildCommandOptions ParseBuild(string[] args)
    {
        var opts = new BuildCommandOptions();
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--watch":
                case "-w":
                    opts = opts with { Watch = true };
                    break;
                case "--verbose":
                case "-v":
                    opts = opts with { Verbose = true };
                    break;
                case "--quiet":
                case "-q":
                    opts = opts with { Quiet = true };
                    break;
                case "--help":
                case "-h":
                    opts = opts with { ShowHelp = true };
                    break;
                default:
                    if (opts.ProjectPath == null && !args[i].StartsWith("-"))
                        opts = opts with { ProjectPath = args[i] };
                    break;
            }
        }
        return opts;
    }

    /// <summary>
    /// Parse `zest serve` arguments.
    /// </summary>
    public static ServeCommandOptions ParseServe(string[] args)
    {
        var opts = new ServeCommandOptions();
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--port":
                case "-p":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var p))
                        opts = opts with { PortOverride = p };
                    else
                        throw new ArgumentException("--port requires a numeric value");
                    break;
                case "--host":
                    if (i + 1 < args.Length)
                        opts = opts with { Host = args[++i] };
                    else
                        throw new ArgumentException("--host requires a value");
                    break;
                case "--open":
                case "-o":
                    opts = opts with { OpenBrowser = true };
                    break;
                case "--verbose":
                case "-v":
                    opts = opts with { Verbose = true };
                    break;
                case "--quiet":
                case "-q":
                    opts = opts with { Quiet = true };
                    break;
                case "--help":
                case "-h":
                    opts = opts with { ShowHelp = true };
                    break;
                default:
                    throw new ArgumentException($"Unknown option: {args[i]}");
            }
        }
        return opts;
    }

    /// <summary>
    /// Parse `zest preview` arguments.
    /// </summary>
    public static PreviewCommandOptions ParsePreview(string[] args)
    {
        var opts = new PreviewCommandOptions();
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--port":
                case "-p":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var p))
                        opts = opts with { Port = p };
                    else
                        throw new ArgumentException("--port requires a numeric value");
                    break;
                case "--host":
                    if (i + 1 < args.Length)
                        opts = opts with { Host = args[++i] };
                    else
                        throw new ArgumentException("--host requires a value");
                    break;
                case "--open":
                case "-o":
                    opts = opts with { OpenBrowser = true };
                    break;
                case "--verbose":
                case "-v":
                    opts = opts with { Verbose = true };
                    break;
                case "--quiet":
                case "-q":
                    opts = opts with { Quiet = true };
                    break;
                case "--help":
                case "-h":
                    opts = opts with { ShowHelp = true };
                    break;
                default:
                    throw new ArgumentException($"Unknown option: {args[i]}");
            }
        }
        return opts;
    }

    /// <summary>
    /// Parse `zest init` arguments.
    /// </summary>
    public static InitCommandOptions ParseInit(string[] args)
    {
        var targetDir = args.Length > 1 ? args[1] : ".";
        return new InitCommandOptions { TargetDirectory = targetDir };
    }

    /// <summary>
    /// Print build command help.
    /// </summary>
    public static void PrintBuildHelp()
    {
        Console.WriteLine("Usage: zest build [path] [options]");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  path              Project directory (default: current directory)");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --watch, -w       Watch for changes and auto-rebuild");
        Console.WriteLine("  --verbose, -v     Enable Debug-level logging");
        Console.WriteLine("  --quiet, -q       Suppress Info-level logs");
    }

    /// <summary>
    /// Print serve command help.
    /// </summary>
    public static void PrintServeHelp()
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
    }

    /// <summary>
    /// Print preview command help.
    /// </summary>
    public static void PrintPreviewHelp()
    {
        Console.WriteLine("Usage: zest preview [options]");
        Console.WriteLine();
        Console.WriteLine("Preview the built _site/ directory (no build triggered).");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --port, -p PORT     Preview server port (default: 8080)");
        Console.WriteLine("  --host HOST         Bind to host (default: localhost)");
        Console.WriteLine("  --open, -o          Open browser on start");
        Console.WriteLine("  --verbose, -v       Enable Debug-level logging");
        Console.WriteLine("  --quiet, -q         Suppress Info-level logs");
        Console.WriteLine("  --help, -h          Show this help");
    }
}
