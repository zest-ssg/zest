namespace Zest.Dsl

open System
open System.IO
open System.Text.Json

// ============================================================
// Zest DSL — Pre-compiled helpers for FSI script evaluation
// ============================================================
// This module is compiled to a DLL and referenced via #r in
// FSI scripts. This avoids recompiling ~250 lines of helper
// code on every script evaluation, dramatically improving
// build performance.
// ============================================================

type ZestContext(ctxFile: string) =
    let json = File.ReadAllText(ctxFile)
    let doc = JsonDocument.Parse(json)
    let root = doc.RootElement

    member _.Pages =
        root.GetProperty("pages").EnumerateArray()
        |> Seq.map (fun e ->
            let tags =
                e.GetProperty("tags").EnumerateArray()
                |> Seq.map (fun t -> t.GetString())
                |> Seq.toArray
            {| url=e.GetProperty("url").GetString()
               title=e.GetProperty("title").GetString()
               date=e.GetProperty("date").GetString()
               slug=e.GetProperty("slug").GetString()
               description=e.GetProperty("description").GetString()
               tags=tags |})
        |> Seq.toArray

    member _.Includes =
        root.GetProperty("includes").EnumerateObject()
        |> Seq.map (fun m -> m.Name, m.Value.GetString())
        |> dict

    member _.SiteData =
        root.GetProperty("siteData").EnumerateObject()
        |> Seq.map (fun m -> m.Name, m.Value.GetString())
        |> dict

/// Global context instance — set by ScriptRunner before evaluation
module Context =
    let mutable current: ZestContext option = None
    let get() =
        match current with
        | Some c -> c
        | None -> failwith "ZestContext not initialized. Call Context.set first."

