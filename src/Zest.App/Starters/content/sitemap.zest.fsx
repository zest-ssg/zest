// @permalink /sitemap.xml
// @layout none
// @title Sitemap

open Zest.Dsl

let data = Context.get().SiteData
let opt k = if data.ContainsKey(k) then data.[k].ToString() else ""

let siteUrl = let u = opt "site.base_url" in if u <> "" then u else "https://example.com"

let pages =
    site_pages ()
    |> Array.filter (fun p -> p.url <> "/rss.xml" && p.url <> "/atom.xml" && p.url <> "/sitemap.xml")
    |> Array.map (fun p ->
        let priority = if p.url = "/" then 1.0 else 0.5
        { url = p.url; date = p.date; priority = priority } : DslXml.SitemapItem)

printfn "%s" (DslXml.sitemap_xml siteUrl pages)
