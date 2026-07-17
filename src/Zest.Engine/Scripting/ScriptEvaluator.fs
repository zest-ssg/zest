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

/// Evaluates .zest.fsx / .md files into Page records.
/// Optimized with content caching, static Regex, and filter/page-data caching.
module ScriptEvaluator =

    // ── Static compiled Regex (allocated once) ──────────────────────
    let private headingPattern = Regex(@"^#\s+(.+)$", RegexOptions.Compiled ||| RegexOptions.Multiline)

    // ── Nunjucks context caching (built once per build, shared across all pages) ──
    let mutable private cachedNunjucksSiteContext : (string * obj)[] option = None
    let mutable private cachedNunjucksGlobalDataHash = 0

    let internal resetNunjucksCache () =
        cachedNunjucksSiteContext <- None
        cachedNunjucksGlobalDataHash <- 0

    let private getNunjucksSiteContext (config: SiteConfig) (globalData: IDictionary<string, obj>) =
        let hash = globalData.GetHashCode() ^^^ config.GetHashCode()
        match cachedNunjucksSiteContext with
        | Some ctx when cachedNunjucksGlobalDataHash = hash -> ctx
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
            cachedNunjucksSiteContext <- Some result
            cachedNunjucksGlobalDataHash <- hash
            result

    // ── Filter registry caching (track registered engines) ─────────
    let mutable private registeredEngines = HashSet<string>()

    let private ensureFiltersRegistered (engine: ITemplateEngine) =
        let key = engine.GetHashCode().ToString()
        if registeredEngines.Add(key) then
            FilterRegistry.registerAllFilters engine

    /// Extract and render content HTML from script text (legacy Markdown fallback mode).
    let private renderContent (ext: string) (bodyText: string) (fullText: string) : string =
        match ext with
        | FileExtensions.Markdown | FileExtensions.MarkdownLong ->
            MarkdownEngine.toHtml bodyText
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
            MarkdownEngine.toHtml (String.concat "\n" lines)

    /// Render .njk / .liquid / .hbs / .mustache / .webc / .haml / .pug content
    /// pages using the Nunjucks engine (with pre-conversion as needed).
    let private renderNunjucksContent
        (bodyText: string)
        (config: SiteConfig)
        (globalData: IDictionary<string, obj>)
        (meta: ContentMeta)
        (slug: string)
        (filePath: string)
        (ext: string)
        : string =
        // Pre-process format-specific syntax before Nunjucks
        let templateText =
            match ext.ToLowerInvariant() with
            | FileExtensions.Handlebars -> HandlebarsMustacheConverter.convertHandlebars bodyText
            | FileExtensions.Mustache   -> HandlebarsMustacheConverter.convertMustache bodyText
            | FileExtensions.Liquid     ->
                // Liquid whitespace control: {%- → {%  and -%} → %}
                bodyText.Replace("{%-", "{%").Replace("-%}", " %}")
            | FileExtensions.WebC       ->
                // WebC SSR: strip script/webc:setup, normalize template tags
                let step1 = Regex.Replace(bodyText, @"<script[^>]*webc:setup[^>]*>.*?</script>", "", RegexOptions.Singleline)
                let step2 = Regex.Replace(step1, @"<template[^>]*webc:nocss[^>]*>", "<!-- webc:nocss -->")
                step2.Replace("</template>", "<!-- /webc -->")
            | FileExtensions.Haml       -> HamlConverter.convert bodyText
            | FileExtensions.Pug        -> PugConverter.convert bodyText
            | _                         -> bodyText
        match TemplateManager.getOrCreateEngine "nunjucks" {
            Engine = "nunjucks"
            EnableCache = true
            Extension = FileExtensions.Nunjucks
            Filters = []
        } with
        | Some engine ->
            ensureFiltersRegistered engine

            let pairs = ResizeArray<string * obj>()
            let siteCtx = getNunjucksSiteContext config globalData
            pairs.AddRange(siteCtx)
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
            match engine.Render templateText ctx with
            | Ok html -> html
            | Error err ->
                eprintfn "[Zest] Nunjucks error in content '%s': %O" filePath err
                templateText
        | None ->
            templateText

    let private resolveContentDir (config: SiteConfig) =
        Path.GetFullPath(
            Path.Combine(Directory.GetCurrentDirectory(),
                         config.EffectiveContentDir.TrimStart('.', '\\', '/')))

    let private computeSlug (filePath: string) (contentDir: string) =
        let relPath  = Path.GetRelativePath(contentDir, filePath)
        let rawSlug  =
            let fn = Path.GetFileNameWithoutExtension(filePath)
            if fn.EndsWith(".zest") then fn.[..fn.Length - 6] else fn
        relPath, rawSlug

    let private buildPageData (globalData: IDictionary<string, obj>) (meta: ContentMeta) =
        let d = Dictionary<string, obj>()
        for kv in globalData do d.[kv.Key] <- kv.Value
        for kv in meta.Extra   do d.[kv.Key] <- box kv.Value
        meta.Description |> Option.iter (fun v -> d.["description"] <- box v)
        d :> IDictionary<string, obj>

    /// Fast metadata extraction with pre-loaded text — avoids double File.ReadAllText.
    let extractMetaWithText (filePath: string) (config: SiteConfig) (text: string) : ContentPage option =
        try
            let ext        = Path.GetExtension(filePath).ToLowerInvariant()
            let contentDir = resolveContentDir config
            let relPath, rawSlug = computeSlug filePath contentDir
            let meta, bodyText = MetaParser.parse ext text
            let slug = PermalinkRouter.slugify rawSlug
            let title =
                meta.Title
                |> Option.orElse (
                    if ext = FileExtensions.Markdown || ext = FileExtensions.MarkdownLong then
                        let m = headingPattern.Match(bodyText)
                        if m.Success then Some(m.Groups.[1].Value.Trim()) else None
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

    /// Fast metadata extraction (no script execution), used for the first-pass collections API scan.
    /// Reads file content from disk.
    let extractMeta (filePath: string) (config: SiteConfig) : ContentPage option =
        try
            extractMetaWithText filePath config (File.ReadAllText(filePath))
        with ex ->
            eprintfn "[Zest] WARN: extractMeta failed for '%s': %s" filePath ex.Message
            None

    /// Build a ContentPage from batch-evaluated script HTML + pre-loaded text.
    /// Used by ContentPipeline to avoid re-reading file text.
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
            let meta, _ = MetaParser.parse ext text
            let slug = PermalinkRouter.slugify rawSlug
            let mergedData = Dictionary<string, obj>()
            for kv in globalData do mergedData.[kv.Key] <- kv.Value
            for kv in meta.Extra do mergedData.[kv.Key] <- box kv.Value
            meta.Description |> Option.iter (fun v -> mergedData.["description"] <- box v)
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
                    Date = meta.Date
                    Slug = slug }
        with ex ->
            Error(sprintf "Failed to build page '%s': %s" filePath ex.Message)

    /// Evaluate a single content file into a Page (returns Error on failure).
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

            let isScript = ScriptRunner.isPageScript ext text

            if isScript then
                PageQuery.setGlobalData globalData

                match ScriptRunner.evaluatePageScript text with
                | Ok htmlContent ->
                    let mergedData = Dictionary<string, obj>()
                    for kv in globalData         do mergedData.[kv.Key] <- kv.Value
                    for kv in meta.Extra         do mergedData.[kv.Key] <- box kv.Value
                    meta.Description |> Option.iter (fun v -> mergedData.["description"] <- box v)

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
                        | FileExtensions.Nunjucks | FileExtensions.Liquid | FileExtensions.Handlebars | FileExtensions.Mustache | FileExtensions.WebC | FileExtensions.Haml | FileExtensions.Pug -> renderNunjucksContent bodyText config globalData meta slug filePath ext
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
                            Date         = meta.Date
                            Slug         = slug }

            else
                let title =
                    meta.Title
                    |> Option.orElse (
                        if ext = FileExtensions.Markdown || ext = FileExtensions.MarkdownLong then
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
                    | FileExtensions.Nunjucks | FileExtensions.Liquid | FileExtensions.Handlebars | FileExtensions.Mustache | FileExtensions.WebC | FileExtensions.Haml | FileExtensions.Pug -> renderNunjucksContent bodyText config globalData meta slug filePath ext
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
                        Date         = meta.Date
                        Slug         = slug }

        with ex ->
            Error(sprintf "Failed to evaluate '%s': %s" filePath ex.Message)
