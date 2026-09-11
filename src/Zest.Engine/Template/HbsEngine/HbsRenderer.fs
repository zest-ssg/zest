namespace Zest.Engine.Template

open System
open System.Collections.Concurrent
open System.Collections.Generic
open HbsTypes
open HbsParser
open HbsContext

// HbsRenderer.fs
//
// Walks a Handlebars AST and emits rendered output. Implements built-in
// helpers (if/unless/with/each), inverted sections, partials, and loop
// metadata. Streams collections without buffering where possible.

module internal HbsRenderer =

    /// Parsed partials shared across every render of every Hbs engine instance.
    /// Hoisted out of RenderEnv so a template that is rendered many times (e.g.
    /// a cached layout reused across pages) parses each partial only once.
    let private partialCache = ConcurrentDictionary<string, HbsNode list>()

    let private mkEnv (vars: IDictionary<string, obj>) (loadPartial: string -> string option) : RenderEnv =
        let root =
            match vars.TryGetValue "@root" with
            | true, r -> r
            | _ -> box vars
        { Stack = [ box vars ]; Root = root; Vars = vars; Meta = Map.empty
          LoadPartial = loadPartial }

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

    /// Clear cached ASTs and parsed partials (called on engine cache clear).
    let clearCaches () =
        HbsParser.clearAstCache()
        partialCache.Clear()
