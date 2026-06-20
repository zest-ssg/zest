using System.Net;
using System.Text;

namespace Zest.Infra.Services;

/// <summary>
/// Shared HTTP utilities for dev server and preview server.
/// Provides MIME types, secure path resolution, and response helpers.
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
        [".txt"]  = "text/plain; charset=utf-8",
        [".md"]   = "text/markdown; charset=utf-8",
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
    /// Write a string response with the given status code and content type.
    /// </summary>
    public static async Task WriteStringResponse(HttpListenerContext ctx, int statusCode, string content, string contentType = "text/html; charset=utf-8")
    {
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = contentType;
        var bytes = Encoding.UTF8.GetBytes(content);
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        await ctx.Response.OutputStream.FlushAsync();
    }

    /// <summary>
    /// Write a file response with the correct MIME type.
    /// </summary>
    public static async Task WriteFileResponse(HttpListenerContext ctx, string filePath)
    {
        ctx.Response.ContentType = GetMimeType(filePath);
        var bytes = await File.ReadAllBytesAsync(filePath);
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        await ctx.Response.OutputStream.FlushAsync();
    }

    /// <summary>
    /// Try to write a 404 response, using custom 404.html if available.
    /// </summary>
    public static async Task WriteNotFoundResponse(HttpListenerContext ctx, string outputDir)
    {
        ctx.Response.StatusCode = 404;
        var custom404 = Path.Combine(outputDir, "404.html");
        if (File.Exists(custom404))
        {
            await WriteFileResponse(ctx, custom404);
        }
        else
        {
            await WriteStringResponse(ctx, 404,
                "<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"UTF-8\">" +
                "<title>404 — Page Not Found</title></head><body>" +
                "<h1>404 — Page Not Found</h1><p>The requested resource was not found.</p>" +
                "</body></html>");
        }
    }
}
