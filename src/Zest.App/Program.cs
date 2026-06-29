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
            Logger.Error("Program", $"Fatal error: {ex.Message}", ex);
            return 1;
        }
    }

    private static int ShowVersion()
    {
        Logger.WriteAccent(string.Format(HelpText.Header, HelpText.Version));
        Logger.WriteDim(HelpText.Ecosystem);
        return 0;
    }

    private static int PrintHelp()
    {
        Logger.WriteAccent(string.Format(HelpText.Header, HelpText.Version));
        Logger.WriteDim(HelpText.Ecosystem);
        Console.WriteLine();

        Logger.WriteSection("Usage");
        Logger.WriteInfo(HelpText.Usage);
        Console.WriteLine();

        Logger.WriteSection("Commands");
        Logger.WriteInfo(HelpText.Commands);
        Logger.WriteInfo(HelpText.CommandsServe);
        Logger.WriteInfo(HelpText.CommandsPreview);
        Logger.WriteInfo(HelpText.CommandsInit);
        Logger.WriteInfo(HelpText.CommandsVersion);
        Logger.WriteInfo(HelpText.CommandsHelp);
        Console.WriteLine();

        Logger.WriteSection("Options");
        Logger.WriteInfo(HelpText.Options);
        Logger.WriteInfo(HelpText.OptionsWatch);
        Console.WriteLine();

        Logger.WriteSection("File Formats");
        Logger.WriteInfo(HelpText.FileFormats);
        Console.WriteLine();

        Logger.WriteDim($"  {HelpText.HelpSuffix}");
        return 0;
    }

    private static int UnknownCommand(string cmd)
    {
        Logger.WriteError($"  Unknown command: '{cmd}'");
        Console.WriteLine();
        PrintHelp();
        return 1;
    }
}
