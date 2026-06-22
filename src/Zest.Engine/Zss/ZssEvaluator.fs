namespace Zest.Engine.Zss

open System
open System.Collections.Generic
open System.Text.RegularExpressions

// ============================================================
// ZSS Evaluator — Math expressions, color functions, built-ins
// ============================================================

module Evaluator =

    // ── Shorthand property map ──────────────────────────────

    module ShorthandMap =
        let private map =
            dict [
                // Spacing
                "m",   "margin";       "mt", "margin-top";     "mr", "margin-right"
                "mb",  "margin-bottom";"ml", "margin-left";    "mx", "margin-inline"
                "my",  "margin-block"
                "p",   "padding";      "pt", "padding-top";    "pr", "padding-right"
                "pb",  "padding-bottom";"pl","padding-left";   "px", "padding-inline"
                "py",  "padding-block"
                // Background
                "bg",  "background";   "bgc","background-color";"bgi","background-image"
                "bgr", "background-repeat";"bgp","background-position";"bgs","background-size"
                // Color / Text
                "c",   "color";        "fs", "font-size";      "fw", "font-weight"
                "ff",  "font-family";  "f",  "font";           "lh", "line-height"
                "ta",  "text-align";   "td", "text-decoration";"ts", "text-shadow"
                "tt",  "text-transform";"ls","letter-spacing"; "ws", "word-spacing"
                "wsb", "word-break";   "ww", "word-wrap"
                // Layout
                "d",   "display";      "pos","position";       "t",  "top"
                "r",   "right";        "b",  "bottom";         "l",  "left"
                "z",   "z-index";      "o",  "opacity";        "ov", "overflow"
                "ovx", "overflow-x";   "ovy","overflow-y";     "cur","cursor"
                // Sizing
                "w",   "width";        "h",  "height";         "mw", "max-width"
                "mh",  "max-height";   "mnw","min-width";      "mnh","min-height"
                // Border
                "bd",  "border";       "bdc","border-color";   "bds","border-style"
                "bdw", "border-width"; "bdr","border-radius";  "bdrs","border-radius"
                "bdt", "border-top";  "bdrr","border-right";   "bdb","border-bottom"; "bdl","border-left"
                "bl",  "border-left"; "br",  "border-right";   "bt",  "border-top";    "bb",  "border-bottom"
                "bdtw","border-top-width";"bdrw","border-right-width"
                "bdbw","border-bottom-width";"bdlw","border-left-width"
                "bdts","border-top-style";"bdrss","border-right-style"
                "bdbs","border-bottom-style";"bdls","border-left-style"
                "bdtt","border-top-color";"bdrc","border-right-color"
                "bdbc","border-bottom-color";"bdlc","border-left-color"
                // Effects
                "bxsh","box-shadow";   "bxz","box-sizing";     "tr", "transition"
                "trf", "transform";    "trfo","transform-origin";"anim","animation"
                "anim-name","animation-name";"anim-duration","animation-duration"
                "anim-delay","animation-delay";"anim-timing-function","animation-timing-function"
                "anim-iteration-count","animation-iteration-count"
                "anim-direction","animation-direction";"anim-fill-mode","animation-fill-mode"
                "anim-play-state","animation-play-state"
                "bdra","border-radius"
                // Misc
                "us",  "user-select";  "pe", "pointer-events"; "cl", "clear"
                "fl",  "float";        "vis","visibility";     "va", "vertical-align"
                "lst", "list-style";   "gap","gap";            "ac", "align-content"
                "ai",  "align-items";  "as", "align-self";     "jc", "justify-content"
                "ji",  "justify-items";"js", "justify-self";   "fo", "flex-flow"
                "fb",  "flex-basis";   "fg", "flex-grow";      "fsh","flex-shrink"
                "gtc", "grid-template-columns";"gtr","grid-template-rows"
                "gta", "grid-template-areas";"ga", "grid-area"
                "gc",  "grid-column";  "gr", "grid-row"
                "gar", "grid-auto-rows";"gac","grid-auto-columns";"gaf","grid-auto-flow"
                "co",  "content";      "rs", "resize";         "app","appearance"
                "out", "outline";      "asp","aspect-ratio"
                "wb",  "writing-mode"; "bdcl","border-collapse";"bdsp","border-spacing"
                "cps", "caption-side";  "ets","empty-cells"
            ]

        let resolve (prop: string) : string =
            match map.TryGetValue prop with
            | true, v -> v
            | _       -> prop

    // ── Value unit shorthands ───────────────────────────────

    module ValueShorthand =
        let private unitPattern = Regex(@"^(-?[\d.]+)(r|p|v|vh|vw|em|s|ms)$", RegexOptions.Compiled)
        let private unitMap = dict [ "r","rem"; "p","%"; "v","vh"; "s","s"; "ms","ms" ]

        let resolveToken (token: string) : string =
            let m = unitPattern.Match(token)
            if m.Success then
                let num  = m.Groups.[1].Value
                let unit = m.Groups.[2].Value
                match unitMap.TryGetValue unit with
                | true, expanded -> num + expanded
                | _              -> num + unit
            else token

        let resolve (value: string) : string =
            value.Split(' ')
            |> Array.map resolveToken
            |> String.concat " "

    // ── Color functions ─────────────────────────────────────

    module ColorFunctions =
        let private hexToRgb (hex: string) : (int * int * int) option =
            let h = hex.TrimStart('#')
            if h.Length = 6 then
                try Some(Convert.ToInt32(h.[0..1], 16),
                         Convert.ToInt32(h.[2..3], 16),
                         Convert.ToInt32(h.[4..5], 16))
                with _ -> None
            elif h.Length = 3 then
                try Some(Convert.ToInt32(string h.[0] + string h.[0], 16),
                         Convert.ToInt32(string h.[1] + string h.[1], 16),
                         Convert.ToInt32(string h.[2] + string h.[2], 16))
                with _ -> None
            elif h.Length = 8 then  // #RRGGBBAA
                try Some(Convert.ToInt32(h.[0..1], 16),
                         Convert.ToInt32(h.[2..3], 16),
                         Convert.ToInt32(h.[4..5], 16))
                with _ -> None
            else None

        let private clamp v = max 0 (min 255 v)
        let private toHex r g b = sprintf "#%02x%02x%02x" (clamp r) (clamp g) (clamp b)

        let lighten (hex: string) (pct: int) =
            match hexToRgb hex with
            | None -> hex
            | Some(r, g, b) -> let d = pct * 255 / 100 in toHex (r+d) (g+d) (b+d)

        let darken (hex: string) (pct: int) =
            match hexToRgb hex with
            | None -> hex
            | Some(r, g, b) -> let d = pct * 255 / 100 in toHex (r-d) (g-d) (b-d)

        let alpha (hex: string) (a: float) =
            match hexToRgb hex with
            | None -> hex
            | Some(r, g, b) -> sprintf "rgba(%d,%d,%d,%.2f)" r g b a

        let mix (hex1: string) (hex2: string) (pct: int) =
            match hexToRgb hex1, hexToRgb hex2 with
            | Some(r1,g1,b1), Some(r2,g2,b2) ->
                let w = float pct / 100.0
                toHex (int (float r1*w + float r2*(1.0-w)))
                      (int (float g1*w + float g2*(1.0-w)))
                      (int (float b1*w + float b2*(1.0-w)))
            | _ -> hex1

        // ── New color functions ──

        let complement (hex: string) =
            match hexToRgb hex with
            | None -> hex
            | Some(r, g, b) -> toHex (255-r) (255-g) (255-b)

        let grayscale (hex: string) =
            match hexToRgb hex with
            | None -> hex
            | Some(r, g, b) ->
                let gray = int (float r * 0.299 + float g * 0.587 + float b * 0.114)
                toHex gray gray gray

        let invert (hex: string) = complement hex

        let saturate (hex: string) (pct: int) =
            match hexToRgb hex with
            | None -> hex
            | Some(r, g, b) ->
                let avg = (r + g + b) / 3
                let factor = float pct / 100.0
                let s v = int (float avg + (float v - float avg) * (1.0 + factor))
                toHex (s r) (s g) (s b)

        let desaturate (hex: string) (pct: int) =
            match hexToRgb hex with
            | None -> hex
            | Some(r, g, b) ->
                let avg = (r + g + b) / 3
                let factor = 1.0 - float pct / 100.0
                let s v = int (float avg + (float v - float avg) * factor)
                toHex (s r) (s g) (s b)

        let adjustHue (hex: string) (deg: float) =
            match hexToRgb hex with
            | None -> hex
            | Some(r, g, b) ->
                // Convert RGB to HSL, adjust hue, convert back
                let rf = float r / 255.0
                let gf = float g / 255.0
                let bf = float b / 255.0
                let mx = max rf (max gf bf)
                let mn = min rf (min gf bf)
                let l = (mx + mn) / 2.0
                let s =
                    if mx = mn then 0.0
                    elif l < 0.5 then (mx - mn) / (mx + mn)
                    else (mx - mn) / (2.0 - mx - mn)
                let h =
                    if mx = mn then 0.0
                    elif mx = rf then 60.0 * (gf - bf) / (mx - mn)
                    elif mx = gf then 60.0 * (bf - rf) / (mx - mn) + 120.0
                    else 60.0 * (rf - gf) / (mx - mn) + 240.0
                let newH = (h + deg) % 360.0
                let newH = if newH < 0.0 then newH + 360.0 else newH
                // HSL to RGB
                let c = (1.0 - abs (2.0 * l - 1.0)) * s
                let x = c * (1.0 - abs ((newH / 60.0) % 2.0 - 1.0))
                let m = l - c / 2.0
                let (r', g', b') =
                    if newH < 60.0 then (c, x, 0.0)
                    elif newH < 120.0 then (x, c, 0.0)
                    elif newH < 180.0 then (0.0, c, x)
                    elif newH < 240.0 then (0.0, x, c)
                    elif newH < 300.0 then (x, 0.0, c)
                    else (c, 0.0, x)
                toHex (int ((r' + m) * 255.0)) (int ((g' + m) * 255.0)) (int ((b' + m) * 255.0))

        let tint (hex: string) (pct: int) = mix "#ffffff" hex pct
        let shade (hex: string) (pct: int) = mix "#000000" hex pct

        // ── Additional color functions ──

        let transparentize (hex: string) (a: float) =
            match hexToRgb hex with
            | None -> hex
            | Some(r, g, b) -> sprintf "rgba(%d,%d,%d,%.2f)" r g b (1.0 - a)

        let rgba (r: int) (g: int) (b: int) (a: float) =
            sprintf "rgba(%d,%d,%d,%.2f)" r g b a

        let rgb (r: int) (g: int) (b: int) =
            sprintf "rgb(%d,%d,%d)" r g b

        let hsl (h: float) (s: float) (l: float) =
            sprintf "hsl(%.0f,%.0f%%,%.0f%%)" h s l

        let hsla (h: float) (s: float) (l: float) (a: float) =
            sprintf "hsla(%.0f,%.0f%%,%.0f%%,%.2f)" h s l a

        let scaleColor (hex: string) (satPct: int) (lightPct: int) =
            match hexToRgb hex with
            | None -> hex
            | Some(r, g, b) ->
                let scaled = saturate hex satPct
                match hexToRgb scaled with
                | None -> hex
                | Some(r2, g2, b2) ->
                    if lightPct > 0 then lighten scaled lightPct
                    else darken scaled (-lightPct)

        // ── Regex-based function resolution ──

        let private fnPatterns =
            [|
                Regex(@"lighten\(\s*(#[0-9a-fA-F]+|\w+)\s*,\s*(\d+)%\s*\)", RegexOptions.Compiled),
                    (fun (m: Match) -> lighten m.Groups.[1].Value (int m.Groups.[2].Value))
                Regex(@"darken\(\s*(#[0-9a-fA-F]+|\w+)\s*,\s*(\d+)%\s*\)", RegexOptions.Compiled),
                    (fun (m: Match) -> darken m.Groups.[1].Value (int m.Groups.[2].Value))
                Regex(@"alpha\(\s*(#[0-9a-fA-F]+|\w+)\s*,\s*([\d.]+)\s*\)", RegexOptions.Compiled),
                    (fun (m: Match) -> alpha m.Groups.[1].Value (float m.Groups.[2].Value))
                Regex(@"transparentize\(\s*(#[0-9a-fA-F]+|\w+)\s*,\s*([\d.]+)\s*\)", RegexOptions.Compiled),
                    (fun (m: Match) -> transparentize m.Groups.[1].Value (float m.Groups.[2].Value))
                Regex(@"mix\(\s*(#[0-9a-fA-F]+|\w+)\s*,\s*(#[0-9a-fA-F]+|\w+)\s*,\s*(\d+)%\s*\)", RegexOptions.Compiled),
                    (fun (m: Match) -> mix m.Groups.[1].Value m.Groups.[2].Value (int m.Groups.[3].Value))
                Regex(@"complement\(\s*(#[0-9a-fA-F]+|\w+)\s*\)", RegexOptions.Compiled),
                    (fun (m: Match) -> complement m.Groups.[1].Value)
                Regex(@"grayscale\(\s*(#[0-9a-fA-F]+|\w+)\s*\)", RegexOptions.Compiled),
                    (fun (m: Match) -> grayscale m.Groups.[1].Value)
                Regex(@"invert\(\s*(#[0-9a-fA-F]+|\w+)\s*\)", RegexOptions.Compiled),
                    (fun (m: Match) -> invert m.Groups.[1].Value)
                Regex(@"saturate\(\s*(#[0-9a-fA-F]+|\w+)\s*,\s*(\d+)%\s*\)", RegexOptions.Compiled),
                    (fun (m: Match) -> saturate m.Groups.[1].Value (int m.Groups.[2].Value))
                Regex(@"desaturate\(\s*(#[0-9a-fA-F]+|\w+)\s*,\s*(\d+)%\s*\)", RegexOptions.Compiled),
                    (fun (m: Match) -> desaturate m.Groups.[1].Value (int m.Groups.[2].Value))
                Regex(@"adjust-hue\(\s*(#[0-9a-fA-F]+|\w+)\s*,\s*(-?[\d.]+)deg\s*\)", RegexOptions.Compiled),
                    (fun (m: Match) -> adjustHue m.Groups.[1].Value (float m.Groups.[2].Value))
                Regex(@"tint\(\s*(#[0-9a-fA-F]+|\w+)\s*,\s*(\d+)%\s*\)", RegexOptions.Compiled),
                    (fun (m: Match) -> tint m.Groups.[1].Value (int m.Groups.[2].Value))
                Regex(@"shade\(\s*(#[0-9a-fA-F]+|\w+)\s*,\s*(\d+)%\s*\)", RegexOptions.Compiled),
                    (fun (m: Match) -> shade m.Groups.[1].Value (int m.Groups.[2].Value))
                Regex(@"rgba\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*([\d.]+)\s*\)", RegexOptions.Compiled),
                    (fun (m: Match) -> rgba (int m.Groups.[1].Value) (int m.Groups.[2].Value) (int m.Groups.[3].Value) (float m.Groups.[4].Value))
                Regex(@"rgb\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)", RegexOptions.Compiled),
                    (fun (m: Match) -> rgb (int m.Groups.[1].Value) (int m.Groups.[2].Value) (int m.Groups.[3].Value))
                Regex(@"hsla\(\s*([\d.]+)\s*,\s*([\d.]+)%\s*,\s*([\d.]+)%\s*,\s*([\d.]+)\s*\)", RegexOptions.Compiled),
                    (fun (m: Match) -> hsla (float m.Groups.[1].Value) (float m.Groups.[2].Value) (float m.Groups.[3].Value) (float m.Groups.[4].Value))
                Regex(@"hsl\(\s*([\d.]+)\s*,\s*([\d.]+)%\s*,\s*([\d.]+)%\s*\)", RegexOptions.Compiled),
                    (fun (m: Match) -> hsl (float m.Groups.[1].Value) (float m.Groups.[2].Value) (float m.Groups.[3].Value))
            |]

        let resolve (value: string) : string =
            let mutable result = value
            let mutable changed = true
            let mutable iterations = 0
            let maxIterations = 20  // safety limit to prevent infinite loops
            while changed && iterations < maxIterations do
                changed <- false
                iterations <- iterations + 1
                for (pat, fn) in fnPatterns do
                    let m = pat.Match(result)
                    if m.Success then
                        result <- result.Substring(0, m.Index) + fn m + result.Substring(m.Index + m.Length)
                        changed <- true
            result

    // ── Math expression evaluator ───────────────────────────

    module MathExpr =
        /// Token types for math expression
        type private Token =
            | Num of float * string  // value * original unit
            | Op of char
            | LParen | RParen
            | Var of string

        let private tokenize (s: string) : Token list =
            let tokens = ResizeArray<Token>()
            let i = ref 0
            while i.Value < s.Length do
                let c = s.[i.Value]
                if Char.IsWhiteSpace c then incr i
                elif c = '(' then tokens.Add LParen; incr i
                elif c = ')' then tokens.Add RParen; incr i
                elif c = '+' || c = '-' || c = '*' || c = '/' then
                    // Distinguish unary minus from binary minus
                    if c = '-' && (tokens.Count = 0 || match tokens.[tokens.Count-1] with Op _ | LParen -> true | _ -> false) then
                        // Part of a number
                        let sb = Text.StringBuilder("-")
                        incr i
                        while i.Value < s.Length && (Char.IsDigit s.[i.Value] || s.[i.Value] = '.') do
                            sb.Append(s.[i.Value]) |> ignore
                            incr i
                        let numStr = sb.ToString()
                        let unitStart = i.Value
                        while i.Value < s.Length && (Char.IsLetter s.[i.Value] || s.[i.Value] = '%') do incr i
                        let unit = s.[unitStart..i.Value-1]
                        tokens.Add(Num(float numStr, unit))
                    else
                        tokens.Add(Op c); incr i
                elif Char.IsDigit c || c = '.' then
                    let sb = Text.StringBuilder()
                    while i.Value < s.Length && (Char.IsDigit s.[i.Value] || s.[i.Value] = '.') do
                        sb.Append(s.[i.Value]) |> ignore
                        incr i
                    let numStr = sb.ToString()
                    let unitStart = i.Value
                    while i.Value < s.Length && (Char.IsLetter s.[i.Value] || s.[i.Value] = '%') do incr i
                    let unit = s.[unitStart..i.Value-1]
                    tokens.Add(Num(float numStr, unit))
                elif c = '$' then
                    let sb = Text.StringBuilder("$")
                    incr i
                    while i.Value < s.Length && (Char.IsLetterOrDigit s.[i.Value] || s.[i.Value] = '-' || s.[i.Value] = '_') do
                        sb.Append(s.[i.Value]) |> ignore
                        incr i
                    tokens.Add(Var(sb.ToString()))
                else incr i  // skip unknown chars
            Seq.toList tokens

        /// Evaluate a math expression, preserving units.
        /// Rules: if operands have the same unit, result keeps that unit.
        /// If one operand is unitless, result uses the other's unit.
        /// For * and /, only the first operand's unit is kept (CSS-like behavior).
        let eval (expr: string) (vars: IDictionary<string, string>) : string =
            // First resolve variables
            let resolved = Regex.Replace(expr, @"\$([\w-]+)", fun m ->
                match vars.TryGetValue(m.Groups.[1].Value) with
                | true, v -> v | _ -> m.Value)

            // Check if this looks like a math expression (contains +, -, *, / outside of function calls)
            let hasMathOps =
                resolved.ToCharArray()
                |> Array.exists (fun c -> c = '+' || c = '*' || c = '/')
                || (resolved.Contains("-") && not (resolved.StartsWith("-") && not (resolved.Substring(1).Contains("-"))))

            // Simple heuristic: only evaluate if there are math operators and numbers
            // Match numbers with optional units (e.g., 16px, 0.5r, 100%)
            let mathPattern = Regex(@"[\d.]+\w*\s*[+\-*/]\s*[\d.]+\w*", RegexOptions.Compiled)
            if not (mathPattern.IsMatch resolved) then resolved
            else
                let tokens = tokenize resolved
                // Simple recursive descent parser
                let pos = ref 0
                let peek() = if pos.Value < tokens.Length then Some tokens.[pos.Value] else None
                let advance() = let t = tokens.[pos.Value] in incr pos; t

                let addNum (n1, u1) (n2, u2) = (n1 + n2, if u1 <> "" then u1 else u2)
                let subNum (n1, u1) (n2, u2) = (n1 - n2, if u1 <> "" then u1 else u2)
                let mulNum (n1, u1) (n2, _) = (n1 * n2, u1)
                let divNum (n1, u1) (n2, _) = if n2 <> 0.0 then (n1 / n2, u1) else (0.0, u1)

                let rec parseExpr() =
                    let left = parseTerm()
                    match peek() with
                    | Some(Op '+') -> advance() |> ignore; let r = parseExpr() in addNum left r
                    | Some(Op '-') -> advance() |> ignore; let r = parseExpr() in subNum left r
                    | _ -> left

                and parseTerm() =
                    let left = parseFactor()
                    match peek() with
                    | Some(Op '*') -> advance() |> ignore; let r = parseTerm() in mulNum left r
                    | Some(Op '/') -> advance() |> ignore; let r = parseTerm() in divNum left r
                    | _ -> left

                and parseFactor() =
                    match peek() with
                    | Some LParen ->
                        advance() |> ignore
                        let e = parseExpr()
                        match peek() with Some RParen -> advance() |> ignore | _ -> ()
                        e
                    | Some(Num(n, u)) -> advance() |> ignore; (n, u)
                    | Some(Var v) ->
                        advance() |> ignore
                        let vName = v.TrimStart('$')
                        match vars.TryGetValue(vName) with
                        | true, vv ->
                            let numPart = Regex.Match(vv, @"^-?[\d.]+")
                            let unitPart = Regex.Match(vv, @"[^\d.-]+$")
                            if numPart.Success then
                                (float numPart.Value, if unitPart.Success then unitPart.Value else "")
                            else (0.0, vv)
                        | _ -> (0.0, "")
                    | _ -> (0.0, "")

                try
                    let (value, unit) = parseExpr()
                    let numStr =
                        if value = floor value then string (int64 value)
                        else value.ToString("0.######")
                    numStr + unit
                with _ -> resolved

    /// Resolve bare let-bound variable names (F#-style) in values.
    /// Only resolves names that exist in the vars dictionary (non-$-prefixed).
    let resolveBareVars (value: string) (vars: IDictionary<string, string>) : string =
        // Resolve bare variable references in a value string.
        // Strategy: use regex to find word-boundary tokens and replace only known variables.
        // This avoids replacing CSS keywords and preserves the original formatting.
        let cssKeywords = set ["none"; "auto"; "inherit"; "initial"; "unset"; "normal";
                               "bold"; "italic"; "left"; "right"; "center"; "top"; "bottom";
                               "solid"; "dashed"; "dotted"; "double"; "transparent";
                               "block"; "inline"; "flex"; "grid"; "hidden"; "visible";
                               "static"; "relative"; "absolute"; "fixed"; "sticky";
                               "nowrap"; "wrap"; "baseline"; "stretch"; "cover"; "contain";
                               "row"; "column"; "pointer"; "default"; "separate"; "collapse";
                               "scroll"; "clip"; "ellipsis"; "break-word"; "keep-all";
                               "pre"; "pre-wrap"; "pre-line"; "uppercase"; "lowercase"; "capitalize";
                               "disc"; "circle"; "square"; "decimal"; "inside"; "outside";
                               "underline"; "overline"; "line-through"; "blink";
                               "justify"; "space-between"; "space-around"; "space-evenly";
                               "flex-start"; "flex-end"; "start"; "end";
                               "border-box"; "content-box"; "padding-box"; "margin-box";
                               "repeat"; "repeat-x"; "repeat-y"; "no-repeat"; "round"; "space";
                               "local"; "fixed"; "ease"; "ease-in"; "ease-out"; "ease-in-out"; "linear";
                               "infinite"; "alternate"; "reverse"; "forwards"; "backwards"; "both";
                               "multiply"; "screen"; "overlay"; "darken"; "lighten";
                               "isolate"; "mixed"; "plaintext"; "ltr"; "rtl";
                               "flat"; "preserve-3d"; "open"; "closed"]
        // Match word-boundary tokens that could be variable names
        // Only match if not preceded by $ (already a $-style reference)
        // and not preceded by - (part of a CSS property like margin-top)
        Regex.Replace(value, @"(?<![.\$a-zA-Z0-9_-])([a-zA-Z_][\w]*)(?![a-zA-Z0-9_])", fun m ->
            let w = m.Groups.[1].Value
            if cssKeywords.Contains(w.ToLower()) then m.Value
            else
                match vars.TryGetValue(w) with
                | true, vv -> vv
                | _ -> m.Value)

    // ── Built-in utility functions ──────────────────────────

    module BuiltinFunctions =
        let private fnMapPat = Regex(@"(\w+)\(\s*([^)]*)\s*\)", RegexOptions.Compiled)

        let private unitPat = Regex(@"^(-?[\d.]+)(\w+|%)?$", RegexOptions.Compiled)

        let resolve (value: string) (vars: IDictionary<string, string>) : string =
            let mutable result = value
            let mutable changed = true
            let mutable iterations = 0
            let maxIterations = 20  // safety limit to prevent infinite loops
            while changed && iterations < maxIterations do
                changed <- false
                iterations <- iterations + 1
                let m = fnMapPat.Match(result)
                if m.Success then
                    let fn = m.Groups.[1].Value
                    let arg = m.Groups.[2].Value.Trim()
                    let replacement =
                        match fn with
                        | "unit" ->
                            let mv = unitPat.Match(arg)
                            if mv.Success && mv.Groups.[2].Success then mv.Groups.[2].Value
                            else ""
                        | "unitless" ->
                            let mv = unitPat.Match(arg)
                            if mv.Success && not mv.Groups.[2].Success then "true" else "false"
                        | "percentage" ->
                            let v = float arg * 100.0
                            sprintf "%g%%" v
                        | "str-length" -> arg.Length.ToString()
                        | "to-upper" -> arg.ToUpper()
                        | "to-lower" -> arg.ToLower()
                        | "quote" -> sprintf "\"%s\"" arg
                        | "unquote" -> arg.Trim('"', '\'')
                        | "list-length" ->
                            arg.Split([|' '|], StringSplitOptions.RemoveEmptyEntries).Length.ToString()
                        | "list-nth" ->
                            let parts = arg.Split(',') |> Array.map (fun s -> s.Trim())
                            if parts.Length >= 2 then
                                let list = parts.[0].Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
                                let n = int parts.[1]
                                if n > 0 && n <= list.Length then list.[n-1]
                                elif n < 0 && abs n <= list.Length then list.[list.Length + n]
                                else ""
                            else ""
                        | "type-of" ->
                            let mv = unitPat.Match(arg)
                            if mv.Success then
                                if mv.Groups.[2].Success then "number"
                                else "number"  // unitless number
                            elif arg.StartsWith("$") then "variable"
                            elif arg.StartsWith("\"") || arg.StartsWith("'") then "string"
                            else "string"
                        | "abs" ->
                            let mv = unitPat.Match(arg)
                            if mv.Success then
                                let n = float mv.Groups.[1].Value
                                let u = if mv.Groups.[2].Success then mv.Groups.[2].Value else ""
                                sprintf "%g%s" (abs n) u
                            else arg
                        | "min" ->
                            let parts = arg.Split(',') |> Array.map (fun s -> s.Trim())
                            if parts.Length >= 2 then
                                let nums = parts |> Array.choose (fun p ->
                                    let mv = unitPat.Match(p)
                                    if mv.Success then Some(float mv.Groups.[1].Value) else None)
                                if nums.Length > 0 then string (Array.min nums) else arg
                            else arg
                        | "max" ->
                            let parts = arg.Split(',') |> Array.map (fun s -> s.Trim())
                            if parts.Length >= 2 then
                                let nums = parts |> Array.choose (fun p ->
                                    let mv = unitPat.Match(p)
                                    if mv.Success then Some(float mv.Groups.[1].Value) else None)
                                if nums.Length > 0 then string (Array.max nums) else arg
                            else arg
                        | _ -> null
                    if replacement <> null then
                        result <- result.Substring(0, m.Index) + replacement + result.Substring(m.Index + m.Length)
                        changed <- true
            result

    // ── Full value resolution pipeline ───────────────────────
    //
    // Design: A single, well-ordered pipeline that handles all value transformations.
    // Order matters:
    //   1. Resolve pipes (|>) - restructure the expression first
    //   2. Resolve $name references (SCSS-style) - replace $primary with its value
    //   3. Resolve bare name references (F#-style let bindings) - replace primary with its value
    //   4. Evaluate math expressions - evaluate arithmetic
    //   5. Resolve color functions (alpha, lighten, etc.) - transform colors
    //   6. Resolve built-in functions (unit, etc.) - transform values
    //   7. Expand unit shorthands (2r → 2rem)
    //   8. Repeat 2-7 a few times to handle chained references
    //
    // This ensures that no matter where resolveValue is called from, the result is consistent.

    /// Resolve F#-style pipe operator: fn1(x) |> fn2(y) → fn2(fn1(x), y)
    let private resolvePipes (v: string) : string =
        if not (v.Contains("|>")) then v
        else
            let parts = v.Split([|"|>"|], StringSplitOptions.None) |> Array.map (fun s -> s.Trim())
            if parts.Length < 2 then v
            else
                let mutable acc = parts.[0]
                for i in 1..parts.Length-1 do
                    let next = parts.[i]
                    let fnMatch = Regex.Match(next, @"^(\w+)\((.*)\)$")
                    if fnMatch.Success then
                        let fnName = fnMatch.Groups.[1].Value
                        let fnArgs = fnMatch.Groups.[2].Value
                        if String.IsNullOrEmpty fnArgs then
                            acc <- sprintf "%s(%s)" fnName acc
                        else
                            acc <- sprintf "%s(%s, %s)" fnName acc fnArgs
                    else
                        acc <- next + " " + acc
                acc

    /// Resolve $name references (SCSS-style) - e.g. $primary → #3b82f6
    let private resolveDollarRefs (v: string) (vars: IDictionary<string, string>) : string =
        if not (v.Contains('$')) then v
        else
            Regex.Replace(v, @"\$([\w-]+)", fun m ->
                match vars.TryGetValue(m.Groups.[1].Value) with
                | true, vv -> vv
                | _ -> m.Value)

    /// Single pass of value transformations (variables → math → color → builtin → shorthand)
    let private resolvePass (v: string) (vars: IDictionary<string, string>) : string =
        v
        |> fun x -> resolveDollarRefs x vars
        |> fun x -> resolveBareVars x vars
        |> fun x -> MathExpr.eval x vars
        |> resolvePipes
        |> ColorFunctions.resolve
        |> fun x -> BuiltinFunctions.resolve x vars
        |> ValueShorthand.resolve

    /// Resolve a complete value: variables → math → color functions → builtins → unit shorthands
    let resolveValue (value: string) (vars: IDictionary<string, string>) : string =
        // First pass: handle pipes first, then full resolution
        let initial = resolvePipes value
        // Multiple passes to handle chained variable references
        // (e.g. $a → $b → #fff, or bare var that contains another bare var)
        let mutable result = initial
        for _ in 0..3 do
            let next = resolvePass result vars
            if next = result then result <- next
            else result <- next
        result
