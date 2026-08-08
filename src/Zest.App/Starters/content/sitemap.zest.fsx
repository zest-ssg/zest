// @permalink /sitemap.xml
// @layout none
// @title Sitemap
//
// Generates the site's XML sitemap for search engines via DslXml.sitemap_xml.
// Like the RSS feed it is rendered with no layout so the output is raw XML
// at /sitemap.xml.

open System
open Zest.Dsl

let data = Context.get().SiteData
let opt k = if data.ContainsKey(k) then data.[k].ToString() else ""

let siteUrl = let u = opt "site.base_url" in if u <> "" then u else "https://example.com"

let pages =
    site_pages ()
    // Exclude machine-generated routes that should not be indexed.
    |> Array.filter (fun p ->
        p.url <> "/rss.xml" && p.url <> "/atom.xml" && p.url <> "/sitemap.xml" && p.url <> "/404.html")
    |> Array.map (fun p ->
        let priority =
            if p.url = "/" then 1.0
            elif p.url.StartsWith("/posts/") then 0.8
            else 0.5
        { url = p.url; date = p.date; priority = priority } : DslXml.SitemapItem)

printfn "%s" (DslXml.sitemap_xml siteUrl pages)
