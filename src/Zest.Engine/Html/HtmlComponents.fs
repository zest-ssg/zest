namespace Zest.Engine.Html

open System
open System.Collections.Generic
open System.Net
open System.Text
open System.Text.RegularExpressions
open Zest.Engine
open Zest.Engine.Zcss

// ============================================================
// HTML DSL — High-level components
// ============================================================

[<AutoOpen>]
module HtmlComponents =

    // ---- Code block ----
    let codeBlock lang (code: string) =
        Element("pre", [],
            [Element("code", ["class", "language-" + lang],
                [Raw (WebUtility.HtmlEncode code)])])

    // ---- Code block with line numbers ----
    let codeBlockNumbered lang (code: string) =
        let lines = code.TrimEnd('\n').Split('\n')
        let lineEls =
            lines |> Array.mapi (fun i line ->
                Element("span", ["class", "line"],
                    [Element("span", ["class", "ln"; "data-line", string (i + 1)], [])
                     Raw (WebUtility.HtmlEncode line + "\n")]))
            |> Array.toList
        Element("pre", ["class", "numbered"],
            [Element("code", ["class", "language-" + lang], lineEls)])

    // ---- Card / badge / alert convenience ----
    let card    ch = divC "card"  ch
    let badge   t  = spanC "badge" [Text t]
    let alert   level ch = divC ("alert alert-" + level) ch

    // ---- Layout primitives ----
    let container ch = divC "container" ch
    let containerFluid ch = divC "container-fluid" ch
    let row ch = divC "row" ch
    let col ch = divC "col" ch
    let flexRow ch = divC "flex-row" ch
    let flexCol ch = divC "flex-col" ch
    let grid ch = divC "grid" ch
    let stack ch = divC "stack" ch

    // ---- Navigation helpers ----
    let navBar brand links =
        nav [
            divC "nav-brand" [a "/" [Text brand]]
            ulC "nav-links" (links |> List.map (fun (label, url) -> li [a url [Text label]]))
        ]

    let navBarRight brand links rightLinks =
        nav [
            divC "nav-brand" [a "/" [Text brand]]
            ulC "nav-links" (links |> List.map (fun (label, url) -> li [a url [Text label]]))
            ulC "nav-right" (rightLinks |> List.map (fun (label, url) -> li [a url [Text label]]))
        ]

    // ---- Pagination ----
    let pagination currentPage totalPages (urlFor: int -> string) =
        let prevBtn =
            if currentPage > 1 then
                li [aC "page-prev" (urlFor (currentPage - 1)) [Text "←"]]
            else
                liC "page-prev disabled" [span [Text "←"]]
        let nextBtn =
            if currentPage < totalPages then
                li [aC "page-next" (urlFor (currentPage + 1)) [Text "→"]]
            else
                liC "page-next disabled" [span [Text "→"]]
        let pageBtns =
            [1..totalPages] |> List.map (fun i ->
                if i = currentPage then
                    liC "page-item active" [span [Text (string i)]]
                else
                    li [aC "page-item" (urlFor i) [Text (string i)]])
        navC "pagination" [
            ulC "pagination-list" ([prevBtn] @ pageBtns @ [nextBtn])
        ]

    // ---- Tag list ----
    let tagList (tags: string list) =
        divC "tag-list" [
            for tag in tags ->
                aC "tag" ("/tags/" + tag + "/") [Text tag]
        ]

    // ---- Post meta ----
    let postMeta (date: string option) (author: string option) (tags: string list) (readingTime: int option) =
        let parts = new ResizeArray<HtmlNode>()
        date |> Option.iter (fun d -> parts.Add(spanC "meta-date" [Text d]))
        author |> Option.iter (fun a -> parts.Add(spanC "meta-author" [Text a]))
        readingTime |> Option.iter (fun t -> parts.Add(spanC "meta-reading" [Text (sprintf "%d min read" t)]))
        if not tags.IsEmpty then
            parts.Add(spanC "meta-tags" [Text "Tags: "])
        divC "post-meta" (parts |> List.ofSeq)

    // ---- Table builder ----
    let tableFrom (headers: string list) (rows: string list list) =
        table [
            thead [tr (headers |> List.map (fun h -> th [Text h]))]
            tbody (rows |> List.map (fun row -> tr (row |> List.map (fun c -> td [Text c]))))
        ]

    // ---- Definition list builder ----
    let dlFrom (pairs: (string * string) list) =
        dl (pairs |> List.collect (fun (t, d) -> [dt [Text t]; dd [Text d]]))

    // ---- Image with srcset ----
    let imgResponsive (src: string) (alt: string) (srcset: string option) (sizes: string option) =
        let baseAttrs = ["src", src; "alt", alt; "loading", "lazy"; "decoding", "async"]
        let withSrcset = srcset |> Option.map (fun s -> "srcset", s) |> Option.toList
        let withSizes = sizes |> Option.map (fun s -> "sizes", s) |> Option.toList
        Element("img", baseAttrs @ withSrcset @ withSizes, [])

    // ---- Picture element ----
    let pictureFrom sources src alt =
        picture [
            for (s, t) in sources -> sourceEl s t
            yield img src alt
        ]

    // ---- Accordion ----
    let accordion (items: (string * HtmlNode list) list) =
        divC "accordion" (
            items |> List.mapi (fun i (title, content) ->
                detailsC "accordion-item" [
                    summaryEl [Text title]
                    divC "accordion-content" content
                ]))

    // ---- Tabs ----
    let tabs (tabs: (string * HtmlNode list) list) =
        let tabBtns = tabs |> List.mapi (fun i (label, _) ->
            buttonC (if i = 0 then "tab-btn active" else "tab-btn") [Text label])
        let tabPanels = tabs |> List.mapi (fun i (_, content) ->
            divC (if i = 0 then "tab-panel active" else "tab-panel") content)
        divC "tabs" [
            divC "tab-list" tabBtns
            divC "tab-panels" tabPanels
        ]

    // ---- Modal/Dialog ----
    let modal id title content =
        dialogC "modal" [
            divC "modal-header" [
                h3 [Text title]
                buttonC "modal-close" [Text "×"]
            ]
            divC "modal-body" content
        ] |> withId id

    // ---- Breadcrumb ----
    let breadcrumb (items: (string * string) list) =
        navC "breadcrumb" [
            olC "breadcrumb-list" (
                items |> List.mapi (fun i (label, url) ->
                    if i = items.Length - 1 then
                        liC "breadcrumb-item active" [Text label]
                    else
                        liC "breadcrumb-item" [a url [Text label]]))
        ]

    // ---- Progress bar ----
    let progressBar (value: int) (max: int) (label: string option) =
        let labelEl = label |> Option.map (fun l -> spanC "progress-label" [Text l]) |> Option.toList
        divC "progress" (labelEl @ [divC "progress-bar" [] |> withStyle (sprintf "width: %d%%" value)])

    // ---- Alert variants ----
    let alertInfo    ch = divC "alert alert-info"    ch
    let alertSuccess ch = divC "alert alert-success" ch
    let alertWarning ch = divC "alert alert-warning" ch
    let alertDanger  ch = divC "alert alert-danger"  ch

    // ---- Badge variants ----
    let badgePrimary ch = spanC "badge badge-primary" ch
    let badgeSuccess ch = spanC "badge badge-success" ch
    let badgeWarning ch = spanC "badge badge-warning" ch
    let badgeDanger  ch = spanC "badge badge-danger"  ch

    // ---- Button variants ----
    let btnPrimary   ch = buttonC "btn btn-primary"   ch
    let btnSecondary ch = buttonC "btn btn-secondary" ch
    let btnSuccess   ch = buttonC "btn btn-success"   ch
    let btnDanger    ch = buttonC "btn btn-danger"    ch
    let btnOutline   ch = buttonC "btn btn-outline"    ch

    // ---- Link button ----
    let linkBtn cls href ch = aC cls href ch

    // ---- Icon (inline SVG) ----
    let icon name =
        svgC ("icon icon-" + name) [
            Element("use", ["href", "#" + name], [])
        ]

    // ---- Avatar ----
    let avatar (src: string) (alt: string option) (size: string option) =
        let sizeClass = size |> Option.map (fun s -> "avatar-" + s) |> Option.defaultValue ""
        imgC ("avatar " + sizeClass) src (alt |> Option.defaultValue "Avatar")

    // ---- Comment (HTML comment) ----
    let comment (text: string) = Raw (sprintf "<!-- %s -->" text)
