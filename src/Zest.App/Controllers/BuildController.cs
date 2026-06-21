using Zest.Engine;
using Zest.Infra.Configuration;
using Zest.Infra.Services;

namespace Zest.App.Controllers;

/// <summary>
/// Handles `zest build [--watch] [--no-incremental]`
/// </summary>
public static class BuildController
{
    public static int Execute(string[] args)
    {
        var watch = false;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--watch":
                case "-w":
                    watch = true;
                    break;
                case "--help":
                    Console.WriteLine("Usage: zest build [options]");
                    Console.WriteLine();
                    Console.WriteLine("Options:");
                    Console.WriteLine("  --watch, -w   Watch for changes and auto-rebuild");
                    return 0;
            }
        }

        Console.WriteLine("[Zest] Loading config...");
        Console.Out.Flush();
        var config = SiteConfigLoader.Load();
        Console.WriteLine("[Zest] Config loaded: " + config.Title);
        Console.Out.Flush();
        var buildSvc = new BuildService();
        var result = buildSvc.Execute(config);

        BuildService.PrintResult(result, config);

        if (watch)
            BuildService.StartWatcher(config);

        return result.Success ? 0 : 1;
    }
}
