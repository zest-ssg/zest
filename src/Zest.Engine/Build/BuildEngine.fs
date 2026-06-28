namespace Zest.Engine

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open Zest.Engine.Parsing
open Zest.Engine.Routing
open Zest.Engine.Scripting
open Zest.Engine.Zcss
open BuildHelpers
open BuildCache
open BuildData
open BuildAssets
open BuildLayout

/// 核心构建管线：内容发现 → 求值 → 布局应用 → 资产处理 → 输出。
module BuildEngine =

    let execute (config: SiteConfig) : BuildResult =
        let sw = Stopwatch.StartNew()
        let errors = ConcurrentBag<string>()
        let mutable processed = 0
        let mutable cached    = 0
        let mutable assets    = 0
        try
            ScriptRunner.resetSession()
            let root       = Directory.GetCurrentDirectory()
            let contentDir = resolveEffectiveContentDir root config
            let outputDir  = resolvePath root config.OutputDir
            let layoutsDir = resolvePath root config.LayoutsDir
            let dataDir    = resolvePath root config.DataDir
            let includesDir = resolvePath root config.IncludesDir

            Directory.CreateDirectory(outputDir) |> ignore
            // Load persistent cache for incremental builds
            if config.EnableIncrementalBuild then loadCache outputDir
            // Clean output directory before build to avoid stale files.
            if not config.EnableIncrementalBuild then
                try
                    for f in Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories) do
                        File.Delete(f)
                    for d in Directory.GetDirectories(outputDir) do
                        Directory.Delete(d, recursive = true)
                with _ -> ()
            let layouts    = loadLayouts layoutsDir
            let globalData = loadGlobalData dataDir
            let includes   = loadIncludes includesDir
            // Compute includes mtime for layout cache keying
            let includesMtime =
                if not (Directory.Exists includesDir) then DateTime.MinValue
                else
                    Directory.EnumerateFiles(includesDir, "*.*", SearchOption.AllDirectories)
                    |> Seq.map (fun f -> File.GetLastWriteTimeUtc(f).Ticks)
                    |> Seq.append [Directory.GetLastWriteTimeUtc(includesDir).Ticks]
                    |> Seq.max |> DateTime
            setIncludesMtime includesMtime
            ScriptRunner.setIncludes includes

            // If globalData came from cache we must clone it before mutation
            let globalData =
                let fresh = Dictionary<string, obj>()
                for kv in globalData do fresh.[kv.Key] <- kv.Value
                fresh :> IDictionary<string, obj>
            // 将站点配置注入 globalData，使脚本中 site_data "site.title" 等可用
            let gData = globalData :?> Dictionary<string, obj>
            gData.["site.title"]       <- box config.Title
            gData.["site.description"] <- box config.Description
            gData.["site.base_url"]    <- box config.BaseUrl
            gData.["site.author"]      <- box config.Author
            gData.["site.language"]    <- box config.Language
            gData.["site.version"]     <- box config.SiteVersion

            // Expose menu items in globalData
            for kv in config.Menus do
                let json =
                    kv.Value
                    |> List.map (fun m -> sprintf """{"label":"%s","url":"%s","weight":%d}""" m.Label m.Url m.Weight)
                    |> String.concat ","
                gData.["menu." + kv.Key] <- box ("[" + json + "]")

            ScriptRunner.setGlobalData globalData

            // ── 执行 _init.zest.fsx（项目根目录下的初始化脚本）────
            let initResult = InitEngine.run root globalData
            if initResult.HasErrors then
                for err in initResult.Errors do
                    eprintfn "[Zest] _init.zest.fsx: %s" err
                    errors.Add err
            for kv in initResult.GlobalData do
                if not (gData.ContainsKey kv.Key) then
                    gData.[kv.Key] <- kv.Value
            ScriptRunner.setGlobalData globalData

            let allFiles =
                if not (Directory.Exists contentDir) then
                    Directory.CreateDirectory(contentDir) |> ignore; [||]
                else
                    [| yield! Directory.GetFiles(contentDir, "*.zpage.fsx", SearchOption.AllDirectories)
                       yield! Directory.GetFiles(contentDir, "*.zhtml",    SearchOption.AllDirectories)
                       yield! Directory.GetFiles(contentDir, "*.znjk",      SearchOption.AllDirectories)
                       yield! Directory.GetFiles(contentDir, "*.fsx",      SearchOption.AllDirectories)
                       yield! Directory.GetFiles(contentDir, "*.md",       SearchOption.AllDirectories)
                       yield! Directory.GetFiles(contentDir, "*.markdown", SearchOption.AllDirectories) |]
                    |> Array.filter (fun f -> not (isExcluded contentDir f))
                    |> Array.distinct

            let total = allFiles.Length

            // ── 第一遍：快速提取所有页面元数据，填充 collections API ──
            let metaPages =
                allFiles
                |> Array.choose (fun f -> ScriptEvaluator.extractMeta f config)
                |> Array.toList
            ScriptRunner.setAllPages metaPages
            ScriptRunner.resetSession ()

            let mdFiles  = allFiles |> Array.filter (fun f -> let e = Path.GetExtension(f).ToLowerInvariant() in e = ".md" || e = ".markdown")
            let fsxFiles = allFiles |> Array.filter (fun f -> let e = Path.GetExtension(f).ToLowerInvariant() in e <> ".md" && e <> ".markdown")

            let evalResults = ConcurrentBag<Result<Page, string>>()

            // Markdown pages — skip cached in incremental mode
            let mdToEval =
                if config.EnableIncrementalBuild then
                    mdFiles |> Array.filter (fun f ->
                        let mtime = File.GetLastWriteTimeUtc(f)
                        match buildCache.TryGetValue(f) with
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
                                let mtime = File.GetLastWriteTimeUtc(f)
                                match buildCache.TryGetValue(f) with
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
            for f in fsxFiles do
                if config.EnableIncrementalBuild then
                    let mtime = File.GetLastWriteTimeUtc(f)
                    match buildCache.TryGetValue(f) with
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
                                    let meta, _ = FrontMatterParser.parse ext text
                                    let mergedData = Dictionary<string, obj>()
                                    for kv in globalData do mergedData.[kv.Key] <- kv.Value
                                    for kv in meta.Extra do mergedData.[kv.Key] <- box kv.Value
                                    meta.Description |> Option.iter (fun v -> mergedData.["description"] <- box v)
                                    let url, outputPath =
                                        match meta.Permalink with
                                        | Some p when p.Length > 0 -> PermalinkRouter.computePermalink p
                                        | _ -> PermalinkRouter.defaultRoute relPath slug
                                    evalResults.Add(Ok { Page.empty with
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
                                eprintfn "[Zest] WARN: 脚本求值失败 '%s'：%s — 回退到 Markdown 模式" f evalErr
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
                                let meta, _ = FrontMatterParser.parse ext text
                                let mergedData = Dictionary<string, obj>()
                                for kv in globalData do mergedData.[kv.Key] <- kv.Value
                                for kv in meta.Extra do mergedData.[kv.Key] <- box kv.Value
                                meta.Description |> Option.iter (fun v -> mergedData.["description"] <- box v)
                                let url, outputPath =
                                    match meta.Permalink with
                                    | Some p when p.Length > 0 -> PermalinkRouter.computePermalink p
                                    | _ -> PermalinkRouter.defaultRoute relPath slug
                                evalResults.Add(Ok { Page.empty with
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
                            eprintfn "[Zest] WARN: 脚本求值失败 '%s'：%s — 回退到 Markdown 模式" f evalErr
                            try evalResults.Add(ScriptEvaluator.evaluate f config globalData)
                            with ex -> errors.Add(sprintf "Failed '%s': %s" f ex.Message)
                    | None ->
                        try evalResults.Add(ScriptEvaluator.evaluate f config globalData)
                        with ex -> errors.Add(sprintf "Failed '%s': %s" f ex.Message)

            // Write output
            for r in evalResults do
                match r with
                | Error e -> errors.Add(e)
                | Ok page ->
                    let outPath = Path.Combine(outputDir, page.OutputPath)
                    if config.EnableIncrementalBuild && not (needsRebuild page.SourcePath outPath) then
                        Threading.Interlocked.Increment(&cached) |> ignore
                    else
                        let replacements = buildReplacements page config globalData
                        let layoutName   = page.Layout |> Option.defaultValue config.DefaultLayout
                        let finalHtml    = applyLayout layoutName page.Content layouts replacements includes
                        let dir = Path.GetDirectoryName(outPath)
                        if dir <> null then Directory.CreateDirectory(dir) |> ignore
                        File.WriteAllText(outPath, finalHtml, System.Text.Encoding.UTF8)
                        updateCache page.SourcePath finalHtml
                        Threading.Interlocked.Increment(&processed) |> ignore

            assets <- copyAssets root outputDir
            if config.EnableIncrementalBuild then saveCache outputDir
            sw.Stop()
            { TotalPages     = total
              ProcessedPages = processed
              CachedPages    = cached
              AssetsCopied   = assets
              AssetsMinified = 0
              DurationMs     = sw.ElapsedMilliseconds
              Errors         = errors |> Seq.toList }
        with ex ->
            errors.Add(sprintf "Build failed: %s" ex.Message)
            sw.Stop()
            { TotalPages     = 0
              ProcessedPages = processed
              CachedPages    = cached
              AssetsCopied   = assets
              AssetsMinified = 0
              DurationMs     = sw.ElapsedMilliseconds
              Errors         = errors |> Seq.toList }
