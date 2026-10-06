namespace Zest.Compiler.Execution
open System
open System.Collections.Generic
open System.IO
open System.Text.RegularExpressions
open Zest.Compiler.Model
open Zest.Compiler.Build
open Zest.Compiler.Build
open Zest.Compiler.Rendering
open Zest.Compiler.Zestucks

/// Evaluates .zest.fsx / .md files into Page records.
/// Optimized with content caching, static Regex, and filter/page-data caching.
module PageEvaluator =

    // ── Static compiled Regex (allocated once) ──────────────────────
    let private headingPattern = Regex(@"^#\s+(.+)$", RegexOptions.Compiled ||| RegexOptions.Multiline)

    // ── Page defaults helper ───────────────────────────────────────
    /// Apply config.PageDefaults to a ContentMeta. First matching
    /// pattern wins (lower-index entries have higher priority).
    let private applyPageDefaults (config: SiteConfig) (filePath: string) (meta: ContentMeta) : ContentMeta =
        if List.isEmpty config.PageDefaults then meta
        else
            let fileName = Path.GetFileName(filePath)
            let relPath = Path.GetRelativePath(Directory.GetCurrentDirectory(), filePath).Replace('\\', '/')
            match config.PageDefaults
                  |> List.tryFind (fun def ->
                      let pattern = def.Path
                      pattern = fileName
                      || pattern = relPath
                      || (pattern.StartsWith("*.") && fileName.EndsWith(pattern.Substring(1), StringComparison.OrdinalIgnoreCase))
                      || (pattern.EndsWith("/*") && relPath.StartsWith(pattern.TrimEnd('/').TrimEnd('*')))) with
            | Some def ->
                let mutable m = meta
                for kv in def.Values do
                    m <- FrontMatterParser.applyDefault m kv.Key kv.Value
                m
            | None -> meta

    // ── Zestucks context caching (built once per build, shared across all pages) ──
    // Keyed by reference equality: the dictionary is mutated before evaluation
    // begins and never again, so a changed reference always means new content.
    let mutable private cachedZestucksSiteContext : (string * obj)[] option = None
    let mutable private cachedZestucksGlobalDataRef : IDictionary<string, obj> = null
    let mutable private cachedZestucksConfigRef : SiteConfig = Unchecked.defaultof<SiteConfig>

    let internal resetZestucksCache () =
        cachedZestucksSiteContext <- None
        cachedZestucksGlobalDataRef <- null
        cachedZestucksConfigRef <- Unchecked.defaultof<SiteConfig>

    let private getZestucksSiteContext (config: SiteConfig) (globalData: IDictionary<string, obj>) =
        match cachedZestucksSiteContext with
        | Some ctx when Object.ReferenceEquals(cachedZestucksGlobalDataRef, globalData)
                       && Object.ReferenceEquals(cachedZestucksConfigRef, config) -> ctx
        | _ ->
            let pairs = ResizeArray<string * obj>()
            pairs.Add("site.title",       box config.Title)
            pairs.Add("site.description", box config.Description)
            pairs.Add("site.base_url",    box config.BaseUrl)
            pairs.Add("site.version",     box config.SiteVersion)
            pairs.Add("site.author",      box config.Author)
            pairs.Add("site.language",    box config.Language)
            for kv in globalData do
                pairs.Add("site." + kv.Key, kv.Value)
            let result = pairs |> Seq.toArray
            cachedZestucksSiteContext <- Some result
            cachedZestucksGlobalDataRef <- globalData
            cachedZestucksConfigRef <- config
            result

    // ── Filter registry caching (track registered engines) ─────────
    let mutable private registeredEngines = HashSet<string>()

    let private ensureFiltersRegistered (engine: Engine) =
        let key = engine.GetHashCode().ToString()
        if registeredEngines.Add(key) then
            ZestucksFilters.registerAllFilters engine

    /// Extract and render content HTML from script text (legacy Markdown fallback mode).
    let private renderContent (ext: string) (bodyText: string) (fullText: string) : string =
        match ext with
        | FileTypes.Markdown | FileTypes.MarkdownLong ->
            Markdown.toHtml bodyText
        | _ ->
            let lines =
                fullText.Split('\n')
                |> Array.filter (fun l ->
                    let t = l.Trim()
                    not (t.StartsWith("//"))
                    && not (t.StartsWith("---"))
                    && not (t.StartsWith("#r "))
                    && not (t.StartsWith("#load ")))
                |> Array.skipWhile String.IsNullOrWhiteSpace
            Markdown.toHtml (String.concat "\n" lines)

    /// Render .ztk / .njk content pages with the Zestucks engine.
    let private renderZestucksContent
        (bodyText: string)
        (config: SiteConfig)
        (globalData: IDictionary<string, obj>)
        (meta: ContentMeta)
        (slug: string)
        (filePath: string)
        (ext: string)
        : string =
        let buildPairs () =
            let pairs = ResizeArray<string * obj>()
            let siteCtx = getZestucksSiteContext config globalData
            pairs.AddRange(siteCtx)
            // ── page.* ──────────────────────────────────────────
            pairs.Add("page.title", box (meta.Title |> Option.defaultValue slug))
            meta.Description |> Option.iter (fun v -> pairs.Add("page.description", box v))
            if not meta.Tags.IsEmpty then pairs.Add("page.tags", box (meta.Tags |> Array.ofList))
            if not meta.Categories.IsEmpty then pairs.Add("page.categories", box (meta.Categories |> Array.ofList))
            meta.Author |> Option.iter (fun v -> pairs.Add("page.author", box v))
            meta.Updated |> Option.iter (fun v -> pairs.Add("page.updated", box (v.ToString("yyyy-MM-dd"))))
            for kv in meta.Extra do
                pairs.Add("page." + kv.Key, box kv.Value)
            // ── Zest collection data ────────────────────────────
            pairs.Add("pages", box (PageStore.getPagesForZestucks () |> Array.map box))
            pairs.Add("tags", box (PageStore.getTagsForZestucks ()))
            pairs.Add("collections", box (PageStore.getCollectionsForZestucks ()))
            EngineHost.buildContext pairs
        let templateText = bodyText
        let engine = EngineHost.instance
        ensureFiltersRegistered engine
        let ctx = buildPairs ()
        match engine.Render templateText ctx with
        | Ok html -> html
        | Error err ->
            eprintfn "[Zest] Zestucks error in content '%s': %O" filePath err
            templateText

    let private resolveContentDir (config: SiteConfig) =
        SitePaths.resolveContentDir (Directory.GetCurrentDirectory()) config

    let private computeSlug (filePath: string) (contentDir: string) =
        let relPath  = Path.GetRelativePath(contentDir, filePath)
        let rawSlug  =
            let fn = Path.GetFileNameWithoutExtension(filePath)
            if fn.EndsWith(".zest") then fn.[..fn.Length - 6] else fn
        relPath, rawSlug

    /// Copy front-matter-derived fields into the page data dictionary so
    /// Zestucks templates can address `page.tags`, `page.categories`,
    /// `page.author`, and `page.updated` with native array/string values.
    let applyMetaFields (d: IDictionary<string, obj>) (meta: ContentMeta) =
        if not meta.Tags.IsEmpty then d.["tags"] <- box (meta.Tags |> Array.ofList)
        if not meta.Categories.IsEmpty then d.["categories"] <- box (meta.Categories |> Array.ofList)
        meta.Author |> Option.iter (fun v -> d.["author"] <- box v)
        meta.Updated |> Option.iter (fun v -> d.["updated"] <- box (v.ToString("yyyy-MM-dd")))
        meta.Description |> Option.iter (fun v -> d.["description"] <- box v)

    let private buildPageData (globalData: IDictionary<string, obj>) (meta: ContentMeta) =
        let d = Dictionary<string, obj>()
        for kv in globalData do d.[kv.Key] <- kv.Value
        for kv in meta.Extra   do d.[kv.Key] <- box kv.Value
        applyMetaFields d meta
        d :> IDictionary<string, obj>

    /// Fast metadata extraction with pre-loaded text — avoids double File.ReadAllText.
    let extractMetaWithText (filePath: string) (config: SiteConfig) (text: string) : ContentPage option =
        try
            let ext        = Path.GetExtension(filePath).ToLowerInvariant()
            let contentDir = resolveContentDir config
            let relPath, rawSlug = computeSlug filePath contentDir
            let meta, bodyText = FrontMatterParser.parse ext text
            let meta = applyPageDefaults config filePath meta
            let slug = PermalinkRouter.slugify rawSlug
            let title =
                meta.Title
                |> Option.orElse (
                    if ext = FileTypes.Markdown || ext = FileTypes.MarkdownLong then
                        let m = headingPattern.Match(bodyText)
                        if m.Success then Some(m.Groups.[1].Value.Trim()) else None
                    else None)
                |> Option.defaultValue rawSlug
            let url, outputPath =
                match meta.Permalink with
                | Some p when p.Length > 0 -> PermalinkRouter.computePermalink p
                | _ -> PermalinkRouter.defaultRoute relPath slug
            let d = Dictionary<string, obj>()
            applyMetaFields d meta
            for kv in meta.Extra do d.[kv.Key] <- box kv.Value
            Some { ContentPage.empty with
                    SourcePath = filePath
                    Url        = url
                    OutputPath = outputPath
                    Title      = title
                    Slug       = slug
                    Tags       = meta.Tags
                    Categories = meta.Categories
                    Date       = meta.Date
                    Updated    = meta.Updated
                    Draft      = meta.Draft
                    Data       = d :> IDictionary<string, obj> }
        with ex ->
            eprintfn "[Zest] WARN: extractMeta failed for '%s': %s" filePath ex.Message
            None

    /// Fast metadata extraction (no script execution), used for the first-pass collections API scan.
    /// Reads file content from disk.
    let extractMeta (filePath: string) (config: SiteConfig) : ContentPage option =
        try
            extractMetaWithText filePath config (File.ReadAllText(filePath))
        with ex ->
            eprintfn "[Zest] WARN: extractMeta failed for '%s': %s" filePath ex.Message
            None

    /// Build a ContentPage from batch-evaluated script HTML + pre-loaded text.
    /// Used by PagePipeline to avoid re-reading file text.
    let buildPage
        (filePath: string)
        (config: SiteConfig)
        (globalData: IDictionary<string, obj>)
        (text: string)
        (htmlContent: string)
        : Result<ContentPage, string> =
        try
            let ext        = Path.GetExtension(filePath).ToLowerInvariant()
            let contentDir = resolveContentDir config
            let relPath, rawSlug = computeSlug filePath contentDir
            let meta, _ = FrontMatterParser.parse ext text
            let meta = applyPageDefaults config filePath meta
            let slug = PermalinkRouter.slugify rawSlug
            let mergedData = buildPageData globalData meta
            let url, outputPath =
                match meta.Permalink with
                | Some p when p.Length > 0 -> PermalinkRouter.computePermalink p
                | _ -> PermalinkRouter.defaultRoute relPath slug
            Ok { ContentPage.empty with
                    SourcePath = filePath
                    Url = url
                    OutputPath = outputPath
                    Layout = Some (meta.Layout |> Option.defaultValue config.DefaultLayout)
                    Title = meta.Title |> Option.defaultValue rawSlug
                    Content = htmlContent
                    Data = mergedData
                    Permalink = meta.Permalink
                    Tags = meta.Tags
                    Categories = meta.Categories
                    Date = meta.Date
                    Updated = meta.Updated
                    Draft = meta.Draft
                    Slug = slug }
        with ex ->
            Error(sprintf "Failed to build page '%s': %s" filePath ex.Message)

    /// Evaluate a content file using pre-loaded text — avoids redundant disk I/O
    /// when the caller (e.g. PagePipeline) already has the file text cached.
    let evaluateWithText
        (filePath:   string)
        (config:     SiteConfig)
        (globalData: IDictionary<string, obj>)
        (text:       string)
        : Result<ContentPage, string> =

        try
            let ext        = Path.GetExtension(filePath).ToLowerInvariant()
            let contentDir = resolveContentDir config

            let relPath, rawSlug = computeSlug filePath contentDir
            let meta, bodyText = FrontMatterParser.parse ext text
            let meta = applyPageDefaults config filePath meta
            let slug = PermalinkRouter.slugify rawSlug

            let isScript = FsiRunner.isPageScript filePath text

            if isScript then
                PageStore.setGlobalData globalData

                match FsiRunner.evaluatePageScript text with
                | Ok htmlContent ->
                    let mergedData = buildPageData globalData meta

                    let finalPermalink = meta.Permalink
                    let url, outputPath =
                        match finalPermalink with
                        | Some p when p.Length > 0 -> PermalinkRouter.computePermalink p
                        | _                         -> PermalinkRouter.defaultRoute relPath slug

                    Ok { ContentPage.empty with
                            SourcePath   = filePath
                            Url          = url
                            OutputPath   = outputPath
                            // A `layout = "none"` directive opts out of the
                            // layout chain for self-contained pages (feeds,
                            // standalone scripts). The pipeline writes the
                            // raw rendered content when Layout is None.
                            Layout       =
                                match meta.Layout with
                                | Some l when l.Equals("none", StringComparison.OrdinalIgnoreCase) -> None
                                | other -> Some (other |> Option.defaultValue config.DefaultLayout)
                            Title        = meta.Title |> Option.defaultValue rawSlug
                            Content      = htmlContent
                            Data         = mergedData
                            Permalink    = finalPermalink
                            Tags         = meta.Tags
                            Categories   = meta.Categories
                            Date         = meta.Date
                            Updated      = meta.Updated
                            Draft        = meta.Draft
                            Slug         = slug }
                | Error evalErr ->
                    eprintfn "[Zest] WARN: Script evaluation failed '%s': %s — falling back to Markdown mode" filePath evalErr
                    let title =
                        meta.Title
                        |> Option.orElse (
                            let m = headingPattern.Match(bodyText)
                            if m.Success then Some(m.Groups.[1].Value.Trim()) else None)
                        |> Option.defaultValue rawSlug

                    let layout = meta.Layout |> Option.defaultValue config.DefaultLayout
                    let url, outputPath =
                        match meta.Permalink with
                        | Some p when p.Length > 0 -> PermalinkRouter.computePermalink p
                        | _                         -> PermalinkRouter.defaultRoute relPath slug

                    let contentHtml =
                        match ext with
                        | FileTypes.Zestucks | FileTypes.Nunjucks -> renderZestucksContent bodyText config globalData meta slug filePath ext
                        | _       -> renderContent ext bodyText text

                    Ok { ContentPage.empty with
                            SourcePath   = filePath
                            Url          = url
                            OutputPath   = outputPath
                            Layout       = Some layout
                            Title        = title
                            Content      = contentHtml
                            Data         = buildPageData globalData meta
                            Permalink    = meta.Permalink
                            Tags         = meta.Tags
                            Categories   = meta.Categories
                            Date         = meta.Date
                            Updated      = meta.Updated
                            Draft        = meta.Draft
                            Slug         = slug }

            else
                let title =
                    meta.Title
                    |> Option.orElse (
                        if ext = FileTypes.Markdown || ext = FileTypes.MarkdownLong then
                            let m = headingPattern.Match(bodyText)
                            if m.Success then Some(m.Groups.[1].Value.Trim()) else None
                        else None)
                    |> Option.defaultValue rawSlug

                let layout = meta.Layout |> Option.defaultValue config.DefaultLayout
                let url, outputPath =
                    match meta.Permalink with
                    | Some p when p.Length > 0 -> PermalinkRouter.computePermalink p
                    | _                         -> PermalinkRouter.defaultRoute relPath slug

                let contentHtml =
                    match ext with
                    | FileTypes.Zestucks | FileTypes.Nunjucks -> renderZestucksContent bodyText config globalData meta slug filePath ext
                    | _       -> renderContent ext bodyText text

                Ok { ContentPage.empty with
                        SourcePath   = filePath
                        Url          = url
                        OutputPath   = outputPath
                        Layout       = Some layout
                        Title        = title
                        Content      = contentHtml
                        ContentNodes = []
                        Data         = buildPageData globalData meta
                        Permalink    = meta.Permalink
                        Tags         = meta.Tags
                        Categories   = meta.Categories
                        Date         = meta.Date
                        Updated      = meta.Updated
                        Draft        = meta.Draft
                        Slug         = slug }

        with ex ->
            Error(sprintf "Failed to evaluate '%s': %s" filePath ex.Message)

    /// Evaluate a single content file into a Page (returns Error on failure).
    /// Reads file content from disk. Delegates to evaluateWithText after reading.
    let evaluate
        (filePath:   string)
        (config:     SiteConfig)
        (globalData: IDictionary<string, obj>)
        : Result<ContentPage, string> =

        try
            let text = File.ReadAllText(filePath)
            evaluateWithText filePath config globalData text
        with ex ->
            Error(sprintf "Failed to evaluate '%s': %s" filePath ex.Message)
