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

/// Evaluates .zpage.fsx / .fsx scripts by spawning `dotnet fsi` as a subprocess.
/// Optimized with static SHA256 and pre-sized StringBuilder.
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
        let sb = Text.StringBuilder(512)
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

    // ── Subprocess evaluation ─────────────────────────────────────────────

    let private runFsi (scriptPath: string) : Result<string, string> =
        let psi = ProcessStartInfo("dotnet", sprintf "fsi --quiet --nologo --exec \"%s\"" scriptPath)
        psi.UseShellExecute        <- false
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError  <- true
        psi.StandardOutputEncoding <- Encoding.UTF8
        psi.StandardErrorEncoding  <- Encoding.UTF8
        psi.CreateNoWindow         <- true
        psi.EnvironmentVariables.["DOTNET_System_GlobalizationInvariant"] <- "1"
        psi.EnvironmentVariables.["DOTNET_TieredPGO"] <- "0"
        psi.EnvironmentVariables.["DOTNET_ReadyToRun"] <- "1"
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

    // ── Public API ────────────────────────────────────────────────────────

    let isPageScript (ext: string) (text: string) =
        if ext = ".md" || ext = ".markdown" then false
        else
            text.Split('\n')
            |> Array.map (fun l -> l.Trim())
            |> Array.tryFind (fun l ->
                not (String.IsNullOrEmpty l)
                && not (l.StartsWith("//"))
                && not (l.StartsWith("#r "))
                && not (l.StartsWith("#load ")))
            |> Option.map (fun l ->
                l.StartsWith("render") || l.StartsWith("page {") || l = "page"
                || l.StartsWith("let ") || l.StartsWith("open "))
            |> Option.defaultValue false

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

    let evaluatePageScriptsBatch (scripts: (string * string) list) : Map<string, Result<string, string>> =
        if scripts.IsEmpty then Map.empty
        else
            try
                if String.IsNullOrEmpty ctxFilePath || not (File.Exists ctxFilePath) then
                    resetSession ()

                let preamble = buildPreamble ctxFilePath

                let marker id = sprintf "___ZEST_PAGE_START_%s___" id
                let endMarker id = sprintf "___ZEST_PAGE_END_%s___" id

                // Pre-size StringBuilder based on total script length
                let totalScriptLen = scripts |> List.sumBy (fun (s: string * string) -> (snd s).Length)
                let estimatedSize = totalScriptLen + preamble.Length + 1000
                let body = StringBuilder(estimatedSize)
                for (id, scriptText) in scripts do
                    let stripped =
                        scriptText.Split('\n')
                        |> Array.filter (fun l ->
                            let t = l.TrimStart()
                            not (t.StartsWith("// @")))
                        |> String.concat "\n"
                    Printf.bprintf body "\nprintfn \"%s\"\n" (marker id)
                    Printf.bprintf body "%s\n" stripped
                    Printf.bprintf body "printfn \"%s\"\n" (endMarker id)

                let tmpFsx = Path.Combine(Path.GetTempPath(), sprintf "zest-batch-%s.fsx" (Guid.NewGuid().ToString("N")))
                try
                    File.WriteAllText(tmpFsx, preamble + "\n" + body.ToString(), Encoding.UTF8)
                    match runFsi tmpFsx with
                    | Ok stdout ->
                        let results = Dictionary<string, Result<string, string>>()
                        for (id, _) in scripts do
                            let s = marker id
                            let e = endMarker id
                            let sIdx = stdout.IndexOf(s, StringComparison.Ordinal)
                            let eIdx = stdout.IndexOf(e, StringComparison.Ordinal)
                            if sIdx >= 0 && eIdx > sIdx then
                                let content = stdout.Substring(sIdx + s.Length, eIdx - (sIdx + s.Length))
                                let trimmed = content.TrimStart('\r', '\n')
                                results.[id] <- Ok trimmed
                            else
                                results.[id] <- Error "Script output not found in batch"
                        for (id, scriptText) in scripts do
                            match results.TryGetValue(id) with
                            | true, Ok html when cacheEnabled ->
                                let hash = computeHash scriptText
                                scriptCache.[hash] <- { Hash = hash; Result = Ok html; Timestamp = DateTime.Now }
                            | _ -> ()
                        results |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq
                    | Error msg ->
                        if !PageQuery.verboseRef then
                            Console.Error.WriteLine(sprintf "[FSI] Batch failed, falling back to individual: %s" msg)
                        scripts |> List.map (fun (id, scriptText) ->
                            id, evaluatePageScript scriptText) |> Map.ofList
                finally
                    if File.Exists tmpFsx then File.Delete tmpFsx
            with ex ->
                scripts |> List.map (fun (id, _) ->
                    id, Error(sprintf "Batch evaluation threw: %s" ex.Message)) |> Map.ofList
