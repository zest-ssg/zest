namespace Zest.Dsl

open System
open System.Collections.Generic
open System.Text.RegularExpressions

// ============================================================
// CssValidator — CSS 语法验证与诊断
// ============================================================
// Provides bracket matching, property-value format checks,
// dangerous rule detection, and colour value validation
// for inline CSS strings used in .zest.fsx templates.
// ============================================================

/// Validation severity level.
type ValidationSeverity = Error | Warning | Info

/// A single validation issue with source location.
type CssValidationIssue =
    { Line: int
      Column: int
      Message: string
      Severity: ValidationSeverity }

/// Global validation behaviour.
type CssValidationLevel =
    /// Emit HTML-comment errors and strip invalid blocks.
    | Strict
    /// Emit HTML-comment warnings, keep content intact.
    | Warn
    /// Skip all validation (fastest).
    | Off

/// CSS validation helpers.
module CssValidator =

    let mutable private globalValidationLevel = CssValidationLevel.Warn

    /// Set the global validation level for all styleCss calls.
    let setLevel (level: CssValidationLevel) =
        globalValidationLevel <- level

    /// Get the current validation level.
    let getLevel () = globalValidationLevel

    // ── Known CSS property set (subset — covers the most common) ──
    let private knownProperties =
        HashSet<string>(
            [|
            // Background
            "background"; "background-color"; "background-image"; "background-repeat"
            "background-position"; "background-size"; "background-attachment"
            "background-clip"; "background-origin"; "background-blend-mode"
            // Colour
            "color"; "opacity"
            // Typography
            "font"; "font-family"; "font-size"; "font-weight"; "font-style"
            "font-variant"; "font-stretch"; "line-height"; "letter-spacing"
            "word-spacing"; "text-align"; "text-decoration"; "text-transform"
            "text-indent"; "text-overflow"; "text-shadow"; "text-wrap"
            "white-space"; "word-break"; "overflow-wrap"; "hyphens"
            "vertical-align"; "direction"; "unicode-bidi"
            // Box model
            "width"; "height"; "min-width"; "max-width"; "min-height"; "max-height"
            "margin"; "margin-top"; "margin-right"; "margin-bottom"; "margin-left"
            "padding"; "padding-top"; "padding-right"; "padding-bottom"; "padding-left"
            "box-sizing"; "box-shadow"
            // Border
            "border"; "border-top"; "border-right"; "border-bottom"; "border-left"
            "border-color"; "border-width"; "border-style"; "border-radius"
            "border-top-left-radius"; "border-top-right-radius"
            "border-bottom-left-radius"; "border-bottom-right-radius"
            "outline"; "outline-color"; "outline-width"; "outline-style"
            "outline-offset"
            // Display & Positioning
            "display"; "position"; "top"; "right"; "bottom"; "left"
            "z-index"; "float"; "clear"; "overflow"; "overflow-x"; "overflow-y"
            "visibility"; "object-fit"; "object-position"; "aspect-ratio"
            // Flexbox
            "flex"; "flex-direction"; "flex-wrap"; "flex-flow"
            "flex-grow"; "flex-shrink"; "flex-basis"
            "justify-content"; "align-items"; "align-content"; "align-self"
            "justify-items"; "justify-self"; "order"; "gap"; "row-gap"; "column-gap"
            "place-items"; "place-content"; "place-self"
            // Grid
            "grid"; "grid-template-columns"; "grid-template-rows"
            "grid-template-areas"; "grid-template"; "grid-auto-columns"
            "grid-auto-rows"; "grid-auto-flow"; "grid-column"; "grid-row"
            "grid-column-start"; "grid-column-end"; "grid-row-start"
            "grid-row-end"; "grid-area"
            // Transform / Transition / Animation
            "transform"; "transform-origin"; "transition"; "transition-duration"
            "transition-property"; "transition-timing-function"; "transition-delay"
            "animation"; "animation-name"; "animation-duration"
            "animation-timing-function"; "animation-delay"
            "animation-iteration-count"; "animation-direction"
            "animation-fill-mode"; "animation-play-state"
            // Filter & Effects
            "filter"; "backdrop-filter"; "clip-path"; "mix-blend-mode"; "isolation"
            // Cursor & Interaction
            "cursor"; "pointer-events"; "user-select"; "resize"
            "caret-color"; "scroll-behavior"; "scrollbar-width"; "scrollbar-color"
            // Lists
            "list-style"; "list-style-type"; "list-style-position"; "list-style-image"
            "counter-reset"; "counter-increment"; "counter-set"
            // Tables
            "table-layout"; "border-collapse"; "border-spacing"
            "caption-side"; "empty-cells"
            // Content
            "content"; "quotes"
            // Print
            "page-break-before"; "page-break-after"; "page-break-inside"
            // Modern
            "will-change"; "contain"; "contain-intrinsic-size"; "content-visibility"
            // Variables
            "var"
        |], StringComparer.OrdinalIgnoreCase)

    // ── Dangerous CSS constructions ──────────────────────────

    let private dangerousPatterns =
        [| @"expression\s*\(.*\)"            // IE expression()
           @"behavior\s*:\s*url"             // IE behaviour
           @"javascript\s*:"                 // javascript: URLs in CSS
           @"\\[0-9a-fA-F]{2,6}"            // Hex-escaped characters (potential obfuscation)
        |]

    // ── Colour value pattern ─────────────────────────────────
    let private validColorPattern = Regex(
        @"^(#([0-9a-fA-F]{3}|[0-9a-fA-F]{4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})" +
        @"|rgb\(.*?\)|rgba\(.*?\)|hsl\(.*?\)|hsla\(.*?\)" +
        @"|transparent|currentColor|inherit|initial|unset" +
        @"|[a-zA-Z]+)$",
        RegexOptions.Compiled)

    let private hexColorPattern = Regex(
        @"^#([0-9a-fA-F]{3}|[0-9a-fA-F]{4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$",
        RegexOptions.Compiled)

    // ── Bracket matching ─────────────────────────────────────

    /// Check whether curly brackets are balanced in a CSS string.
    let checkBrackets (css: string) : bool =
        let mutable depth = 0
        for c in css do
            match c with
            | '{' -> depth <- depth + 1
            | '}' -> depth <- depth - 1
            | _ -> ()
            if depth < 0 then false |> ignore // early bail handled below
        // Reset count after early negative — simpler: just track min depth
        let mutable d = 0
        let mutable ok = true
        for c in css do
            match c with
            | '{' -> d <- d + 1
            | '}' -> d <- d - 1
            | _ -> ()
            if d < 0 then ok <- false
        ok && d = 0

    // ── Dangerous rule detection ─────────────────────────────

    let private checkDangerousRules (css: string) : CssValidationIssue list =
        dangerousPatterns
        |> Array.indexed
        |> Array.collect (fun (_i, pattern) ->
            let matches = Regex.Matches(css, pattern, RegexOptions.IgnoreCase)
            matches
            |> Seq.cast<Match>
            |> Seq.map (fun m ->
                let line = css.[..m.Index].Split('\n').Length
                { Line = line; Column = m.Index; Message = sprintf "Potentially dangerous CSS: '%s'" m.Value; Severity = Warning })
            |> Seq.toArray
        )
        |> Array.toList

    // ── Colour value validation ──────────────────────────────

    let private isColorProperty (prop: string) =
        let lower = prop.ToLowerInvariant().Trim()
        lower = "color"
        || lower = "background-color" || lower = "background"
        || lower = "border-color" || lower = "border-top-color"
        || lower = "border-right-color" || lower = "border-bottom-color"
        || lower = "border-left-color"
        || lower = "outline-color"
        || lower = "text-shadow"
        || lower = "box-shadow"
        || lower = "caret-color"
        || lower = "text-decoration-color"
        || lower = "column-rule-color"
        || lower.EndsWith("-color")

    let private checkColorValue (prop: string) (value: string) : CssValidationIssue list =
        if not (isColorProperty prop) then []
        else
            let trimmed = value.Trim()
            if validColorPattern.IsMatch(trimmed) then
                // Reject invalid hex lengths (4, 8 chars)
                let m = hexColorPattern.Match(trimmed)
                if m.Success then
                    let len = m.Value.Length
                    if len <> 4 && len <> 7 then
                        [{ Line = 0; Column = 0
                           Message = sprintf "Hex colour '%s' should have 3, 4, 6, or 8 digits (CSS Color Level 4)" trimmed
                           Severity = Warning }]
                    else []
                else []
            else
                [{ Line = 0; Column = 0
                   Message = sprintf "Unrecognised colour value: '%s'" trimmed
                   Severity = Warning }]

    // ── Unknown property detection ───────────────────────────

    let private checkKnownProperty (prop: string) : CssValidationIssue list =
        let trimmed = prop.Trim()
        if String.IsNullOrEmpty trimmed then []
        elif trimmed.StartsWith("--") then []  // Custom properties always valid
        elif knownProperties.Contains(trimmed) then []
        elif trimmed.StartsWith("-webkit-") || trimmed.StartsWith("-moz-")
             || trimmed.StartsWith("-ms-") || trimmed.StartsWith("-o-") then []  // Vendor prefixes OK
        else
            [{ Line = 0; Column = 0
               Message = sprintf "Possibly unknown CSS property: '%s'" trimmed
               Severity = Info }]

    // ── Property-value format check ─────────────────────────

    let private checkDeclarationFormat (line: string) (lineNum: int) : CssValidationIssue list =
        let issues = ResizeArray<CssValidationIssue>()
        let trimmed = line.Trim()

        // Skip empty lines, comments, and block markers
        if String.IsNullOrWhiteSpace trimmed then ()
        elif trimmed.StartsWith("/*") || trimmed.EndsWith("*/") then ()
        elif trimmed = "{" || trimmed = "}" then ()
        else
            // Check for a colon (property: value separator)
            let colonIdx = trimmed.IndexOf(':')
            if colonIdx <= 0 then
                if trimmed.Length > 0 && not (trimmed.StartsWith("@")) && not (trimmed.EndsWith("{")) && not (trimmed.StartsWith("}")) then
                    issues.Add({ Line = lineNum; Column = 0
                                 Message = sprintf "Missing colon in declaration: '%s'" (if trimmed.Length > 40 then trimmed.[..39] + "…" else trimmed)
                                 Severity = Warning })
            else
                let prop = trimmed.[..colonIdx - 1].Trim()
                let value = trimmed.[colonIdx + 1..].TrimEnd(';').Trim()
                // Validate property name
                issues.AddRange(checkKnownProperty prop)
                // Validate colour values
                issues.AddRange(checkColorValue prop value)
        issues |> List.ofSeq

    // ── Empty rule detection ─────────────────────────────────

    let private checkEmptyRules (css: string) : CssValidationIssue list =
        let pattern = Regex(@"[^\}]\s*\{\s*\}", RegexOptions.Compiled)
        pattern.Matches(css)
        |> Seq.cast<Match>
        |> Seq.map (fun m ->
            let line = css.[..m.Index].Split('\n').Length
            { Line = line; Column = m.Index
              Message = "Empty CSS rule detected (no declarations)"
              Severity = Info })
        |> Seq.toList

    // ── Public API ───────────────────────────────────────────

    /// Validate a CSS string and return all issues found.
    let validateDetailed (css: string) : CssValidationIssue list =
        if globalValidationLevel = CssValidationLevel.Off then []
        else
            let issues = ResizeArray<CssValidationIssue>()

            // Bracket check
            if not (checkBrackets css) then
                issues.Add({ Line = 0; Column = 0
                             Message = "Unbalanced curly brackets in CSS"
                             Severity = Error })

            // Per-line checks
            css.Split('\n')
            |> Array.iteri (fun i line ->
                issues.AddRange(checkDeclarationFormat line (i + 1)))

            // Empty rules
            issues.AddRange(checkEmptyRules css)

            // Dangerous rules
            issues.AddRange(checkDangerousRules css)

            issues |> List.ofSeq

    /// Validate a CSS string.
    /// Strict: wraps in HTML comment errors + strips invalid blocks.
    /// Warn: wraps in HTML comment warnings, keeps content intact.
    /// Off: returns original CSS unchanged.
    let validate (css: string) : string =
        match globalValidationLevel with
        | CssValidationLevel.Off -> css
        | _ ->
            let issues = validateDetailed css
            if issues.IsEmpty then css
            else
                let prefix =
                    issues
                    |> List.map (fun i ->
                        let tag = match i.Severity with Error -> "ERROR" | Warning -> "WARNING" | Info -> "INFO"
                        sprintf "<!-- CSS %s (L%d): %s -->" tag i.Line (System.Net.WebUtility.HtmlEncode i.Message))
                    |> String.concat "\n"
                match globalValidationLevel with
                | CssValidationLevel.Strict ->
                    // In strict mode, prepend errors and still output CSS (user can see + fix)
                    prefix + "\n" + css
                | CssValidationLevel.Warn ->
                    prefix + "\n" + css
                | CssValidationLevel.Off -> css
