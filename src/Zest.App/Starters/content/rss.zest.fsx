// @permalink /rss.xml
// @layout none
// @title RSS Feed
//
// Generates the site's RSS 2.0 feed. Rendered with no layout (`@layout none`)
// and a custom permalink so the output is raw XML at /rss.xml, which the
// <link rel="alternate"> tags in the layouts point to.
//
// The XML is built inline (rather than via DslXml) so the script only reads
// page fields and never crosses the F# anonymous-record assembly boundary.

open System
open System.Globalization
open System.Text
open Zest.Dsl

let inv = CultureInfo.InvariantCulture

let xe (s: string) =
    if String.IsNullOrEmpty s then "" else
    s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
     .Replace("\"", "&quot;").Replace("'", "&apos;")

let data = Context.get().SiteData
let opt k = if data.ContainsKey(k) then data.[k] else ""

let siteUrl   = let u = opt "site.base_url" in if u <> "" then u else "https://example.com"
let siteTitle = if opt "site.title" <> "" then opt "site.title" else "Zest Site"
let siteDesc  = opt "site.description"

let posts =
    site_pages ()
    |> Array.filter (fun p -> p.url.StartsWith("/posts/") && p.url <> "/posts/")
    |> Array.sortByDescending (fun p -> p.date)

let sb = StringBuilder()
sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>") |> ignore
sb.AppendLine("<rss version=\"2.0\" xmlns:atom=\"http://www.w3.org/2005/Atom\">") |> ignore
sb.AppendLine("  <channel>") |> ignore
sb.AppendFormat("    <title>{0}</title>\n", xe siteTitle) |> ignore
sb.AppendFormat("    <link>{0}</link>\n", xe siteUrl) |> ignore
sb.AppendFormat("    <description>{0}</description>\n", xe siteDesc) |> ignore
sb.AppendFormat("    <atom:link href=\"{0}/rss.xml\" rel=\"self\" type=\"application/rss+xml\" />\n", xe siteUrl) |> ignore
sb.AppendLine("    <language>en</language>") |> ignore
sb.AppendFormat("    <lastBuildDate>{0}</lastBuildDate>\n", DateTime.UtcNow.ToString("ddd, dd MMM yyyy HH:mm:ss 'GMT'", inv)) |> ignore
for p in posts do
    let full = if p.url.StartsWith("/") then siteUrl.TrimEnd('/') + p.url else p.url
    let pub =
        match DateTime.TryParse(p.date, inv, DateTimeStyles.None) with
        | true, d -> d.ToString("ddd, dd MMM yyyy HH:mm:ss 'GMT'", inv)
        | _ -> DateTime.UtcNow.ToString("ddd, dd MMM yyyy HH:mm:ss 'GMT'", inv)
    sb.AppendLine("    <item>") |> ignore
    sb.AppendFormat("      <title>{0}</title>\n", xe p.title) |> ignore
    sb.AppendFormat("      <link>{0}</link>\n", xe full) |> ignore
    sb.AppendFormat("      <guid>{0}</guid>\n", xe full) |> ignore
    sb.AppendFormat("      <pubDate>{0}</pubDate>\n", pub) |> ignore
    sb.AppendFormat("      <description>{0}</description>\n", xe p.description) |> ignore
    sb.AppendLine("    </item>") |> ignore
sb.AppendLine("  </channel>") |> ignore
sb.AppendLine("</rss>") |> ignore

printfn "%s" (sb.ToString())
