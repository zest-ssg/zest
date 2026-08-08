// @permalink /atom.xml
// @layout none
// @title Atom Feed
//
// Generates the site's Atom 1.0 feed via DslXml.atom_xml. Rendered with no
// layout (`@layout none`) and a custom permalink so the output is raw XML at
// /atom.xml.

open System
open Zest.Dsl

let data = Context.get().SiteData
let opt k = if data.ContainsKey(k) then data.[k].ToString() else ""

// TrimEnd guards against a trailing slash in site.base_url producing "//" links.
let siteUrl   = (let u = opt "site.base_url" in if u <> "" then u else "https://example.com").TrimEnd('/')
let siteTitle = if opt "site.title" <> "" then opt "site.title" else "Zest Site"
let siteDesc  = opt "site.description"
let author    = opt "site.author"

let posts =
    site_pages ()
    |> Array.filter (fun p -> p.url.StartsWith("/posts/") && p.url <> "/posts/")
    |> Array.sortByDescending (fun p -> p.date)
    |> Array.map (fun p ->
        { url = p.url; title = p.title; date = p.date; description = p.description } : DslXml.FeedItem)

printfn "%s" (DslXml.atom_xml siteTitle siteUrl siteDesc author posts)
