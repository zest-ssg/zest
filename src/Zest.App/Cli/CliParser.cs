using Zest.App.Runtime;

namespace Zest.App.Cli;

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
                    if (opts.ProjectPath == null && !args[i].StartsWith('-'))
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
                case "--spa":
                    opts = opts with { SPA = true };
                    break;
                case "--dir":
                    opts = opts with { DirectoryListing = true };
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
                case "--watch":
                case "-w":
                    opts = opts with { Watch = true };
                    break;
                case "--livereload":
                case "-l":
                    opts = opts with { LiveReload = true };
                    break;
                case "--spa":
                    opts = opts with { SPA = true };
                    break;
                case "--dir":
                    opts = opts with { DirectoryListing = true };
                    break;
                default:
                    throw new ArgumentException($"Unknown option: {args[i]}");
            }
        }
        return opts;
    }

    /// <summary>
    /// Parse `zest init` arguments: an optional path plus --empty.
    /// </summary>
    public static InitCommandOptions ParseInit(string[] args)
    {
        var target = ".";
        var empty = false;

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--empty":
                    empty = true;
                    break;
                default:
                    if (target == "." && !args[i].StartsWith('-')) target = args[i];
                    break;
            }
        }

        return new InitCommandOptions { TargetDirectory = target, Empty = empty };
    }

    /// <summary>
    /// Apply common options (--verbose, --quiet, --help) to any command options record.
    /// Returns true if the argument was a recognized common option.
    /// </summary>
    private static bool TryApplyCommonOption<T>(ref T opts, string arg) where T : CliOptions
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
    // Copy for every page lives in .config/zest/help.toml under a table named
    // after the command, so the CLI has exactly one place to edit help text.

    /// <summary>Print the help page of a command, read from help.toml.</summary>
    public static void PrintCommandHelp(string command)
    {
        LogWriter.WriteSection("Usage");
        LogWriter.WriteInfo($"  {HelpRenderer.CommandUsage(command)}");
        Console.WriteLine();

        LogWriter.WriteSection("Options");
        foreach (var option in HelpRenderer.CliOptions(command))
            LogWriter.WriteInfo($"  {option}");
    }
}
