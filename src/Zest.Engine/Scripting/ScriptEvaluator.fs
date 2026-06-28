namespace Zest.Engine.Scripting

open System
open System.Collections.Generic
open System.IO
open System.Text.RegularExpressions
open Zest.Engine
open Zest.Engine.Parsing
open Zest.Engine.Routing
open Zest.Engine.Html
open Zest.Engine.Template

/// 负责将 .zpage.fsx / .md 文件求值为 Page 记录。
module ScriptEvaluator =

    /// 从脚本文本中提取并渲染内容 HTML（传统 Markdown 回退模式）。
    let private renderContent (ext: string) (bodyText: string) (fullText: string) : string =
        match ext with
        | ".md" | ".markdown" ->
            Markdown.toHtml bodyText
        | ".zhtml" ->
            // .zhtml: 纯 HTML 内容，无需任何转换
            bodyText
        | _ ->
            // .zpage.fsx: 剥去元数据注释和 #r / #load 指令，其余视为 Markdown 内容
            let lines =
                fullText.Split('\n')
                |> Array.filter (fun l ->
                    let t = l.Trim()
                    not (t.StartsWith("//"))
                    && not (t.StartsWith("---"))
                    && not (t.StartsWith("#r "))
                    && not (t.StartsWith("#load ")))
                |> Array.skipWhile String.IsNullOrWhiteSpace
            Markdown.toHtml (String.concat "\n" lines)

    /// 使用 Nunjucks 引擎渲染 .njk 内容页面。
    /// 构建嵌套上下文（site.*、page.* 等），使 {{ site.title }} 模板语法充分生效。
    /// 同时注入 Zest 页面数据（pages、tags、collections）和注册 Zest 自定义过滤器。
    let private renderNunjucksContent
        (bodyText: string)
        (config: SiteConfig)
        (globalData: IDictionary<string, obj>)
        (meta: FrontMeta)
        (slug: string)
        (filePath: string)
        : string =
        match TemplateManager.getOrCreateEngine "nunjucks" {
            Engine = "nunjucks"
            EnableCache = true
            Extension = ".njk"
            Filters = []
        } with
        | Some engine ->
            // ── 注册 Zest 自定义 filters ────────────────────
            engine.RegisterFilter "pages_by_tag" (fun value args ->
                let tag = if args.Length > 0 then args.[0] else ""
                let pages = ScriptRunner.getPagesForNunjucks ()
                pages |> Array.filter (fun p ->
                    match p.TryGetValue "tags" with
                    | true, (:? (string[]) as tags) -> tags |> Array.exists (fun t -> t.Equals(tag, StringComparison.OrdinalIgnoreCase))
                    | _ -> false)
                |> Array.map (fun d -> d :> obj) |> box)

            engine.RegisterFilter "recent" (fun value args ->
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

            engine.RegisterFilter "by_collection" (fun value args ->
                let col = if args.Length > 0 then args.[0] else ""
                let pages = ScriptRunner.getPagesForNunjucks ()
                pages |> Array.filter (fun p ->
                    match p.TryGetValue "url" with
                    | true, (:? string as u) ->
                        let parts = u.Trim('/').Split('/')
                        parts.Length > 0 && parts.[0].Equals(col, StringComparison.OrdinalIgnoreCase)
                    | _ -> false)
                |> Array.map (fun d -> d :> obj) |> box)

            engine.RegisterFilter "search" (fun value args ->
                let q = if args.Length > 0 then args.[0] else ""
                ScriptRunner.getPagesForNunjucks ()
                |> Array.filter (fun p ->
                    match p.TryGetValue "title" with
                    | true, (:? string as t) -> t.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                    | _ -> false)
                |> Array.map (fun d -> d :> obj) |> box)

            // ── 构建上下文 ──────────────────────────────────
            let pairs = ResizeArray<string * obj>()
            // ── site.* ──────────────────────────────────────
            pairs.Add("site.title",       box config.Title)
            pairs.Add("site.description", box config.Description)
            pairs.Add("site.base_url",    box config.BaseUrl)
            pairs.Add("site.version",     box config.SiteVersion)
            pairs.Add("site.author",      box config.Author)
            pairs.Add("site.language",    box config.Language)
            for kv in globalData do
                pairs.Add("site." + kv.Key, kv.Value)
            // ── page.* ──────────────────────────────────────
            pairs.Add("page.title", box (meta.Title |> Option.defaultValue slug))
            meta.Description |> Option.iter (fun v -> pairs.Add("page.description", box v))
            for kv in meta.Extra do
                pairs.Add("page." + kv.Key, box kv.Value)
            // ── Zest 集合数据 ───────────────────────────────
            pairs.Add("pages", box (ScriptRunner.getPagesForNunjucks () |> Array.map box))
            pairs.Add("tags", box (ScriptRunner.getTagsForNunjucks ()))
            pairs.Add("collections", box (ScriptRunner.getCollectionsForNunjucks ()))
            let ctx = TemplateManager.buildNestedContext pairs
            match engine.Render bodyText ctx with
            | Ok html -> html
            | Error err ->
                eprintfn "[Zest] Nunjucks error in content '%s': %O" filePath err
                bodyText
        | None ->
            // Nunjucks 引擎不可用时，原样返回
            bodyText

    /// 构建内容目录的绝对路径。
    /// 使用 EffectiveContentDir 支持根目录管理机制：
    /// - RootDir = "." → 项目根目录即内容目录
    /// - RootDir = "content" → 使用 content 子目录
    let private resolveContentDir (config: SiteConfig) =
        Path.GetFullPath(
            Path.Combine(Directory.GetCurrentDirectory(),
                         config.EffectiveContentDir.TrimStart('.', '\\', '/')))

    /// 从文件路径计算 slug。
    let private computeSlug (filePath: string) (contentDir: string) =
        let relPath  = Path.GetRelativePath(contentDir, filePath)
        let rawSlug  =
            let fn = Path.GetFileNameWithoutExtension(filePath)
            // Handle .zpage.fsx → slug without .zpage suffix
            let fn2 = if fn.EndsWith(".zpage") then fn.[..fn.Length - 7] else fn
            // Handle .zest → legacy fallback
            if fn2.EndsWith(".zest") then fn2.[..fn2.Length - 6] else fn2
        relPath, rawSlug

    /// 构造 pageData 字典（全局数据 + 元数据扩展）。
    let private buildPageData (globalData: IDictionary<string, obj>) (meta: FrontMeta) =
        let d = Dictionary<string, obj>()
        for kv in globalData do d.[kv.Key] <- kv.Value
        for kv in meta.Extra   do d.[kv.Key] <- box kv.Value
        meta.Description |> Option.iter (fun v -> d.["description"] <- box v)
        d :> IDictionary<string, obj>

    /// 快速提取页面元数据（不执行脚本），用于 collections API 的第一遍扫描。
    let extractMeta (filePath: string) (config: SiteConfig) : Page option =
        try
            let text       = File.ReadAllText(filePath)
            let ext        = Path.GetExtension(filePath).ToLowerInvariant()
            let contentDir = resolveContentDir config
            let relPath, rawSlug = computeSlug filePath contentDir
            let meta, bodyText = FrontMatterParser.parse ext text
            let slug = PermalinkRouter.slugify rawSlug
            let title =
                meta.Title
                |> Option.orElse (
                    if ext = ".md" || ext = ".markdown" then
                        Regex.Match(bodyText, @"^#\s+(.+)$", RegexOptions.Multiline)
                        |> fun m -> if m.Success then Some(m.Groups.[1].Value.Trim()) else None
                    else None)
                |> Option.defaultValue rawSlug
            let url, outputPath =
                match meta.Permalink with
                | Some p when p.Length > 0 -> PermalinkRouter.computePermalink p
                | _ -> PermalinkRouter.defaultRoute relPath slug
            let d = Dictionary<string, obj>()
            meta.Description |> Option.iter (fun v -> d.["description"] <- box v)
            for kv in meta.Extra do d.[kv.Key] <- box kv.Value
            Some { Page.empty with
                    SourcePath = filePath
                    Url        = url
                    OutputPath = outputPath
                    Title      = title
                    Slug       = slug
                    Tags       = meta.Tags
                    Date       = meta.Date
                    Data       = d :> IDictionary<string, obj> }
        with ex ->
            eprintfn "[Zest] WARN: extractMeta failed for '%s': %s" filePath ex.Message
            None

    /// 将单个内容文件求值为 Page（失败时返回 Error）。
    /// 自动检测文件是否使用 page { ... } CE 块并执行 F# 脚本。
    let evaluate
        (filePath:   string)
        (config:     SiteConfig)
        (globalData: IDictionary<string, obj>)
        : Result<Page, string> =

        try
            let text       = File.ReadAllText(filePath)
            let ext        = Path.GetExtension(filePath).ToLowerInvariant()
            let contentDir = resolveContentDir config

            let relPath, rawSlug = computeSlug filePath contentDir
            let meta, bodyText = FrontMatterParser.parse ext text
            let slug = PermalinkRouter.slugify rawSlug

            // ── 判断是否为 page { ... } F# 脚本 ──────────────────
            let isScript = ScriptRunner.isPageScript ext text

            if isScript then
                // ── 模式 A：作为 F# 脚本求值 ──────────────────
                // 注入全局数据，使脚本内部能调用 ScriptRunner.getData 等
                ScriptRunner.setGlobalData globalData

                match ScriptRunner.evaluatePageScript text with
                | Ok htmlContent ->
                    // 合并元数据
                    let mergedData = Dictionary<string, obj>()
                    for kv in globalData         do mergedData.[kv.Key] <- kv.Value
                    for kv in meta.Extra         do mergedData.[kv.Key] <- box kv.Value
                    meta.Description |> Option.iter (fun v -> mergedData.["description"] <- box v)

                    // 确定最终 URL / OutputPath
                    let finalPermalink = meta.Permalink
                    let url, outputPath =
                        match finalPermalink with
                        | Some p when p.Length > 0 -> PermalinkRouter.computePermalink p
                        | _                         -> PermalinkRouter.defaultRoute relPath slug

                    Ok { Page.empty with
                            SourcePath   = filePath
                            Url          = url
                            OutputPath   = outputPath
                            Layout       = Some (meta.Layout |> Option.defaultValue config.DefaultLayout)
                            Title        = meta.Title |> Option.defaultValue rawSlug
                            Content      = htmlContent
                            Data         = mergedData
                            Permalink    = finalPermalink
                            Tags         = meta.Tags
                            Date         = meta.Date
                            Slug         = slug }
                | Error evalErr ->
                    // 脚本求值失败，回退到 Markdown
                    eprintfn "[Zest] WARN: 脚本求值失败 '%s'：%s — 回退到 Markdown 模式" filePath evalErr
                    // fallback to Markdown
                    let title =
                        meta.Title
                        |> Option.orElse (Regex.Match(bodyText, @"^#\s+(.+)$", RegexOptions.Multiline)
                                              |> fun m -> if m.Success then Some(m.Groups.[1].Value.Trim()) else None)
                        |> Option.defaultValue rawSlug

                    let layout = meta.Layout |> Option.defaultValue config.DefaultLayout
                    let url, outputPath =
                        match meta.Permalink with
                        | Some p when p.Length > 0 -> PermalinkRouter.computePermalink p
                        | _                         -> PermalinkRouter.defaultRoute relPath slug

                    let contentHtml =
                        match ext with
                        | ".njk" | ".nunjucks" ->
                            renderNunjucksContent bodyText config globalData meta slug filePath
                        | _ ->
                            renderContent ext bodyText text

                    Ok { Page.empty with
                            SourcePath   = filePath
                            Url          = url
                            OutputPath   = outputPath
                            Layout       = Some layout
                            Title        = title
                            Content      = contentHtml
                            Data         = buildPageData globalData meta
                            Permalink    = meta.Permalink
                            Tags         = meta.Tags
                            Date         = meta.Date
                            Slug         = slug }

            else
                // ── 模式 B：传统 Markdown / 注释元数据模式 ──────
                let title =
                    meta.Title
                    |> Option.orElse (
                        if ext = ".md" || ext = ".markdown" then
                            Regex.Match(bodyText, @"^#\s+(.+)$", RegexOptions.Multiline)
                            |> fun m -> if m.Success then Some(m.Groups.[1].Value.Trim()) else None
                        else None)
                    |> Option.defaultValue rawSlug

                let layout = meta.Layout |> Option.defaultValue config.DefaultLayout
                let url, outputPath =
                    match meta.Permalink with
                    | Some p when p.Length > 0 -> PermalinkRouter.computePermalink p
                    | _                         -> PermalinkRouter.defaultRoute relPath slug

                let contentHtml =
                    match ext with
                    | ".njk" | ".nunjucks" ->
                        renderNunjucksContent bodyText config globalData meta slug filePath
                    | _ ->
                        renderContent ext bodyText text

                Ok { Page.empty with
                        SourcePath   = filePath
                        Url          = url
                        OutputPath   = outputPath
                        Layout       = Some layout
                        Title        = title
                        Content      = contentHtml
                        ContentNodes = []
                        Data         = buildPageData globalData meta
                        Permalink    = meta.Permalink
                        Tags         = meta.Tags
                        Date         = meta.Date
                        Slug         = slug }

        with ex ->
            Error(sprintf "Failed to evaluate '%s': %s" filePath ex.Message)
