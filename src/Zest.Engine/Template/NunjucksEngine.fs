namespace Zest.Engine.Template

open System
open System.Collections.Generic
open System.IO
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
    let customFilters = Dictionary<string, FilterFn>()

    // ── Safe string wrapper (bypasses auto-escaping) ──
    type SafeString(s: string) =
        member _.Value = s
        override _.ToString() = s

    // ── Token types ────────────────────────────────────────
    type Token =
        | TextToken of string
        | VarToken  of string      // {{ expr }}
        | TagToken  of tag: string * args: string list  // {% tag args %}
        | CmtToken  of string     // {# comment #}

    // ── Tokenizer (idempotent, cached) ─────────────────────
    let tokenCache = Dictionary<string, struct(DateTime * Token list)>()

    let tokenize (text: string) : Token list =
        let tokens = ResizeArray<Token>()
        let sb = StringBuilder()
        let len = text.Length
        let mutable i = 0
        let flush () =
            if sb.Length > 0 then tokens.Add(TextToken(sb.ToString())); sb.Clear() |> ignore

        while i < len do
            if i + 2 < len then
                let c = text.[i]
                if c = '{' && text.[i+1] = '#' then               // {# comment #}
                    flush()
                    let e = text.IndexOf("#}", i+2)
                    if e < 0 then i <- len
                    else tokens.Add(CmtToken(text.Substring(i+2, e-i-2))); i <- e + 2
                elif c = '{' && text.[i+1] = '{' then              // {{ var }}
                    flush()
                    let e = text.IndexOf("}}", i+2)
                    if e < 0 then sb.Append(text.Substring(i)) |> ignore; i <- len
                    else tokens.Add(VarToken(text.Substring(i+2, e-i-2).Trim())); i <- e + 2
                elif c = '{' && text.[i+1] = '%' then              // {% tag %}
                    flush()
                    let e = text.IndexOf("%}", i+2)
                    if e < 0 then sb.Append(text.Substring(i)) |> ignore; i <- len
                    else
                        let raw = text.Substring(i+2, e-i-2).Trim()
                        let parts = raw.Split([|' ';'\n';'\t';'\r'|], StringSplitOptions.RemoveEmptyEntries)
                        let tag = if parts.Length > 0 then parts.[0] else ""
                        let args = if parts.Length > 1 then parts.[1..] |> Array.toList else []
                        // raw tag: capture everything until {% endraw %} as literal text
                        if tag = "raw" then
                            let rawEnd = text.IndexOf("{% endraw %}", e+2)
                            if rawEnd >= e+2 then
                                let rawContent = text.Substring(e+2, rawEnd - (e+2))
                                tokens.Add(TextToken(rawContent))
                                i <- rawEnd + 13  // skip past "{% endraw %}"
                            else
                                tokens.Add(TagToken(tag, args))
                                i <- e + 2
                        else
                            tokens.Add(TagToken(tag, args)); i <- e + 2
                else sb.Append(c) |> ignore; i <- i + 1
            else sb.Append(text.[i]) |> ignore; i <- i + 1
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
        | _ -> null
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

    let rec evalExpr (exprText: string) (ctx: IDictionary<string, obj>) : obj =
        let text = exprText.Trim()
        if text = "" then box "" else evalOr text ctx

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
        match findTopOp text [ "*"; "/"; "%" ] with
        | Some(i, op) when text.[..i-1].Trim() <> "" ->
            let l = toNum (evalMul (text.[..i-1]) ctx)
            let r = toNum (evalAtom (text.[i+op.Length..]) ctx)
            box (match op with "*" -> l * r | "/" -> (if r = 0.0 then 0.0 else l / r) | _ -> (if r = 0.0 then 0.0 else l % r))
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

    and evalAtom (text: string) (ctx: IDictionary<string, obj>) : obj =
        let t = text.Trim()
        if t = "" then box "" else
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
        let pipeIdx = t.IndexOf("|")
        if pipeIdx >= 0 && (pipeIdx = 0 || t.[pipeIdx-1] <> '|') && (pipeIdx+1 >= t.Length || t.[pipeIdx+1] <> '|') then
            let lhs = t.[..pipeIdx-1].Trim()
            let rawVal = match tryLiteral lhs with Some v -> v | _ -> resolvePath lhs ctx
            let filterParts = t.[pipeIdx+1..].Split('|') |> Array.map (fun x -> x.Trim()) |> Array.filter (fun x -> x <> "")
            let mutable result = rawVal
            for fp in filterParts do
                let ppi = fp.IndexOf('(')
                let fname, fargsText =
                    if ppi >= 0 then fp.[..ppi-1], fp.[ppi+1..fp.Length-2].Trim()
                    else fp, ""
                let fargs =
                    if fargsText = "" then []
                    else
                        // Parse filter arguments respecting quoted strings (comma is separator)
                        let rec parseFilterArgs (rem: string) (acc: string list) =
                            let rem = rem.TrimStart()
                            if rem = "" then List.rev acc
                            elif rem.[0] = '"' || rem.[0] = '\'' then
                                let quote = rem.[0]
                                let mutable i = 1
                                while i < rem.Length && rem.[i] <> quote do i <- i + 1
                                let arg = if i > 1 then rem.[1..i-1] else ""
                                let rest = if i + 1 < rem.Length then rem.[i+1..] else ""
                                let restTrimmed = rest.TrimStart()
                                let rest2 = if restTrimmed.StartsWith(",") then restTrimmed.[1..] else restTrimmed
                                parseFilterArgs rest2 (arg :: acc)
                            else
                                let commaIdx = rem.IndexOf(',')
                                if commaIdx >= 0 then
                                    let arg = rem.[..commaIdx-1].Trim()
                                    parseFilterArgs rem.[commaIdx+1..] (arg :: acc)
                                else
                                    List.rev ((rem.Trim()) :: acc)
                        parseFilterArgs fargsText [] |> List.map (fun s -> box s :> obj)
                result <- applyFilter fname result fargs
            result
        else
            match tryLiteral t with
            | Some v -> v
            | _ -> resolvePath t ctx

    and applyFilter (name: string) (value: obj) (args: obj list) : obj =
        let s = toStr value
        match name.ToLowerInvariant() with
        // String filters
        | "capitalize" -> if s.Length > 0 then box(s.[0..0].ToUpper() + s.[1..]) else box s
        | "lower" | "lowercase" -> box(s.ToLowerInvariant())
        | "upper" | "uppercase" -> box(s.ToUpperInvariant())
        | "title" -> box(Regex.Replace(s.ToLower(), @"\b\w", fun m -> m.Value.ToUpper()))
        | "trim" -> box(s.Trim())
        | "strip" -> box(s.Trim())
        | "lstrip" -> box(s.TrimStart())
        | "rstrip" -> box(s.TrimEnd())
        | "nl2br" -> box(s.Replace("\r\n", "\n").Replace("\n", "<br />\n"))
        | "string" | "str" -> box s
        | "safe" -> SafeString(s) :> obj  // bypass auto-escape
        | "escape" | "e" -> SafeString(HtmlEncode(s)) :> obj
        | "striptags" -> box(Regex.Replace(s, @"<[^>]+>", "").Trim())
        | "truncate" ->
            let len = if args.Length > 0 then (try int(toStr args.[0]) with _ -> 255) else 255
            if s.Length > len then box(s.[..len-1] + "...") else box s
        | "wordcount" -> box(s.Split([|' ';'\n';'\t'|], StringSplitOptions.RemoveEmptyEntries).Length)
        | "replace" -> if args.Length >= 2 then box(s.Replace(toStr args.[0], toStr args.[1])) else value
        | "slugify" -> box(Regex.Replace(s.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-'))
        | "urlencode" -> box(Uri.EscapeDataString(s))
        | "format" ->
            if args.Length > 0 then box(String.Format(s, args |> List.map toStr |> Array.ofList))
            else value
        | "indent" ->
            let w = if args.Length > 0 then (try int(toStr args.[0]) with _ -> 4) else 4
            box(Regex.Replace(s, "^", String(' ', w), RegexOptions.Multiline))
        | "center" ->
            let w = if args.Length > 0 then (try int(toStr args.[0]) with _ -> 80) else 80
            box(s.PadLeft((w + s.Length) / 2).PadRight(w))

        // Numeric filters
        | "int" -> box(try int s with _ -> 0)
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
                         match DateTime.TryParse sv with
                         | true, d -> d
                         | _ -> DateTime.Now
                     | _ -> DateTime.Now
            box(dt.ToString(fmt))

        // Zest-specific date filters (SEO / RSS)
        | "dateiso" ->
            let dt = match value with
                     | :? DateTime as d -> d
                     | :? string as sv -> (match DateTime.TryParse sv with true, d -> d | _ -> DateTime.Now)
                     | _ -> DateTime.Now
            box(dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"))
        | "daterfc822" | "daterss" ->
            let dt = match value with
                     | :? DateTime as d -> d
                     | :? string as sv -> (match DateTime.TryParse sv with true, d -> d | _ -> DateTime.Now)
                     | _ -> DateTime.Now
            box(dt.ToUniversalTime().ToString("ddd, dd MMM yyyy HH:mm:ss", Globalization.CultureInfo.InvariantCulture) + " GMT")

        // Zest-specific slug / text filters
        | "slugize" ->
            box(Regex.Replace(s.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-'))
        | "slugizepath" ->
            let segs = s.Split('/') |> Array.map (fun seg -> Regex.Replace(seg.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-'))
            box(String.Join("/", segs))
        | "totext" ->
            box(Regex.Replace(s, @"<[^>]+>", "").Trim())

        // URL filter
        | "urlize" ->
            box(Regex.Replace(s, @"(https?://[^\s<>""']+)",
                              fun m -> sprintf "<a href=\"%s\">%s</a>" m.Value m.Value))

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
            | TagToken("block", args) when args.Length > 0 ->
                let name = args.[0].Trim('"', '\'')
                let endIdx = findMatchingEnd (i+1) "block" (arr |> Array.toList)
                if endIdx > i then
                    let body = arr.[i+1..endIdx-1] |> Array.toList
                    blocks.[name] <- body
                    i <- endIdx + 1
                else i <- i + 1
            | TagToken("extends", _) | TagToken("macro", _) ->
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
            | TagToken(n, _) when n = tagName -> depth <- depth + 1; i <- i + 1
            | TagToken(n, _) when n = "end" + tagName ->
                if depth = 0 then result <- i; i <- len  // found it, save position
                else depth <- depth - 1; i <- i + 1
            | _ -> i <- i + 1
        result

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
    }

    // ── Main renderer ──────────────────────────────────────
    let rec renderTokens (tokens: Token list) (env: RenderEnv) : Result<string, string> =
        let sb = StringBuilder()
        let len = tokens.Length
        let mutable idx = 0
        let mutable error: string option = None

        while idx < len && error.IsNone do
            match tokens.[idx] with
            | TextToken t -> sb.Append(t) |> ignore; idx <- idx + 1

            | CmtToken _ -> idx <- idx + 1

            | VarToken expr ->
                let exprTrim = expr.Trim()
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

            | TagToken(tag, args) ->
                let arr = tokens |> Array.ofList
                let isBlock = blockTags.Contains(tag)
                let endIdx =
                    if isBlock then findMatchingEnd (idx+1) tag (arr |> Array.toList)
                    else idx
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
                        for idxItem, item in iter |> Array.indexed do
                            let ctx = Dictionary<string, obj>(env.Variables |> Seq.map (fun kv -> KeyValuePair(kv.Key, kv.Value)))
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
                            ctx.["loop"] <- box(dict [
                                "index", box(idxItem+1); "index0", box idxItem
                                "revindex", box(iter.Length-idxItem); "revindex0", box(iter.Length-idxItem-1)
                                "first", box(idxItem=0); "last", box(idxItem=iter.Length-1)
                                "length", box iter.Length
                            ])
                            match renderTokens loopTokens { env with Variables = ctx :> IDictionary<string, obj> } with
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
                        let childEnv = { env with BlockStack = name :: env.BlockStack }
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
                        // Collect blocks from child (the current template's tokens)
                        let childBlocks = collectBlocks tokens
                        // Render parent with child blocks available for override
                        let parentEnv = { env with
                                            ChildBlocks = childBlocks
                                            Depth = env.Depth + 1
                                            BlockStack = [] }
                        match renderTokens parentTokens parentEnv with
                        | Ok h -> sb.Append(h) |> ignore
                        | Error e -> error <- Some e
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
                    match env.Macros.TryGetValue mname with
                    | true, (margs, mbody) ->
                        // Build a context with macro args + caller block
                        let ctx = Dictionary<string, obj>(env.Variables |> Seq.map (fun kv -> KeyValuePair(kv.Key, kv.Value)))
                        // Pass caller content as the body
                        match bodyHtml with
                        | Some h -> ctx.["caller"] <- box h
                        | None -> ()
                        match renderTokens mbody { env with Variables = ctx :> IDictionary<string, obj> } with
                        | Ok h -> sb.Append(h) |> ignore
                        | Error e -> error <- Some e
                    | _ -> ()
                    // Remove from variables if it's a call target, not an output
                    if mname.Length > 0 then
                        match env.Variables.TryGetValue mname with
                        | true, _ -> ()  // keep it
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
                        let importTokens = tokenize txt
                        // Collect macros from imported file
                        let importMacros = Dictionary<string, obj>()
                        let rec collectMacros (ts: Token list) =
                            for t in ts do
                                match t with
                                | TagToken("macro", a) when a.Length > 0 ->
                                    let mn = a |> String.concat " "
                                    let p2 = mn.IndexOf('(')
                                    let mname = if p2 >= 0 then mn.[..p2-1].Trim() else mn.Trim()
                                    importMacros.[mname] <- box(sprintf "<macro:%s from %s>" mname path)
                                | _ -> ()
                        collectMacros importTokens
                        if asName <> "" then
                            env.Variables.[asName] <- box importMacros
                        else
                            for kv in importMacros do
                                env.Variables.[kv.Key] <- kv.Value
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
                        let importTokens = tokenize txt
                        // Find the macro definition
                        let mutable body = []
                        let rec findMacro (tsArr: Token []) =
                            let mutable result = None
                            let mutable i = 0
                            while i < tsArr.Length && Option.isNone result do
                                match tsArr.[i] with
                                | TagToken("macro", a) when a.Length > 0 ->
                                    let mn = a |> String.concat " "
                                    let p2 = mn.IndexOf('(')
                                    let mname = if p2 >= 0 then mn.[..p2-1].Trim() else mn.Trim()
                                    let eIdx = findMatchingEnd (i+1) "macro" (tsArr |> Array.toList)
                                    if mname = importName then
                                        let bd = if eIdx > i+1 then tsArr.[i+1..eIdx-1] |> Array.toList else []
                                        result <- Some bd
                                    i <- eIdx + 1
                                | _ -> i <- i + 1
                            result
                        match findMacro (importTokens |> Array.ofList) with
                        | Some b -> body <- b
                        | None -> ()
                        // Store rendered macro in variables
                        let finalName = if asName <> "" then asName else importName
                        match renderTokens body { env with Depth = env.Depth + 1 } with
                        | Ok h -> env.Variables.[finalName] <- box h
                        | Error e -> error <- Some e
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
            | TagToken(("if" | "for"), _) -> depth <- depth + 1; cur.Add arr.[i]
            | TagToken(("endif" | "endfor"), _) -> depth <- depth - 1; cur.Add arr.[i]
            | TagToken(("elif" | "elseif"), a) when depth = 0 ->
                branches.Add(curCond, List.ofSeq cur); cur.Clear()
                curCond <- Some(a |> String.concat " ")
            | TagToken("else", _) when depth = 0 ->
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
            | TagToken(("if" | "for"), _) -> depth <- depth + 1
            | TagToken(("endif" | "endfor"), _) -> depth <- depth - 1
            | TagToken("else", _) when depth = 0 -> elseIdx <- i
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
            | TagToken(("if" | "for"), _) -> depth <- depth + 1
            | TagToken(("endif" | "endfor"), _) -> depth <- depth - 1
            | TagToken("else", _) when depth = 0 -> elseIdx <- i
            | _ -> ()
            i <- i + 1
        if elseIdx >= 0 then arr.[..elseIdx-1] |> Array.toList else tokens


// ── Public engine class ────────────────────────────────────────────────

type NunjucksEngine() =

    let templateCache = Dictionary<string, struct(DateTime * string)>()
    let mutable loadFileFn: string -> Result<string, string> = fun path ->
        try Ok(File.ReadAllText(path))
        with :? FileNotFoundException -> Error(sprintf "Template not found: %s" path)
           | ex -> Error(ex.Message)

    member _.SetLoadFile(fn: string -> Result<string, string>) = loadFileFn <- fn

    interface ITemplateEngine with
        member _.Name = "nunjucks"

        member _.Render(templateText: string) (variables: IDictionary<string, obj>) : Result<string, TemplateError> =
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
                }
                match NunjucksImpl.renderTokens tokens env with
                | Ok s -> Ok s
                | Error msg -> Error(TemplateError.RuntimeError(msg, 0))
            with ex -> Error(TemplateError.RuntimeError(ex.Message, 0))

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
