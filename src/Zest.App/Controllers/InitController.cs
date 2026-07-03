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
            LogWriter.WriteWarning("  Warning: Current directory is not empty.");
            Console.Write("  Continue anyway? (y/N): ");
            var resp = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (resp != "y" && resp != "yes")
            {
                LogWriter.WriteDim("  Aborted.");
                return 1;
            }
        }

        var templateDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "..", "templates", "default");
        if (!Directory.Exists(templateDir))
            templateDir = Path.Combine(Directory.GetCurrentDirectory(), "templates", "default");

        if (!Directory.Exists(templateDir))
        {
            LogWriter.WriteError("  Error: Could not locate default template directory.");
            return 1;
        }

        CopyDirectory(templateDir, targetDir);
        LogWriter.WriteSuccess($"  [Zest] Created new project at '{targetDir}'");
        Console.WriteLine();
        LogWriter.WriteAccent("  Next steps:");
        LogWriter.WriteInfo("    1. cd " + targetDir);
        LogWriter.WriteInfo("    2. zest build              # Build the site");
        LogWriter.WriteInfo("    3. zest serve              # Start dev server");
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
