namespace Zest.Engine.Scripting

open System
open System.Collections.Generic
open System.Text.RegularExpressions
open Zest.Engine.Template
open Zest.Engine.Resources

/// Centralised Nunjucks custom filter registration for Zest.
/// Used by both content rendering and layout rendering paths.
module FilterRegistry =

    /// Init-script-declared filters: name → pipeline spec (e.g. "upper | trim").
    /// Set by BuildEngine after running _init.zest.fsx, applied during
    /// `registerAllFilters` so every engine instance picks them up.
    let private initFilters = Dictionary<string, string>()

    /// Whether to register Zest extension filters (pages_by_tag, recent,
    /// by_collection, search). When `NunjucksCompatibility = "strict"`,
    /// these are skipped so only official-Nunjucks-compatible filters
    /// remain available. User-declared init filters are always registered.
    let private strictMode = ref false

    /// Set the init-script-declared filter specs. Called once per build
    /// after _init.zest.fsx executes. Clears any previously accumulated filters.
    let setInitFilters (filters: IDictionary<string, string>) =
        initFilters.Clear()
        for kv in filters do initFilters.[kv.Key] <- kv.Value

    /// Add init filter specs without clearing existing ones. Used for
    /// theme _theme.zest.fsx filters so user _init.zest.fsx can extend them.
    let addInitFilters (filters: IDictionary<string, string>) =
        for kv in filters do
            if not (initFilters.ContainsKey kv.Key) then
                initFilters.[kv.Key] <- kv.Value

    /// Toggle strict Nunjucks compatibility mode. When true, Zest-specific
    /// extension filters are not registered on engine instances.
    let setStrictMode (enabled: bool) = strictMode := enabled

    /// Apply a filter pipeline spec (e.g. "upper | trim") to a value by
    /// rendering a minimal template through the engine. This avoids needing
    /// a public ApplyFilter method on ITemplateEngine and works for any
    /// engine that supports the `|` filter syntax.
    let private applyPipeline (engine: ITemplateEngine) (spec: string) (value: obj) : obj =
        let ctx = Dictionary<string, obj>()
        ctx.["__zv"] <- value
        let template = sprintf "{{ __zv | %s }}" (spec.Trim())
        match engine.Render template ctx with
        | Ok s -> box s
        | Error _ -> value  // fall back to original on error

    /// Locale data reference — set by BuildEngine before build.
    /// Key: language code → (key → translation)
    let private localeRef : IDictionary<string, IDictionary<string, string>> ref =
        ref (dict [] :> IDictionary<string, IDictionary<string, string>>)

    /// Default language for t() filter fallback.
    let private defaultLangRef : string ref = ref "en"

    /// <summary>
    /// Set locale data and default language for the t() translation filter.
    /// Called by BuildEngine after loading locale files.
    /// </summary>
    let setLocales (locales: IDictionary<string, IDictionary<string, string>>) (defaultLang: string) =
        localeRef := locales
        defaultLangRef := defaultLang

    /// Register all Zest-specific filters on the given template engine,
    /// including any init-script-declared filters.
    ///
    /// In strict Nunjucks mode (setStrictMode true), the Zest extension
    /// filters (pages_by_tag / recent / by_collection / search) are skipped
    /// so templates behave like official Nunjucks. Init-declared filters
    /// are always registered because they are user-owned, not Zest builtins.
    let registerAllFilters (engine: ITemplateEngine) =
        // ── Zest extension filters (skipped in strict mode) ──
        if not !strictMode then
            // ── pages_by_tag / by_tag: filter pages by a tag ─────
            // `by_tag` is registered as an alias so templates ported from
            // other SSGs (e.g. Hugo's `where`-style tag filters) work without
            // rewriting. Both names share the same filter body.
            let pagesByTag (value: obj) (args: string list) =
                let tag = if args.Length > 0 then args.[0] else ""
                let pages = PageQuery.getPagesForNunjucks ()
                pages
                |> Array.filter (fun p ->
                    match p.TryGetValue "tags" with
                    | true, (:? (string[]) as tags) ->
                        tags |> Array.exists (fun t -> t.Equals(tag, StringComparison.OrdinalIgnoreCase))
                    | _ -> false)
                |> Array.map (fun d -> d :> obj) |> box
            engine.RegisterFilter "pages_by_tag" (fun value args -> pagesByTag value args)
            engine.RegisterFilter "by_tag"       (fun value args -> pagesByTag value args)

            // ── recent: get N most recent pages ────────────────
            engine.RegisterFilter "recent" (fun value args ->
                let n = if args.Length > 0 then (try int args.[0] with _ -> 5) else 5
                PageQuery.getPagesForNunjucks ()
                |> Array.filter (fun p ->
                    match p.TryGetValue "date" with
                    | true, (:? string as d) -> d <> ""
                    | _ -> false)
                |> Array.sortByDescending (fun p ->
                    match p.TryGetValue "date" with
                    | true, (:? string as d) -> d
                    | _ -> "")
                |> Array.truncate n
                |> Array.map (fun d -> d :> obj) |> box)

            // ── by_collection: filter pages by collection name ─
            engine.RegisterFilter "by_collection" (fun value args ->
                let col = if args.Length > 0 then args.[0] else ""
                // 2nd arg `exclude_index` arrives as a string ("true"/"True"/"1").
                let excludeIndex =
                    args.Length > 1 &&
                    (match args.[1].Trim().ToLowerInvariant() with "true" | "yes" | "1" -> true | _ -> false)
                PageQuery.getPagesForNunjucks ()
                |> Array.filter (fun p ->
                    match p.TryGetValue "url" with
                    | true, (:? string as u) ->
                        let parts = u.Trim('/').Split('/')
                        let inCol = parts.Length > 0 && parts.[0].Equals(col, StringComparison.OrdinalIgnoreCase)
                        let isIndex = parts.Length <= 1
                        inCol && (not excludeIndex || not isIndex)
                    | _ -> false)
                |> Array.map (fun d -> d :> obj) |> box)

            // ── search: simple full-text search across pages ───
            engine.RegisterFilter "search" (fun value args ->
                let query = if args.Length > 0 then args.[0].ToLowerInvariant() else ""
                let pages = PageQuery.getPagesForNunjucks ()
                if query = "" then pages |> Array.map (fun d -> d :> obj) |> box
                else
                    pages
                    |> Array.filter (fun p ->
                        [ "title"; "content"; "excerpt"; "description" ]
                        |> List.exists (fun key ->
                            match p.TryGetValue key with
                            | true, (:? string as s) ->
                                s.ToLowerInvariant().Contains(query)
                            | _ -> false))
                    |> Array.map (fun d -> d :> obj) |> box)

        // ── where: generic attribute filter (Liquid-style, also in 11ty) ──
        // Kept available even in strict mode because Liquid and 11ty users
        // expect `where` to work.
        engine.RegisterFilter "where" (fun value args ->
            let key = if args.Length > 0 then args.[0] else ""
            let expected = if args.Length > 1 then args.[1] else ""
            let toStr (v: obj) = if isNull v then "" else v.ToString()
            match value with
            | :? System.Collections.IEnumerable as ie ->
                ie |> Seq.cast<obj>
                |> Seq.filter (fun item ->
                    match item with
                    | :? IDictionary<string, obj> as d ->
                        match d.TryGetValue key with
                        | true, v -> toStr v = expected
                        | _ -> false
                    | _ -> false)
                |> Array.ofSeq :> obj
            | _ -> value)

        // ── init-script-declared filters (from _init.zest.fsx) ──
        // Each spec is a Nunjucks filter pipeline applied via a mini-render.
        // Always registered — these are user-owned, not Zest builtins.
        for kv in initFilters do
            let spec = kv.Value
            let name = kv.Key
            engine.RegisterFilter name (fun value _args -> applyPipeline engine spec value)

        // ── readingTime: estimate reading time in minutes ──────
        // Chinese: ~350 chars/min, English: ~220 words/min.
        // Strips HTML tags and code blocks before counting.
        engine.RegisterFilter "readingTime" (fun value _args ->
            if isNull value then box 1
            else
                let text = value.ToString()
                let stripped =
                    Regex(@"<pre[^>]*>[\s\S]*?<\/pre>").Replace(text, "")
                    |> fun s -> Regex(@"<code[^>]*>[\s\S]*?<\/code>").Replace(s, "")
                    |> fun s -> Regex(@"<[^>]+>").Replace(s, " ")
                    |> fun s -> s.Trim()
                let chineseChars = Regex(@"[\u4e00-\u9fff\u3400-\u4dbf]").Matches(stripped).Count
                let englishWords = Regex(@"[a-zA-Z0-9]+").Matches(stripped).Count
                let minutes = Math.Max(1, Math.Ceiling(float chineseChars / 350. + float englishWords / 220.) |> int)
                box minutes)

        // ── t: i18n translation key lookup ─────────────────────
        // Usage: {{ 'nav.home' | t }} or {{ 'nav.home' | t('zh') }}
        engine.RegisterFilter "t" (fun value args ->
            let key = if isNull value then "" else value.ToString()
            let lang = if args.Length > 0 then args.[0] else !defaultLangRef
            let locales = !localeRef
            if locales.Count = 0 then key
            else
                match locales.TryGetValue lang with
                | true, dict ->
                    match dict.TryGetValue key with
                    | true, v -> v
                    | _ -> key
                | _ -> key)

        // ── prevPost: get previous (older) page from a collection ─
        // Usage: {{ collection.posts | prevPost(page.url) }}
        engine.RegisterFilter "prevPost" (fun value args ->
            let currentUrl = if args.Length > 0 then args.[0] else ""
            match value with
            | :? System.Collections.IEnumerable as ie ->
                let arr = ie |> Seq.cast<obj> |> Array.ofSeq
                let idx = arr |> Array.tryFindIndex (fun p ->
                    match p with
                    | :? IDictionary<string, obj> as d ->
                        match d.TryGetValue "url" with
                        | true, (:? string as u) -> u = currentUrl
                        | _ -> false
                    | _ -> false)
                match idx with
                | Some i when i + 1 < arr.Length -> box arr.[i + 1]
                | _ -> box null
            | _ -> box null)

        // ── nextPost: get next (newer) page from a collection ───
        // Usage: {{ collection.posts | nextPost(page.url) }}
        engine.RegisterFilter "nextPost" (fun value args ->
            let currentUrl = if args.Length > 0 then args.[0] else ""
            match value with
            | :? System.Collections.IEnumerable as ie ->
                let arr = ie |> Seq.cast<obj> |> Array.ofSeq
                let idx = arr |> Array.tryFindIndex (fun p ->
                    match p with
                    | :? IDictionary<string, obj> as d ->
                        match d.TryGetValue "url" with
                        | true, (:? string as u) -> u = currentUrl
                        | _ -> false
                    | _ -> false)
                match idx with
                | Some i when i > 0 -> box arr.[i - 1]
                | _ -> box null
            | _ -> box null)

        // ── searchIndex: generate JSON search index for static search ──
        // Usage: {{ pages | searchIndex | dump }}
        // Output: JSON array of { url, title, tags, description, date }
        engine.RegisterFilter "searchIndex" (fun value _args ->
            let pages =
                match value with
                | :? System.Collections.IEnumerable as ie ->
                    ie |> Seq.cast<obj> |> Array.ofSeq
                | _ -> PageQuery.getPagesForNunjucks () |> Array.map box
            let index =
                pages
                |> Array.choose (fun p ->
                    match p with
                    | :? IDictionary<string, obj> as d ->
                        let url = match d.TryGetValue "url" with true, (:? string as u) -> u | _ -> ""
                        let title = match d.TryGetValue "title" with true, (:? string as t) -> t | _ -> ""
                        let tags = match d.TryGetValue "tags" with true, (:? (string[]) as ts) -> String.Join(",", ts) | _ -> ""
                        let desc = match d.TryGetValue "description" with true, (:? string as ds) -> ds | _ -> ""
                        let date = match d.TryGetValue "date" with true, (:? string as dt) -> dt | _ -> ""
                        if String.IsNullOrEmpty url then None
                        else
                            Some (sprintf """{"url":"%s","title":"%s","tags":"%s","description":"%s","date":"%s"}"""
                                    (url.Replace("\"", "\\\""))
                                    (title.Replace("\"", "\\\""))
                                    (tags.Replace("\"", "\\\""))
                                    (desc.Replace("\"", "\\\""))
                                    (date.Replace("\"", "\\\"")))
                    | _ -> None)
            box (sprintf "[%s]" (String.Join(",", index))))

        // ── pjaxScript: inject self-contained pjax JS ─────────
        // Usage: {{ pjaxScript | safe }} in head or before </body>
        engine.RegisterFilter "pjaxScript" (fun _value _args ->
            box ZestPjax.script)
