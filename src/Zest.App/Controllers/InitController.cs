using Zest.Infra.Services;

namespace Zest.App.Controllers;

/// <summary>
/// Handles `zest init [path]`
/// Creates a new Zest project from the default template.
/// </summary>
public static class InitController
{
    public static int Execute(string[] args)
    {
        var targetDir = args.Length > 1 ? args[1] : ".";

        if (targetDir == "." && Directory.GetFiles(targetDir).Length > 0)
        {
            Logger.WriteWarning("  Warning: Current directory is not empty.");
            Console.Write("  Continue anyway? (y/N): ");
            var resp = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (resp != "y" && resp != "yes")
            {
                Logger.WriteDim("  Aborted.");
                return 1;
            }
        }

        var templateDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "..", "templates", "default");
        if (!Directory.Exists(templateDir))
            templateDir = Path.Combine(Directory.GetCurrentDirectory(), "templates", "default");

        if (!Directory.Exists(templateDir))
        {
            Logger.WriteError("  Error: Could not locate default template directory.");
            return 1;
        }

        CopyDirectory(templateDir, targetDir);
        Logger.WriteSuccess($"  [Zest] Created new project at '{targetDir}'");
        Console.WriteLine();
        Logger.WriteAccent("  Next steps:");
        Logger.WriteInfo("    1. cd " + targetDir);
        Logger.WriteInfo("    2. zest build              # Build the site");
        Logger.WriteInfo("    3. zest serve              # Start dev server");
        return 0;
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
