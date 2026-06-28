namespace Zest.Engine.Template

open System
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.RegularExpressions

// ============================================================
// NunjucksEngine — F# implementation of Nunjucks template engine
// ============================================================

module private NunjucksImpl =

    // ── Custom filter registry (extensible by Zest engine) ──
    let customFilters = Dictionary<string, FilterFn>()

    // ── Token types ───────────
    type Token =
        | TextToken of string
        | VarToken  of string
        | TagToken  of tag: string * args: string list
        | CmtToken  of string

    // ── Tokenizer ─────────────
    let tokenize (text: string) : Token list =
        let tokens = ResizeArray<Token>()
        let sb = StringBuilder()
        let len = text.Length
        let mutable i = 0
        let flush () =
            if sb.Length > 0 then tokens.Add(TextToken(sb.ToString())); sb.Clear() |> ignore
        while i < len do
            if i + 2 < len && text.[i] = '{' && text.[i+1] = '#' then
                flush()
                let e = text.IndexOf("#}", i+2)
                if e < 0 then tokens.Add(CmtToken(text.Substring(i+2))); i <- len
                else tokens.Add(CmtToken(text.Substring(i+2, e-i-2))); i <- e + 2
            elif i + 2 < len && text.[i] = '{' && text.[i+1] = '{' then
                flush()
                let e = text.IndexOf("}}", i+2)
                if e < 0 then sb.Append(text.Substring(i)) |> ignore; i <- len
                else tokens.Add(VarToken(text.Substring(i+2, e-i-2).Trim())); i <- e + 2
            elif i + 2 < len && text.[i] = '{' && text.[i+1] = '%' then
                flush()
                let e = text.IndexOf("%}", i+2)
                if e < 0 then sb.Append(text.Substring(i)) |> ignore; i <- len
                else
                    let raw = text.Substring(i+2, e-i-2).Trim()
                    let parts = raw.Split([|' ';'\n';'\t'|], StringSplitOptions.RemoveEmptyEntries)
                    let tag = if parts.Length > 0 then parts.[0] else ""
                    let args = if parts.Length > 1 then parts.[1..] |> Array.toList else []
                    tokens.Add(TagToken(tag, args)); i <- e + 2
            else sb.Append(text.[i]) |> ignore; i <- i + 1
        flush()
        Seq.toList tokens

    // ── Runtime helpers ──────
    let toStr (v: obj) = match v with null -> "" | :? string as s -> s | _ -> v.ToString()
    let toBool (v: obj) =
        match v with null -> false | :? bool as b -> b | :? string as s -> s <> ""
                       | :? int as i -> i <> 0 | _ -> true
    let propGet (v: obj) (key: string) =
        match v with
        | null -> null
        | :? IDictionary<string, obj> as d -> match d.TryGetValue key with true, v -> v | _ -> null
        | :? IDictionary<string, string> as d -> match d.TryGetValue key with true, v -> v :> obj | _ -> null
        | _ -> null
    let seqOf (v: obj) =
        match v with null -> Seq.empty | :? System.Collections.IEnumerable as ie -> ie |> Seq.cast<obj> | _ -> Seq.singleton v

    // ── Expression evaluator ──
    let rec evalExpr (exprText: string) (ctx: IDictionary<string, obj>) : obj =
        let rec resolvePath (parts: string list) (cur: obj option) : obj =
            match parts, cur with
            | [], None -> null
            | [], Some v -> v
            | name :: rest, None ->
                match ctx.TryGetValue name with true, v -> resolvePath rest (Some v) | _ -> null
            | part :: rest, Some curObj ->
                resolvePath rest (Some(propGet curObj part))
        let text = exprText.Trim()
        if text = "" then box "" else
        let tryLiteral (s: string) =
            let t = s.Trim()
            if (t.StartsWith("\"") && t.EndsWith("\"")) || (t.StartsWith("'") && t.EndsWith("'")) then Some(box(t.Substring(1, t.Length-2)))
            elif t = "true" then Some(box true) elif t = "false" then Some(box false)
            elif t = "null" || t = "none" || t = "undefined" then Some null
            else
                match System.Int32.TryParse t with
                | true, i -> Some(box i)
                | _ ->
                    match System.Double.TryParse t with
                    | true, f -> Some(box f)
                    | _ -> None
        let pipeIdx = text.IndexOf("|")
        if pipeIdx >= 0 then
            let lhs = text.[..pipeIdx-1].Trim()
            let rawVal = match tryLiteral lhs with Some v -> v | _ -> resolvePath (lhs.Split('.') |> Array.toList) None
            let filterParts = text.[pipeIdx+1..].Split('|') |> Array.map (fun x -> x.Trim()) |> Array.filter (fun x -> x <> "")
            let mutable result = rawVal
            for fp in filterParts do
                let ppi = fp.IndexOf('(')
                let fname, fargsText = if ppi >= 0 then fp.[..ppi-1], fp.[ppi+1..fp.Length-2].Trim() else fp, ""
                let fargs = if fargsText = "" then [] else fargsText.Split(',') |> Array.map (fun a -> evalExpr a ctx) |> Array.toList
                result <- applyFilter fname result fargs
            result
        else match tryLiteral text with Some v -> v | _ -> resolvePath (text.Split('.') |> Array.toList) None

    and applyFilter (name: string) (value: obj) (args: obj list) : obj =
        let s = toStr value
        match name with
        | "capitalize" -> if s.Length > 0 then box(s.[0..0].ToUpper() + s.[1..]) else box s
        | "lower" | "lowercase" -> box(s.ToLowerInvariant())
        | "upper" | "uppercase" -> box(s.ToUpperInvariant())
        | "trim" -> box(s.Trim())
        | "length" -> match value with :? string as sv -> box sv.Length | :? System.Collections.ICollection as c -> box c.Count | _ -> box 0
        | "reverse" -> match value with :? string as sv -> box(String(Array.rev(sv.ToCharArray()))) | _ -> value
        | "first" -> match value with :? string as sv when sv.Length > 0 -> box(sv.[0].ToString()) | :? System.Collections.IList as l when l.Count > 0 -> l.[0] | _ -> null
        | "last" -> match value with :? string as sv when sv.Length > 0 -> box(sv.[sv.Length-1].ToString()) | :? System.Collections.IList as l when l.Count > 0 -> l.[l.Count-1] | _ -> null
        | "join" -> let sep = if args.Length > 0 then toStr args.[0] else "," in match value with :? System.Collections.IEnumerable as ie -> box(String.Join(sep, ie |> Seq.cast<obj> |> Seq.map toStr |> Array.ofSeq)) | _ -> box s
        | "sort" -> match value with :? System.Collections.IEnumerable as ie -> ie |> Seq.cast<obj> |> Seq.map toStr |> Seq.sort |> Array.ofSeq :> obj | _ -> value
        | "default" | "d" -> let fb = if args.Length > 0 then args.[0] else box "" in if value = null then fb else match value with :? string as sv when sv = "" -> fb | _ -> value
        | "safe" -> value
        | "escape" | "e" -> box(System.Net.WebUtility.HtmlEncode(s))
        | "truncate" -> let len = if args.Length > 0 then (try int(toStr args.[0]) with _ -> 255) else 255 in if s.Length > len then box(s.[..len-1] + "...") else box s
        | "wordcount" -> box(s.Split([|' ';'\n';'\t'|], StringSplitOptions.RemoveEmptyEntries).Length)
        | "replace" -> if args.Length >= 2 then box(s.Replace(toStr args.[0], toStr args.[1])) else value
        | "slugify" -> box(Regex.Replace(s.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-'))
        | "date" -> let fmt = if args.Length > 0 then toStr args.[0] else "yyyy-MM-dd" in let dt = match value with :? DateTime as d -> d | :? string as sv -> match DateTime.TryParse sv with true,d->d|_->DateTime.Now | _ -> DateTime.Now in box(dt.ToString(fmt))
        | "striptags" -> box(Regex.Replace(s, @"<[^>]+>", "").Trim())
        | "int" -> box(try int s with _ -> 0)
        | "float" -> box(try float s with _ -> 0.0)
        | "list" -> seqOf value |> Seq.cast<obj> |> Seq.toList :> obj
        | _ ->
            // Check custom registered filters
            match customFilters.TryGetValue name with
            | true, fn ->
                let strArgs = args |> List.map toStr
                fn value strArgs
            | _ -> value

    // ── RenderEnv ────────────
    type RenderEnv = {
        Variables: IDictionary<string, obj>
        LoadTemplate: string * int -> Result<string, string>
        Blocks: IDictionary<string, string>
        BlockDepth: int
    }

    // ── Main renderer ────────
    let rec renderTokens (tokens: Token list) (env: RenderEnv) : Result<string, string> =
        let sb = StringBuilder()
        let len = tokens.Length
        let mutable idx = 0
        let mutable error: string option = None

        let findEnd (start: int) (tagName: string) : int * Token list * Token list option =
            let mutable i = start
            let mutable depth = 0
            let mutable bodyEnd = start
            let mutable bodyTokens: Token list = []
            let mutable elseTokens: Token list option = None
            let hasElse = tagName = "if" || tagName = "for"

            while i < len do
                match tokens.[i] with
                | TagToken(n, _) when n = tagName -> depth <- depth + 1; bodyTokens <- bodyTokens @ [tokens.[i]]; i <- i + 1
                | TagToken(n, _) when n = "end" + tagName ->
                    if depth = 0 then bodyEnd <- i; i <- len
                    else depth <- depth - 1; bodyTokens <- bodyTokens @ [tokens.[i]]; i <- i + 1
                | TagToken(n, _) when hasElse && n = "else" && depth = 0 ->
                    bodyTokens <- tokens.[start..i-1] |> Array.ofSeq |> Array.toList
                    let mutable j = i + 1
                    let mutable ed = 0
                    while j < len do
                        match tokens.[j] with
                        | TagToken(tn, _) when tn = tagName -> ed <- ed + 1; j <- j + 1
                        | TagToken(tn, _) when tn = "end" + tagName ->
                            if ed = 0 then elseTokens <- Some(tokens.[i+1..j-1] |> Array.ofSeq |> Array.toList); bodyEnd <- j; j <- len
                            else ed <- ed - 1; j <- j + 1
                        | _ -> j <- j + 1
                    i <- len
                | _ -> bodyTokens <- bodyTokens @ [tokens.[i]]; i <- i + 1
            bodyEnd, bodyTokens, elseTokens

        while idx < len && error.IsNone do
            match tokens.[idx] with
            | TextToken s -> sb.Append(s) |> ignore; idx <- idx + 1
            | CmtToken _ -> idx <- idx + 1
            | VarToken expr ->
                let v = evalExpr expr env.Variables |> toStr
                sb.Append(System.Net.WebUtility.HtmlEncode(v)) |> ignore; idx <- idx + 1
            | TagToken(tag, args) ->
                let endIdx, bodyTokens, elseBody = findEnd (idx+1) tag
                let bodyHtml : string option =
                    match renderTokens bodyTokens env with
                    | Ok h -> Some h
                    | Error e -> error <- Some e; None
                match tag with
                | "if" ->
                    let cond = if args.Length > 0 then toBool(evalExpr args.[0] env.Variables) else true
                    if cond then
                        match bodyHtml with Some h -> sb.Append(h) |> ignore | None -> ()
                    else
                        match elseBody with
                        | Some et -> match renderTokens et env with Ok h -> sb.Append(h) |> ignore | Error e -> error <- Some e
                        | None -> ()
                | "for" ->
                    let ls = args |> String.concat " "
                    let inIdx = ls.IndexOf(" in ", StringComparison.Ordinal)
                    let loopVar, iterExpr = if inIdx >= 0 then ls.[..inIdx-1].Trim(), ls.[inIdx+4..] else ls, ""
                    let iter = evalExpr iterExpr env.Variables |> seqOf |> Array.ofSeq
                    if iter.Length > 0 then
                        for idxItem, item in iter |> Array.indexed do
                            let ctx = Dictionary<string, obj>(env.Variables |> Seq.map (fun kv -> KeyValuePair(kv.Key, kv.Value)))
                            ctx.[loopVar] <- item
                            ctx.["loop"] <- box(dict ["index", box(idxItem+1); "index0", box idxItem; "first", box(idxItem=0); "last", box(idxItem=iter.Length-1); "length", box iter.Length])
                            match renderTokens bodyTokens { env with Variables = ctx :> IDictionary<string, obj> } with
                            | Ok h -> sb.Append(h) |> ignore | Error e -> error <- Some e
                    else
                        match elseBody with Some et -> (match renderTokens et env with Ok h -> sb.Append(h) |> ignore | Error e -> error <- Some e) | None -> ()
                | "block" ->
                    let name = if args.Length > 0 then args.[0].Trim('"', '\'') else ""
                    match env.Blocks.TryGetValue name with
                    | true, oc -> sb.Append(oc) |> ignore
                    | _ -> match bodyHtml with Some h -> sb.Append(h) |> ignore | None -> ()
                | "extends" ->
                    let path = if args.Length > 0 then args.[0].Trim('"', '\'') else ""
                    match env.LoadTemplate (path, env.BlockDepth + 1) with
                    | Ok txt ->
                        let pt = tokenize txt
                        match renderTokens pt { env with BlockDepth = env.BlockDepth + 1 } with
                        | Ok h -> sb.Append(h) |> ignore | Error e -> error <- Some e
                    | Error e -> error <- Some e
                | "include" ->
                    let path = if args.Length > 0 then args.[0].Trim('"', '\'') else ""
                    let ignoreMissing = args.Length > 1 && (args |> String.concat " ").Contains("ignore")
                    match env.LoadTemplate (path, env.BlockDepth + 1) with
                    | Ok txt -> match renderTokens (tokenize txt) env with Ok h -> sb.Append(h) |> ignore | Error e -> error <- Some e
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
                | "raw" -> match bodyHtml with Some h -> sb.Append(h) |> ignore | None -> ()
                | "filter" ->
                    let fname = if args.Length > 0 then args.[0] else ""
                    match bodyHtml with
                    | Some h -> sb.Append(toStr (applyFilter fname (box h) [])) |> ignore
                    | None -> ()
                | _ -> ()
                idx <- if endIdx > idx then endIdx + 1 else idx + 1

        match error with Some e -> Error e | None -> Ok(sb.ToString())


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
                            let fullPath = if Path.IsPathRooted(path) then path else Path.Combine(Directory.GetCurrentDirectory(), path)
                            loadFileFn fullPath
                    Blocks = dict []
                    BlockDepth = 0
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

        member _.ClearCache() = templateCache.Clear()
