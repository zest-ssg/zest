using Zest.Compiler.Model;
using Zest.Compiler.Build;

#nullable enable

namespace Zest.App.Runtime;

/// <summary>
/// Centralized directory-exclusion logic for file watchers and dev servers.
/// Replaces the duplicated <c>IgnoredDirNames</c> sets previously scattered
/// across <see cref="DevServer"/>, <see cref="BuildWatcher"/>, and <see cref="PreviewServer"/>.
/// </summary>
/// <remarks>
/// The output directory name (default <c>_site</c>) is derived from the active
/// <see cref="SiteConfig"/> so changes to <c>OutputDir</c> in <c>_config.toml</c>
/// are respected by watchers and servers without code changes. A nested output
/// path contributes every one of its segments, so <c>dist/_site</c> excludes
/// both names. <see cref="ContentWatcher"/> additionally excludes the output
/// subtree by full path, which is what makes sibling names such as
/// <c>_site-archive</c> safe.
/// </remarks>
public static class ExcludedPaths
{
    /// <summary>
    /// System and VCS directories that should always be excluded from
    /// file watching and serving. These are not project-specific.
    /// </summary>
    private static readonly HashSet<string> SystemDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg",
        "bin", "obj", "node_modules", "packages", ".vs"
    };

    /// <summary>
    /// Build the set of directory names to exclude, including the site's
    /// output directory (so changes inside <c>_site/</c> don't trigger
    /// rebuilds or get served as source).
    /// </summary>
    /// <param name="config">
    /// Site configuration providing <c>OutputDir</c>. Pass the loaded
    /// <see cref="SiteConfig"/> from <c>_config.toml</c>.
    /// </param>
    /// <returns>
    /// A case-insensitive <see cref="HashSet{T}"/> of directory names to exclude.
    /// Always includes <see cref="SystemDirs"/> plus the output path segments.
    /// </returns>
    public static HashSet<string> For(SiteConfig config)
    {
        var set = new HashSet<string>(SystemDirs, StringComparer.OrdinalIgnoreCase);

        // Normalize "./dist/_site" → ["dist", "_site"] so a nested output path
        // contributes every level, not just the leaf.
        var normalized = config.OutputDir.Replace('\\', '/');
        foreach (var segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "." or "..") continue;
            set.Add(segment);
        }

        return set;
    }
}
