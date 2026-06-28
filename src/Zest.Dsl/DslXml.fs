namespace Zest.Dsl

open System
open System.Text

// ============================================================
// DslXml — RSS 2.0, Atom 1.0, and Sitemap XML generation
// ============================================================

module DslXml =

    /// XML-encode a string for safe insertion into XML content.
    let private xe (s: string) =
        if String.IsNullOrEmpty s then ""
        else
            s.Replace("&", "&amp;")
             .Replace("<", "&lt;")
             .Replace(">", "&gt;")
             .Replace("\"", "&quot;")
             .Replace("'", "&apos;")

    /// Generate an RSS 2.0 feed XML string from a list of pages.
    /// Each page is an anonymous record with: url, title, date, description, tags.
    let rss_xml (siteTitle: string) (siteUrl: string) (siteDescription: string) (pages: {| url: string; title: string; date: string; description: string |}[]) =
        let sb = StringBuilder()
        sb.AppendLine("""<?xml version="1.0" encoding="utf-8"?>""") |> ignore
        sb.AppendLine("""<rss version="2.0" xmlns:atom="http://www.w3.org/2005/Atom">""") |> ignore
        sb.AppendLine("  <channel>") |> ignore
        sb.AppendFormat("    <title>{0}</title>\n", xe siteTitle) |> ignore
        sb.AppendFormat("    <link>{0}</link>\n", xe siteUrl) |> ignore
        sb.AppendFormat("    <description>{0}</description>\n", xe siteDescription) |> ignore
        sb.AppendFormat("    <atom:link href=\"{0}/rss.xml\" rel=\"self\" type=\"application/rss+xml\" />\n", xe siteUrl) |> ignore
        sb.AppendLine("    <language>en</language>") |> ignore
        sb.AppendFormat("    <lastBuildDate>{0}</lastBuildDate>\n", DateTime.UtcNow.ToString("ddd, dd MMM yyyy HH:mm:ss GMT")) |> ignore
        for page in pages do
            let pubDate =
                match DateTime.TryParse(page.date) with
                | true, d -> d.ToString("ddd, dd MMM yyyy HH:mm:ss GMT")
                | _ -> DateTime.UtcNow.ToString("ddd, dd MMM yyyy HH:mm:ss GMT")
            let fullUrl =
                if page.url.StartsWith("/") then siteUrl.TrimEnd('/') + page.url
                else page.url
            sb.AppendLine("    <item>") |> ignore
            sb.AppendFormat("      <title>{0}</title>\n", xe page.title) |> ignore
            sb.AppendFormat("      <link>{0}</link>\n", xe fullUrl) |> ignore
            sb.AppendFormat("      <guid>{0}</guid>\n", xe fullUrl) |> ignore
            sb.AppendFormat("      <pubDate>{0}</pubDate>\n", pubDate) |> ignore
            sb.AppendFormat("      <description>{0}</description>\n", xe page.description) |> ignore
            sb.AppendLine("    </item>") |> ignore
        done
        sb.AppendLine("  </channel>") |> ignore
        sb.AppendLine("</rss>") |> ignore
        sb.ToString()

    /// Generate an Atom 1.0 feed XML string.
    let atom_xml (siteTitle: string) (siteUrl: string) (siteDescription: string) (authorName: string) (pages: {| url: string; title: string; date: string; description: string |}[]) =
        let sb = StringBuilder()
        sb.AppendLine("""<?xml version="1.0" encoding="utf-8"?>""") |> ignore
        sb.AppendLine("""<feed xmlns="http://www.w3.org/2005/Atom">""") |> ignore
        sb.AppendFormat("  <title>{0}</title>\n", xe siteTitle) |> ignore
        sb.AppendFormat("  <subtitle>{0}</subtitle>\n", xe siteDescription) |> ignore
        sb.AppendFormat("  <link href=\"{0}/atom.xml\" rel=\"self\" />\n", xe siteUrl) |> ignore
        sb.AppendFormat("  <link href=\"{0}\" rel=\"alternate\" />\n", xe siteUrl) |> ignore
        sb.AppendFormat("  <updated>{0}</updated>\n", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")) |> ignore
        sb.AppendFormat("  <id>{0}/atom.xml</id>\n", xe siteUrl) |> ignore
        if not (String.IsNullOrEmpty authorName) then
            sb.AppendFormat("  <author><name>{0}</name></author>\n", xe authorName) |> ignore
        for page in pages do
            let updated =
                match DateTime.TryParse(page.date) with
                | true, d -> d.ToString("yyyy-MM-ddTHH:mm:ssZ")
                | _ -> DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
            let fullUrl =
                if page.url.StartsWith("/") then siteUrl.TrimEnd('/') + page.url
                else page.url
            sb.AppendLine("  <entry>") |> ignore
            sb.AppendFormat("    <title>{0}</title>\n", xe page.title) |> ignore
            sb.AppendFormat("    <link href=\"{0}\" />\n", xe fullUrl) |> ignore
            sb.AppendFormat("    <id>{0}</id>\n", xe fullUrl) |> ignore
            sb.AppendFormat("    <updated>{0}</updated>\n", updated) |> ignore
            sb.AppendFormat("    <summary>{0}</summary>\n", xe page.description) |> ignore
            sb.AppendLine("  </entry>") |> ignore
        done
        sb.AppendLine("</feed>") |> ignore
        sb.ToString()

    /// Generate a Sitemap XML string.
    let sitemap_xml (baseUrl: string) (pages: {| url: string; date: string; priority: float |}[]) =
        let sb = StringBuilder()
        sb.AppendLine("""<?xml version="1.0" encoding="utf-8"?>""") |> ignore
        sb.AppendLine("""<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">""") |> ignore
        for page in pages do
            let fullUrl =
                if page.url.StartsWith("/") then baseUrl.TrimEnd('/') + page.url
                else page.url
            let lastMod =
                match DateTime.TryParse(page.date) with
                | true, d -> d.ToString("yyyy-MM-dd")
                | _ -> DateTime.UtcNow.ToString("yyyy-MM-dd")
            sb.AppendLine("  <url>") |> ignore
            sb.AppendFormat("    <loc>{0}</loc>\n", xe fullUrl) |> ignore
            sb.AppendFormat("    <lastmod>{0}</lastmod>\n", lastMod) |> ignore
            sb.AppendFormat("    <priority>{0:F1}</priority>\n", page.priority) |> ignore
            sb.AppendLine("  </url>") |> ignore
        done
        sb.AppendLine("</urlset>") |> ignore
        sb.ToString()
