namespace Zest.Engine.Scripting

open System
open System.Collections.Generic
open System.IO
open System.Diagnostics
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Zest.Engine
open PageQuery
open ScriptDiscovery

/// Evaluates .zest.fsx / .fsx scripts by spawning `dotnet fsi` as a subprocess.
/// Optimized with static SHA256, pre-sized StringBuilder, and long-running FSI session reuse.
module ScriptRunner =

    // ── Script Cache ─────────────────────────────────────────────────────

    type private CacheEntry = {
        Hash: string
        Result: Result<string, string>
        Timestamp: DateTime
    }

    let private scriptCache = Dictionary<string, CacheEntry>()
    let mutable private cacheEnabled = true

    let setCacheEnabled (enabled: bool) = cacheEnabled <- enabled
    let clearCache () = scriptCache.Clear()

    // Static SHA256 instance (reused, thread-safe via lock)
    let private sha256 = SHA256.Create()
    let private sha256Lock = obj()

    let private computeHash (content: string) : string =
        let bytes = Encoding.UTF8.GetBytes(content)
        let hash = lock sha256Lock (fun () -> sha256.ComputeHash(bytes))
        BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant()

    // ── Context serialisation ─────────────────────────────────────────────

    let private writeContextFile (path: string) =
        let pageToObj (p: ContentPage) =
            let date = p.Date |> Option.map (fun d -> d.ToString("yyyy-MM-dd")) |> Option.defaultValue ""
            let desc = match p.Data.TryGetValue("description") with true, v -> v.ToString() | _ -> ""
            {| url=p.Url; title=p.Title; date=date; slug=p.Slug; description=desc; tags=p.Tags |}
        let siteData =
            !PageQuery.globalDataRef
            |> Seq.map (fun kv -> kv.Key, kv.Value |> Option.ofObj |> Option.map (fun v -> v.ToString()) |> Option.defaultValue "")
            |> dict
        let payload = {|
            pages   = !PageQuery.allPagesRef |> List.map pageToObj
            includes = !PageQuery.includesRef |> Seq.map (fun kv -> kv.Key, kv.Value) |> dict
            siteData = siteData
        |}
        File.WriteAllText(path, JsonSerializer.Serialize(payload), Encoding.UTF8)

    // ── DSL preamble ──────────────────────────────────────────────────────

    let private buildPreamble (ctxFile: string) =
        let dllPath = ScriptDiscovery.getIsolatedDslDll ()
        let sb = Text.StringBuilder(1024)
        sb.AppendLine("#r @\"" + dllPath + "\"") |> ignore
        sb.AppendLine("open System") |> ignore
        sb.AppendLine("open System.Text.RegularExpressions") |> ignore
        sb.AppendLine("open System.Collections.Generic") |> ignore
        sb.AppendLine("open Zest.Dsl") |> ignore
        sb.AppendLine("Zest.Dsl.Context.current <- Some (Zest.Dsl.ZestContext(@\"" + ctxFile + "\"))") |> ignore
        sb.AppendLine("open Zest.Dsl.Dsl") |> ignore
        sb.AppendLine("open Zest.Dsl.DslComponents") |> ignore
        sb.AppendLine("open Zest.Dsl.DslSugar") |> ignore
        sb.AppendLine("open Zest.Dsl.DslCollections") |> ignore
        sb.AppendLine("open Zest.Dsl.DslUtilities") |> ignore
        sb.AppendLine("open Zest.Dsl.DslSeo") |> ignore
        sb.AppendLine("open Zest.Dsl.DslXml") |> ignore
        sb.AppendLine("""let console_log (message: string) = eprintfn "[DEBUG] %s" message""") |> ignore
        sb.ToString()

    // ── FSI process helper (shared env var config) ────────────────────────

    let private configureFsiProcess (psi: ProcessStartInfo) =
        psi.UseShellExecute        <- false
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError  <- true
        psi.StandardOutputEncoding <- Encoding.UTF8
        psi.StandardErrorEncoding  <- Encoding.UTF8
        psi.CreateNoWindow         <- true
        // Performance: enable tiered PGO for faster startup, limit GC heaps
        psi.EnvironmentVariables.["DOTNET_TieredPGO"] <- "1"
        psi.EnvironmentVariables.["DOTNET_ReadyToRun"] <- "1"
        psi.EnvironmentVariables.["DOTNET_GCHeapCount"] <- "1"
        psi.EnvironmentVariables.["DOTNET_GCDynamicAdaptationMode"] <- "0"

    // ── FSI: single-exec mode (used for both individual and batch scripts) ─

    let private runFsi (scriptPath: string) : Result<string, string> =
        let psi = ProcessStartInfo("dotnet", sprintf "fsi --quiet --nologo --exec \"%s\"" scriptPath)
        configureFsiProcess psi
        use proc = Process.Start(psi)
        let stdoutTask = proc.StandardOutput.ReadToEndAsync()
        let stderrTask = proc.StandardError.ReadToEndAsync()
        if not (proc.WaitForExit(60_000)) then
            try proc.Kill() with _ -> ()
            Error "FSI process timed out (60s)"
        else
            let stdout = stdoutTask.Result
            let stderr = stderrTask.Result
            if !PageQuery.verboseRef && not (String.IsNullOrEmpty stderr) then
                Console.ForegroundColor <- ConsoleColor.DarkGray
                Console.Error.WriteLine("[FSI] ---- stderr ----")
                Console.Error.WriteLine(stderr)
                Console.Error.WriteLine("[FSI] ---- end stderr ----")
                Console.ResetColor()
            if proc.ExitCode = 0 then Ok stdout
            else
                let errLines =
                    stderr.Split('\n')
                    |> Array.filter (fun l ->
                        not (String.IsNullOrWhiteSpace l)
                        && not (l.Contains("warning FS"))
                        && not (l.Contains("info :")))
                    |> Array.truncate 20
                let formattedErrors =
                    errLines
                    |> Array.mapi (fun i line ->
                        if line.Contains("error FS") || line.Contains("error:") then
                            sprintf "  ▶ %s" (line.Trim())
                        else
                            sprintf "    %s" (line.Trim()))
                    |> String.concat "\n"
                if !PageQuery.verboseRef && errLines.Length > 0 then
                    Console.ForegroundColor <- ConsoleColor.Red
                    Console.Error.WriteLine(sprintf "[FSI] Evaluation failed — %d error(s)" errLines.Length)
                    Console.Error.WriteLine(formattedErrors)
                    Console.ResetColor()
                Error(formattedErrors)

    // ── Context file path (per-build, shared) ─────────────────────────────

    let mutable private ctxFilePath = ""

    let resetSession () =
        ctxFilePath <- Path.Combine(Path.GetTempPath(), sprintf "zest-ctx-%s.json" (Guid.NewGuid().ToString("N")))
        writeContextFile ctxFilePath

    // ── isPageScript: detect whether a file uses the Zest DSL render pipeline.
    //   .zest.fsx always returns true (the extension guarantees it).
    //   .fsx files are matched by keyword patterns.
    //   .md / .markdown always returns false (plain Markdown).

    let isPageScript (ext: string) (text: string) =
        match ext with
        | FileExtensions.Markdown | FileExtensions.MarkdownLong -> false
        | FileExtensions.ZestScript -> true
        | _ ->
            text.Split('\n')
            |> Array.map (fun l -> l.Trim())
            |> Array.tryFind (fun l ->
                not (String.IsNullOrEmpty l)
                && not (l.StartsWith("//"))
                && not (l.StartsWith("#r "))
                && not (l.StartsWith("#load ")))
            |> Option.map (fun l ->
                l.StartsWith("render") || l = "page" || l.StartsWith("page {")
                || l.StartsWith("let ") || l.StartsWith("open ")
                || l.StartsWith("div ") || l.StartsWith("divC ")
                || l.StartsWith("h1 ") || l.StartsWith("h2 ") || l.StartsWith("h3 ")
                || l.StartsWith("p ") || l.StartsWith("section ") || l.StartsWith("article ")
                || l.StartsWith("a ") || l.StartsWith("span ")
                || l.StartsWith("ul ") || l.StartsWith("ol "))
            |> Option.defaultValue false

    // ── F# layout evaluation ───────────────────────────────────────────────
    //   A `.zest.fsx`/`.fsx` layout is evaluated by FSI (just like a page
    //   script). The page `content`, `page` metadata, and `site` config are
    //   injected as top-level F# bindings via a generated `#load` data file so
    //   the layout can compose the final document with the DSL builders and
    //   `printf`/`render` the result to stdout (which becomes the rendered HTML).

    let private buildLayoutPreamble (ctxFile: string) (dataFile: string) =
        let dllPath = ScriptDiscovery.getIsolatedDslDll ()
        let sb = Text.StringBuilder(1024)
        sb.AppendLine("#r @\"" + dllPath + "\"") |> ignore
        sb.AppendLine("#load @\"" + dataFile + "\"") |> ignore
        sb.AppendLine("open System") |> ignore
        sb.AppendLine("open System.Text.RegularExpressions") |> ignore
        sb.AppendLine("open System.Collections.Generic") |> ignore
        sb.AppendLine("open Zest.Dsl") |> ignore
        sb.AppendLine("Zest.Dsl.Context.current <- Some (Zest.Dsl.ZestContext(@\"" + ctxFile + "\"))") |> ignore
        sb.AppendLine("open Zest.Dsl.Dsl") |> ignore
        sb.AppendLine("open Zest.Dsl.DslComponents") |> ignore
        sb.AppendLine("open Zest.Dsl.DslSugar") |> ignore
        sb.AppendLine("open Zest.Dsl.DslCollections") |> ignore
        sb.AppendLine("open Zest.Dsl.DslUtilities") |> ignore
        sb.AppendLine("open Zest.Dsl.DslSeo") |> ignore
        sb.AppendLine("open Zest.Dsl.DslXml") |> ignore
        sb.AppendLine("""let console_log (message: string) = eprintfn "[DEBUG] %s" message""") |> ignore
        sb.ToString()

    let evaluateLayoutScript (scriptText: string) (content: string)
                              (page: ContentPage) (config: SiteConfig)
                              (globalData: IDictionary<string, obj>) : Result<string, string> =
        try
            if String.IsNullOrEmpty ctxFilePath || not (File.Exists ctxFilePath) then resetSession ()

            let date = page.Date |> Option.map (fun d -> d.ToString("yyyy-MM-dd")) |> Option.defaultValue ""
            let desc = match page.Data.TryGetValue("description") with true, v -> v.ToString() | _ -> ""
            let mutable gv = Unchecked.defaultof<obj>
            let github  = if globalData.TryGetValue("social_github",  &gv) then gv.ToString() else ""
            let twitter = if globalData.TryGetValue("social_twitter", &gv) then gv.ToString() else ""

            let esc (s: string) =
                s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n")

            let tagsArr =
                page.Tags
                |> Seq.map (fun t -> "\"" + esc t + "\"")
                |> String.concat "; "

            let dataContent =
                sprintf "let content = \"%s\"\n" (esc content)
                + sprintf "let page = {| title = \"%s\"; url = \"%s\"; date = \"%s\"; slug = \"%s\"; description = \"%s\"; tags = [|%s|] |}\n"
                    (esc page.Title) (esc page.Url) (esc date) (esc page.Slug) (esc desc) tagsArr
                + sprintf "let site = {| title = \"%s\"; description = \"%s\"; author = \"%s\"; language = \"%s\"; social_github = \"%s\"; social_twitter = \"%s\" |}\n"
                    (esc config.Title) (esc config.Description) (esc config.Author) (esc config.Language) (esc github) (esc twitter)

            let dataFile = Path.Combine(Path.GetTempPath(), sprintf "zest-layout-ctx-%s.fsx" (Guid.NewGuid().ToString("N")))
            File.WriteAllText(dataFile, dataContent, Encoding.UTF8)
            let preamble = buildLayoutPreamble ctxFilePath dataFile
            let tmpFsx = Path.Combine(Path.GetTempPath(), sprintf "zest-layout-%s.fsx" (Guid.NewGuid().ToString("N")))
            try
                File.WriteAllText(tmpFsx, preamble + "\n" + scriptText, Encoding.UTF8)
                runFsi tmpFsx
            finally
                if File.Exists tmpFsx  then File.Delete tmpFsx
                if File.Exists dataFile then File.Delete dataFile
        with ex ->
            Error(sprintf "Layout evaluation threw: %s" ex.Message)

    // ── Individual script evaluation ──────────────────────────────────────

    let evaluatePageScript (scriptText: string) : Result<string, string> =
        try
            let hash = computeHash scriptText
            if cacheEnabled && scriptCache.ContainsKey(hash) then
                scriptCache.[hash].Result
            else
                if String.IsNullOrEmpty ctxFilePath || not (File.Exists ctxFilePath) then
                    resetSession ()

                let stripped =
                    scriptText.Split('\n')
                    |> Array.filter (fun l ->
                        let t = l.TrimStart()
                        not (t.StartsWith("// @")))
                    |> String.concat "\n"

                let tmpFsx = Path.Combine(Path.GetTempPath(), sprintf "zest-page-%s.fsx" (Guid.NewGuid().ToString("N")))
                try
                    File.WriteAllText(tmpFsx, (buildPreamble ctxFilePath) + "\n" + stripped, Encoding.UTF8)
                    match runFsi tmpFsx with
                    | Ok html ->
                        let result = Ok html
                        if cacheEnabled then
                            scriptCache.[hash] <- { Hash = hash; Result = result; Timestamp = DateTime.Now }
                        result
                    | Error msg ->
                        Error(sprintf "FSI evaluation reported errors — %s" msg)
                finally
                    if File.Exists tmpFsx then File.Delete tmpFsx
        with ex ->
            Error(sprintf "ScriptRunner threw: %s" ex.Message)

    // ── Batch evaluation: all page scripts in ONE FSI process ─────────────
    //   Uses numeric marker IDs to avoid backslash/special-char issues in paths.

    let evaluatePageScriptsBatch (scripts: (string * string) list) : Map<string, Result<string, string>> =
        if scripts.IsEmpty then Map.empty
        else
            try
                if String.IsNullOrEmpty ctxFilePath || not (File.Exists ctxFilePath) then
                    resetSession ()

                let preamble = buildPreamble ctxFilePath
                let sb = System.Text.StringBuilder(preamble.Length + 2048)
                sb.Append(preamble) |> ignore

                // Map numeric IDs → file paths (avoids backslash escaping in F# string literals)
                let idMap = Dictionary<int, string>()
                let mutable idx = 0

                for filePath, scriptText in scripts do
                    let stripped =
                        scriptText.Split('\n')
                        |> Array.filter (fun l ->
                            let t = l.TrimStart()
                            not (t.StartsWith("// @")))
                        |> String.concat "\n"
                    let markerId = idx
                    idMap.[markerId] <- filePath
                    idx <- idx + 1
                    Printf.bprintf sb "\nprintfn \"___ZEST_BATCH_START_%d___\"\n" markerId
                    Printf.bprintf sb "%s\n" stripped
                    Printf.bprintf sb "printfn \"___ZEST_BATCH_END_%d___\"\n" markerId

                let tmpFsx = Path.Combine(Path.GetTempPath(), sprintf "zest-batch-%s.fsx" (Guid.NewGuid().ToString("N")))
                try
                    File.WriteAllText(tmpFsx, sb.ToString(), Encoding.UTF8)
                    match runFsi tmpFsx with
                    | Ok stdout ->
                        let results = Dictionary<string, Result<string, string>>()
                        for kv in idMap do
                            let markerId = kv.Key
                            let filePath = kv.Value
                            let s = sprintf "___ZEST_BATCH_START_%d___" markerId
                            let e = sprintf "___ZEST_BATCH_END_%d___" markerId
                            let sIdx = stdout.IndexOf(s, StringComparison.Ordinal)
                            let eIdx = stdout.IndexOf(e, StringComparison.Ordinal)
                            if sIdx >= 0 && eIdx > sIdx then
                                let content = stdout.Substring(sIdx + s.Length, eIdx - (sIdx + s.Length))
                                let trimmed = content.Trim()
                                if trimmed.Length > 0 then
                                    results.[filePath] <- Ok trimmed
                                else
                                    results.[filePath] <- Ok content
                            else
                                results.[filePath] <- Error (sprintf "Script output not found in batch stdout (id: %d)" markerId)

                        for filePath, scriptText in scripts do
                            match results.TryGetValue(filePath) with
                            | true, Ok html when cacheEnabled ->
                                let hash = computeHash scriptText
                                scriptCache.[hash] <- { Hash = hash; Result = Ok html; Timestamp = DateTime.Now }
                            | _ -> ()
                        results |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq
                    | Error msg ->
                        if !PageQuery.verboseRef then
                            Console.Error.WriteLine(sprintf "[FSI] Batch failed: %s" msg)
                        scripts |> List.map (fun (id, scriptText) ->
                            id, evaluatePageScript scriptText) |> Map.ofList
                finally
                    if File.Exists tmpFsx then File.Delete tmpFsx
            with ex ->
                scripts |> List.map (fun (id, _) ->
                    id, Error(sprintf "Batch evaluation threw: %s" ex.Message)) |> Map.ofList
