namespace Zest.Engine.Scripting

open System
open System.Collections.Generic
open System.IO
open Zest.Engine
open Zest.Engine.Parsing

/// Collections API: page queries, global data, and Nunjucks helpers.
/// Optimized with on-demand caching for Nunjucks data.
module PageQuery =

    let internal allPagesRef : ContentPage list ref = ref []
    let internal draftPagesRef : ContentPage list ref = ref []
    let internal includesRef : IDictionary<string, string> ref = ref (dict [])
    let internal verboseRef   : bool ref = ref false

    let setAllPages (pages: ContentPage list) = allPagesRef := pages
    let setDraftPages (pages: ContentPage list) = draftPagesRef := pages
    let setIncludes (includes: IDictionary<string, string>) = includesRef := includes
    let setVerbose (v: bool) = verboseRef := v

    let getPages () = !allPagesRef
    let getPagesByTag (tag: string) =
        !allPagesRef |> List.filter (fun p -> p.Tags |> List.exists (fun t -> t.Equals(tag, StringComparison.OrdinalIgnoreCase)))
    let getPagesByDir (dirName: string) =
        !allPagesRef |> List.filter (fun p ->
            p.SourcePath.Contains(Path.DirectorySeparatorChar.ToString() + dirName + Path.DirectorySeparatorChar.ToString())
            || p.SourcePath.Contains("/" + dirName + "/"))
    let getRecentPages (n: int) =
        !allPagesRef |> List.filter (fun p -> p.Date.IsSome) |> List.sortByDescending (fun p -> p.Date.Value) |> List.truncate n
    let includePartial (name: string) =
        match (!includesRef).TryGetValue(name) with true, c -> c | _ -> sprintf "<!-- include '%s' not found -->" name

    let internal globalDataRef : IDictionary<string, obj> ref =
        ref (dict [] :> IDictionary<string, obj>)

    let setGlobalData (data: IDictionary<string, obj>) =
        System.Threading.Interlocked.Exchange(globalDataRef, data) |> ignore

    let getDataString (key: string) : string =
        let mutable v : obj = null
        if (!globalDataRef).TryGetValue(key, &v) then (if isNull v then "" else v.ToString())
        else ""

    let getDataSection (prefix: string) : IDictionary<string, obj> =
        let d = Dictionary<string, obj>()
        for kv in !globalDataRef do
            if kv.Key.StartsWith(prefix + ".") then
                d.[kv.Key.Substring(prefix.Length + 1)] <- kv.Value
        d :> _

    let getPagesByDate () =
        !allPagesRef |> List.filter (fun p -> p.Date.IsSome) |> List.sortByDescending (fun p -> p.Date.Value)

    let getPagesByCollection (collection: string) =
        !allPagesRef |> List.filter (fun p ->
            let parts = p.Url.Trim('/').Split('/')
            parts.Length > 0 && parts.[0].Equals(collection, StringComparison.OrdinalIgnoreCase))

    let getAllTags () =
        !allPagesRef |> List.collect (fun p -> p.Tags) |> List.distinct |> List.sort

    let getAllCategories () =
        !allPagesRef |> List.collect (fun p -> p.Categories) |> List.distinct |> List.sort

    let getAllCollections () =
        !allPagesRef
        |> List.map (fun p ->
            let parts = p.Url.Trim('/').Split('/')
            if parts.Length > 0 && parts.[0] <> "" then parts.[0] else "root")
        |> List.distinct |> List.sort

    let searchPages (query: string) =
        !allPagesRef |> List.filter (fun p -> p.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)

    let getPageCount () = (!allPagesRef).Length

    let getPagesByDateRange (fromDate: string) (toDate: string) =
        let fromDt = DateTime.Parse(fromDate)
        let toDt   = DateTime.Parse(toDate)
        !allPagesRef
        |> List.filter (fun p ->
            p.Date.IsSome &&
            p.Date.Value >= fromDt &&
            p.Date.Value <= toDt)

    // ── Nunjucks data helpers ────────────────────────────────────────────

    /// Read a page's rendered body for collection templates.
    ///
    /// The collection snapshot is taken during the metadata pass, so
    /// Content is empty there. Rendered HTML is needed by reading-time and
    /// word-count filters on listing pages, and by the search index. The
    /// source file is parsed once on demand: markdown posts are converted to
    /// HTML, other templates expose their raw body text. Failures degrade to
    /// the already-rendered Content (possibly empty) so one unreadable file
    /// cannot break a listing page.
    let private renderedBodyCache = Dictionary<string, string>()
    let private renderedBodyLock = obj ()

    let private loadRenderedBody (p: ContentPage) : string =
        lock renderedBodyLock (fun () ->
            if renderedBodyCache.ContainsKey p.SourcePath then
                renderedBodyCache.[p.SourcePath]
            else
                let body =
                    try
                        if not (File.Exists p.SourcePath) then p.Content
                        else
                            let text = File.ReadAllText p.SourcePath
                            let ext = Path.GetExtension(p.SourcePath).ToLowerInvariant()
                            // MetaParser is the single authority for front
                            // matter boundaries. It returns the normalized
                            // text unchanged when no valid +++ block exists, so
                            // files without TOML headers pass through untouched.
                            let bodyText = MetaParser.parseToml text |> snd
                            if ext = FileExtensions.Markdown || ext = FileExtensions.MarkdownLong then
                                Zest.Engine.Html.MarkdownEngine.toHtml bodyText
                            else bodyText
                    with _ -> p.Content
                renderedBodyCache.[p.SourcePath] <- body
                body)

    /// Reset caches that hold file-derived data. Called on every build pass so
    /// edited content never leaves a stale rendered body behind.
    let internal resetBodyCache () =
        lock renderedBodyLock (fun () -> renderedBodyCache.Clear())

    let pageToNunjucksDict (p: ContentPage) : IDictionary<string, obj> =
        let d = Dictionary<string, obj>()
        d.["url"]    <- box p.Url
        d.["title"]  <- box p.Title
        d.["slug"]   <- box p.Slug
        d.["date"]   <- box (p.Date |> Option.map (fun d -> d.ToString("yyyy-MM-dd")) |> Option.defaultValue "")
        d.["tags"]   <- box (p.Tags |> Array.ofList)
        d.["categories"] <- box (p.Categories |> Array.ofList)
        d.["updated"] <- box (p.Updated |> Option.map (fun d -> d.ToString("yyyy-MM-dd")) |> Option.defaultValue "")
        match p.Data.TryGetValue "description" with
        | true, v -> d.["description"] <- box v
        | _ -> ()
        // `excerpt` and any other front-matter extra live in Data; surface
        // them on the flat dict so listing templates can use post.excerpt.
        for key in [ "excerpt"; "author" ] do
            match p.Data.TryGetValue key with
            | true, v -> d.[key] <- box v
            | _ -> ()
        // Rendered body, exposed under both names used by common blog themes:
        // `content` (Zest idiom) and `templateContent` (Eleventy idiom).
        let body = if not (String.IsNullOrEmpty p.Content) then p.Content else loadRenderedBody p
        d.["content"] <- box body
        d.["templateContent"] <- box body
        d :> IDictionary<string, obj>

    // ── Cached Nunjucks data — computed once per build pass ──────────────

    let mutable private _cachedPagesForNunjucks : IDictionary<string, obj>[] option = None
    let mutable private _cachedTagsForNunjucks : string[] option = None
    let mutable private _cachedCollectionsForNunjucks : IDictionary<string, obj> option = None

    /// Reset cached Nunjucks data (call at build start).
    let internal resetNunjucksCache () =
        _cachedPagesForNunjucks <- None
        _cachedTagsForNunjucks <- None
        _cachedCollectionsForNunjucks <- None
        resetBodyCache ()

    let getPagesForNunjucks () : IDictionary<string, obj>[] =
        match _cachedPagesForNunjucks with
        | Some cached -> cached
        | None ->
            let result = !allPagesRef |> List.map pageToNunjucksDict |> Array.ofList
            _cachedPagesForNunjucks <- Some result
            result

    let getTagsForNunjucks () : string[] =
        match _cachedTagsForNunjucks with
        | Some cached -> cached
        | None ->
            let result = !allPagesRef |> List.collect (fun p -> p.Tags) |> List.distinct |> List.sort |> Array.ofList
            _cachedTagsForNunjucks <- Some result
            result

    /// Collection pages keyed by collection name, newest first — enables
    /// `{% for post in collections.posts %}` and prev/next pagination via
    /// the `prevPost` / `nextPost` filters.
    let getCollectionsForNunjucks () : IDictionary<string, obj> =
        match _cachedCollectionsForNunjucks with
        | Some cached -> cached
        | None ->
            let result = Dictionary<string, obj>()
            for name in getAllCollections () do
                let pages =
                    getPagesByCollection name
                    // Exclude the collection's own index page (e.g. `/posts/`),
                    // so pagination/listing navigate between actual posts only.
                    |> List.filter (fun p -> not (p.Url.Trim('/').Equals(name, StringComparison.OrdinalIgnoreCase)))
                    |> List.sortByDescending (fun p -> p.Date |> Option.defaultValue DateTime.MinValue)
                    |> List.map pageToNunjucksDict
                    |> Array.ofList
                result.[name] <- box pages

            // Aggregated taxonomy lists consumed by sidebars and taxonomy
            // index pages. Each entry is `{ name, slug, posts }` sorted by
            // post count descending. The slug comes from the configured
            // Chinese-to-ASCII alias map (site.params.taxonomy); unknown terms
            // fall back to the ASCII slug produced by taxonomyTermSlug.
            let taxonomyTable (kind: string) (terms: string list) (select: ContentPage -> string list) : IDictionary<string, obj>[] =
                let aliasMap =
                    match (!globalDataRef).TryGetValue "params.taxonomy" with
                    | true, (:? IDictionary<string, obj> as tax) ->
                        match tax.TryGetValue kind with
                        | true, (:? IDictionary<string, obj> as table) ->
                            table |> Seq.map (fun kv -> kv.Key, string kv.Value) |> Map.ofSeq
                        | _ -> Map.empty
                    | _ -> Map.empty
                let slugOf term =
                    match Map.tryFind term aliasMap with
                    | Some s when s.Length > 0 -> s
                    | None ->
                        let s =
                            Text.RegularExpressions.Regex.Replace(term.ToLowerInvariant(), @"\s+", "-")
                            |> fun x -> Text.RegularExpressions.Regex.Replace(x, "[^a-z0-9-]", "")
                            |> fun x -> x.Trim('-')
                        if s.Length = 0 then "untitled" else s
                terms
                |> List.map (fun term ->
                    let termPages =
                        !allPagesRef
                        |> List.filter (fun p -> select p |> List.exists (fun t -> t.Equals(term, StringComparison.OrdinalIgnoreCase)))
                        |> List.sortByDescending (fun p -> p.Date |> Option.defaultValue DateTime.MinValue)
                        |> List.map pageToNunjucksDict
                        |> Array.ofList
                    let entry = Dictionary<string, obj>()
                    entry.["name"] <- box term
                    entry.["slug"] <- box (slugOf term)
                    entry.["posts"] <- box termPages
                    entry :> IDictionary<string, obj>)
                |> List.sortByDescending (fun d ->
                    match d.TryGetValue "posts" with true, (:? (IDictionary<string,obj>[]) as ps) -> ps.Length | _ -> 0)
                |> Array.ofList

            result.["categories"] <- box (taxonomyTable "categories" (getAllCategories ()) (fun p -> p.Categories))
            result.["tagList"]    <- box (taxonomyTable "tags" (getAllTags ()) (fun p -> p.Tags))

            let boxed = result :> IDictionary<string, obj>
            _cachedCollectionsForNunjucks <- Some boxed
            boxed
