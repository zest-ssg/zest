using System.Globalization;
using System.Text;
using Zest.App.CommandLine;
using Zest.App.Controllers;
using Zest.Infra.Services;

// Program.cs
//
// CLI entry point and command dispatcher. The first argument selects the
// controller; the remaining arguments pass through unmodified so each
// controller owns its own option parsing.
namespace Zest.App;

public static class Program
{
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
                "scaffold" => ScaffoldCommand.Execute(args),
                "migrate" => MigrateCommand.Execute(args),
                "convert-config" or "convert_config" => ConfigConverter.Execute(args),
                "clean" => CleanController.Execute(args),
                "--version" or "-v" => ShowVersion(),
                "--help" or "-h" or "help" => PrintHelp(),
                _ => UnknownCommand(command)
            };
        }
        catch (Exception ex)
        {
            LogWriter.Error("Program", $"Fatal error: {ex.Message}", ex);
            return 1;
        }
    }

    private static readonly CompositeFormat _headerFormat = CompositeFormat.Parse(HelpRenderer.Header);

    // Fixed-width left column for command/option rows so all descriptions
    // start on the same column regardless of glyph count.
    private const int RowWidth = 28;
    private const string RowIndent = "    ";

    private static int ShowVersion()
    {
        PrintBanner();
        return 0;
    }

    private static int PrintHelp()
    {
        PrintBanner();

        WriteSection("Usage");
        WriteRow("zest <command> [options]", null);

        WriteSection("Commands");
        WriteRow("build [path]", "Build the site into _site/");
        WriteRow("serve [path]", "Build and start the dev server");
        WriteRow("preview [path]", "Serve _site/ without rebuilding");
        WriteRow("init [path]", "Scaffold a new project");
        WriteRow("scaffold <template> [path]", "Generate a project from a preset");
        WriteRow("migrate <source-ssg>", "Migrate from Jekyll, Hexo, Hugo or 11ty");
        WriteRow("convert-config <from> <to>", "Convert a config between YAML and TOML");
        WriteRow("clean", "Clear build artifacts");
        WriteRow("help", "Show this help message");

        WriteSection("Options");
        WriteRow("-p, --port <port>", "Server port (default: 8080)");
        WriteRow("-w, --watch", "Watch files and rebuild on change");
        WriteRow("-v, --verbose", "Enable debug-level logging");
        WriteRow("-q, --quiet", "Suppress info-level output");
        WriteRow("-h, --help", "Show help");
        WriteRow("--version", "Show the version");

        Console.WriteLine();
        LogWriter.WriteDim($"  {HelpRenderer.HelpSuffix}");
        Console.WriteLine();
        return 0;
    }

    /// <summary>Print the branded header: glyph, name and tagline.</summary>
    private static void PrintBanner()
    {
        var header = string.Format(CultureInfo.InvariantCulture, _headerFormat, HelpRenderer.Version);

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("  ⚡ ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(header);
        Console.ResetColor();
        LogWriter.WriteDim($"     {HelpRenderer.Ecosystem}");
    }

    /// <summary>Write an uppercase section label in the accent color.</summary>
    private static void WriteSection(string title) =>
        LogWriter.WriteSection(title.ToUpperInvariant());

    /// <summary>
    /// Write an aligned two-column row: white invocation followed by a dim
    /// description. A null description renders the invocation as plain info
    /// (used for the usage example).
    /// </summary>
    private static void WriteRow(string invocation, string? description)
    {
        Console.Write(RowIndent);
        Console.ForegroundColor = description is null ? ConsoleColor.Gray : ConsoleColor.White;
        Console.Write(invocation);
        Console.ResetColor();

        if (description is null)
        {
            Console.WriteLine();
            return;
        }

        var pad = RowWidth - invocation.Length;
        Console.Write(pad > 0 ? new string(' ', pad) : "  ");
        LogWriter.WriteDim(description);
    }

    private static int UnknownCommand(string cmd)
    {
        Console.WriteLine();
        LogWriter.WriteError($"  Unknown command: '{cmd}'");
        LogWriter.WriteDim("  Run 'zest --help' to see the available commands.");
        Console.WriteLine();
        return 1;
    }
}
