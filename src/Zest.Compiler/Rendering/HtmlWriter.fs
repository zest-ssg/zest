namespace Zest.Compiler.Rendering
open System
open Zest.Compiler.Model

// ============================================================
// HTML Renderer
// ============================================================
//
// The output boundary: nodes go in, escaped text comes out. All escaping is
// delegated to Escape so there is exactly one implementation of it.

module HtmlWriter =

    // W3C HTML5 void elements — these MUST NOT have a closing tag or self-closing slash.
    let private voidTags = set [
        "area"; "base"; "br"; "col"; "embed"; "hr"; "img"; "input";
        "link"; "meta"; "param"; "source"; "track"; "wbr"
    ]

    // Block-level elements that typically get indentation in pretty-print mode.
    let private blockTags = set [
        "html"; "head"; "body"; "main"; "nav"; "article"; "section"; "aside";
        "header"; "footer"; "div"; "p"; "h1"; "h2"; "h3"; "h4"; "h5"; "h6";
        "ul"; "ol"; "li"; "dl"; "dt"; "dd"; "table"; "thead"; "tbody"; "tfoot";
        "tr"; "th"; "td"; "form"; "fieldset"; "figure"; "figcaption";
        "details"; "summary"; "dialog"; "blockquote"; "pre"; "address";
        "template"; "noscript"; "canvas"; "video"; "audio"; "picture"
    ]

    /// Escape text for an HTML comment body, for callers that build a `Raw`
    /// comment node.
    let escapeComment = Escape.escapeComment

    let private renderAttrs (attrs: (string * string) list) : string =
        attrs
        |> List.map (fun (k, v) ->
            if not (Escape.isValidAttrName k) then
                // Dropped rather than emitted: a name containing whitespace or
                // `=` adds attributes of its own.
                Escape.reportInvalidAttrName k
                ""
            // An empty value is the DSL's spelling of a boolean attribute.
            elif String.IsNullOrEmpty v then sprintf " %s" k
            else sprintf " %s=\"%s\"" k (Escape.escapeAttrValue v))
        |> String.concat ""

    let rec renderNode (node: HtmlNode) : string =
        match node with
        | Text s -> Escape.escapeText s
        | Raw  s -> s
        | Fragment ns           -> ns |> List.map renderNode |> String.concat ""
        | Conditional(true,  n) -> renderNode n
        | Conditional(false, _) -> ""
        | Element(tag, attrs, ch) ->
            let attrStr = renderAttrs attrs
            if voidTags.Contains tag then
                // W3C: void elements must NOT have a trailing slash
                sprintf "<%s%s>" tag attrStr
            else
                let inner = ch |> List.map renderNode |> String.concat ""
                sprintf "<%s%s>%s</%s>" tag attrStr inner tag

    let render (nodes: HtmlNode list) : string =
        nodes |> List.map renderNode |> String.concat ""

    // ── Pretty-print variant with proper indentation ──────────────────────

    let rec private renderNodePretty (indent: int) (node: HtmlNode) : string =
        let ws = String.replicate indent "  "
        match node with
        | Text s -> Escape.escapeText s
        | Raw  s -> s
        | Fragment ns -> ns |> List.map (renderNodePretty indent) |> String.concat ""
        | Conditional(true, n) -> renderNodePretty indent n
        | Conditional(false, _) -> ""
        | Element(tag, attrs, ch) ->
            let attrStr = renderAttrs attrs
            if voidTags.Contains tag then
                sprintf "%s<%s%s>" ws tag attrStr
            elif ch.IsEmpty then
                sprintf "%s<%s%s></%s>" ws tag attrStr tag
            elif blockTags.Contains tag then
                let inner = ch |> List.map (renderNodePretty (indent + 1)) |> String.concat "\n"
                sprintf "%s<%s%s>\n%s\n%s</%s>" ws tag attrStr inner ws tag
            else
                let inner = ch |> List.map (renderNodePretty 0) |> String.concat ""
                sprintf "%s<%s%s>%s</%s>" ws tag attrStr inner tag

    /// Render a list of HtmlNodes to a pretty-printed HTML string
    /// with proper indentation and line breaks for readability.
    let renderPretty (nodes: HtmlNode list) : string =
        nodes |> List.map (renderNodePretty 0) |> String.concat "\n"
