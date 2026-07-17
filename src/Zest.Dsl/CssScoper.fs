namespace Zest.Dsl

open System
open System.Text
open System.Text.RegularExpressions

// ============================================================
// CssScoper — Scoped CSS Engine
// ============================================================
// Applies a unique scope attribute to all CSS selectors,
// enabling component-level style isolation inside
// .zest.fsx templates.  Inspired by Vue's scoped styles.
// ============================================================

module CssScoper =

    /// Generate a short unique scope attribute value.
    let generateScope () : string =
        Guid.NewGuid().ToString("N").[..7]

    /// Format a scope ID as a CSS attribute selector.
    let private scopeSelector (scopeId: string) =
        sprintf "[data-z-%s]" scopeId

    // ── Selector splitting ────────────────────────────────────
    // Split a multi-selector string on commas, respecting:
    //   - Parentheses (e.g., :not(), :is(), :where(), :has())
    //   - Quotes (e.g., [attr="val,ue"])
    //   - Function calls (e.g., rgb(1,2))

    let private splitSelectors (selector: string) : string list =
        let parts = ResizeArray<string>()
        let mutable depth = 0
        let mutable inQuote = false
        let mutable quoteChar = '"'
        let mutable start = 0
        for i = 0 to selector.Length - 1 do
            let c = selector.[i]
            match c with
            | '(' | '[' -> if not inQuote then depth <- depth + 1
            | ')' | ']' -> if not inQuote then depth <- depth - 1
            | '"' | '\'' ->
                if not inQuote then
                    inQuote <- true
                    quoteChar <- c
                elif c = quoteChar then
                    inQuote <- false
            | ',' ->
                if depth = 0 && not inQuote then
                    parts.Add(selector.[start..i - 1].Trim())
                    start <- i + 1
            | _ -> ()
        if start < selector.Length then
            parts.Add(selector.[start..].Trim())
        parts |> List.ofSeq

    // ── Single selector scoping ──────────────────────────────

    let private scopeSingleSelector (scopeAttr: string) (sel: string) : string =
        let trimmed = sel.Trim()
        if String.IsNullOrEmpty trimmed then trimmed
        elif trimmed = ":root" then ":root"  // :root is always the root element
        elif trimmed.StartsWith("@") then trimmed  // Leave at-rules alone
        elif trimmed.StartsWith(":root") then trimmed
        // Add scope attribute as ancestor
        else
            // Check if selector already contains the scope
            if trimmed.Contains(scopeAttr) then trimmed
            else
                // For pseudo-element-heavy selectors, just prepend
                // e.g., ".card::before" → "[data-z-xxx] .card::before"
                scopeAttr + " " + trimmed

    // ── Public API ────────────────────────────────────────────

    /// Apply a scope attribute selector as an ancestor to every
    /// top-level selector in a CSS block.
    ///
    /// Example:
    ///   applyScope "data-z-abc" ".card { color: red } .card .title { }"
    ///   → "[data-z-abc] .card { color: red } [data-z-abc] .card .title { }"
    let applyScope (scopeAttr: string) (css: string) : string =
        if String.IsNullOrWhiteSpace css then css
        else
            // Build the CSS attribute selector string
            let attrSel = scopeSelector scopeAttr

            // Regex to match CSS rules: selector { ... }
            let rulePattern = Regex(
                @"(?<sel>[^{]+)\s*\{",
                RegexOptions.Compiled ||| RegexOptions.Multiline)

            let result = rulePattern.Replace(css, fun (m: Match) ->
                let rawSelector = m.Groups.["sel"].Value.Trim()
                if String.IsNullOrEmpty rawSelector then
                    m.Value
                elif rawSelector.StartsWith("@") then
                    // Don't scope at-rules themselves; their inner rules will be matched separately
                    m.Value
                else
                    let scopedSelectors =
                        splitSelectors rawSelector
                        |> List.map (scopeSingleSelector attrSel)
                        |> String.concat ", "
                    scopedSelectors + " {"
            )
            result

    /// Apply scope and return both the scoped CSS and the scope attribute string.
    /// Useful for embedding the scope attribute on the container element.
    let applyScopeWithAttr (scopeId: string) (css: string) : string * string =
        let attr = sprintf "data-z-%s" scopeId
        let scoped = applyScope attr css
        scoped, attr

    /// Generate a scoped `<style>` block from raw CSS.
    /// Returns the `<style>` HTML and the scope attribute to use on containers.
    let scopedStyleBlock (css: string) : string * string =
        let scopeId = generateScope ()
        let attr = sprintf "data-z-%s" scopeId
        let scoped = applyScope attr css
        let styleTag =
            sprintf "<style data-scope=\"%s\">\n%s\n</style>" attr scoped
        styleTag, attr
