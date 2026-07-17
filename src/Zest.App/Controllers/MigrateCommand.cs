using Zest.Engine;
using Zest.Dsl;
using Zest.Infra.Services;

namespace Zest.App.Controllers;

/// <summary>
/// Handles `zest migrate &lt;source-ssg&gt; --from &lt;dir&gt; --to &lt;dir&gt; [--dry-run]`
/// Scans a source SSG project (Jekyll/Hexo/Hugo/11ty) and generates a
/// corresponding Zest project structure, converting frontmatter and config.
/// </summary>
public static class MigrateCommand
{
    private static readonly string[] SupportedSsgs = { "jekyll", "hexo", "hugo", "eleventy" };

    public static int Execute(string[] args)
    {
        // Args: [ "migrate", <source-ssg>, --from <dir>, --to <dir>, --dry-run ]
        if (args.Length < 2)
        {
            PrintUsage();
            return 1;
        }

        var ssg = args[1].ToLowerInvariant();
        if (!SupportedSsgs.Contains(ssg))
        {
            LogWriter.WriteError($"  Unknown source SSG '{ssg}'. Supported: {string.Join(", ", SupportedSsgs)}");
            return 1;
        }

        // Parse options
        string? fromDir = null;
        string? toDir = null;
        var dryRun = false;

        for (var i = 2; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "--from":
                    if (i + 1 < args.Length) { fromDir = args[++i]; }
                    break;
                case "--to":
                    if (i + 1 < args.Length) { toDir = args[++i]; }
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                default:
                    if (a.StartsWith("--from=", StringComparison.Ordinal)) fromDir = a["--from=".Length..];
                    else if (a.StartsWith("--to=", StringComparison.Ordinal)) toDir = a["--to=".Length..];
                    else LogWriter.WriteWarning($"  Ignoring unknown option: {a}");
                    break;
            }
        }

        fromDir ??= Directory.GetCurrentDirectory();
        toDir ??= Path.Combine(fromDir, "_zest_migrated");

        if (!Directory.Exists(fromDir))
        {
            LogWriter.WriteError($"  Source directory not found: {fromDir}");
            return 1;
        }

        LogWriter.WriteAccent($"  [Zest] Migrating {ssg} project → Zest");
        LogWriter.WriteInfo($"    Source: {fromDir}");
        LogWriter.WriteInfo($"    Target: {toDir}");
        if (dryRun) LogWriter.WriteDim("    Mode:   dry-run (no files written)");

        var plan = BuildMigrationPlan(ssg, fromDir, toDir);

        LogWriter.WriteSection($"  Migration plan ({plan.Count} files)");
        foreach (var (relPath, _) in plan.Take(20))
            LogWriter.WriteDim($"    {relPath}");
        if (plan.Count > 20)
            LogWriter.WriteDim($"    ... and {plan.Count - 20} more");

        if (dryRun)
        {
            LogWriter.WriteSuccess("  [Zest] Dry run complete. No files written.");
            return 0;
        }

        // Write files
        foreach (var (relPath, content) in plan)
        {
            var fullPath = Path.Combine(toDir, relPath);
            var dir = Path.GetDirectoryName(fullPath);
            if (dir is not null) Directory.CreateDirectory(dir);
            File.WriteAllText(fullPath, content, System.Text.Encoding.UTF8);
        }

