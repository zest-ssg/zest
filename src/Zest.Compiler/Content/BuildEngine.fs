namespace Zest.Compiler

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open Zest.Compiler.Scripting
open Zest.Compiler.Content
open Zest.Compiler.Html
open Zest.Compiler.Template
open PathResolver
open BuildCache
open BuildData
open BuildAssets
open ProgressTracker

/// Core build pipeline with parallel content processing and optimised I/O.
module BuildEngine =

    let execute (config: SiteConfig) : BuildResult =
        let sw = Stopwatch.StartNew()
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

            ScriptRunner.resetSession()
            ScriptEvaluator.resetZealucksCache()

            // Strict mode disables Zest extension filters so only the
            // Nunjucks-compatible filter set remains available.
            let isStrict = config.ZealucksCompatibility = "strict"
            FilterRegistry.setStrictMode isStrict
            if isStrict then
                eprintfn "[Zest] Zealucks strict mode — Zest extension filters disabled."

            let root        = Directory.GetCurrentDirectory()
            let contentDir  = resolveContentDir root config
            let outputDir   = resolvePath root config.OutputDir
            let layoutsDir  = resolveSiteDir root SiteDirectories.Layouts
            let dataDir     = resolveSiteDir root SiteDirectories.Data
            let includesDir = resolveSiteDir root SiteDirectories.Includes

            // Native Zealucks `{% include %}` uses bare filenames; register the
            // include directory on the loader so "head.zlk" resolves without
            // leaking absolute paths in templates.
            TemplateManager.clearTemplateSearchDirs ()
            TemplateManager.addTemplateSearchDir includesDir

            progress.OutputDir <- outputDir

            Directory.CreateDirectory(outputDir) |> ignore
            // Fast cleanup: delete and recreate to avoid per-file enumeration
            if not config.EnableIncrementalBuild then
                try Directory.Delete(outputDir, recursive = true); Directory.CreateDirectory(outputDir) |> ignore
                with _ -> ()

            let layouts = LayoutEngine.loadLayouts layoutsDir
            let globalData = loadGlobalData dataDir
            let includes = LayoutEngine.loadIncludes includesDir
            // The include-substituted layout cache is keyed by the include set's
            // content signature, so an include edit invalidates it without
            // relying on timestamps.
            LayoutEngine.setIncludesSignature (computeIncludesSignature includes)
            PageQuery.setIncludes includes

            // Load the incremental cache only after layouts and includes are in
            // hand. The cache diffs per-template content hashes and marks only
            // the pages depending on a changed template stale, so an edit
            // rebuilds the affected pages instead of the whole site.
            if config.EnableIncrementalBuild then
                let templatePairs =
                    [ for (_, (path, text)) in Map.toList layouts -> path, text
                      for kv in includes do
                          match (LayoutEngine.getIncludePathMap ()).TryFind kv.Key with
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
            // object and flat `params.<key>` entries are set so Zealucks can
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
            gDict.["pjaxScript"] <- box ZestPjax.script

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
            // Merge init-declared global functions as template-accessible values.
            for kv in initResult.GlobalFunctions do
                if not (gDict.ContainsKey kv.Key) then
                    gDict.[kv.Key] <- kv.Value
            // Propagate init-declared filters so every engine picks them up.
            FilterRegistry.setInitFilters initResult.Filters
            PageQuery.setGlobalData gDict

            // ── Load locale files (_locales/{lang}.toml) ────
            let locales = Zest.Compiler.Content.LocaleLoader.loadLocales root
            FilterRegistry.setLocales locales config.Language
            // Expose locales to templates
            for langKv in locales do
                for transKv in langKv.Value do
                    gDict.["locale." + langKv.Key + "." + transKv.Key] <- box transKv.Value
            PageQuery.setGlobalData gDict

            // ── Collect afterBuild commands declared by _init.zest.fsx ──
            let afterBuildCmds = initResult.AfterBuildCommands

            // ── Content pipeline: discover → evaluate → write output ──
            markPhase "setup"
            progress.Phase <- BuildPhase.Discovering
            let struct(total, contentProcessed, contentCached, evalResults) =
                ContentPipeline.processContent contentDir outputDir config gDict layouts includes progress
            markPhase "content"

            processed <- contentProcessed
            cached    <- contentCached

            // Collect any errors from evaluation results
            for r in evalResults do
                match r with
                | Error e -> errors.Add(e)
                | _ -> ()

            // ── Generate taxonomy archive pages (e.g. /tags/, /tags/<term>/) ──
            // Runs after content so PageQuery already holds every page and tag.
            // Content files always win: if /tags/foo/ already exists in the
            // output tree, the generator skips it.
            let taxonomyPages = TaxonomyGenerator.generate config outputDir layouts includes gDict
            markPhase "taxonomy"
            processed <- processed + taxonomyPages

            // ── Generate paginated listing pages (e.g. /posts/, /posts/page/2/) ──
            // Runs after content + taxonomy so PageQuery knows every page and the
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
            // Two independent modes, matching the HTML formatting approach:
            //   enable_asset_formatting → pretty-print with indentation
            //   enable_minification     → compress (whitespace stripped)
            // When both are enabled, formatting takes priority.
            let mutable assetsProcessed = 0
            if (config.EnableAssetFormatting || config.EnableMinification) && Directory.Exists outputDir then
                let processExts = set [ ".css"; ".js" ]
                // Enumerate once, then process files in parallel — formatting is
                // CPU-bound and independent per file.
                let assetFiles =
                    Directory.EnumerateFiles(outputDir, "*.*", SearchOption.AllDirectories)
                    |> Seq.filter (fun f -> processExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    |> Seq.toArray
                Parallel.ForEach(assetFiles, fun file ->
                    try
                        let content = File.ReadAllText(file, System.Text.Encoding.UTF8)
                        let processed =
                            if config.EnableAssetFormatting then
                                if Path.GetExtension(file).ToLowerInvariant() = ".css" then HtmlFormatter.formatCss 2 content
                                else HtmlFormatter.formatJs 2 content
                            elif config.EnableMinification then
                                if Path.GetExtension(file).ToLowerInvariant() = ".css" then HtmlFormatter.minifyCss content
                                else HtmlFormatter.minifyJs content
                            else content
                        if processed <> content then
                            AtomicFile.write file (System.Text.Encoding.UTF8.GetBytes processed)
                            Interlocked.Increment(&assetsProcessed) |> ignore
                    with ex ->
                        eprintfn "[Zest] Asset processing failed for '%s': %s" file ex.Message) |> ignore

            progress.Phase <- BuildPhase.Finalizing
            markPhase "assets"
            if config.EnableAssetFormatting || config.EnableMinification then
                eprintfn "[Zest] Asset post-process: %s %d file(s)"
                    (if config.EnableAssetFormatting then "formatted" else "minified")
                    assetsProcessed

            // ── Execute afterBuild commands (e.g. sitemap, search index) ──
            for (cmd, args) in afterBuildCmds do
                try
                    let psi = ProcessStartInfo(cmd, args)
                    psi.UseShellExecute <- false
                    psi.RedirectStandardOutput <- true
                    psi.RedirectStandardError <- true
                    psi.CreateNoWindow <- true
                    use proc = Process.Start(psi)
                    let stdout = proc.StandardOutput.ReadToEnd()
                    let stderr = proc.StandardError.ReadToEnd()
                    if not (proc.WaitForExit(30_000)) then
                        try proc.Kill() with _ -> ()
                        eprintfn "[Zest] afterBuild '%s %s' timed out" cmd args
                    elif proc.ExitCode <> 0 then
                        eprintfn "[Zest] afterBuild '%s %s' failed (exit %d): %s" cmd args proc.ExitCode (stderr.Trim())
                    elif !PageQuery.verboseRef && stdout.Trim() <> "" then
                        eprintfn "[Zest] afterBuild '%s %s': %s" cmd args (stdout.Trim())
                with ex ->
                    eprintfn "[Zest] afterBuild '%s %s' threw: %s" cmd args ex.Message

            sw.Stop()
            ProgressTracker.clear ()
            { TotalPages     = total
              ProcessedPages = processed
              CachedPages    = cached
              AssetsCopied   = assets
              AssetsProcessed = assetsProcessed
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
              AssetsProcessed = 0
              DurationMs     = sw.ElapsedMilliseconds
              OutputDir      = ""
              Errors         = errors |> Seq.toList }
