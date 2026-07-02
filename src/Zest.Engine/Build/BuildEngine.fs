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

/// Core build pipeline: path resolution → cache → layouts → init → content processing → assets → output.
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
            PageQuery.setIncludes includes

            // If globalData came from cache we must clone it before mutation
            let globalData =
                let fresh = Dictionary<string, obj>()
                for kv in globalData do fresh.[kv.Key] <- kv.Value
                fresh :> IDictionary<string, obj>
            // Inject site config into globalData so scripts can access site.*
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

            PageQuery.setGlobalData globalData

            // ── Execute _init.zest.fsx (project root init script) ────
            let initResult = InitEngine.run root globalData
            if initResult.HasErrors then
                for err in initResult.Errors do
                    eprintfn "[Zest] _init.zest.fsx: %s" err
                    errors.Add err
            for kv in initResult.GlobalData do
                if not (gData.ContainsKey kv.Key) then
                    gData.[kv.Key] <- kv.Value
            PageQuery.setGlobalData globalData

            // ── Content pipeline: discover → evaluate → write output ──
            let struct(total, contentProcessed, contentCached, evalResults) =
                ContentPipeline.processContent contentDir outputDir config globalData layouts includes

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
