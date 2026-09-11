namespace Zest.Engine.Template

open System
open System.Collections.Generic
open System.Reflection
open HbsTypes

// HbsContext.fs
//
// Resolves Handlebars values: HTML escaping, truthiness rules, dotted-path
// lookups, partial argument parsing, and expression resolution against the
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
            // empty collection → falsey; non-empty → truthy
            let en = e.GetEnumerator()
            if en.MoveNext() then false else true
        | _ -> false

    let isTruthy (v: obj) = not (isFalsey v)

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
    let parseArgs (args: string) : string list * (string * obj) list =
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

    let resolveExpr (env: RenderEnv) (expr: string) : obj =
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
