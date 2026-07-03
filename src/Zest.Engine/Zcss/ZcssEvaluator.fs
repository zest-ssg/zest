namespace Zest.Engine.Zcss

open System
open System.Collections.Generic
open System.Text.RegularExpressions

// ============================================================
// ZCSS Evaluator — Value resolution pipeline
// ============================================================

module Evaluator =

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

    // ── Cached regex and data for resolveBareVars ──
    let private bareVarRe = Regex(@"(?<![.\$a-zA-Z0-9_-])([a-zA-Z_][\w]*)(?![a-zA-Z0-9_-])", RegexOptions.Compiled)
    let private cssKeywords = set ["none"; "auto"; "inherit"; "initial"; "unset"; "normal";
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

    let resolveBareVars (value: string) (vars: IDictionary<string, string>) : string =
        bareVarRe.Replace(value, fun m ->
            let w = m.Groups.[1].Value
            if cssKeywords.Contains(w.ToLower()) then m.Value
            else
                match vars.TryGetValue(w) with
                | true, vv -> vv
                | _ -> m.Value)

    // ── Full value resolution pipeline ───────────────────────
    //
    // Design: A single, well-ordered pipeline that handles all value transformations.
    // Order matters:
    //   1. Resolve $name references (SCSS-style) - replace $primary with its value
    //   2. Resolve bare name references (F#-style let bindings) - replace primary with its value
    //   3. Resolve let ... in expressions (F#-style inline binding)
    //   4. Resolve if ... then ... else expressions (F#-style conditional)
    //   5. Evaluate math expressions - evaluate arithmetic
    //   6. Resolve pipes (|>) - function composition
    //   7. Resolve color functions (alpha, lighten, etc.) - transform colors
    //   8. Resolve built-in functions (unit, etc.) - transform values
    //   9. Expand unit shorthands (2r → 2rem)
    //  10. Repeat 1-9 a few times to handle chained references

    // ── Cached regex patterns (module-level, created once) ──
    let private dollarRefRe  = Regex(@"\$([\w-]+)", RegexOptions.Compiled)
    let private pipeFnRe     = Regex(@"^(\w+)\((.*)\)$", RegexOptions.Compiled)

    /// Resolve $name references (SCSS-style) - e.g. $primary → #3b82f6
    let private resolveDollarRefs (v: string) (vars: IDictionary<string, string>) : string =
        if not (v.Contains('$')) then v
        else
            dollarRefRe.Replace(v, fun m ->
                match vars.TryGetValue(m.Groups.[1].Value) with
                | true, vv -> vv
                | _ -> m.Value)

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
                    let fnMatch = pipeFnRe.Match(next)
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

    // ── Mutually recursive core: resolvePass ↔ resolveLetIn ↔ resolveIfExpr ↔ evalBool ──

    /// Single pass of value transformations.
    let rec private resolvePass (v: string) (vars: IDictionary<string, string>) : string =
        v
        |> fun x -> resolveDollarRefs x vars
        |> fun x -> resolveBareVars x vars
        |> fun x -> resolveLetIn x vars
        |> fun x -> resolveIfExpr x vars
        |> fun x -> MathEvaluator.eval x vars
        |> resolvePipes
        |> ColorPipeline.resolve
        |> fun x -> BuiltinFunctions.resolve x vars
        |> ValueShorthand.resolve

    /// Resolve F#-style inline let expression: let x = value in expr
    and private resolveLetIn (v: string) (vars: IDictionary<string, string>) : string =
        let m = ParserCore.letInPattern.Match(v)
        if not m.Success then v
        else
            let varName = m.Groups.[1].Value
            let varValue = m.Groups.[2].Value
            let bodyExpr = m.Groups.[3].Value
            let resolvedVal = resolvePass varValue vars
            let substituted = bodyExpr.Replace(varName, resolvedVal)
            resolvePass substituted vars

    /// Evaluate a boolean condition (returns true/false).  Used by if/then/else in values and @if directive.
    and evalBool (cond: string) (vars: IDictionary<string, string>) : bool =
        let resolved = resolveDollarRefs cond vars |> fun x -> resolveBareVars x vars
        let t = resolved.Trim().Trim('"', '\'')
        // Try comparison operators first
        let cm = ParserCore.compOpPattern.Match(t)
        if cm.Success then
            let left  = cm.Groups.[1].Value.Trim().Trim('"', '\'')
            let op    = cm.Groups.[2].Value
            let right = cm.Groups.[3].Value.Trim().Trim('"', '\'')
            let isNumeric (s: string) = Regex.IsMatch(s, @"^[\d.]+(px|rem|em|%|vh|vw|r|p|v|s|ms)?$")
            if isNumeric left && isNumeric right then
                let leftNum  = Regex.Replace(left,  @"[a-z%]+", "") |> float
                let rightNum = Regex.Replace(right, @"[a-z%]+", "") |> float
                match op with
                | "==" -> leftNum =  rightNum
                | "!=" | "<>" -> leftNum <> rightNum
                | ">=" -> leftNum >= rightNum
                | "<=" -> leftNum <= rightNum
                | ">"  -> leftNum >  rightNum
                | "<"  -> leftNum <  rightNum
                | _    -> false
            else
                match op with
                | "==" -> left = right
                | "!=" | "<>" -> left <> right
                | _    -> false
        else
            let nm = ParserCore.notOpPattern.Match(t)
            if nm.Success then
                not (evalBool (nm.Groups.[2].Value) vars)
            else
                let lm = ParserCore.logicOpPattern.Match(t)
                if lm.Success then
                    let parts = ParserCore.logicOpPattern.Split(t) |> Array.toList
                    let mutable result = evalBool (parts.[0].Trim()) vars
                    let mutable i = 1
                    while i < parts.Length - 1 do
                        let op = parts.[i].Trim()
                        let next = evalBool (parts.[i+1].Trim()) vars
                        match op with
                        | "&&" -> result <- result && next
                        | "||" -> result <- result || next
                        | _ -> ()
                        i <- i + 2
                    result
                else
                    t <> "" && t <> "false" && t <> "0" && t <> "null" && t <> "none"

    /// Resolve F#-style inline if expression: if cond then true_val else false_val
    and private resolveIfExpr (v: string) (vars: IDictionary<string, string>) : string =
        let m = ParserCore.ifExprPattern.Match(v)
        if not m.Success then v
        else
            let cond     = m.Groups.[1].Value.Trim()
            let thenVal  = m.Groups.[2].Value.Trim()
            let elseVal  = m.Groups.[3].Value.Trim()
            if evalBool cond vars then
                resolvePass thenVal vars
            else
                resolvePass elseVal vars

    // ── Public API ──────────────────────────────────────────

    /// Resolve a complete value: variables → let/in → if/then/else → math → color → builtin → shorthand
    let resolveValue (value: string) (vars: IDictionary<string, string>) : string =
        let initial = resolvePipes value
        let mutable result = initial
        for _ in 0..3 do
            let next = resolvePass result vars
            if next = result then result <- next
            else result <- next
        result

    /// Post-process a (property, value) pair to fix up values that the parser
    /// accepts as valid CSS keyword tokens but that some CSS properties
    /// reject. For example `letter-spacing: none` is invalid CSS — the
    /// correct keyword is `normal`.
    let normalizePropertyValue (prop: string) (value: string) : string =
        match prop.Trim().ToLowerInvariant() with
        | "letter-spacing" ->
            let v = value.Trim().ToLowerInvariant()
            if v = "none" then "normal" else value
        | "text-decoration" ->
            value
        | _ -> value
