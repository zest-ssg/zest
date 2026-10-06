// DslSugar.fs
//
// Convenience helpers for DSL scripts: conditionals, loops, pipelines,
// shorthand element builders, and i18n lookups (t / tLang). Site data values
// reach these helpers as native CLR objects, so every value is normalised to a
// string via siteString before use.
//
// Dependencies: Zest.Markup (Context), System.Text

namespace Zest.Markup

open System
open System.Text

// ============================================================
// DslSugar — Conditionals, loops, pipelines, and shortcuts
// ============================================================

[<AutoOpen>]
module DslSugar =
    open Dsl

    // ── Implicit yield helpers ───────────────────────────────────

    /// Begin an implicit-yield block with newline separators.
    let yieldBlock (nodes: string list) =
        nodes |> String.concat "\n"

    /// Begin an implicit-yield block without separators.
    let yieldInline (nodes: string list) =
        nodes |> String.concat ""

    // ── Shorthand conditionals ───────────────────────────────────

    /// Ternary-like conditional for strings.
    let inline cond (condition: bool) (ifTrue: string) (ifFalse: string) =
        if condition then ifTrue else ifFalse

    /// Return fallback if value is null/empty.
    let defaultTo (fallback: string) (value: string) =
        if String.IsNullOrEmpty value then fallback else value

    /// Return the first non-null/non-empty value from a list.
    let coalesceStr (values: string list) =
        values |> List.tryFind (fun v -> not (String.IsNullOrEmpty v))
        |> Option.defaultValue ""

    /// Return content only if condition is true.
    let whenTrue (cond: bool) (content: string) =
        if cond then content else ""

    /// Return content only if condition is false.
    let unlessTrue (cond: bool) (content: string) =
        if cond then "" else content

    /// Alias for unlessTrue — return content only if condition is false.
    let whenFalse (cond: bool) (content: string) = unlessTrue cond content

    /// Switch on a string value, return the matching case.
    /// Case-insensitive comparison for convenience.
    let switchValue (value: string) (cases: (string * string) list) (defaultCase: string) =
        cases
        |> List.tryFind (fun (v, _) -> v.Equals(value, StringComparison.OrdinalIgnoreCase))
        |> Option.map snd
        |> Option.defaultValue defaultCase

    /// Match on boolean conditions, return first match.
    let matchCond (cases: (bool * string) list) (fallback: string) =
        cases |> List.tryFind fst |> Option.map snd |> Option.defaultValue fallback

    /// Conditional rendering with else clause — if cond then trueContent else falseContent.
    let ifElse (cond: bool) (trueContent: string) (falseContent: string) =
        if cond then trueContent else falseContent

    // ── Simplified loops and iterators ───────────────────────────

    /// Map over items and join with a separator.
    let eachWith (items: 'a list) (separator: string) (f: 'a -> string) =
        items |> List.map f |> String.concat separator

    /// Map over items and join with newlines.
    let eachLine (items: 'a list) (f: 'a -> string) =
        items |> List.map f |> String.concat "\n"

    /// Map over items and wrap in a container tag.
    let eachInContainer (tag: string) (items: 'a list) (f: 'a -> string) =
        let inner = items |> List.map f |> String.concat ""
        elem tag [] [inner]

    /// Repeat a string N times.
    let repeatStr (count: int) (s: string) =
        StringBuilder().Insert(0, s, count).ToString()

    /// Generate a numbered list of items.
    let numberedList (items: 'a list) (f: int -> 'a -> string) =
        items |> List.mapi (fun i item -> f (i + 1) item) |> String.concat "\n"

    /// For-loop over a range with a render function.
    let forRange (start: int) (endInclusive: int) (f: int -> string) =
        [start..endInclusive] |> List.map f |> String.concat ""

    // ── Pipeline / chaining operators ────────────────────────────

    /// Forward pipe operator.
    let (|>) = (|>)

    /// Backward pipe operator.
    let (<|) = (<|)

    /// Function composition.
    let (>>) = (>>)

    /// Wrap a string in an HTML tag.
    let wrapIn (tag: string) (content: string) =
        sprintf "<%s>%s</%s>" tag content tag

    /// Add a CSS class to an element string.
    let addClass (cls: string) (element: string) =
        let pattern = @"^<(\w+)"
        let m = Text.RegularExpressions.Regex.Match(element, pattern)
        if m.Success then
            let tag = m.Groups.[1].Value
            element.Replace(sprintf "<%s" tag, sprintf "<%s class=\"%s\"" tag cls)
        else element

    /// Alias for `addClass` (`C` ≡ `Class`). `addC` ≡ `addClass`.
    let addC = addClass

    // ── Shorthand element builders ───────────────────────────────
    // `<tag>Text` takes text content; `<tag>TextClass` / `<tag>TextC` take a CSS
    // class plus text content (see CLAUDE.md §3.1.1 R7). Every `TextClass`
    // helper ships with its `TextC` alias.

    /// Create a div with text content.
    let divText (content: string) =
        div [text content]

    /// Create a div with a CSS class and text content.
    let divTextClass (cls: string) (content: string) =
        divClass cls [text content]

    /// Alias for `divTextClass` (`C` ≡ `Class`). `divTextC` ≡ `divTextClass`.
    let divTextC = divTextClass

    /// Create a span with text content.
    let spanText (content: string) =
        span [text content]

    /// Create a span with a CSS class and text content.
    let spanTextClass (cls: string) (content: string) =
        spanClass cls [text content]

    /// Alias for `spanTextClass` (`C` ≡ `Class`).
    let spanTextC = spanTextClass

    /// Create a paragraph with text content.
    let pText (content: string) =
        p [text content]

    /// Create a paragraph with a CSS class and text content.
    let pTextClass (cls: string) (content: string) =
        pClass cls [text content]

    /// Alias for `pTextClass` (`C` ≡ `Class`).
    let pTextC = pTextClass

    /// Create a list item with text content.
    let liText (content: string) =
        li [text content]

    /// Create a list item with a CSS class and text content.
    let liTextClass (cls: string) (content: string) =
        liClass cls [text content]

    /// Alias for `liTextClass` (`C` ≡ `Class`).
    let liTextC = liTextClass

    /// Create a heading with text content. `level` is 1–6.
    let hText (level: int) (content: string) =
        let tag = sprintf "h%d" level
        elem tag [] [text content]

    /// Create a heading with a CSS class and text content.
    let hTextClass (cls: string) (level: int) (content: string) =
        let tag = sprintf "h%d" level
        elem tag [attr "class" cls] [text content]

    /// Alias for `hTextClass` (`C` ≡ `Class`).
    let hTextC = hTextClass

    /// Create a link with text content.
    let aText (url: string) (textContent: string) =
        a url [text textContent]

    /// Create a link with a CSS class and text content.
    let aTextClass (cls: string) (url: string) (textContent: string) =
        aClass cls url [text textContent]

    /// Alias for `aTextClass` (`C` ≡ `Class`). `aTextC` ≡ `aTextClass`.
    let aTextC = aTextClass

    /// Create a ul from items using a render function.
    let ulFrom (items: 'a list) (f: 'a -> string) =
        ul (items |> List.map (fun i -> li [f i]))

    /// Create an ol from items using a render function.
    let olFrom (items: 'a list) (f: 'a -> string) =
        ol (items |> List.map (fun i -> li [f i]))

    // ── Type conversion shortcuts ────────────────────────────────

    /// Convert any value to a string via .ToString().
    let inline str (x: 'a) = x.ToString()

    /// Convert an integer to a string.
    let inline intStr (x: int) = string x

    /// Convert a float to a string with format.
    let floatStr (format: string) (x: float) = x.ToString(format)

    /// Convert a boolean to "true" / "false".
    let boolStr (x: bool) = if x then "true" else "false"

    // ── Option / nullable rendering ─────────────────────────────

    /// Render an `Option<string>`: `Some s` → `s`, `None` → `""`.
    /// Same as `Components.opt` but available in the sugar module.
    let optStr (v: string option) = match v with Some s -> s | None -> ""

    /// Render an `Option<string>` with a fallback for `None`.
    let optOr (fallback: string) (v: string option) =
        match v with Some s when not (String.IsNullOrEmpty s) -> s | _ -> fallback

    /// Apply a render function only when the value is `Some`, else `""`.
    let optMap (f: 'a -> string) (v: 'a option) =
        match v with Some x -> f x | None -> ""

    /// Render content only when the value is `Some`, ignoring the inner value.
    let optWhen (v: 'a option) (content: string) =
        match v with Some _ -> content | None -> ""

    // ── Joining helpers ─────────────────────────────────────────

    /// Join items with newlines (alias for readability in pipelines).
    let joinLines (items: string list) = String.concat "\n" items

    /// Join items with commas (e.g. tag lists).
    let joinComma (items: string list) = String.concat ", " items

    /// Join items with a custom separator — the single definition of `joinWith`.
    let joinWith (sep: string) (items: string list) = String.concat sep items

    // ── Collection helpers ──────────────────────────────────────

    /// Filter items that are not null or empty strings.
    let filterNotEmpty (items: string list) =
        items |> List.filter (fun s -> not (String.IsNullOrEmpty s))

    /// Take first N items from a list.
    let takeFirst (count: int) (items: 'a list) =
        items |> List.truncate count

    /// Skip first N items from a list.
    let skipFirst (count: int) (items: 'a list) =
        items.[max 0 (min count items.Length) ..]

    /// Split a list into chunks of the specified size.
    /// Last chunk may be smaller if the list length is not evenly divisible by chunk size.
    let chunk (chunkSize: int) (items: 'a list) : 'a list list =
        if chunkSize <= 0 || List.isEmpty items then [items]
        else
            let rec loop remaining acc =
                if List.isEmpty remaining then List.rev acc
                else
                    let currentChunk = List.take chunkSize remaining
                    loop (List.skip chunkSize remaining) (currentChunk :: acc)
            loop items []

    /// Intersperse a separator BETWEEN items (not trailing).
    /// `intersperse ", " ["a";"b";"c"]` → `"a, b, c"`.
    let intersperse (sep: string) (items: string list) =
        match items with
        | [] | [_] -> String.concat "" items
        | head :: tail -> head + (tail |> List.map (fun x -> sep + x) |> String.concat "")

    // ── Text formatting ─────────────────────────────────────────

    /// Truncate a string to `maxLen` chars, appending an ellipsis if cut.
    let truncateStr (maxLen: int) (s: string) =
        if s = null then ""
        elif s.Length <= maxLen then s
        else s.[..maxLen-1] + "…"

    /// Pad a string to a fixed width with spaces (right-padded).
    let padRight (width: int) (s: string) = s.PadRight(width)

    /// Pad a string to a fixed width with spaces (left-padded).
    let padLeft (width: int) (s: string) = s.PadLeft(width)

    /// Simple pluralisation: `pluralize 1 "item"` → `"1 item"`,
    /// `pluralize 3 "item"` → `"3 items"` (appends 's'). For irregular
    /// plurals pass the plural form explicitly.
    let pluralize (count: int) (singular: string) =
        let word = if count = 1 then singular else singular + "s"
        sprintf "%d %s" count word

    /// Pluralise with an explicit plural form.
    let pluralizeWith (count: int) (singular: string) (plural: string) =
        let word = if count = 1 then singular else plural
        sprintf "%d %s" count word

    /// Capitalise the first character (sentence case).
    let capitalize (s: string) =
        if String.IsNullOrEmpty s then s
        else s.[0].ToString().ToUpperInvariant() + s.[1..]

    /// Convert a kebab/snake-case string to a human-readable title.
    /// `"post-list"` / `"post_list"` → `"Post List"`.
    let titleize (s: string) =
        s.Replace('-', ' ').Replace('_', ' ').Split(' ')
        |> Array.filter (fun w -> w.Length > 0)
        |> Array.map (fun w -> w.[0].ToString().ToUpperInvariant() + (if w.Length > 1 then w.[1..] else ""))
        |> String.concat " "

    // ── i18n translation lookup ────────────────────────────────

    /// Convert a site-data value (native CLR object) to a display string.
    let private siteString (v: obj) : string =
        match v with
        | null -> ""
        | :? string as s -> s
        | :? bool as b -> if b then "true" else "false"
        | _ -> v.ToString()

    /// Translate a key using locale data from siteData.
    /// Looks up `locale.{lang}.{key}` with fallback to any available locale.
    /// Usage: `t "nav.home"` or `tLang "nav.home" "zh"
    let t (key: string) : string =
        let ctx = Context.get ()
        let defaultLang =
            match ctx.SiteData.TryGetValue("site.language") with
            | true, lang -> siteString lang
            | _ -> "en"
        // Try default language first
        let tryKey = sprintf "locale.%s.%s" defaultLang key
        match ctx.SiteData.TryGetValue(tryKey) with
        | true, v -> siteString v
        | _ ->
            // Fallback: search all locale entries for this key
            let prefix = sprintf "locale."
            ctx.SiteData
            |> Seq.tryPick (fun kv ->
                if kv.Key.StartsWith(prefix) && kv.Key.EndsWith("." + key) then
                    Some (siteString kv.Value)
                else None)
            |> Option.defaultValue key

    /// Translate a key with an explicit language code.
    let tLang (key: string) (lang: string) : string =
        let ctx = Context.get ()
        let tryKey = sprintf "locale.%s.%s" lang key
        match ctx.SiteData.TryGetValue(tryKey) with
        | true, v -> siteString v
        | _ -> t key  // fallback to auto-detect

    // ── pjax script injection ──────────────────────────────────

    /// Inject the self-contained pjax client script.
    /// Place in head or before closing body tag.
    /// Usage: `pjaxScript ()`
    ///
    /// Reuses Zest.Compiler.Rendering.Pjax.script (the single source of
    /// truth also served to Zestucks templates via `{{ pjaxScript | safe }}`),
    /// so the DSL and template paths always ship the same script.
    let pjaxScript () : string =
        Zest.Compiler.Rendering.Pjax.script

    // ── Error handling ──────────────────────────────────────────

    /// Execute `tryFn` and recover via `onError` when it raises.
    /// `onError` receives the exception and must produce a result.
    ///
    ///   tryCatch (fun () -> risky ()) (fun ex -> sprintf "failed: %s" ex.Message)
    let tryCatch (tryFn: unit -> 'T) (onError: exn -> 'T) : 'T =
        try tryFn ()
        with ex -> onError ex

    // ── Async / concurrency ─────────────────────────────────────

    /// Map a function over a list concurrently on the .NET thread pool,
    /// preserving input order in the result. Exceptions propagate to the
    /// caller (wrap with `tryCatch` to recover per item).
    let asyncMap (f: 'a -> 'b) (items: 'a list) : 'b list =
        items
        |> List.map (fun x -> async { return f x })
        |> Async.Parallel
        |> Async.RunSynchronously
        |> Array.toList

    // ── Timing control ──────────────────────────────────────────

    /// Debounce: returns a wrapper that postpones invoking `f` until
    /// `delayMs` milliseconds have elapsed without another call. A call while
    /// a timer is pending resets the delay (trailing-edge semantics). The
    /// last argument received wins.
    let debounce (delayMs: int) (f: 'T -> unit) : ('T -> unit) =
        let syncRoot = obj ()
        let mutable timer = new System.Threading.Timer(fun _ -> ())
        let mutable pending = false
        fun (arg: 'T) ->
            lock syncRoot (fun () ->
                if not pending then
                    pending <- true
                    timer.Dispose()
                    timer <-
                        new System.Threading.Timer(
                            (fun _ ->
                                lock syncRoot (fun () -> pending <- false)
                                f arg),
                            null, delayMs, System.Threading.Timeout.Infinite)
                else
                    timer.Change(delayMs, System.Threading.Timeout.Infinite) |> ignore)

    /// Throttle: returns a wrapper that invokes `f` at most once per
    /// `intervalMs` window (leading-edge semantics); calls within the window
    /// are dropped.
    let throttle (intervalMs: int) (f: 'T -> unit) : ('T -> unit) =
        let sw = System.Diagnostics.Stopwatch.StartNew()
        let mutable lastRun = 0L
        fun (arg: 'T) ->
            let now = sw.ElapsedMilliseconds
            if now - lastRun >= int64 intervalMs then
                lastRun <- now
                f arg

    // ── Performance ─────────────────────────────────────────────

    /// Memoize a unary function with a bounded cache. When the cache reaches
    /// `maxSize` entries it is cleared entirely (simple bounded strategy).
    /// `maxSize <= 0` disables caching and returns `f` unchanged.
    let memoize (maxSize: int) (f: 'a -> 'b) : ('a -> 'b) =
        let cache = System.Collections.Generic.Dictionary<'a, 'b>()
        if maxSize <= 0 then f
        else
            fun x ->
                lock cache (fun () ->
                    match cache.TryGetValue x with
                    | true, v -> v
                    | _ ->
                        let v = f x
                        if cache.Count >= maxSize then cache.Clear ()
                        cache.[x] <- v
                        v)

    // ── Functional composition helpers ──────────────────────────

    /// Tap: run a side effect and return the value unchanged.
    ///   tap (fun v -> printfn "%O" v) 42
    let tap (f: 'T -> unit) (value: 'T) =
        f value; value

    /// Curry: turn a function of a pair into a two-argument function.
    ///   curry (fun (a, b) -> a + b) 1 2   → 3
    let curry (f: 'a * 'b -> 'c) (a: 'a) (b: 'b) : 'c = f (a, b)

    /// Uncurry: turn a two-argument function into a function of a pair.
    let uncurry (f: 'a -> 'b -> 'c) (pair: 'a * 'b) : 'c = f (fst pair) (snd pair)

    /// Compose two unary functions: `pipe2 f g x = g (f x)`.
    let pipe2 (f: 'a -> 'b) (g: 'b -> 'c) (x: 'a) : 'c = g (f x)

    /// Compose three unary functions: `pipe3 f g h x = h (g (f x))`.
    let pipe3 (f: 'a -> 'b) (g: 'b -> 'c) (h: 'c -> 'd) (x: 'a) : 'd = h (g (f x))

    // ── Array / list manipulation ───────────────────────────────

    /// Flatten one level of nesting. Repeated calls flatten deeper structures:
    /// `flatten (flatten [[[1];[2]]])` → `[1;2]`.
    let flatten (items: 'a list list) : 'a list =
        items |> List.collect id

    /// Interleave multiple lists element-by-element, taking one element from
    /// each list in turn until all lists are exhausted.
    ///   interleave [["a";"b";"c"]; ["1";"2"]]  →  ["a";"1";"b";"2";"c"]
    let interleave (lists: 'a list list) : 'a list =
        let rec loop (remaining: 'a list list) (acc: 'a list) : 'a list =
            let active =
                remaining
                |> List.choose (function
                    | x :: rest -> Some (x, rest)
                    | [] -> None)
            match active with
            | [] -> List.rev acc
            | pairs ->
                let heads, tails = List.unzip pairs
                loop tails (List.rev heads @ acc)
        loop lists []

    /// Zip two lists with a custom combiner, stopping at the shorter list
    /// (no exception when lengths differ).
    ///   zipWith (fun a b -> a + b) [1;2;3] [10;20]  →  [11;22]
    let zipWith (f: 'a -> 'b -> 'c) (left: 'a list) (right: 'b list) : 'c list =
        let n = min left.Length right.Length
        [ for i in 0 .. n - 1 -> f left.[i] right.[i] ]

    // ── String processing ───────────────────────────────────────

    /// Split a string by a literal separator (not a regex).
    ///   splitBy "," "a,b,c"  →  ["a";"b";"c"]
    let splitBy (separator: string) (s: string) : string list =
        if isNull s then []
        elif String.IsNullOrEmpty separator then [ s ]
        else
            s.Split([| separator |], StringSplitOptions.None)
            |> Array.toList

    /// Case-sensitive prefix check.
    let startsWith (prefix: string) (s: string) : bool =
        not (isNull s) && not (isNull prefix) && s.StartsWith(prefix, StringComparison.Ordinal)

    /// Case-sensitive suffix check.
    let endsWith (suffix: string) (s: string) : bool =
        not (isNull s) && not (isNull suffix) && s.EndsWith(suffix, StringComparison.Ordinal)

    /// Case-sensitive substring check.
    let contains (substr: string) (s: string) : bool =
        not (isNull s) && not (isNull substr) && s.IndexOf(substr, StringComparison.Ordinal) >= 0

    /// Replace every occurrence of `oldValue` with `newValue` (literal, not regex).
    let replaceAll (oldValue: string) (newValue: string) (s: string) : string =
        if isNull s then s
        elif String.IsNullOrEmpty oldValue then s
        else s.Replace(oldValue, newValue)
