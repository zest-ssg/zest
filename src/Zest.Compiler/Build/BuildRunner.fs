namespace Zest.Compiler.Build
open Zest.Compiler.Model
open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open Zest.Compiler.Execution
open Zest.Compiler.Build
open Zest.Compiler.Rendering
open Zest.Compiler.Zestucks
open SitePaths
open IncrementalCache
open GlobalData
open AssetWriter
open ProgressTracker

/// Core build pipeline with parallel content processing and optimised I/O.
module BuildRunner =

    /// Total size of every file below `dir`, in bytes. Used for the build
    /// report handed to _finalize.fsx; an unreadable file counts as zero
    /// rather than failing the build.
    let private directorySize (dir: string) : int64 =
        if not (Directory.Exists dir) then 0L
        else
            try
                Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                |> Seq.sumBy (fun f -> (FileInfo f).Length)
            with _ -> 0L

    let execute (config: SiteConfig) : BuildResult =
        let sw = Stopwatch.StartNew()
        let startedAt = DateTimeOffset.UtcNow
        let errors = ConcurrentBag<string>()
        let mutable processed = 0
        let mutable cached    = 0
        let mutable assets    = 0
        let phaseSw = Stopwatch.StartNew()
        let markPhase (name: string) =
            phaseSw.Stop()
            eprintfn "[Zest][timing] %s: %d ms" name phaseSw.ElapsedMilliseconds
            phaseSw.Restart()

        // ── Initialize progress tracking for the build animator ──
        let progress = ProgressTracker.start ()

        try
            progress.Phase <- BuildPhase.Initializing

            FsiRunner.resetSession()
            PageEvaluator.resetZestucksCache()

            // Strict mode disables Zest extension filters so only the
            // Nunjucks-compatible filter set remains available.
            let isStrict = config.ZestucksCompatibility = "strict"
            ZestucksFilters.setStrictMode isStrict
            if isStrict then
                eprintfn "[Zest] Zestucks strict mode — Zest extension filters disabled."

            let root        = Directory.GetCurrentDirectory()
            let contentDir  = resolveContentDir root config
            let outputDir   = resolvePath root config.OutputDir
            let layoutsDir  = resolveSiteDir root SiteDirectories.Layouts
            let dataDir     = resolveSiteDir root SiteDirectories.Data
            let includesDir = resolveSiteDir root SiteDirectories.Includes

            // Native Zestucks `{% include %}` / `{% extends %}` take bare
            // names; register the partial and layout directories on the loader
            // so "head" resolves without leaking absolute paths in templates.
            // Layouts come second so a partial wins over a same-named layout.
            EngineHost.clearSearchDirs ()
            EngineHost.addSearchDir includesDir
            EngineHost.addSearchDir layoutsDir

            progress.OutputDir <- outputDir

            Directory.CreateDirectory(outputDir) |> ignore
            // Fast cleanup: delete and recreate to avoid per-file enumeration
            if not config.EnableIncrementalBuild then
                try Directory.Delete(outputDir, recursive = true); Directory.CreateDirectory(outputDir) |> ignore
                with _ -> ()

            let layouts = LayoutChain.loadLayouts layoutsDir
            let globalData = loadGlobalData dataDir
            let includes = LayoutChain.loadIncludes includesDir
            // The include-substituted layout cache is keyed by the include set's
            // content signature, so an include edit invalidates it without
            // relying on timestamps.
            LayoutChain.setIncludesSignature (computeIncludesSignature includes)
            PageStore.setIncludes includes

            // Load the incremental cache only after layouts and includes are in
            // hand. The cache diffs per-template content hashes and marks only
            // the pages depending on a changed template stale, so an edit
            // rebuilds the affected pages instead of the whole site.
            if config.EnableIncrementalBuild then
                let templatePairs =
                    [ for (_, (path, text)) in Map.toList layouts -> path, text
                      for kv in includes do
                          match (LayoutChain.getIncludePathMap ()).TryFind kv.Key with
                          | Some path -> yield path, kv.Value
                          | None -> () ]
                loadCache outputDir templatePairs

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

            // ── Inject [params] from _config.toml into globalData ──────
            // Priority: theme _data/params.toml (defaults) < project
            // _data/params.toml < _config.toml [params] (highest). Deep-merge
            // so nested tables (e.g. [params.colors]) replace only the keys
            // they specify, not the entire sub-table. Both the whole `params`
            // object and flat `params.<key>` entries are set so Zestucks can
            // resolve `site.params` as an object and `site.params.colors.accent`
            // via property traversal.
            let rec deepMergeParams (src: IDictionary<string, obj>) (dst: Dictionary<string, obj>) =
                for kv in src do
                    match kv.Value with
                    | :? IDictionary<string, obj> as srcSub ->
                        match dst.TryGetValue kv.Key with
                        | true, (:? Dictionary<string, obj> as dstSub) ->
                            deepMergeParams srcSub dstSub
                        | true, (:? IDictionary<string, obj> as dstSubIface) ->
                            // Wrap mutable copy so deepMergeParams can recurse.
                            let dstSub = Dictionary<string, obj>()
                            for sk in dstSubIface do dstSub.[sk.Key] <- sk.Value
                            deepMergeParams srcSub dstSub
                            dst.[kv.Key] <- box dstSub
                        | _ ->
                            let copy = Dictionary<string, obj>()
                            for sk in srcSub do copy.[sk.Key] <- sk.Value
                            dst.[kv.Key] <- box copy
                    | _ ->
                        dst.[kv.Key] <- kv.Value

            if config.Params.Count > 0 then
                match gDict.TryGetValue "params" with
                | true, (:? Dictionary<string, obj> as existing) ->
                    deepMergeParams config.Params existing
                | true, (:? IDictionary<string, obj> as existingIface) ->
                    let merged = Dictionary<string, obj>()
                    for kv in existingIface do merged.[kv.Key] <- kv.Value
                    deepMergeParams config.Params merged
                    gDict.["params"] <- box merged
                | _ ->
                    let copy = Dictionary<string, obj>()
                    for kv in config.Params do copy.[kv.Key] <- kv.Value
                    gDict.["params"] <- box copy
                // Flat `params.<key>` entries override _data/params.toml keys.
                for kv in config.Params do
                    gDict.["params." + kv.Key] <- kv.Value

            // ── Inject pjax script as a global variable for templates ──
            gDict.["pjaxScript"] <- box Pjax.script

            PageStore.setGlobalData gDict

            // ── Execute _prebuild.fsx (project root pre-build script) ────
            let prebuildResult = PrebuildScript.run root gDict
            if prebuildResult.HasErrors then
                for err in prebuildResult.Errors do
                    eprintfn "[Zest] _prebuild.fsx: %s" err
                    errors.Add err
            for kv in prebuildResult.GlobalData do
                if not (gDict.ContainsKey kv.Key) then
                    gDict.[kv.Key] <- kv.Value
            // Merge prebuild-declared global functions as template-accessible values.
            for kv in prebuildResult.GlobalFunctions do
                if not (gDict.ContainsKey kv.Key) then
                    gDict.[kv.Key] <- kv.Value
            // Propagate prebuild-declared filters so every engine picks them up.
            ZestucksFilters.setPrebuildFilters prebuildResult.Filters
            PageStore.setGlobalData gDict

            // ── Load locale files (_locales/{lang}.toml) ────
            let locales = Zest.Compiler.Build.LocaleLoader.loadLocales root
            ZestucksFilters.setLocales locales config.Language
            // Expose locales to templates
            for langKv in locales do
                for transKv in langKv.Value do
                    gDict.["locale." + langKv.Key + "." + transKv.Key] <- box transKv.Value
            PageStore.setGlobalData gDict

            // ── Content pipeline: discover → evaluate → write output ──
            markPhase "setup"
            progress.Phase <- BuildPhase.Discovering
            let struct(total, contentProcessed, contentCached, evalResults) =
                PagePipeline.processContent contentDir outputDir config gDict layouts includes progress
            markPhase "content"

            processed <- contentProcessed
            cached    <- contentCached

            // Collect any errors from evaluation results
            for r in evalResults do
                match r with
                | Error e -> errors.Add(e)
                | _ -> ()

            // ── Generate taxonomy archive pages (e.g. /tags/, /tags/<term>/) ──
            // Runs after content so PageStore already holds every page and tag.
            // Content files always win: if /tags/foo/ already exists in the
            // output tree, the generator skips it.
            let taxonomyPages = TaxonomyGenerator.generate config outputDir layouts includes gDict
            markPhase "taxonomy"
            processed <- processed + taxonomyPages

            // ── Generate paginated listing pages (e.g. /posts/, /posts/page/2/) ──
            // Runs after content + taxonomy so PageStore knows every page and the
            // output tree is stable. Content files that declare @paginate are
            // skipped by the pipeline, so this generator owns those URLs.
            let paginationPages = PaginationGenerator.generate config contentDir outputDir layouts includes gDict
            markPhase "pagination"
            processed <- processed + paginationPages

            // ── Copy project assets ──
            progress.Phase <- BuildPhase.Assets
            assets <- copyAssets root outputDir
            progress.AssetsCopied <- assets
            if config.EnableIncrementalBuild then saveCache outputDir

            // ── CSS/JS post-processing ──
            // Deliberately not a build stage: pretty-printing and minification
            // are post-build work the author performs in _finalize.fsx with
            // rewriteFiles / formatCss / minifyCss / formatJs / minifyJs.
            progress.Phase <- BuildPhase.Finalizing
            markPhase "assets"

            // ── Execute _finalize.fsx (post-build hook) ──
            // Last step of the pipeline: the output tree is complete, so the
            // hook may index it, validate it, or reshape it (pretty-print,
            // minify). Every write is confined to the output directory.
            // `finalize_on_error = false` skips the hook when the build already
            // failed; the default keeps it running so "check what was written"
            // hooks still fire on a broken build.
            if config.FinalizeOnError || errors.IsEmpty then
                let renderedPages =
                    PageStore.getPages ()
                    |> List.map (fun p ->
                        { Route    = p.Url
                          Output   = Path.GetFullPath(Path.Combine(outputDir, p.OutputPath))
                          Title    = (if String.IsNullOrEmpty p.Title then None else Some p.Title)
                          Source   = p.SourcePath })
                let buildInfo =
                    { DurationMs  = int sw.ElapsedMilliseconds
                      PageCount   = renderedPages.Length
                      AssetCount  = assets
                      OutputBytes = directorySize outputDir
                      StartedAt   = startedAt }

                let finalizeResult = FinalizeScript.run root outputDir config renderedPages buildInfo
                // A hook that asked for leniency logs its error but must not
                // fail the build; FinalizeScript already printed it.
                if finalizeResult.HasErrors && finalizeResult.FailOnError then
                    for err in finalizeResult.Errors do errors.Add err

            sw.Stop()
            ProgressTracker.clear ()
            { TotalPages     = total
              ProcessedPages = processed
              CachedPages    = cached
              AssetsCopied   = assets
              DurationMs     = sw.ElapsedMilliseconds
              OutputDir      = outputDir
              Errors         = errors |> Seq.toList }
        with ex ->
            errors.Add(sprintf "Build failed: %s" ex.Message)
            sw.Stop()
            ProgressTracker.clear ()
            { TotalPages     = 0
              ProcessedPages = processed
              CachedPages    = cached
              AssetsCopied   = assets
              DurationMs     = sw.ElapsedMilliseconds
              OutputDir      = ""
              Errors         = errors |> Seq.toList }
