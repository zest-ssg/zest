namespace Zest.Dsl

open System
open System.IO

// ============================================================
// DslStyle — Unified Inline Style API for .zest.fsx Templates
// ============================================================
// Provides a consistent surface for writing both ZCSS
// (computation-expression stylesheets) and native CSS inline
// inside <style> tags.  Users can mix ZCSS CE output with
// raw CSS strings using the same ergonomic API.
//
// Usage:
//   // ZCSS computation expression
//   styleZcss (stylesheet { body [ bg "#000"; color "#fff" ] })
//
//   // Native CSS (with optional validation)
//   styleCss ".card { padding: 1rem; border-radius: 8px; }"
//
//   // Scoped CSS (component isolation)
//   styleScoped ".title { font-size: 2rem; }"
//
//   // External .zcss file reference
//   styleExternal "css/main.zcss"
// ============================================================

[<AutoOpen>]
module DslStyle =
    open Dsl

    // ── ZCSS integration ──────────────────────────────────────

    /// Inline a compiled ZCSS stylesheet string (the output of
    /// `stylesheet { ... }`) inside a `<style>` tag.
    ///
    /// Example:
    ///   let myCss = stylesheet { body [ bg "#f0f0f0"; color "#333" ] }
    ///   styleZcss myCss
    let styleZcss (compiledCss: string) : string =
        if String.IsNullOrWhiteSpace compiledCss then ""
        else elem "style" [] [raw ("\n" + compiledCss + "\n")]

    /// Compile a list of `CssRule` values directly into a `<style>` tag.
    /// Uses `compileStylesheet` under the hood (pretty-printed).
    ///
    /// Example:
    ///   let rules = [ body [ bg "#000" ]; cls "card" [ padding "1rem" ] ]
    ///   styleFromZcss rules
    let styleFromZcss (cssRules: CssRule list) : string =
        if List.isEmpty cssRules then ""
        else
            let compiled = compileStylesheet cssRules
            elem "style" [] [raw ("\n" + compiled + "\n")]

    /// Compile a list of `CssRule` values into a minified `<style>` tag.
    /// Uses `compileStylesheetMinified` — single line, no whitespace.
    ///
    /// Example:
    ///   styleFromZcssMinified [ body [ bg "#000"; color "#fff" ] ]
    let styleFromZcssMinified (cssRules: CssRule list) : string =
        if List.isEmpty cssRules then ""
        else
            let compiled = compileStylesheetMinified cssRules
            elem "style" [] [raw compiled]

    // ── Native CSS integration ────────────────────────────────

    /// Inline a raw CSS string inside a `<style>` tag, with optional
    /// validation controlled by `CssValidator.setLevel`.
    ///
    /// Validation levels:
    ///   - `Off`    — no validation (fastest, default for production)
    ///   - `Warn`   — HTML-comment warnings prepended (default)
    ///   - `Strict` — HTML-comment errors prepended
    ///
    /// Example:
    ///   styleCss ".hero { background: #667eea; padding: 2rem; }"
    let styleCss (css: string) : string =
        if String.IsNullOrWhiteSpace css then ""
        else
            let validated = CssValidator.validate css
            elem "style" [] [raw ("\n" + validated + "\n")]

    // ── Scoped CSS ────────────────────────────────────────────

    /// Generate a `<style>` tag with scoped CSS selectors and
    /// return both the tag and the scope attribute for the container.
    ///
    /// The returned tuple is `(styleTag, scopeAttribute)`.
    /// Apply `scopeAttribute` to the container element to activate isolation.
    ///
    /// Example:
    ///   let styleBlock, scopeAttr = styleScoped ".title { color: red; }"
    ///   div [attr scopeAttr ""] [  ...  ]   // scope the container
    let styleScoped (css: string) : string * string =
        if String.IsNullOrWhiteSpace css then "", ""
        else
            let styleTag, scopeAttr = CssScoper.scopedStyleBlock css
            styleTag, scopeAttr

    /// Generate a scoped `<style>` tag only (no scope attribute returned).
    /// Use when you already have the scope attribute from elsewhere.
    let styleScopedOnly (css: string) : string =
        styleScoped css |> fst

    // ── ZCSS computation expression shortcut ──────────────────

    /// Directly accept `stylesheet { ... }` computation expression
    /// results and wrap in `<style>`.  Syntactic sugar for `styleZcss`.
    ///
    /// Example:
    ///   inlineZcss <| stylesheet { body [ bg "#fff" ] }
    let inline inlineZcss (compiledCss: string) : string =
        styleZcss compiledCss

    // ── External stylesheet helpers ───────────────────────────

    /// Generate a `<link rel="stylesheet">` tag that references an
    /// external `.zcss` file.  The extension is automatically
    /// rewritten from `.zcss` → `.css`.
    ///
    /// Example:
    ///   styleExternal "css/main.zcss"
    ///   → <link rel="stylesheet" href="css/main.css" />
    let styleExternal (zcssPath: string) : string =
        let cssPath =
            if zcssPath.EndsWith(".zcss", StringComparison.OrdinalIgnoreCase) then
                zcssPath.[..zcssPath.Length - 6] + ".css"
            else
                zcssPath
        stylesheet cssPath

    /// Load an external `.zcss` file from disk, compile it, and
    /// inline the result inside a `<style>` tag.
    ///
    /// The file path is resolved relative to the build working directory.
    ///
    /// Example:
    ///   styleInlineExternal "css/theme.zcss"
    let styleInlineExternal (zcssPath: string) : string =
        try
            if not (File.Exists zcssPath) then
                sprintf "<!-- Zest: stylesheet not found: %s -->" zcssPath
            else
                let content = File.ReadAllText(zcssPath)
                // Use the engine-side ZCSS processor if available, otherwise
                // treat as plain CSS (external .zcss files are pre-compiled at build time).
                // For inline usage the file content is already CSS (ZCSS compilation
                // happens during the asset pipeline).
                elem "style" [] [raw ("\n" + content + "\n")]
        with ex ->
            sprintf "<!-- Zest: failed to read stylesheet '%s': %s -->" zcssPath ex.Message

    // ── Critical CSS extraction helper ────────────────────────

    /// Extract "above-the-fold" / critical CSS from a full stylesheet
    /// by matching selectors against a list of known critical patterns.
    ///
    /// This is a heuristic helper for performance tuning — it does not
    /// parse the DOM, only filters by selector name.
    ///
    /// Example:
    ///   criticalCss myFullCss ["body"; ".hero"; ".header"; "h1"]
    let criticalCss (fullCss: string) (criticalSelectors: string list) : string =
        if String.IsNullOrWhiteSpace fullCss || List.isEmpty criticalSelectors then fullCss
        else
            let lines = fullCss.Split('\n')
            let result = ResizeArray<string>()
            let mutable inCriticalBlock = false
            let mutable braceDepth = 0

            for line in lines do
                let trimmed = line.Trim()
                // Detect selector line (before the opening brace)
                if not inCriticalBlock && trimmed.EndsWith("{") then
                    let sel = trimmed.[..trimmed.Length - 2].Trim()
                    inCriticalBlock <-
                        criticalSelectors
                        |> List.exists (fun cs ->
                            sel.Contains(cs, StringComparison.OrdinalIgnoreCase))
                if inCriticalBlock then
                    result.Add(line)
                    braceDepth <- braceDepth + (if trimmed.Contains("{") then 1 else 0)
                                       - (if trimmed.Contains("}") then 1 else 0)
                    if braceDepth <= 0 then
                        inCriticalBlock <- false
                        braceDepth <- 0

            result |> String.concat "\n"
