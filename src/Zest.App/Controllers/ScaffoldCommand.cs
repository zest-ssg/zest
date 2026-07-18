using Zest.Infra.Services;

namespace Zest.App.Controllers;

/// <summary>
/// Handles `zest scaffold &lt;template&gt; [path]`
/// Generates a Zest project structure. Two starters are available:
///   - "blog"  : the full bundled starter site, shared with `zest init`
///              (extracted from embedded resources, with a dev-disk fallback).
///   - "empty" : a minimal config-only project with no starter content.
/// </summary>
public static class ScaffoldCommand
{
    private static readonly string[] ValidTemplates = { "blog", "empty" };

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

        if (template == "empty")
        {
            // Minimal, config-only project with no starter content.
            GenerateMinimalStructure(targetDir);
            LogWriter.WriteSuccess($"  [Zest] Created empty project at '{targetDir}'");
        }
        else
        {
            // Every real starter variant reuses the bundled starter site shared
            // with `zest init` (embedded resources, with a dev-disk fallback).
            InitController.ExtractBundledStarter(targetDir);
            LogWriter.WriteSuccess($"  [Zest] Scaffolded '{template}' project at '{targetDir}'");
        }

        PrintNextSteps(targetDir);
        return 0;
    }

    /// <summary>Generate a minimal, config-only project (no starter content).</summary>
    private static void GenerateMinimalStructure(string target)
    {
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(Path.Combine(target, "content"));
        Directory.CreateDirectory(Path.Combine(target, "_layouts"));
        Directory.CreateDirectory(Path.Combine(target, "assets"));

        File.WriteAllText(Path.Combine(target, "_config.toml"), MinimalConfigs.Empty, System.Text.Encoding.UTF8);
        File.WriteAllText(Path.Combine(target, "_init.zest.fsx"), MinimalConfigs.InitScript, System.Text.Encoding.UTF8);
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

        public const string InitScript = """
            // _init.zest.fsx — Zest site initialization script
            // Register filters, global functions, and migrations here.

            open Zest.Dsl

            // Example: register a custom template filter
            // addFilter "shout" (fun (s: string) -> s.ToUpper())

            // Example: register a global template function
            // addGlobalFunction "siteName" (fun () -> "My Zest Site")
            """;
    }
}
