namespace Zest.Engine.Scripting

open System
open System.Collections.Generic
open System.IO
open System.Text.RegularExpressions
open Zest.Engine
open Zest.Engine.Parsing
open Zest.Engine.Routing
open Zest.Engine.Html
open Zest.Engine.Template

/// Evaluates .zpage.fsx / .md files into Page records.
module ScriptEvaluator =

    /// Extract and render content HTML from script text (legacy Markdown fallback mode).
    let private renderContent (ext: string) (bodyText: string) (fullText: string) : string =
        match ext with
        | ".md" | ".markdown" ->
            MarkdownEngine.toHtml bodyText
        | _ ->
            // .zpage.fsx / .fsx: strip metadata comments and #r / #load directives, treat remainder as Markdown
            let lines =
                fullText.Split('\n')
                |> Array.filter (fun l ->
                    let t = l.Trim()
                    not (t.StartsWith("//"))
                    && not (t.StartsWith("---"))
                    && not (t.StartsWith("#r "))
                    && not (t.StartsWith("#load ")))
                |> Array.skipWhile String.IsNullOrWhiteSpace
            MarkdownEngine.toHtml (String.concat "\n" lines)

    /// Render .znjk content pages using the ZestNjk engine.
    /// Builds a nested context (site.*, page.*, etc.) so {{ site.title }} syntax works.
    /// Also injects Zest page data (pages, tags, collections) and registers Zest custom filters.
    let private renderNunjucksContent
        (bodyText: string)
        (config: SiteConfig)
        (globalData: IDictionary<string, obj>)
        (meta: ContentMeta)
        (slug: string)
        (filePath: string)
        : string =
        match TemplateManager.getOrCreateEngine "znjk" {
            Engine = "znjk"
            EnableCache = true
            Extension = ".znjk"
            Filters = []
        } with
        | Some engine ->
            // ── Register Zest custom filters ──────────────────
            FilterRegistry.registerAllFilters engine

            // ── Build context ──────────────────────────────────
            let pairs = ResizeArray<string * obj>()
            // ── site.* ──────────────────────────────────────────
            pairs.Add("site.title",       box config.Title)
            pairs.Add("site.description", box config.Description)
            pairs.Add("site.base_url",    box config.BaseUrl)
            pairs.Add("site.version",     box config.SiteVersion)
            pairs.Add("site.author",      box config.Author)
            pairs.Add("site.language",    box config.Language)
            for kv in globalData do
                pairs.Add("site." + kv.Key, kv.Value)
            // ── page.* ──────────────────────────────────────────
            pairs.Add("page.title", box (meta.Title |> Option.defaultValue slug))
            meta.Description |> Option.iter (fun v -> pairs.Add("page.description", box v))
            for kv in meta.Extra do
                pairs.Add("page." + kv.Key, box kv.Value)
            // ── Zest collection data ────────────────────────────
            pairs.Add("pages", box (PageQuery.getPagesForNunjucks () |> Array.map box))
            pairs.Add("tags", box (PageQuery.getTagsForNunjucks ()))
            pairs.Add("collections", box (PageQuery.getCollectionsForNunjucks ()))
            let ctx = TemplateManager.buildNestedContext pairs
            match engine.Render bodyText ctx with
            | Ok html -> html
            | Error err ->
                eprintfn "[Zest] Nunjucks error in content '%s': %O" filePath err
                bodyText
        | None ->
            // Return raw text when Nunjucks engine is unavailable
            bodyText

    /// Build the absolute path to the content directory.
    /// Uses EffectiveContentDir to support root directory management:
    /// - RootDir = "." → project root is the content directory
    /// - RootDir = "content" → use the content subdirectory
    let private resolveContentDir (config: SiteConfig) =
        Path.GetFullPath(
            Path.Combine(Directory.GetCurrentDirectory(),
                         config.EffectiveContentDir.TrimStart('.', '\\', '/')))

    /// Compute slug from file path.
    let private computeSlug (filePath: string) (contentDir: string) =
        let relPath  = Path.GetRelativePath(contentDir, filePath)
        let rawSlug  =
            let fn = Path.GetFileNameWithoutExtension(filePath)
            // Handle .zpage.fsx → slug without .zpage suffix
            let fn2 = if fn.EndsWith(".zpage") then fn.[..fn.Length - 7] else fn
            // Handle .zest → legacy fallback
            if fn2.EndsWith(".zest") then fn2.[..fn2.Length - 6] else fn2
        relPath, rawSlug

    /// Build pageData dictionary (global data + metadata extensions).
    let private buildPageData (globalData: IDictionary<string, obj>) (meta: ContentMeta) =
        let d = Dictionary<string, obj>()
        for kv in globalData do d.[kv.Key] <- kv.Value
        for kv in meta.Extra   do d.[kv.Key] <- box kv.Value
        meta.Description |> Option.iter (fun v -> d.["description"] <- box v)
        d :> IDictionary<string, obj>

    /// Fast metadata extraction (no script execution), used for the first-pass collections API scan.
    let extractMeta (filePath: string) (config: SiteConfig) : ContentPage option =
        try
            let text       = File.ReadAllText(filePath)
            let ext        = Path.GetExtension(filePath).ToLowerInvariant()
            let contentDir = resolveContentDir config
            let relPath, rawSlug = computeSlug filePath contentDir
            let meta, bodyText = MetaParser.parse ext text
            let slug = PermalinkRouter.slugify rawSlug
            let title =
                meta.Title
                |> Option.orElse (
                    if ext = ".md" || ext = ".markdown" then
                        Regex.Match(bodyText, @"^#\s+(.+)$", RegexOptions.Multiline)
                        |> fun m -> if m.Success then Some(m.Groups.[1].Value.Trim()) else None
                    else None)
                |> Option.defaultValue rawSlug
            let url, outputPath =
                match meta.Permalink with
                | Some p when p.Length > 0 -> PermalinkRouter.computePermalink p
                | _ -> PermalinkRouter.defaultRoute relPath slug
            let d = Dictionary<string, obj>()
            meta.Description |> Option.iter (fun v -> d.["description"] <- box v)
            for kv in meta.Extra do d.[kv.Key] <- box kv.Value
            Some { ContentPage.empty with
                    SourcePath = filePath
                    Url        = url
                    OutputPath = outputPath
                    Title      = title
                    Slug       = slug
                    Tags       = meta.Tags
                    Date       = meta.Date
                    Data       = d :> IDictionary<string, obj> }
        with ex ->
            eprintfn "[Zest] WARN: extractMeta failed for '%s': %s" filePath ex.Message
            None

    /// Evaluate a single content file into a Page (returns Error on failure).
    /// Auto-detects whether the file uses the page { ... } CE block and executes F# scripts accordingly.
    let evaluate
        (filePath:   string)
        (config:     SiteConfig)
        (globalData: IDictionary<string, obj>)
        : Result<ContentPage, string> =

        try
            let text       = File.ReadAllText(filePath)
            let ext        = Path.GetExtension(filePath).ToLowerInvariant()
            let contentDir = resolveContentDir config

            let relPath, rawSlug = computeSlug filePath contentDir
            let meta, bodyText = MetaParser.parse ext text
            let slug = PermalinkRouter.slugify rawSlug

            // ── Determine if this is a page { ... } F# script ──
            let isScript = ScriptRunner.isPageScript ext text

            if isScript then
                // ── Mode A: evaluate as F# script ───────────────
                // Inject global data so scripts can call PageQuery.getData etc.
                PageQuery.setGlobalData globalData

                match ScriptRunner.evaluatePageScript text with
                | Ok htmlContent ->
                    // Merge metadata
                    let mergedData = Dictionary<string, obj>()
                    for kv in globalData         do mergedData.[kv.Key] <- kv.Value
                    for kv in meta.Extra         do mergedData.[kv.Key] <- box kv.Value
                    meta.Description |> Option.iter (fun v -> mergedData.["description"] <- box v)

                    // Determine final URL / OutputPath
                    let finalPermalink = meta.Permalink
                    let url, outputPath =
                        match finalPermalink with
                        | Some p when p.Length > 0 -> PermalinkRouter.computePermalink p
                        | _                         -> PermalinkRouter.defaultRoute relPath slug

                    Ok { ContentPage.empty with
                            SourcePath   = filePath
                            Url          = url
                            OutputPath   = outputPath
                            Layout       = Some (meta.Layout |> Option.defaultValue config.DefaultLayout)
                            Title        = meta.Title |> Option.defaultValue rawSlug
                            Content      = htmlContent
                            Data         = mergedData
                            Permalink    = finalPermalink
                            Tags         = meta.Tags
                            Date         = meta.Date
                            Slug         = slug }
                | Error evalErr ->
                    // Script evaluation failed, fall back to Markdown
                    eprintfn "[Zest] WARN: Script evaluation failed '%s': %s — falling back to Markdown mode" filePath evalErr
                    // fallback to Markdown
                    let title =
                        meta.Title
                        |> Option.orElse (Regex.Match(bodyText, @"^#\s+(.+)$", RegexOptions.Multiline)
                                              |> fun m -> if m.Success then Some(m.Groups.[1].Value.Trim()) else None)
                        |> Option.defaultValue rawSlug

                    let layout = meta.Layout |> Option.defaultValue config.DefaultLayout
                    let url, outputPath =
                        match meta.Permalink with
                        | Some p when p.Length > 0 -> PermalinkRouter.computePermalink p
                        | _                         -> PermalinkRouter.defaultRoute relPath slug

                    let contentHtml =
                        match ext with
                        | ".znjk" ->
                            renderNunjucksContent bodyText config globalData meta slug filePath
                        | _ ->
                            renderContent ext bodyText text

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
                            Date         = meta.Date
                            Slug         = slug }

            else
                // ── Mode B: legacy Markdown / comment metadata mode ──
                let title =
                    meta.Title
                    |> Option.orElse (
                        if ext = ".md" || ext = ".markdown" then
                            Regex.Match(bodyText, @"^#\s+(.+)$", RegexOptions.Multiline)
                            |> fun m -> if m.Success then Some(m.Groups.[1].Value.Trim()) else None
                        else None)
                    |> Option.defaultValue rawSlug

                let layout = meta.Layout |> Option.defaultValue config.DefaultLayout
                let url, outputPath =
                    match meta.Permalink with
                    | Some p when p.Length > 0 -> PermalinkRouter.computePermalink p
                    | _                         -> PermalinkRouter.defaultRoute relPath slug

                let contentHtml =
                    match ext with
                    | ".znjk" ->
                        renderNunjucksContent bodyText config globalData meta slug filePath
                    | _ ->
                        renderContent ext bodyText text

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
                        Date         = meta.Date
                        Slug         = slug }

        with ex ->
            Error(sprintf "Failed to evaluate '%s': %s" filePath ex.Message)
