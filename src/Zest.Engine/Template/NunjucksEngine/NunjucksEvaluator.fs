namespace Zest.Engine.Template

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Globalization
open System.Reflection
open System.Text
open System.Text.RegularExpressions
open NunjucksTypes
open NunjucksCompiler

// NunjucksEvaluator.fs
//
// Evaluates compiled Nunjucks expressions against a render context and
// implements the standard Nunjucks/Jinja2 filter set. This module owns every
// runtime helper, the custom-filter registry, and the compiled-expression cache.
//
// Invariant: evaluation is side-effect free except for loop.changed bookkeeping
// stored inside the per-iteration loop dictionary.

module internal NunjucksEvaluator =

    // ── Custom filter registry (extensible by Zest engine) ──
    // ConcurrentDictionary: filter registration may race with rendering under
    // multi-threaded web servers, so a plain Dictionary is unsafe here.
    let customFilters = ConcurrentDictionary<string, FilterFn>()

    // ── Reflection cache for POCO property access ──
    let private propCache = ConcurrentDictionary<string, PropertyInfo>()

    // ── Precompiled regexes (avoid recompiling on every filter call) ──
    let private reTitle   = Regex(@"\b\w", RegexOptions.Compiled)
    let private reTags    = Regex(@"<[^>]+>", RegexOptions.Compiled)
    let private reSlug    = Regex(@"[^a-z0-9]+", RegexOptions.Compiled)
    let private reIndent  = Regex(@"^", RegexOptions.Compiled ||| RegexOptions.Multiline)
    let private reUrl     = Regex(@"(https?://[^\s<>""']+)", RegexOptions.Compiled)

    // ── strftime → .NET format conversion ──────────────────
    // The `date` filter accepts both C strftime tokens (%Y-%m-%d)
    // and .NET tokens (yyyy-MM-dd). When `%` is present, translate.
    let private strftimeToDotnet (fmt: string) : string =
        let sb = StringBuilder()
        let n = fmt.Length
        let mutable i = 0
        while i < n do
            if fmt.[i] = '%' && i + 1 < n then
                let tok = fmt.Substring(i + 1, 1)
                let mapped =
                    match tok with
                    | "Y" -> "yyyy" | "y" -> "yy"
                    | "m" -> "MM"   | "b" -> "MMM" | "B" -> "MMMM"
                    | "d" -> "dd"   | "e" -> "d"
                    | "a" -> "ddd"  | "A" -> "dddd"
                    | "H" -> "HH"   | "I" -> "hh"
                    | "M" -> "mm"   | "S" -> "ss"
                    | "p" -> "tt"   | "z" -> "zzz" | "Z" -> "zzz"
                    | "%" -> "%"
                    | t -> "%" + t   // unknown token → keep literal
                sb.Append(mapped) |> ignore
                i <- i + 2
            else
                sb.Append(fmt.[i]) |> ignore
                i <- i + 1
        sb.ToString()

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

    /// Evaluate a compiled expression tree against the render context.
    let rec evalC (e: CExpr) (ctx: IDictionary<string, obj>) : obj =
        match e with
        | CLit v -> v
        | CPath p -> resolvePath p ctx
        | CParen inner -> evalC inner ctx
        | CNotE inner -> box (not (toBool (evalC inner ctx)))
        | CUnary(op, inner) ->
            let n = toNum (evalC inner ctx)
            box (if op = "-" then -n else n)
        | CIf(thenE, condE, elseE) ->
            if toBool (evalC condE ctx) then evalC thenE ctx else evalC elseE ctx
        | CBin("or", l, r) ->
            let lv = evalC l ctx
            if toBool lv then box true else box (toBool (evalC r ctx))
        | CBin("and", l, r) ->
            let lv = evalC l ctx
            if not (toBool lv) then box false else box (toBool (evalC r ctx))
        | CBin(op, l, r) ->
            let lv = evalC l ctx
            let rv = evalC r ctx
            match op with
            | "==" -> box (valuesEqual lv rv)
            | "!=" -> box (not (valuesEqual lv rv))
            | ">"  -> box (toNum lv >  toNum rv)
            | "<"  -> box (toNum lv <  toNum rv)
            | ">=" -> box (toNum lv >= toNum rv)
            | "<=" -> box (toNum lv <= toNum rv)
            | "in" ->
                let found = seqOf rv |> Seq.exists (fun x -> valuesEqual x lv)
                let strContains = match rv with :? string as sv -> sv.Contains(toStr lv) | _ -> false
                box (found || strContains)
            | "not in" ->
                let found = seqOf rv |> Seq.exists (fun x -> valuesEqual x lv)
                let strContains = match rv with :? string as sv -> sv.Contains(toStr lv) | _ -> false
                box (not (found || strContains))
            | "is" -> box (applyIsTest (toStr rv) lv)
            | "is not" -> box (not (applyIsTest (toStr rv) lv))
            | "~" -> box (toStr lv + toStr rv)
            | "+" ->
                if (match lv, rv with
                    | (:? string as ls), _ when Double.IsNaN(toNum ls) -> true
                    | _, (:? string as rs) when Double.IsNaN(toNum rs) -> true
                    | _ -> false)
                then box (toStr lv + toStr rv)
                else box (toNum lv + toNum rv)
            | "-" -> box (toNum lv - toNum rv)
            | "*" -> box (toNum lv * toNum rv)
            | "/" -> let r = toNum rv in box (if r = 0.0 then 0.0 else toNum lv / r)
            | "%" -> let r = toNum rv in box (if r = 0.0 then 0.0 else toNum lv % r)
            | "**" -> box (Math.Pow(toNum lv, toNum rv))
            | _ -> null
        | CRange args ->
            let toI (v: obj) = match v with :? int as i -> i | _ -> int(toNum v)
            match args |> List.map (fun a -> evalC a ctx) with
            | [stop] ->
                [| for i in 0 .. toI stop - 1 -> box i |] :> obj
            | [start; stop] ->
                [| for i in toI start .. toI stop - 1 -> box i |] :> obj
            | [start; stop; step] ->
                let s = toI start
                let e = toI stop
                let st = toI step
                if st = 0 then [||] :> obj
                else
                    [| let mutable i = s
                       while (if st > 0 then i < e else i > e) do
                           yield box i
                           i <- i + st |] :> obj
            | _ -> [||] :> obj
        | CCall(name, args) ->
            let vals = args |> List.map (fun a -> evalC a ctx)
            match name with
            | "loop.cycle" ->
                match ctx.TryGetValue "loop" with
                | true, (:? IDictionary<string, obj> as ld) ->
                    let idx0 = match ld.TryGetValue "index0" with true, v -> (try int(toStr v) with _ -> 0) | _ -> 0
                    if vals.Length > 0 then box vals.[idx0 % vals.Length] else box ""
                | _ -> box ""
            | "loop.changed" ->
                match ctx.TryGetValue "loop" with
                | true, (:? IDictionary<string, obj> as ld) ->
                    let now = if vals.Length > 0 then vals.Head else box ""
                    match ld.TryGetValue "__changed__" with
                    | true, prev when valuesEqual prev now -> box false
                    | _ ->
                        ld.["__changed__"] <- now
                        box true
                | _ -> box false
            | _ -> null
        | CPipeE(baseExpr, chain) ->
            let mutable result = evalC baseExpr ctx
            for (fname, fargs) in chain do
                let argVals = fargs |> List.map (fun a -> evalC a ctx)
                result <- applyFilter fname result argVals
            result

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
        // semantics (truncate toward zero).
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
                let rev = args.Length > 1 && toBool args.[1]
                let sorted =
                    if attr = "" then
                        ie |> Seq.cast<obj> |> Seq.sortBy (fun x -> toStr x)
                    else
                        ie |> Seq.cast<obj> |> Seq.sortBy (fun x -> toStr(propGet x attr))
                let arr = sorted |> Array.ofSeq
                if rev then Array.rev arr :> obj else arr :> obj
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
        | "selectattr" | "rejectattr" ->
            let attr = if args.Length > 0 then toStr args.[0] else ""
            let test = if args.Length > 1 then toStr args.[1] else "truthy"
            let testArg = if args.Length > 2 then args.[2] else null
            let reject = name = "rejectattr"
            let knownTests = set ["truthy"; "falsy"; "defined"; "undefined"; "number"; "string";
                                  "iterable"; "empty"; "odd"; "even"; "equalto"; "eq";
                                  "not_equalto"; "ne"; "contains"]
            let passes (v: obj) =
                if knownTests.Contains(test.ToLowerInvariant()) then applyValueTest test v testArg
                else toStr v = test
            match value with
            | :? System.Collections.IEnumerable as ie ->
                ie |> Seq.cast<obj>
                |> Seq.filter (fun x -> let r = passes (propGet x attr) in if reject then not r else r)
                |> Array.ofSeq :> obj
            | _ -> value
        | "select" | "reject" ->
            let test = if args.Length > 0 then toStr args.[0] else "truthy"
            let testArg = if args.Length > 1 then args.[1] else null
            let reject = name = "reject"
            match value with
            | :? System.Collections.IEnumerable as ie ->
                ie |> Seq.cast<obj>
                |> Seq.filter (fun x -> let r = applyValueTest test x testArg in if reject then not r else r)
                |> Array.ofSeq :> obj
            | _ -> value
        | "compact" ->
            match value with
            | :? System.Collections.IEnumerable as ie ->
                ie |> Seq.cast<obj>
                |> Seq.filter (fun x ->
                    not (isNull x)
                    && not (match x with :? string as s -> s = "" | _ -> false))
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

        // Date filter — accepts strftime tokens (%Y-%m-%d) or .NET tokens
        | "date" ->
            let rawFmt = if args.Length > 0 then toStr args.[0] else "yyyy-MM-dd"
            let fmt = if rawFmt.Contains("%") then strftimeToDotnet rawFmt else rawFmt
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

    /// Apply a Jinja-style `is` test name to a value (x is defined, x is empty).
    /// Supports a parenthesized argument for `divisibleby(n)`. The argument is
    /// a literal; expression arguments such as `sameas(x)` are not resolved
    /// because the compiler keeps the test name as text.
    and applyIsTest (test: string) (v: obj) : bool =
        let raw = test.Trim()
        let name, argOpt =
            let p = raw.IndexOf('(')
            if p > 0 && raw.EndsWith(")") then
                raw.[..p-1].Trim().ToLowerInvariant(), Some(raw.[p+1..raw.Length-2].Trim())
            else raw.ToLowerInvariant(), None
        match name with
        | "defined" -> v <> null
        | "undefined" | "none" | "null" -> isNull v
        | "truthy" -> toBool v
        | "falsy" -> not (toBool v)
        | "number" -> match v with :? int | :? int64 | :? double | :? single -> true | _ -> false
        | "integer" -> match v with :? int | :? int64 -> true | _ -> false
        | "float" -> match v with :? double | :? single -> true | _ -> false
        | "boolean" -> v :? bool
        | "string" -> v :? string
        | "lower" -> match v with :? string as s -> s = s.ToLowerInvariant() | _ -> false
        | "upper" -> match v with :? string as s -> s = s.ToUpperInvariant() | _ -> false
        | "mapping" -> match v with :? System.Collections.IDictionary -> true | _ -> false
        | "sequence" ->
            match v with
            | :? System.Collections.IEnumerable when not (v :? string) && not (v :? System.Collections.IDictionary) -> true
            | _ -> false
        | "iterable" -> match v with :? System.Collections.IEnumerable when not (v :? string) -> true | _ -> false
        | "divisibleby" ->
            match argOpt with
            | Some a ->
                match Int32.TryParse a with
                | true, n when n <> 0 -> (try int(toNum v) with _ -> 0) % n = 0
                | _ -> false
            | None -> false
        | "empty" ->
            match v with
            | null -> true
            | :? string as s -> s = ""
            | :? System.Collections.ICollection as c -> c.Count = 0
            | :? System.Collections.IEnumerable as e ->
                let en = e.GetEnumerator()
                try not (en.MoveNext())
                finally match box en with :? IDisposable as d -> d.Dispose() | _ -> ()
            | _ -> false
        | "odd" -> match v with :? int as i -> i % 2 <> 0 | :? int64 as i -> i % 2L <> 0L | _ -> false
        | "even" -> match v with :? int as i -> i % 2 = 0 | :? int64 as i -> i % 2L = 0L | _ -> false
        | _ -> false

    /// Apply a value test for `select` / `reject` / `selectattr` filters.
    /// Supported tests: truthy, falsy, defined, undefined, number, integer,
    /// float, boolean, string, lower, upper, mapping, sequence, iterable,
    /// empty, divisibleby, equalto/eq, not_equalto/ne, contains, odd, even.
    and applyValueTest (test: string) (v: obj) (arg: obj) : bool =
        match test.Trim().ToLowerInvariant() with
        | "truthy" -> toBool v
        | "falsy" -> not (toBool v)
        | "defined" -> v <> null
        | "undefined" -> isNull v
        | "number" -> match v with :? int | :? int64 | :? double | :? single -> true | _ -> false
        | "integer" -> match v with :? int | :? int64 -> true | _ -> false
        | "float" -> match v with :? double | :? single -> true | _ -> false
        | "boolean" -> v :? bool
        | "string" -> v :? string
        | "lower" -> match v with :? string as s -> s = s.ToLowerInvariant() | _ -> false
        | "upper" -> match v with :? string as s -> s = s.ToUpperInvariant() | _ -> false
        | "mapping" -> match v with :? System.Collections.IDictionary -> true | _ -> false
        | "sequence" ->
            match v with
            | :? System.Collections.IEnumerable when not (v :? string) && not (v :? System.Collections.IDictionary) -> true
            | _ -> false
        | "iterable" -> match v with :? System.Collections.IEnumerable when not (v :? string) -> true | _ -> false
        | "empty" -> applyIsTest "empty" v
        | "odd" -> applyIsTest "odd" v
        | "even" -> applyIsTest "even" v
        | "divisibleby" ->
            match Int32.TryParse(toStr arg) with
            | true, n when n <> 0 -> (try int(toNum v) with _ -> 0) % n = 0
            | _ -> false
        | "equalto" | "eq" -> valuesEqual v arg
        | "not_equalto" | "ne" -> not (valuesEqual v arg)
        | "contains" ->
            match v with
            | :? string as s -> s.Contains(toStr arg)
            | :? System.Collections.IEnumerable as e -> seqOf e |> Seq.exists (fun x -> valuesEqual x arg)
            | _ -> false
        | _ -> toStr v = toStr arg

    /// HTML-encode a string for safe output.
    and HtmlEncode (s: string) =
        if String.IsNullOrEmpty s then s
        else
            let sb = StringBuilder(s)
            sb.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;").ToString()

    // ── Compiled expression cache ───────────────────────────
    // Compiled expressions keyed by their exact text. Bounded: cleared when
    // it grows past a few thousand entries, defensively guarding against
    // templates that generate unbounded expression strings.
    let private exprCache = ConcurrentDictionary<string, IDictionary<string, obj> -> obj>()

    let private getCompiled (text: string) =
        match exprCache.TryGetValue text with
        | true, fn -> fn
        | _ ->
            checkBalanced text
            let tree = compileExpr text
            let fn = fun ctx -> evalC tree ctx
            if exprCache.Count > 2048 then exprCache.Clear()
            exprCache.[text] <- fn
            fn

    let evalExpr (exprText: string) (ctx: IDictionary<string, obj>) : obj =
        let text = exprText.Trim()
        if text = "" then box "" else
        (getCompiled text) ctx
