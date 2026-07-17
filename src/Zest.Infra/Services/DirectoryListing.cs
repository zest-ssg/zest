using System.Globalization;
using System.Text;

#nullable enable

namespace Zest.Infra.Services;

/// <summary>
/// Generates a styled HTML directory listing page.
/// </summary>
internal static class DirectoryListing
{
    /// <summary>
    /// Render a directory listing HTML page.
    /// </summary>
    public static string Render(string dirPath, string requestPath, string outputDir)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<title>Index of {Escape(requestPath)}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("  body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;");
        sb.AppendLine("         background: #1a1a2e; color: #e0e0e0; padding: 2rem; margin: 0; }");
        sb.AppendLine("  h1 { color: #7c8aff; font-size: 1.3rem; margin-bottom: 1.5rem; }");
        sb.AppendLine("  table { border-collapse: collapse; width: 100%; max-width: 700px; }");
        sb.AppendLine("  th { text-align: left; color: #888; font-weight: normal; font-size: 0.8rem;");
        sb.AppendLine("       padding: 0.5rem 0.75rem; border-bottom: 1px solid #333; }");
        sb.AppendLine("  td { padding: 0.4rem 0.75rem; border-bottom: 1px solid #222; font-size: 0.9rem; }");
        sb.AppendLine("  a { color: #7c8aff; text-decoration: none; }");
        sb.AppendLine("  a:hover { text-decoration: underline; }");
        sb.AppendLine("  .dir { color: #a78bfa; }");
        sb.AppendLine("  .size { color: #888; text-align: right; }");
        sb.AppendLine("  .date { color: #666; font-size: 0.8rem; }");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<h1>Index of {Escape(requestPath)}</h1>");
        sb.AppendLine("<table>");
        sb.AppendLine("<tr><th>Name</th><th>Size</th><th>Modified</th></tr>");

        // Parent directory link
        if (requestPath != "/")
        {
            var parent = requestPath.TrimEnd('/');
            var lastSep = parent.LastIndexOf('/');
            parent = lastSep <= 0 ? "/" : parent[..(lastSep + 1)];
            sb.AppendLine(CultureInfo.InvariantCulture, $"<tr><td><a href=\"{Escape(parent)}\" class=\"dir\">../</a></td><td></td><td></td></tr>");
        }

        try
        {
            var dirs = Directory.GetDirectories(dirPath)
                .Select(d => new { Name = Path.GetFileName(d), IsDir = true, Info = (FileSystemInfo)new DirectoryInfo(d) })
                .OrderBy(d => d.Name);

            var files = Directory.GetFiles(dirPath)
                .Select(f => new { Name = Path.GetFileName(f), IsDir = false, Info = (FileSystemInfo)new FileInfo(f) })
                .OrderBy(f => f.Name);

            foreach (var entry in dirs.Concat(files))
            {
                var name = entry.Name;
                var isDir = entry.IsDir;
                var href = requestPath.TrimEnd('/') + "/" + Uri.EscapeDataString(name);
                if (isDir) href += "/";

                var sizeStr = isDir ? "-" : HttpHelper.FormatBytes(((FileInfo)entry.Info).Length);
                var dateStr = entry.Info.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

                var cssClass = isDir ? "dir" : "";
                sb.AppendLine(CultureInfo.InvariantCulture, $"<tr><td><a href=\"{href}\" class=\"{cssClass}\">{Escape(name)}</a></td>");
                sb.AppendLine(CultureInfo.InvariantCulture, $"<td class=\"size\">{sizeStr}</td>");
                sb.AppendLine(CultureInfo.InvariantCulture, $"<td class=\"date\">{dateStr}</td></tr>");
            }
        }
        catch { }

        sb.AppendLine("</table>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    private static string Escape(string s) => System.Net.WebUtility.HtmlEncode(s);
}
