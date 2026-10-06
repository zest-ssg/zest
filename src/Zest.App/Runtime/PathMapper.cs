#nullable enable

namespace Zest.App.Runtime;

/// <summary>
/// Secure file path resolution with directory traversal protection.
/// Handles directory-style URLs, query string stripping, URL decoding,
/// and index.html fallback.
/// </summary>
internal static class PathMapper
{
    /// <summary>
    /// Resolve a URL path to a secure physical file path within the output directory.
    /// Throws if the resolved path escapes the output directory (directory traversal protection).
    /// </summary>
    public static string ResolveFilePath(string outputDir, string urlPath)
    {
        // Normalize: default to index.html for root
        if (string.IsNullOrEmpty(urlPath) || urlPath == "/")
            urlPath = "/index.html";

        var relative = ToRelativePath(urlPath);

        // Ends with separator → append index.html
        if (relative.EndsWith(Path.DirectorySeparatorChar))
            relative += "index.html";

        // Full path within output dir
        var fullPath = Path.GetFullPath(Path.Combine(outputDir, relative));

        // If file doesn't exist and has no extension, try as directory + index.html
        if (!File.Exists(fullPath) && string.IsNullOrEmpty(Path.GetExtension(relative)))
            fullPath = Path.GetFullPath(Path.Combine(outputDir, relative, "index.html"));

        EnsureWithin(outputDir, fullPath, urlPath);
        return fullPath;
    }

    /// <summary>
    /// Resolve a URL path to a physical directory path within the output directory.
    /// Returns null if the URL doesn't correspond to a directory path or escapes
    /// the output directory.
    /// </summary>
    public static string? ResolveDirPath(string outputDir, string urlPath)
    {
        if (string.IsNullOrEmpty(urlPath) || urlPath == "/")
            return Path.GetFullPath(outputDir);

        var relative = ToRelativePath(urlPath);
        var fullPath = Path.GetFullPath(Path.Combine(outputDir, relative));

        var normalizedOutput = Path.GetFullPath(outputDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var prefix = normalizedOutput + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(prefix, PathComparison) &&
            !string.Equals(fullPath, normalizedOutput, PathComparison))
            return null;

        return fullPath;
    }

    /// <summary>
    /// Strip the query string, decode percent-escapes, and convert the URL
    /// path into a platform-relative path.
    /// </summary>
    /// <remarks>
    /// Decoding uses <see cref="Uri.UnescapeDataString"/>, not
    /// <c>HttpUtility.UrlDecode</c>: inside a path a '+' is a literal plus,
    /// not a space, so decoding it would break files whose names contain one.
    /// </remarks>
    private static string ToRelativePath(string urlPath)
    {
        var qIdx = urlPath.IndexOf('?');
        if (qIdx >= 0) urlPath = urlPath[..qIdx];

        try
        {
            urlPath = Uri.UnescapeDataString(urlPath);
        }
        catch (UriFormatException)
        {
            // Malformed escapes (e.g. a stray '%'): keep the raw text so the
            // traversal check below still sees the original characters.
        }

        if (urlPath.Contains('\0'))
            throw new UnauthorizedAccessException("Null byte in path.");

        return urlPath.TrimStart('/')
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
    }

    /// <summary>Throw when <paramref name="fullPath"/> leaves <paramref name="outputDir"/>.</summary>
    private static void EnsureWithin(string outputDir, string fullPath, string urlPath)
    {
        var normalizedOutput = Path.GetFullPath(outputDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var prefix = normalizedOutput + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(prefix, PathComparison) &&
            !string.Equals(fullPath, normalizedOutput, PathComparison))
        {
            throw new UnauthorizedAccessException(
                $"Path traversal detected: {urlPath} resolved to {fullPath}");
        }
    }

    /// <summary>
    /// Windows and macOS compare paths case-insensitively; Linux does not.
    /// </summary>
    private static StringComparison PathComparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
}
