using System.Net;
using System.Security.Cryptography;
using System.Text;

#nullable enable

namespace Zest.Infra.Services;

/// <summary>
/// Shared HTTP utilities for dev server and preview server.
/// Provides MIME types, secure path resolution, ETag caching, CORS, and response helpers.
/// </summary>
internal static class HttpHelper
{
    /// <summary>
    /// MIME type mapping by file extension.
    /// </summary>
    private static readonly Dictionary<string, string> MimeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".htm"]  = "text/html; charset=utf-8",
        [".css"]  = "text/css; charset=utf-8",
        [".js"]   = "application/javascript; charset=utf-8",
        [".mjs"]  = "application/javascript; charset=utf-8",
        [".json"] = "application/json",
        [".xml"]  = "application/xml",
        [".svg"]  = "image/svg+xml",
        [".png"]  = "image/png",
        [".jpg"]  = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"]  = "image/gif",
        [".webp"] = "image/webp",
        [".ico"]  = "image/x-icon",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".ttf"]  = "font/ttf",
        [".otf"]  = "font/otf",
        [".pdf"]  = "application/pdf",
        [".map"]  = "application/json",
        [".mp4"]  = "video/mp4",
        [".webm"] = "video/webm",
        [".mp3"]  = "audio/mpeg",
        [".ogg"]  = "audio/ogg",
        [".wav"]  = "audio/wav",
        [".txt"]  = "text/plain; charset=utf-8",
        [".md"]   = "text/markdown; charset=utf-8",
        [".csv"]  = "text/csv; charset=utf-8",
        [".wasm"] = "application/wasm",
        [".avif"] = "image/avif",
    };

    /// <summary>
    /// Get MIME type for a file extension. Returns application/octet-stream if unknown.
    /// </summary>
    public static string GetMimeType(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return MimeMap.TryGetValue(ext, out var mime) ? mime : "application/octet-stream";
    }

    /// <summary>
    /// Resolve a URL path to a secure physical file path within the output directory.
    /// Handles directory-style URLs: "/guide/" → "/guide/index.html", "/guide" → "/guide/index.html"
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

    /// <summary>
    /// Add CORS headers for local development (allows cross-origin requests from browser dev tools).
    /// </summary>
    public static void AddCorsHeaders(HttpListenerResponse response)
    {
        response.Headers["Access-Control-Allow-Origin"] = "*";
        response.Headers["Access-Control-Allow-Methods"] = "GET, HEAD, OPTIONS";
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type, If-None-Match";
    }

    /// <summary>
    /// Compute an ETag for a file based on path + last write time.
    /// </summary>
    public static string ComputeETag(string filePath)
    {
        var info = new FileInfo(filePath);
        var raw = $"{filePath}:{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(raw));
        return "\"" + Convert.ToHexString(hash) + "\"";
    }

    /// <summary>
    /// Check if the client's If-None-Match matches the file's ETag.
    /// </summary>
    public static bool IsETagMatch(HttpListenerRequest request, string etag)
    {
        var ifNoneMatch = request.Headers["If-None-Match"];
        return !string.IsNullOrEmpty(ifNoneMatch) && ifNoneMatch == etag;
    }

    /// <summary>
    /// Write a string response with the given status code and content type.
    /// </summary>
    public static async Task WriteStringResponse(HttpListenerContext ctx, int statusCode, string content, string contentType = "text/html; charset=utf-8")
    {
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = contentType;
        AddCorsHeaders(ctx.Response);
        var bytes = Encoding.UTF8.GetBytes(content);
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        await ctx.Response.OutputStream.FlushAsync();
    }

    /// <summary>
    /// Write a file response with the correct MIME type, ETag, and caching headers.
    /// Returns true if a 304 Not Modified was sent (client cache hit).
    /// </summary>
    public static async Task<bool> WriteFileResponseAsync(HttpListenerContext ctx, string filePath)
    {
        AddCorsHeaders(ctx.Response);
        ctx.Response.ContentType = GetMimeType(filePath);

        // ETag / conditional GET
        var etag = ComputeETag(filePath);
        ctx.Response.Headers["ETag"] = etag;
        var info = new FileInfo(filePath);
        ctx.Response.Headers["Last-Modified"] = info.LastWriteTimeUtc.ToString("R");

        if (IsETagMatch(ctx.Request, etag))
        {
            ctx.Response.StatusCode = 304;
            ctx.Response.ContentLength64 = 0;
            return true; // cache hit
        }

        var bytes = await File.ReadAllBytesAsync(filePath);
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        await ctx.Response.OutputStream.FlushAsync();
        return false;
    }

    /// <summary>
    /// Write a file response (legacy sync wrapper).
    /// </summary>
    public static async Task WriteFileResponse(HttpListenerContext ctx, string filePath)
    {
        await WriteFileResponseAsync(ctx, filePath);
    }

    /// <summary>
    /// Try to write a 404 response, using custom 404.html if available.
    /// Includes suggestion for similar paths.
    /// </summary>
    public static async Task WriteNotFoundResponse(HttpListenerContext ctx, string outputDir, string? requestedPath = null)
    {
        ctx.Response.StatusCode = 404;
        AddCorsHeaders(ctx.Response);

        var custom404 = Path.Combine(outputDir, "404.html");
        if (File.Exists(custom404))
        {
            await WriteFileResponseAsync(ctx, custom404);
            return;
        }

        // Build a helpful 404 page with suggestions
        var suggestions = FindSimilarPaths(outputDir, requestedPath);
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"UTF-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine("<title>404 — Page Not Found</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body{font-family:system-ui,sans-serif;max-width:600px;margin:80px auto;padding:0 20px;color:#333}");
        sb.AppendLine("h1{font-size:2em;color:#e74c3c} .suggestions{margin-top:20px} .suggestions li{margin:5px 0}");
        sb.AppendLine("a{color:#3498db;text-decoration:none} a:hover{text-decoration:underline}");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine($"<h1>404 — Page Not Found</h1>");
        sb.AppendLine($"<p>The requested path <code>{System.Net.WebUtility.HtmlEncode(requestedPath ?? "/")}</code> was not found.</p>");

        if (suggestions.Count > 0)
        {
            sb.AppendLine("<div class=\"suggestions\">");
            sb.AppendLine("<p>Did you mean:</p><ul>");
            foreach (var s in suggestions)
                sb.AppendLine($"<li><a href=\"{s.Url}\">{System.Net.WebUtility.HtmlEncode(s.Title)}</a></li>");
            sb.AppendLine("</ul></div>");
        }

        sb.AppendLine($"<p><a href=\"/\">← Back to home</a></p>");
        sb.AppendLine("</body></html>");

        await WriteStringResponse(ctx, 404, sb.ToString());
    }

    /// <summary>
    /// Find similar paths in the output directory for 404 suggestions.
    /// </summary>
    private static List<(string Url, string Title)> FindSimilarPaths(string outputDir, string? requestedPath)
    {
        var result = new List<(string, string)>();
        if (string.IsNullOrEmpty(requestedPath) || requestedPath == "/")
            return result;

        var requested = requestedPath.Trim('/').ToLowerInvariant();
        var htmlFiles = Directory.GetFiles(outputDir, "*.html", SearchOption.AllDirectories);

        foreach (var file in htmlFiles)
        {
            var relPath = Path.GetRelativePath(outputDir, file)
                .Replace('\\', '/')
                .Replace("index.html", "")
                .TrimEnd('/');
            if (string.IsNullOrEmpty(relPath)) relPath = "/";

            // Check for partial match
            var relLower = relPath.ToLowerInvariant();
            if (relLower.Contains(requested) || requested.Contains(relLower))
            {
                var title = Path.GetFileNameWithoutExtension(file);
                if (title == "index")
                    title = Path.GetFileName(Path.GetDirectoryName(file)!) ?? relPath;
                var url = "/" + relPath.TrimStart('/') + (relPath.EndsWith("/") ? "" : "/");
                if (url == "//") url = "/";
                result.Add((url, title));
            }
            if (result.Count >= 5) break;
        }

        return result;
    }
}
