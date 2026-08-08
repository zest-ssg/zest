namespace Zest.Engine.Template

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open System.Reflection

// ============================================================
// HbsEngine — standalone Mustache / Handlebars template engine
// ============================================================
// Renders `.hbs` / `.mustache` templates directly, without going
// through the Nunjucks converter. Handles the syntax that cannot
// be fully expressed in Nunjucks:
//   * `{{{ expr }}}` / `{{& expr }}` — unescaped output
//   * `{{#section}}…{{else}}…{{/section}}` — blocks with else
//   * `{{^inverted}}…{{/inverted}}` — inverted sections
//   * `{{#each list}}` with `{{@index}}` / `{{@key}}` / `{{@first}}` / `{{@last}}`
//   * `{{this}}`, `.`, `../` parent-context lookups, `@root`
//   * `{{> partial}}` partials
//   * `{{#if}}` / `{{#unless}}` / `{{#with}}` built-in helpers
// Engine selection is by file extension (`.hbs` / `.mustache`),
// see ScriptEvaluator and LayoutEngine.
// ============================================================

module private HbsImpl =

    // ── HTML escaping (Mustache `{{ }}` is HTML-escaped by default) ──
    let htmlEncode (s: string) =
        if isNull s then "" else
        s.Replace("&", "&amp;")
         .Replace("<", "&lt;")
         .Replace(">", "&gt;")
         .Replace("\"", "&quot;")
         .Replace("'", "&#39;")

    // ── Tokens ─────────────────────────────────────────────────────────
    type HbsToken =
        | TText of string
        | TExpr of expr: string * triple: bool
        | TBlockOpen of name: string * args: string
        | TBlockClose of name: string
        | TElseIf of args: string
        | TElse
        | TInverted of name: string * args: string
        | TPartial of name: string * args: string
        | TComment

    // ── AST nodes ──────────────────────────────────────────────────────
    type HbsNode =
        | NText of string
        | NExpr of expr: string * triple: bool
        | NBlock of name: string * args: string * body: HbsNode list * elseBody: HbsNode list option
        | NInverted of name: string * args: string * body: HbsNode list
        | NPartial of name: string * args: string

    // ── Single-pass tokenizer ──────────────────────────────────────────
    // Scans `{{ }}`, `{{{ }}}`, `{{!-- --}}`, `{{! }}` in one pass.
    let tokenize (src: string) : HbsToken list =
        let tokens = ResizeArray<HbsToken>()
        let sb = Text.StringBuilder()
        let len = src.Length
        let mutable i = 0
        let flushText () =
            if sb.Length > 0 then
                tokens.Add(TText(sb.ToString()))
                sb.Clear() |> ignore
        let findClose (start: int) (closeLen: int) =
            let mutable j = start
            let mutable found = -1
            while found < 0 && j + closeLen <= len do
                if src.[j] = '}' && (closeLen = 1 || (closeLen = 2 && src.[j + 1] = '}') || (closeLen = 3 && j + 2 < len && src.[j + 1] = '}' && src.[j + 2] = '}')) then
                    found <- j
                else j <- j + 1
            found
        while i < len do
            let c = src.[i]
            if c = '{' && i + 1 < len && src.[i + 1] = '{' then
                // triple mustache `{{{ ... }}}`?
                let triple = i + 2 < len && src.[i + 2] = '{' && i + 3 < len && src.[i + 3] <> '{'
                let openLen = if triple then 3 else 2
                let start = i + openLen
                // comment `{{!-- ... --}}`?
                if not triple && start + 2 < len && src.[start] = '!' && src.[start + 1] = '-' && src.[start + 2] = '-' then
                    let closeIdx = src.IndexOf("--}}", start + 3, StringComparison.Ordinal)
                    if closeIdx >= 0 then
                        flushText ()
                        tokens.Add(TComment)
                        i <- closeIdx + 4
                    else
                        sb.Append(src, i, len - i) |> ignore
                        i <- len
                else
                    let closeLen = if triple then 3 else 2
                    let closeIdx = findClose start closeLen
                    if closeIdx < 0 then
                        sb.Append(src, i, len - i) |> ignore
                        i <- len
                    else
                        flushText ()
                        let raw = src.Substring(start, closeIdx - start).Trim()
                        let classify =
                            if raw.StartsWith("!") then TComment
                            elif raw.StartsWith(">") then
                                let rest = raw.Substring(1).Trim()
                                let parts = rest.Split([| ' '; '\t'; '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
                                if parts.Length = 0 then TComment
                                else TPartial(parts.[0], String.Join(" ", parts.[1..]))
                            elif raw.StartsWith("#") then
                                let rest = raw.Substring(1).Trim()
                                let parts = rest.Split([| ' '; '\t'; '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
                                if parts.Length = 0 then TComment
                                else TBlockOpen(parts.[0], String.Join(" ", parts.[1..]))
                            elif raw.StartsWith("/") then
                                TBlockClose(raw.Substring(1).Trim())
                            elif raw.StartsWith("^") then
                                let rest = raw.Substring(1).Trim()
                                let parts = rest.Split([| ' '; '\t'; '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
                                if parts.Length = 0 then TComment
                                else TInverted(parts.[0], String.Join(" ", parts.[1..]))
                            elif raw.StartsWith("else if", StringComparison.Ordinal) then
                                TElseIf(raw.Substring(8).Trim())
                            elif raw = "else" then TElse
                            elif raw.StartsWith("&") then
                                TExpr(raw.Substring(1).Trim(), true)
                            else TExpr(raw, triple)
                        tokens.Add(classify)
                        i <- closeIdx + closeLen
            else
                sb.Append(c) |> ignore
                i <- i + 1
        flushText ()
        tokens |> Seq.toList

    // ── Recursive-descent parser ───────────────────────────────────────
    // Builds the AST. parseNode handles one token; blocks recurse.
    let rec parseBodyUntilElse (tokens: HbsToken list) : HbsNode list * HbsToken list * bool =
        // returns (nodes, rest, hitElse) — stops at TElse/TElseIf/TBlockClose
        let rec go acc rest =
            match rest with
            | [] -> List.rev acc, [], false
            | TElse :: _ -> List.rev acc, rest, true
            | TElseIf _ :: _ -> List.rev acc, rest, true
            | TBlockClose _ :: _ -> List.rev acc, rest, false
            | token :: tail ->
                match parseNode token tail with
                | node, rest' -> go (node :: acc) rest'
        go [] tokens

    and parseBodyUntilClose (name: string) (tokens: HbsToken list) : HbsNode list * HbsToken list =
        let rec go acc rest =
            match rest with
            | [] -> List.rev acc, []
            | TBlockClose n :: tail when n = name -> List.rev acc, tail
            | token :: tail ->
                match parseNode token tail with
                | node, rest' -> go (node :: acc) rest'
        go [] tokens

    and parseNode (token: HbsToken) (rest: HbsToken list) : HbsNode * HbsToken list =
        match token with
        | TText t -> NText t, rest
        | TExpr(e, t) -> NExpr(e, t), rest
        | TComment -> NText "", rest
        | TPartial(name, args) -> NPartial(name, args), rest
        | TBlockOpen(name, args) ->
            let body, afterBody, hitElse = parseBodyUntilElse rest
            if not hitElse then
                match afterBody with
                | TBlockClose n :: tail when n = name ->
                    NBlock(name, args, body, None), tail
                | _ -> NBlock(name, args, body, None), afterBody
            else
                // collect else / else-if chain
                let rec collectElseChain (nodes: HbsNode list) (toks: HbsToken list) : HbsNode list * HbsToken list =
                    match toks with
                    | TElse :: tail ->
                        let eb, after = parseBodyUntilClose name tail
                        List.rev (List.rev nodes @ eb), after
                    | TElseIf a :: tail ->
                        let eb, after = parseBodyUntilClose name tail
                        let inner = NBlock("if", a, eb, None)
                        collectElseChain (inner :: nodes) after
                    | TBlockClose n :: tail when n = name ->
                        List.rev nodes, tail
                    | TBlockClose n :: tail ->
                        List.rev nodes, (TBlockClose n :: tail)
                    | [] -> List.rev nodes, []
                    | _ :: tail -> collectElseChain nodes tail
                let elseBody, tail = collectElseChain [] afterBody
                if elseBody.IsEmpty then NBlock(name, args, body, None), tail
                else NBlock(name, args, body, Some elseBody), tail
        | TInverted(name, args) ->
            let body, afterBody, _ = parseBodyUntilElse rest
            match afterBody with
            | TBlockClose n :: tail when n = name -> NInverted(name, args, body), tail
            | _ -> NInverted(name, args, body), afterBody
        | TElse | TElseIf _ | TBlockClose _ -> NText "", rest

    // ── Parsed AST cache ───────────────────────────────────────────────
    // A layout rendered once per page (or shared across pages) should parse
    // once per distinct source. Keyed by content hash via TemplateUtils.
    let private astCache = ConcurrentDictionary<int64, HbsNode list>()

    let parse (src: string) : HbsNode list =
        if isNull src then []
        else
            let key = TemplateUtils.hashSource src
            match astCache.TryGetValue key with
            | true, a -> a
            | _ ->
                let tokens = tokenize src
                let rec go acc rest =
                    match rest with
                    | [] -> List.rev acc
                    | token :: tail ->
                        match parseNode token tail with
                        | node, rest' -> go (node :: acc) rest'
                let ast = go [] tokens
                astCache.[key] <- ast
                ast

    // ── Runtime ────────────────────────────────────────────────────────
    let private isFalsey (v: obj) =
        match v with
        | null -> true
        | :? bool as b -> not b
        | :? string as s -> s = ""
        | :? double as d -> d = 0.0
        | :? int as i -> i = 0
        | :? int64 as i -> i = 0L
        | :? System.Collections.IEnumerable as e when not (v :? string) && not (v :? System.Collections.IDictionary) ->
            // empty collection → falsey; non-empty → truthy
            let en = e.GetEnumerator()
            if en.MoveNext() then false else true
        | _ -> false

    let private isTruthy (v: obj) = not (isFalsey v)

    /// Resolve a dotted path (and `../`, `this`, `.`, `@root`) against a value.
    let rec private lookupPath (path: string) (current: obj) (root: obj) (idx: Map<string, obj>) : obj =
        if isNull path then null
        else
            let mutable v = current
            // `../` walks up handled by caller (Stack); here only root/idx/base
            if path = "this" || path = "." then v
            elif path.StartsWith("@root") then
                let rest = if path.Length > 5 then path.Substring(6).TrimStart('.') else ""
                if rest = "" then root else lookupPath rest root root Map.empty
            elif path.StartsWith("@") then
                match idx.TryFind path with
                | Some x -> x
                | None -> null
            else
                let parts = path.Split('.')
                let mutable failed = false
                for p in parts do
                    if not failed && v <> null then
                        v <- getProp p v
                        if isNull v then failed <- true
                if failed then null else v

    and getProp (name: string) (v: obj) : obj =
        match v with
        | null -> null
        | :? IDictionary<string, obj> as d ->
            match d.TryGetValue name with
            | true, x -> x
            | _ -> null
        | :? System.Collections.IDictionary as d ->
            if d.Contains name then d.[name] else null
        | :? System.Collections.IEnumerable as e when not (v :? string) ->
            match Int32.TryParse name with
            | true, i when i >= 0 ->
                let mutable n = 0
                let mutable result = null
                let en = e.GetEnumerator()
                while n <= i && en.MoveNext() do
                    if n = i then result <- en.Current
                    n <- n + 1
                result
            | _ -> null
        | _ ->
            let t = v.GetType()
            let p = t.GetProperty(name, BindingFlags.Public ||| BindingFlags.Instance ||| BindingFlags.IgnoreCase)
            if p <> null && p.CanRead then p.GetValue(v) else null

    /// Split helper args like `items` or `a b='x' c=3` into (positional, named).
    let private parseArgs (args: string) : string list * (string * obj) list =
        if String.IsNullOrWhiteSpace args then [], []
        else
            let positional = ResizeArray<string>()
            let named = ResizeArray<string * obj>()
            let parts = args.Split([| ' '; '\t'; '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
            for p in parts do
                let eq = p.IndexOf('=')
                if eq > 0 then
                    let k = p.Substring(0, eq)
                    let raw = p.Substring(eq + 1)
                    let value: obj =
                        if raw.Length >= 2 && ((raw.[0] = '"' && raw.[raw.Length - 1] = '"') || (raw.[0] = '\'' && raw.[raw.Length - 1] = '\'')) then
                            raw.Substring(1, raw.Length - 2) :> obj
                        else
                            match Double.TryParse raw with
                            | true, d -> d :> obj
                            | _ -> raw :> obj
                    named.Add(k, value)
                else positional.Add p
            positional |> Seq.toList, named |> Seq.toList

    /// Runtime environment for a single render.
    type RenderEnv = {
        /// Context stack; head is current context.
        Stack: obj list
        Root: obj
        Vars: IDictionary<string, obj>
        /// Loop metadata: @index / @key / @first / @last
        Meta: Map<string, obj>
        /// Partial loader: name → source text
        LoadPartial: string -> string option
    }

    /// Parsed partials shared across every render of every Hbs engine instance.
    /// Hoisted out of RenderEnv so a template that is rendered many times (e.g.
    /// a cached layout reused across pages) parses each partial only once.
    let private partialCache = ConcurrentDictionary<string, HbsNode list>()

    /// Clear cached ASTs and parsed partials (called on engine cache clear).
    let clearCaches () =
        astCache.Clear()
        partialCache.Clear()

    let private mkEnv (vars: IDictionary<string, obj>) (loadPartial: string -> string option) : RenderEnv =
        let root =
            match vars.TryGetValue "@root" with
            | true, r -> r
            | _ -> box vars
        { Stack = [ box vars ]; Root = root; Vars = vars; Meta = Map.empty
          LoadPartial = loadPartial }

    let private resolveExpr (env: RenderEnv) (expr: string) : obj =
        let e = expr.Trim()
        if e = "" then null
        elif e.StartsWith("../") then
            // walk up the stack
            let upCount = e |> Seq.takeWhile ((=) '.') |> Seq.length |> fun n -> n / 2
            let path = e.Substring(upCount * 3)
            let rec goUp n st =
                if n <= 0 || List.isEmpty st then st
                else goUp (n - 1) (List.tail st)
            match goUp upCount env.Stack with
            | [] -> null
            | cur :: _ -> lookupPath path cur env.Root env.Meta
        else
            let cur = match env.Stack with c :: _ -> c | [] -> box env.Vars
            lookupPath e cur env.Root env.Meta

    let rec private renderNodes (nodes: HbsNode list) (env: RenderEnv) (sb: Text.StringBuilder) : unit =
        for node in nodes do
            renderNode node env sb

    and private renderNode (node: HbsNode) (env: RenderEnv) (sb: Text.StringBuilder) : unit =
        match node with
        | NText t -> sb.Append(t) |> ignore
        | NExpr(expr, triple) ->
            let v = resolveExpr env expr
            if v <> null then
                let s = v.ToString()
                if triple then sb.Append(s) |> ignore
                else sb.Append(htmlEncode s) |> ignore
        | NPartial(name, args) ->
            let pos, named = parseArgs args
            match env.LoadPartial name with
            | Some src ->
                let ast =
                    match partialCache.TryGetValue name with
                    | true, a -> a
                    | _ ->
                        let a = parse src
                        partialCache.[name] <- a
                        a
                // Handlebars partial arguments: a positional value becomes the
                // partial's context; named args are merged into a new layer.
                let env' =
                    if pos.IsEmpty && named.IsEmpty then env
                    elif named.IsEmpty then
                        let v = resolveExpr env pos.Head
                        if isNull v then env else { env with Stack = v :: env.Stack }
                    else
                        let merged = Dictionary<string, obj>()
                        match env.Stack with
                        | cur :: _ ->
                            match cur with
                            | :? IDictionary<string, obj> as d ->
                                for kv in d do merged.[kv.Key] <- kv.Value
                            | _ -> ()
                        | _ -> ()
                        for k, v in named do merged.[k] <- v
                        { env with Stack = (box merged) :: env.Stack }
                renderNodes ast env' sb
            | None -> () // missing partial → render nothing
        | NInverted(name, args, body) ->
            let v = resolveExpr env name
            let falsey =
                match v with
                | :? System.Collections.IEnumerable as e when not (v :? string) && not (v :? System.Collections.IDictionary) ->
                    let en = e.GetEnumerator()
                    not (en.MoveNext())
                | _ -> isFalsey v
            if falsey then renderNodes body env sb
            else let _ = args in ()
        | NBlock(name, args, body, elseBody) ->
            match name with
            | "if" ->
                let cond = resolveExpr env (args.Trim())
                if isTruthy cond then renderNodes body env sb
                else elseBody |> Option.iter (fun eb -> renderNodes eb env sb)
            | "unless" ->
                let cond = resolveExpr env (args.Trim())
                if isFalsey cond then renderNodes body env sb
                else elseBody |> Option.iter (fun eb -> renderNodes eb env sb)
            | "with" ->
                let v = resolveExpr env (args.Trim())
                if isTruthy v then
                    let env' = { env with Stack = v :: env.Stack }
                    renderNodes body env' sb
                else elseBody |> Option.iter (fun eb -> renderNodes eb env sb)
            | "each" ->
                let v = resolveExpr env (args.Trim())
                match v with
                | :? System.Collections.IList as list ->
                    renderEachList list body elseBody env sb
                | :? IDictionary<string, obj> as d ->
                    renderEachDict d body elseBody env sb
                | :? System.Collections.IDictionary as d ->
                    renderEachDictObj d body elseBody env sb
                | :? System.Collections.IEnumerable as e when not (v :? string) ->
                    renderEachSeq e body elseBody env sb
                | null ->
                    elseBody |> Option.iter (fun eb -> renderNodes eb env sb)
                | _ ->
                    // Non-iterable value (string/scalar): Handlebars renders the
                    // else block for `each` over a non-array.
                    elseBody |> Option.iter (fun eb -> renderNodes eb env sb)
            | _ ->
                // generic section: array/dict → iterate; truthy object → push context; else → elseBody
                let v = resolveExpr env name
                match v with
                | :? System.Collections.IList as list ->
                    renderEachList list body elseBody env sb
                | :? IDictionary<string, obj> as d ->
                    renderEachDict d body elseBody env sb
                | :? System.Collections.IDictionary as d ->
                    renderEachDictObj d body elseBody env sb
                | :? System.Collections.IEnumerable as e when not (v :? string) ->
                    renderEachSeq e body elseBody env sb
                | null ->
                    elseBody |> Option.iter (fun eb -> renderNodes eb env sb)
                | _ when isTruthy v ->
                    let env' = { env with Stack = v :: env.Stack }
                    renderNodes body env' sb
                | _ ->
                    elseBody |> Option.iter (fun eb -> renderNodes eb env sb)

    // ── Streaming iteration ─────────────────────────────────────────────
    // Collections are never copied into an intermediate ResizeArray: IList
    // (arrays, List<T>) is walked by index so @last/@key stay exact, and a
    // plain IEnumerable is streamed once. Streaming means @last/@key are not
    // available on pure IEnumerable — the cost of knowing them is buffering
    // the whole sequence, which the user explicitly opted out of.
    and renderEachList (list: System.Collections.IList) (body: HbsNode list)
                       (elseBody: HbsNode list option) (env: RenderEnv) (sb: Text.StringBuilder) =
        let count = list.Count
        if count = 0 then
            elseBody |> Option.iter (fun eb -> renderNodes eb env sb)
        else
            for k in 0 .. count - 1 do
                let meta = Map.ofList [ "@index", box k; "@key", box k; "@first", box (k = 0); "@last", box (k = count - 1) ]
                let env' = { env with Stack = list.[k] :: env.Stack; Meta = meta }
                renderNodes body env' sb

    and renderEachSeq (e: System.Collections.IEnumerable) (body: HbsNode list)
                      (elseBody: HbsNode list option) (env: RenderEnv) (sb: Text.StringBuilder) =
        let mutable hasAny = false
        let mutable k = 0
        let mutable first = true
        for x in e do
            hasAny <- true
            let meta = Map.ofList [ "@index", box k; "@key", box k; "@first", box first ]
            first <- false
            let env' = { env with Stack = x :: env.Stack; Meta = meta }
            renderNodes body env' sb
            k <- k + 1
        if not hasAny then
            elseBody |> Option.iter (fun eb -> renderNodes eb env sb)

    and renderEachDict (d: IDictionary<string, obj>) (body: HbsNode list)
                       (elseBody: HbsNode list option) (env: RenderEnv) (sb: Text.StringBuilder) =
        let pairs = d |> Seq.toArray
        if pairs.Length = 0 then
            elseBody |> Option.iter (fun eb -> renderNodes eb env sb)
        else
            for k in 0 .. pairs.Length - 1 do
                let kv = pairs.[k]
                let meta = Map.ofList [ "@index", box k; "@key", box kv.Key; "@first", box (k = 0); "@last", box (k = pairs.Length - 1) ]
                let env' = { env with Stack = kv.Value :: env.Stack; Meta = meta }
                renderNodes body env' sb

    and renderEachDictObj (d: System.Collections.IDictionary) (body: HbsNode list)
                          (elseBody: HbsNode list option) (env: RenderEnv) (sb: Text.StringBuilder) =
        let keys = d.Keys |> Seq.cast<obj> |> Seq.toArray
        if keys.Length = 0 then
            elseBody |> Option.iter (fun eb -> renderNodes eb env sb)
        else
            for k in 0 .. keys.Length - 1 do
                let key = keys.[k]
                let meta = Map.ofList [ "@index", box k; "@key", key; "@first", box (k = 0); "@last", box (k = keys.Length - 1) ]
                let env' = { env with Stack = d.[key] :: env.Stack; Meta = meta }
                renderNodes body env' sb

    let render (src: string) (vars: IDictionary<string, obj>) (loadPartial: string -> string option) : Result<string, TemplateError> =
        try
            let env = mkEnv vars loadPartial
            let ast = parse src
            let sb = Text.StringBuilder(src.Length + 64)
            renderNodes ast env sb
            Ok(sb.ToString())
        with ex ->
            Error(TemplateError.RuntimeError(ex.Message, 0))

// ── Public engine class ─────────────────────────────────────────────────
type HbsEngine() =

    let fileCache = ConcurrentDictionary<string, struct(DateTime * string)>()
    // Default loader confines every read to the working directory so a
    // malicious {% include %} cannot traverse outside the site root.
    let mutable loadFileFn: string -> Result<string, string> = fun path ->
        match TemplateUtils.resolveWithinRoot path with
        | Ok fullPath ->
            try Ok(File.ReadAllText(fullPath))
            with :? FileNotFoundException -> Error(sprintf "Template not found: %s" fullPath)
               | ex -> Error(ex.Message)
        | Error e -> Error e

    let mutable partialLoader: string -> string option = fun _ -> None

    member _.SetLoadFile(fn: string -> Result<string, string>) = loadFileFn <- fn
    member _.SetPartialLoader(fn: string -> string option) = partialLoader <- fn

    interface ITemplateEngine with
        member _.Name = "hbs"

        member _.Render(templateText: string) (variables: IDictionary<string, obj>) : Result<string, TemplateError> =
            HbsImpl.render templateText variables partialLoader

        member _.RenderFile(filePath: string) (variables: IDictionary<string, obj>) : Result<string, TemplateError> =
            try
                let text =
                    match fileCache.TryGetValue filePath with
                    | true, struct(mtime, cached) when mtime = File.GetLastWriteTimeUtc(filePath) -> cached
                    | _ ->
                        let t = File.ReadAllText(filePath)
                        fileCache.[filePath] <- struct(File.GetLastWriteTimeUtc(filePath), t)
                        t
                HbsImpl.render text variables partialLoader
            with :? FileNotFoundException -> Error(TemplateError.NotFound filePath)
               | ex -> Error(TemplateError.RuntimeError(ex.Message, 0))

        member _.RegisterFilter(_name: string) (_fn: FilterFn) = () // Hbs has no filters

        member _.RegisterTag(_handler: TagHandler) = ()

        member _.ClearCache() =
            fileCache.Clear()
            HbsImpl.clearCaches()
