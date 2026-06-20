using Zest.App.Controllers;

/*
 * ZEST = Zenith Efficient Static Toolkit (recursive acronym)
 *
 * Commands:
 *   zest build [--watch]           Build static site
 *   zest serve [--port PORT]       Build + start dev server with live reload
 *   zest preview [--port PORT]     Preview built _site/ directory (no build)
 *   zest init [path]               Scaffold new project
 *   zest --version                 Show version
 *   zest --help                    Show help
 */
public static class Program
{
    private const string Version = "0.3.0";

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                PrintHelp();
                return 0;
            }

            var command = args[0].ToLowerInvariant();

            return command switch
            {
                "build" => BuildController.Execute(args),
                "serve" or "dev" => ServeController.Execute(args),
                "preview" => ServeController.ExecutePreview(args),
                "init" => InitController.Execute(args),
                "--version" or "-v" => ShowVersion(),
                "--help" or "-h" => PrintHelp(),
                _ => UnknownCommand(command)
            };
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"[Zest] Fatal error: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }

    private static int ShowVersion()
    {
        Console.WriteLine($"Zest v{Version} — Zenith Efficient Static Toolkit");
        Console.WriteLine("Ecosystem: .zest.fsx + .zss + .toml + .js");
        return 0;
    }

    private static int PrintHelp()
    {
        Console.WriteLine($"Zest v{Version} — Zenith Efficient Static Toolkit");
        Console.WriteLine("(recursive acronym: ZEST = Zenith Efficient Static Toolkit)");
        Console.WriteLine();
        Console.WriteLine("Usage: zest <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  build                       Build static site to _site/");
        Console.WriteLine("  serve                       Build + start dev server (live reload)");
        Console.WriteLine("  preview                     Preview built _site/ directory");
        Console.WriteLine("  init                        Scaffold new project from template");
        Console.WriteLine("  --version, -v               Show version info");
        Console.WriteLine("  --help,    -h               Show this help");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --port, -p PORT             Server port (default: 8080)");
        Console.WriteLine("  --watch, -w                 Watch files and auto-rebuild");
        Console.WriteLine();
        Console.WriteLine("File formats:");
        Console.WriteLine("  .zest.fsx   F# template scripts with HTML DSL & Markdown");
        Console.WriteLine("  .zss        Zest Stylesheet — CSS superset with nesting/vars");
        Console.WriteLine("  .toml       Site config & global data (zero-config by default)");
        Console.WriteLine("  .js         Client scripts, copied as-is to output");
        Console.WriteLine("  .md         Standard Markdown content files");
        Console.WriteLine();
        Console.WriteLine("Run 'zest <command> --help' for command-specific options.");
        return 0;
    }

    private static int UnknownCommand(string cmd)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"Unknown command: '{cmd}'");
        Console.ResetColor();
        Console.WriteLine();
        PrintHelp();
        return 1;
    }
}
