// PagePipeline.fs
//
// Content discovery, evaluation and output writing — fully parallelised.
//
// Structure: `processContent` is a sequence of named phases (discover, extract
// metadata, evaluate Markdown, evaluate F# pages, write). Each phase used to be
// inlined into one 300-line function, which made the incremental-cache decision
// appear in four places and drift.
//
// Dependencies: Zest.Compiler.Model, Zest.Compiler.Rendering, Zest.Compiler.Zestucks, Zest.Compiler.Execution

namespace Zest.Compiler.Build
open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open System.Threading
open System.Threading.Tasks
open Zest.Compiler.Model
open Zest.Compiler.Rendering
open Zest.Compiler.Zestucks
open Zest.Compiler.Execution

/// Outcome of the content pipeline.
type ContentPipelineResult =
    { /// Files the pipeline considered (content files plus `*.html`).
      TotalFiles: int
      /// Pages evaluated and written this build.
      Processed: int
      /// Pages skipped because the incremental cache still considered them fresh.
      Cached: int
      /// Pages that made it through the pipeline, for the generators that run
      /// afterwards.
      Pages: ContentPage list
      /// Failures. A non-empty list fails the build.
      Errors: string list }

/// Content discovery, evaluation, and output writing pipeline — fully parallelized.
module PagePipeline =

    /// Extensions routed by the content pipeline, excluding the Zest Page
    /// suffix handled separately below.
    ///
    /// `*.html` / `*.htm` are absent on purpose: they are not routed pages but
    /// native-mode Zestucks input, handled by `processHtmlFiles`.
    let private processableExts =
        [ FileTypes.Zestucks; FileTypes.Nunjucks
          FileTypes.Markdown; FileTypes.MarkdownLong ]

    /// <summary>
    /// True when a discovered file becomes a routed page.
    /// Only <c>*.zest.fsx</c> is a Zest Page; ordinary <c>*.fsx</c> scripts are
    /// ignored so helper/build scripts never generate a URL. The page test uses
    /// the whole file name because `Path.GetExtension "page.zest.fsx"` returns
    /// ".fsx" and cannot distinguish the two cases.
    /// </summary>
    let private isRoutedFile (path: string) =
        FileTypes.isZestPage path
        || (processableExts |> List.exists ((=) (Path.GetExtension(path).ToLowerInvariant())))

    /// File text, read once per build.
    ///
    /// Every phase needs a file's text: metadata extraction, the incremental
    /// decision, evaluation and the content hash. Re-reading it — or hashing it
    /// again — per phase was the single largest avoidable cost in a rebuild.
    type private FileCache() =
        let texts = ConcurrentDictionary<string, string>(StringComparer.Ordinal)
        member _.Text(path: string) =
            texts.GetOrAdd(path, fun p -> File.ReadAllText p)

    /// The single incremental-rebuild decision per file, memoised for the build.
    ///
    /// The decision used to be recomputed in four separate filters; each
    /// recomputation re-hashed the revision file, and a cache entry updated
    /// between two of them could make the same file simultaneously "needs
    /// rebuild" and "cached".
    let private rebuildDecisions (config: SiteConfig) (files: FileCache) =
        let decisions = ConcurrentDictionary<string, bool>(StringComparer.Ordinal)
        fun (path: string) ->
            if not config.EnableIncrementalBuild then true
            else decisions.GetOrAdd(path, fun p -> IncrementalCache.needsRebuildWithText p (files.Text p))

    /// Record a page evaluation result, routing the failure case into `errors`.
    let private recordResult (errors: ConcurrentBag<string>) (pages: ConcurrentBag<ContentPage>)
                             (result: Result<ContentPage, string>) =
        match result with
        | Ok page -> pages.Add page
        | Error e -> errors.Add e

    /// Route `*.html` / `*.htm` through the Zestucks compatibility layer so
    /// `{{ }}` and `{% %}` resolve against the full page and site context.
    /// Plain HTML with no template syntax is copied byte-for-byte.
    let private processHtmlFiles (contentDir: string) (outputDir: string) (config: SiteConfig)
                                 (globalData: IDictionary<string, obj>)
                                 (files: FileCache) (htmlFiles: string[])
                                 (progress: BuildProgress) (errors: ConcurrentBag<string>) : int =
        if htmlFiles.Length = 0 then 0
        else
            // Snapshot globalData for thread-safe iteration inside Parallel.ForEach.
            // Dictionary<K,V>.GetEnumerator is not safe for concurrent enumeration
            // (it can corrupt internal state even for read-only access).
            let gdSnapshot = globalData |> Seq.map (fun kv -> kv.Key, kv.Value) |> Seq.toArray
            let mutable count = 0
            Parallel.ForEach(htmlFiles, fun htmlFile ->
                try
                    let relPath = SitePaths.normalizeOutputRel (Path.GetRelativePath(contentDir, htmlFile))
                    // Resolved under outputDir and rejected if it escapes.
                    let destPath = SitePaths.assertWithinOutput outputDir relPath
                    let destDir = Path.GetDirectoryName(destPath)
                    if not (String.IsNullOrEmpty destDir) then Directory.CreateDirectory(destDir) |> ignore
                    let content = files.Text htmlFile
                    if content.Contains("{{") || content.Contains("{%") then
                        let engine = EngineHost.instance
                        // Full page + site context so HTML can reference
                        // {{ page.title }}, {{ site.* }}, pages, etc.
                        let pairs = ResizeArray<string * obj>()
                        for (key, value) in gdSnapshot do pairs.Add("site." + key, value)
                        pairs.Add("site.title", box config.Title)
                        pairs.Add("site.description", box config.Description)
                        pairs.Add("site.base_url", box config.BaseUrl)
                        pairs.Add("site.author", box config.Author)
                        pairs.Add("site.language", box config.Language)
                        let pageTitle =
                            match PageEvaluator.extractMetaWithText htmlFile config content with
                            | Some m when not (String.IsNullOrEmpty m.Title) -> m.Title
                            | _ -> Path.GetFileNameWithoutExtension htmlFile
                        pairs.Add("page.title", box pageTitle)
                        // The page's Data dictionary holds description plus every
                        // front-matter extra; surface them as page.* keys.
                        match PageEvaluator.extractMetaWithText htmlFile config content with
                        | Some m -> for kv in m.Data do pairs.Add("page." + kv.Key, kv.Value)
                        | None -> ()
                        pairs.Add("pages", box (PageStore.getPagesForZestucks () |> Array.map box))
                        pairs.Add("tags", box (PageStore.getTagsForZestucks ()))
                        pairs.Add("collections", box (PageStore.getCollectionsForZestucks ()))
                        let ctx = EngineHost.buildContext pairs
                        match engine.Render content ctx with
                        | Ok rendered ->
                            AtomicFile.write destPath (Text.Encoding.UTF8.GetBytes rendered)
                        | Error err ->
                            Diagnostics.error "[Zest] HTML template error in '%s': %O" htmlFile err
                            AtomicFile.write destPath (Text.Encoding.UTF8.GetBytes content)
                    else
                        AtomicFile.write destPath (Text.Encoding.UTF8.GetBytes content)
                    Interlocked.Increment(&count) |> ignore
                with ex ->
                    errors.Add(sprintf "Failed to process HTML '%s': %s" htmlFile ex.Message)
                    progress.IncErrors()) |> ignore
            progress.IncProcessed count
            // The pipeline reports one progress tick per generated file so the
            // bar matches TotalFiles, which counts HTML files too.
            count

    /// <summary>
    /// Process all content files: discover, evaluate, and write output.
    /// </summary>
    let internal processContent
        (contentDir: string)
        (outputDir: string)
        (config: SiteConfig)
        (globalData: IDictionary<string, obj>)
        (layouts: Map<string, string * string>)
        (includes: IDictionary<string, string>)
        (progress: BuildProgress)
        : ContentPipelineResult =

        // Wrap globalData for thread-safe concurrent enumeration.
        // Regular Dictionary.GetEnumerator corrupts when enumerated
        // concurrently; ConcurrentDictionary provides snapshot enumeration.
        let safeData = ConcurrentDictionary<string, obj>(globalData)
        let safeIncludes = ConcurrentDictionary<string, string>(includes)

        let errors = ConcurrentBag<string>()
        let files = FileCache()
        let needsRebuild = rebuildDecisions config files

        // ── Discovery: one file-system traversal, partitioned in memory ──
        let allHtml, routedFiles =
            if not (Directory.Exists contentDir) then
                Directory.CreateDirectory(contentDir) |> ignore
                [||], [||]
            else
                Directory.EnumerateFiles(contentDir, "*.*", SearchOption.AllDirectories)
                |> Seq.filter (fun f ->
                    not (FileTypes.isReservedFile f)
                    && not (SitePaths.isExcludedWithConfig contentDir config f))
                |> Seq.distinct
                |> Seq.toArray
                |> Array.partition (fun f ->
                    let e = Path.GetExtension(f).ToLowerInvariant()
                    e = FileTypes.Html || e = FileTypes.HtmlLong)

        let allFiles = routedFiles |> Array.filter isRoutedFile
        progress.TotalFiles <- allFiles.Length + allHtml.Length

        // ── `*.html`: native-mode Zestucks preprocessing ──
        let htmlProcessed = processHtmlFiles contentDir outputDir config globalData files allHtml progress errors

        let mutable processed = htmlProcessed
        let mutable cached = 0

        // ── First pass: metadata extraction for the collections API ──
        // Draft pages are excluded from the main page set so they never appear
        // in production builds. Files declaring `@paginate` are skipped too —
        // PaginationGenerator owns their URL entirely.
        progress.Phase <- BuildPhase.Discovering
        let paginateFiles = ConcurrentDictionary<string, bool>(StringComparer.Ordinal)
        let metaPages =
            allFiles
            |> Array.Parallel.map (fun f ->
                try
                    let text = files.Text f
                    f, PageEvaluator.extractMetaWithText f config text
                with ex ->
                    // A metadata failure means this file cannot become a page.
                    // It must be reported, not dropped: silently losing a page
                    // produces a site that is missing content with a green build.
                    errors.Add(sprintf "Failed to read metadata from '%s': %s" f ex.Message)
                    f, None)
            |> Array.choose (fun (f, metaOpt) ->
                metaOpt
                |> Option.filter (fun (page: ContentPage) ->
                    if page.Draft then false
                    elif page.Data.ContainsKey "paginate" then
                        paginateFiles.TryAdd(f, true) |> ignore
                        false
                    else true))
            |> Array.toList
        PageStore.setAllPages metaPages
        FsiRunner.resetSession ()

        // Pagination templates are excluded from normal evaluation and writing.
        let writable = allFiles |> Array.filter (fun f -> not (paginateFiles.ContainsKey f))
        let isMarkdown (f: string) =
            let e = Path.GetExtension(f).ToLowerInvariant()
            e = FileTypes.Markdown || e = FileTypes.MarkdownLong
        let mdFiles = writable |> Array.filter isMarkdown
        let fsxFiles = writable |> Array.filter (isMarkdown >> not)

        let pages = ConcurrentBag<ContentPage>()

        // ── Markdown pages ──
        progress.Phase <- BuildPhase.Evaluating
        let mdToEval =
            mdFiles
            |> Array.filter (fun f ->
                if needsRebuild f then true
                else
                    Interlocked.Increment(&cached) |> ignore
                    progress.IncCached()
                    false)

        if mdToEval.Length > 0 then
            Parallel.ForEach(mdToEval, fun f ->
                try
                    let result = PageEvaluator.evaluateWithText f config safeData (files.Text f)
                    recordResult errors pages result
                    progress.IncProcessed()
                with ex ->
                    errors.Add(sprintf "Failed '%s': %s" f ex.Message)
                    progress.IncErrors()) |> ignore

        // ── F# pages: batched in a single FSI process ──
        let batchResults =
            if fsxFiles.Length = 0 then Map.empty
            else
                let scriptsToEval =
                    fsxFiles
                    |> Array.choose (fun f ->
                        try
                            let text = files.Text f
                            if not (needsRebuild f) then None
                            elif FsiRunner.isPageScript f text then Some(f, text)
                            else None
                        with ex ->
                            errors.Add(sprintf "Failed to read '%s': %s" f ex.Message)
                            None)
                    |> Array.toList

                if not scriptsToEval.IsEmpty then
                    FsiRunner.evaluatePageScriptsBatch scriptsToEval
                else
                    // Nothing to batch, but non-page scripts (or a build with
                    // incremental rebuilds disabled) still need evaluating.
                    if not config.EnableIncrementalBuild then
                        Parallel.ForEach(fsxFiles, fun f ->
                            try
                                let result = PageEvaluator.evaluateWithText f config safeData (files.Text f)
                                recordResult errors pages result
                                progress.IncProcessed()
                            with ex ->
                                errors.Add(sprintf "Failed '%s': %s" f ex.Message)
                                progress.IncErrors()) |> ignore
                    Map.empty

        let processFsxFile (f: string) =
            try
                if not (needsRebuild f) then
                    Interlocked.Increment(&cached) |> ignore
                    progress.IncCached()
                else
                    match Map.tryFind f batchResults with
                    | Some (Ok htmlContent) ->
                        recordResult errors pages
                            (PageEvaluator.buildPage f config safeData (files.Text f) htmlContent)
                    | Some (Error evalErr) ->
                        Diagnostics.warn
                            "[Zest] Script evaluation failed for '%s': %s — falling back to Markdown mode."
                            f evalErr
                        recordResult errors pages
                            (PageEvaluator.evaluateWithText f config safeData (files.Text f))
                    | None ->
                        recordResult errors pages
                            (PageEvaluator.evaluateWithText f config safeData (files.Text f))
                    progress.IncProcessed()
            with ex ->
                errors.Add(sprintf "Failed '%s': %s" f ex.Message)
                progress.IncErrors()

        Parallel.ForEach(fsxFiles, fun f -> processFsxFile f) |> ignore

        // ── Write ──
        progress.Phase <- BuildPhase.Writing
        let rebuildPages =
            pages
            |> Seq.filter (fun page ->
                if needsRebuild page.SourcePath then true
                else
                    Interlocked.Increment(&cached) |> ignore
                    progress.IncCached()
                    false)
            |> Seq.toArray

        // One batched layout pass over every page that needs a rebuild
        // (FSI per chain level) instead of re-entering FSI per page.
        let batchedHtml =
            if rebuildPages.Length = 0 then Map.empty
            else
                let tasks =
                    rebuildPages
                    |> Seq.map (fun p -> p, (p.Layout |> Option.defaultValue config.DefaultLayout))
                    |> Seq.toList
                LayoutChain.applyLayoutsBatched tasks layouts safeIncludes config safeData

        // Output shaping is deliberately absent here: HTML pretty-printing and
        // minification are post-build work performed by _finalize.fsx, which
        // sees the finished output tree instead of a per-page temp result.
        let mutable written = 0
        Parallel.ForEach(rebuildPages, fun page ->
            try
                let outPath = SitePaths.assertWithinOutput outputDir page.OutputPath
                let dir = Path.GetDirectoryName outPath
                if not (String.IsNullOrEmpty dir) then Directory.CreateDirectory dir |> ignore
                let layoutName = page.Layout |> Option.defaultValue config.DefaultLayout
                let finalHtml =
                    match batchedHtml.TryFind page.SourcePath with
                    | Some html ->
                        // Record every template this page rendered through: each
                        // level of the layout chain plus the includes those
                        // layouts reference. A later edit to any of them then
                        // scopes the rebuild to exactly the affected pages.
                        let includePaths = LayoutChain.getIncludePathMap ()
                        for (lname, lpath, _) in LayoutChain.layoutChain layoutName layouts do
                            IncrementalCache.recordDependency page.SourcePath lpath
                            match layouts.TryFind lname with
                            | Some (_, ltext) ->
                                for includePath in LayoutChain.collectIncludePaths ltext safeIncludes includePaths do
                                    IncrementalCache.recordDependency page.SourcePath includePath
                            | None -> ()
                        html
                    | None ->
                        // No layout result (missing top layout) — write raw content.
                        page.Content
                AtomicFile.write outPath (Text.Encoding.UTF8.GetBytes finalHtml)
                IncrementalCache.updateCacheWithHash page.SourcePath outPath finalHtml (files.Text page.SourcePath)
                Interlocked.Increment(&written) |> ignore
            with ex ->
                // A single page must never abort the whole build.
                errors.Add(sprintf "Failed to write '%s': %s" page.SourcePath ex.Message)
                progress.IncErrors()) |> ignore
        processed <- processed + written

        { TotalFiles = allFiles.Length + allHtml.Length
          Processed = processed
          Cached = cached
          Pages = pages |> Seq.toList
          Errors = errors |> Seq.toList }
