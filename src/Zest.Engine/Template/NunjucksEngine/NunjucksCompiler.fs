namespace Zest.Engine.Template

open System
open System.Text

// NunjucksCompiler.fs
//
// Compiles Nunjucks expression text into a small tree (CExpr) so a template
// loop does not re-parse the same expression on every iteration.
//
// Invariant: the compiler mirrors the evaluator's precedence exactly
// (pipe → or → and → not → compare → additive → multiplicative → atom).

module internal NunjucksCompiler =

    /// Scan for the rightmost top-level occurrence of any operator in `ops`.
    /// `ops` must be ordered longest-first so multi-char ops win. Word
    /// operators (and/or/not/in) require alphanumeric word boundaries.
    let findTopOp (text: string) (ops: string list) : (int * string) option =
        let n = text.Length
        let isWord (op: string) = op.Length > 0 && Char.IsLetter op.[0]
        let boundaryOk (i: int) (len: int) =
            let before = i = 0 || not (Char.IsLetterOrDigit text.[i-1] || text.[i-1] = '_')
            let after = i + len >= n || not (Char.IsLetterOrDigit text.[i+len] || text.[i+len] = '_')
            before && after
        let mutable inS = false
        let mutable inD = false
        let mutable depth = 0
        let mutable best : (int * string) option = None
        let mutable i = 0
        while i < n do
            let c = text.[i]
            if inS then (if c = '\'' then inS <- false); i <- i + 1
            elif inD then (if c = '"' then inD <- false); i <- i + 1
            elif c = '\'' then inS <- true; i <- i + 1
            elif c = '"' then inD <- true; i <- i + 1
            elif c = '(' || c = '[' then depth <- depth + 1; i <- i + 1
            elif c = ')' || c = ']' then depth <- depth - 1; i <- i + 1
            elif depth = 0 then
                let matched =
                    ops |> List.tryFind (fun op ->
                        i + op.Length <= n
                        && text.Substring(i, op.Length) = op
                        && (not (isWord op) || boundaryOk i op.Length))
                match matched with
                | Some op -> best <- Some(i, op); i <- i + op.Length
                | None -> i <- i + 1
            else i <- i + 1
        best

    /// Split a comma-separated argument list at top level, respecting quotes
    /// and nested parentheses/brackets. Used by loop.cycle / loop.changed.
    let splitTopLevelArgs (s: string) : string list =
        let res = ResizeArray<string>()
        let sb = StringBuilder()
        let mutable inS = false
        let mutable inD = false
        let mutable depth = 0
        let mutable i = 0
        let n = s.Length
        while i < n do
            let c = s.[i]
            if inS then (if c = '\'' then inS <- false); sb.Append(c) |> ignore; i <- i + 1
            elif inD then (if c = '"' then inD <- false); sb.Append(c) |> ignore; i <- i + 1
            elif c = '\'' then inS <- true; sb.Append(c) |> ignore; i <- i + 1
            elif c = '"' then inD <- true; sb.Append(c) |> ignore; i <- i + 1
            elif c = '(' || c = '[' then depth <- depth + 1; sb.Append(c) |> ignore; i <- i + 1
            elif c = ')' || c = ']' then depth <- depth - 1; sb.Append(c) |> ignore; i <- i + 1
            elif c = ',' && depth = 0 then (res.Add(sb.ToString().Trim()); sb.Clear() |> ignore; i <- i + 1)
            else sb.Append(c) |> ignore; i <- i + 1
        if sb.Length > 0 then res.Add(sb.ToString().Trim())
        List.ofSeq res

    /// Split a filter chain on top-level `|` pipes, respecting quotes and
    /// nested parentheses/brackets so that a `|` inside a filter argument
    /// (e.g. `date(x | default('y'))`) is NOT treated as a chain separator.
    /// Also skips `||` (logical or).
    let private splitTopLevelPipes (s: string) : string list =
        let res = ResizeArray<string>()
        let sb = StringBuilder()
        let mutable inS = false
        let mutable inD = false
        let mutable depth = 0
        let mutable i = 0
        let n = s.Length
        while i < n do
            let c = s.[i]
            if inS then (if c = '\'' then inS <- false); sb.Append(c) |> ignore; i <- i + 1
            elif inD then (if c = '"' then inD <- false); sb.Append(c) |> ignore; i <- i + 1
            elif c = '\'' then inS <- true; sb.Append(c) |> ignore; i <- i + 1
            elif c = '"' then inD <- true; sb.Append(c) |> ignore; i <- i + 1
            elif c = '(' || c = '[' then depth <- depth + 1; sb.Append(c) |> ignore; i <- i + 1
            elif c = ')' || c = ']' then depth <- depth - 1; sb.Append(c) |> ignore; i <- i + 1
            elif c = '|' && depth = 0 then
                // Skip `||` (logical or) — it stays part of the current segment.
                if i + 1 < n && s.[i+1] = '|' then
                    sb.Append(c) |> ignore; sb.Append(s.[i+1]) |> ignore; i <- i + 2
                else
                    res.Add(sb.ToString().Trim()); sb.Clear() |> ignore; i <- i + 1
            else sb.Append(c) |> ignore; i <- i + 1
        if sb.Length > 0 then res.Add(sb.ToString().Trim())
        List.ofSeq res

    /// Split an inline conditional `then if cond else otherwise` at the
    /// leftmost top-level `if` and its matching `else`. Returns None when no
    /// complete conditional is present, so the text falls through to ordinary
    /// expression parsing. Leftmost-splitting makes the conditional
    /// right-associative, matching Jinja/Nunjucks.
    let private splitInlineIf (text: string) : (string * string * string) option =
        let n = text.Length
        let isWordAt (k: int) (w: string) =
            k >= 0 && k + w.Length <= n
            && String.CompareOrdinal(text, k, w, 0, w.Length) = 0
            && (k = 0 || not (Char.IsLetterOrDigit text.[k-1] || text.[k-1] = '_'))
            && (k + w.Length >= n || not (Char.IsLetterOrDigit text.[k+w.Length] || text.[k+w.Length] = '_'))
        let mutable inS = false
        let mutable inD = false
        let mutable depth = 0
        let mutable ifIdx = -1
        let mutable elseIdx = -1
        let mutable i = 0
        while i < n && elseIdx < 0 do
            let c = text.[i]
            if inS then (if c = '\'' then inS <- false); i <- i + 1
            elif inD then (if c = '"' then inD <- false); i <- i + 1
            elif c = '\'' then inS <- true; i <- i + 1
            elif c = '"' then inD <- true; i <- i + 1
            elif c = '(' || c = '[' then depth <- depth + 1; i <- i + 1
            elif c = ')' || c = ']' then depth <- depth - 1; i <- i + 1
            elif depth = 0 then
                if ifIdx < 0 && isWordAt i "if" then ifIdx <- i; i <- i + 2
                elif ifIdx >= 0 && isWordAt i "else" then elseIdx <- i; i <- i + 4
                else i <- i + 1
            else i <- i + 1
        if ifIdx >= 0 && elseIdx > ifIdx then
            let thenE = text.[..ifIdx-1].Trim()
            let condE = text.[ifIdx+2..elseIdx-1].Trim()
            let elseE = text.[elseIdx+4..].Trim()
            if thenE <> "" && condE <> "" && elseE <> "" then Some(thenE, condE, elseE) else None
        else None

    // ── Expression precompilation ────────────────────────────
    // Templates render once per page, but the same expression string inside a
    // loop evaluates once per iteration. Re-parsing it every time (balance
    // scan, operator scans, string splits) is the dominant CPU cost of
    // template evaluation. Each distinct expression is therefore compiled ONCE
    // into a small tree keyed by its exact text; every subsequent evaluation
    // walks the tree directly, so `{{ item.price + 10 }}` inside a 1000-item
    // loop parses once and evaluates 1000 times.

    type CExpr =
        | CLit of obj
        | CPath of string
        | CNotE of CExpr
        | CUnary of string * CExpr            // "+" | "-" prefix sign
        | CBin of string * CExpr * CExpr
        | CIf of CExpr * CExpr * CExpr         // then, condition, else (inline if)
        | CRange of CExpr list
        | CCall of string * CExpr list
        | CParen of CExpr
        | CPipeE of CExpr * (string * CExpr list) list

    let private tryLiteral (s: string) =
        let t = s.Trim()
        if t.Length >= 2 && ((t.StartsWith("\"") && t.EndsWith("\"")) || (t.StartsWith("'") && t.EndsWith("'"))) then
            Some(box(t.Substring(1, t.Length-2)))
        elif t = "true" then Some(box true)
        elif t = "false" then Some(box false)
        elif t = "null" || t = "none" || t = "undefined" then Some null
        else
            match Int32.TryParse t with
            | true, i -> Some(box i)
            | _ -> match Double.TryParse t with | true, f -> Some(box f) | _ -> None

    let rec compileExpr (exprText: string) : CExpr =
        let t = exprText.Trim()
        if t = "" then CLit ""
        else compileCond t

    and compileCond (text: string) : CExpr =
        match splitInlineIf text with
        | Some(thenE, condE, elseE) -> CIf(compileCond thenE, compileCond condE, compileCond elseE)
        | None -> compilePipe text

    and compilePipe (text: string) : CExpr =
        let parts = splitTopLevelPipes text
        if parts.Length <= 1 then compileOr parts.Head
        else
            let baseExpr = compileOr parts.Head
            let chain =
                parts.Tail
                |> List.map (fun fp ->
                    let ppi = fp.IndexOf('(')
                    let fname, fargsText =
                        if ppi >= 0 then fp.[..ppi-1], fp.[ppi+1..fp.Length-2].Trim()
                        else fp, ""
                    let fargs =
                        if fargsText = "" then []
                        else splitTopLevelArgs fargsText |> List.map compileExpr
                    fname, fargs)
            CPipeE(baseExpr, chain)

    and compileOr (text: string) : CExpr =
        match findTopOp text [ "or" ] with
        | Some(i, op) -> CBin("or", compileOr (text.[..i-1]), compileOr (text.[i+op.Length..]))
        | None -> compileAnd text

    and compileAnd (text: string) : CExpr =
        match findTopOp text [ "and" ] with
        | Some(i, op) -> CBin("and", compileAnd (text.[..i-1]), compileAnd (text.[i+op.Length..]))
        | None -> compileNot text

    and compileNot (text: string) : CExpr =
        let t = text.Trim()
        if t.StartsWith("not ") then CNotE(compileNot (t.[4..]))
        else compileCompare t

    and compileCompare (text: string) : CExpr =
        let t = text.Trim()
        // `is` / `is not` tests (x is defined, x is not empty). The test name is
        // kept as a literal string so evaluation can dispatch on it; `is not`
        // flips the operator so the evaluator negates the test result.
        match findTopOp t [ " is not "; " is " ] with
        | Some(i, op) when t.[..i-1].Trim() <> "" && t.[i+op.Length..].Trim() <> "" ->
            let negated = op = " is not "
            CBin((if negated then "is not" else "is"),
                 compileAdd (t.[..i-1]),
                 CLit(box (t.[i+op.Length..].Trim())))
        | _ ->
            match findTopOp t [ "=="; "!="; ">="; "<="; ">"; "<"; " not in "; " in " ] with
            | Some(i, op) ->
                CBin(op.Trim(), compileAdd (t.[..i-1]), compileAdd (t.[i+op.Length..]))
            | None -> compileAdd t

    and compileAdd (text: string) : CExpr =
        match findTopOp text [ "+"; "-"; "~" ] with
        | Some(i, op) when text.[..i-1].Trim() <> "" ->
            CBin(op, compileAdd (text.[..i-1]), compileMul (text.[i+op.Length..]))
        | _ -> compileMul text

    and compileMul (text: string) : CExpr =
        match findTopOp text [ "**"; "*"; "/"; "%" ] with
        | Some(i, op) when text.[..i-1].Trim() <> "" ->
            CBin(op, compileMul (text.[..i-1]), compileUnary (text.[i+op.Length..]))
        | _ -> compileUnary text

    /// Unary numeric sign (`-5`, `+3`, `-x`). Nunjucks has no separate unary
    /// operator level, so the sign binds tighter than `*` and `/` but looser
    /// than atoms; the prefix marker keeps `-x` distinct from binary `a - x`.
    and compileUnary (text: string) : CExpr =
        let t = text.Trim()
        if t.StartsWith("-") then CUnary("-", compileUnary (t.[1..].Trim()))
        elif t.StartsWith("+") then CUnary("+", compileUnary (t.[1..].Trim()))
        else compileAtom t

    and compileAtom (text: string) : CExpr =
        let t = text.Trim()
        if t = "" then CLit ""
        elif t.StartsWith("range(") && t.EndsWith(")") then
            CRange (splitTopLevelArgs (t.[6..t.Length-2].Trim()) |> List.map compileExpr)
        elif t.StartsWith("loop.cycle(") && t.EndsWith(")") then
            CCall("loop.cycle", splitTopLevelArgs (t.[11..t.Length-2].Trim()) |> List.map compileExpr)
        elif t.StartsWith("loop.changed(") && t.EndsWith(")") then
            CCall("loop.changed", splitTopLevelArgs (t.[13..t.Length-2].Trim()) |> List.map compileExpr)
        elif t.StartsWith("(") && t.EndsWith(")") then CParen (compileExpr (t.[1..t.Length-2]))
        else
            match tryLiteral t with
            | Some v -> CLit v
            | None -> CPath t
