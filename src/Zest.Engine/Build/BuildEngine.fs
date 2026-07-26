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

            let globalData = loadGlobalData dataDir
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

            // ── Inject pjax script as a global variable for templates ──
            gDict.["pjaxScript"] <- box Resources.ZestPjax.script

            PageQuery.setGlobalData gDict

            // ── Execute theme _theme.zest.fsx (before user _init.zest.fsx) ──
            // Theme scripts can register filters/globals that user scripts
            // may then override or extend.
            let mutable themeAfterBuild : (string * string) list = []
            match themeDir with
            | Some td ->
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
            let struct(total, contentProcessed, contentCached, evalResults) =
                ContentPipeline.processContent contentDir outputDir config gDict layouts includes

            processed <- contentProcessed
            cached    <- contentCached

            // Collect any errors from evaluation results
            for r in evalResults do
                match r with
                | Error e -> errors.Add(e)
                | _ -> ()

            // ── Copy theme assets first, then project assets overwrite ──
            match themeDir with
            | Some td ->
                let themeAssetsDir = Path.Combine(td, "assets")
                if Directory.Exists themeAssetsDir then
                    copyAssetsDir themeAssetsDir outputDir |> ignore
            | None -> ()

            assets <- copyAssets root outputDir
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
            { TotalPages     = total
              ProcessedPages = processed
              CachedPages    = cached
              AssetsCopied   = assets
              AssetsProcessed = assetsProcessed
              DurationMs     = sw.ElapsedMilliseconds
              Errors         = errors |> Seq.toList }
        with ex ->
            errors.Add(sprintf "Build failed: %s" ex.Message)
            sw.Stop()
            { TotalPages     = 0
              ProcessedPages = processed
              CachedPages    = cached
              AssetsCopied   = assets
              AssetsProcessed = 0
              DurationMs     = sw.ElapsedMilliseconds
              Errors         = errors |> Seq.toList }
