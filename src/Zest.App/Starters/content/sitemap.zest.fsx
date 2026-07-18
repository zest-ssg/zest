// @permalink /sitemap.xml
// @layout none
// @title Sitemap
//
// Generates the site's XML sitemap for search engines. Like the RSS feed it
// is rendered with no layout so the output is raw XML at /sitemap.xml.
//
// The XML is built inline (rather than via DslXml) so the script only reads
// page fields and never crosses the F# anonymous-record assembly boundary.

open System
open System.Text
open Zest.Dsl

let xe (s: string) =
    if String.IsNullOrEmpty s then "" else
    s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
     .Replace("\"", "&quot;").Replace("'", "&apos;")

let data = Context.get().SiteData
let opt k = if data.ContainsKey(k) then data.[k] else ""

let siteUrl = let u = opt "site.base_url" in if u <> "" then u else "https://example.com"

let pages =
    site_pages ()
    // Exclude machine-generated routes that should not be indexed.
    |> Array.filter (fun p ->
        p.url <> "/rss.xml" && p.url <> "/sitemap.xml" && p.url <> "/404.html")
    |> Array.map (fun p ->
        let priority =
            if p.url = "/" then 1.0
            elif p.url.StartsWith("/posts/") then 0.8
            else 0.5
        p.url, p.date, priority)

let sb = StringBuilder()
sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>") |> ignore
sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">") |> ignore
for (url, date, priority) in pages do
    let full = if url.StartsWith("/") then siteUrl.TrimEnd('/') + url else url
    let lastMod =
        match DateTime.TryParse(date) with
        | true, d -> d.ToString("yyyy-MM-dd")
        | _ -> DateTime.UtcNow.ToString("yyyy-MM-dd")
    sb.AppendLine("  <url>") |> ignore
    sb.AppendFormat("    <loc>{0}</loc>\n", xe full) |> ignore
    sb.AppendFormat("    <lastmod>{0}</lastmod>\n", lastMod) |> ignore
    sb.AppendFormat("    <priority>{0:F1}</priority>\n", priority) |> ignore
    sb.AppendLine("  </url>") |> ignore
sb.AppendLine("</urlset>") |> ignore

printfn "%s" (sb.ToString())
