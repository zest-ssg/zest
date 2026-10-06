using System.Globalization;
using System.Text;
using Zest.App.Cli;
using Zest.App.Command;
using Zest.App.Runtime;

// Program.cs
//
// CLI entry point and command dispatcher. The first argument selects the
// controller; the remaining arguments pass through unmodified so each
// controller owns its own option parsing. All help copy lives in
// .config/zest/help.toml so there is one place to edit it.
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
                "build" => BuildCommand.Execute(args),
                "serve" or "dev" => ServeCommand.Execute(args),
                "preview" => ServeCommand.ExecutePreview(args),
                "init" => InitCommand.Execute(args),
                "clean" => CleanCommand.Execute(args),
                "--version" or "-v" => ShowVersion(),
                "--help" or "-h" or "help" => PrintHelp(),
                _ => UnknownCommand(command)
            };
        }
        catch (Exception ex)
        {
            // The build animation temporarily replaces Console.Out/Error with
            // proxies; a fatal error must not be written through one of them.
            BuildAnimator.Restore();
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
        foreach (var row in HelpRenderer.Commands)
            WriteRow(row.Usage, row.Description);

        WriteSection("Options");
        foreach (var row in HelpRenderer.Options)
            WriteRow(row.Usage, row.Description);

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

        // Pad by display columns, not by UTF-16 code units: a CJK character
        // occupies two terminal columns and would otherwise skew the column.
        var pad = RowWidth - DisplayWidth(invocation);
        Console.Write(pad > 0 ? new string(' ', pad) : "  ");
        LogWriter.WriteDim(description);
    }

    /// <summary>Number of terminal columns the text occupies.</summary>
    private static int DisplayWidth(string text)
    {
        var width = 0;
        foreach (var ch in text)
            width += IsWideCharacter(ch) ? 2 : 1;
        return width;
    }

    /// <summary>
    /// Approximate East-Asian Wide/Fullwidth test, which is all the help table
    /// needs to keep descriptions aligned.
    /// </summary>
    private static bool IsWideCharacter(char c)
    {
        if (c < 0x1100) return false;
        return c <= 0x115F                       // Hangul Jamo
            || (c >= 0x2E80 && c <= 0xA4CF)      // CJK radicals … Yi
            || (c >= 0xAC00 && c <= 0xD7A3)      // Hangul syllables
            || (c >= 0xF900 && c <= 0xFAFF)      // CJK compatibility ideographs
            || (c >= 0xFE30 && c <= 0xFE6F)      // CJK compatibility forms
            || (c >= 0xFF00 && c <= 0xFF60)      // Fullwidth forms
            || (c >= 0xFFE0 && c <= 0xFFE6);
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
