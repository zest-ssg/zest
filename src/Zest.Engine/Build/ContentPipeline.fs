namespace Zest.Engine.Build

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open System.Threading
open System.Threading.Tasks
open Zest.Engine
open Zest.Engine.Parsing
open Zest.Engine.Template
open Zest.Engine.Routing
open Zest.Engine.Scripting

/// Content discovery, evaluation, and output writing pipeline — fully parallelized.
module ContentPipeline =

    /// Process all content files: discover, evaluate, and write output.
    let internal processContent
        (contentDir: string)
        (outputDir: string)
        (config: SiteConfig)
        (globalData: IDictionary<string, obj>)
        (layouts: Map<string, string * string>)
        (includes: IDictionary<string, string>)
        =

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
                    (ext = ".zest.fsx" || ext = ".njk" || ext = ".liquid" || ext = ".hbs" || ext = ".mustache" || ext = ".webc" || ext = ".haml" || ext = ".pug" || ext = ".fsx" || ext = ".md" || ext = ".markdown")
                    && not (PathResolver.isExcluded contentDir f))
                |> Seq.distinct
                |> Seq.toArray

        // ── .html files: 11ty-style Nunjucks preprocessing ──
        // Like 11ty preprocesses .html through Liquid, Zest preprocesses
        // .html files through Nunjucks when they contain template syntax.
        if Directory.Exists contentDir then
            let htmlFiles = Directory.GetFiles(contentDir, "*.html", SearchOption.AllDirectories)
                            |> Array.filter (fun f -> not (PathResolver.isExcluded contentDir f))
            if htmlFiles.Length > 0 then
                Parallel.ForEach(htmlFiles, fun htmlFile ->
                    let relPath = Path.GetRelativePath(contentDir, htmlFile)
                    let destPath = Path.Combine(outputDir, relPath)
                    let destDir = Path.GetDirectoryName(destPath)
                    if destDir <> null then Directory.CreateDirectory(destDir) |> ignore
                    let content = File.ReadAllText(htmlFile)
                    // 11ty-style: preprocess .html through Nunjucks if template syntax detected
                    if content.Contains("{{") || content.Contains("{%") then
                        match TemplateManager.getOrCreateEngine "nunjucks" {
                            Engine = "nunjucks"
                            EnableCache = true
                            Extension = ".njk"
                            Filters = []
                        } with
                        | Some engine ->
                            let objDict = Collections.Generic.Dictionary<string, obj>()
                            objDict.["content"] <- box ""
                            match engine.Render content objDict with
                            | Ok rendered ->
                                let utf8 = System.Text.Encoding.UTF8
                                File.WriteAllText(destPath, rendered, utf8)
                            | Error _ ->
                                File.WriteAllText(destPath, content, System.Text.Encoding.UTF8)
                        | None ->
                            File.WriteAllText(destPath, content, System.Text.Encoding.UTF8)
                    else
                        File.WriteAllText(destPath, content, System.Text.Encoding.UTF8)
                    Interlocked.Increment(&processed) |> ignore) |> ignore

        let total = allFiles.Length

        // ── First pass: fast metadata extraction for collections API ──
        // Cache file text between extractMeta and evaluate to avoid double ReadAllText
        let fileContentCache = ConcurrentDictionary<string, string>()
        let metaPages =
            allFiles
            |> Array.choose (fun f ->
                try
                    match fileContentCache.TryGetValue(f) with
                    | true, text -> ScriptEvaluator.extractMetaWithText f config text
                    | _ ->
                        let text = File.ReadAllText(f)
                        fileContentCache.[f] <- text
                        ScriptEvaluator.extractMetaWithText f config text
                with _ -> None)
            |> Array.toList
        PageQuery.setAllPages metaPages
        ScriptRunner.resetSession ()

        let mdFiles  = allFiles |> Array.filter (fun f -> let e = Path.GetExtension(f).ToLowerInvariant() in e = ".md" || e = ".markdown")
        let fsxFiles = allFiles |> Array.filter (fun f -> let e = Path.GetExtension(f).ToLowerInvariant() in e <> ".md" && e <> ".markdown")

        let evalResults = ConcurrentBag<Result<ContentPage, string>>()

        // Markdown pages — skip cached in incremental mode, parallel evaluation
        let mdToEval =
            if config.EnableIncrementalBuild then
                mdFiles |> Array.filter (fun f ->
                    let mtime = File.GetLastWriteTimeUtc(f : string)
                    match BuildCache.buildCache.TryGetValue(f) with
                    | true, e when e.Mtime = mtime ->
                        Interlocked.Increment(&cached) |> ignore
                        false
                    | _ -> true)
            else mdFiles

        if mdToEval.Length > 0 then
            Parallel.ForEach(mdToEval, fun f ->
                try evalResults.Add(ScriptEvaluator.evaluate f config globalData)
                with ex -> errors.Add(sprintf "Failed '%s': %s" f ex.Message)) |> ignore

        // FSI scripts: batch evaluate in a single FSI process for performance
        let fsxResults =
            if fsxFiles.Length > 0 then
                let scriptsToEval =
                    fsxFiles
                    |> Array.choose (fun f ->
                        if config.EnableIncrementalBuild then
                            let mtime = File.GetLastWriteTimeUtc(f : string)
                            match BuildCache.buildCache.TryGetValue(f) with
                            | true, e when e.Mtime = mtime -> None
                            | _ ->
                                try
                                    let text = fileContentCache.GetOrAdd(f, fun _ -> File.ReadAllText(f))
                                    if ScriptRunner.isPageScript (Path.GetExtension(f).ToLowerInvariant()) text then
                                        Some (f, text)
                                    else None
                                with _ -> None
                        else
                            try
                                let text = fileContentCache.GetOrAdd(f, fun _ -> File.ReadAllText(f))
                                if ScriptRunner.isPageScript (Path.GetExtension(f).ToLowerInvariant()) text then
                                    Some (f, text)
                                else None
                            with _ -> None)
                    |> Array.toList

                if scriptsToEval.IsEmpty then
                    if not config.EnableIncrementalBuild then
                        Parallel.ForEach(fsxFiles, fun f ->
                            try evalResults.Add(ScriptEvaluator.evaluate f config globalData)
                            with ex -> errors.Add(sprintf "Failed '%s': %s" f ex.Message)) |> ignore
                    Map.empty
                else
                    let batchResults = ScriptRunner.evaluatePageScriptsBatch scriptsToEval
                    batchResults
            else Map.empty

        // Process batch results and evaluate non-page scripts individually — parallelized
        let processFsxFile f =
            try
                if config.EnableIncrementalBuild then
                    let mtime = File.GetLastWriteTimeUtc(f : string)
                    match BuildCache.buildCache.TryGetValue(f) with
                    | true, e when e.Mtime = mtime ->
                        Interlocked.Increment(&cached) |> ignore
                    | _ ->
                        let text = fileContentCache.GetOrAdd(f, fun _ -> File.ReadAllText(f))
                        match Map.tryFind f fsxResults with
                        | Some batchResult ->
                            match batchResult with
                            | Ok htmlContent -> evalResults.Add(ScriptEvaluator.buildPage f config globalData text htmlContent)
                            | Error evalErr ->
                                eprintfn "[Zest] WARN: Script evaluation failed '%s': %s — falling back to Markdown mode" f evalErr
                                evalResults.Add(ScriptEvaluator.evaluate f config globalData)
                        | None -> evalResults.Add(ScriptEvaluator.evaluate f config globalData)
                else
                    let text = fileContentCache.GetOrAdd(f, fun _ -> File.ReadAllText(f))
                    match Map.tryFind f fsxResults with
                    | Some batchResult ->
                        match batchResult with
                        | Ok htmlContent -> evalResults.Add(ScriptEvaluator.buildPage f config globalData text htmlContent)
                        | Error evalErr ->
                            eprintfn "[Zest] WARN: Script evaluation failed '%s': %s — falling back to Markdown mode" f evalErr
                            evalResults.Add(ScriptEvaluator.evaluate f config globalData)
                    | None -> evalResults.Add(ScriptEvaluator.evaluate f config globalData)
            with ex -> errors.Add(sprintf "Failed '%s': %s" f ex.Message)

        Parallel.ForEach(fsxFiles, fun f -> processFsxFile f) |> ignore

        // Write output — parallelized
        let mutable localProcessed = 0
        let mutable localCached    = 0
        Parallel.ForEach(evalResults, fun r ->
            match r with
            | Error e -> errors.Add(e)
            | Ok page ->
                let outPath = Path.Combine(outputDir, page.OutputPath)
                if config.EnableIncrementalBuild && not (BuildCache.needsRebuild page.SourcePath outPath) then
                    Interlocked.Increment(&localCached) |> ignore
                else
                    let replacements = BuildLayout.buildReplacements page config globalData
                    let layoutName   = page.Layout |> Option.defaultValue config.DefaultLayout
                    let finalHtml    = BuildLayout.applyLayout layoutName page.Content layouts replacements includes
                    let dir = Path.GetDirectoryName(outPath)
                    if dir <> null then Directory.CreateDirectory(dir) |> ignore
                    File.WriteAllText(outPath, finalHtml, System.Text.Encoding.UTF8)
                    BuildCache.updateCache page.SourcePath finalHtml
                    Interlocked.Increment(&localProcessed) |> ignore) |> ignore
        processed <- processed + localProcessed
        cached    <- cached + localCached

        // Collect any errors from the error bag
        for e in errors do
            evalResults.Add(Error e)

        struct(total, processed, cached, evalResults)
