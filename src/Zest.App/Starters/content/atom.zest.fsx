// @permalink /atom.xml
// @layout none
// @title Atom Feed

open Zest.Dsl

let data = Context.get().SiteData
let opt k = if data.ContainsKey(k) then data.[k].ToString() else ""

let siteUrl = (let u = opt "site.base_url" in if u <> "" then u else "https://example.com").TrimEnd('/')
let siteTitle = if opt "site.title" <> "" then opt "site.title" else "Zest Site"
let siteDesc = opt "site.description"
let author = opt "site.author"

let articles =
    site_pages ()
    |> Array.filter (fun p -> p.date <> "")
    |> Array.sortByDescending (fun p -> p.date)
    |> Array.map (fun p ->
        { url = p.url; title = p.title; date = p.date; description = p.description } : DslXml.FeedItem)

printfn "%s" (DslXml.atom_xml siteTitle siteUrl siteDesc author articles)
