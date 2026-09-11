namespace Zest.Engine.Build

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open System.Threading
open System.Threading.Tasks
open Zest.Engine
open Zest.Engine.Html
open Zest.Engine.Parsing
open Zest.Engine.Template
open Zest.Engine.Routing
open Zest.Engine.Scripting

/// Content discovery, evaluation, and output writing pipeline — fully parallelized.
module ContentPipeline =

    /// Extensions processed by the content pipeline.
    /// Excludes .html — HTML is handled separately (native-mode Nunjucks preprocessing).
    let private processableExts =
        [ FileExtensions.ZestScript; FileExtensions.Nunjucks; FileExtensions.Liquid
          FileExtensions.Handlebars; FileExtensions.Mustache; FileExtensions.WebC
          FileExtensions.Haml; FileExtensions.Pug; FileExtensions.FSharpScript
          FileExtensions.Markdown; FileExtensions.MarkdownLong ]

    /// Process all content files: discover, evaluate, and write output.
    let internal processContent
        (contentDir: string)
        (outputDir: string)
        (config: SiteConfig)
        (globalData: IDictionary<string, obj>)
        (layouts: Map<string, string * string>)
        (includes: IDictionary<string, string>)
        (progress: BuildProgress)
        =

        // Wrap globalData for thread-safe concurrent enumeration.
        // Regular Dictionary.GetEnumerator corrupts when enumerated
        // concurrently; ConcurrentDictionary provides snapshot enumeration.
        let safeData = ConcurrentDictionary<string, obj>(globalData)
        let safeIncludes = ConcurrentDictionary<string, string>(includes)

        let errors = ConcurrentBag<string>()
        let mutable processed = 0
        let mutable cached    = 0

        let allFiles =
            if not (Directory.Exists contentDir) then
                Directory.CreateDirectory(contentDir) |> ignore; [||]
            else
                // Single file system traversal — enumerate once, filter in memory
                Directory.EnumerateFiles(contentDir, "*.*", SearchOption.AllDirectories)
                |> Seq.filter (fun f ->
                    let ext = Path.GetExtension(f).ToLowerInvariant()
                    (processableExts |> List.exists ((=) ext))
                    && not (PathResolver.isExcludedWithConfig contentDir config f))
                |> Seq.distinct
                |> Seq.toArray

        progress.TotalFiles <- allFiles.Length

        // ── .html files: native-mode Nunjucks preprocessing ──
        // In native mode, HTML files are routed through the Nunjucks compat
        // layer so `{{ }}` / `{% %}` syntax resolves against the full page +
        // site context (like .njk content). Plain HTML without template
        // syntax is copied verbatim.
        if Directory.Exists contentDir then
            let htmlFiles = Directory.GetFiles(contentDir, "*.html", SearchOption.AllDirectories)
                            |> Array.filter (fun f -> not (PathResolver.isExcludedWithConfig contentDir config f))
            if htmlFiles.Length > 0 then
                let engineCfg = { Engine = "nunjucks"; EnableCache = true; Extension = FileExtensions.Nunjucks; Filters = [] }
                // Snapshot globalData for thread-safe iteration inside Parallel.ForEach.
                // Dictionary<K,V>.GetEnumerator is not safe for concurrent enumeration
                // (can corrupt internal state even for read-only access across threads).
                let gdSnapshot = safeData |> Seq.map (fun kv -> kv.Key, kv.Value) |> Seq.toArray
                Parallel.ForEach(htmlFiles, fun htmlFile ->
                    let relPath = Path.GetRelativePath(contentDir, htmlFile)
                    let destPath = Path.Combine(outputDir, relPath)
                    let destDir = Path.GetDirectoryName(destPath)
                    if destDir <> null then Directory.CreateDirectory(destDir) |> ignore
                    let content = File.ReadAllText(htmlFile)
                    if content.Contains("{{") || content.Contains("{%") then
                        match TemplateManager.getOrCreateEngine "nunjucks" engineCfg with
                        | Some engine ->
                            // Build the full page + site context so HTML can
                            // reference {{ page.title }}, {{ site.* }}, pages, etc.
                            let pairs = ResizeArray<string * obj>()
                            for (key, value) in gdSnapshot do pairs.Add(key, value)
                            pairs.Add("site.title", box config.Title)
                            pairs.Add("site.description", box config.Description)
                            pairs.Add("site.base_url", box config.BaseUrl)
                            pairs.Add("site.author", box config.Author)
                            pairs.Add("site.language", box config.Language)
                            // Extract page meta (title/slug from frontmatter if present)
                            try
                                let meta = ScriptEvaluator.extractMetaWithText htmlFile config content
                                match meta with
                                | Some m ->
                                    let title =
                                        if String.IsNullOrEmpty m.Title then Path.GetFileNameWithoutExtension htmlFile
                                        else m.Title
                                    pairs.Add("page.title", box title)
                                    // Surface the page's Data dictionary (which holds
                                    // description + all frontmatter extras) as page.* keys.
                                    for kv in m.Data do pairs.Add("page." + kv.Key, box kv.Value)
                                | None -> ()
                            with _ -> ()
                            pairs.Add("pages", box (PageQuery.getPagesForNunjucks () |> Array.map box))
                            pairs.Add("tags", box (PageQuery.getTagsForNunjucks ()))
                            pairs.Add("collections", box (PageQuery.getCollectionsForNunjucks ()))
                            let ctx = TemplateManager.buildNestedContext pairs
                            match engine.Render content ctx with
                            | Ok rendered ->
                                // Atomic replace so the preview server's open
                                // read handle is never invalidated mid-stream.
                                AtomicFile.write destPath (System.Text.Encoding.UTF8.GetBytes rendered)
                            | Error _ ->
                                AtomicFile.write destPath (System.Text.Encoding.UTF8.GetBytes content)
                        | None ->
                            AtomicFile.write destPath (System.Text.Encoding.UTF8.GetBytes content)
                    else
                        AtomicFile.write destPath (System.Text.Encoding.UTF8.GetBytes content)
                    Interlocked.Increment(&processed) |> ignore
                    progress.IncProcessed()) |> ignore

        let total = allFiles.Length

        // ── First pass: fast metadata extraction for collections API ──
        // Parallel file read + metadata extraction.
        // The fileContentCache avoids double ReadAllText in later phases.
        // Draft pages (meta.Draft = true) are collected for _drafts but excluded
        // from the main page set to prevent them from appearing in production builds.
        // Files declaring `@paginate` are skipped here too — PaginationGenerator
        // takes over their URL entirely, so they must not surface as pages.
        progress.Phase <- BuildPhase.Discovering
        let fileContentCache = ConcurrentDictionary<string, string>()
        let draftPages = ConcurrentBag<ContentPage>()
        let paginateFiles = ConcurrentDictionary<string, bool>()
        let metaPages =
            // Parallel: read file + extract meta concurrently
            // Uses Partitioner for better chunk distribution than Parallel.ForEach on arrays.
            allFiles
            |> Array.Parallel.map (fun f ->
                try
                    let text = File.ReadAllText(f)
                    fileContentCache.[f] <- text
                    let meta = ScriptEvaluator.extractMetaWithText f config text
                    f, meta
                with _ -> f, None)
            |> Array.choose (fun (f, metaOpt) ->
                metaOpt |> Option.bind (fun (page: ContentPage) ->
                    if page.Draft then
                        draftPages.Add(page)
                        None
                    elif page.Data.ContainsKey "paginate" then
                        paginateFiles.TryAdd(f, true) |> ignore
                        None
                    else Some page))
            |> Array.toList
        PageQuery.setAllPages metaPages
        PageQuery.setDraftPages (draftPages |> Seq.toList)
        ScriptRunner.resetSession ()

        // Exclude pagination templates from normal evaluation/writing — the
        // PaginationGenerator owns their output paths.
        let paginatedAllFiles = allFiles |> Array.filter (fun f -> not (paginateFiles.ContainsKey f))

        let mdFiles  = paginatedAllFiles |> Array.filter (fun f -> let e = Path.GetExtension(f).ToLowerInvariant() in e = FileExtensions.Markdown || e = FileExtensions.MarkdownLong)
        let fsxFiles = paginatedAllFiles |> Array.filter (fun f -> let e = Path.GetExtension(f).ToLowerInvariant() in e <> FileExtensions.Markdown && e <> FileExtensions.MarkdownLong)

        let evalResults = ConcurrentBag<Result<ContentPage, string>>()

        // Markdown pages — skip cached in incremental mode, parallel evaluation
        progress.Phase <- BuildPhase.Evaluating
        let mdToEval =
            if config.EnableIncrementalBuild then
                mdFiles |> Array.filter (fun f ->
                    let text = fileContentCache.GetOrAdd(f, fun _ -> File.ReadAllText f)
                    if BuildCache.needsRebuildWithText f text then true
                    else
                        Interlocked.Increment(&cached) |> ignore
                        progress.IncCached()
                        false)
            else mdFiles

        if mdToEval.Length > 0 then
            Parallel.ForEach(mdToEval, fun f ->
                try
                    // Use cached text from first-pass metadata extraction
                    // to avoid a second File.ReadAllText on the same file.
                    let text = fileContentCache.GetOrAdd(f, fun _ -> File.ReadAllText(f))
                    evalResults.Add(ScriptEvaluator.evaluateWithText f config safeData text)
                    progress.IncProcessed()
                with ex ->
                    errors.Add(sprintf "Failed '%s': %s" f ex.Message)
                    progress.IncErrors()) |> ignore

        // FSI scripts: batch evaluate in a single FSI process for performance
        let fsxResults =
            if fsxFiles.Length > 0 then
                let scriptsToEval =
                    fsxFiles
                    |> Array.choose (fun f ->
                        try
                            let text = fileContentCache.GetOrAdd(f, fun _ -> File.ReadAllText(f))
                            if config.EnableIncrementalBuild && not (BuildCache.needsRebuildWithText f text) then None
                            elif ScriptRunner.isPageScript (Path.GetExtension(f).ToLowerInvariant()) text then
                                Some (f, text)
                            else None
                        with _ -> None)
                    |> Array.toList

                if scriptsToEval.IsEmpty then
                    if not config.EnableIncrementalBuild then
                        Parallel.ForEach(fsxFiles, fun f ->
                            try
                                let text = fileContentCache.GetOrAdd(f, fun _ -> File.ReadAllText(f))
                                evalResults.Add(ScriptEvaluator.evaluateWithText f config safeData text)
                                progress.IncProcessed()
                            with ex ->
                                errors.Add(sprintf "Failed '%s': %s" f ex.Message)
                                progress.IncErrors()) |> ignore
                    Map.empty
                else
                    let batchResults = ScriptRunner.evaluatePageScriptsBatch scriptsToEval
                    batchResults
            else Map.empty

        // Process batch results and evaluate non-page scripts individually — parallelized
        let processFsxFile f =
            try
                let text = fileContentCache.GetOrAdd(f, fun _ -> File.ReadAllText(f))
                if config.EnableIncrementalBuild && not (BuildCache.needsRebuildWithText f text) then
                    Interlocked.Increment(&cached) |> ignore
                    progress.IncCached()
                else
                    match Map.tryFind f fsxResults with
                    | Some batchResult ->
                        match batchResult with
                        | Ok htmlContent -> evalResults.Add(ScriptEvaluator.buildPage f config safeData text htmlContent)
                        | Error evalErr ->
                            eprintfn "[Zest] WARN: Script evaluation failed '%s': %s — falling back to Markdown mode" f evalErr
                            evalResults.Add(ScriptEvaluator.evaluateWithText f config safeData text)
                    | None -> evalResults.Add(ScriptEvaluator.evaluateWithText f config safeData text)
                    progress.IncProcessed()
            with ex ->
                errors.Add(sprintf "Failed '%s': %s" f ex.Message)
                progress.IncErrors()

        Parallel.ForEach(fsxFiles, fun f -> processFsxFile f) |> ignore

        // Write output — lock-safe atomic writes that never conflict with the
        // preview server's open read handles. Layout chains are applied to all
        // rebuild pages in ONE batched pass (FSI per chain level) instead of
        // re-entering FSI per page, which was the dominant build cost.
        let mutable localProcessed = 0
        let mutable localCached    = 0
        progress.Phase <- BuildPhase.Writing

        // Partition results first: failures surface as errors; pages that the
        // incremental cache still considers fresh are counted and skipped.
        let rebuildPages = ResizeArray<ContentPage>()
        for r in evalResults do
            match r with
            | Error e -> errors.Add(e); progress.IncErrors()
            | Ok page ->
                let srcText = fileContentCache.GetOrAdd(page.SourcePath, fun _ -> File.ReadAllText page.SourcePath)
                if config.EnableIncrementalBuild && not (BuildCache.needsRebuildWithText page.SourcePath srcText) then
                    Interlocked.Increment(&localCached) |> ignore
                else
                    rebuildPages.Add(page)

        // One batched layout pass over every page that needs a rebuild.
        let batchedHtml =
            if rebuildPages.Count = 0 then Map.empty
            else
                let tasks =
                    rebuildPages
                    |> Seq.map (fun p -> p, (p.Layout |> Option.defaultValue config.DefaultLayout))
                    |> Seq.toList
                LayoutEngine.applyLayoutsBatched tasks layouts safeIncludes config safeData

        // ── HTML post-processing pass ──
        // Pretty-print (enable_html_formatting) or minify
        // (enable_html_minification) the final HTML for pages that have a
        // layout result. Formatting takes priority when both flags are
        // enabled. Pages without a layout result keep their raw content
        // untouched, matching the previous write-time behaviour.
        let htmlPostProcess =
            if config.EnableHtmlFormatting then HtmlFormatter.formatDefault
            else HtmlFormatter.minifySafe
        let needsHtmlPostProcess = config.EnableHtmlFormatting || config.EnableHtmlMinification
        let processedHtml =
            if needsHtmlPostProcess then
                rebuildPages
                |> Seq.choose (fun page ->
                    match batchedHtml.TryFind page.SourcePath with
                    | Some html -> Some (page.SourcePath, htmlPostProcess html)
                    | None -> None)
                |> Map.ofSeq
            else Map.empty

        // Write each result in parallel. The content hash for the cache comes
        // from the first-pass file cache, so no second ReadAllText is needed.
        Parallel.ForEach(rebuildPages, fun page ->
            try
                let outPath = Path.Combine(outputDir, page.OutputPath)
                let dir = Path.GetDirectoryName outPath
                if dir <> null then Directory.CreateDirectory dir |> ignore
                let layoutName = page.Layout |> Option.defaultValue config.DefaultLayout
                match batchedHtml.TryFind page.SourcePath with
                | Some _ ->
                    // Record every template this page rendered through: each
                    // level of the layout chain plus the includes those layouts
                    // reference. A later edit to any of them then scopes the
                    // rebuild to exactly the affected pages.
                    let includePaths = LayoutEngine.getIncludePathMap ()
                    for (lname, lpath, _) in LayoutEngine.layoutChain layoutName layouts do
                        BuildCache.recordDependency page.SourcePath lpath
                        match layouts.TryFind lname with
                        | Some (_, ltext) ->
                            for includePath in LayoutEngine.collectIncludePaths ltext safeIncludes includePaths do
                                BuildCache.recordDependency page.SourcePath includePath
                        | None -> ()
                    let finalHtml =
                        if needsHtmlPostProcess then processedHtml.[page.SourcePath]
                        else batchedHtml.[page.SourcePath]
                    AtomicFile.write outPath (System.Text.Encoding.UTF8.GetBytes finalHtml)
                    let srcText = fileContentCache.GetOrAdd(page.SourcePath, fun _ -> File.ReadAllText page.SourcePath)
                    BuildCache.updateCacheWithHash page.SourcePath outPath finalHtml srcText
                    Interlocked.Increment(&localProcessed) |> ignore
                | None ->
                    // No layout result (missing top layout) — write the raw content.
                    AtomicFile.write outPath (System.Text.Encoding.UTF8.GetBytes page.Content)
                    let srcText = fileContentCache.GetOrAdd(page.SourcePath, fun _ -> File.ReadAllText page.SourcePath)
                    BuildCache.updateCacheWithHash page.SourcePath outPath page.Content srcText
                    Interlocked.Increment(&localProcessed) |> ignore
            with ex ->
                // A single page must never abort the whole build.
                errors.Add(sprintf "Failed to write '%s': %s" page.SourcePath ex.Message)
                progress.IncErrors()) |> ignore
        processed <- processed + localProcessed
        cached    <- cached + localCached

        // Collect any errors from the error bag
        for e in errors do
            evalResults.Add(Error e)

        struct(total, processed, cached, evalResults)
