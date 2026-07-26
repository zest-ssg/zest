using System.Globalization;
using System.Net;
using System.Text;

#nullable enable

namespace Zest.Infra.Services;

/// <summary>
/// Error page generation with custom 404.html fallback and path suggestions.
/// Styled after Netlify's clean, minimal 404 page with dark mode support.
/// </summary>
internal static class ErrorPage
{
    private const string DarkBg    = "rgb(6 11 16)";
    private const string DarkCard  = "rgb(15 22 30)";
    private const string DarkBorder = "rgb(36 47 54)";
    private const string DarkText  = "rgb(233 235 237)";
    private const string DarkMuted = "rgb(139 148 158)";
    private const string DarkLink  = "rgb(88 166 255)";

    /// <summary>
    /// Write a styled 404 response. Uses custom 404.html if present,
    /// otherwise renders a clean card-based page with optional suggestions.
    /// </summary>
    public static async Task WriteNotFound(HttpListenerContext ctx, string outputDir, string? requestedPath = null)
    {
        ctx.Response.StatusCode = 404;
        HttpHelper.AddCorsHeaders(ctx.Response);

        // User-provided custom 404 page takes priority.
        var custom404 = Path.Combine(outputDir, "404.html");
        if (File.Exists(custom404))
        {
            await HttpHelper.WriteFileResponseAsync(ctx, custom404);
            return;
        }

        var suggestions = FindSimilarPaths(outputDir, requestedPath);
        var displayPath = WebUtility.HtmlEncode(requestedPath ?? "/");

        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html><html lang=en>");
        sb.AppendLine("<meta charset=utf-8>");
        sb.AppendLine("<meta name=viewport content=\"width=device-width,initial-scale=1\">");
        sb.AppendLine("<title>Page not found · Zest</title>");
        sb.AppendLine("<style>");
        sb.AppendLine(":root{--z-bg:#fff;--z-card:#fff;--z-border:#d0d7de;--z-text:#1f2328;--z-muted:#656d76;--z-link:#0969da;--z-shadow:0 1px 0 rgba(31 35 40/.04),0 0 0 1px rgba(31 35 40/.06),0 6px 16px rgba(31 35 40/.08)}");
        sb.AppendLine("@media(prefers-color-scheme:dark){:root{--z-bg:" + DarkBg + ";--z-card:" + DarkCard + ";--z-border:" + DarkBorder + ";--z-text:" + DarkText + ";--z-muted:" + DarkMuted + ";--z-link:" + DarkLink + ";--z-shadow:0 1px 0 rgba(0 0 0/.2),0 0 0 1px rgba(255 255 255/.04),0 6px 16px rgba(0 0 0/.24)}}");
        sb.AppendLine("*{margin:0;padding:0;box-sizing:border-box}");
        sb.AppendLine("body{font-family:system-ui,-apple-system,BlinkMacSystemFont,Segoe UI,Roboto,Helvetica,Arial,sans-serif;background:var(--z-bg);color:var(--z-text);line-height:1.5;-webkit-font-smoothing:antialiased}");
        sb.AppendLine(".main{display:flex;flex-direction:column;align-items:center;justify-content:center;min-height:100vh;padding:24px}");
        sb.AppendLine(".card{width:100%;max-width:420px;background:var(--z-card);border:1px solid var(--z-border);border-radius:8px;box-shadow:var(--z-shadow);padding:24px}");
        sb.AppendLine("h1{margin:0;font-size:1.25rem;font-weight:600;line-height:1.3}");
        sb.AppendLine("h1+p{margin-top:8px;font-size:.875rem;color:var(--z-muted)}");
        sb.AppendLine("code{font-family:ui-monospace,SFMono-Regular,Menlo,Monaco,Consolas,Liberation Mono,Courier New,monospace;font-size:.8125rem;background:var(--z-bg);padding:2px 6px;border-radius:4px;border:1px solid var(--z-border)}");
        sb.AppendLine("hr{border:0;height:1px;background:var(--z-border);margin:16px 0}");
        sb.AppendLine("a{color:var(--z-link);font-weight:500;text-decoration:none;text-underline-offset:2px}");
        sb.AppendLine("a:hover{text-decoration:underline}");
        sb.AppendLine(".footnote{font-size:.8125rem;color:var(--z-muted)}");
        sb.AppendLine(".suggestions{margin-top:12px}");
        sb.AppendLine(".suggestions h2{font-size:.8125rem;font-weight:600;color:var(--z-muted);margin-bottom:4px}");
        sb.AppendLine(".suggestions ul{list-style:none}");
        sb.AppendLine(".suggestions li{font-size:.8125rem;line-height:1.8}");
        sb.AppendLine(".back{display:inline-block;margin-top:16px;font-weight:500}");
        sb.AppendLine("</style>");
        sb.AppendLine("<div class=main><div class=card>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<h1>Page not found</h1>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<p>The path <code>{displayPath}</code> doesn't exist on this preview server.</p>");

        if (suggestions.Count > 0)
        {
            sb.AppendLine("<div class=suggestions>");
            sb.AppendLine("<h2>Did you mean:</h2><ul>");
            foreach (var s in suggestions)
                sb.AppendLine(CultureInfo.InvariantCulture, $"<li><a href=\"{s.Url}\">{WebUtility.HtmlEncode(s.Title)}</a></li>");
            sb.AppendLine("</ul></div>");
        }

        sb.AppendLine("<hr>");
        sb.AppendLine("<p class=footnote>If this is your site, check the URL or verify the file exists in your output directory.</p>");
        sb.AppendLine("<a href=/ class=back>&larr; Back to home</a>");
        sb.AppendLine("<p class=footnote style=margin-top:16px>Zest SSG &mdash; Preview Server</p>");
        sb.AppendLine("</div></div>");

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
