namespace Zest.Engine.Template

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Globalization
open System.IO
open System.Reflection
open System.Text
open System.Text.RegularExpressions

// ============================================================
// NunjucksEngine — Nunjucks-compatible template engine
// ============================================================
// Nunjucks-compatible (.njk) template engine for Zest.
// Syntax aims to be highly compatible with Nunjucks/Jinja2,
// but this is a Zest-specific implementation with Zest API
// integrations. Use .njk extension for Nunjucks templates.
//
// Supports: variables, filters, tags (if/for/block/extends/include/
//           set/raw/filter/macro/import/from), template inheritance,
//           auto-escaping (safe filter to bypass), custom filters.
// ============================================================

module private NunjucksImpl =

    // ── Custom filter registry (extensible by Zest engine) ──
    // ConcurrentDictionary: filter registration may race with rendering under
    // multi-threaded web servers, so a plain Dictionary is unsafe here.
    let customFilters = ConcurrentDictionary<string, FilterFn>()

    // ── Safe string wrapper (bypasses auto-escaping) ──
    type SafeString(s: string) =
        member _.Value = s
        override _.ToString() = s

    // ── Reflection cache for POCO property access ──
    let private propCache = ConcurrentDictionary<string, PropertyInfo>()

    // ── Precompiled regexes (avoid recompiling on every filter call) ──
    let private reTitle   = Regex(@"\b\w", RegexOptions.Compiled)
    let private reTags    = Regex(@"<[^>]+>", RegexOptions.Compiled)
    let private reSlug    = Regex(@"[^a-z0-9]+", RegexOptions.Compiled)
    let private reIndent  = Regex(@"^", RegexOptions.Compiled ||| RegexOptions.Multiline)
    let private reUrl     = Regex(@"(https?://[^\s<>""']+)", RegexOptions.Compiled)

    // ── Token types ────────────────────────────────────────
    // Each token carries its 1-based source line so runtime/syntax errors can
    // be reported with a meaningful location instead of line 0.
    type Token =
        | TextToken of string * int      // literal text, source line
        | VarToken  of string * int      // {{ expr }}, source line
        | TagToken  of string * string list * int  // {% tag args %}, source line
        | CmtToken  of string * int      // {# comment #}, source line

    // ── Tokenizer (idempotent, cached) ─────────────────────
    // ConcurrentDictionary: many threads may populate the cache for the same
    // uncached template simultaneously; a plain Dictionary can corrupt/throw.
    let tokenCache = ConcurrentDictionary<string, struct(DateTime * Token list)>()

    let tokenize (text: string) : Token list =
        let tokens = ResizeArray<Token>()
        let sb = StringBuilder()
        let len = text.Length
        let mutable i = 0
        let mutable line = 1
        let mutable stripLeftNext = false   // strip leading WS of the next text token (set by `-}}` / `-%}`)

        // Helpers that track the current source line as characters are consumed.
        let addChar (ch: char) =
            if ch = '\n' then line <- line + 1
            sb.Append(ch) |> ignore
        let addStr (s: string) =
            for ch in s do if ch = '\n' then line <- line + 1
            sb.Append(s) |> ignore

        // Remove trailing whitespace from the last emitted text token (for `{{-` / `{%-`).
        let removeTrailingWs () =
            if tokens.Count > 0 then
                match tokens.[tokens.Count - 1] with
                | TextToken(t, l) ->
                    if t.Length > 0 && (t |> Seq.forall Char.IsWhiteSpace) then tokens.RemoveAt(tokens.Count - 1)
                    else
                        let trimmed = t.TrimEnd()
                        if trimmed <> t then tokens.[tokens.Count - 1] <- TextToken(trimmed, l)
                | _ -> ()

        let flush () =
            if sb.Length > 0 then
                let t = if stripLeftNext then (stripLeftNext <- false; sb.ToString().TrimStart()) else sb.ToString()
                tokens.Add(TextToken(t, line)); sb.Clear() |> ignore

        while i < len do
            if i + 2 < len then
                let c = text.[i]
                if c = '{' && text.[i+1] = '#' then               // {# comment #} (supports nesting)
                    flush()
                    let cl = line
                    let mutable j = i + 2
                    let mutable depth = 1
                    let mutable endPos = -1
                    while j + 1 < len && endPos < 0 do
                        if text.[j] = '{' && text.[j+1] = '#' then depth <- depth + 1; j <- j + 2
                        elif text.[j] = '#' && text.[j+1] = '}' then
                            depth <- depth - 1
                            if depth = 0 then endPos <- j else j <- j + 2
                        else j <- j + 1
                    if endPos < 0 then addStr (text.Substring(i)); i <- len
                    else
                        let commentText = text.Substring(i+2, endPos - (i+2))
                        // advance the line counter over any newlines inside the comment
                        for ch in commentText do if ch = '\n' then line <- line + 1
                        tokens.Add(CmtToken(commentText, cl)); i <- endPos + 2
                elif c = '{' && text.[i+1] = '{' then              // {{ var }} / {{- var -}}
                    flush()
                    let cl = line
                    let mutable lstrip = false
                    let mutable ci = i + 2
                    if ci < len && text.[ci] = '-' then lstrip <- true; ci <- ci + 1
                    let e = text.IndexOf("}}", ci)
                    if e < 0 then addStr (text.Substring(i)); i <- len
                    else
                        let mutable rstrip = false
                        let innerEnd = if e >= 1 && text.[e-1] = '-' then (rstrip <- true; e - 1) else e
                        let expr = if innerEnd > ci then text.Substring(ci, innerEnd - ci).Trim() else ""
                        if lstrip then removeTrailingWs ()
                        tokens.Add(VarToken(expr, cl))
                        if rstrip then stripLeftNext <- true
                        i <- e + 2
                elif c = '{' && text.[i+1] = '%' then              // {% tag %} / {%- tag -%}
                    flush()
                    let cl = line
                    let mutable lstrip = false
                    let mutable ci = i + 2
                    if ci < len && text.[ci] = '-' then lstrip <- true; ci <- ci + 1
                    let e = text.IndexOf("%}", ci)
                    if e < 0 then addStr (text.Substring(i)); i <- len
                    else
                        let mutable rstrip = false
                        let innerEnd = if e >= 1 && text.[e-1] = '-' then (rstrip <- true; e - 1) else e
                        let raw = if innerEnd > ci then text.Substring(ci, innerEnd - ci).Trim() else ""
                        let parts = raw.Split([|' ';'\n';'\t';'\r'|], StringSplitOptions.RemoveEmptyEntries)
                        let tag = if parts.Length > 0 then parts.[0] else ""
                        let args = if parts.Length > 1 then parts.[1..] |> Array.toList else []
                        // Left-strip before emitting this tag's token.
                        if lstrip then removeTrailingWs ()
                        // raw tag: capture everything until {% endraw %} as literal text
                        if tag = "raw" then
                            let rawEnd = text.IndexOf("{% endraw %}", e+2)
                            if rawEnd >= e+2 then
                                let rawContent = text.Substring(e+2, rawEnd - (e+2))
                                tokens.Add(TextToken(rawContent, cl))
                                i <- rawEnd + "{% endraw %}".Length
                            else
                                tokens.Add(TagToken(tag, args, cl))
                                i <- e + 2
                        else
                            tokens.Add(TagToken(tag, args, cl)); i <- e + 2
                        if rstrip then stripLeftNext <- true
                else addChar c; i <- i + 1
            else addChar text.[i]; i <- i + 1
        flush()
        Seq.toList tokens

    let tokenizeFile (path: string) =
        let mtime = File.GetLastWriteTimeUtc(path)
        match tokenCache.TryGetValue path with
        | true, struct(cm, _) when cm = mtime -> tokenCache.[path]
        | _ ->
            let text = File.ReadAllText(path)
            let tokens = tokenize text
            tokenCache.[path] <- struct(mtime, tokens)
            struct(mtime, tokens)

    // ── Runtime helpers ─────────────────────────────────────
    let toStr (v: obj) =
        match v with null -> "" | :? string as s -> s | _ -> v.ToString()
    let toBool (v: obj) =
        match v with null -> false | :? bool as b -> b | :? string as s -> s <> ""
                       | :? int as i -> i <> 0 | :? int64 as i -> i <> 0L
                       | :? double as d -> d <> 0.0 | _ -> true
    let propGet (v: obj) (key: string) =
        match v with
        | null -> null
        | :? IDictionary<string, obj> as d -> match d.TryGetValue key with true, v -> v | _ -> null
        | :? IDictionary<string, string> as d -> match d.TryGetValue key with true, v -> box v | _ -> null
        | :? IDictionary<string, int> as d -> match d.TryGetValue key with true, v -> box v | _ -> null
        | _ ->
            // POCO property access via reflection (case-insensitive, public instance).
            // Essential for things like `{{ user.Name }}` where user is a plain CLR object.
            let t = v.GetType()
            let cacheKey = t.FullName + "|" + key.ToLowerInvariant()
            let prop = propCache.GetOrAdd(cacheKey, fun _ ->
                t.GetProperty(key, BindingFlags.Public ||| BindingFlags.Instance ||| BindingFlags.IgnoreCase))
            if prop <> null && prop.CanRead then prop.GetValue(v) else null
    let seqOf (v: obj) =
        match v with
        | null -> Seq.empty
        | :? System.Collections.IEnumerable as ie -> ie |> Seq.cast<obj>
        | _ -> Seq.singleton v

    // ── Expression evaluator ────────────────────────────────
    // Precedence (low → high): or → and → not → comparison → additive
    // → multiplicative → atom (literal / path / pipe-filter chain).
    // Respects quotes and paren/bracket nesting when splitting operators.

    /// Coerce any value to a float for arithmetic/numeric comparison.
    let toNum (v: obj) : float =
        match v with
        | :? int as i -> float i
        | :? int64 as i -> float i
        | :? double as d -> d
        | :? single as f -> float f
        | :? bool as b -> if b then 1.0 else 0.0
        | :? string as s -> (match Double.TryParse s with true, n -> n | _ -> nan)
        | null -> 0.0
        | _ -> nan

    /// Structural equality used by == / != operators.
    let valuesEqual (a: obj) (b: obj) : bool =
        match a, b with
        | null, null -> true
        | null, _ | _, null -> false
        | (:? string as sa), (:? string as sb) -> sa = sb
        | _ ->
            let na, nb = toNum a, toNum b
            if not (Double.IsNaN na) && not (Double.IsNaN nb) then na = nb
            else (toStr a) = (toStr b)

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
    let private splitTopLevelArgs (s: string) : string list =
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
    /// Also skips `||` (logical or). Fixes MIGRATION_NOTES §1.4.
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

    /// Validate that an expression's parentheses/brackets are balanced.
    /// Throws on imbalance so the error surfaces with a source line.
    let private checkBalanced (s: string) =
        let n = s.Length
        let mutable inS = false
        let mutable inD = false
        let mutable depth = 0
        let mutable i = 0
        while i < n do
            let c = s.[i]
            if inS then (if c = '\'' then inS <- false); i <- i + 1
            elif inD then (if c = '"' then inD <- false); i <- i + 1
            elif c = '\'' then inS <- true; i <- i + 1
            elif c = '"' then inD <- true; i <- i + 1
            elif c = '(' || c = '[' then depth <- depth + 1; i <- i + 1
            elif c = ')' || c = ']' then depth <- depth - 1; if depth < 0 then i <- n else i <- i + 1
            else i <- i + 1
        if depth <> 0 then
            raise (Exception(sprintf "Unbalanced parentheses/brackets in expression: %s" s))

    let rec evalExpr (exprText: string) (ctx: IDictionary<string, obj>) : obj =
        let text = exprText.Trim()
        if text = "" then box "" else
        checkBalanced text
        evalPipe text ctx

    // Pipe `|` has the LOWEST precedence in Nunjucks/Jinja — lower than
    // arithmetic, comparison and logic — so it is handled here, ABOVE evalOr.
    // `a / b | round` therefore parses as `(a / b) | round`, not `a / (b|round)`.
    // The LHS is delegated to evalOr (which descends through arithmetic etc.);
    // each RHS segment is a filter. Filter args are split on top-level commas
    // and evaluated via `evalExpr`, so `filter(x | subfilter(y))` resolves.
    // Fixes MIGRATION_NOTES §1.4 (pipe inside filter args) and §1.7
    // (arithmetic + nested pipes evaluating to 0).
    and evalPipe (text: string) ctx : obj =
        let parts = splitTopLevelPipes text
        if parts.Length <= 1 then evalOr text ctx else
        let rawVal = evalOr parts.Head ctx
        let mutable result = rawVal
        for fp in parts.Tail do
            if fp <> "" then
                let ppi = fp.IndexOf('(')
                let fname, fargsText =
                    if ppi >= 0 then fp.[..ppi-1], fp.[ppi+1..fp.Length-2].Trim()
                    else fp, ""
                let fargs =
                    if fargsText = "" then []
                    else
                        splitTopLevelArgs fargsText |> List.map (fun a -> evalExpr a ctx)
                result <- applyFilter fname result fargs
        result

    and evalOr (text: string) ctx : obj =
        match findTopOp text [ "or" ] with
        | Some(i, op) ->
            let l = evalOr (text.[..i-1]) ctx
            if toBool l then box true else box (toBool (evalOr (text.[i+op.Length..]) ctx))
        | None -> evalAnd text ctx

    and evalAnd (text: string) ctx : obj =
        match findTopOp text [ "and" ] with
        | Some(i, op) ->
            let l = evalAnd (text.[..i-1]) ctx
            if not (toBool l) then box false else box (toBool (evalAnd (text.[i+op.Length..]) ctx))
        | None -> evalNot text ctx

    and evalNot (text: string) ctx : obj =
        let t = text.Trim()
        if t.StartsWith("not ") then box (not (toBool (evalNot (t.[4..]) ctx)))
        else evalCompare t ctx

    and evalCompare (text: string) ctx : obj =
        match findTopOp text [ "=="; "!="; ">="; "<="; ">"; "<"; " in " ] with
        | Some(i, op) ->
            let lhs = text.[..i-1]
            let rhs = text.[i+op.Length..]
            let l = evalAdd lhs ctx
            let r = evalAdd rhs ctx
            match op.Trim() with
            | "==" -> box (valuesEqual l r)
            | "!=" -> box (not (valuesEqual l r))
            | ">"  -> box (toNum l >  toNum r)
            | "<"  -> box (toNum l <  toNum r)
            | ">=" -> box (toNum l >= toNum r)
            | "<=" -> box (toNum l <= toNum r)
            | "in" ->
                let found = seqOf r |> Seq.exists (fun x -> valuesEqual x l)
                let strContains = match r with :? string as sv -> sv.Contains(toStr l) | _ -> false
                box (found || strContains)
            | _ -> box false
        | None -> evalAdd text ctx

    and evalAdd (text: string) ctx : obj =
        match findTopOp text [ "+"; "-" ] with
        | Some(i, op) when text.[..i-1].Trim() <> "" ->
            let l = evalAdd (text.[..i-1]) ctx
            let r = evalMul (text.[i+op.Length..]) ctx
            // '+' concatenates when either side is a non-numeric string
            if op = "+" && (match l, r with (:? string as ls), _ when Double.IsNaN(toNum ls) -> true
                                          | _, (:? string as rs) when Double.IsNaN(toNum rs) -> true
                                          | _ -> false)
            then box (toStr l + toStr r)
            else box (if op = "+" then toNum l + toNum r else toNum l - toNum r)
        | _ -> evalMul text ctx

    and evalMul (text: string) ctx : obj =
        match findTopOp text [ "**"; "*"; "/"; "%" ] with
        | Some(i, op) when text.[..i-1].Trim() <> "" ->
            let l = toNum (evalMul (text.[..i-1]) ctx)
            let r = toNum (evalAtom (text.[i+op.Length..]) ctx)
            box (match op with
                  | "**" -> Math.Pow(l, r)
                  | "*" -> l * r
                  | "/" -> (if r = 0.0 then 0.0 else l / r)
                  | _ -> (if r = 0.0 then 0.0 else l % r))
        | _ -> evalAtom text ctx

    /// Resolve a dotted/bracketed path like `a.b[0].c['x']` against the context.
    and resolvePath (pathText: string) (ctx: IDictionary<string, obj>) : obj =
        let segments = ResizeArray<string>()
        let sb = StringBuilder()
        let mutable i = 0
        let n = pathText.Length
        while i < n do
            let c = pathText.[i]
            if c = '.' then
                if sb.Length > 0 then segments.Add(sb.ToString()); sb.Clear() |> ignore
                i <- i + 1
            elif c = '[' then
                if sb.Length > 0 then segments.Add(sb.ToString()); sb.Clear() |> ignore
                let e = pathText.IndexOf(']', i)
                if e > i then
                    segments.Add(pathText.Substring(i+1, e-i-1).Trim().Trim('"', '\''))
                    i <- e + 1
                else i <- n
            else sb.Append(c) |> ignore; i <- i + 1
        if sb.Length > 0 then segments.Add(sb.ToString())
        let mutable cur : obj = null
        let mutable first = true
        let mutable ok = true
        for seg in segments do
            if ok then
                if first then
                    match ctx.TryGetValue seg with
                    | true, v -> cur <- v
                    | _ -> ok <- false; cur <- null
                    first <- false
                else
                    match cur with
                    | :? System.Collections.IList as l ->
                        match Int32.TryParse seg with
                        | true, idx when idx >= 0 && idx < l.Count -> cur <- l.[idx]
                        | _ -> cur <- propGet cur seg
                    | _ -> cur <- propGet cur seg
        cur

    /// Built-in `range([start], stop, [step])` generator (Nunjucks-compatible).
    /// Returns an obj[] of integers so it is directly iterable by `for` and `seqOf`.
    and evalRange (inner: string) (ctx: IDictionary<string, obj>) : obj =
        let parts = splitTopLevelArgs inner |> List.map (fun a -> evalExpr a ctx)
        let toI (v: obj) = match v with :? int as i -> i | _ -> int(toNum v)
        let arr =
            match parts with
            | [stop] ->
                [| for i in 0 .. toI stop - 1 -> box i |]
            | [start; stop] ->
                [| for i in toI start .. toI stop - 1 -> box i |]
            | [start; stop; step] ->
                let s = toI start
                let e = toI stop
                let st = toI step
                if st = 0 then [||]
                else
                    [| let mutable i = s
                       while (if st > 0 then i < e else i > e) do
                           yield box i
                           i <- i + st |]
            | _ -> [||]
        arr :> obj

    and evalAtom (text: string) (ctx: IDictionary<string, obj>) : obj =
        let t = text.Trim()
        if t = "" then box "" else
        // range([start], stop, [step]) — built-in global generator function.
        if t.StartsWith("range(") && t.EndsWith(")") then
            let inner = t.[6..t.Length-2].Trim()
            evalRange inner ctx
        // loop.cycle(...) / loop.changed(...) — function-like access on the loop object.
        elif t.StartsWith("loop.cycle(") && t.EndsWith(")") then
            let inner = t.[11..t.Length-2].Trim()
            let vals = splitTopLevelArgs inner |> List.map (fun a -> evalExpr a ctx)
            match ctx.TryGetValue "loop" with
            | true, (:? IDictionary<string, obj> as ld) ->
                let idx0 = match ld.TryGetValue "index0" with true, v -> (try int(toStr v) with _ -> 0) | _ -> 0
                if vals.Length > 0 then box vals.[idx0 % vals.Length] else box ""
            | _ -> box ""
        elif t.StartsWith("loop.changed(") && t.EndsWith(")") then
            let inner = t.[13..t.Length-2].Trim()
            let valNow = evalExpr inner ctx
            match ctx.TryGetValue "loop" with
            | true, (:? IDictionary<string, obj> as ld) ->
                // state lives on the (stable) loop dictionary so it persists across iterations
                match ld.TryGetValue "__changed__" with
                | true, prev ->
                    if valuesEqual prev valNow then box false
                    else ld.["__changed__"] <- valNow; box true
                | _ -> ld.["__changed__"] <- valNow; box true
            | _ -> box false
        else
        // Parenthesized sub-expression
        if t.StartsWith("(") && t.EndsWith(")") then evalExpr (t.[1..t.Length-2]) ctx else
        let tryLiteral (s: string) =
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
        // Pipe handling now lives in `evalPipe` (above evalOr) so that `|`
        // has the correct lowest precedence. evalAtom resolves only literals
        // and variable paths; parenthesised sub-expressions recurse via
        // evalExpr → evalPipe, so inner pipes inside `(...)` still work.
        match tryLiteral t with
        | Some v -> v
        | _ -> resolvePath t ctx

    and applyFilter (name: string) (value: obj) (args: obj list) : obj =
        let s = toStr value
        // Preserve safe-ness: if the input was already marked safe, string
        // transforms must keep it safe so it is not double-escaped downstream.
        let isSafe = value :? SafeString
        let ret (str: string) = if isSafe then SafeString(str) :> obj else box str
        match name.ToLowerInvariant() with
        // String filters
        | "capitalize" -> if s.Length > 0 then ret(s.[0..0].ToUpper() + s.[1..]) else ret s
        | "lower" | "lowercase" -> ret(s.ToLowerInvariant())
        | "upper" | "uppercase" -> ret(s.ToUpperInvariant())
        | "title" -> ret(reTitle.Replace(s.ToLower(), fun m -> m.Value.ToUpper()))
        | "trim" -> ret(s.Trim())
        | "strip" -> ret(s.Trim())
        | "lstrip" -> ret(s.TrimStart())
        | "rstrip" -> ret(s.TrimEnd())
        | "nl2br" -> ret(s.Replace("\r\n", "\n").Replace("\n", "<br />\n"))
        | "string" | "str" -> ret s
        | "safe" -> SafeString(s) :> obj  // bypass auto-escape
        | "escape" | "e" -> SafeString(HtmlEncode(s)) :> obj
        | "striptags" -> ret(reTags.Replace(s, "").Trim())
        | "truncate" ->
            let len = if args.Length > 0 then (try int(toStr args.[0]) with _ -> 255) else 255
            if s.Length > len then ret(s.[..len-1] + "...") else ret s
        | "wordcount" -> box(s.Split([|' ';'\n';'\t'|], StringSplitOptions.RemoveEmptyEntries).Length)
        | "replace" -> if args.Length >= 2 then ret(s.Replace(toStr args.[0], toStr args.[1])) else value
        | "slugify" -> ret(reSlug.Replace(s.ToLowerInvariant(), "-").Trim('-'))
        | "urlencode" -> ret(Uri.EscapeDataString(s))
        | "filesizeformat" ->
            let n = try float s with _ -> 0.0
            if n < 1024.0 then ret(sprintf "%.0f B" n)
            elif n < 1048576.0 then ret(sprintf "%.1f KB" (n / 1024.0))
            elif n < 1073741824.0 then ret(sprintf "%.1f MB" (n / 1048576.0))
            else ret(sprintf "%.1f GB" (n / 1073741824.0))
        | "random" ->
            match value with
            | :? System.Collections.IEnumerable as ie ->
                let arr = ie |> Seq.cast<obj> |> Array.ofSeq
                if arr.Length > 0 then arr.[System.Random().Next(arr.Length)] else value
            | _ -> value
        | "tojson" -> box(System.Text.Json.JsonSerializer.Serialize(value))
        | "format" ->
            if args.Length > 0 then ret(String.Format(s, args |> List.map toStr |> Array.ofList))
            else value
        | "indent" ->
            let w = if args.Length > 0 then (try int(toStr args.[0]) with _ -> 4) else 4
            ret(reIndent.Replace(s, String(' ', w)))
        | "center" ->
            let w = if args.Length > 0 then (try int(toStr args.[0]) with _ -> 80) else 80
            ret(s.PadLeft((w + s.Length) / 2).PadRight(w))

        // Numeric filters
        // `int` parses via `float` first so decimal strings like "1.245"
        // truncate to 1 instead of failing to 0. Matches Nunjucks `int`
        // semantics (truncate toward zero). Fixes MIGRATION_NOTES §1.5.
        | "int" -> box(try int (float s) with _ -> 0)
        | "float" -> box(try float s with _ -> 0.0)
        | "abs" -> box(abs (try float s with _ -> 0.0))
        | "round" ->
            let precision = if args.Length > 0 then (try int(toStr args.[0]) with _ -> 0) else 0
            let n = try float s with _ -> 0.0
            box(Math.Round(n, precision))

        // Collection filters
        | "length" ->
            match value with
            | :? string as sv -> box sv.Length
            | :? System.Collections.ICollection as c -> box c.Count
            | :? System.Collections.IEnumerable as ie -> box(ie |> Seq.cast<obj> |> Seq.length)
            | _ -> box 0
        | "reverse" ->
            match value with
            | :? string as sv -> box(String(Array.rev(sv.ToCharArray())))
            | :? System.Collections.IEnumerable as ie ->
                box(ie |> Seq.cast<obj> |> Seq.toArray |> Array.rev)
            | _ -> value
        | "first" ->
            match value with
            | :? string as sv when sv.Length > 0 -> box(sv.[0].ToString())
            | :? System.Collections.IList as l when l.Count > 0 -> l.[0]
            | :? System.Collections.IEnumerable as ie ->
                ie |> Seq.cast<obj> |> Seq.tryHead |> Option.defaultValue null
            | _ -> null
        | "last" ->
            match value with
            | :? string as sv when sv.Length > 0 -> box(sv.[sv.Length-1].ToString())
            | :? System.Collections.IList as l when l.Count > 0 -> l.[l.Count-1]
            | :? System.Collections.IEnumerable as ie ->
                ie |> Seq.cast<obj> |> Seq.toArray |> fun a -> if a.Length > 0 then a.[a.Length-1] |> box else null
            | _ -> null
        | "join" ->
            let sep = if args.Length > 0 then toStr args.[0] else ","
            match value with
            | :? System.Collections.IEnumerable as ie ->
                box(String.Join(sep, ie |> Seq.cast<obj> |> Seq.map toStr |> Array.ofSeq))
            | _ -> box s
        | "sort" ->
            match value with
            | :? System.Collections.IEnumerable as ie ->
                let attr = if args.Length > 0 then toStr args.[0] else ""
                if attr = "" then ie |> Seq.cast<obj> |> Seq.map toStr |> Seq.sort |> Array.ofSeq :> obj
                else
                    ie |> Seq.cast<obj>
                    |> Seq.sortBy (fun x -> toStr(propGet x attr))
                    |> Array.ofSeq :> obj
            | _ -> value
        | "slice" ->
            let start = if args.Length > 0 then (try int(toStr args.[0]) with _ -> 0) else 0
            let step = if args.Length > 1 then (try int(toStr args.[1]) with _ -> 1) else 1
            match value with
            | :? System.Collections.IList as l ->
                [| for i in start..step..l.Count-1 -> l.[i] |] :> obj
            | :? System.Collections.IEnumerable as ie ->
                ie |> Seq.cast<obj> |> Seq.indexed
                |> Seq.filter (fun (i, _) -> i >= start && (i - start) % step = 0)
                |> Seq.map snd |> Array.ofSeq :> obj
            | _ -> value
        | "batch" ->
            let n = if args.Length > 0 then max 1 (try int(toStr args.[0]) with _ -> 2) else 2
            match value with
            | :? System.Collections.IEnumerable as ie ->
                let items = ie |> Seq.cast<obj> |> Array.ofSeq
                [| for i in 0..n..items.Length-1 -> items.[i..min (i+n-1) (items.Length-1)] :> obj |] :> obj
            | _ -> value
        | "groupby" ->
            let attr = if args.Length > 0 then toStr args.[0] else ""
            if attr = "" then value else
            match value with
            | :? System.Collections.IEnumerable as ie ->
                ie |> Seq.cast<obj>
                |> Seq.groupBy (fun x -> toStr(propGet x attr))
                |> Seq.map (fun (k, g) -> dict ["key", box k; "items", box(g |> Array.ofSeq)] :> obj)
                |> Array.ofSeq :> obj
            | _ -> value
        | "selectattr" ->
            let attr = if args.Length > 0 then toStr args.[0] else ""
            let test = if args.Length > 1 then toStr args.[1] else "truthy"
            match value with
            | :? System.Collections.IEnumerable as ie ->
                ie |> Seq.cast<obj>
                |> Seq.filter (fun x ->
                    let v = propGet x attr
                    match test with
                    | "truthy" | "True" -> toBool v
                    | "falsy" | "False" -> not (toBool v)
                    | "defined" -> v <> null
                    | "undefined" -> isNull v
                    | _ -> toStr v = test)
                |> Array.ofSeq :> obj
            | _ -> value
        | "rejectattr" ->
            let attr = if args.Length > 0 then toStr args.[0] else ""
            let test = if args.Length > 1 then toStr args.[1] else "truthy"
            match value with
            | :? System.Collections.IEnumerable as ie ->
                ie |> Seq.cast<obj>
                |> Seq.filter (fun x ->
                    let v = propGet x attr
                    match test with
                    | "truthy" | "True" -> not (toBool v)
                    | "falsy" | "False" -> toBool v
                    | "defined" -> isNull v
                    | "undefined" -> v <> null
                    | _ -> toStr v <> test)
                |> Array.ofSeq :> obj
            | _ -> value
        | "items" ->
            match value with
            | :? IDictionary<string, obj> as d ->
                d |> Seq.map (fun kv -> dict ["key", box kv.Key; "value", kv.Value] :> obj)
                |> Array.ofSeq :> obj
            | _ -> value
        | "dictsort" ->
            let caseInsensitive = args.Length > 1 && toStr args.[0] = "true"
            match value with
            | :? IDictionary<string, obj> as d ->
                d |> Seq.sortBy (fun kv -> if caseInsensitive then kv.Key.ToLowerInvariant() else kv.Key)
                |> Seq.map (fun kv -> [| box kv.Key; kv.Value |] :> obj)
                |> Array.ofSeq :> obj
            | _ -> value
        | "list" ->
            match value with
            | :? string as sv -> sv.ToCharArray() |> Array.map (fun c -> box(string c)) :> obj
            | :? System.Collections.IEnumerable as ie -> ie |> Seq.cast<obj> |> Array.ofSeq :> obj
            | _ -> [| value |] :> obj
        | "keys" ->
            match value with
            | :? IDictionary<string, obj> as d -> d.Keys |> Seq.map box |> Array.ofSeq :> obj
            | _ -> value
        | "values" ->
            match value with
            | :? IDictionary<string, obj> as d -> d.Values |> Array.ofSeq :> obj
            | _ -> value
        | "unique" ->
            match value with
            | :? System.Collections.IEnumerable as ie ->
                ie |> Seq.cast<obj> |> Seq.distinctBy toStr |> Array.ofSeq :> obj
            | _ -> value
        | "map" ->
            let attr = if args.Length > 0 then toStr args.[0] else ""
            match value with
            | :? System.Collections.IEnumerable as ie when attr <> "" ->
                ie |> Seq.cast<obj> |> Seq.map (fun x -> propGet x attr) |> Array.ofSeq :> obj
            | _ -> value
        | "sum" ->
            let attr = if args.Length > 0 then toStr args.[0] else ""
            match value with
            | :? System.Collections.IEnumerable as ie ->
                ie |> Seq.cast<obj>
                |> Seq.sumBy (fun x ->
                    let v = if attr = "" then x else propGet x attr
                    match Double.TryParse(toStr v) with true, n -> n | _ -> 0.0)
                |> box
            | _ -> value
        | "min" ->
            match value with
            | :? System.Collections.IEnumerable as ie ->
                let nums = ie |> Seq.cast<obj> |> Seq.choose (fun x -> match Double.TryParse(toStr x) with true, n -> Some n | _ -> None) |> Array.ofSeq
                if nums.Length > 0 then box(Array.min nums) else value
            | _ -> value
        | "max" ->
            match value with
            | :? System.Collections.IEnumerable as ie ->
                let nums = ie |> Seq.cast<obj> |> Seq.choose (fun x -> match Double.TryParse(toStr x) with true, n -> Some n | _ -> None) |> Array.ofSeq
                if nums.Length > 0 then box(Array.max nums) else value
            | _ -> value
        | "merge" ->
            match value, (if args.Length > 0 then args.[0] else null) with
            | (:? IDictionary<string, obj> as a), (:? IDictionary<string, obj> as b) ->
                let merged = Dictionary<string, obj>(a)
                for kv in b do merged.[kv.Key] <- kv.Value
                merged :> obj
            | _ -> value
        | "json" | "dump" ->
            let indent = if args.Length > 0 then (try int(toStr args.[0]) with _ -> 0) else 0
            let opts = System.Text.Json.JsonSerializerOptions(WriteIndented = (indent > 0))
            box(System.Text.Json.JsonSerializer.Serialize(value, opts))

        // Default filter
        | "default" | "d" ->
            let fb = if args.Length > 0 then args.[0] else box ""
            let booleanCheck = args.Length > 1 && toBool args.[1]
            if booleanCheck then
                if toBool value then value else fb
            else
                if isNull value then fb
                else match value with :? string as sv when sv = "" -> fb | _ -> value

        // Date filter
        | "date" ->
            let fmt = if args.Length > 0 then toStr args.[0] else "yyyy-MM-dd"
            let dt = match value with
                     | :? DateTime as d -> d
                     | :? string as sv ->
                         match DateTime.TryParse(sv, CultureInfo.InvariantCulture, DateTimeStyles.None) with
                         | true, d -> d
                         | _ -> DateTime.Now
                     | _ -> DateTime.Now
            box(dt.ToString(fmt, CultureInfo.InvariantCulture))

        // Zest-specific date filters (SEO / RSS)
        | "dateiso" ->
            let dt = match value with
                     | :? DateTime as d -> d
                     | :? string as sv -> (match DateTime.TryParse(sv, CultureInfo.InvariantCulture, DateTimeStyles.None) with true, d -> d | _ -> DateTime.Now)
                     | _ -> DateTime.Now
            box(dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"))
        | "daterfc822" | "daterss" ->
            let dt = match value with
                     | :? DateTime as d -> d
                     | :? string as sv -> (match DateTime.TryParse(sv, CultureInfo.InvariantCulture, DateTimeStyles.None) with true, d -> d | _ -> DateTime.Now)
                     | _ -> DateTime.Now
            box(dt.ToUniversalTime().ToString("ddd, dd MMM yyyy HH:mm:ss", CultureInfo.InvariantCulture) + " GMT")

        // Zest-specific slug / text filters
        | "slugize" ->
            box(reSlug.Replace(s.ToLowerInvariant(), "-").Trim('-'))
        | "slugizepath" ->
            let segs = s.Split('/') |> Array.map (fun seg -> reSlug.Replace(seg.ToLowerInvariant(), "-").Trim('-'))
            box(String.Join("/", segs))
        | "totext" ->
            box(reTags.Replace(s, "").Trim())

        // URL filter
        | "urlize" ->
            box(reUrl.Replace(s, fun m -> sprintf "<a href=\"%s\">%s</a>" m.Value m.Value))

        // Custom registered filters (from Zest)
        | _ ->
            match customFilters.TryGetValue name with
            | true, fn ->
                let strArgs = args |> List.map toStr
                fn value strArgs
            | _ -> value

    /// HTML-encode a string for safe output.
    and HtmlEncode (s: string) =
        if String.IsNullOrEmpty s then s
        else
            let sb = StringBuilder(s)
            sb.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;").ToString()

    // ── Block collector (for extends/block inheritance) ──
    /// Collect all top-level `{% block NAME %}...{% endblock %}` blocks
    /// from a token list. Returns a map from block name to its body tokens.
    let rec private collectBlocks (tokens: Token list) : IDictionary<string, Token list> =
        let arr = tokens |> Array.ofList
        let blocks = Dictionary<string, Token list>()
        let len = arr.Length
        let mutable i = 0
        while i < len do
            match arr.[i] with
            | TagToken("block", args, _) when args.Length > 0 ->
                let name = args.[0].Trim('"', '\'')
                let endIdx = findMatchingEnd (i+1) "block" (arr |> Array.toList)
                if endIdx > i then
                    let body = arr.[i+1..endIdx-1] |> Array.toList
                    blocks.[name] <- body
                    i <- endIdx + 1
                else i <- i + 1
            | TagToken("extends", _, _) | TagToken("macro", _, _) ->
                i <- i + 1
            | _ -> i <- i + 1
        blocks :> IDictionary<string, Token list>

    and findMatchingEnd (start: int) (tagName: string) (tokens: Token list) : int =
        let len = tokens.Length
        let mutable depth = 0
        let mutable result = len
        let mutable i = start
        while i < len do
            match tokens.[i] with
            | TagToken(n, _, _) when n = tagName -> depth <- depth + 1; i <- i + 1
            | TagToken(n, _, _) when n = "end" + tagName ->
                if depth = 0 then result <- i; i <- len  // found it, save position
                else depth <- depth - 1; i <- i + 1
            | _ -> i <- i + 1
        result

    /// Collect all top-level `{% macro name(args) %}...{% endmacro %}` definitions
    /// from a token array. Returns (name, args, body) tuples so they can be
    /// registered into the macro table (used by import / from).
    let collectMacroDefs (tsArr: Token []) : (string * string list * Token list) list =
        let mutable result = []
        let mutable i = 0
        let n = tsArr.Length
        while i < n do
            match tsArr.[i] with
            | TagToken("macro", a, _) when a.Length > 0 ->
                let macroText = a |> String.concat " "
                let pIdx = macroText.IndexOf('(')
                let mname, margs =
                    if pIdx >= 0 then
                        let name = macroText.[..pIdx-1].Trim()
                        let cp = macroText.IndexOf(')', pIdx)
                        let argsPart = if cp >= pIdx then macroText.[pIdx+1..cp-1].Trim() else ""
                        let pargs = if argsPart = "" then [] else argsPart.Split(',') |> Array.map (fun x -> x.Trim()) |> Array.toList
                        name, pargs
                    else macroText.Trim(), []
                let eIdx = findMatchingEnd (i+1) "macro" (tsArr |> Array.toList)
                let body = if eIdx > i+1 then tsArr.[i+1..eIdx-1] |> Array.toList else []
                result <- (mname, margs, body) :: result
                i <- if eIdx > i then eIdx + 1 else i + 1
            | _ -> i <- i + 1
        List.rev result

    // ── Block tags that require a closing end-tag ──────────
    let blockTags = set ["if"; "for"; "block"; "macro"; "filter"; "call"]

    // ── RenderEnv ──────────────────────────────────────────
    type RenderEnv = {
        Variables: IDictionary<string, obj>
        LoadTemplate: string * int -> Result<string, string>
        ChildBlocks: IDictionary<string, Token list>   // blocks from child template
        BlockStack: string list                        // currently active block names
        Depth: int
        Macros: IDictionary<string, (string list * Token list)>   // macro name → (args, body)
        Blocks: IDictionary<string, Token list>        // this template's own block defs (for super())
        CurrentBlock: string option                    // block being rendered (for super())
        CallerBody: Token list option                  // captured {% call %} body (for caller())
        LoopNesting: int                               // current for-loop nesting depth
        LastLine: int ref                              // most recently processed source line (for errors)
    }

    // ── Main renderer ──────────────────────────────────────
    let rec renderTokens (tokens: Token list) (env: RenderEnv) : Result<string, string> =
        let sb = StringBuilder()
        let len = tokens.Length
        let mutable idx = 0
        let mutable error: string option = None

        while idx < len && error.IsNone do
            let curLine =
                match tokens.[idx] with
                | TextToken(_, l) | VarToken(_, l) | TagToken(_, _, l) | CmtToken(_, l) -> l
            env.LastLine.Value <- curLine

            match tokens.[idx] with
            | TextToken(t, _) -> sb.Append(t) |> ignore; idx <- idx + 1

            | CmtToken _ -> idx <- idx + 1

            | VarToken(expr, _) ->
                let exprTrim = expr.Trim()
                // {{ super() }} — render the parent's version of the current block.
                if exprTrim.StartsWith("super(") then
                    match env.CurrentBlock with
                    | Some name ->
                        match env.Blocks.TryGetValue name with
                        | true, parentBody ->
                            match renderTokens parentBody { env with CurrentBlock = Some name } with
                            | Ok h -> sb.Append(h) |> ignore
                            | Error e -> error <- Some e
                        | _ -> ()
                    | None -> ()
                    idx <- idx + 1
                // {{ caller() }} — render the captured {% call %} body.
                elif exprTrim.StartsWith("caller(") then
                    match env.CallerBody with
                    | Some body ->
                        match renderTokens body env with
                        | Ok h -> sb.Append(h) |> ignore
                        | Error e -> error <- Some e
                    | None -> ()
                    idx <- idx + 1
                else
                    // Check for macro call: macroName(args) — must be a function-like expr
                    let pOpen = exprTrim.IndexOf('(')
                    let mutable macroResult : string option = None
                    if pOpen > 0 && exprTrim.EndsWith(")") then
                        let mName = exprTrim.[..pOpen-1].Trim()
                        match env.Macros.TryGetValue mName with
                        | true, (margNames, mbody) ->
                            let argsText = exprTrim.[pOpen+1..exprTrim.Length-2].Trim()
                            let argValues =
                                if argsText = "" then []
                                else argsText.Split(',') |> Array.map (fun a -> evalExpr a env.Variables) |> Array.toList
                            // Build a new context with macro arguments
                            let mCtx = Dictionary<string, obj>(env.Variables |> Seq.map (fun kv -> KeyValuePair(kv.Key, kv.Value)))
                            let rec zipArgs (names: string list) (vals: obj list) =
                                match names, vals with
                                | n::ns, v::vs -> mCtx.[n] <- v; zipArgs ns vs
                                | _ -> ()
                            zipArgs margNames argValues
                            match renderTokens mbody { env with Variables = mCtx :> IDictionary<string, obj> } with
                            | Ok h -> macroResult <- Some h
                            | Error e -> error <- Some e
                        | _ -> ()
                    match macroResult with
                    | Some h ->
                        sb.Append(h) |> ignore
                    | None ->
                        let v = evalExpr expr env.Variables
                        // Auto-escape: strings are escaped unless marked as safe
                        let html =
                            match v with
                            | :? SafeString as ss -> ss.Value
                            | :? string as sv -> HtmlEncode sv
                            | null -> ""
                            | _ -> toStr v
                        sb.Append(html) |> ignore
                    idx <- idx + 1

            | TagToken(tag, args, _) ->
                let arr = tokens |> Array.ofList
                let isBlock = blockTags.Contains(tag)
                let endIdx =
                    if isBlock then findMatchingEnd (idx+1) tag (arr |> Array.toList)
                    else idx
                // A block tag whose matching end was not found: report a precise error.
                if isBlock && endIdx >= len && len > idx then
                    error <- Some(sprintf "Unclosed block tag '{%% %s %%}'" tag)
                else
                let bodyTokens =
                    if isBlock && endIdx > idx+1 then arr.[idx+1..endIdx-1] |> Array.toList
                    else []
                let bodyHtml =
                    if isBlock then
                        match renderTokens bodyTokens env with
                        | Ok h -> Some h
                        | Error e -> error <- Some e; None
                    else None

                match tag with
                | "if" ->
                    // Split the body into (condition, branchTokens) at top-level
                    // elif/else boundaries, then render the first matching branch.
                    let condExpr = args |> String.concat " "
                    let branches = splitIfBranches condExpr bodyTokens
                    let chosen =
                        branches |> List.tryPick (fun (cond, toks) ->
                            let matched =
                                match cond with
                                | None -> true   // else branch
                                | Some "" -> true
                                | Some e -> toBool (evalExpr e env.Variables)
                            if matched then Some toks else None)
                    match chosen with
                    | Some toks ->
                        match renderTokens toks env with
                        | Ok h -> sb.Append(h) |> ignore
                        | Error e -> error <- Some e
                    | None -> ()

                | "for" ->
                    let ls = args |> String.concat " "
                    let inIdx = ls.IndexOf(" in ", StringComparison.Ordinal)
                    let loopVar, iterExpr =
                        if inIdx >= 0 then ls.[..inIdx-1].Trim(), ls.[inIdx+4..]
                        else ls, ""
                    let iter = evalExpr iterExpr env.Variables |> seqOf |> Array.ofSeq
                    let loopTokens = forLoopBody bodyTokens
                    // Support "key, value" destructuring for dict/pair iteration.
                    let varNames = loopVar.Split(',') |> Array.map (fun v -> v.Trim())
                    if iter.Length > 0 then
                        // Reuse a single context + a single loop dictionary across all
                        // iterations to avoid per-iteration heap allocations (perf).
                        let loopDict = Dictionary<string, obj>()
                        loopDict.Remove("__changed__") |> ignore   // reset loop.changed() state
                        let ctx = Dictionary<string, obj>(env.Variables |> Seq.map (fun kv -> KeyValuePair(kv.Key, kv.Value)))
                        ctx.["loop"] <- loopDict
                        for idxItem, item in iter |> Array.indexed do
                            if varNames.Length = 2 then
                                match item with
                                | :? IDictionary<string, obj> as kv ->
                                    ctx.[varNames.[0]] <- (match kv.TryGetValue "key" with true, k -> k | _ -> box "")
                                    ctx.[varNames.[1]] <- (match kv.TryGetValue "value" with true, v -> v | _ -> box "")
                                | :? System.Collections.IList as pair when pair.Count >= 2 ->
                                    ctx.[varNames.[0]] <- pair.[0]
                                    ctx.[varNames.[1]] <- pair.[1]
                                | _ -> ctx.[loopVar] <- item
                            else
                                ctx.[loopVar] <- item
                            let prev = if idxItem > 0 then box iter.[idxItem-1] else null
                            let nxt = if idxItem < iter.Length - 1 then box iter.[idxItem+1] else null
                            loopDict.["index"] <- box(idxItem+1); loopDict.["index0"] <- box idxItem
                            loopDict.["revindex"] <- box(iter.Length-idxItem); loopDict.["revindex0"] <- box(iter.Length-idxItem-1)
                            loopDict.["first"] <- box(idxItem=0); loopDict.["last"] <- box(idxItem=iter.Length-1)
                            loopDict.["length"] <- box iter.Length
                            loopDict.["depth"] <- box(env.LoopNesting + 1)
                            loopDict.["depth0"] <- box env.LoopNesting
                            loopDict.["previtem"] <- prev; loopDict.["nextitem"] <- nxt
                            match renderTokens loopTokens { env with Variables = ctx :> IDictionary<string, obj>; LoopNesting = env.LoopNesting + 1 } with
                            | Ok h -> sb.Append(h) |> ignore
                            | Error e -> error <- Some e
                    else
                        // else body of for
                        let elseBody = forElseBody bodyTokens
                        match renderTokens elseBody env with
                        | Ok h -> sb.Append(h) |> ignore
                        | Error e -> error <- Some e

                | "block" ->
                    let name = if args.Length > 0 then args.[0].Trim('"', '\'') else ""
                    // Check if child template overrides this block
                    match env.ChildBlocks.TryGetValue name with
                    | true, childBody when not (env.BlockStack |> List.contains name) ->
                        // Render child's block content (which may itself extend further)
                        let childEnv = { env with BlockStack = name :: env.BlockStack; CurrentBlock = Some name }
                        match renderTokens childBody childEnv with
                        | Ok h -> sb.Append(h) |> ignore
                        | Error e -> error <- Some e
                    | _ ->
                        // Use parent's default content
                        match bodyHtml with Some h -> sb.Append(h) |> ignore | None -> ()

                | "extends" ->
                    let path = if args.Length > 0 then args.[0].Trim('"', '\'') else ""
                    match env.LoadTemplate (path, env.Depth + 1) with
                    | Ok txt ->
                        let parentTokens = tokenize txt
                        // Collect blocks from the parent (for super()) and the child
                        let parentBlocks = collectBlocks parentTokens
                        let childBlocks = collectBlocks tokens
                        // Render parent with child blocks available for override
                        let parentEnv = { env with
                                            ChildBlocks = childBlocks
                                            Blocks = parentBlocks
                                            Depth = env.Depth + 1
                                            BlockStack = [] }
                        match renderTokens parentTokens parentEnv with
                        | Ok h -> sb.Append(h) |> ignore
                        | Error e -> error <- Some e
                        // extends replaces the whole template: stop rendering the
                        // child's own (already-inherited) tokens.
                        idx <- len
                    | Error e -> error <- Some e

                | "include" ->
                    let path = if args.Length > 0 then args.[0].Trim('"', '\'') else ""
                    let ignoreMissing = args.Length > 1 && (args |> String.concat " ").Contains("ignore", StringComparison.OrdinalIgnoreCase)
                    match env.LoadTemplate (path, env.Depth + 1) with
                    | Ok txt ->
                        match renderTokens (tokenize txt) env with
                        | Ok h -> sb.Append(h) |> ignore
                        | Error e -> error <- Some e
                    | Error _ when ignoreMissing -> ()
                    | Error e -> error <- Some e

                | "set" ->
                    let setText = args |> String.concat " "
                    let eqIdx = setText.IndexOf("=")
                    if eqIdx >= 0 then
                        let sname = setText.[..eqIdx-1].Trim()
                        let sval = evalExpr setText.[eqIdx+1..] env.Variables
                        env.Variables.[sname] <- sval
                    else
                        // Block assignment: {% set name %}...{% endset %}
                        let sname = setText.Trim().Trim('"', '\'')
                        let endIdx = findMatchingEnd (idx+1) "set" (arr |> Array.toList)
                        if endIdx > idx then
                            let body = arr.[idx+1..endIdx-1] |> Array.toList
                            match renderTokens body env with
                            | Ok h -> env.Variables.[sname] <- box h
                            | Error e -> error <- Some e
                            idx <- endIdx   // consumed; default increment advances past {% endset %}
                        ()
                | "macro" ->
                    if args.Length > 0 then
                        let macroText = args |> String.concat " "
                        let pIdx = macroText.IndexOf('(')
                        let mname, margs =
                            if pIdx >= 0 then
                                let name = macroText.[..pIdx-1].Trim()
                                let argsPart =
                                    let cp = macroText.IndexOf(')', pIdx)
                                    if cp >= pIdx then macroText.[pIdx+1..cp-1].Trim() else ""
                                let pargs =
                                    if argsPart = "" then []
                                    else argsPart.Split(',') |> Array.map (fun a -> a.Trim()) |> Array.toList
                                name, pargs
                            else macroText.Trim(), []
                        env.Macros.[mname] <- (margs, bodyTokens)
                    ()

                | "call" ->
                    // {% call macroName(arg) %}body{% endcall %}
                    let macroText = args |> String.concat " "
                    let pIdx = macroText.IndexOf('(')
                    let mname =
                        if pIdx >= 0 then macroText.[..pIdx-1].Trim()
                        else macroText.Trim()
                    let callArgs =
                        if pIdx >= 0 then
                            let at = macroText.[pIdx+1..macroText.Length-2].Trim()
                            if at = "" then [] else at.Split(',') |> Array.map (fun a -> evalExpr a env.Variables) |> Array.toList
                        else []
                    match env.Macros.TryGetValue mname with
                    | true, (margs, mbody) ->
                        let mCtx = Dictionary<string, obj>(env.Variables |> Seq.map (fun kv -> KeyValuePair(kv.Key, kv.Value)))
                        // Bind positional macro arguments
                        let rec zipArgs (names: string list) (vals: obj list) =
                            match names, vals with
                            | n::ns, v::vs -> mCtx.[n] <- v; zipArgs ns vs
                            | _ -> ()
                        zipArgs margs callArgs
                        // Make the captured body available as caller() (also kept as
                        // a string for backwards compatibility with {{ caller }}).
                        match bodyHtml with
                        | Some h -> mCtx.["caller"] <- box h
                        | None -> ()
                        let callEnv = { env with
                                            Variables = (mCtx :> IDictionary<string, obj>)
                                            CallerBody = if bodyHtml.IsSome then Some bodyTokens else None }
                        match renderTokens mbody callEnv with
                        | Ok h -> sb.Append(h) |> ignore
                        | Error e -> error <- Some e
                    | _ -> ()

                | "import" ->
                    let importText = args |> String.concat " "
                    let asIdx = importText.IndexOf(" as ", StringComparison.OrdinalIgnoreCase)
                    let path, asName =
                        if asIdx >= 0 then
                            importText.[..asIdx-1].Trim().Trim('"', '\''),
                            importText.[asIdx+4..].Trim()
                        else importText.Trim().Trim('"', '\''), ""
                    match env.LoadTemplate (path, env.Depth + 1) with
                    | Ok txt ->
                        let importTokens = tokenize txt |> Array.ofList
                        // Register every macro from the imported file as a callable.
                        let defs = collectMacroDefs importTokens
                        for (mname, margs, body) in defs do
                            let key = if asName <> "" then asName + "." + mname else mname
                            env.Macros.[key] <- (margs, body)
                    | Error e -> error <- Some e

                | "from" ->
                    let fromText = args |> String.concat " "
                    let impIdx = fromText.IndexOf(" import ", StringComparison.OrdinalIgnoreCase)
                    let path, imports =
                        if impIdx >= 0 then
                            fromText.[..impIdx-1].Trim().Trim('"', '\''),
                            fromText.[impIdx+8..].Trim()
                        else "", ""
                    let asIdxOrig = imports.IndexOf(" as ", StringComparison.OrdinalIgnoreCase)
                    let importName, asName =
                        if asIdxOrig >= 0 then imports.[..asIdxOrig-1].Trim(), imports.[asIdxOrig+4..].Trim()
                        else imports.Trim(), ""
                    match env.LoadTemplate (path, env.Depth + 1) with
                    | Ok txt ->
                        let defs = collectMacroDefs (tokenize txt |> Array.ofList)
                        match defs |> List.tryFind (fun (mname, _, _) -> mname = importName) with
                        | Some(_, margs, body) ->
                            let key = if asName <> "" then asName else importName
                            env.Macros.[key] <- (margs, body)
                        | None -> ()
                    | Error e -> error <- Some e

                | "raw" ->
                    match bodyHtml with Some h -> sb.Append(h) |> ignore | None -> ()

                | "filter" ->
                    let fname = if args.Length > 0 then args.[0] else ""
                    match bodyHtml with
                    | Some h -> sb.Append(toStr (applyFilter fname (box h) [])) |> ignore
                    | None -> ()

                | "now" | "endblock" | "endfor" | "endif" | "endmacro" | "endcall" | "endraw" | "endfilter" ->
                    ()  // closing tags are handled by findMatchingEnd

                | _ -> ()  // unknown tag — silently ignore (Nunjucks behavior)

                idx <- if isBlock && endIdx > idx then endIdx + 1 else idx + 1

        match error with
        | Some e -> Error e
        | None -> Ok(sb.ToString())

    /// Split an if-block body into ordered branches, each tagged with an
    /// optional condition (None = the final `else`). Nested if/for blocks are
    /// skipped so their inner elif/else tags don't split the outer branch.
    and splitIfBranches (firstCond: string) (tokens: Token list) : (string option * Token list) list =
        let arr = tokens |> Array.ofList
        let n = arr.Length
        let branches = ResizeArray<string option * Token list>()
        let mutable curCond : string option = Some firstCond
        let cur = ResizeArray<Token>()
        let mutable depth = 0
        let mutable i = 0
        while i < n do
            match arr.[i] with
            | TagToken(("if" | "for"), _, _) -> depth <- depth + 1; cur.Add arr.[i]
            | TagToken(("endif" | "endfor"), _, _) -> depth <- depth - 1; cur.Add arr.[i]
            | TagToken(("elif" | "elseif"), a, _) when depth = 0 ->
                branches.Add(curCond, List.ofSeq cur); cur.Clear()
                curCond <- Some(a |> String.concat " ")
            | TagToken("else", _, _) when depth = 0 ->
                branches.Add(curCond, List.ofSeq cur); cur.Clear()
                curCond <- None
            | t -> cur.Add t
            i <- i + 1
        branches.Add(curCond, List.ofSeq cur)
        List.ofSeq branches

    /// Extract the `{% else %}` body of a for-loop (depth-aware). Returns the
    /// tokens after a top-level else, or an empty list when none is present.
    and forElseBody (tokens: Token list) : Token list =
        let arr = tokens |> Array.ofList
        let n = arr.Length
        let mutable depth = 0
        let mutable elseIdx = -1
        let mutable i = 0
        while i < n && elseIdx < 0 do
            match arr.[i] with
            | TagToken(("if" | "for"), _, _) -> depth <- depth + 1
            | TagToken(("endif" | "endfor"), _, _) -> depth <- depth - 1
            | TagToken("else", _, _) when depth = 0 -> elseIdx <- i
            | _ -> ()
            i <- i + 1
        if elseIdx >= 0 && elseIdx + 1 < n then arr.[elseIdx+1..] |> Array.toList else []

    /// Extract the loop body of a for-loop, stopping at a top-level else.
    and forLoopBody (tokens: Token list) : Token list =
        let arr = tokens |> Array.ofList
        let n = arr.Length
        let mutable depth = 0
        let mutable elseIdx = -1
        let mutable i = 0
        while i < n && elseIdx < 0 do
            match arr.[i] with
            | TagToken(("if" | "for"), _, _) -> depth <- depth + 1
            | TagToken(("endif" | "endfor"), _, _) -> depth <- depth - 1
            | TagToken("else", _, _) when depth = 0 -> elseIdx <- i
            | _ -> ()
            i <- i + 1
        if elseIdx >= 0 then arr.[..elseIdx-1] |> Array.toList else tokens


// ── Public engine class ────────────────────────────────────────────────

type NunjucksEngine() =

    let templateCache = ConcurrentDictionary<string, struct(DateTime * string)>()
    let mutable loadFileFn: string -> Result<string, string> = fun path ->
        try Ok(File.ReadAllText(path))
        with :? FileNotFoundException -> Error(sprintf "Template not found: %s" path)
           | ex -> Error(ex.Message)

    member _.SetLoadFile(fn: string -> Result<string, string>) = loadFileFn <- fn

    interface ITemplateEngine with
        member _.Name = "nunjucks"

        member _.Render(templateText: string) (variables: IDictionary<string, obj>) : Result<string, TemplateError> =
            let lastLine = ref 0
            try
                let tokens = NunjucksImpl.tokenize templateText
                let env: NunjucksImpl.RenderEnv = {
                    Variables = variables
                    LoadTemplate = fun (path, depth) ->
                        if depth > 10 then Error("Circular include/extends detected")
                        else
                            let fullPath = if Path.IsPathRooted(path) then path
                                           else Path.Combine(Directory.GetCurrentDirectory(), path)
                            loadFileFn fullPath
                    ChildBlocks = dict [] :> IDictionary<_, _>
                    BlockStack = []
                    Depth = 0
                    Macros = Dictionary<string, (string list * NunjucksImpl.Token list)>()
                    Blocks = dict [] :> IDictionary<_, _>
                    CurrentBlock = None
                    CallerBody = None
                    LoopNesting = 0
                    LastLine = lastLine
                }
                match NunjucksImpl.renderTokens tokens env with
                | Ok s -> Ok s
                | Error msg -> Error(TemplateError.RuntimeError(msg, !lastLine))
            with ex -> Error(TemplateError.RuntimeError(ex.Message, !lastLine))

        member this.RenderFile(filePath: string) (variables: IDictionary<string, obj>) : Result<string, TemplateError> =
            try
                let text =
                    match templateCache.TryGetValue filePath with
                    | true, struct(mtime, cached) when mtime = File.GetLastWriteTimeUtc(filePath) -> cached
                    | _ ->
                        let t = File.ReadAllText(filePath)
                        templateCache.[filePath] <- struct(File.GetLastWriteTimeUtc(filePath), t)
                        t
                (this :> ITemplateEngine).Render text variables
            with :? FileNotFoundException -> Error(TemplateError.NotFound filePath)
               | ex -> Error(TemplateError.RuntimeError(ex.Message, 0))

        member _.RegisterFilter(name: string) (fn: FilterFn) =
            NunjucksImpl.customFilters.[name] <- fn

        member _.RegisterTag(handler: TagHandler) = ()

        member _.ClearCache() =
            templateCache.Clear()
            NunjucksImpl.tokenCache.Clear()
