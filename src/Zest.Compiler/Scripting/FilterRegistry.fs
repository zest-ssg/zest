namespace Zest.Compiler.Scripting

open System
open System.Collections.Generic
open System.Text.RegularExpressions
open Zest.Compiler.Template
open Zest.Compiler.Html

/// Centralised Zealucks custom filter registration for Zest.
/// Used by both content rendering and layout rendering paths.
module FilterRegistry =

    /// Init-script-declared filters: name → pipeline spec (e.g. "upper | trim").
    /// Set by BuildEngine after running _init.zest.fsx, applied during
    /// `registerAllFilters` so every engine instance picks them up.
    let private initFilters = Dictionary<string, string>()

    /// Whether to register Zest extension filters (pages_by_tag, recent,
    /// by_collection, search). When `ZealucksCompatibility = "strict"`,
    /// these are skipped so only the Nunjucks-compatible filter set
    /// remains available. User-declared init filters are always registered.
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

    /// Toggle strict Zealucks compatibility mode. When true, Zest-specific
    /// extension filters are not registered on engine instances.
    let setStrictMode (enabled: bool) = strictMode := enabled


    /// Read the taxonomy term-to-slug alias map injected by BuildData. The
    /// map lives at global key `params.taxonomy`; missing or malformed data
    /// degrades to no aliases rather than failing the render.
    let private lookupTaxonomyAlias (kind: string) (term: string) : string option =
        let data = !PageQuery.globalDataRef
        match data.TryGetValue "params.taxonomy" with
        | true, (:? IDictionary<string, obj> as tax) ->
            match tax.TryGetValue kind with
            | true, (:? IDictionary<string, obj> as table) ->
                match table.TryGetValue term with
                | true, (:? string as slug) when slug.Length > 0 -> Some slug
                | _ -> None
            | _ -> None
        | _ -> None

    /// Apply the configured taxonomy alias and fall back to an ASCII slug.
    /// Mirrors the site's 11ty slugify: whitespace to hyphens, every other
    /// non-alphanumeric character dropped, empty result becomes `untitled`.
    let private taxonomySlugify (term: string) : string =
        match lookupTaxonomyAlias "categories" term with
        | Some slug -> slug
        | None ->
            match lookupTaxonomyAlias "tags" term with
            | Some slug -> slug
            | None ->
                let slug =
                    Regex.Replace(term.ToLowerInvariant(), @"\s+", "-")
                    |> fun s -> Regex.Replace(s, "[^a-z0-9-]", "")
                    |> fun s -> s.Trim('-')
                if slug.Length = 0 then "untitled" else slug

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
    /// In strict mode (setStrictMode true), the Zest extension
    /// filters (pages_by_tag / recent / by_collection / search) are skipped
    /// so templates behave like stock Nunjucks. Init-declared filters
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
                let pages = PageQuery.getPagesForZealucks ()
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
                PageQuery.getPagesForZealucks ()
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
                PageQuery.getPagesForZealucks ()
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
                let pages = PageQuery.getPagesForZealucks ()
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

        // ── where: generic attribute filter (also available in 11ty) ──
        // Kept available even in strict mode because 11ty users expect
        // `where` to work.
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
        // Each spec is a Zealucks filter pipeline applied via a mini-render.
        // Always registered — these are user-owned, not Zest builtins.
        for kv in initFilters do
            let spec = kv.Value
            let name = kv.Key
            engine.RegisterFilter name (fun value _args -> applyPipeline engine spec value)

        // ── readingTime: estimate reading time in minutes ──────
        engine.RegisterFilter "readingTime" (fun value _args ->
            if isNull value then box 1
            else box (Zest.Core.TextMetrics.readingMinutes (value.ToString())))

        // ── wordCount: count CJK chars plus English words ──────
        // Usage: {{ post.templateContent | wordCount }}
        engine.RegisterFilter "wordCount" (fun value _args ->
            if isNull value then box 0
            else box (Zest.Core.TextMetrics.countWords (value.ToString())))

        // ── year: current calendar year for copyright lines ────
        engine.RegisterFilter "year" (fun _value _args -> box DateTime.Now.Year)

        // ── limit: keep the first N items of an array ──────────
        // Usage: {{ posts | limit(5) }}
        engine.RegisterFilter "limit" (fun value args ->
            let n = if args.Length > 0 then (try int args.[0] with _ -> 0) else 0
            match value with
            | :? System.Collections.IEnumerable as ie when not (value :? string) ->
                ie |> Seq.cast<obj> |> Seq.truncate n |> Array.ofSeq |> box
            | _ -> value)

        // ── relatedPosts: related entries excluding the current one ──
        // The collection is already sorted newest-first; the site only needs
        // "other recent posts", so relevance is the collection order.
        // Usage: {{ collections.posts | relatedPosts(page.url, 3) }}
        engine.RegisterFilter "relatedPosts" (fun value args ->
            let currentUrl = if args.Length > 0 then args.[0] else ""
            let maxCount = if args.Length > 1 then (try int args.[1] with _ -> 3) else 3
            match value with
            | :? System.Collections.IEnumerable as ie ->
                ie |> Seq.cast<obj>
                |> Seq.filter (fun item ->
                    match item with
                    | :? IDictionary<string, obj> as d ->
                        match d.TryGetValue "url" with
                        | true, (:? string as u) -> u <> currentUrl
                        | _ -> true
                    | _ -> true)
                |> Seq.truncate maxCount
                |> Array.ofSeq |> box
            | _ -> box Array.empty<obj>)

        // ── groupByYear: group page dicts by publication year, newest first ──
        // Returns an array of { year, posts } dictionaries so Zealucks can
        // iterate `{% for group in posts | groupByYear %}` without object keys.
        engine.RegisterFilter "groupByYear" (fun value _args ->
            let pageYear (d: IDictionary<string, obj>) : int =
                match d.TryGetValue "date" with
                | true, (:? string as ds) when ds.Length >= 4 ->
                    match Int32.TryParse ds.[0..3] with true, y -> y | _ -> 0
                | _ -> 0
            match value with
            | :? System.Collections.IEnumerable as ie ->
                ie |> Seq.cast<obj>
                |> Seq.choose (fun item ->
                    match item with
                    | :? IDictionary<string, obj> as d -> Some d
                    | _ -> None)
                |> Seq.groupBy pageYear
                |> Seq.sortByDescending fst
                |> Seq.map (fun (year, posts) ->
                    let group = Dictionary<string, obj>()
                    group.["year"] <- box year
                    group.["posts"] <- box (posts |> Array.ofSeq |> Array.map (fun d -> d :> obj))
                    group :> obj)
                |> Array.ofSeq |> box
            | _ -> box Array.empty<obj>)

        // ── Date aliases expected by the migrated 11ty templates ──
        // readableDate normalises front-matter date strings to yyyy-MM-dd.
        engine.RegisterFilter "readableDate" (fun value _args ->
            let text = if isNull value then "" else value.ToString()
            match DateTime.TryParse(text, Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.None) with
            | true, d -> box (d.ToString("yyyy-MM-dd"))
            | false, _ -> box text)
        // dateToRfc3339 emits a UTC timestamp for RSS and JSON-LD.
        engine.RegisterFilter "dateToRfc3339" (fun value _args ->
            let text = if isNull value then "" else value.ToString()
            match DateTime.TryParse(text, Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.None) with
            | true, d -> box (d.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"))
            | false, _ -> box text)
        // archiveDate renders the month-day-year label used by the archive page.
        engine.RegisterFilter "archiveDate" (fun value _args ->
            let text = if isNull value then "" else value.ToString()
            match DateTime.TryParse(text, Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.None) with
            | true, d -> box (d.ToString("MMM dd, yyyy", Globalization.CultureInfo.InvariantCulture))
            | false, _ -> box text)

        // ── slugify: taxonomy-alias-aware ASCII slug ───────────
        engine.RegisterFilter "slugify" (fun value _args ->
            if isNull value then box "untitled"
            else box (taxonomySlugify (value.ToString())))

        // ── stripHtml: strip HTML and common Markdown, keep plain text ──
        // Used by the search index and excerpts; keeps link text but drops URLs.
        engine.RegisterFilter "stripHtml" (fun value _args ->
            if isNull value then box ""
            else
                value.ToString()
                |> fun s -> Regex(@"<[^>]+>").Replace(s, " ")
                |> fun s -> Regex(@"```[\s\S]*?```").Replace(s, " ")
                |> fun s -> Regex(@"`[^`]*`").Replace(s, " ")
                |> fun s -> Regex(@"!\[[^\]]*\]\([^)]*\)").Replace(s, " ")
                |> fun s -> Regex.Replace(s, @"\[([^\]]*)\]\([^)]*\)", "$1")
                |> fun s -> Regex(@"[#>*_~]").Replace(s, " ")
                |> fun s -> Regex(@"\s+").Replace(s, " ").Trim()
                |> box)

        // ── t: i18n translation key lookup ─────────────────────
        // Usage: {{ 'nav.home' | t }} or {{ 'nav.home' | t('zh') }}
        // Extra args supply {name} interpolation: {{ 'hi' | t('zh', 'name=World') }}
        engine.RegisterFilter "t" (fun value args ->
            let key = if isNull value then "" else value.ToString()
            let lang = if args.Length > 0 then args.[0] else !defaultLangRef
            let locales = !localeRef
            if locales.Count = 0 then key
            else
                // args[1..] are key=value pairs bound to {name} placeholders.
                let interp = Dictionary<string, string>()
                for arg in args.[1..] do
                    let s = arg |> string
                    let eq = s.IndexOf('=')
                    if eq > 0 then interp.[s.[..eq-1].Trim()] <- s.[eq+1..].Trim()
                Zest.Compiler.Content.LocaleLoader.translateWithArgs
                    locales !defaultLangRef key (Some lang) interp)

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
        // Output: JSON array of { url, title, tags, categories, description,
        // excerpt, content, date }. Content and excerpt are stripped to plain
        // text so the client-side Fuse index matches prose, not markup.
        engine.RegisterFilter "searchIndex" (fun value _args ->
            let escapeJson (s: string) = s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ")
            let stringField (d: IDictionary<string, obj>) key =
                match d.TryGetValue key with true, (:? string as s) -> s | _ -> ""
            let arrayField (d: IDictionary<string, obj>) key =
                match d.TryGetValue key with
                | true, (:? (string[]) as xs) -> String.Join(",", xs)
                | _ -> ""
            let pages =
                match value with
                | :? System.Collections.IEnumerable as ie ->
                    ie |> Seq.cast<obj> |> Array.ofSeq
                | _ -> PageQuery.getPagesForZealucks () |> Array.map box
            let index =
                pages
                |> Array.choose (fun p ->
                    match p with
                    | :? IDictionary<string, obj> as d ->
                        let url = stringField d "url"
                        if String.IsNullOrEmpty url then None
                        else
                            let title = stringField d "title"
                            let tags = arrayField d "tags"
                            let categories = arrayField d "categories"
                            let description = stringField d "description"
                            let date = stringField d "date"
                            let excerpt = stringField d "excerpt"
                            let content =
                                stringField d "content"
                                |> fun s -> Regex(@"<[^>]+>").Replace(s, " ")
                                |> fun s -> Regex(@"\s+").Replace(s, " ").Trim()
                            Some (sprintf """{"url":"%s","title":"%s","tags":"%s","categories":"%s","description":"%s","excerpt":"%s","content":"%s","date":"%s"}"""
                                    (escapeJson url) (escapeJson title) (escapeJson tags) (escapeJson categories)
                                    (escapeJson description) (escapeJson excerpt) (escapeJson content) (escapeJson date))
                    | _ -> None)
            box (sprintf "[%s]" (String.Join(",", index))))

        // ── pjaxScript: inject self-contained pjax JS ─────────
        // Usage: {{ pjaxScript | safe }} in head or before </body>
        engine.RegisterFilter "pjaxScript" (fun _value _args ->
            box ZestPjax.script)

