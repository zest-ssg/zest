using Zest.App.Runtime;

namespace Zest.App.Cli;

/// <summary>
/// Unified CLI argument parser for Zest commands.
/// Eliminates duplicate inline parsing across controllers.
///
/// Every command rejects unknown options with <see cref="ArgumentException"/>
/// rather than ignoring them: a silently dropped flag is indistinguishable
/// from a flag that did nothing, which hides typos and unimplemented switches.
/// </summary>
internal static class CliParser
{
    /// <summary>
    /// Parse `zest build [path] [--watch] [--no-incremental]`.
    /// </summary>
    public static BuildCommandOptions ParseBuild(string[] args)
    {
        var opts = new BuildCommandOptions();
        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (TryReadCommonOption(arg, out var common))
            {
                opts = opts.WithCommon(common.Verbose, common.Quiet, common.ShowHelp);
                continue;
            }

            switch (arg.ToLowerInvariant())
            {
                case "--watch":
                case "-w":
                    opts = opts with { Watch = true };
                    break;
                case "--no-incremental":
                    opts = opts with { NoIncremental = true };
                    break;
                default:
                    if (arg.StartsWith('-'))
                        throw new ArgumentException($"Unknown option: {arg}");
                    if (opts.ProjectPath != null)
                        throw new ArgumentException($"Unexpected argument: {arg}");
                    opts = opts with { ProjectPath = arg };
                    break;
            }
        }
        return opts;
    }

    /// <summary>
    /// Parse `zest serve [options]`.
    /// </summary>
    public static ServeCommandOptions ParseServe(string[] args)
    {
        var opts = new ServeCommandOptions();
        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (TryReadCommonOption(arg, out var common))
            {
                opts = opts.WithCommon(common.Verbose, common.Quiet, common.ShowHelp);
                continue;
            }

            switch (arg.ToLowerInvariant())
            {
                case "--port":
                case "-p":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var port))
                        opts = opts with { PortOverride = port };
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
                    throw new ArgumentException($"Unknown option: {arg}");
            }
        }
        return opts;
    }

    /// <summary>
    /// Parse `zest preview [options]`.
    /// </summary>
    public static PreviewCommandOptions ParsePreview(string[] args)
    {
        var opts = new PreviewCommandOptions();
        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (TryReadCommonOption(arg, out var common))
            {
                opts = opts.WithCommon(common.Verbose, common.Quiet, common.ShowHelp);
                continue;
            }

            switch (arg.ToLowerInvariant())
            {
                case "--port":
                case "-p":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var port))
                        opts = opts with { Port = port };
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
                    throw new ArgumentException($"Unknown option: {arg}");
            }
        }
        return opts;
    }

    /// <summary>
    /// Parse `zest init [path] [--empty]`.
    /// </summary>
    public static InitCommandOptions ParseInit(string[] args)
    {
        var opts = new InitCommandOptions();
        string? target = null;

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (TryReadCommonOption(arg, out var common))
            {
                opts = opts.WithCommon(common.Verbose, common.Quiet, common.ShowHelp);
                continue;
            }

            switch (arg.ToLowerInvariant())
            {
                case "--empty":
                    opts = opts with { Empty = true };
                    break;
                default:
                    if (arg.StartsWith('-'))
                        throw new ArgumentException($"Unknown option: {arg}");
                    if (target != null)
                        throw new ArgumentException($"Unexpected argument: {arg}");
                    target = arg;
                    break;
            }
        }

        return target is null ? opts : opts with { TargetDirectory = target };
    }

    /// <summary>
    /// Recognise a common option and report which one it was. Only the
    /// matching field is non-null, so the others keep their current value.
    /// </summary>
    private static bool TryReadCommonOption(string arg, out (bool? Verbose, bool? Quiet, bool? ShowHelp) common)
    {
        switch (arg.ToLowerInvariant())
        {
            case "--verbose":
            case "-v":
                common = (Verbose: true, Quiet: null, ShowHelp: null);
                return true;
            case "--quiet":
            case "-q":
                common = (Verbose: null, Quiet: true, ShowHelp: null);
                return true;
            case "--help":
            case "-h":
                common = (Verbose: null, Quiet: null, ShowHelp: true);
                return true;
            default:
                common = default;
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
