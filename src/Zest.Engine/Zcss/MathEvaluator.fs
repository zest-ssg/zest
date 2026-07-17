namespace Zest.Engine.Zcss

open System
open System.Collections.Generic
open System.Text.RegularExpressions

// ============================================================
// Math Evaluator — Math expression tokenizer + recursive descent parser
// ============================================================

module MathEvaluator =
    /// Token types for math expression
    type private Token =
        | Num of float * string  // value * original unit
        | Op of char
        | LParen | RParen
        | Var of string

    // Cached regex — created once, reused across all calls
    let private mathPattern = Regex(@"[\d.]+\w*\s*[+\-*/]\s*[\d.]+\w*", RegexOptions.Compiled)
    let private dollarVarPattern = Regex(@"\$([\w-]+)", RegexOptions.Compiled)

    let private tokenize (s: string) : Token list =
        let tokens = ResizeArray<Token>()
        let i = ref 0
        while i.Value < s.Length do
            let c = s.[i.Value]
            if Char.IsWhiteSpace c then incr i
            elif c = '(' then tokens.Add LParen; incr i
            elif c = ')' then tokens.Add RParen; incr i
            elif c = '+' || c = '-' || c = '*' || c = '/' then
                // A leading '-' is only a unary minus when it is immediately followed by a
                // digit or dot. Otherwise it is a binary operator (e.g. `16px - 10px`) or a
                // stray hyphen inside a word (e.g. `system-ui`), which must NOT be parsed as a
                // negative number — doing so would call `float "-"` and throw a FormatException.
                let isUnaryMinus =
                    c = '-'
                    && (tokens.Count = 0 || match tokens.[tokens.Count-1] with Op _ | LParen -> true | _ -> false)
                    && (i.Value + 1 < s.Length && (Char.IsDigit s.[i.Value+1] || s.[i.Value+1] = '.'))
                if isUnaryMinus then
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
        let resolved = dollarVarPattern.Replace(expr, fun m ->
            match vars.TryGetValue(m.Groups.[1].Value) with
            | true, v -> v | _ -> m.Value)

        // Guard: a value is only evaluated as a math expression when it is composed
        // solely of numbers, operators, parentheses, variables ($), units (letters/%)
        // and whitespace. Values containing other characters — notably commas in CSS
        // `font` shorthands such as `16px/1.65 system-ui, -apple-system` — are passed
        // through unchanged, so we never corrupt real CSS or trip the tokenizer on a
        // stray hyphen.
        let nonMathCharPattern = Regex(@"[^0-9.+*/()$%a-zA-Z\s-]", RegexOptions.Compiled)
        let looksLikeMath = mathPattern.IsMatch resolved && not (nonMathCharPattern.IsMatch resolved)

        // Simple heuristic: only evaluate if there are math operators and numbers
        // Match numbers with optional units (e.g., 16px, 0.5r, 100%)
        if not looksLikeMath then resolved
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
