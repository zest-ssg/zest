using System.Net;

#nullable enable

namespace Zest.App.Runtime;

/// <summary>
/// MIME type mapping by file extension.
/// </summary>
internal static class MimeMapper
{
    private static readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase)
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
        // ZCSS is normally compiled on the fly, but a source file can still be
        // requested directly (e.g. when compilation fails).
        [".zcss"] = "text/css; charset=utf-8",
        [".webmanifest"] = "application/manifest+json",
    };

    /// <summary>
    /// Get MIME type for a file extension. Returns application/octet-stream if unknown.
    /// </summary>
    public static string GetMimeType(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return _map.TryGetValue(ext, out var mime) ? mime : "application/octet-stream";
    }
}
