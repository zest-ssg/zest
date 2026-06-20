namespace Zest.Engine.Scripting

open System
open System.Collections.Generic
open System.IO
open System.Text.RegularExpressions
open Zest.Engine
open Zest.Engine.Parsing
open Zest.Engine.Routing
open Zest.Engine.Html

/// 负责将 .zest.fsx / .md 文件求值为 Page 记录。
module ScriptEvaluator =

    /// 从脚本文本中提取并渲染内容 HTML（传统 Markdown 回退模式）。
    let private renderContent (ext: string) (bodyText: string) (fullText: string) : string =
        match ext with
        | ".md" | ".markdown" ->
            Markdown.toHtml bodyText
        | _ ->
            // .zest.fsx: 剥去元数据注释和 #r / #load 指令，其余视为 Markdown 内容
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

    /// 构建内容目录的绝对路径。
    let private resolveContentDir (config: SiteConfig) =
        Path.GetFullPath(
            Path.Combine(Directory.GetCurrentDirectory(),
                         config.ContentDir.TrimStart('.', '\\', '/')))

    /// 从文件路径计算 slug。
    let private computeSlug (filePath: string) (contentDir: string) =
        let relPath  = Path.GetRelativePath(contentDir, filePath)
        let rawSlug  =
            let fn = Path.GetFileNameWithoutExtension(filePath)
            if fn.EndsWith(".zest") then fn.[..fn.Length - 6] else fn
        relPath, rawSlug

    /// 构造 pageData 字典（全局数据 + 元数据扩展）。
    let private buildPageData (globalData: IDictionary<string, obj>) (meta: FrontMeta) =
        let d = Dictionary<string, obj>()
        for kv in globalData do d.[kv.Key] <- kv.Value
        for kv in meta.Extra   do d.[kv.Key] <- box kv.Value
        meta.Description |> Option.iter (fun v -> d.["description"] <- box v)
        d :> IDictionary<string, obj>

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
                    eprintfn "[Zest] 脚本求值失败 '%s'：%s — 回退到 Markdown 模式" filePath evalErr
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

                    let contentHtml = renderContent ext bodyText text

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

                let contentHtml = renderContent ext bodyText text

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
