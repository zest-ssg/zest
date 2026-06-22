namespace Zest.Engine.Zss

open System
open System.Collections.Generic
open System.Text.RegularExpressions

// ============================================================
// ZSS Parser Core — Shared types, patterns, and helpers
// ============================================================

module ParserCore =

    type ParseMode =
        | BraceMode
        | IndentMode

    type ZssError = {
        Message: string
        Line: int
        Col:  int
        Context: string
    }
    with
        override this.ToString() =
            let lines = this.Context.Split('\n')
            let ctx =
                if this.Line > 0 && this.Line <= lines.Length then
                    let line = lines.[this.Line - 1]
                    let marker = String(' ', this.Col - 1) + "^"
                    sprintf "  %d | %s\n     | %s" this.Line line marker
                else ""
            sprintf "[ZSS ERROR] %d:%d\n  %s\n%s" this.Line this.Col this.Message ctx

    let errors = ResizeArray<ZssError>()
    let getErrors() = List.ofSeq errors
    let clearErrors() = errors.Clear()

    let stripComments (text: string) =
        // Remove // comments but preserve them in URLs (http://)
        let s = Regex.Replace(text, @"(?<!:)//[^\n]*", "", RegexOptions.Multiline)
        Regex.Replace(s, @"/\*.*?\*/", "", RegexOptions.Singleline)

    // ── Regex patterns ──────────────────────────────────────

    let varPattern    = Regex(@"^\s*\$([\w-]+)\s*:\s*(.+?)(\s+!default)?\s*;?\s*$", RegexOptions.Compiled)
    // F#-style: let name = value  or  let name: type = value
    let letPattern     = Regex(@"^\s*let\s+([\w-]+)\s*(?::\s*\w+)?\s*=\s*(.+?)\s*;?\s*$", RegexOptions.Compiled)
    // F#-style: let name = value in expression  (inline let)
    let letInPattern   = Regex(@"^\s*let\s+([\w-]+)\s*=\s*(.+?)\s+in\s+(.+)$", RegexOptions.Compiled)
    let mixinDefPat   = Regex(@"^@mixin\s+([\w-]+)\s*(?:\(([^)]*)\))?\s*\{?\s*$", RegexOptions.Compiled)
    let includePat    = Regex(@"^@include\s+([\w-]+)\s*(?:\(([^)]*)\))?\s*;?\s*$", RegexOptions.Compiled)
    let extendPat     = Regex(@"^@extend\s+(.+?)\s*;?\s*$", RegexOptions.Compiled)
    let applyPat     = Regex(@"^@apply\s+(.+?)\s*;?\s*$", RegexOptions.Compiled)
    let importPat    = Regex(@"^@import\s+[""'](.+?)[""']\s*;?\s*$", RegexOptions.Compiled)
    let usePat        = Regex(@"^@use\s+[""'](.+?)[""'](?:\s+as\s+(\w+))?\s*;?\s*$", RegexOptions.Compiled)
    let eachPat       = Regex(@"^@each\s+\$([\w-]+)(?:\s*,\s*\$([\w-]+))?\s+in\s+\(([^)]+)\)\s*\{?\s*$", RegexOptions.Compiled)
    let eachMapPat    = Regex(@"^@each\s+\$([\w-]+)\s*,\s*\$([\w-]+)\s+in\s+\$([\w-]+)\s*\{?\s*$", RegexOptions.Compiled)
    let forPat        = Regex(@"^@for\s+\$([\w-]+)\s+from\s+(\d+)\s+(through|to)\s+(\d+)\s*\{?\s*$", RegexOptions.Compiled)
    let ifPat         = Regex(@"^@if\s+(.+?)\s*\{?\s*$", RegexOptions.Compiled)
    let elsePat       = Regex(@"^@else(?:\s+if\s+(.+?))?\s*\{?\s*$", RegexOptions.Compiled)
    let exportPat     = Regex(@"^@export\s+\$([\w-]+)\s*;?\s*$", RegexOptions.Compiled)
    let optionPat     = Regex(@"^@option\s+([\w-]+)\s*:\s*(.+?)\s*;?\s*$", RegexOptions.Compiled)
    let warnPat       = Regex(@"^@warn\s+(.+?)\s*;?\s*$", RegexOptions.Compiled)
    let debugPat      = Regex(@"^@debug\s+(.+?)\s*;?\s*$", RegexOptions.Compiled)
    let rspBpMap      = dict ["sm","(min-width:640px)";"md","(min-width:768px)";"lg","(min-width:1024px)";"xl","(min-width:1280px)";"2xl","(min-width:1536px)"]

    // ── Variable extraction and resolution ──────────────────

    /// Resolve F#-style pipe operator: fn1(x) |> fn2(y) → fn2(fn1(x), y)
    /// Also handles: value |> fn2(y) → fn2(value, y)
    let resolvePipes (value: string) : string =
        if not (value.Contains("|>")) then value
        else
            let parts = value.Split([|"|>"|], StringSplitOptions.None) |> Array.map (fun s -> s.Trim())
            if parts.Length < 2 then value
            else
                let mutable acc = parts.[0]
                for i in 1..parts.Length-1 do
                    let next = parts.[i]
                    // Check if next part is a function call: fn(args)
                    let fnMatch = Regex.Match(next, @"^(\w+)\((.*)\)$")
                    if fnMatch.Success then
                        let fnName = fnMatch.Groups.[1].Value
                        let fnArgs = fnMatch.Groups.[2].Value
                        // If the function already has args, prepend the piped value
                        if String.IsNullOrEmpty fnArgs then
                            acc <- sprintf "%s(%s)" fnName acc
                        else
                            acc <- sprintf "%s(%s, %s)" fnName acc fnArgs
                    else
                        // Not a function call — just concatenate
                        acc <- next + " " + acc
                acc

    /// Resolve variable references in a value string.
    /// Supports both $name (SCSS-style) and bare name (F#-style) references.
    /// For bare names, only resolves if the name is a known variable AND is not
    /// a CSS keyword/property name (to avoid corrupting CSS values).
    let resolveVarRefs (rawVal: string) (d: IDictionary<string, string>) : string =
        // First resolve $name references (SCSS-style) — always safe
        let v1 = Regex.Replace(rawVal, @"\$([\w-]+)", fun mm ->
            match d.TryGetValue(mm.Groups.[1].Value) with true, vv -> vv | _ -> mm.Value)
        // Then resolve bare variable names (F#-style) — but only if:
        // 1. The name is a known variable in the dictionary
        // 2. The name is NOT a CSS property name or keyword
        // 3. The name is not preceded by a hyphen (part of a property name like margin-top)
        let cssKeywords = set [
            "none"; "auto"; "inherit"; "initial"; "unset"; "transparent"; "currentColor"
            "block"; "inline"; "flex"; "grid"; "table"; "contents"; "list-item"
            "row"; "column"; "row-reverse"; "column-reverse"; "wrap"; "nowrap"
            "center"; "left"; "right"; "top"; "bottom"; "stretch"; "baseline"
            "space-between"; "space-around"; "space-evenly"; "flex-start"; "flex-end"
            "absolute"; "relative"; "fixed"; "sticky"; "static"
            "hidden"; "visible"; "scroll"; "clip"
            "bold"; "normal"; "italic"; "underline"; "overline"; "line-through"
            "uppercase"; "lowercase"; "capitalize"; "full-width"
            "solid"; "dashed"; "dotted"; "double"; "groove"; "ridge"; "inset"; "outset"
            "cover"; "contain"; "fill"; "scale-down"
            "ease"; "ease-in"; "ease-out"; "ease-in-out"; "linear"; "step-start"; "step-end"
            "forwards"; "backwards"; "both"; "infinite"; "alternate"; "reverse"
            "border-box"; "content-box"; "padding-box"; "margin-box"
            "multiply"; "screen"; "overlay"; "darken"; "lighten"; "color-dodge"; "color-burn"
            "hard-light"; "soft-light"; "difference"; "exclusion"; "hue"; "saturation"
            "color"; "luminosity"; "normal"
            "repeat"; "repeat-x"; "repeat-y"; "no-repeat"; "space"; "round"
            "local"; "fixed"; "content-box"; "border-box"; "padding-box"
            "ellipsis"; "clip"; "break-word"; "keep-all"
            "pre"; "pre-wrap"; "pre-line"; "nowrap"
            "disc"; "circle"; "square"; "decimal"; "decimal-leading-zero"
            "lower-roman"; "upper-roman"; "lower-alpha"; "upper-alpha"
            "ltr"; "rtl"; "horizontal-tb"; "vertical-rl"; "vertical-lr"
            "flat"; "preserve-3d"
            "open-quote"; "close-quote"; "no-open-quote"; "no-close-quote"
            "show"; "hide"
            "collapse"; "separate"
            "avoid"; "always"; "auto"; "all"
            "text"; "all"; "none"; "punctuation"
            "strict"; "loose"; "anywhere"
            "balance"; "auto"; "pretty";
            "isolate"; "isolate-override"; "plaintext"; "mixed"; "bidi-override";
            "sideways"; "sideways-lr"; "sideways-rl"; "upright"; "use-glyph-orientation"
            "optimizeSpeed"; "optimizeQuality"; "crisp-edges"; "geometricPrecision"
            "pixelated"; "smooth"; "high-quality"; "crispEdges"
        ]
        Regex.Replace(v1, @"(?<![a-zA-Z0-9#-])([\w-]+)(?![a-zA-Z0-9])", fun mm ->
            let name = mm.Groups.[1].Value
            if cssKeywords.Contains name then mm.Value
            else
                match d.TryGetValue(name) with
                | true, vv -> vv
                | _ -> mm.Value)

    let extractVars (lines: string seq) =
        let d = Dictionary<string, string>()
        for line in lines do
            // Match $name: value (SCSS-style)
            let m = varPattern.Match(line)
            // Match let name = value (F#-style)
            let lm = letPattern.Match(line)
            if m.Success then
                let isDefault = m.Groups.[3].Success
                let rawVal = m.Groups.[2].Value.Trim()
                let v = Evaluator.resolveValue rawVal d
                if isDefault then
                    if not (d.ContainsKey(m.Groups.[1].Value)) then
                        d.[m.Groups.[1].Value] <- v
                else
                    d.[m.Groups.[1].Value] <- v
            elif lm.Success then
                let name = lm.Groups.[1].Value
                let rawVal = lm.Groups.[2].Value.Trim()
                let v = Evaluator.resolveValue rawVal d
                d.[name] <- v
        d

    let resolveVars (value: string) (vars: IDictionary<string, string>) =
        Regex.Replace(value, @"\$([\w-]+)", fun m ->
            match vars.TryGetValue(m.Groups.[1].Value) with true, v -> v | _ -> m.Value)

    /// Parse a declaration line. Supports both `prop: value` and `prop = value` (F#/C# style).
    let parseDecl (line: string) (lineNum: int) (vars: IDictionary<string, string>) : Declaration option =
        let t = line.Trim().TrimEnd(';').Trim()
        if String.IsNullOrEmpty t || t.StartsWith("//") then None
        else
            // Try colon first, then equals (F#/C# style)
            // But be careful with colons inside values: url(http://...), calc(...), etc.
            let colonIdx = t.IndexOf(':')
            let eqIdx    = t.IndexOf('=')

            // Find the separator index, preferring the first separator that looks like
            // a property-value separator (not inside a function call)
            let findSepIdx() =
                let mutable bestIdx = -1
                let mutable depth = 0
                let mutable i = 0
                while i < t.Length do
                    let c = t.[i]
                    if c = '(' then depth <- depth + 1
                    elif c = ')' then depth <- max 0 (depth - 1)
                    elif depth = 0 then
                        if c = ':' && i > 0 then
                            // Check if this colon is a property separator
                            // It should be preceded by a word character (property name)
                            // and not be part of a pseudo-selector like :hover
                            let before = if i > 0 then t.[i-1] else ' '
                            if Char.IsLetterOrDigit before || before = '-' || before = '_' then
                                if bestIdx < 0 then bestIdx <- i
                        elif c = '=' && i > 0 && t.[i-1] <> '!' && t.[i-1] <> '<' && t.[i-1] <> '>' then
                            if bestIdx < 0 then bestIdx <- i
                    i <- i + 1
                bestIdx

            let sepIdx =
                if colonIdx > 0 && (eqIdx <= 0 || colonIdx < eqIdx) then colonIdx
                elif eqIdx > 0 then eqIdx
                else findSepIdx()

            if sepIdx <= 0 then None
            else
                let prop = t.Substring(0, sepIdx).Trim()
                let rest = t.Substring(sepIdx + 1).Trim()
                let important = rest.EndsWith("!important")
                let rawVal = (if important then rest.Substring(0, rest.Length - 10) else rest).Trim()
                // Pre-resolve $name references first to ensure they're replaced
                // (in case the caller passed a vars dict that doesn't contain them)
                let preResolved = resolveVars rawVal vars
                // Let resolveValue handle the rest (pipes, math, color functions, builtins, shorthands)
                let value = Evaluator.resolveValue preResolved vars
                // Handle nested property shorthand: margin.top → margin-top
                let resolvedProp =
                    if prop.Contains(".") then
                        prop.Replace(".", "-")
                    else prop
                let pos = { Line = lineNum; Col = sepIdx + 1 }
                Some { Property = Evaluator.ShorthandMap.resolve resolvedProp; Value = value; Important = important; Pos = pos }

    // ── Mode detection and indentation ──────────────────────

    let detectMode (lines: string array) : ParseMode =
        let hasBraces = lines |> Array.exists (fun l -> l.Contains("{"))
        if hasBraces then BraceMode else IndentMode

    let getIndent (line: string) : int =
        let mutable n = 0
        let mutable counting = true
        for c in line do
            if counting then
                if c = ' ' then n <- n + 1
                elif c = '\t' then n <- n + 4
                else counting <- false
        n

    // ── Shared mixin parameter parser ───────────────────────

    let parseMixinParams (m: Match) =
        if m.Groups.[2].Success then
            m.Groups.[2].Value.Split(',')
            |> Array.map (fun p ->
                let parts = p.Trim().TrimStart('$').Split([|':'|], 2)
                let pName = parts.[0].Trim()
                let pDefault = if parts.Length > 1 then Some(parts.[1].Trim()) else None
                (pName, pDefault))
            |> Array.filter (fun (n, _) -> n.Length > 0)
            |> Array.toList
        else []

    // ── Shared include args parser ─────────────────────────

    let parseIncludeArgs (m: Match) (vars: IDictionary<string, string>) =
        if m.Groups.[2].Success then
            m.Groups.[2].Value.Split(',')
            |> Array.map (fun a -> resolveVars (a.Trim().TrimStart('$')) vars)
            |> Array.toList
        else []
