namespace Zest.Compiler.Rendering
open System
open System.Collections.Generic
open System.Net
open System.Text
open System.Text.RegularExpressions
open Zest.Compiler.Model
open Zest.Compiler.Zcss

// ============================================================
// Authoring — helpers for use inside .zest.fsx templates
// ============================================================

module Authoring =

    /// Inline Markdown string as an HtmlNode.
    let md (markdownText: string) : HtmlNode =
        Raw(Markdown.toHtml markdownText)

    /// Alias for `md` — full name `markdown` for explicitness.
    let markdown (markdownText: string) : HtmlNode = md markdownText

    /// Compile a ZCSS snippet inline as a `<style>` node.
    let styleBlock (zcssSource: string) : HtmlNode =
        Element("style", [], [Raw(Zcss.processText zcssSource)])

    /// Reference an external stylesheet (.zcss → .css auto-rewritten).
    let stylesheet (href: string) : HtmlNode =
        let cssHref =
            if href.EndsWith(FileTypes.Zcss, StringComparison.OrdinalIgnoreCase)
            then href.[..href.Length - 5] + "css"
            else href
        Element("link", ["rel", "stylesheet"; "href", cssHref], [])
