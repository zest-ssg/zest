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
open Zest.Engine.Zcss
open Zest.Engine.Template
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

    let private layoutCache2 = ConcurrentDictionary<string, struct(DateTime * Map<string, string * string>)>()
    let private loadLayouts (layoutsDir: string) =
        if not (Directory.Exists layoutsDir) then Map.empty
        else
            let mtime =
                Directory.EnumerateFiles(layoutsDir, "*.*", SearchOption.AllDirectories)
                |> Seq.map (fun f -> File.GetLastWriteTimeUtc(f).Ticks)
                |> Seq.append [Directory.GetLastWriteTimeUtc(layoutsDir).Ticks]
                |> Seq.max |> DateTime
            match layoutCache2.TryGetValue(layoutsDir) with
            | true, (cachedMtime, cachedLayouts) when cachedMtime = mtime -> cachedLayouts
            | _ ->
                let result =
                    Directory.GetFiles(layoutsDir, "*.*", SearchOption.AllDirectories)
                    |> Array.filter (fun f ->
                        let ext = Path.GetExtension(f).ToLowerInvariant()
                        List.contains ext [".html"; ".htm"; ".njk"; ".nunjucks"; ".zpage.fsx"; ".zhtml"; ".fsx"])
                    |> Array.map (fun f ->
                        let rec stripExts (name: string) =
                            let e = Path.GetExtension(name)
                            if String.IsNullOrEmpty e then name
                            else stripExts (Path.GetFileNameWithoutExtension(name))
                        let key = stripExts (Path.GetFileName(f))
                        key, (f, File.ReadAllText(f)))
                    |> Map.ofArray
                layoutCache2.[layoutsDir] <- struct(mtime, result)
                result

    let private globalDataCache = ConcurrentDictionary<string, struct(DateTime * IDictionary<string, obj>)>()
    let private loadGlobalData (dataDir: string) : IDictionary<string, obj> =
        let cacheKey = dataDir
        let mtime =
            if not (Directory.Exists dataDir) then DateTime.MinValue
            else
                Directory.EnumerateFiles(dataDir, "*.toml", SearchOption.AllDirectories)
                |> Seq.map (fun f -> File.GetLastWriteTimeUtc(f).Ticks)
                |> Seq.append [Directory.GetLastWriteTimeUtc(dataDir).Ticks]
                |> Seq.max |> DateTime
        match globalDataCache.TryGetValue(cacheKey) with
        | true, (cachedMtime, cachedData) when cachedMtime = mtime -> cachedData
        | _ ->
            let dict = Dictionary<string, obj>()
            if not (Directory.Exists dataDir) then ()
            else
                for file in Directory.GetFiles(dataDir, "*.toml", SearchOption.AllDirectories) do
                    try
                        let name  = Path.GetFileNameWithoutExtension(file)
                        let model = Tomlyn.Toml.ToModel(File.ReadAllText(file))
                        if model <> null then
                            for kv in model do dict.[name + "." + kv.Key] <- kv.Value
                            dict.[name] <- model :> obj
                    with ex -> eprintfn "[Zest] Failed to load data '%s': %s" file ex.Message
            let result = dict :> IDictionary<string, obj>
            globalDataCache.[cacheKey] <- struct(mtime, result)
            result

    let private includesCache = ConcurrentDictionary<string, struct(DateTime * IDictionary<string, string>)>()
    let private loadIncludes (includesDir: string) : IDictionary<string, string> =
        let mtime =
            if not (Directory.Exists includesDir) then DateTime.MinValue
            else
                Directory.EnumerateFiles(includesDir, "*.*", SearchOption.AllDirectories)
                |> Seq.map (fun f -> File.GetLastWriteTimeUtc(f).Ticks)
                |> Seq.append [Directory.GetLastWriteTimeUtc(includesDir).Ticks]
                |> Seq.max |> DateTime
        match includesCache.TryGetValue(includesDir) with
        | true, (cachedMtime, cachedData) when cachedMtime = mtime -> cachedData
        | _ ->
            let d = Dictionary<string, string>()
            if Directory.Exists includesDir then
                for f in Directory.GetFiles(includesDir, "*.*", SearchOption.AllDirectories) do
                    d.[Path.GetFileName(f)] <- File.ReadAllText(f)
            let result = d :> IDictionary<string, string>
            includesCache.[includesDir] <- struct(mtime, result)
            result

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

    // Cached compiled regexes (reused across all builds) for ~10× speedup
    // over per-call new Regex().
    let private includePattern =
        Regex(@"\{\{\s*include\s+([\w\.]+)\s*\}\}", RegexOptions.Compiled)
    let private placeholderPattern =
        Regex(@"\{\{\s*([\w\.]+)\s*\}\}", RegexOptions.Compiled)

    // Cached parsed layouts. Keyed by layout file path. The value is the
    // (preamble) text with includes already expanded. Re-parsed only when
    // the file's mtime changes.
    let private layoutCache = ConcurrentDictionary<string, struct(DateTime * string)>()
    let private getLayoutCached (path: string) (rawText: string) : string =
        let mtime = File.GetLastWriteTimeUtc(path)
        match layoutCache.TryGetValue(path) with
        | true, (cachedMtime, cachedText) when cachedMtime = mtime -> cachedText
        | _ ->
            // Layout text is cached at raw level; include expansion happens
            // per page (different includes dicts are rare but possible).
            layoutCache.[path] <- struct(mtime, rawText)
            rawText

    let private replacePlaceholders (text: string) (replacements: IDictionary<string, string>) =
        placeholderPattern.Replace(text, fun (m: Match) ->
            let key = m.Groups.[1].Value.ToLowerInvariant()
            match replacements.TryGetValue key with
            | true, v -> v
            | _ -> m.Value)

    /// 处理 {{include filename}} 标签，递归展开嵌套 include。
    let private processIncludes (text: string) (includes: IDictionary<string, string>) =
        let rec processText (t: string) (depth: int) =
            if depth > 10 then t  // 防止无限递归
            else
                includePattern.Replace(t, fun (m: Match) ->
                    let name = m.Groups.[1].Value
                    match includes.TryGetValue(name) with
                    | true, content -> processText content (depth + 1)
                    | _ -> m.Value)
        processText text 0

    // Cache the fully-preprocessed layout text (includes already expanded,
    // nested layout resolved). Keyed by (layout path, includes mtime).
    // The dict identity is used as the includes-generation token, so we
    // get a fresh cache for each build's includes dictionary.
    let private processedLayoutCache = ConcurrentDictionary<string, string>()
    let private includesMtimeRef = ref DateTime.MinValue
    let private setIncludesMtime (t: DateTime) = 
        if t > !includesMtimeRef then
            processedLayoutCache.Clear()  // Clear stale entries when includes change
        includesMtimeRef := t
    let private currentIncludesMtime () = !includesMtimeRef

    let private applyLayoutCached (path: string) (layoutText: string) (includes: IDictionary<string, string>) =
        let key = path + "|" + (currentIncludesMtime ()).Ticks.ToString()
        match processedLayoutCache.TryGetValue(key) with
        | true, cached -> cached
        | _ ->
            let processed = processIncludes layoutText includes
            processedLayoutCache.[key] <- processed
            processed

    let rec private applyLayout (name: string) (content: string) (layouts: Map<string, string * string>)
                                (replacements: IDictionary<string, string>) (includes: IDictionary<string, string>) =
        match layouts.TryFind name with
        | None -> content
        | Some (path, layoutText) ->
            let isNunjucks = path.EndsWith(".njk", StringComparison.OrdinalIgnoreCase)
                             || path.EndsWith(".nunjucks", StringComparison.OrdinalIgnoreCase)

            let rendered =
                if isNunjucks then
                    // Use Nunjucks engine for .njk templates
                    let engine = TemplateManager.getOrCreateEngine "nunjucks" {
                        Engine = "nunjucks"
                        EnableCache = true
                        Extension = ".njk"
                        Filters = []
                    }
                    match engine with
                    | Some e ->
                        // Register Zest custom filters on engine
                        e.RegisterFilter "pages_by_tag" (fun value args ->
                            let tag = if args.Length > 0 then args.[0] else ""
                            ScriptRunner.getPagesForNunjucks ()
                            |> Array.filter (fun p ->
                                match p.TryGetValue "tags" with
                                | true, (:? (string[]) as tags) -> tags |> Array.exists (fun t -> t.Equals(tag, StringComparison.OrdinalIgnoreCase))
                                | _ -> false)
                            |> Array.map (fun d -> d :> obj) |> box)
                        e.RegisterFilter "recent" (fun value args ->
                            let n = if args.Length > 0 then (try int args.[0] with _ -> 5) else 5
                            ScriptRunner.getPagesForNunjucks ()
                            |> Array.filter (fun p ->
                                match p.TryGetValue "date" with
                                | true, (:? string as d) -> d <> ""
                                | _ -> false)
                            |> Array.sortByDescending (fun p ->
                                match p.TryGetValue "date" with
                                | true, (:? string as d) -> d
                                | _ -> "")
                            |> Array.truncate n
                            |> Array.map (fun d -> d :> obj) |> box)
                        e.RegisterFilter "by_collection" (fun value args ->
                            let col = if args.Length > 0 then args.[0] else ""
                            ScriptRunner.getPagesForNunjucks ()
                            |> Array.filter (fun p ->
                                match p.TryGetValue "url" with
                                | true, (:? string as u) ->
                                    let parts = u.Trim('/').Split('/')
                                    parts.Length > 0 && parts.[0].Equals(col, StringComparison.OrdinalIgnoreCase)
                                | _ -> false)
                            |> Array.map (fun d -> d :> obj) |> box)

                        // Convert flat string dict to nested Nunjucks context
                        let pairs = ResizeArray<string * obj>()
                        for kv in replacements do pairs.Add(kv.Key, box kv.Value)
                        pairs.Add("content", box content)
                        pairs.Add("page.content", box content)
                        // Also add page.* keys as nested dict entries
                        pairs.Add("page.url", box (replacements.TryGetValue "page.url" |> function true,v -> box v | _ -> box ""))
                        pairs.Add("page.date", box (replacements.TryGetValue "page.date" |> function true,v -> box v | _ -> box ""))
                        pairs.Add("page.tags", box (replacements.TryGetValue "page.tags" |> function true,v -> box (v.Split(',') |> Array.map (fun t -> t.Trim())) | _ -> box [||]))
                        for kv in includes do
                            if not (pairs |> Seq.exists (fun (k, _) -> k = kv.Key)) then
                                pairs.Add(kv.Key, box kv.Value)
                        // Inject Zest collection data
                        pairs.Add("pages", box (ScriptRunner.getPagesForNunjucks () |> Array.map box))
                        pairs.Add("tags", box (ScriptRunner.getTagsForNunjucks ()))
                        pairs.Add("collections", box (ScriptRunner.getCollectionsForNunjucks ()))
                        let ctx = TemplateManager.buildNestedContext pairs
                        match e.Render layoutText ctx with
                        | Ok html -> html
                        | Error err ->
                            eprintfn "[Zest] Nunjucks error in layout '%s': %O" name err
                            sprintf "<!-- Template error: %O -->" err
                    | None ->
                        // Fallback to placeholder system
                        let withIncludes = applyLayoutCached path layoutText includes
                        let ctx = Dictionary<string, string>()
                        for kv in replacements do ctx.[kv.Key] <- kv.Value
                        ctx.["content"]      <- content
                        ctx.["page.content"] <- content
                        replacePlaceholders withIncludes ctx
                else
                    // Native placeholder system
                    let withIncludes = applyLayoutCached path layoutText includes
                    let ctx = Dictionary<string, string>()
                    for kv in replacements do ctx.[kv.Key] <- kv.Value
                    ctx.["content"]      <- content
                    ctx.["page.content"] <- content
                    replacePlaceholders withIncludes ctx

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

    // ── Incremental build cache (persisted to disk) ──────────────────────
    [<Struct>]
    type private CacheEntry = { Mtime: DateTime; OutputHash: int }
    let private buildCache = ConcurrentDictionary<string, CacheEntry>()
    let private cacheDirty = ref false
    let private cacheFilePath (outputDir: string) = Path.Combine(outputDir, ".zest-cache.json")

    let private loadCache (outputDir: string) =
        if not (buildCache.IsEmpty) then () // Already loaded in this session
        else
            let path = cacheFilePath outputDir
            if File.Exists path then
                try
                    for line in File.ReadAllLines(path) do
                        let parts = line.Split([|'\t'|], 3)
                        if parts.Length = 3 then
                            match Int64.TryParse(parts.[1]), Int32.TryParse(parts.[2]) with
                            | (true, ticks), (true, hash) ->
                                buildCache.[parts.[0]] <- { Mtime = DateTime(ticks, DateTimeKind.Utc); OutputHash = hash }
                            | _ -> ()
                with _ -> ()

    let private saveCache (outputDir: string) =
        if not !cacheDirty then () // Skip write if nothing changed
        else
            try
                let path = cacheFilePath outputDir
                let sb = System.Text.StringBuilder(1024 * buildCache.Count)
                for kv in buildCache do
                    sb.AppendLine(sprintf "%s\t%d\t%d" kv.Key kv.Value.Mtime.Ticks kv.Value.OutputHash) |> ignore
                File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8)
                cacheDirty := false
            with _ -> ()

    let private needsRebuild (srcPath: string) (outPath: string) =
        let mtime = File.GetLastWriteTimeUtc(srcPath)
        match buildCache.TryGetValue(srcPath) with
        | true, e when e.Mtime = mtime && File.Exists(outPath) -> false
        | _ -> true

    let private updateCache (srcPath: string) (html: string) =
        buildCache.[srcPath] <- { Mtime = File.GetLastWriteTimeUtc(srcPath); OutputHash = html.GetHashCode() }
        cacheDirty := true

    let private copyAssets (projectRoot: string) (outputDir: string) =
        let src = Path.Combine(projectRoot, "assets")
        if not (Directory.Exists src) then 0
        else
            let dst = Path.Combine(outputDir, "assets")
            Directory.CreateDirectory(dst) |> ignore
            let createdDirs = System.Collections.Generic.HashSet<string>()
            let ensureDir (target: string) =
                let dir = Path.GetDirectoryName(target)
                if dir <> null && createdDirs.Add(dir) then
                    Directory.CreateDirectory(dir) |> ignore
            let mutable n = 0
            for file in Directory.GetFiles(src, "*", SearchOption.AllDirectories) do
                let ext = Path.GetExtension(file).ToLowerInvariant()
                let rel = Path.GetRelativePath(src, file)
                let srcLastWrite = File.GetLastWriteTimeUtc(file)
                if ext = ".zcss" then
                    let target = Path.Combine(dst, Path.ChangeExtension(rel, ".css"))
                    ensureDir target
                    if not (File.Exists target) || srcLastWrite > File.GetLastWriteTimeUtc(target) then
                        Processor.processFileTo file target |> ignore
                else
                    let target = Path.Combine(dst, rel)
                    ensureDir target
                    if not (File.Exists target) || srcLastWrite > File.GetLastWriteTimeUtc(target) then
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
            // SKIP clean when incremental build is enabled and we have a
            // hot cache — this is the single biggest speedup. Stale files
            // are handled by per-page `needsRebuild` check.
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
            // to avoid corrupting the cached copy (builds are stateless w.r.t.
            // site config but the cache is shared across builds).
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
            // 合并 _init.zest.fsx 注入的全局数据
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
                       yield! Directory.GetFiles(contentDir, "*.njk",      SearchOption.AllDirectories)
                       yield! Directory.GetFiles(contentDir, "*.nunjucks", SearchOption.AllDirectories)
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
            // In incremental mode, skip scripts whose output is up-to-date
            let fsxResults =
                if fsxFiles.Length > 0 then
                    let scriptsToEval =
                        fsxFiles
                        |> Array.choose (fun f ->
                            // Skip if output is up-to-date in incremental mode
                            if config.EnableIncrementalBuild then
                                let contentDir = resolveEffectiveContentDir root config
                                let relPath = Path.GetRelativePath(contentDir, f)
                                let fn = Path.GetFileNameWithoutExtension(f)
                                let rawSlug =
                                    let fn2 = if fn.EndsWith(".zpage") then fn.[..fn.Length - 7] else fn
                                    if fn2.EndsWith(".zest") then fn2.[..fn2.Length - 6] else fn2
                                let slug = PermalinkRouter.slugify rawSlug
                                // We need to compute output path to check cache
                                // For now, just check mtime against buildCache
                                let mtime = File.GetLastWriteTimeUtc(f)
                                match buildCache.TryGetValue(f) with
                                | true, e when e.Mtime = mtime -> None  // cached, skip FSI
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
                        // No page scripts to batch-evaluate.
                        // In incremental mode, all fsx files are cached — skip entirely.
                        // In non-incremental mode, evaluate non-page scripts individually.
                        if not config.EnableIncrementalBuild then
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
                // In incremental mode, skip files that are in the build cache
                if config.EnableIncrementalBuild then
                    let mtime = File.GetLastWriteTimeUtc(f)
                    match buildCache.TryGetValue(f) with
                    | true, e when e.Mtime = mtime ->
                        Threading.Interlocked.Increment(&cached) |> ignore  // count as cached
                    | _ ->
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
                            // Not batch-evaluated (non-page script); evaluate individually
                            try evalResults.Add(ScriptEvaluator.evaluate f config globalData)
                            with ex -> errors.Add(sprintf "Failed '%s': %s" f ex.Message)
                else
                    // Non-incremental mode: process all
                    match Map.tryFind f fsxResults with
                    | Some batchResult ->
                        match batchResult with
                        | Ok htmlContent ->
                            try
                                let text = File.ReadAllText(f)
                                let ext = Path.GetExtension(f).ToLowerInvariant()
                                let contentDir = resolveEffectiveContentDir root config
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
            // Persist cache for next incremental build
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
