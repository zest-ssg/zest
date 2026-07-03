#nullable enable

namespace Zest.Infra.Configuration;

/// <summary>
/// Finds the project root directory by looking for _config.toml or content/ directory.
/// Walks up from the starting directory until a match is found.
/// </summary>
internal static class RootFinder
{
    /// <summary>
    /// Find the project root directory. Walks up from hint (or CWD) looking for
    /// _config.toml or content/ directory.
    /// </summary>
    public static string? Find(string? hint)
    {
        var start = hint != null
            ? new DirectoryInfo(hint)
            : new DirectoryInfo(Directory.GetCurrentDirectory());
        var dir = start;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "_config.toml")) ||
                Directory.Exists(Path.Combine(dir.FullName, "content")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return start.FullName;
    }
}
