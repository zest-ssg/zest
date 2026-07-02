namespace Zest.Engine.Html

open System
open Zest.Engine

// ============================================================
// DslSugar — Syntactic sugar for HtmlNode-based DSL
// ============================================================
// Provides simplified builders and helpers for the typed HtmlNode
// DSL used in `page { }` computation expressions and template
// partials.
// ============================================================

[<AutoOpen>]
module DslSugar =

    // ══════════════════════════════════════════════════════════════
    // ── Shorthand node builders (less typing than full DSL) ──────
    // ══════════════════════════════════════════════════════════════

    /// Create a Text node: `t "Hello"`
    let t (s: string) = Text s

    /// Create a Raw HTML node: `r "<br>"`
    let r (s: string) = Raw s

    /// Create an Element node with attributes and children.
    /// Usage: `e "div" ["class", "hero"] [t "Hello"]`
    let e (tag: string) (attrs: (string * string) list) (children: HtmlNode list) =
        Element(tag, attrs, children)

    /// Create a void Element (no children).
    /// Usage: `ve "br" []`
    let ve (tag: string) (attrs: (string * string) list) =
        Element(tag, attrs, [])

    /// Element with just a class attribute.
    /// Usage: `ec "div" "hero" [t "Hello"]`
    let ec (tag: string) (cls: string) (children: HtmlNode list) =
        Element(tag, ["class", cls], children)

    /// Element with just text content.
    /// Usage: `et "h1" "Hello World"`
    let et (tag: string) (textContent: string) =
        Element(tag, [], [Text textContent])

    /// Element with class and text content.
    /// Usage: `ect "p" "lead" "Hello"`
    let ect (tag: string) (cls: string) (textContent: string) =
        Element(tag, ["class", cls], [Text textContent])

    /// Fragment (list of nodes rendered sequentially).
    /// Usage: `f [t "A"; br; t "B"]`
    let f (nodes: HtmlNode list) = Fragment nodes

    /// Conditional node: rendered only if condition is true.
    /// Usage: `cnd (x > 0) (t "positive")`
    let cnd (condition: bool) (node: HtmlNode) =
        Conditional(condition, node)

    /// Conditional with else: choose between two nodes.
    /// Usage: `cnd_else (x > 0) (t "positive") (t "non-positive")`
    let cnd_else (condition: bool) (ifTrue: HtmlNode) (ifFalse: HtmlNode) =
        if condition then ifTrue else ifFalse

    /// Repeated nodes from a list.
    /// Usage: `rep (items |> List.map (fun i -> li [t i]))`
    let rep (nodes: HtmlNode list) = Repeat nodes

    // ══════════════════════════════════════════════════════════════
    // ── Common HTML element shortcuts ────────────────────────────
    // ══════════════════════════════════════════════════════════════

    /// `<br>` void element.
    let br = Element("br", [], [])

    /// `<hr>` void element.
    let hr = Element("hr", [], [])

    /// `<img>` void element.
    let img (src: string) (alt: string) =
        Element("img", ["src", src; "alt", alt], [])

    /// `<img>` with class.
    let img_c (cls: string) (src: string) (alt: string) =
        Element("img", ["src", src; "alt", alt; "class", cls], [])

    /// `<a>` with href and text.
    let a_href (url: string) (textContent: string) =
        Element("a", ["href", url], [Text textContent])

    /// `<a>` with href, class, and children.
    let a_c (cls: string) (url: string) (children: HtmlNode list) =
        Element("a", ["class", cls; "href", url], children)

    /// `<a>` with target="_blank".
    let a_blank (url: string) (textContent: string) =
        Element("a", ["href", url; "target", "_blank"; "rel", "noopener noreferrer"], [Text textContent])

    /// `<div>` with class.
    let div_c (cls: string) (children: HtmlNode list) =
        Element("div", ["class", cls], children)

    /// `<span>` with class.
    let span_c (cls: string) (children: HtmlNode list) =
        Element("span", ["class", cls], children)

    /// `<h1>`-`<h6>` with text.
    let h (level: int) (textContent: string) =
        let tag = sprintf "h%d" level
        Element(tag, [], [Text textContent])

    /// `<p>` with text content.
    let p_text (content: string) =
        Element("p", [], [Text content])

    /// `<p>` with children.
    let p (children: HtmlNode list) =
        Element("p", [], children)

    /// `<li>` with children.
    let li (children: HtmlNode list) =
        Element("li", [], children)

    /// `<li>` with text.
    let li_text (content: string) =
        Element("li", [], [Text content])

    /// `<ul>` with children.
    let ul (children: HtmlNode list) =
        Element("ul", [], children)

    /// `<ol>` with children.
    let ol (children: HtmlNode list) =
        Element("ol", [], children)

    /// `<section>` with class and children.
    let section_c (cls: string) (children: HtmlNode list) =
        Element("section", ["class", cls], children)

    /// `<nav>` with class and children.
    let nav_c (cls: string) (children: HtmlNode list) =
        Element("nav", ["class", cls], children)

    /// `<header>` with class and children.
    let header_c (cls: string) (children: HtmlNode list) =
        Element("header", ["class", cls], children)

    /// `<footer>` with class and children.
    let footer_c (cls: string) (children: HtmlNode list) =
        Element("footer", ["class", cls], children)

    /// `<main>` with class and children.
    let main_c (cls: string) (children: HtmlNode list) =
        Element("main", ["class", cls], children)

    /// `<article>` with class and children.
    let article_c (cls: string) (children: HtmlNode list) =
        Element("article", ["class", cls], children)

    /// `<blockquote>` with class and children.
    let blockquote_c (cls: string) (children: HtmlNode list) =
        Element("blockquote", ["class", cls], children)

    /// `<code>` with class and text.
    let code_c (cls: string) (content: string) =
        Element("code", ["class", cls], [Text content])

    /// `<pre><code>` block.
    let code_block (lang: string) (code: string) =
        Element("pre", [], [
            Element("code", ["class", sprintf "lang-%s" lang], [Text code])
        ])

    /// `<style>` block.
    let style_block (css: string) =
        Element("style", [], [Raw css])

    /// `<script>` block.
    let script_block (js: string) =
        Element("script", [], [Raw js])

    // ══════════════════════════════════════════════════════════════
    // ── Collection rendering helpers ─────────────────────────────
    // ══════════════════════════════════════════════════════════════

    /// Map items to nodes and wrap in a container element.
    /// Usage: `map_in "ul" items (fun i -> li_text i)`
    let map_in (containerTag: string) (items: 'a list) (render: 'a -> HtmlNode) =
        Element(containerTag, [], items |> List.map render)

    /// Map items with an index.
    /// Usage: `mapi items (fun idx item -> li_text (sprintf "%d. %s" (idx+1) item))`
    let mapi (items: 'a list) (render: int -> 'a -> HtmlNode) =
        items |> List.mapi render |> Fragment

    /// Filter items by predicate and render.
    /// Usage: `filter_render items (fun i -> i <> "") (fun i -> li_text i)`
    let filter_render (items: 'a list) (pred: 'a -> bool) (render: 'a -> HtmlNode) =
        items |> List.filter pred |> List.map render |> Fragment

    /// Render items with a separator between them.
    /// Usage: `intersperse hr (items |> List.map li_text)`
    let intersperse_nodes (sep: HtmlNode) (nodes: HtmlNode list) =
        nodes |> List.collect (fun n -> [sep; n]) |> List.tail |> Fragment

    /// Group items by a key and render each group.
    /// Usage: `group_render pages (fun p -> string p.date.Year) (fun year ps -> section_c year (ps |> List.map renderPage))`
    let group_render (items: 'a list) (keyFn: 'a -> string) (render: string -> 'a list -> HtmlNode) =
        items
        |> List.groupBy keyFn
        |> List.map (fun (k, g) -> render k (List.ofSeq g))
        |> Fragment

    /// Chunk items into groups of a given size and render each chunk.
    /// Usage: `chunk_render 3 items (fun chunk -> div_c "row" (chunk |> List.map renderCard))`
    let chunk_render (size: int) (items: 'a list) (render: 'a list -> HtmlNode) =
        items
        |> List.chunkBySize size
        |> List.map render
        |> Fragment

    /// Take first N items and render.
    /// Usage: `take_render 5 items (fun i -> li_text i)`
    let take_render (n: int) (items: 'a list) (render: 'a -> HtmlNode) =
        items |> List.truncate n |> List.map render |> Fragment

    /// Skip first N items and render the rest.
    /// Usage: `skip_render 3 items (fun i -> li_text i)`
    let skip_render (n: int) (items: 'a list) (render: 'a -> HtmlNode) =
        if items.Length <= n then Fragment []
        else items |> List.skip n |> List.map render |> Fragment

    /// Paginate: render only items for a given page.
    /// Usage: `paginate_render 1 10 items (fun item -> card item)`
    let paginate_render (page: int) (perPage: int) (items: 'a list) (render: 'a -> HtmlNode) =
        let skip = (page - 1) * perPage
        items |> List.skip skip |> List.truncate perPage |> List.map render |> Fragment

    // ══════════════════════════════════════════════════════════════
    // ── Common UI component shortcuts ────────────────────────────
    // ══════════════════════════════════════════════════════════════

    /// A simple card component.
    /// Usage: `card "Title" "Body text" "/link" "Read more"`
    let card (title: string) (body: string) (linkUrl: string) (linkText: string) =
        div_c "card" [
            div_c "card-body" [
                h 4 title
                p_text body
                a_c "btn btn-primary" linkUrl [t linkText]
            ]
        ]

    /// A media object (image + text side-by-side).
    /// Usage: `media "/img.jpg" "Alt text" "Title" "Description"`
    let media (src: string) (alt: string) (title: string) (desc: string) =
        div_c "media" [
            img_c "media-img" src alt
            div_c "media-body" [
                h 4 title
                p_text desc
            ]
        ]

    /// A hero section.
    /// Usage: `hero "Welcome" "Subtitle" "/cta" "Get Started"`
    let hero (title: string) (subtitle: string) (ctaUrl: string) (ctaText: string) =
        section_c "hero" [
            div_c "hero-content" [
                h 1 title
                p_text subtitle
                a_c "btn btn-lg btn-primary" ctaUrl [t ctaText]
            ]
        ]

    /// An alert/banner component.
    /// Usage: `alert "info" "This is an info alert"`
    let alert (level: string) (message: string) =
        div_c (sprintf "alert alert-%s" level) [t message]

    /// A badge/tag component.
    /// Usage: `badge "New"`
    let badge (textContent: string) =
        span_c "badge" [t textContent]

    /// A button component.
    /// Usage: `btn "primary" "Click me"`
    let btn (variant: string) (textContent: string) =
        Element("button", ["class", sprintf "btn btn-%s" variant], [t textContent])

    // ══════════════════════════════════════════════════════════════
    // ── Pipeline / chaining operators ────────────────────────────
    // ══════════════════════════════════════════════════════════════

    /// Wrap nodes in an element tag.
    /// Usage: `nodes |> wrap_in "div"`
    let wrap_in (tag: string) (nodes: HtmlNode list) : HtmlNode =
        Element(tag, [], nodes)

    /// Wrap nodes in an element with a class.
    /// Usage: `nodes |> wrap_in_c "div" "container"`
    let wrap_in_c (tag: string) (cls: string) (nodes: HtmlNode list) : HtmlNode =
        Element(tag, ["class", cls], nodes)

    /// Add a CSS class to an element node.
    /// Usage: `elem |> with_class "active"`
    let with_class (cls: string) (node: HtmlNode) : HtmlNode =
        match node with
        | Element(tag, attrs, children) ->
            let cleaned = attrs |> List.filter (fun (k, _) -> k <> "class")
            Element(tag, ("class", cls) :: cleaned, children)
        | other -> other

    /// Add an attribute to an element node.
    /// Usage: `elem |> with_attr "data-id" "42"`
    let with_attr (key: string) (value: string) (node: HtmlNode) : HtmlNode =
        match node with
        | Element(tag, attrs, children) ->
            let cleaned = attrs |> List.filter (fun (k, _) -> k <> key)
            Element(tag, (key, value) :: cleaned, children)
        | other -> other

    /// Add an id attribute to an element node.
    /// Usage: `elem |> with_id "main-content"`
    let with_id (id: string) (node: HtmlNode) = with_attr "id" id node

    // ══════════════════════════════════════════════════════════════
    // ── Validation / error-reporting helpers ─────────────────────
    // ══════════════════════════════════════════════════════════════

    /// Validate a condition and emit an HTML comment with the error
    /// message if the condition is not met.
    /// Usage: `validate_cond (not (String.IsNullOrEmpty title)) "Title is required" renderedTitle`
    let validate_cond (condition: bool) (message: string) (node: HtmlNode) : HtmlNode =
        if condition then node
        else Fragment [Raw (sprintf "<!-- VALIDATION ERROR: %s -->" message); node]

    /// Guard: render only if the string value is non-empty.
    /// Usage: `guard_str title (fun t -> h 1 t)`
    let guard_str (value: string) (render: string -> HtmlNode) : HtmlNode =
        if String.IsNullOrEmpty value then Fragment []
        else render value

    /// Guard for optional values.
    /// Usage: `guard_opt maybeTitle (fun t -> h 1 t) (p_text "Untitled")`
    let guard_opt (value: string option) (render: string -> HtmlNode) (fallback: HtmlNode) : HtmlNode =
        match value with
        | Some v when not (String.IsNullOrEmpty v) -> render v
        | _ -> fallback

    /// Guard a list: render only if non-empty.
    /// Usage: `guard_list items (fun items -> ul (items |> List.map li_text)) (p_text "No items")`
    let guard_list (items: 'a list) (render: 'a list -> HtmlNode) (fallback: HtmlNode) : HtmlNode =
        if List.isEmpty items then fallback
        else render items
