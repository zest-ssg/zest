namespace Zest.Engine.Html

open System
open System.Collections.Generic
open System.Net
open System.Text
open System.Text.RegularExpressions
open Zest.Engine

// ============================================================
// HTML Renderer
// ============================================================

module HtmlRenderer =

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

    let rec renderNode (node: HtmlNode) : string =
        match node with
        | Text s -> WebUtility.HtmlEncode s
        | Raw  s -> s
        | Fragment ns     -> ns |> List.map renderNode |> String.concat ""
        | Conditional(true,  n) -> renderNode n
        | Conditional(false, _) -> ""
        | Repeat items         -> items |> List.map renderNode |> String.concat ""
        | Element(tag, attrs, ch) ->
            let attrStr =
                attrs
                |> List.map (fun (k, v) ->
                    // Skip empty attribute values for boolean attributes
                    if String.IsNullOrEmpty v then sprintf " %s" k
                    else sprintf " %s=\"%s\"" k (WebUtility.HtmlEncode v))
                |> String.concat ""
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
        | Text s -> WebUtility.HtmlEncode s
        | Raw  s -> s
        | Fragment ns -> ns |> List.map (renderNodePretty indent) |> String.concat ""
        | Conditional(true, n) -> renderNodePretty indent n
        | Conditional(false, _) -> ""
        | Repeat items -> items |> List.map (renderNodePretty indent) |> String.concat ""
        | Element(tag, attrs, ch) ->
            let attrStr =
                attrs
                |> List.map (fun (k, v) ->
                    if String.IsNullOrEmpty v then sprintf " %s" k
                    else sprintf " %s=\"%s\"" k (WebUtility.HtmlEncode v))
                |> String.concat ""
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
