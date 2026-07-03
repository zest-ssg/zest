using System.Globalization;
using System.Text;
using Zest.App.CommandLine;
using Zest.App.Controllers;
using Zest.Infra.Services;

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
                "--version" or "-v" => ShowVersion(),
                "--help" or "-h" => PrintHelp(),
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

    private static int ShowVersion()
    {
        LogWriter.WriteAccent(string.Format(CultureInfo.InvariantCulture, _headerFormat, HelpRenderer.Version));
        LogWriter.WriteDim(HelpRenderer.Ecosystem);
        return 0;
    }

    private static int PrintHelp()
    {
        LogWriter.WriteAccent(string.Format(CultureInfo.InvariantCulture, _headerFormat, HelpRenderer.Version));
        LogWriter.WriteDim(HelpRenderer.Ecosystem);
        Console.WriteLine();

        LogWriter.WriteSection("Usage");
        LogWriter.WriteInfo(HelpRenderer.Usage);
        Console.WriteLine();

        LogWriter.WriteSection("Commands");
        LogWriter.WriteInfo(HelpRenderer.Commands);
        LogWriter.WriteInfo(HelpRenderer.CommandsServe);
        LogWriter.WriteInfo(HelpRenderer.CommandsPreview);
        LogWriter.WriteInfo(HelpRenderer.CommandsInit);
        LogWriter.WriteInfo(HelpRenderer.CommandsVersion);
        LogWriter.WriteInfo(HelpRenderer.CommandsHelp);
        Console.WriteLine();

        LogWriter.WriteSection("Options");
        LogWriter.WriteInfo(HelpRenderer.Options);
        LogWriter.WriteInfo(HelpRenderer.OptionsWatch);
        Console.WriteLine();

        LogWriter.WriteSection("File Formats");
        LogWriter.WriteInfo(HelpRenderer.FileFormats);
        Console.WriteLine();

        LogWriter.WriteDim($"  {HelpRenderer.HelpSuffix}");
        return 0;
    }

    private static int UnknownCommand(string cmd)
    {
        LogWriter.WriteError($"  Unknown command: '{cmd}'");
        Console.WriteLine();
        PrintHelp();
        return 1;
    }
}
