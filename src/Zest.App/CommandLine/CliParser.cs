using Zest.Infra.Services;

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
            if (TryApplyCommonOption(ref opts, args[i])) continue;

            switch (args[i].ToLowerInvariant())
            {
                case "--watch":
                case "-w":
                    opts = opts with { Watch = true };
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
            if (TryApplyCommonOption(ref opts, args[i])) continue;

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
            if (TryApplyCommonOption(ref opts, args[i])) continue;

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
    /// Apply common options (--verbose, --quiet, --help) to any command options record.
    /// Returns true if the argument was a recognized common option.
    /// </summary>
    private static bool TryApplyCommonOption<T>(ref T opts, string arg) where T : CommandOptions
    {
        switch (arg.ToLowerInvariant())
        {
            case "--verbose":
            case "-v":
                opts = (T)opts with { Verbose = true };
                return true;
            case "--quiet":
            case "-q":
                opts = (T)opts with { Quiet = true };
                return true;
            case "--help":
            case "-h":
                opts = (T)opts with { ShowHelp = true };
                return true;
            default:
                return false;
        }
    }

    // ── Command-specific help pages ────────────────────────

    /// <summary>
    /// Print build command help.
    /// </summary>
    public static void PrintBuildHelp()
    {
        Logger.WriteSection("Usage");
        Logger.WriteInfo("  zest build [path] [options]");
        Console.WriteLine();

        Logger.WriteSection("Arguments");
        Logger.WriteInfo("  path              Project directory (default: current directory)");
        Console.WriteLine();

        Logger.WriteSection("Options");
        Logger.WriteInfo("  --watch, -w       Watch for changes and auto-rebuild");
        Logger.WriteInfo("  --verbose, -v     Enable Debug-level logging");
        Logger.WriteInfo("  --quiet, -q       Suppress Info-level logs");
    }

    /// <summary>
    /// Print serve command help.
    /// </summary>
    public static void PrintServeHelp()
    {
        Logger.WriteSection("Usage");
        Logger.WriteInfo("  zest serve [options]");
        Console.WriteLine();

        Logger.WriteDim("  Start the development server with live reload.");
        Console.WriteLine();

        Logger.WriteSection("Options");
        Logger.WriteInfo("  --port, -p PORT     Dev server port (default: 8080)");
        Logger.WriteInfo("  --host HOST         Bind to host (default: localhost)");
        Logger.WriteInfo("  --open, -o          Open browser on start");
        Logger.WriteInfo("  --verbose, -v       Show detailed FSI output");
        Logger.WriteInfo("  --quiet, -q         Suppress INFO logs");
        Logger.WriteInfo("  --help, -h          Show this help");
    }

    /// <summary>
    /// Print preview command help.
    /// </summary>
    public static void PrintPreviewHelp()
    {
        Logger.WriteSection("Usage");
        Logger.WriteInfo("  zest preview [options]");
        Console.WriteLine();

        Logger.WriteDim("  Preview the built _site/ directory (no build triggered).");
        Console.WriteLine();

        Logger.WriteSection("Options");
        Logger.WriteInfo("  --port, -p PORT     Preview server port (default: 8080)");
        Logger.WriteInfo("  --host HOST         Bind to host (default: localhost)");
        Logger.WriteInfo("  --open, -o          Open browser on start");
        Logger.WriteInfo("  --verbose, -v       Enable Debug-level logging");
        Logger.WriteInfo("  --quiet, -q         Suppress Info-level logs");
        Logger.WriteInfo("  --help, -h          Show this help");
    }
}
