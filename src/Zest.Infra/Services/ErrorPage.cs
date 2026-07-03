using System.Globalization;
using System.Net;
using System.Text;

#nullable enable

namespace Zest.Infra.Services;

/// <summary>
/// 404 response generation with custom 404.html fallback and page suggestions.
/// </summary>
internal static class ErrorPage
{
    /// <summary>
    /// Try to write a 404 response, using custom 404.html if available.
    /// Includes suggestion for similar paths.
    /// </summary>
    public static async Task WriteNotFound(HttpListenerContext ctx, string outputDir, string? requestedPath = null)
    {
        ctx.Response.StatusCode = 404;
        HttpHelper.AddCorsHeaders(ctx.Response);

        var custom404 = Path.Combine(outputDir, "404.html");
        if (File.Exists(custom404))
        {
            await HttpHelper.WriteFileResponseAsync(ctx, custom404);
            return;
        }

        var suggestions = FindSimilarPaths(outputDir, requestedPath);
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"UTF-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine("<title>404 — Page Not Found · Zest</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("*{margin:0;padding:0;box-sizing:border-box}");
        sb.AppendLine("body{font-family:system-ui,-apple-system,sans-serif;max-width:600px;margin:80px auto;padding:0 24px;color:#1a1a2e;line-height:1.6}");
        sb.AppendLine("h1{font-size:2.5em;font-weight:700;color:#e74c3c;margin-bottom:8px}");
        sb.AppendLine("code{background:#f0f0f5;padding:2px 8px;border-radius:4px;font-size:.9em}");
        sb.AppendLine(".tag{display:inline-block;margin-top:24px;padding:4px 12px;background:#1a1a2e;color:#fff;border-radius:20px;font-size:.75em;letter-spacing:.5px}");
        sb.AppendLine(".suggestions{margin-top:24px}");
        sb.AppendLine(".suggestions h2{font-size:1.1em;font-weight:600;margin-bottom:8px;color:#555}");
        sb.AppendLine(".suggestions ul{list-style:none;padding:0}");
        sb.AppendLine(".suggestions li{margin:6px 0}");
        sb.AppendLine("a{color:#4361ee;text-decoration:none} a:hover{text-decoration:underline;color:#3a0ca3}");
        sb.AppendLine(".back{display:inline-block;margin-top:24px;padding:8px 20px;background:#4361ee;color:#fff;border-radius:6px;font-size:.9em;font-weight:500}");
        sb.AppendLine(".back:hover{background:#3a0ca3;text-decoration:none}");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<h1>404</h1>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<p>The requested path <code>{WebUtility.HtmlEncode(requestedPath ?? "/")}</code> was not found.</p>");

        if (suggestions.Count > 0)
        {
            sb.AppendLine("<div class=\"suggestions\">");
            sb.AppendLine("<h2>Did you mean:</h2><ul>");
            foreach (var s in suggestions)
                sb.AppendLine(CultureInfo.InvariantCulture, $"<li><a href=\"{s.Url}\">{WebUtility.HtmlEncode(s.Title)}</a></li>");
            sb.AppendLine("</ul></div>");
        }

        sb.AppendLine("<div class=\"tag\">ZEST · Zenith Efficient Static Toolkit</div>");
        sb.AppendLine($"<a href=\"/\" class=\"back\">← Back to home</a>");
        sb.AppendLine("</body></html>");

        await HttpHelper.WriteStringResponse(ctx, 404, sb.ToString());
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
                var url = "/" + relPath.TrimStart('/') + (relPath.EndsWith('/') ? "" : "/");
                if (url == "//") url = "/";
                result.Add((url, title));
            }
            if (result.Count >= 5) break;
        }

        return result;
    }
}
