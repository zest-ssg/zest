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
    let rec evalExpr (exprText: string) (ctx: IDictionary<string, obj>) : obj =
        let rec resolvePath (parts: string list) (cur: obj option) : obj =
            match parts, cur with
            | [], None -> null
            | [], Some v -> v
            | name :: rest, None ->
                match ctx.TryGetValue name with
                | true, v -> resolvePath rest (Some v)
                | _ -> null
            | part :: rest, Some curObj ->
                resolvePath rest (Some(propGet curObj part))
        let text = exprText.Trim()
        if text = "" then box "" else
        let tryLiteral (s: string) =
            let t = s.Trim()
            if (t.StartsWith("\"") && t.EndsWith("\"")) || (t.StartsWith("'") && t.EndsWith("'")) then
                Some(box(t.Substring(1, t.Length-2)))
            elif t = "true" then Some(box true)
            elif t = "false" then Some(box false)
            elif t = "null" || t = "none" || t = "undefined" then Some null
            else
                match Int32.TryParse t with
                | true, i -> Some(box i)
                | _ -> match Double.TryParse t with | true, f -> Some(box f) | _ -> None
        let pipeIdx = text.IndexOf("|")
        if pipeIdx >= 0 then
            let lhs = text.[..pipeIdx-1].Trim()
            let rawVal = match tryLiteral lhs with Some v -> v | _ -> resolvePath (lhs.Split('.') |> Array.toList) None
            let filterParts = text.[pipeIdx+1..].Split('|') |> Array.map (fun x -> x.Trim()) |> Array.filter (fun x -> x <> "")
            let mutable result = rawVal
            for fp in filterParts do
                let ppi = fp.IndexOf('(')
                let fname, fargsText =
                    if ppi >= 0 then fp.[..ppi-1], fp.[ppi+1..fp.Length-2].Trim()
                    else fp, ""
                let fargs =
                    if fargsText = "" then []
                    else fargsText.Split(',') |> Array.map (fun a -> evalExpr a ctx) |> Array.toList
                result <- applyFilter fname result fargs
            result
        else
            match tryLiteral text with
            | Some v -> v
            | _ -> resolvePath (text.Split('.') |> Array.toList) None

    and applyFilter (name: string) (value: obj) (args: obj list) : obj =
        let s = toStr value
        match name.ToLowerInvariant() with
        // String filters
        | "capitalize" -> if s.Length > 0 then box(s.[0..0].ToUpper() + s.[1..]) else box s
        | "lower" | "lowercase" -> box(s.ToLowerInvariant())
        | "upper" | "uppercase" -> box(s.ToUpperInvariant())
        | "title" -> box(Regex.Replace(s.ToLower(), @"\b\w", fun m -> m.Value.ToUpper()))
        | "trim" -> box(s.Trim())
        | "safe" -> value   // bypass auto-escape
        | "escape" | "e" -> box(HtmlEncode(s))
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
        let mutable i = start
        while i < len do
            match tokens.[i] with
            | TagToken(n, _) when n = tagName -> depth <- depth + 1; i <- i + 1
            | TagToken(n, _) when n = "end" + tagName ->
                if depth = 0 then i <- len  // found it
                else depth <- depth - 1; i <- i + 1
            | _ -> i <- i + 1
        i

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
                let v = evalExpr expr env.Variables
                // Auto-escape: only escape strings, not other types (safe filter bypasses this)
                let html =
                    match v with
                    | :? string as sv -> HtmlEncode sv
                    | null -> ""
                    | _ -> toStr v
                sb.Append(html) |> ignore; idx <- idx + 1

            | TagToken(tag, args) ->
                let arr = tokens |> Array.ofList
                let endIdx = findMatchingEnd (idx+1) tag (arr |> Array.toList)
                let bodyTokens =
                    if endIdx > idx+1 then arr.[idx+1..endIdx-1] |> Array.toList
                    else []
                let bodyHtml =
                    match renderTokens bodyTokens env with
                    | Ok h -> Some h
                    | Error e -> error <- Some e; None

                match tag with
                | "if" ->
                    let cond = if args.Length > 0 then toBool(evalExpr args.[0] env.Variables) else true
                    // Check for elseif/else in bodyTokens
                    if cond then
                        // Render body until first elseif/else
                        let bodyOnly = bodyTokens |> takeUntilElseTag
                        match renderTokens bodyOnly env with
                        | Ok h -> sb.Append(h) |> ignore
                        | Error e -> error <- Some e
                    else
                        // Skip to else/elseif/end
                        let remaining = bodyTokens |> skipPastIfBody
                        match renderTokens remaining env with
                        | Ok h -> sb.Append(h) |> ignore
                        | Error e -> error <- Some e

                | "for" ->
                    let ls = args |> String.concat " "
                    let inIdx = ls.IndexOf(" in ", StringComparison.Ordinal)
                    let loopVar, iterExpr =
                        if inIdx >= 0 then ls.[..inIdx-1].Trim(), ls.[inIdx+4..]
                        else ls, ""
                    let iter = evalExpr iterExpr env.Variables |> seqOf |> Array.ofSeq
                    if iter.Length > 0 then
                        for idxItem, item in iter |> Array.indexed do
                            let ctx = Dictionary<string, obj>(env.Variables |> Seq.map (fun kv -> KeyValuePair(kv.Key, kv.Value)))
                            ctx.[loopVar] <- item
                            ctx.["loop"] <- box(dict [
                                "index", box(idxItem+1); "index0", box idxItem
                                "first", box(idxItem=0); "last", box(idxItem=iter.Length-1)
                                "length", box iter.Length
                            ])
                            match renderTokens bodyTokens { env with Variables = ctx :> IDictionary<string, obj> } with
                            | Ok h -> sb.Append(h) |> ignore
                            | Error e -> error <- Some e
                    else
                        // else body of for
                        let elseBody = bodyTokens |> skipPastIfBody
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

                idx <- if endIdx >= idx then endIdx + 1 else idx + 1

        match error with
        | Some e -> Error e
        | None -> Ok(sb.ToString())

    /// Take tokens from body until hitting an elseif/else tag (for if body)
    and takeUntilElseTag (tokens: Token list) : Token list =
        tokens |> List.takeWhile (fun t ->
            match t with
            | TagToken(n, _) when n = "else" || n = "elseif" || n = "elif" -> false
            | _ -> true)

    /// Given an if body, skip past the if-true portion and return remaining (else/elseif branches)
    and skipPastIfBody (tokens: Token list) : Token list =
        tokens |> List.skipWhile (fun t ->
            match t with
            | TagToken(n, _) when n = "else" || n = "elseif" || n = "elif" -> false
            | _ -> true)


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
