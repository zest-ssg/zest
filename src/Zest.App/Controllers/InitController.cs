using System.Reflection;
using Zest.Infra.Services;

namespace Zest.App.Controllers;

/// <summary>
/// Handles `zest init [path]`
/// Creates a new Zest project from the bundled starter site that is
/// (embedded) inside this assembly, so it works out of the box after a
/// `dotnet tool install` without relying on any on-disk starter folder.
/// </summary>
public static class InitController
{
    // LogicalName prefix produced by the EmbeddedResource items in the csproj.
    private const string ResourcePrefix = "Zest.App.Starters.";

    public static int Execute(string[] args)
    {
        var targetDir = args.Length > 1 ? args[1] : ".";

        if (targetDir == "." && Directory.GetFiles(targetDir).Length > 0)
        {
            LogWriter.WriteWarning("  Warning: Current directory is not empty.");
            Console.Write("  Continue anyway? (y/N): ");
            var resp = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (resp != "y" && resp != "yes")
            {
                LogWriter.WriteDim("  Aborted.");
                return 1;
            }
        }

        ExtractBundledStarter(targetDir);

        LogWriter.WriteSuccess($"  [Zest] Created new project at '{targetDir}'");
        Console.WriteLine();
        LogWriter.WriteAccent("  Next steps:");
        LogWriter.WriteInfo("    1. cd " + targetDir);
        LogWriter.WriteInfo("    2. zest build              # Build the site");
        LogWriter.WriteInfo("    3. zest serve              # Start dev server");
        return 0;
    }

    /// <summary>Write the bundled starter site to <paramref name="targetDir"/>,
    /// using the embedded resources when present and falling back to the on-disk
    /// <c>Starters</c> folder during local development. Shared by `zest init`
    /// and `zest scaffold`.</summary>
    internal static void ExtractBundledStarter(string targetDir)
    {
        var asm = typeof(InitController).Assembly;
        var resourceNames = asm.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .ToArray();

        if (resourceNames.Length == 0)
        {
            // Fall back to the starter on disk (convenient during local dev
            // when the preset lives outside the assembly).
            var templateDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "..", "Starters");
            if (!Directory.Exists(templateDir))
                templateDir = Path.Combine(Directory.GetCurrentDirectory(), "Starters");

            if (!Directory.Exists(templateDir))
            {
                LogWriter.WriteError("  Error: Could not locate the starter site (embedded resources missing).");
                return;
            }

            CopyDirectory(templateDir, targetDir);
        }
        else
        {
            ExtractEmbeddedTemplate(asm, resourceNames, targetDir);
        }
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
            var tgt = Path.Combine(target, rel);
            var dir = Path.GetDirectoryName(tgt);
            if (dir is not null) Directory.CreateDirectory(dir);
            File.Copy(file, tgt, overwrite: true);
        }
    }
}