        LogWriter.WriteSuccess($"  [Zest] Migration complete. {plan.Count} files written to '{toDir}'.");
        Console.WriteLine();
        LogWriter.WriteAccent("  Next steps:");
        LogWriter.WriteInfo($"    1. cd {toDir}");
        LogWriter.WriteInfo("    2. Review _config.toml and _init.zest.fsx");
        LogWriter.WriteInfo("    3. zest build");
        return 0;
    }

    /// <summary>
    /// Build the list of (relativePath, content) files that constitute the
    /// migrated Zest project. Mirrors the source SSG's content/layout structure.
    /// </summary>
    private static List<(string, string)> BuildMigrationPlan(string ssg, string fromDir, string toDir)
    {
        var plan = new List<(string, string)>();

        // 1. Generate _config.toml from the source SSG's config.
        var config = GenerateConfig(ssg, fromDir);
        plan.Add(("_config.toml", config));

        // 2. Generate _init.zest.fsx with compat flags enabled.
        plan.Add(("_init.zest.fsx", GenerateInitScript(ssg)));

        // 3. Migrate content files (markdown with frontmatter).
        var contentDirs = GetContentDirectories(ssg, fromDir);
        foreach (var contentDir in contentDirs)
        {
            if (!Directory.Exists(contentDir)) continue;
            foreach (var file in Directory.EnumerateFiles(contentDir, "*.*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is not FileExtensions.Markdown and not FileExtensions.MarkdownLong and not FileExtensions.Html) continue;

                var content = File.ReadAllText(file, System.Text.Encoding.UTF8);
                // Convert YAML frontmatter → TOML for Zest-native format.
                var converted = TryConvertFrontmatter(content, "toml");
                var rel = Path.GetRelativePath(fromDir, file);
                // Normalize: place under content/ for Zest
                var zestRel = rel.StartsWith("content", StringComparison.OrdinalIgnoreCase)
                    ? rel
                    : Path.Combine("content", rel);
                plan.Add((zestRel, converted));
            }
        }

        // 4. Copy static assets (images, css, js) into assets/.
        foreach (var assetDir in GetAssetDirectories(ssg, fromDir))
        {
            if (!Directory.Exists(assetDir)) continue;
            foreach (var file in Directory.EnumerateFiles(assetDir, "*.*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is FileExtensions.Markdown or FileExtensions.MarkdownLong) continue;
                var content = File.ReadAllText(file, System.Text.Encoding.UTF8);
                var rel = Path.GetRelativePath(fromDir, file);
                plan.Add((Path.Combine("assets", Path.GetFileName(rel)), content));
            }
        }

        return plan;
    }

    /// <summary>Convert frontmatter if present; fall back to original on error.</summary>
    private static string TryConvertFrontmatter(string content, string targetFormat)
    {
        try { return DslMigration.convertFrontmatter(content, targetFormat); }
        catch { return content; }
    }

    private static string[] GetContentDirectories(string ssg, string root) => ssg switch
    {
        "jekyll" => new[] { Path.Combine(root, "_posts"), Path.Combine(root, "_pages"), root },
        "hexo" => new[] { Path.Combine(root, "source", "_posts"), Path.Combine(root, "source") },
        "hugo" => new[] { Path.Combine(root, "content") },
        "eleventy" => new[] { Path.Combine(root, "src"), Path.Combine(root, "content") },
        _ => new[] { root }
    };

    private static string[] GetAssetDirectories(string ssg, string root) => ssg switch
    {
        "jekyll" => new[] { Path.Combine(root, "assets"), Path.Combine(root, "css"), Path.Combine(root, "js") },
        "hexo" => new[] { Path.Combine(root, "source", "assets"), Path.Combine(root, "themes") },
        "hugo" => new[] { Path.Combine(root, "static"), Path.Combine(root, "assets") },
        "eleventy" => new[] { Path.Combine(root, "assets"), Path.Combine(root, "public") },
        _ => Array.Empty<string>()
    };

    /// <summary>Generate a Zest _config.toml with compat flags for the source SSG.</summary>
    private static string GenerateConfig(string ssg, string fromDir)
    {
        var siteTitle = Capitalize(ssg) + " Site";
        // Attempt to read the source config for title/url.
        TryReadSourceSiteMeta(ssg, fromDir, ref siteTitle, out var siteUrl);

        // Zest uses flat config keys (title, base_url, output_dir, ...).
        // [compat] and [template] are the two nested tables, matching the
        // CLAUDE.md spec for SSG-migration compatibility flags.
        var compatLine = ssg switch
        {
            "jekyll" => "jekyll = true",
            "hexo" => "hexo = true",
            "hugo" => "hugo = true",
            "eleventy" => "eleventy = true",
            _ => ""
        };
        var compatBlock = string.IsNullOrEmpty(compatLine)
            ? ""
            : $"\n[compat]\n{compatLine}\n";

        return $"""
            # Zest site configuration — migrated from {ssg}
            title = "{EscapeToml(siteTitle)}"
            base_url = "{siteUrl}"
            description = "A site migrated from {ssg} and built with Zest SSG."
            output_dir = "./_site"
            content_dir = "./content"
            layouts_dir = "./_layouts"
            includes_dir = "./_includes"
            data_dir = "./_data"
            assets_dir = "./assets"
            default_layout = "default"
            template_engine = "native"

            [template]
            engine = "native"
            nunjucks.compatibility = "zest"{compatBlock}
            """;
    }

    private static void TryReadSourceSiteMeta(string ssg, string fromDir, ref string title, out string url)
    {
        url = "https://example.com";
        try
        {
            var cfgPath = ssg switch
            {
                "jekyll" or "hexo" => Path.Combine(fromDir, "_config.yml"),
                "hugo" => Path.Combine(fromDir, "config.toml"),
                "eleventy" => Path.Combine(fromDir, ".eleventy.js"),
                _ => null
            };
            if (cfgPath is null || !File.Exists(cfgPath)) return;
            var text = File.ReadAllText(cfgPath, System.Text.Encoding.UTF8);
            // Crude title extraction (good enough for migration seeding).
            var titleMatch = System.Text.RegularExpressions.Regex.Match(
                text, @"(?m)^\s*title\s*[:=]\s*[""']?(.*?)[""']?\s*$");
            if (titleMatch.Success) title = titleMatch.Groups[1].Value.Trim();
            var urlMatch = System.Text.RegularExpressions.Regex.Match(
                text, @"(?m)^\s*(?:url|baseurl|baseURL)\s*[:=]\s*[""']?(.*?)[""']?\s*$");
            if (urlMatch.Success) url = urlMatch.Groups[1].Value.Trim();
        }
        catch { /* ignore — keep defaults */ }
    }

    private static string GenerateInitScript(string ssg)
    {
        return $$"""
            // _init.zest.fsx — migrated from {{ssg}}
            // Compat mode is enabled in _config.toml under [compat].

            open Zest.Dsl

            // Register any custom migration logic here.
            // DslMigration.registerMigration (fun src tgt -> [])
            """;
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static string EscapeToml(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static void PrintUsage()
    {
        LogWriter.WriteError("  Usage: zest migrate <source-ssg> --from <dir> --to <dir> [--dry-run]");
        LogWriter.WriteDim("    source-ssg: jekyll | hexo | hugo | eleventy");
        LogWriter.WriteDim("    --from      Source site directory (default: current dir)");
        LogWriter.WriteDim("    --to        Target Zest project directory (default: ./_zest_migrated)");
        LogWriter.WriteDim("    --dry-run   Print migration plan without writing files");
        LogWriter.WriteDim("    Example: zest migrate jekyll --from ./my-blog --to ./zest-blog");
    }
}
