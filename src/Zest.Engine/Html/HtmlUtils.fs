namespace Zest.Engine.Html

open System
open System.Collections.Generic
open System.Net
open System.Text
open System.Text.RegularExpressions
open Zest.Engine
open Zest.Engine.Zss

// ============================================================
// HtmlUtils — helpers for use inside .zest.fsx templates
// ============================================================

module HtmlUtils =

    /// Inline Markdown string as an HtmlNode.
    let md (markdownText: string) : HtmlNode =
        Raw(Markdown.toHtml markdownText)

    /// Compile a ZSS snippet inline as a `<style>` node.
    let styleBlock (zssSource: string) : HtmlNode =
        Element("style", [], [Raw(Zss.Processor.processText zssSource)])

    /// Reference an external stylesheet (.zss → .css auto-rewritten).
    let stylesheet (href: string) : HtmlNode =
        let cssHref =
            if href.EndsWith(".zss", StringComparison.OrdinalIgnoreCase)
            then href.[..href.Length - 5] + "css"
            else href
        Element("link", ["rel", "stylesheet"; "href", cssHref], [])

    /// Reference an external script.
    let jsFile (src: string) : HtmlNode =
        Element("script", ["src", src], [])

    /// Inline JavaScript as a `<script>` node.
    let jsInline (code: string) : HtmlNode =
        Element("script", [], [Raw code])

    /// Generate a `<meta>` charset declaration.
    let charset (enc: string) : HtmlNode =
        Element("meta", ["charset", enc], [])

    /// Generate an Open Graph `<meta>` tag.
    let ogMeta (property: string) (content: string) : HtmlNode =
        Element("meta", ["property", "og:" + property; "content", content], [])

    /// Render a simple two-column definition list from a list of (term, definition) pairs.
    let dl (pairs: (string * string) list) : HtmlNode =
        Element("dl", [],
            pairs |> List.collect (fun (t, d) ->
                [ Element("dt", [], [Text t])
                  Element("dd", [], [Text d]) ]))

    /// Breadcrumb nav from a list of (label, url) pairs.
    let breadcrumb (items: (string * string) list) : HtmlNode =
        Element("nav", ["aria-label", "breadcrumb"],
            [ Element("ol", ["class", "breadcrumb"],
                items |> List.mapi (fun i (label, url) ->
                    let isLast = i = items.Length - 1
                    if isLast then Element("li", ["class", "breadcrumb-item active"], [Text label])
                    else Element("li", ["class", "breadcrumb-item"],
                             [Element("a", ["href", url], [Text label])]))) ])
