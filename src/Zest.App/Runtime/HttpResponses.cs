using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

#nullable enable

namespace Zest.App.Runtime;

/// <summary>
/// HTTP response helpers: CORS, ETag, string/file response writers.
/// Single home for ETag computation and conditional-request matching so the
/// development and preview servers cannot drift apart.
/// </summary>
internal static class HttpResponses
{
    /// <summary>
    /// Add CORS and security headers for local development.
    /// </summary>
    /// <param name="response">Response to decorate.</param>
    /// <param name="wildcardOrigin">
    /// When false, <c>Access-Control-Allow-Origin</c> is restricted to the
    /// requesting origin instead of <c>*</c>. The dev server disables the
    /// wildcard once it is reachable from outside the loopback interface.
    /// </param>
    public static void AddCorsHeaders(HttpListenerResponse response, bool wildcardOrigin = true)
    {
        // A wildcard origin is only safe while the server is reachable from
        // the same machine; once bound to a routable interface the header is
        // omitted so other hosts cannot read responses cross-origin.
        if (wildcardOrigin)
            response.Headers["Access-Control-Allow-Origin"] = "*";
        response.Headers["Access-Control-Allow-Methods"] = "GET, HEAD, OPTIONS";
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type, If-None-Match";
        response.Headers["X-Content-Type-Options"] = "nosniff";
    }

    /// <summary>
    /// Compute an ETag from path, length and last-write time.
    /// </summary>
    public static string ComputeETag(string filePath, long length, DateTime lastWriteTimeUtc)
    {
        var raw = $"{filePath}:{length}:{lastWriteTimeUtc.Ticks}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return "\"" + Convert.ToHexString(hash) + "\"";
    }

    /// <summary>
    /// Compute an ETag for a file on disk.
    /// </summary>
    public static string ComputeETag(string filePath)
    {
        var info = new FileInfo(filePath);
        return ComputeETag(filePath, info.Length, info.LastWriteTimeUtc);
    }

    /// <summary>
    /// Check whether the client's <c>If-None-Match</c> header matches the
    /// resource ETag. Handles <c>*</c>, a comma-separated validator list, and
    /// the weak-validator prefix (<c>W/</c>).
    /// </summary>
    public static bool IsETagMatch(HttpListenerRequest request, string etag)
    {
        var ifNoneMatch = request.Headers["If-None-Match"];
        if (string.IsNullOrWhiteSpace(ifNoneMatch)) return false;

        var expected = StripWeak(etag.Trim());
        foreach (var candidateRaw in ifNoneMatch.Split(','))
        {
            var candidate = candidateRaw.Trim();
            if (candidate.Length == 0) continue;
            if (candidate == "*") return true;
            if (string.Equals(StripWeak(candidate), expected, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static string StripWeak(string validator) =>
        validator.StartsWith("W/", StringComparison.Ordinal) ? validator[2..] : validator;

    /// <summary>
    /// Write a string response with the given status code and content type.
    /// For HEAD requests the headers are sent without a body.
    /// </summary>
    public static async Task WriteStringResponse(HttpListenerContext ctx, int statusCode, string content,
        string contentType = "text/html; charset=utf-8", bool wildcardOrigin = true)
    {
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = contentType;
        AddCorsHeaders(ctx.Response, wildcardOrigin);
        var bytes = Encoding.UTF8.GetBytes(content);
        ctx.Response.ContentLength64 = bytes.Length;
        if (IsHead(ctx))
            return;

        await ctx.Response.OutputStream.WriteAsync(bytes);
        await ctx.Response.OutputStream.FlushAsync();
    }

    /// <summary>
    /// Write a file response with MIME type, ETag, and caching headers.
    /// Uses streaming (CopyToAsync) to avoid buffering entire file in memory.
    /// Returns true if a 304 Not Modified was sent (client cache hit).
    /// </summary>
    public static async Task<bool> WriteFileResponseAsync(HttpListenerContext ctx, string filePath,
        bool wildcardOrigin = true)
    {
        AddCorsHeaders(ctx.Response, wildcardOrigin);
        ctx.Response.ContentType = MimeMapper.GetMimeType(filePath);

        var info = new FileInfo(filePath);
        var etag = ComputeETag(filePath, info.Length, info.LastWriteTimeUtc);
        ctx.Response.Headers["ETag"] = etag;
        ctx.Response.Headers["Last-Modified"] = info.LastWriteTimeUtc.ToString("R", CultureInfo.InvariantCulture);

        if (IsETagMatch(ctx.Request, etag))
        {
            ctx.Response.StatusCode = 304;
            ctx.Response.ContentLength64 = 0;
            return true;
        }

        ctx.Response.ContentLength64 = info.Length;
        if (IsHead(ctx))
            return false;

        // FileShare.Delete lets the build's atomic rename swap the file while
        // this handle is open, matching the dev server's serving path.
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read,
            FileShare.Read | FileShare.Delete, 65536, useAsync: true);
        await fs.CopyToAsync(ctx.Response.OutputStream);
        await ctx.Response.OutputStream.FlushAsync();
        return false;
    }

    /// <summary>True when the request asks for headers only.</summary>
    public static bool IsHead(HttpListenerContext ctx) =>
        string.Equals(ctx.Request.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Format a byte count into a human-readable string (B, KB, MB).
    /// </summary>
    public static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes}B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1}KB",
            _ => $"{bytes / (1024.0 * 1024.0):F1}MB"
        };
    }
}
