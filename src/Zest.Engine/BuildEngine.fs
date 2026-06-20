namespace Zest.Engine

open System
open System.Collections.Generic
open System.IO
open System.Diagnostics
open Zest.Engine
open Zest.Engine.Scripting
open Zest.Engine.Zss

/// 核心构建管线：内容发现 → 求值 → 布局应用 → 资产处理 → 输出。
module BuildEngine =

    let private resolvePath root dir =
        Path.GetFullPath(Path.Combine(root, dir.ToString().TrimStart('.', '\\', '/')))

    let private isExcluded (contentDir: string) (filePath: string) =
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
                Path.GetFileNameWithoutExtension(f), (f, File.ReadAllText(f)))
            |> Map.ofArray

    /// 从 _data/ 目录加载 TOML 文件为全局数据字典。
    let private loadGlobalData (dataDir: string) : IDictionary<string, obj> =
        let dict = Dictionary<string, obj>()
        if not (Directory.Exists dataDir) then dict :> _
        else
            let tomlFiles = Directory.GetFiles(dataDir, "*.toml", SearchOption.AllDirectories)
            for file in tomlFiles do
                try
                    let name = Path.GetFileNameWithoutExtension(file)
                    let text = File.ReadAllText(file)
                    let model = Tomlyn.Toml.ToModel(text)
                    if model <> null then
                        for kv in model do
                            dict.[name + "." + kv.Key] <- kv.Value
                        // 也按文件名直接注册顶层
                        dict.[name] <- model :> obj
                with ex ->
                    eprintfn "[Zest] Failed to load data '%s': %s" file ex.Message
            dict :> _

    let rec private applyLayout (name: string) (content: string) (layouts: Map<string, string * string>) =
        match layouts.TryFind name with
        | None -> content
        | Some (layoutPath, layoutText) ->
            let rendered =
                layoutText
                    .Replace("{{ content }}",      content)
                    .Replace("{{ page.content }}", content)
            // 检测嵌套布局
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
            | Some nl when nl <> name -> applyLayout nl rendered layouts
            | _                       -> rendered

    let private copyAssets (projectRoot: string) (outputDir: string) =
        let assetsSource = Path.Combine(projectRoot, "assets")
        if not (Directory.Exists assetsSource) then 0
        else
            let assetsTarget = Path.Combine(outputDir, "assets")
            Directory.CreateDirectory(assetsTarget) |> ignore
            let mutable count = 0
            for file in Directory.GetFiles(assetsSource, "*", SearchOption.AllDirectories) do
                let ext      = Path.GetExtension(file).ToLowerInvariant()
                let relative = Path.GetRelativePath(assetsSource, file)
                if ext = ".zss" then
                    let cssRel    = Path.ChangeExtension(relative, ".css")
                    let targetFile = Path.Combine(assetsTarget, cssRel)
                    let targetDir  = Path.GetDirectoryName(targetFile)
                    if targetDir <> null then Directory.CreateDirectory(targetDir) |> ignore
                    Processor.processFileTo file targetFile |> ignore
                else
                    let targetFile = Path.Combine(assetsTarget, relative)
                    let targetDir  = Path.GetDirectoryName(targetFile)
                    if targetDir <> null then Directory.CreateDirectory(targetDir) |> ignore
                    let srcInfo = FileInfo(file)
                    let tgtInfo = FileInfo(targetFile)
                    if not tgtInfo.Exists || srcInfo.LastWriteTimeUtc > tgtInfo.LastWriteTimeUtc then
                        File.Copy(file, targetFile, overwrite = true)
                count <- count + 1
            count

    let execute (config: SiteConfig) : BuildResult =
        let sw     = Stopwatch.StartNew()
        let errors = ResizeArray<string>()
        let mutable totalPages    = 0
        let mutable processedPages = 0
        let mutable assetsCopied  = 0
        try
            let root       = Directory.GetCurrentDirectory()
            let contentDir = resolvePath root config.ContentDir
            let outputDir  = resolvePath root config.OutputDir
            let layoutsDir = resolvePath root config.LayoutsDir
            let dataDir    = resolvePath root config.DataDir

            Directory.CreateDirectory(outputDir) |> ignore
            let layouts     = loadLayouts layoutsDir
            let globalData  = loadGlobalData dataDir

            let contentFiles =
                if not (Directory.Exists contentDir) then
                    Directory.CreateDirectory(contentDir) |> ignore; [||]
                else
                    [| yield! Directory.GetFiles(contentDir, "*.zest.fsx",  SearchOption.AllDirectories)
                       yield! Directory.GetFiles(contentDir, "*.fsx",       SearchOption.AllDirectories)
                       yield! Directory.GetFiles(contentDir, "*.md",        SearchOption.AllDirectories)
                       yield! Directory.GetFiles(contentDir, "*.markdown",  SearchOption.AllDirectories) |]
                    |> Array.filter (fun f -> not (isExcluded contentDir f))
                    |> Array.distinct

            for filePath in contentFiles do
                try
                    match ScriptEvaluator.evaluate filePath config globalData with
                    | Error e -> errors.Add(e)
                    | Ok page ->
                        let layoutName  = page.Layout |> Option.defaultValue config.DefaultLayout
                        let finalContent = applyLayout layoutName page.Content layouts
                        let outPath     = Path.Combine(outputDir, page.OutputPath)
                        let outDir      = Path.GetDirectoryName(outPath)
                        if outDir <> null then Directory.CreateDirectory(outDir) |> ignore
                        File.WriteAllText(outPath, finalContent)
                        totalPages    <- totalPages    + 1
                        processedPages <- processedPages + 1
                with ex ->
                    errors.Add(sprintf "Failed to process '%s': %s" filePath ex.Message)

            assetsCopied <- copyAssets root outputDir
            sw.Stop()
            { TotalPages     = totalPages
              ProcessedPages = processedPages
              CachedPages    = 0
              AssetsCopied   = assetsCopied
              AssetsMinified = 0
              DurationMs     = sw.ElapsedMilliseconds
              Errors         = errors |> Seq.toList }
        with ex ->
            errors.Add(sprintf "Build failed: %s" ex.Message)
            sw.Stop()
            { TotalPages     = totalPages
              ProcessedPages = processedPages
              CachedPages    = 0
              AssetsCopied   = assetsCopied
              AssetsMinified = 0
              DurationMs     = sw.ElapsedMilliseconds
              Errors         = errors |> Seq.toList }
