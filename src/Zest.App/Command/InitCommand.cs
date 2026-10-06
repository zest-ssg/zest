using System.Reflection;
using System.Text;
using Zest.App.Cli;
using Zest.App.Runtime;

namespace Zest.App.Command;

/// <summary>
/// Handles `zest init [path] [--empty]`.
/// Writes the bundled starter site that is embedded inside this assembly, so
/// it works out of the box after a `dotnet tool install` without relying on
/// any on-disk starter folder. `--empty` writes the conventional directory
/// layout with no content, for sites that start from scratch.
/// </summary>
public static class InitCommand
{
    // LogicalName prefix produced by the EmbeddedResource items in the csproj.
    private const string ResourcePrefix = "Zest.App.Starter.";

    /// <summary>Directory names never copied out of an on-disk starter.</summary>
    private static readonly HashSet<string> SkipDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", "bin", "obj", "node_modules"
    };

    // Writing a BOM into Markdown/HTML files would surface as a stray
    // character in rendered output and diffs.
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static int Execute(string[] args)
    {
        InitCommandOptions opts;
        try
        {
            opts = CliParser.ParseInit(args);
        }
        catch (ArgumentException ex)
        {
            LogWriter.WriteError($"  Error: {ex.Message}");
            return 1;
        }

        if (opts.ShowHelp)
        {
            CliParser.PrintCommandHelp("init");
            return 0;
        }

        LogWriter.SetQuiet(opts.Quiet);

        var targetDir = opts.TargetDirectory;
        var targetFull = Path.GetFullPath(targetDir);

        // Warn about any non-empty target, not just ".": the previous check
        // compared strings, so `zest init ./` skipped the prompt entirely.
        if (Directory.Exists(targetFull) && Directory.EnumerateFileSystemEntries(targetFull).Any())
        {
            LogWriter.WriteWarning($"  Warning: '{targetDir}' is not empty.");
            Console.Write("  Continue anyway? (y/N): ");
            var resp = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (resp is not ("y" or "yes"))
            {
                LogWriter.WriteDim("  Aborted.");
                return 1;
            }
        }

        var created = opts.Empty
            ? GenerateEmptyLayout(targetDir)
            : ExtractBundledStarter(targetDir);

        if (!created)
            return 1;

        LogWriter.WriteSuccess($"  [Zest] Created new project at '{targetDir}'");
        Console.WriteLine();
        LogWriter.WriteAccent("  Next steps:");
        LogWriter.WriteInfo("    1. cd " + targetDir);
        LogWriter.WriteInfo("    2. zest build              # Build the site");
        LogWriter.WriteInfo("    3. zest serve              # Start dev server");
        return 0;
    }

    /// <summary>Write the conventional empty site layout: content/, _layouts/,
    /// _includes/ and assets/, each with a placeholder file.</summary>
    private static bool GenerateEmptyLayout(string target)
    {
        Directory.CreateDirectory(Path.Combine(target, "content"));
        Directory.CreateDirectory(Path.Combine(target, "_layouts"));
        Directory.CreateDirectory(Path.Combine(target, "_includes"));
        Directory.CreateDirectory(Path.Combine(target, "assets"));

        Write(Path.Combine(target, "content", "index.md"), """
            +++
            title = "Home"
            +++

            # Hello, Zest

            Edit `content/index.md` and run `zest build` again.
            """);

        Write(Path.Combine(target, "_layouts", "default.ztk"), """
            <!DOCTYPE html>
            <html lang="{{ site.language | default('en') }}">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1.0">
              <title>{% if page.title %}{{ page.title }} | {{ site.title }}{% else %}{{ site.title }}{% endif %}</title>
            </head>
            <body>
              <main>
                {{ content | safe }}
              </main>
            </body>
            </html>
            """);

        return true;
    }

    private static void Write(string path, string content) =>
        File.WriteAllText(path, content.ReplaceLineEndings(Environment.NewLine), Utf8NoBom);

    /// <summary>Write the bundled starter site to <paramref name="targetDir"/>,
    /// using the embedded resources when present and falling back to the on-disk
    /// <c>Starter</c> folder during local development.</summary>
    private static bool ExtractBundledStarter(string targetDir)
    {
        var asm = typeof(InitCommand).Assembly;
        var resourceNames = asm.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .ToArray();

        if (resourceNames.Length > 0)
        {
            ExtractEmbeddedTemplate(asm, resourceNames, targetDir);
            return true;
        }

        // Fall back to the starter on disk (convenient during local dev when
        // the preset lives outside the assembly).
        var templateDir = FindStarterDirectory();
        if (templateDir is null)
        {
            LogWriter.WriteError("  Error: Could not locate the starter site (embedded resources missing).");
            return false;
        }

        CopyDirectory(templateDir, targetDir);
        return true;
    }

    /// <summary>
    /// Locate a starter directory by walking up from the executable towards the
    /// repository root. Replaces the previous fixed <c>../../../../</c> hop,
    /// which broke as soon as the output layout changed (RID folders, TFMs).
    /// </summary>
    private static string? FindStarterDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            foreach (var name in new[] { "Starter", "Starters" })
            {
                var candidate = Path.Combine(dir.FullName, name);
                if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "_config.toml")))
                    return candidate;
            }
        }

        var local = Path.Combine(Directory.GetCurrentDirectory(), "Starter");
        return Directory.Exists(local) ? local : null;
    }

    /// <summary>Write the embedded preset resources to <paramref name="target"/>,
    /// reconstructing each file's relative path from its logical resource name.</summary>
    private static void ExtractEmbeddedTemplate(Assembly asm, string[] resourceNames, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var name in resourceNames)
        {
            var rel = name[ResourcePrefix.Length..].Replace('\\', '/');
            var tgt = Path.Combine(target, rel);
            var dir = Path.GetDirectoryName(tgt);
            if (dir is not null) Directory.CreateDirectory(dir);

            using var stream = asm.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Missing embedded resource: {name}");
            using var outFile = File.Create(tgt);
            stream.CopyTo(outFile);
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            if (IsInSkippedDirectory(rel)) continue;

            var tgt = Path.Combine(target, rel);
            var dir = Path.GetDirectoryName(tgt);
            if (dir is not null) Directory.CreateDirectory(dir);
            File.Copy(file, tgt, overwrite: true);
        }
    }

    private static bool IsInSkippedDirectory(string relativePath)
    {
        var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (SkipDirectoryNames.Contains(parts[i])) return true;
        }
        return false;
    }
}
