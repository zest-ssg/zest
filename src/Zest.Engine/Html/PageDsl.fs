namespace Zest.Engine.Html

open System
open System.Collections.Generic
open System.Net
open System.Text
open System.Text.RegularExpressions
open Zest.Engine

// ============================================================
// Page Computation Expression — Enhanced DSL
// ============================================================

type PageBuilder () =
    // ── Implicit yield core methods ────────────────────────────────
    /// Implicit yield: a single HtmlNode is automatically appended as content.
    member _.Yield(node: HtmlNode) : Page =
        { Page.empty with ContentNodes = [node] }

    /// Implicit yield: a list of HtmlNode is set as content directly.
    member _.Yield(nodes: HtmlNode list) : Page =
        { Page.empty with ContentNodes = nodes }

    /// Implicit yield from a string (auto-wraps as Text node).
    member _.Yield(text: string) : Page =
        { Page.empty with ContentNodes = [Text text] }

    /// Combine two page states by concatenating their content nodes.
    member _.Combine(a: Page, b: Page) : Page =
        { a with ContentNodes = a.ContentNodes @ b.ContentNodes }

    /// Delay evaluation for proper computation expression semantics.
    member _.Delay(f: unit -> Page) : Page = f ()

    /// Zero: empty page state (used for empty branches).
    member _.Zero() : Page = Page.empty

    /// For: enables `for x in items do expr` inside the page CE.
    member _.For(items: 'a seq, f: 'a -> Page) : Page =
        let results = items |> Seq.map f |> Seq.toList
        let merged =
            results
            |> List.fold (fun acc p -> { acc with ContentNodes = acc.ContentNodes @ p.ContentNodes }) Page.empty
        merged

    /// TryWith: try/catch error handling inside the page CE.
    member _.TryWith(body: unit -> Page, handler: exn -> Page) : Page =
        try body ()
        with ex ->
            handler ex

    /// TryFinally: try/finally inside the page CE.
    member _.TryFinally(body: unit -> Page, compensation: unit -> unit) : Page =
        try body ()
        finally compensation ()

    /// Using: resource management inside the page CE.
    member _.Using(resource: 'T :> IDisposable, body: 'T -> Page) : Page =
        try body resource
        finally
            match box resource with
            | null -> ()
            | _ -> resource.Dispose()

    member _.Run(state: Page) = { state with Content = state.ContentNodes |> HtmlRenderer.render }

    // ── Page metadata operations ──────────────────────────────────
    [<CustomOperation "layout">]
    member _.Layout(s: Page, v)    = { s with Layout = Some v }
    [<CustomOperation "title">]
    member _.Title(s: Page, v)     = { s with Title = v }
    [<CustomOperation "permalink">]
    member _.Permalink(s: Page, v) = { s with Permalink = Some v; Url = v }
    [<CustomOperation "slug">]
    member _.Slug(s: Page, v)      = { s with Slug = v }
    [<CustomOperation "tags">]
    member _.Tags(s: Page, v)      = { s with Tags = v }
    [<CustomOperation "tag">]
    member _.Tag(s: Page, v)       = { s with Tags = v :: s.Tags }
    [<CustomOperation "date">]
    member _.Date(s: Page, v)      = { s with Date = Some v }
    [<CustomOperation "description">]
    member _.Description(s: Page, v) =
        let d = Dictionary<string, obj>()
        for kv in s.Data do d.[kv.Key] <- kv.Value
        d.["description"] <- box v
        { s with Data = d }
    [<CustomOperation "data">]
    member _.Data(s: Page, key, value : obj) =
        let d = Dictionary<string, obj>()
        for kv in s.Data do d.[kv.Key] <- kv.Value
        d.[key] <- value
        { s with Data = d }

    // ── Content operations ────────────────────────────────────────
    [<CustomOperation "content">]
    member _.Content(s: Page, nodes : HtmlNode list) =
        { s with ContentNodes = nodes }

    // ── Append content (for incremental content building) ─────────
    [<CustomOperation "append">]
    member _.Append(s: Page, nodes : HtmlNode list) =
        { s with ContentNodes = s.ContentNodes @ nodes }

    // ── Prepend content ───────────────────────────────────────────
    [<CustomOperation "prepend">]
    member _.Prepend(s: Page, nodes : HtmlNode list) =
        { s with ContentNodes = nodes @ s.ContentNodes }

    // ── Conditional content (if/then/else in CE) ──────────────────
    [<CustomOperation "if_content">]
    member _.IfContent(s: Page, cond: bool, nodes: HtmlNode list) =
        if cond then
            { s with ContentNodes = s.ContentNodes @ nodes }
        else s

    // ── Switch/match content (pattern-match style) ────────────────
    [<CustomOperation "match_content">]
    member _.MatchContent(s: Page, cases: (bool * HtmlNode list) list) =
        let matched = cases |> List.tryFind (fun (c, _) -> c)
        match matched with
        | Some(_, nodes) -> { s with ContentNodes = s.ContentNodes @ nodes }
        | None -> s

    // ── For-each content (list comprehension in CE) ───────────────
    [<CustomOperation "for_each">]
    member _.ForEach(s: Page, items: 'a list, render: 'a -> HtmlNode) =
        let nodes = items |> List.map render
        { s with ContentNodes = s.ContentNodes @ nodes }

    // ── Set page data from a map ──────────────────────────────────
    [<CustomOperation "data_from">]
    member _.DataFrom(s: Page, pairs: (string * obj) list) =
        let d = Dictionary<string, obj>()
        for kv in s.Data do d.[kv.Key] <- kv.Value
        for (k, v) in pairs do d.[k] <- v
        { s with Data = d }

    // ── Override output path ──────────────────────────────────────
    [<CustomOperation "output">]
    member _.Output(s: Page, path: string) =
        { s with OutputPath = path }

    // ── Set source path (for programmatic pages) ──────────────────
    [<CustomOperation "source">]
    member _.Source(s: Page, path: string) =
        { s with SourcePath = path }

    // ── Author metadata ───────────────────────────────────────────
    [<CustomOperation "author">]
    member _.Author(s: Page, v: string) =
        let d = Dictionary<string, obj>()
        for kv in s.Data do d.[kv.Key] <- kv.Value
        d.["author"] <- box v
        { s with Data = d }

    // ── Category metadata ─────────────────────────────────────────
    [<CustomOperation "category">]
    member _.Category(s: Page, v: string) =
        let d = Dictionary<string, obj>()
        for kv in s.Data do d.[kv.Key] <- kv.Value
        d.["category"] <- box v
        { s with Data = d }

    // ── Thumbnail image URL ───────────────────────────────────────
    [<CustomOperation "thumbnail">]
    member _.Thumbnail(s: Page, v: string) =
        let d = Dictionary<string, obj>()
        for kv in s.Data do d.[kv.Key] <- kv.Value
        d.["thumbnail"] <- box v
        { s with Data = d }

    // ── Redirect from URL (for alias/redirect pages) ──────────────
    [<CustomOperation "redirect_from">]
    member _.RedirectFrom(s: Page, v: string) =
        let d = Dictionary<string, obj>()
        for kv in s.Data do d.[kv.Key] <- kv.Value
        d.["redirect_from"] <- box v
        { s with Data = d }

    // ══════════════════════════════════════════════════════════════
    // ── NEW: Syntactic sugar custom operations ────────────────────
    // ══════════════════════════════════════════════════════════════

    // ── when': shorthand conditional content append ───────────────
    /// Append content only when a condition is true.
    /// Syntax sugar for `if_content`.
    /// Usage: `when' (isPublished) [ h1 [text "Published"] ]`
    [<CustomOperation "when'">]
    member _.When(s: Page, cond: bool, nodes: HtmlNode list) =
        if cond then { s with ContentNodes = s.ContentNodes @ nodes }
        else s

    // ── unless: negative conditional content append ───────────────
    /// Append content only when a condition is false (negated `when'`).
    /// Usage: `unless (isDraft) [ p [text "Public post"] ]`
    [<CustomOperation "unless">]
    member _.Unless(s: Page, cond: bool, nodes: HtmlNode list) =
        if not cond then { s with ContentNodes = s.ContentNodes @ nodes }
        else s

    // ── choose_content: inline ternary for content ────────────────
    /// Choose between two content branches based on a condition.
    /// Usage: `choose_content (hasImage) [ img "a.jpg" "alt" ] [ text "No image" ]`
    [<CustomOperation "choose_content">]
    member _.ChooseContent(s: Page, cond: bool, ifTrue: HtmlNode list, ifFalse: HtmlNode list) =
        let nodes = if cond then ifTrue else ifFalse
        { s with ContentNodes = s.ContentNodes @ nodes }

    // ── for_pages: iterate over site pages ────────────────────────
    /// Render content for each page in a list using a template function.
    /// Usage: `for_pages (recent_pages 5) (fun p -> li [a p.url [text p.title]])`
    [<CustomOperation "for_pages">]
    member _.ForPages(s: Page, pages: 'a list, render: 'a -> HtmlNode) =
        let nodes = pages |> List.map render
        { s with ContentNodes = s.ContentNodes @ nodes }

    // ── for_range: iterate over an integer range ──────────────────
    /// Render content for each integer in a range.
    /// Usage: `for_range 1 5 (fun i -> li [text (string i)])`
    [<CustomOperation "for_range">]
    member _.ForRange(s: Page, start: int, endInclusive: int, render: int -> HtmlNode) =
        let nodes = [start..endInclusive] |> List.map render
        { s with ContentNodes = s.ContentNodes @ nodes }

    // ── repeat: repeat a node N times ─────────────────────────────
    /// Repeat the same content N times.
    /// Usage: `repeat 3 [ br () ]`
    [<CustomOperation "repeat">]
    member _.Repeat(s: Page, count: int, nodes: HtmlNode list) =
        let repeated = [1..count] |> List.collect (fun _ -> nodes)
        { s with ContentNodes = s.ContentNodes @ repeated }

    // ── spaced: insert separator between content items ────────────
    /// Append content items interspersed with a separator node.
    /// Usage: `spaced (hr ()) [ p [text "A"]; p [text "B"] ]`
    [<CustomOperation "spaced">]
    member _.Spaced(s: Page, sep: HtmlNode, items: HtmlNode list) =
        let spaced = items |> List.collect (fun n -> [sep; n]) |> List.tail
        { s with ContentNodes = s.ContentNodes @ spaced }

    // ── raw_html: inject raw HTML string ──────────────────────────
    /// Inject raw (unescaped) HTML string as content.
    /// Usage: `raw_html "<script>console.log('hi')</script>"`
    [<CustomOperation "raw_html">]
    member _.RawHtml(s: Page, html: string) =
        { s with ContentNodes = s.ContentNodes @ [Raw html] }

    // ── css: inject a <style> block ───────────────────────────────
    /// Inject a CSS style block.
    /// Usage: `css ".hero { color: red }"`
    [<CustomOperation "css">]
    member _.Css(s: Page, cssText: string) =
        { s with ContentNodes = s.ContentNodes @ [Element("style", [], [Raw cssText])] }

    // ── js: inject a <script> block ───────────────────────────────
    /// Inject a JavaScript block.
    /// Usage: `js "console.log('loaded')"`
    [<CustomOperation "js">]
    member _.Js(s: Page, jsText: string) =
        { s with ContentNodes = s.ContentNodes @ [Element("script", [], [Raw jsText])] }

