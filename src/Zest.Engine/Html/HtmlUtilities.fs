namespace Zest.Engine.Html

open System
open System.Collections.Generic
open System.Net
open System.Text
open System.Text.RegularExpressions
open Zest.Engine
open Zest.Engine.Zcss

// ============================================================
// HtmlUtilities — helpers for use inside .zpage.fsx templates
// ============================================================

module HtmlUtilities =

    /// Inline Markdown string as an HtmlNode.
    let md (markdownText: string) : HtmlNode =
        Raw(Markdown.toHtml markdownText)

    /// Compile a ZSS snippet inline as a `<style>` node.
    let styleBlock (zssSource: string) : HtmlNode =
        Element("style", [], [Raw(Zcss.Processor.processText zssSource)])

    /// Reference an external stylesheet (.zss → .css auto-rewritten).
    let stylesheet (href: string) : HtmlNode =
        let cssHref =
            if href.EndsWith(".zcss", StringComparison.OrdinalIgnoreCase)
            then href.[..href.Length - 5] + "css"
            else href
        Element("link", ["rel", "stylesheet"; "href", cssHref], [])
