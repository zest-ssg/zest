using System.Net;
using System.Text;

#nullable enable

namespace Zest.Infra.Services;

/// <summary>
/// 404 response generation with custom 404.html fallback and page suggestions.
/// </summary>
internal static class NotFoundResponse
{
    /// <summary>
    /// Try to write a 404 response, using custom 404.html if available.
    /// Includes suggestion for similar paths.
    /// </summary>
    public static async Task WriteNotFound(HttpListenerContext ctx, string outputDir, string? requestedPath = null)
    {
        ctx.Response.StatusCode = 404;
        HttpResponseHelper.AddCorsHeaders(ctx.Response);

        var custom404 = Path.Combine(outputDir, "404.html");
        if (File.Exists(custom404))
        {
            await HttpResponseHelper.WriteFileResponseAsync(ctx, custom404);
            return;
        }

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
        sb.AppendLine($"<p>The requested path <code>{WebUtility.HtmlEncode(requestedPath ?? "/")}</code> was not found.</p>");

        if (suggestions.Count > 0)
        {
            sb.AppendLine("<div class=\"suggestions\">");
            sb.AppendLine("<p>Did you mean:</p><ul>");
            foreach (var s in suggestions)
                sb.AppendLine($"<li><a href=\"{s.Url}\">{WebUtility.HtmlEncode(s.Title)}</a></li>");
            sb.AppendLine("</ul></div>");
        }

        sb.AppendLine($"<p><a href=\"/\">← Back to home</a></p>");
        sb.AppendLine("</body></html>");

        await HttpResponseHelper.WriteStringResponse(ctx, 404, sb.ToString());
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