/// Module containing all DSL helpers — opened by user scripts
module Dsl =
    open Context

    let htmlEncode (s: string) =
        s.Replace("&","&amp;").Replace("<","&lt;").Replace(">","&gt;").Replace("\"","&quot;")
    let text s = htmlEncode s
    let raw  s = s
    let attr k v = sprintf "%s=\"%s\"" k (htmlEncode v)
    let elem tag (attrs: string list) (children: string list) =
        let a = if attrs.IsEmpty then "" else " " + String.concat " " attrs
        sprintf "<%s%s>%s</%s>" tag a (String.concat "" children) tag
    let voidElem tag (attrs: string list) =
        let a = if attrs.IsEmpty then "" else " " + String.concat " " attrs
        sprintf "<%s%s />" tag a
    let a url (ch: string list) = elem "a" [attr "href" url] ch
    let span (ch: string list) = elem "span" [] ch
    let code (ch: string list) = elem "code" [] ch
    let strong (ch: string list) = elem "strong" [] ch
    let em (ch: string list) = elem "em" [] ch
    let img src alt = voidElem "img" [attr "src" src; attr "alt" alt]
    let br () = voidElem "br" []
    let hr () = voidElem "hr" []
    let h1 ch = elem "h1" [] ch
    let h2 ch = elem "h2" [] ch
    let h3 ch = elem "h3" [] ch
    let h4 ch = elem "h4" [] ch
    let h5 ch = elem "h5" [] ch
    let h6 ch = elem "h6" [] ch
    let p ch = elem "p" [] ch
    let div ch = elem "div" [] ch
    let section ch = elem "section" [] ch
    let article ch = elem "article" [] ch
    let nav ch = elem "nav" [] ch
    let header ch = elem "header" [] ch
    let footer ch = elem "footer" [] ch
    let main ch = elem "main" [] ch
    let ul ch = elem "ul" [] ch
    let ol ch = elem "ol" [] ch
    let li ch = elem "li" [] ch
    let blockquote ch = elem "blockquote" [] ch
    let pre ch = elem "pre" [] ch
    let table ch = elem "table" [] ch
    let thead ch = elem "thead" [] ch
    let tbody ch = elem "tbody" [] ch
    let tr ch = elem "tr" [] ch
    let th ch = elem "th" [] ch
    let td ch = elem "td" [] ch
    let divC cls ch = elem "div" [attr "class" cls] ch
    let pC cls ch = elem "p" [attr "class" cls] ch
    let spanC cls ch = elem "span" [attr "class" cls] ch
    let sectionC cls ch = elem "section" [attr "class" cls] ch
    let codeBlock lang c = elem "pre" [] [elem "code" [attr "class" ("lang-"+lang)] [c]]
    let showIf cond ch = if cond then ch else ""
    let hideIf cond ch = if cond then "" else ch
    let render (nodes: string list) = printf "%s" (String.concat "\n" nodes)

    // Extended DSL helpers
    let aC cls url ch = elem "a" [attr "href" url; attr "class" cls] ch
    let h1C cls ch = elem "h1" [attr "class" cls] ch
    let h2C cls ch = elem "h2" [attr "class" cls] ch
    let h3C cls ch = elem "h3" [attr "class" cls] ch
    let ulC cls ch = elem "ul" [attr "class" cls] ch
    let olC cls ch = elem "ol" [attr "class" cls] ch
    let liC cls ch = elem "li" [attr "class" cls] ch
    let navC cls ch = elem "nav" [attr "class" cls] ch
    let headerC cls ch = elem "header" [attr "class" cls] ch
    let footerC cls ch = elem "footer" [attr "class" cls] ch
    let mainC cls ch = elem "main" [attr "class" cls] ch
    let articleC cls ch = elem "article" [attr "class" cls] ch
    let asideC cls ch = elem "aside" [attr "class" cls] ch
    let formC cls action ch = elem "form" [attr "action" action; attr "class" cls] ch
    let buttonC cls ch = elem "button" [attr "class" cls] ch
    let labelC cls forVal ch = elem "label" [attr "for" forVal; attr "class" cls] ch
    let tableC cls ch = elem "table" [attr "class" cls] ch
    let blockquoteC cls ch = elem "blockquote" [attr "class" cls] ch
    let preC cls ch = elem "pre" [attr "class" cls] ch
    let codeC cls ch = elem "code" [attr "class" cls] ch
    let imgC cls src alt = voidElem "img" [attr "src" src; attr "alt" alt; attr "class" cls]
    let aBlank url t = elem "a" [attr "href" url; attr "target" "_blank"; attr "rel" "noopener noreferrer"] [text t]
    let aHref url t = elem "a" [attr "href" url] [text t]
    let small ch = elem "small" [] ch
    let mark ch = elem "mark" [] ch
    let del ch = elem "del" [] ch
    let abbr title ch = elem "abbr" [attr "title" title] ch
    let figure src alt cap = elem "figure" [] [voidElem "img" [attr "src" src; attr "alt" alt]; elem "figcaption" [] [text cap]]
    let details summary ch = elem "details" [] (elem "summary" [] [text summary] :: ch)
    let form action ch = elem "form" [attr "action" action] ch
    let input t n v = voidElem "input" [attr "type" t; attr "name" n; attr "value" v]
    let button ch = elem "button" [] ch
    let textarea name ch = elem "textarea" [attr "name" name] ch
    let select name ch = elem "select" [attr "name" name] ch
    let option value ch = elem "option" [attr "value" value] ch
    let label forVal ch = elem "label" [attr "for" forVal] ch
    let doctype = "<!DOCTYPE html>"
    let html ch = elem "html" [] ch
    let head ch = elem "head" [] ch
    let body ch = elem "body" [] ch
    let title ch = elem "title" [] ch
    let meta attrs = voidElem "meta" attrs
    let link rel href = voidElem "link" [attr "rel" rel; attr "href" href]
    let stylesheet href = link "stylesheet" href
    let script src = voidElem "script" [attr "src" src]
    let scriptInline code = elem "script" [] [raw code]
    let style css = elem "style" [] [raw css]

    // Layout helpers
    let container ch = divC "container" ch
    let row ch = divC "row" ch
    let col ch = divC "col" ch
    let card ch = divC "card" ch
    let badge t = spanC "badge" [text t]
    let alert level ch = divC ("alert alert-" + level) ch
    let alertInfo ch = divC "alert alert-info" ch
    let alertSuccess ch = divC "alert alert-success" ch
    let alertWarning ch = divC "alert alert-warning" ch
    let alertDanger ch = divC "alert alert-danger" ch
    let btnPrimary ch = buttonC "btn btn-primary" ch
    let btnSecondary ch = buttonC "btn btn-secondary" ch
    let btnSuccess ch = buttonC "btn btn-success" ch
    let btnDanger ch = buttonC "btn btn-danger" ch

    // Utility helpers
    let each items f = items |> List.map f |> String.concat ""
    let joinWith sep items = String.concat sep items
    let opt v = match v with Some x -> x | None -> ""
    let renderIf cond node fallback = if cond then node else fallback
    let renderOpt v f = match v with Some x -> f x | None -> ""

    // Collections API
    let site_pages () = (get()).Pages
    let recent_pages n = (get()).Pages |> Array.filter (fun r -> r.date <> "") |> Array.sortByDescending (fun r -> r.date) |> Array.truncate n
    let pages_by_tag (tag: string) = (get()).Pages |> Array.filter (fun r -> r.tags |> Array.exists (fun t -> t.Equals(tag, StringComparison.OrdinalIgnoreCase)))
    let pages_by_dir dir = (get()).Pages |> Array.filter (fun r -> r.url.Contains("/" + dir + "/"))
    let pages_by_collection col = (get()).Pages |> Array.filter (fun r -> r.url.Trim('/').Split('/').[0] = col)
    let all_tags () = (get()).Pages |> Array.collect (fun r -> r.tags) |> Array.distinct |> Array.sort
    let all_collections () = (get()).Pages |> Array.map (fun r -> r.url.Trim('/').Split('/').[0]) |> Array.distinct |> Array.sort
    let search_pages (query: string) = (get()).Pages |> Array.filter (fun r -> r.title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
    let page_count () = (get()).Pages.Length
    let include_partial name = match (get()).Includes.TryGetValue(name) with true, c -> c | _ -> sprintf "<!-- include '%s' not found -->" name
    let site_data key = match (get()).SiteData.TryGetValue(key) with true, v -> v | _ -> ""
    let site_section prefix =
        (get()).SiteData
        |> Seq.filter (fun kv -> kv.Key.StartsWith(prefix + "."))
        |> Seq.map (fun kv -> kv.Key.Substring(prefix.Length + 1), kv.Value)
        |> dict

    // Date helpers
    let format_date (dateStr: string) =
        match DateTime.TryParse(dateStr) with
        | true, d -> d.ToString("yyyy-MM-dd")
        | _ -> dateStr
    let format_date_custom (dateStr: string) (fmt: string) =
        match DateTime.TryParse(dateStr) with
        | true, d -> d.ToString(fmt)
        | _ -> dateStr
    let now () = DateTime.Now.ToString("yyyy-MM-dd")
    let current_year () = DateTime.Now.Year.ToString()
