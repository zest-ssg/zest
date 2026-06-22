namespace Zest.Engine

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open System.Diagnostics
open System.Threading.Tasks
open Zest.Engine
open Zest.Engine.Parsing
open Zest.Engine.Routing
open Zest.Engine.Scripting
open Zest.Engine.Zss
open System.Text.RegularExpressions

/// 核心构建管线：内容发现 → 求值 → 布局应用 → 资产处理 → 输出。
module BuildEngine =

    let private resolvePath root dir =
        Path.GetFullPath(Path.Combine(root, dir.ToString().TrimStart('.', '\\', '/')))

    let private isExcluded (contentDir: string) (filePath: string) =
        // Exclude directories starting with _ or . (e.g. _layouts, _includes, _data, .git)
        // Also exclude the output directory itself to prevent infinite loops
        Path.GetRelativePath(contentDir, filePath)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        |> Array.exists (fun p -> p.StartsWith("_") || p.StartsWith("."))

    let private loadLayouts (layoutsDir: string) =
        if not (Directory.Exists layoutsDir) then Map.empty
        else
            Directory.GetFiles(layoutsDir, "*.*", SearchOption.AllDirectories)
            |> Array.filter (fun f ->
                let ext = Path.GetExtension(f).ToLowerInvariant()
                List.contains ext [".html"; ".htm"; ".zest.fsx"; ".fsx"])
            |> Array.map (fun f ->
                let rec stripExts (name: string) =
                    let e = Path.GetExtension(name)
                    if String.IsNullOrEmpty e then name
                    else stripExts (Path.GetFileNameWithoutExtension(name))
                let key = stripExts (Path.GetFileName(f))
                key, (f, File.ReadAllText(f)))
            |> Map.ofArray

    let private loadGlobalData (dataDir: string) : IDictionary<string, obj> =
        let dict = Dictionary<string, obj>()
        if not (Directory.Exists dataDir) then dict :> _
        else
            for file in Directory.GetFiles(dataDir, "*.toml", SearchOption.AllDirectories) do
                try
                    let name  = Path.GetFileNameWithoutExtension(file)
                    let model = Tomlyn.Toml.ToModel(File.ReadAllText(file))
                    if model <> null then
                        for kv in model do dict.[name + "." + kv.Key] <- kv.Value
                        dict.[name] <- model :> obj
                with ex -> eprintfn "[Zest] Failed to load data '%s': %s" file ex.Message
            dict :> _

    let private buildReplacements (page: Page) (config: SiteConfig) (globalData: IDictionary<string, obj>) =
        let d = Dictionary<string, string>()
        d.["page.title"] <- page.Title
        d.["page.url"]   <- page.Url
        d.["page.slug"]  <- page.Slug
        if page.Date.IsSome then d.["page.date"] <- page.Date.Value.ToString("yyyy-MM-dd")
        if not page.Tags.IsEmpty then d.["page.tags"] <- String.Join(", ", page.Tags)
        // page.description: 优先使用页面描述，回退到站点描述
        d.["page.description"] <-
            match page.Data.TryGetValue("description") with
            | true, v when v <> null -> v.ToString()
            | _ -> config.Description
        d.["site.title"]       <- config.Title
        d.["site.description"] <- config.Description
        d.["site.base_url"]    <- config.BaseUrl
        d.["site.version"]     <- config.SiteVersion
        d.["site.author"]      <- config.Author
        d.["site.language"]    <- config.Language
        for kv in globalData do
            let key = "site." + kv.Key
            if not (d.ContainsKey key) then d.[key] <- kv.Value.ToString()
        for kv in page.Data do
            let key = if kv.Key.Contains "." then kv.Key else "page." + kv.Key
            if not (d.ContainsKey key) then d.[key] <- kv.Value.ToString()
        d :> IDictionary<string, string>

    let private replacePlaceholders (text: string) (replacements: IDictionary<string, string>) =
        Regex.Replace(text, "\\{\\{\\s*([\\w\\.]+)\\s*\\}\\}", fun (m: Match) ->
            let key = m.Groups.[1].Value.ToLowerInvariant()
            match replacements.TryGetValue key with
            | true, v -> v
            | _ -> m.Value)

    /// 处理 {{include filename}} 标签，递归展开嵌套 include。
    let private processIncludes (text: string) (includes: IDictionary<string, string>) =
        let pat = Regex(@"\{\{\s*include\s+([\w\.]+)\s*\}\}", RegexOptions.Compiled)
        let rec processText (t: string) (depth: int) =
            if depth > 10 then t  // 防止无限递归
            else
                pat.Replace(t, fun (m: Match) ->
                    let name = m.Groups.[1].Value
                    match includes.TryGetValue(name) with
                    | true, content -> processText content (depth + 1)
                    | _ -> m.Value)
        processText text 0

    let rec private applyLayout (name: string) (content: string) (layouts: Map<string, string * string>)
                                (replacements: IDictionary<string, string>) (includes: IDictionary<string, string>) =
        match layouts.TryFind name with
        | None -> content
        | Some (_, layoutText) ->
            let withIncludes = processIncludes layoutText includes
            let ctx = Dictionary<string, string>()
            for kv in replacements do ctx.[kv.Key] <- kv.Value
            ctx.["content"]      <- content
            ctx.["page.content"] <- content
            let rendered = replacePlaceholders withIncludes ctx
            let nestedLayout =
                if layoutText.StartsWith "---" then
                    let endIdx = layoutText.IndexOf("---", 3)
                    if endIdx > 0 then
                        layoutText.Substring(3, endIdx - 3).Split('\n')
                        |> Array.tryFind (fun l -> l.Trim().StartsWith("layout"))
                        |> Option.bind (fun l ->
                            let p = l.Split(':')
                            if p.Length >= 2 then Some (p.[1].Trim().Trim('"', ' ')) else None)
                    else None
                else None
            match nestedLayout with
            | Some nl when nl <> name -> applyLayout nl rendered layouts replacements includes
            | _ -> rendered

    let private loadIncludes (includesDir: string) : IDictionary<string, string> =
        let d = Dictionary<string, string>()
        if Directory.Exists includesDir then
            for f in Directory.GetFiles(includesDir, "*.*", SearchOption.AllDirectories) do
                d.[Path.GetFileName(f)] <- File.ReadAllText(f)
        d :> _

    // ── Incremental build cache ──────────────────────────────────────────
    [<Struct>]
    type private CacheEntry = { Mtime: DateTime; OutputHash: int }
    let private buildCache = ConcurrentDictionary<string, CacheEntry>()

    let private needsRebuild (srcPath: string) (outPath: string) =
        let mtime = File.GetLastWriteTimeUtc(srcPath)
        match buildCache.TryGetValue(srcPath) with
        | true, e when e.Mtime = mtime && File.Exists(outPath) -> false
        | _ -> true

    let private updateCache (srcPath: string) (html: string) =
        buildCache.[srcPath] <- { Mtime = File.GetLastWriteTimeUtc(srcPath); OutputHash = html.GetHashCode() }

    let private copyAssets (projectRoot: string) (outputDir: string) =
        let src = Path.Combine(projectRoot, "assets")
        if not (Directory.Exists src) then 0
        else
            let dst = Path.Combine(outputDir, "assets")
            Directory.CreateDirectory(dst) |> ignore
            let mutable n = 0
            for file in Directory.GetFiles(src, "*", SearchOption.AllDirectories) do
                let ext = Path.GetExtension(file).ToLowerInvariant()
                let rel = Path.GetRelativePath(src, file)
                if ext = ".zss" then
                    let target = Path.Combine(dst, Path.ChangeExtension(rel, ".css"))
                    let dir    = Path.GetDirectoryName(target)
                    if dir <> null then Directory.CreateDirectory(dir) |> ignore
                    Processor.processFileTo file target |> ignore
                else
                    let target = Path.Combine(dst, rel)
                    let dir    = Path.GetDirectoryName(target)
                    if dir <> null then Directory.CreateDirectory(dir) |> ignore
                    let si = FileInfo(file)
                    let ti = FileInfo(target)
                    if not ti.Exists || si.LastWriteTimeUtc > ti.LastWriteTimeUtc then
                        File.Copy(file, target, overwrite = true)
                n <- n + 1
            n

    /// Resolve the effective content directory based on RootDir configuration.
    /// - RootDir = "." or "" → project root is the content directory
    /// - RootDir = "content" (default) → use ./content subdirectory
    /// - RootDir = custom path → use that path
    let private resolveEffectiveContentDir (root: string) (config: SiteConfig) =
        let rootDir = config.RootDir.Trim()
        if String.IsNullOrEmpty rootDir || rootDir = "." then
            root  // Project root itself is the content directory
        else
            resolvePath root rootDir

    let execute (config: SiteConfig) : BuildResult =
        let sw     = Stopwatch.StartNew()
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

            Directory.CreateDirectory(outputDir) |> ignore
            // Clean output directory before build to avoid stale files
            try
                for f in Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories) do
                    File.Delete(f)
                for d in Directory.GetDirectories(outputDir) do
                    Directory.Delete(d, recursive = true)
            with _ -> ()
            let layouts    = loadLayouts layoutsDir
            let globalData = loadGlobalData dataDir
            let includes   = loadIncludes (resolvePath root config.IncludesDir)
            ScriptRunner.setIncludes includes

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

            let allFiles =
                if not (Directory.Exists contentDir) then
                    Directory.CreateDirectory(contentDir) |> ignore; [||]
                else
                    [| yield! Directory.GetFiles(contentDir, "*.zest.fsx", SearchOption.AllDirectories)
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

            // Markdown pages
            if config.EnableParallelBuild && mdFiles.Length > 0 then
                Parallel.ForEach(mdFiles, fun f ->
                    try evalResults.Add(ScriptEvaluator.evaluate f config globalData)
                    with ex -> errors.Add(sprintf "Failed '%s': %s" f ex.Message)) |> ignore
            else
                for f in mdFiles do
                    try evalResults.Add(ScriptEvaluator.evaluate f config globalData)
                    with ex -> errors.Add(sprintf "Failed '%s': %s" f ex.Message)

            // FSI scripts: batch evaluate in a single FSI process for performance
            let fsxResults =
                if fsxFiles.Length > 0 then
                    let scriptsToEval =
                        fsxFiles
                        |> Array.choose (fun f ->
                            try
                                let text = File.ReadAllText(f)
                                if ScriptRunner.isPageScript (Path.GetExtension(f).ToLowerInvariant()) text then
                                    Some (f, text)
                                else None
                            with _ -> None)
                        |> Array.toList

                    if scriptsToEval.IsEmpty then
                        // All scripts are non-page scripts, evaluate individually as before
                        for f in fsxFiles do
                            try evalResults.Add(ScriptEvaluator.evaluate f config globalData)
                            with ex -> errors.Add(sprintf "Failed '%s': %s" f ex.Message)
                        Map.empty
                    else
                        // Batch evaluate all page scripts in one FSI process
                        let batchResults = ScriptRunner.evaluatePageScriptsBatch scriptsToEval
                        batchResults
                else Map.empty

            // Process batch results and evaluate non-page scripts individually
            for f in fsxFiles do
                match Map.tryFind f fsxResults with
                | Some batchResult ->
                    // This was batch-evaluated; construct Page from result
                    match batchResult with
                    | Ok htmlContent ->
                        try
                            let text = File.ReadAllText(f)
                            let ext = Path.GetExtension(f).ToLowerInvariant()
                            let contentDir = resolveEffectiveContentDir root config
                            let relPath, rawSlug =
                                let rel = Path.GetRelativePath(contentDir, f)
                                let fn = Path.GetFileNameWithoutExtension(f)
                                let raw = if fn.EndsWith(".zest") then fn.[..fn.Length - 6] else fn
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
                    // Not batch-evaluated (non-page script); evaluate individually
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
