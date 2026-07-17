using Zest.Infra.Services;

namespace Zest.App.Controllers;

/// <summary>
/// Handles `zest scaffold &lt;template&gt; [path]`
/// Generates a standard Zest project structure from a preset template.
/// Templates: blog, docs, portfolio, empty.
/// </summary>
public static class ScaffoldCommand
{
    private static readonly string[] ValidTemplates = { "blog", "docs", "portfolio", "empty" };

    public static int Execute(string[] args)
    {
        if (args.Length < 2)
        {
            LogWriter.WriteError("  Usage: zest scaffold <template> [path]");
            LogWriter.WriteDim($"    Available templates: {string.Join(", ", ValidTemplates)}");
            return 1;
        }

        var template = args[1].ToLowerInvariant();
        var targetDir = args.Length > 2 ? args[2] : ".";

        if (!ValidTemplates.Contains(template))
        {
            LogWriter.WriteError($"  Unknown template '{template}'. Available: {string.Join(", ", ValidTemplates)}");
            return 1;
        }

        // Resolve the template directory bundled with the app.
        var templateDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "..", "templates", template);
        if (!Directory.Exists(templateDir))
            templateDir = Path.Combine(Directory.GetCurrentDirectory(), "templates", template);

        if (!Directory.Exists(templateDir) && template != "empty")
        {
            // Fall back to the default template if the specific one is missing.
            LogWriter.WriteWarning($"  Template '{template}' not bundled; generating a minimal structure instead.");
            GenerateMinimalStructure(targetDir, template);
            LogWriter.WriteSuccess($"  [Zest] Scaffolded '{template}' project at '{targetDir}'");
            PrintNextSteps(targetDir);
            return 0;
        }

        if (template == "empty")
        {
            GenerateMinimalStructure(targetDir, "empty");
            LogWriter.WriteSuccess($"  [Zest] Created empty project at '{targetDir}'");
            PrintNextSteps(targetDir);
            return 0;
        }

        CopyDirectory(templateDir, targetDir);
        LogWriter.WriteSuccess($"  [Zest] Scaffolded '{template}' project at '{targetDir}'");
        PrintNextSteps(targetDir);
        return 0;
    }

    /// <summary>Generate a minimal, in-memory project structure for a template.</summary>
    private static void GenerateMinimalStructure(string target, string template)
    {
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(Path.Combine(target, "content"));
        Directory.CreateDirectory(Path.Combine(target, "_layouts"));
        Directory.CreateDirectory(Path.Combine(target, "assets"));

        // _config.toml — vary defaults per template
        var config = template switch
        {
            "blog" => MinimalConfigs.Blog,
            "docs" => MinimalConfigs.Docs,
            "portfolio" => MinimalConfigs.Portfolio,
            _ => MinimalConfigs.Empty
        };
        File.WriteAllText(Path.Combine(target, "_config.toml"), config, System.Text.Encoding.UTF8);

        // _init.zest.fsx — minimal init script
        File.WriteAllText(Path.Combine(target, "_init.zest.fsx"), MinimalConfigs.InitScript, System.Text.Encoding.UTF8);

        // A starter content page
        var starterPage = template switch
        {
            "blog" => MinimalConfigs.BlogStarter,
            "docs" => MinimalConfigs.DocsStarter,
            "portfolio" => MinimalConfigs.PortfolioStarter,
            _ => ""
        };
        if (!string.IsNullOrEmpty(starterPage))
            File.WriteAllText(Path.Combine(target, "content", "index.zest.fsx"), starterPage, System.Text.Encoding.UTF8);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            var tgt = Path.Combine(target, rel);
            var dir = Path.GetDirectoryName(tgt);
            if (dir is not null) Directory.CreateDirectory(dir);
            File.Copy(file, tgt, overwrite: true);
        }
    }

    private static void PrintNextSteps(string targetDir)
    {
        Console.WriteLine();
        LogWriter.WriteAccent("  Next steps:");
        LogWriter.WriteInfo("    1. cd " + targetDir);
        LogWriter.WriteInfo("    2. zest build              # Build the site");
        LogWriter.WriteInfo("    3. zest serve              # Start dev server");
    }

    private static class MinimalConfigs
    {
        public const string Empty = """
            # Zest site configuration
            [site]
            title = "My Zest Site"
            url = "https://example.com"

            [template]
            engine = "nunjucks"

            [build]
            output = "_site"
            """;

        public const string Blog = """
            # Zest blog configuration
            [site]
            title = "My Blog"
            description = "A blog built with Zest"
            url = "https://example.com"

            [template]
            engine = "nunjucks"

            [build]
            output = "_site"

            [collections]
            posts = "content/posts"
            """;

        public const string Docs = """
            # Zest docs configuration
            [site]
            title = "My Docs"
            description = "Documentation built with Zest"
            url = "https://example.com"

            [template]
            engine = "nunjucks"

            [build]
            output = "_site"

            [collections]
            docs = "content/docs"
            """;

        public const string Portfolio = """
            # Zest portfolio configuration
            [site]
            title = "My Portfolio"
            description = "Portfolio built with Zest"
            url = "https://example.com"

            [template]
            engine = "nunjucks"

            [build]
            output = "_site"
            """;

        public const string InitScript = """
            // _init.zest.fsx — Zest site initialization script
            // Register filters, global functions, and migrations here.

            open Zest.Dsl

            // Example: register a custom template filter
            // addFilter "shout" (fun (s: string) -> s.ToUpper())

            // Example: register a global template function
            // addGlobalFunction "siteName" (fun () -> "My Zest Site")
            """;

        public const string BlogStarter = """
            // content/index.zest.fsx — Blog home page
            open Zest.Dsl

            let recent = DslCollections.recent_pages 5

            html [
              head [
                title [ text "My Blog" ]
                meta [ attr "charset" "utf-8" ]
                stylesheet "/assets/style.css"
              ]
              body [
                h1 [ text "Welcome to My Blog" ]
                ul [
                    for p in recent do
                        li [ aHref p.url p.title ]
                ]
              ]
            ]
            """;

        public const string DocsStarter = """
            // content/index.zest.fsx — Docs home page
            open Zest.Dsl

            html [
              head [
                title [ text "My Docs" ]
                meta [ attr "charset" "utf-8" ]
                stylesheet "/assets/style.css"
              ]
              body [
                h1 [ text "Documentation" ]
                p [ text "Browse the docs using the navigation." ]
              ]
            ]
            """;

        public const string PortfolioStarter = """
            // content/index.zest.fsx — Portfolio home page
            open Zest.Dsl

            html [
              head [
                title [ text "My Portfolio" ]
                meta [ attr "charset" "utf-8" ]
                stylesheet "/assets/style.css"
              ]
              body [
                h1 [ text "My Work" ]
                p [ text "A selection of recent projects." ]
              ]
            ]
            """;
    }
}
