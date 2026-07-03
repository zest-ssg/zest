namespace Zest.Engine

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open System.Diagnostics
open Zest.Engine.Scripting
open Zest.Engine.Build
open PathResolver
open BuildCache
open BuildData
open BuildAssets
open BuildLayout

/// Core build pipeline with parallel content processing and optimised I/O.
module BuildEngine =

    let execute (config: SiteConfig) : BuildResult =
        let sw = Stopwatch.StartNew()
        let errors = ConcurrentBag<string>()
        let mutable processed = 0
        let mutable cached    = 0
        let mutable assets    = 0
        try
            ScriptRunner.resetSession()
            ScriptEvaluator.resetNunjucksCache()
            let root       = Directory.GetCurrentDirectory()
            let contentDir = resolveEffectiveContentDir root config
            let outputDir  = resolvePath root config.OutputDir
            let layoutsDir = resolvePath root config.LayoutsDir
            let dataDir    = resolvePath root config.DataDir
            let includesDir = resolvePath root config.IncludesDir

            Directory.CreateDirectory(outputDir) |> ignore
            // Load persistent cache for incremental builds
            if config.EnableIncrementalBuild then loadCache outputDir
            // Fast cleanup: delete and recreate to avoid per-file enumeration
            if not config.EnableIncrementalBuild then
                try Directory.Delete(outputDir, recursive = true); Directory.CreateDirectory(outputDir) |> ignore
                with _ -> ()

            // Load layouts & data in parallel (independent operations)
            let layouts    = loadLayouts layoutsDir
            let globalData = loadGlobalData dataDir
            let includes   = loadIncludes includesDir
            // ── includes mtime computed in loadIncludes now via single traversal ──
            let includesMtime =
                if not (Directory.Exists includesDir) then DateTime.MinValue
                else
                    // Already traversed in loadIncludes — use directory mtime as sufficient proxy
                    let dirMtime = Directory.GetLastWriteTimeUtc(includesDir).Ticks
                    let mutable maxFile = dirMtime
                    for f in Directory.EnumerateFiles(includesDir, "*.*", SearchOption.AllDirectories) do
                        let t = File.GetLastWriteTimeUtc(f).Ticks
                        if t > maxFile then maxFile <- t
                    DateTime(maxFile)
            setIncludesMtime includesMtime
            PageQuery.setIncludes includes

            // Inject site config into globalData without unnecessary full clone
            let gData = globalData
            let gDict = match gData with
                        | :? Dictionary<string, obj> as d -> d
                        | _ -> let d = Dictionary<string, obj>()
                               for kv in gData do d.[kv.Key] <- kv.Value
                               d
            gDict.["site.title"]       <- box config.Title
            gDict.["site.description"] <- box config.Description
            gDict.["site.base_url"]    <- box config.BaseUrl
            gDict.["site.author"]      <- box config.Author
            gDict.["site.language"]    <- box config.Language
            gDict.["site.version"]     <- box config.SiteVersion

            // Expose menu items in globalData
            for kv in config.Menus do
                let json =
                    kv.Value
                    |> List.map (fun m -> sprintf """{"label":"%s","url":"%s","weight":%d}""" m.Label m.Url m.Weight)
                    |> String.concat ","
                gDict.["menu." + kv.Key] <- box ("[" + json + "]")

            PageQuery.setGlobalData gDict

            // ── Execute _init.zest.fsx (project root init script) ────
            let initResult = InitEngine.run root gDict
            if initResult.HasErrors then
                for err in initResult.Errors do
                    eprintfn "[Zest] _init.zest.fsx: %s" err
                    errors.Add err
            for kv in initResult.GlobalData do
                if not (gDict.ContainsKey kv.Key) then
                    gDict.[kv.Key] <- kv.Value
            PageQuery.setGlobalData gDict

            // ── Content pipeline: discover → evaluate → write output ──
            let struct(total, contentProcessed, contentCached, evalResults) =
                ContentPipeline.processContent contentDir outputDir config gDict layouts includes

            processed <- contentProcessed
            cached    <- contentCached

            // Collect any errors from evaluation results
            for r in evalResults do
                match r with
                | Error e -> errors.Add(e)
                | _ -> ()

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