module PageDsl =
    /// Computation expression entry point: `page { title "..."; content [...] }`.
    let page = PageBuilder()

// ============================================================
// Template DSL — Partial views and layout inheritance
// ============================================================

[<AutoOpen>]
module TemplateDsl =

    /// Partial view builder — render a reusable template fragment.
    /// Usage: `partial "card" { divC "card" [...] }`
    type PartialBuilder(name: string) =
        member _.Yield _ = ([] : HtmlNode list)
        member _.Run(nodes: HtmlNode list) =
            Element("div", ["data-partial", name], nodes)

    /// Create a named partial view.
    let partial (name: string) = PartialBuilder(name)

    /// Render a partial view by name from the registry.
    let renderPartial (name: string) (content: HtmlNode list) =
        Element("div", ["data-partial", name], content)

    /// Slot — a placeholder for content injection (like Vue/Blazor slots).
    let slot (name: string) =
        Element("slot", ["name", name], [])

    /// Inject content into a named slot.
    let fillSlot (name: string) (content: HtmlNode list) =
        Element("template", ["slot", name], content)

    // ============================================================
    // Control Flow DSL — Match/When/Switch expressions
    // ============================================================

    /// Match expression builder — pattern-match style content rendering.
    /// Usage: ``match` value |> when' (fun x -> x = "a") [text "A"] |> when' (fun x -> x = "b") [text "B"] |> default' [text "Other"]``
    type MatchBuilder<'T>() =
        member _.Yield _ = ([] : (bool * HtmlNode list) list)
        [<CustomOperation "when'">]
        member _.When(state: (bool * HtmlNode list) list, cond: 'T -> bool, nodes: HtmlNode list) =
            state @ [(true, nodes)]  // placeholder; actual eval deferred
        member _.Run(cases: (bool * HtmlNode list) list) =
            cases |> List.tryFind fst |> Option.map snd |> Option.defaultValue []

    /// Switch expression — evaluate a value against cases.
    let switch (value: 'T) (cases: ('T * HtmlNode list) list) (defaultCase: HtmlNode list) =
        cases |> List.tryFind (fun (v, _) -> v = value)
        |> Option.map snd
        |> Option.defaultValue defaultCase

    /// Cond expression — evaluate conditions in order, return first match.
    let cond (cases: (bool * HtmlNode) list) (fallback: HtmlNode) =
        cases |> List.tryFind fst |> Option.map snd |> Option.defaultValue fallback

    /// CondList expression — evaluate conditions, return first match as a list.
    let condList (cases: (bool * HtmlNode list) list) (fallback: HtmlNode list) =
        cases |> List.tryFind fst |> Option.map snd |> Option.defaultValue fallback

    // ============================================================
    // Expression DSL — String interpolation, pipe chains
    // ============================================================

    /// String interpolation: interp "Hello {name}" ["name", "World"]
    let interp (template: string) (vars: (string * string) list) =
        let dict = dict vars
        Regex.Replace(template, @"\{(\w+)\}", fun m ->
            match dict.TryGetValue(m.Groups.[1].Value) with
            | true, v -> v | _ -> m.Value)

    /// Safe string interpolation with HTML encoding.
    let interpSafe (template: string) (vars: (string * string) list) =
        let dict = dict vars
        Regex.Replace(template, @"\{(\w+)\}", fun m ->
            match dict.TryGetValue(m.Groups.[1].Value) with
            | true, v -> WebUtility.HtmlEncode v | _ -> m.Value)

    /// Conditional expression: choose between two values.
    let inline choose (cond: bool) (ifTrue: 'T) (ifFalse: 'T) =
        if cond then ifTrue else ifFalse

    /// Chain of conditions — like a switch on conditions.
    let chainCond (conditions: (bool * 'T) list) (fallback: 'T) =
        conditions |> List.tryFind fst |> Option.map snd |> Option.defaultValue fallback

    // ============================================================
    // Collection DSL — Enhanced list operations
    // ============================================================

    /// Filter and render: render only items matching a predicate.
    let filterRender (items: 'a list) (pred: 'a -> bool) (render: 'a -> HtmlNode) =
        items |> List.filter pred |> List.map render |> Fragment

    /// Take first N items and render.
    let takeRender (n: int) (items: 'a list) (render: 'a -> HtmlNode) =
        items |> List.truncate n |> List.map render |> Fragment

    /// Skip first N items and render.
    let skipRender (n: int) (items: 'a list) (render: 'a -> HtmlNode) =
        items |> List.skip n |> List.map render |> Fragment

    /// Chunk items into groups and render each group.
    let chunkRender (size: int) (items: 'a list) (render: 'a list -> HtmlNode) =
        items
        |> List.chunkBySize size
        |> List.map render
        |> Fragment

    /// Zip two lists and render pairs.
    let zipRender (list1: 'a list) (list2: 'b list) (render: 'a -> 'b -> HtmlNode) =
        List.zip list1 list2 |> List.map (fun (a, b) -> render a b) |> Fragment

    /// Group items by a key function and render groups.
    let groupRender (items: 'a list) (keyFn: 'a -> string) (render: string -> 'a list -> HtmlNode) =
        items
        |> List.groupBy keyFn
        |> List.map (fun (k, g) -> render k (List.ofSeq g))
        |> Fragment

    /// Paginate items and render a page.
    let paginate (page: int) (perPage: int) (items: 'a list) (render: 'a list -> HtmlNode) =
        let skipped = (page - 1) * perPage
        items |> List.skip skipped |> List.take perPage |> render

    /// Intersperse a separator between rendered items.
    let intersperse (sep: HtmlNode) (items: HtmlNode list) =
        items |> List.collect (fun n -> [sep; n]) |> List.tail |> Fragment
