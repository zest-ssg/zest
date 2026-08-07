namespace Zest.Engine

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open System.Diagnostics
open Zest.Engine.Scripting
open Zest.Engine.Build
open Zest.Engine.Html
open PathResolver
open BuildCache
open BuildData
open BuildAssets
open BuildLayout
open ProgressTracker

/// Core build pipeline with parallel content processing and optimised I/O.
module BuildEngine =

    let execute (config: SiteConfig) : BuildResult =
        let sw = Stopwatch.StartNew()
        let errors = ConcurrentBag<string>()
        let mutable processed = 0
        let mutable cached    = 0
        let mutable assets    = 0

        // ── Initialize progress tracking for the build animator ──
        let progress = ProgressTracker.start ()

        try
            progress.Phase <- BuildPhase.Initializing

            ScriptRunner.resetSession()
            ScriptEvaluator.resetNunjucksCache()

            // ── Surface active compatibility / template modes ──
            // Helps users verify that [compat] / [template] flags took effect.
            let compatFlags =
                [ if config.CompatJekyll then "jekyll"
                  if config.CompatHexo then "hexo"
                  if config.CompatHugo then "hugo"
                  if config.CompatEleventy then "eleventy" ]
            if not (List.isEmpty compatFlags) then
                eprintfn "[Zest] Compat mode active: %s" (String.concat ", " compatFlags)
            // Strict Nunjucks mode disables Zest extension filters so only
            // official-Nunjucks-compatible filters remain available.
            let isStrict = config.NunjucksCompatibility = "strict"
            FilterRegistry.setStrictMode isStrict
            if isStrict then
                eprintfn "[Zest] Nunjucks strict mode — Zest extension filters disabled."

            let root       = Directory.GetCurrentDirectory()
            let contentDir = resolveEffectiveContentDir root config
            let outputDir  = resolvePath root config.OutputDir
            let layoutsDir = resolvePath root config.LayoutsDir
            let dataDir    = resolvePath root config.DataDir
            let includesDir = resolvePath root config.IncludesDir

            // ── Resolve theme directory (if configured) ────
            let themeDir = ThemeResolver.resolve root config.Theme

            progress.OutputDir <- outputDir

            Directory.CreateDirectory(outputDir) |> ignore
            // Load persistent cache for incremental builds
            if config.EnableIncrementalBuild then loadCache outputDir
            // Fast cleanup: delete and recreate to avoid per-file enumeration
            if not config.EnableIncrementalBuild then
                try Directory.Delete(outputDir, recursive = true); Directory.CreateDirectory(outputDir) |> ignore
                with _ -> ()

            // Load layouts & data in parallel (independent operations)
            // Theme layouts are loaded first; project layouts with the same name overwrite.
            let layouts =
                match themeDir with
                | Some td ->
                    let themeLayoutsDir = Path.Combine(td, "_layouts")
                    let themeLayouts = if Directory.Exists themeLayoutsDir then loadLayouts themeLayoutsDir else Map.empty
                    let projectLayouts = loadLayouts layoutsDir
                    // Project overwrites theme — project's Map keys take priority
                    Map.fold (fun acc k v -> Map.add k v acc) themeLayouts projectLayouts
                | None -> loadLayouts layoutsDir

            // Load global data: theme _data first (as defaults), then project _data overwrites.
            // This mirrors the layouts/includes pattern: theme provides presets,
            // project files with the same key take priority.
            let globalData =
                match themeDir with
                | Some td ->
                    let themeDataDir = Path.Combine(td, "_data")
                    if Directory.Exists themeDataDir then
                        let themeData = loadGlobalData themeDataDir
                        let projectData = loadGlobalData dataDir
                        // Merge: project keys overwrite theme keys.
                        let merged = Dictionary<string, obj>(themeData)
                        for kv in projectData do merged.[kv.Key] <- kv.Value
                        merged :> IDictionary<string, obj>
                    else
                        loadGlobalData dataDir
                | None -> loadGlobalData dataDir
            let includes =
                match themeDir with
                | Some td ->
                    let themeIncludesDir = Path.Combine(td, "_includes")
                    let baseIncludes = loadIncludes includesDir
                    if Directory.Exists themeIncludesDir then
                        let themeIncludes = loadIncludes themeIncludesDir
                        // Theme keys first; project keys overwrite
                        for kv in themeIncludes do
                            if not (baseIncludes.ContainsKey kv.Key) then
                                baseIncludes.[kv.Key] <- kv.Value
                    baseIncludes
                | None -> loadIncludes includesDir
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

            // ── Inject [params] from _config.toml into globalData ──────
            // Priority: theme _data/params.toml (defaults) < project
            // _data/params.toml < _config.toml [params] (highest). Deep-merge
            // so nested tables (e.g. [params.colors]) replace only the keys
            // they specify, not the entire sub-table. Both the whole `params`
            // object and flat `params.<key>` entries are set so Nunjucks can
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
            gDict.["pjaxScript"] <- box Resources.ZestPjax.script

            PageQuery.setGlobalData gDict

            // ── Execute theme init: _theme.toml (preferred) then _theme.zest.fsx (legacy) ──
            // _theme.toml is declarative: metadata, data, filters, afterBuild hooks.
            // _theme.zest.fsx (if present) runs after and can register additional
            // globals/functions via F# code. Theme data is merged before user
            // _init.zest.fsx so user scripts can override or extend.
            let mutable themeAfterBuild : (string * string) list = []
            match themeDir with
            | Some td ->
                // ── Load _theme.toml (declarative theme config) ──
                let themeTomlPath = Path.Combine(td, "_theme.toml")
                if File.Exists themeTomlPath then
                    let manifest = ThemeConfigLoader.load td
                    // Expose theme metadata as `site.theme.*` globals.
                    let themeMeta = System.Collections.Generic.Dictionary<string, obj>()
                    for kv in manifest.Meta do themeMeta.[kv.Key] <- kv.Value
                    // Only set if not already present (user config takes priority later).
                    if not (gDict.ContainsKey "theme") then
                        gDict.["theme"] <- box themeMeta
                    // Merge theme [data] section as top-level globals.
                    for kv in manifest.Data do
                        if not (gDict.ContainsKey kv.Key) then
                            gDict.[kv.Key] <- kv.Value
                    // Register theme filters (user init filters will overwrite).
                    FilterRegistry.addInitFilters manifest.Filters
                    themeAfterBuild <- manifest.AfterBuild
                    PageQuery.setGlobalData gDict

                // ── Legacy: run _theme.zest.fsx if present (backward compat) ──
                let themeScript = Path.Combine(td, "_theme.zest.fsx")
                if File.Exists themeScript then
                    let themeResult = InitEngine.runScript themeScript gDict
                    if themeResult.HasErrors then
                        for err in themeResult.Errors do
                            eprintfn "[Zest] _theme.zest.fsx: %s" err
                            // Theme script errors are non-fatal; build continues.
                    for kv in themeResult.GlobalData do
                        if not (gDict.ContainsKey kv.Key) then
                            gDict.[kv.Key] <- kv.Value
                    for kv in themeResult.GlobalFunctions do
                        if not (gDict.ContainsKey kv.Key) then
                            gDict.[kv.Key] <- kv.Value
                    // Register theme filters; user init script filters will overwrite.
                    FilterRegistry.addInitFilters themeResult.Filters
                    if List.isEmpty themeAfterBuild then
                        themeAfterBuild <- themeResult.AfterBuildCommands
                    PageQuery.setGlobalData gDict
            | None -> ()

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
            let locales =
                match themeDir with
                | Some td ->
                    Zest.Engine.I18n.LocaleLoader.loadLocales root (Some td)
                | None ->
                    Zest.Engine.I18n.LocaleLoader.loadLocales root None
            FilterRegistry.setLocales locales config.Language
            // Expose locales to templates
            for langKv in locales do
                for transKv in langKv.Value do
                    gDict.["locale." + langKv.Key + "." + transKv.Key] <- box transKv.Value
            PageQuery.setGlobalData gDict

            // ── Collect afterBuild commands from theme + init scripts ──
            let afterBuildCmds = themeAfterBuild @ initResult.AfterBuildCommands

            // ── Content pipeline: discover → evaluate → write output ──
            progress.Phase <- BuildPhase.Discovering
            let struct(total, contentProcessed, contentCached, evalResults) =
                ContentPipeline.processContent contentDir outputDir config gDict layouts includes progress

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
            processed <- processed + taxonomyPages

            // ── Generate paginated listing pages (e.g. /posts/, /posts/page/2/) ──
            // Runs after content + taxonomy so PageQuery knows every page and the
            // output tree is stable. Content files that declare @paginate are
            // skipped by the pipeline, so this generator owns those URLs.
            let paginationPages = PaginationGenerator.generate config contentDir outputDir layouts includes gDict
            processed <- processed + paginationPages

            // ── Copy theme assets first, then project assets overwrite ──
            progress.Phase <- BuildPhase.Assets
            match themeDir with
            | Some td ->
                let themeAssetsDir = Path.Combine(td, "assets")
                if Directory.Exists themeAssetsDir then
                    copyAssetsDir themeAssetsDir outputDir |> ignore
            | None -> ()

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
                for file in Directory.EnumerateFiles(outputDir, "*.*", SearchOption.AllDirectories) do
                    let ext = Path.GetExtension(file).ToLowerInvariant()
                    if processExts.Contains ext then
                        try
                            let content = File.ReadAllText(file, System.Text.Encoding.UTF8)
                            let processed =
                                if config.EnableAssetFormatting then
                                    if ext = ".css" then HtmlFormatter.formatCss 2 content
                                    else HtmlFormatter.formatJs 2 content
                                elif config.EnableMinification then
                                    if ext = ".css" then HtmlFormatter.minifyCss content
                                    else HtmlFormatter.minifyJs content
                                else content
                            if processed <> content then
                                File.WriteAllText(file, processed, System.Text.Encoding.UTF8)
                                assetsProcessed <- assetsProcessed + 1
                        with ex ->
                            eprintfn "[Zest] Asset processing failed for '%s': %s" file ex.Message

            progress.Phase <- BuildPhase.Finalizing

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
