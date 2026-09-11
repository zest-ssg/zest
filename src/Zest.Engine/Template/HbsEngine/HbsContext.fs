namespace Zest.Engine.Template

open System
open System.Collections.Generic
open System.Reflection
open System.Text
open HbsTypes

// HbsContext.fs
//
// Resolves Handlebars values: HTML escaping, truthiness rules, dotted-path
// lookups, `@`-data variables (including `@../` parent frames), quoted
// argument parsing, helper dispatch, and expression resolution against the
// per-render context stack.

module internal HbsContext =

    // ── HTML escaping (Mustache `{{ }}` is HTML-escaped by default) ──
    let htmlEncode (s: string) =
        if isNull s then "" else
        s.Replace("&", "&amp;")
         .Replace("<", "&lt;")
         .Replace(">", "&gt;")
         .Replace("\"", "&quot;")
         .Replace("'", "&#39;")

    let isFalsey (v: obj) =
        match v with
        | null -> true
        | :? bool as b -> not b
        | :? string as s -> s = ""
        | :? double as d -> d = 0.0
        | :? int as i -> i = 0
        | :? int64 as i -> i = 0L
        | :? System.Collections.IEnumerable as e when not (v :? string) && not (v :? System.Collections.IDictionary) ->
            let en = e.GetEnumerator()
            if en.MoveNext() then false else true
        | _ -> false

    let isTruthy (v: obj) = not (isFalsey v)

    /// Resolve a property on a value: dictionary key, list index, or POCO property.
    let rec getProp (name: string) (v: obj) : obj =
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

    /// Split helper/partial args like `items` or `a b='x y' c=3` into
    /// (positional, named). Quoted values keep interior whitespace; bare
    /// named values are coerced to number/bool/string literals.
    let parseArgs (args: string) : string list * (string * obj) list =
        if String.IsNullOrWhiteSpace args then [], []
        else
            let positional = ResizeArray<string>()
            let named = ResizeArray<string * obj>()
            let n = args.Length
            let mutable i = 0

            // Read a quoted run starting at args.[i] (which must be the quote char).
            let readQuoted (q: char) : string =
                i <- i + 1
                let sb = StringBuilder()
                let mutable closed = false
                while i < n && not closed do
                    let c = args.[i]
                    if c = '\\' && i + 1 < n then
                        sb.Append(args.[i + 1]) |> ignore
                        i <- i + 2
                    elif c = q then
                        closed <- true
                        i <- i + 1
                    else
                        sb.Append(c) |> ignore
                        i <- i + 1
                sb.ToString()

            let coerceLiteral (raw: string) : obj =
                match Double.TryParse raw with
                | true, d -> box d
                | _ ->
                    match raw.ToLowerInvariant() with
                    | "true" -> box true
                    | "false" -> box false
                    | "null" -> null
                    | _ -> box raw

            while i < n do
                while i < n && Char.IsWhiteSpace args.[i] do i <- i + 1
                if i < n then
                    if args.[i] = '"' || args.[i] = '\'' then
                        positional.Add(readQuoted args.[i])
                    else
                        // Read a bare name; stop at whitespace or `=`.
                        let nameSb = StringBuilder()
                        let mutable hasEq = false
                        while i < n && not (Char.IsWhiteSpace args.[i]) && args.[i] <> '=' do
                            nameSb.Append(args.[i]) |> ignore
                            i <- i + 1
                        if i < n && args.[i] = '=' then
                            hasEq <- true
                            i <- i + 1
                        let name = nameSb.ToString()
                        if hasEq then
                            while i < n && Char.IsWhiteSpace args.[i] do i <- i + 1
                            let value =
                                if i < n && (args.[i] = '"' || args.[i] = '\'') then
                                    box (readQuoted args.[i])
                                else
                                    let vb = StringBuilder()
                                    while i < n && not (Char.IsWhiteSpace args.[i]) do
                                        vb.Append(args.[i]) |> ignore
                                        i <- i + 1
                                    coerceLiteral (vb.ToString())
                            named.Add(name, value)
                        elif name <> "" then
                            positional.Add name
            positional |> Seq.toList, named |> Seq.toList

    /// Resolve a plain dotted path against a starting value.
    let rec private lookupDots (path: string) (v: obj) : obj =
        let parts = path.Split('.')
        let mutable cur = v
        let mutable ok = true
        for part in parts do
            if ok && not (isNull cur) then
                cur <- getProp part cur
                if isNull cur then ok <- false
        if ok then cur else null

    /// Resolve an `@`-data path (`@index`, `@../index`) against the data stack.
    let rec private resolveData (path: string) (data: Map<string, obj> list) : obj =
        let mutable frames = data
        let mutable key = path
        while key.StartsWith("../") do
            key <- key.Substring(3)
            match frames with
            | _ :: tail -> frames <- tail
            | [] -> frames <- []
        match frames with
        | frame :: _ ->
            match frame.TryFind key with
            | Some v -> v
            | None -> null
        | [] -> null

    /// Resolve a block-parameter name (first path segment) against param frames.
    let private resolveParam (p: string) (paramsFrames: Map<string, obj> list) : obj option =
        let dot = p.IndexOf('.')
        let first = if dot >= 0 then p.[..dot - 1] else p
        let rec find (frames: Map<string, obj> list) : obj option =
            match frames with
            | frame :: tail ->
                match frame.TryFind first with
                | Some v ->
                    if dot >= 0 then Some (lookupDots (p.Substring(dot + 1)) v)
                    else Some v
                | None -> find tail
            | [] -> None
        find paramsFrames

    /// Invoke a registered helper if the expression's leading word names one.
    let rec private tryInvokeHelper (env: RenderEnv) (expr: string) : obj option =
        let e = expr.Trim()
        if e = "" then None
        else
            let mutable i = 0
            while i < e.Length && (Char.IsLetterOrDigit e.[i] || e.[i] = '_') do i <- i + 1
            let name = if i > 0 then e.Substring(0, i) else ""
            match env.Helpers.TryFind name with
            | None -> None
            | Some fn ->
                let rest = e.Substring(name.Length).Trim()
                let pos, named = parseArgs rest
                let posVals = pos |> List.map (fun p -> resolveExpr env p)
                let namedMap = named |> Map.ofList
                Some (fn posVals namedMap)

    /// Resolve a path expression against the current context (no helper dispatch).
    and private resolvePath (p: string) (current: obj) (env: RenderEnv) : obj =
        if p = "this" || p = "." then current
        elif p = "@root" then env.Root
        elif p.StartsWith("@root.") then lookupDots (p.Substring(6)) env.Root
        elif p.StartsWith("@") then resolveData (p.Substring(1)) env.Data
        elif p.StartsWith("../") then
            let upCount = (p |> Seq.takeWhile ((=) '.') |> Seq.length) / 2
            let rest = p.Substring(upCount * 3)
            let rec goUp n st =
                if n <= 0 || List.isEmpty st then st
                else goUp (n - 1) (List.tail st)
            match goUp upCount env.Stack with
            | c :: _ -> if rest = "" then c else lookupDots rest c
            | [] -> null
        else
            match resolveParam p env.Params with
            | Some v -> v
            | None -> lookupDots p current

    /// <summary>
    /// Resolve a full Handlebars expression: subexpressions `(...)`, helper
    /// calls, `@`-data variables, and dotted paths.
    /// </summary>
    and resolveExpr (env: RenderEnv) (expr: string) : obj =
        let e = expr.Trim()
        if e = "" then null
        elif e.StartsWith("(") && e.EndsWith(")") then
            resolveExpr env (e.Substring(1, e.Length - 2).Trim())
        else
            match tryInvokeHelper env e with
            | Some v -> v
            | None ->
                let cur = match env.Stack with c :: _ -> c | [] -> box env.Vars
                resolvePath e cur env
