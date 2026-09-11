namespace Zest.Engine.Template

open System
open System.Collections.Concurrent
open System.Collections.Generic
open HbsTypes
open HbsParser
open HbsContext

// HbsRenderer.fs
//
// Walks a Handlebars AST and emits rendered output. Implements built-in block
// helpers (if/unless/with/each), inverted sections, partials (including
// partial blocks and `@partial-block`), block parameters (`as |item index|`),
// `@`-data variables with `@../` parent frames, and user-registered inline
// helpers. Collections are streamed where possible.
//
// Not supported (documented to keep the engine honest): user-defined *block*
// helpers with their own body — only the built-in block helpers and inline
// helpers are wired. Subexpressions are supported for inline helpers, but a
// subexpression used as a hash value (`key=(helper)`) is treated as literal
// text because Handlebars hash values are static literals in practice.

module internal HbsRenderer =

    /// Parsed partials shared across every render of every Hbs engine instance.
    let private partialCache = ConcurrentDictionary<string, HbsNode list>()

    // ── Numeric coercion for comparison helpers ────────────────────────
    let private toNum (v: obj) =
        match v with
        | :? int as i -> float i
        | :? int64 as i -> float i
        | :? double as d -> d
        | :? single as f -> float f
        | :? bool as b -> if b then 1.0 else 0.0
        | :? string as s -> match Double.TryParse s with true, n -> n | _ -> nan
        | null -> 0.0
        | _ -> nan

    let private cmp (a: obj) (b: obj) =
        let na, nb = toNum a, toNum b
        if not (Double.IsNaN na) && not (Double.IsNaN nb) then compare na nb
        else compare (if isNull a then "" else a.ToString()) (if isNull b then "" else b.ToString())

    // ── Built-in inline helpers ────────────────────────────────────────
    // Registered before user helpers so a user helper of the same name wins.
    let private builtinHelpers : (string * HbsHelper) list =
        [ "lookup", (fun pos _ ->
            match pos with
            | container :: key :: _ -> getProp (if isNull key then "" else key.ToString()) container
            | _ -> null)
          "log", (fun _ _ -> null)   // Logging is a side effect; output nothing.
          "eq", (fun pos _ ->
            match pos with
            | a :: b :: _ -> box ((isNull a && isNull b) || (not (isNull a) && a.Equals b))
            | _ -> box false)
          "ne", (fun pos _ ->
            match pos with
            | a :: b :: _ -> box (not ((isNull a && isNull b) || (not (isNull a) && a.Equals b)))
            | _ -> box true)
          "lt", (fun pos _ -> match pos with a :: b :: _ -> box (cmp a b < 0) | _ -> box false)
          "gt", (fun pos _ -> match pos with a :: b :: _ -> box (cmp a b > 0) | _ -> box false)
          "lte", (fun pos _ -> match pos with a :: b :: _ -> box (cmp a b <= 0) | _ -> box false)
          "gte", (fun pos _ -> match pos with a :: b :: _ -> box (cmp a b >= 0) | _ -> box false)
          "and", (fun pos _ -> box (pos |> List.forall isTruthy))
          "or", (fun pos _ -> box (pos |> List.exists isTruthy))
          "not", (fun pos _ -> box (match pos with a :: _ -> not (isTruthy a) | _ -> true)) ]

    let private mkEnv (vars: IDictionary<string, obj>) (helpers: Map<string, HbsHelper>)
                      (loadPartial: string -> string option) : RenderEnv =
        let root =
            match vars.TryGetValue "@root" with
            | true, r -> r
            | _ -> box vars
        let allHelpers = (builtinHelpers @ (helpers |> Map.toList)) |> Map.ofList
        { Stack = [ box vars ]; Root = root; Vars = vars
          Data = []; Params = []; Helpers = allHelpers
          LoadPartial = loadPartial; PartialBlockBody = None }

    /// Bind `as |item index|` names. The first name always binds the current
    /// element; the second binds the loop index (arrays) or key (objects).
    let private bindBlockParams (names: string list) (item: obj) (meta: Map<string, obj>) (secondKey: string) : Map<string, obj> =
        match names with
        | [] -> Map.empty
        | first :: rest ->
            let second = if rest.IsEmpty then None else Some (match meta.TryFind secondKey with Some v -> v | None -> null)
            let pairs = ResizeArray<string * obj>()
            pairs.Add(first, item)
            match second, rest with
            | Some v, name :: _ -> pairs.Add(name, v)
            | _ -> ()
            Map.ofList (List.ofSeq pairs)

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
        | NPartial(name, args) -> renderPartial name args None env sb
        | NPartialBlock(name, args, body) -> renderPartial name args (Some body) env sb
        | NInverted(name, _, _, body) ->
            let v = resolveExpr env name
            let falsey =
                match v with
                | :? System.Collections.IEnumerable as e when not (v :? string) && not (v :? System.Collections.IDictionary) ->
                    let en = e.GetEnumerator()
                    not (en.MoveNext())
                | _ -> isFalsey v
            if falsey then renderNodes body env sb
        | NBlock(name, args, blockParams, body, elseBody) ->
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
                    let paramsFrame =
                        if blockParams.IsEmpty then env.Params
                        else (bindBlockParams blockParams v Map.empty "") :: env.Params
                    let env' = { env with Stack = v :: env.Stack; Params = paramsFrame }
                    renderNodes body env' sb
                else elseBody |> Option.iter (fun eb -> renderNodes eb env sb)
            | "each" ->
                let v = resolveExpr env (args.Trim())
                renderEach v blockParams body elseBody env sb
            | _ ->
                // Generic section: arrays/dicts iterate, a truthy object pushes
                // its context, otherwise the else body renders.
                let v = resolveExpr env name
                match v with
                | :? System.Collections.IList as list -> renderEachList list blockParams body elseBody env sb
                | :? IDictionary<string, obj> as d -> renderEachDict d blockParams body elseBody env sb
                | :? System.Collections.IDictionary as d -> renderEachDictObj d blockParams body elseBody env sb
                | :? System.Collections.IEnumerable as e when not (v :? string) -> renderEachSeq e blockParams body elseBody env sb
                | null -> elseBody |> Option.iter (fun eb -> renderNodes eb env sb)
                | _ when isTruthy v ->
                    let env' = { env with Stack = v :: env.Stack }
                    renderNodes body env' sb
                | _ -> elseBody |> Option.iter (fun eb -> renderNodes eb env sb)

    /// Build a partial's render environment from its positional/hash args.
    and private buildPartialEnv (env: RenderEnv) (pos: string list) (named: (string * obj) list)
                                (blockBody: HbsNode list option) : RenderEnv =
        let baseEnv = { env with PartialBlockBody = blockBody }
        if pos.IsEmpty && named.IsEmpty then baseEnv
        elif named.IsEmpty then
            let v = resolveExpr env pos.Head
            if isNull v then baseEnv else { baseEnv with Stack = v :: env.Stack }
        else
            let merged = Dictionary<string, obj>()
            match env.Stack with
            | cur :: _ ->
                match cur with
                | :? IDictionary<string, obj> as d -> for kv in d do merged.[kv.Key] <- kv.Value
                | _ -> ()
            | _ -> ()
            for k, v in named do merged.[k] <- v
            { baseEnv with Stack = (box merged) :: env.Stack }

    and private renderPartial (name: string) (args: string) (blockBody: HbsNode list option)
                              (env: RenderEnv) (sb: Text.StringBuilder) : unit =
        if name = "@partial-block" then
            match env.PartialBlockBody with
            | Some body -> renderNodes body { env with PartialBlockBody = None } sb
            | None -> ()
        else
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
                renderNodes ast (buildPartialEnv env pos named blockBody) sb
            | None -> ()   // Missing partial renders nothing.

    and renderEach (v: obj) (blockParams: string list) (body: HbsNode list)
                   (elseBody: HbsNode list option) (env: RenderEnv) (sb: Text.StringBuilder) =
        match v with
        | :? System.Collections.IList as list -> renderEachList list blockParams body elseBody env sb
        | :? IDictionary<string, obj> as d -> renderEachDict d blockParams body elseBody env sb
        | :? System.Collections.IDictionary as d -> renderEachDictObj d blockParams body elseBody env sb
        | :? System.Collections.IEnumerable as e when not (v :? string) -> renderEachSeq e blockParams body elseBody env sb
        | _ -> elseBody |> Option.iter (fun eb -> renderNodes eb env sb)

    // ── Streaming iteration ─────────────────────────────────────────────
    // IList (arrays, List<T>) is walked by index so @last/@key stay exact; a
    // plain IEnumerable is streamed once, so @last/@key are unavailable there.
    and renderEachList (list: System.Collections.IList) (blockParams: string list)
                       (body: HbsNode list) (elseBody: HbsNode list option)
                       (env: RenderEnv) (sb: Text.StringBuilder) =
        let count = list.Count
        if count = 0 then
            elseBody |> Option.iter (fun eb -> renderNodes eb env sb)
        else
            for k in 0 .. count - 1 do
                let item = list.[k]
                let meta = Map.ofList [ "@index", box k; "@key", box k; "@first", box (k = 0); "@last", box (k = count - 1) ]
                let env' =
                    { env with
                        Stack = item :: env.Stack
                        Data = meta :: env.Data
                        Params = (bindBlockParams blockParams item meta "@index") :: env.Params }
                renderNodes body env' sb

    and renderEachSeq (e: System.Collections.IEnumerable) (blockParams: string list)
                      (body: HbsNode list) (elseBody: HbsNode list option)
                      (env: RenderEnv) (sb: Text.StringBuilder) =
        let mutable hasAny = false
        let mutable k = 0
        for x in e do
            hasAny <- true
            let meta = Map.ofList [ "@index", box k; "@key", box k; "@first", box (k = 0) ]
            let env' =
                { env with
                    Stack = x :: env.Stack
                    Data = meta :: env.Data
                    Params = (bindBlockParams blockParams x meta "@index") :: env.Params }
            renderNodes body env' sb
            k <- k + 1
        if not hasAny then
            elseBody |> Option.iter (fun eb -> renderNodes eb env sb)

    and renderEachDict (d: IDictionary<string, obj>) (blockParams: string list)
                       (body: HbsNode list) (elseBody: HbsNode list option)
                       (env: RenderEnv) (sb: Text.StringBuilder) =
        let pairs = d |> Seq.toArray
        if pairs.Length = 0 then
            elseBody |> Option.iter (fun eb -> renderNodes eb env sb)
        else
            for k in 0 .. pairs.Length - 1 do
                let kv = pairs.[k]
                let meta = Map.ofList [ "@index", box k; "@key", box kv.Key; "@first", box (k = 0); "@last", box (k = pairs.Length - 1) ]
                let env' =
                    { env with
                        Stack = kv.Value :: env.Stack
                        Data = meta :: env.Data
                        Params = (bindBlockParams blockParams kv.Value meta "@key") :: env.Params }
                renderNodes body env' sb

    and renderEachDictObj (d: System.Collections.IDictionary) (blockParams: string list)
                          (body: HbsNode list) (elseBody: HbsNode list option)
                          (env: RenderEnv) (sb: Text.StringBuilder) =
        let keys = d.Keys |> Seq.cast<obj> |> Seq.toArray
        if keys.Length = 0 then
            elseBody |> Option.iter (fun eb -> renderNodes eb env sb)
        else
            for k in 0 .. keys.Length - 1 do
                let key = keys.[k]
                let value = d.[key]
                let meta = Map.ofList [ "@index", box k; "@key", key; "@first", box (k = 0); "@last", box (k = keys.Length - 1) ]
                let env' =
                    { env with
                        Stack = value :: env.Stack
                        Data = meta :: env.Data
                        Params = (bindBlockParams blockParams value meta "@key") :: env.Params }
                renderNodes body env' sb

    let render (src: string) (vars: IDictionary<string, obj>) (helpers: Map<string, HbsHelper>)
               (loadPartial: string -> string option) : Result<string, TemplateError> =
        try
            let env = mkEnv vars helpers loadPartial
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
