using System.Net;
using System.Security.Cryptography;
using System.Text;

#nullable enable

namespace Zest.Infra.Services;

/// <summary>
/// HTTP response helpers: CORS, ETag, string/file response writers.
/// </summary>
internal static class HttpResponseHelper
{
    /// <summary>
    /// Add CORS headers for local development.
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
    /// Write a file response with MIME type, ETag, and caching headers.
    /// Uses streaming (CopyToAsync) to avoid buffering entire file in memory.
    /// Returns true if a 304 Not Modified was sent (client cache hit).
    /// </summary>
    public static async Task<bool> WriteFileResponseAsync(HttpListenerContext ctx, string filePath)
    {
        AddCorsHeaders(ctx.Response);
        ctx.Response.ContentType = MimeTypeMap.GetMimeType(filePath);

        var etag = ComputeETag(filePath);
        ctx.Response.Headers["ETag"] = etag;
        ctx.Response.Headers["Last-Modified"] = new FileInfo(filePath).LastWriteTimeUtc.ToString("R");

        if (IsETagMatch(ctx.Request, etag))
        {
            ctx.Response.StatusCode = 304;
            ctx.Response.ContentLength64 = 0;
            return true;
        }

        // Stream the file directly — no intermediate buffer for large files
        var fileLength = new FileInfo(filePath).Length;
        ctx.Response.ContentLength64 = fileLength;
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
        await fs.CopyToAsync(ctx.Response.OutputStream);
        await ctx.Response.OutputStream.FlushAsync();
        return false;
    }
}
