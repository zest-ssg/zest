namespace Zest.Engine.Build

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open System.Threading
open System.Threading.Tasks
open Zest.Engine
open Zest.Engine.Parsing
open Zest.Engine.Routing
open Zest.Engine.Scripting

/// Content discovery, evaluation, and output writing pipeline.
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
                [| yield! Directory.GetFiles(contentDir, "*.zpage.fsx", SearchOption.AllDirectories)
                   yield! Directory.GetFiles(contentDir, "*.znjk",      SearchOption.AllDirectories)
                   yield! Directory.GetFiles(contentDir, "*.fsx",      SearchOption.AllDirectories)
                   yield! Directory.GetFiles(contentDir, "*.md",       SearchOption.AllDirectories)
                   yield! Directory.GetFiles(contentDir, "*.markdown", SearchOption.AllDirectories) |]
                |> Array.filter (fun f -> not (PathResolver.isExcluded contentDir f))
                |> Array.distinct

        // ── .html files: copy verbatim to output, no template processing ──
        if Directory.Exists contentDir then
            for htmlFile in Directory.GetFiles(contentDir, "*.html", SearchOption.AllDirectories) do
                if not (PathResolver.isExcluded contentDir htmlFile) then
                    let relPath = Path.GetRelativePath(contentDir, htmlFile)
                    let destPath = Path.Combine(outputDir, relPath)
                    let destDir = Path.GetDirectoryName(destPath)
                    if destDir <> null then Directory.CreateDirectory(destDir) |> ignore
                    File.Copy(htmlFile, destPath, true)
                    // Warn if template syntax is detected in .html files
                    let content = File.ReadAllText(htmlFile)
                    if content.Contains("{{") || content.Contains("{%") then
                        eprintfn "[Zest] WARN: '%s' contains Nunjucks template syntax, .html files do not support it — use .znjk instead" htmlFile
                    Threading.Interlocked.Increment(&processed) |> ignore

        let total = allFiles.Length

        // ── First pass: fast metadata extraction for collections API ──
        let metaPages =
            allFiles
            |> Array.choose (fun f -> ScriptEvaluator.extractMeta f config)
            |> Array.toList
        PageQuery.setAllPages metaPages
        ScriptRunner.resetSession ()

        let mdFiles  = allFiles |> Array.filter (fun f -> let e = Path.GetExtension(f).ToLowerInvariant() in e = ".md" || e = ".markdown")
        let fsxFiles = allFiles |> Array.filter (fun f -> let e = Path.GetExtension(f).ToLowerInvariant() in e <> ".md" && e <> ".markdown")

        let evalResults = ConcurrentBag<Result<ContentPage, string>>()

        // Markdown pages — skip cached in incremental mode
        let mdToEval =
            if config.EnableIncrementalBuild then
                mdFiles |> Array.filter (fun f ->
                    let mtime = File.GetLastWriteTimeUtc(f : string)
                    match BuildCache.buildCache.TryGetValue(f) with
                    | true, e when e.Mtime = mtime ->
                        Threading.Interlocked.Increment(&cached) |> ignore
                        false
                    | _ -> true)
            else mdFiles
        if config.EnableParallelBuild && mdToEval.Length > 0 then
            Parallel.ForEach(mdToEval, fun f ->
                try evalResults.Add(ScriptEvaluator.evaluate f config globalData)
                with ex -> errors.Add(sprintf "Failed '%s': %s" f ex.Message)) |> ignore
        else
            for f in mdToEval do
                try evalResults.Add(ScriptEvaluator.evaluate f config globalData)
                with ex -> errors.Add(sprintf "Failed '%s': %s" f ex.Message)

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
                                    let text = File.ReadAllText(f)
                                    if ScriptRunner.isPageScript (Path.GetExtension(f).ToLowerInvariant()) text then
                                        Some (f, text)
                                    else None
                                with _ -> None
                        else
                            try
                                let text = File.ReadAllText(f)
                                if ScriptRunner.isPageScript (Path.GetExtension(f).ToLowerInvariant()) text then
                                    Some (f, text)
                                else None
                            with _ -> None)
                    |> Array.toList

                if scriptsToEval.IsEmpty then
                    if not config.EnableIncrementalBuild then
                        for f in fsxFiles do
                            try evalResults.Add(ScriptEvaluator.evaluate f config globalData)
                            with ex -> errors.Add(sprintf "Failed '%s': %s" f ex.Message)
                    Map.empty
                else
                    let batchResults = ScriptRunner.evaluatePageScriptsBatch scriptsToEval
                    batchResults
            else Map.empty

        // Process batch results and evaluate non-page scripts individually
        let processFsxFile f =
            if config.EnableIncrementalBuild then
                let mtime = File.GetLastWriteTimeUtc(f : string)
                match BuildCache.buildCache.TryGetValue(f) with
                | true, e when e.Mtime = mtime ->
                    Threading.Interlocked.Increment(&cached) |> ignore
                | _ ->
                    match Map.tryFind f fsxResults with
                    | Some batchResult ->
                        match batchResult with
                        | Ok htmlContent ->
                            try
                                let text = File.ReadAllText(f)
                                let ext = Path.GetExtension(f).ToLowerInvariant()
                                let relPath, rawSlug =
                                    let rel = Path.GetRelativePath(contentDir, f)
                                    let fn = Path.GetFileNameWithoutExtension(f)
                                    let fn2 = if fn.EndsWith(".zpage") then fn.[..fn.Length - 7] else fn
                                    let raw = if fn2.EndsWith(".zest") then fn2.[..fn2.Length - 6] else fn2
                                    rel, raw
                                let slug = PermalinkRouter.slugify rawSlug
                                let meta, _ = MetaParser.parse ext text
                                let mergedData = Dictionary<string, obj>()
                                for kv in globalData do mergedData.[kv.Key] <- kv.Value
                                for kv in meta.Extra do mergedData.[kv.Key] <- box kv.Value
                                meta.Description |> Option.iter (fun v -> mergedData.["description"] <- box v)
                                let url, outputPath =
                                    match meta.Permalink with
                                    | Some p when p.Length > 0 -> PermalinkRouter.computePermalink p
                                    | _ -> PermalinkRouter.defaultRoute relPath slug
                                evalResults.Add(Ok { ContentPage.empty with
                                                        SourcePath = f
                                                        Url = url
                                                        OutputPath = outputPath
                                                        Layout = Some (meta.Layout |> Option.defaultValue config.DefaultLayout)
                                                        Title = meta.Title |> Option.defaultValue rawSlug
                                                        Content = htmlContent
                                                        Data = mergedData
                                                        Permalink = meta.Permalink
                                                        Tags = meta.Tags
                                                        Date = meta.Date
                                                        Slug = slug })
                            with ex -> errors.Add(sprintf "Failed '%s': %s" f ex.Message)
                        | Error evalErr ->
                            eprintfn "[Zest] WARN: Script evaluation failed '%s': %s — falling back to Markdown mode" f evalErr
                            try evalResults.Add(ScriptEvaluator.evaluate f config globalData)
                            with ex -> errors.Add(sprintf "Failed '%s': %s" f ex.Message)
                    | None ->
                        try evalResults.Add(ScriptEvaluator.evaluate f config globalData)
                        with ex -> errors.Add(sprintf "Failed '%s': %s" f ex.Message)
            else
                match Map.tryFind f fsxResults with
                | Some batchResult ->
                    match batchResult with
                    | Ok htmlContent ->
                        try
                            let text = File.ReadAllText(f)
                            let ext = Path.GetExtension(f).ToLowerInvariant()
                            let relPath, rawSlug =
                                let rel = Path.GetRelativePath(contentDir, f)
                                let fn = Path.GetFileNameWithoutExtension(f)
                                let fn2 = if fn.EndsWith(".zpage") then fn.[..fn.Length - 7] else fn
                                let raw = if fn2.EndsWith(".zest") then fn2.[..fn2.Length - 6] else fn2
                                rel, raw
                            let slug = PermalinkRouter.slugify rawSlug
                            let meta, _ = MetaParser.parse ext text
                            let mergedData = Dictionary<string, obj>()
                            for kv in globalData do mergedData.[kv.Key] <- kv.Value
                            for kv in meta.Extra do mergedData.[kv.Key] <- box kv.Value
                            meta.Description |> Option.iter (fun v -> mergedData.["description"] <- box v)
                            let url, outputPath =
                                match meta.Permalink with
                                | Some p when p.Length > 0 -> PermalinkRouter.computePermalink p
                                | _ -> PermalinkRouter.defaultRoute relPath slug
                            evalResults.Add(Ok { ContentPage.empty with
                                                    SourcePath = f
                                                    Url = url
                                                    OutputPath = outputPath
                                                    Layout = Some (meta.Layout |> Option.defaultValue config.DefaultLayout)
                                                    Title = meta.Title |> Option.defaultValue rawSlug
                                                    Content = htmlContent
                                                    Data = mergedData
                                                    Permalink = meta.Permalink
                                                    Tags = meta.Tags
                                                    Date = meta.Date
                                                    Slug = slug })
                        with ex -> errors.Add(sprintf "Failed '%s': %s" f ex.Message)
                    | Error evalErr ->
                        eprintfn "[Zest] WARN: Script evaluation failed '%s': %s — falling back to Markdown mode" f evalErr
                        try evalResults.Add(ScriptEvaluator.evaluate f config globalData)
                        with ex -> errors.Add(sprintf "Failed '%s': %s" f ex.Message)
                | None ->
                    try evalResults.Add(ScriptEvaluator.evaluate f config globalData)
                    with ex -> errors.Add(sprintf "Failed '%s': %s" f ex.Message)

        for f in fsxFiles do
            processFsxFile f

        // Write output
        for r in evalResults do
            match r with
            | Error e -> errors.Add(e)
            | Ok page ->
                let outPath = Path.Combine(outputDir, page.OutputPath)
                if config.EnableIncrementalBuild && not (BuildCache.needsRebuild page.SourcePath outPath) then
                    Threading.Interlocked.Increment(&cached) |> ignore
                else
                    let replacements = BuildLayout.buildReplacements page config globalData
                    let layoutName   = page.Layout |> Option.defaultValue config.DefaultLayout
                    let finalHtml    = BuildLayout.applyLayout layoutName page.Content layouts replacements includes
                    let dir = Path.GetDirectoryName(outPath)
                    if dir <> null then Directory.CreateDirectory(dir) |> ignore
                    File.WriteAllText(outPath, finalHtml, System.Text.Encoding.UTF8)
                    BuildCache.updateCache page.SourcePath finalHtml
                    Threading.Interlocked.Increment(&processed) |> ignore

        // Collect any errors from the error bag
        for e in errors do
            evalResults.Add(Error e)

        struct(total, processed, cached, evalResults)
