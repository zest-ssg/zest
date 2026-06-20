using Zest.Engine;
using Zest.Infra.Configuration;
using Zest.Infra.Services;

namespace Zest.App.Controllers;

/// <summary>
/// Handles zest serve / zest preview commands.
/// </summary>
public static class ServeController
{
    /// <summary>
    /// Build + start dev server with live reload.
    /// </summary>
    public static int Execute(string[] args)
    {
        int? portOverride = null;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--port":
                case "-p":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var p))
                        portOverride = p;
                    else
                    {
                        Console.Error.WriteLine("Error: --port requires a numeric value");
                        return 1;
                    }
                    break;
                case "--help":
                    Console.WriteLine("Usage: zest serve [options]");
                    Console.WriteLine();
                    Console.WriteLine("Options:");
                    Console.WriteLine("  --port, -p PORT   Dev server port (default: 8080)");
                    return 0;
            }
        }

        var config = SiteConfigLoader.Load();
        if (portOverride.HasValue)
        {
            config = new SiteConfig(
                title: config.Title,
                baseUrl: config.BaseUrl,
                description: config.Description,
                contentDir: config.ContentDir,
                outputDir: config.OutputDir,
                layoutsDir: config.LayoutsDir,
                includesDir: config.IncludesDir,
                dataDir: config.DataDir,
                assetsDir: config.AssetsDir,
                defaultLayout: config.DefaultLayout,
                permalinkFormat: config.PermalinkFormat,
                devServerPort: portOverride.Value,
                liveReloadPort: config.LiveReloadPort,
                enableMinification: config.EnableMinification,
                enableCacheBusting: config.EnableCacheBusting,
                siteVersion: config.SiteVersion
            );
        }

        using var server = new DevServerService(config);
        server.Start();

        var evt = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, args) =>
        {
            Console.WriteLine("[Zest] Shutting down...");
            server.Stop();
            evt.Set();
            args.Cancel = true;
        };
        evt.Wait();
        return 0;
    }

    /// <summary>
    /// Preview mode: serve _site/ directory directly without building.
    /// </summary>
    public static int ExecutePreview(string[] args)
    {
        int port = 8080;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--port":
                case "-p":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var p))
                        port = p;
                    else
                    {
                        Console.Error.WriteLine("Error: --port requires a numeric value");
                        return 1;
                    }
                    break;
                case "--help":
                    Console.WriteLine("Usage: zest preview [options]");
                    Console.WriteLine();
                    Console.WriteLine("Options:");
                    Console.WriteLine("  --port, -p PORT   Preview server port (default: 8080)");
                    return 0;
            }
        }

        var config = SiteConfigLoader.Load();
        using var server = new PreviewService(config, port);
        server.Start();

        var evt = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, args) =>
        {
            Console.WriteLine("[Zest] Shutting down preview server...");
            server.Stop();
            evt.Set();
            args.Cancel = true;
        };
        evt.Wait();
        return 0;
    }
}
