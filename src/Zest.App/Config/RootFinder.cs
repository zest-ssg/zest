#nullable enable

namespace Zest.App.Config;

/// <summary>
/// Finds the project root directory by looking for _config.toml or content/ directory.
/// Walks up from the starting directory until a match is found.
/// </summary>
internal static class RootFinder
{
    /// <summary>
    /// Find the project root directory. Walks up from <paramref name="hint"/>
    /// (or the current directory when null) looking for _config.toml or a
    /// content/ directory.
    /// <para>
    /// Always returns a usable directory: when no marker is found anywhere up
    /// the tree the starting directory is returned, because Zest builds a site
    /// from any directory with no configuration at all.
    /// </para>
    /// </summary>
    public static string Find(string? hint)
    {
        var start = ToDirectory(hint);
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

    /// <summary>
    /// Turn a caller-supplied hint into a <see cref="DirectoryInfo"/>, falling
    /// back to the current directory when the hint is invalid or missing.
    /// </summary>
    private static DirectoryInfo ToDirectory(string? hint)
    {
        var cwd = new DirectoryInfo(Directory.GetCurrentDirectory());
        if (string.IsNullOrWhiteSpace(hint)) return cwd;

        try
        {
            var dir = new DirectoryInfo(hint);
            return dir.Exists ? dir : cwd;
        }
        catch (ArgumentException)
        {
            // Invalid path characters — fall back rather than fail the command.
            return cwd;
        }
        catch (PathTooLongException)
        {
            return cwd;
        }
    }
}
