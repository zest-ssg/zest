#nullable enable

namespace Zest.Infra.Services;

/// <summary>
/// Secure file path resolution with directory traversal protection.
/// Handles directory-style URLs, query string stripping, and index.html fallback.
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

        // Strip query string
        var qIdx = urlPath.IndexOf('?');
        if (qIdx >= 0) urlPath = urlPath[..qIdx];

        // Strip leading slash, normalize path separators
        var relative = urlPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);

        // Ends with separator → append index.html
        if (relative.EndsWith(Path.DirectorySeparatorChar))
            relative = relative + "index.html";

        // Full path within output dir
        var fullPath = Path.GetFullPath(Path.Combine(outputDir, relative));

        // If file doesn't exist and has no extension, try as directory + index.html
        if (!File.Exists(fullPath) && string.IsNullOrEmpty(Path.GetExtension(relative)))
            fullPath = Path.GetFullPath(Path.Combine(outputDir, relative, "index.html"));

        // Security: ensure resolved path is within outputDir
        if (!fullPath.StartsWith(Path.GetFullPath(outputDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fullPath, Path.GetFullPath(outputDir), StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException($"Path traversal detected: {urlPath} resolved to {fullPath}");
        }

        return fullPath;
    }
}
