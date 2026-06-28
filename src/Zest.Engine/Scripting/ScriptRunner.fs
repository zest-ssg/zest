namespace Zest.Engine.Scripting

open System
open System.Collections.Generic
open System.IO
open System.Diagnostics
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Zest.Engine

/// Evaluates .zpage.fsx / .fsx scripts by spawning `dotnet fsi` as a subprocess.
/// Context data (pages, site config) is passed via a temp JSON file.
/// The script preamble injects DSL helpers + collections API via #load.
module ScriptRunner =

    // ── Script Cache ─────────────────────────────────────────────────────

    /// Cache entry for evaluated scripts.
    type private CacheEntry = {
        Hash: string
        Result: Result<string, string>
        Timestamp: DateTime
    }

    let private scriptCache = Dictionary<string, CacheEntry>()
    let mutable private cacheEnabled = true

    /// Enable or disable script caching.
    let setCacheEnabled (enabled: bool) = cacheEnabled <- enabled

    /// Clear the script cache.
    let clearCache () = scriptCache.Clear()

    /// Compute a hash of the script content for cache keying.
    let private computeHash (content: string) : string =
        use sha = SHA256.Create()
        let bytes = Encoding.UTF8.GetBytes(content)
        let hash = sha.ComputeHash(bytes)
        BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant()

    // ── Global state ──────────────────────────────────────────────────────

    let private globalDataRef : IDictionary<string, obj> ref =
        ref (dict [] :> IDictionary<string, obj>)

    let setGlobalData (data: IDictionary<string, obj>) =
        System.Threading.Interlocked.Exchange(globalDataRef, data) |> ignore

    let getDataString (key: string) : string =
        let mutable v : obj = null
        if (!globalDataRef).TryGetValue(key, &v) then (if isNull v then "" else v.ToString())
        else ""

    let getDataSection (prefix: string) : IDictionary<string, obj> =
        let d = Dictionary<string, obj>()
        for kv in !globalDataRef do
            if kv.Key.StartsWith(prefix + ".") then
                d.[kv.Key.Substring(prefix.Length + 1)] <- kv.Value
        d :> _

    let mutable private allPagesRef : Page list = []
    let mutable private includesRef : IDictionary<string, string> = dict []
    let mutable private verboseRef   = false

    let setAllPages (pages: Page list) = allPagesRef <- pages
    let setIncludes (includes: IDictionary<string, string>) = includesRef <- includes
    let setVerbose (v: bool) = verboseRef <- v

    let getPages () = allPagesRef
    let getPagesByTag (tag: string) =
        allPagesRef |> List.filter (fun p -> p.Tags |> List.exists (fun t -> t.Equals(tag, StringComparison.OrdinalIgnoreCase)))
    let getPagesByDir (dirName: string) =
        allPagesRef |> List.filter (fun p ->
            p.SourcePath.Contains(Path.DirectorySeparatorChar.ToString() + dirName + Path.DirectorySeparatorChar.ToString())
            || p.SourcePath.Contains("/" + dirName + "/"))
    let getRecentPages (n: int) =
        allPagesRef |> List.filter (fun p -> p.Date.IsSome) |> List.sortByDescending (fun p -> p.Date.Value) |> List.truncate n
    let includePartial (name: string) =
        match includesRef.TryGetValue(name) with true, c -> c | _ -> sprintf "<!-- include '%s' not found -->" name

    // ── Extended Collections API ──────────────────────────────────────────

    /// Get pages sorted by date (newest first).
    let getPagesByDate () =
        allPagesRef |> List.filter (fun p -> p.Date.IsSome) |> List.sortByDescending (fun p -> p.Date.Value)

    /// Get pages by collection (first URL segment).
    let getPagesByCollection (collection: string) =
        allPagesRef |> List.filter (fun p ->
            let parts = p.Url.Trim('/').Split('/')
            parts.Length > 0 && parts.[0].Equals(collection, StringComparison.OrdinalIgnoreCase))

    /// Get all unique tags from all pages.
    let getAllTags () =
        allPagesRef |> List.collect (fun p -> p.Tags) |> List.distinct |> List.sort

    /// Get all unique collections (first URL segment).
    let getAllCollections () =
        allPagesRef
        |> List.map (fun p ->
            let parts = p.Url.Trim('/').Split('/')
            if parts.Length > 0 && parts.[0] <> "" then parts.[0] else "root")
        |> List.distinct |> List.sort

    /// Search pages by title (case-insensitive).
    let searchPages (query: string) =
        allPagesRef |> List.filter (fun p -> p.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)

    /// Get page count.
    let getPageCount () = allPagesRef.Length

    /// Get pages within a date range.
    let getPagesByDateRange (fromDate: string) (toDate: string) =
        let fromDt = DateTime.Parse(fromDate)
        let toDt   = DateTime.Parse(toDate)
        allPagesRef
        |> List.filter (fun p ->
            p.Date.IsSome &&
            p.Date.Value >= fromDt &&
            p.Date.Value <= toDt)

    // ── Nunjucks data helpers ────────────────────────────────────────────

    /// Convert a Page record to a plain dict for Nunjucks template context.
    let pageToNunjucksDict (p: Page) : IDictionary<string, obj> =
        let d = Dictionary<string, obj>()
        d.["url"]    <- box p.Url
        d.["title"]  <- box p.Title
        d.["slug"]   <- box p.Slug
        d.["date"]   <- box (p.Date |> Option.map (fun d -> d.ToString("yyyy-MM-dd")) |> Option.defaultValue "")
        d.["tags"]   <- box (p.Tags |> Array.ofList)
        match p.Data.TryGetValue "description" with
        | true, v -> d.["description"] <- box v
        | _ -> ()
        d :> IDictionary<string, obj>

    /// Get all pages as plain dict arrays for Nunjucks template injection.
    let getPagesForNunjucks () : IDictionary<string, obj>[] =
        allPagesRef |> List.map pageToNunjucksDict |> Array.ofList

    /// Get all unique tags as string array.
    let getTagsForNunjucks () : string[] =
        allPagesRef
        |> List.collect (fun p -> p.Tags)
        |> List.distinct
        |> List.sort
        |> Array.ofList

    /// Get all collections (first URL segment) as string array.
    let getCollectionsForNunjucks () : string[] =
        allPagesRef
        |> List.map (fun p ->
            let parts = p.Url.Trim('/').Split('/')
            if parts.Length > 0 && parts.[0] <> "" then parts.[0] else "root")
        |> List.distinct
        |> List.sort
        |> Array.ofList

    // ── Context serialisation ─────────────────────────────────────────────

    /// Serialise all context to a JSON file that the subprocess preamble reads.
    let private writeContextFile (path: string) =
        let pageToObj (p: Page) =
            let date = p.Date |> Option.map (fun d -> d.ToString("yyyy-MM-dd")) |> Option.defaultValue ""
            let desc = match p.Data.TryGetValue("description") with true, v -> v.ToString() | _ -> ""
            {| url=p.Url; title=p.Title; date=date; slug=p.Slug; description=desc; tags=p.Tags |}
        let siteData =
            !globalDataRef
            |> Seq.map (fun kv -> kv.Key, kv.Value |> Option.ofObj |> Option.map (fun v -> v.ToString()) |> Option.defaultValue "")
            |> dict
        let payload = {|
            pages   = allPagesRef |> List.map pageToObj
            includes = includesRef |> Seq.map (fun kv -> kv.Key, kv.Value) |> dict
            siteData = siteData
        |}
        File.WriteAllText(path, JsonSerializer.Serialize(payload), Encoding.UTF8)

    // ── DSL preamble ──────────────────────────────────────────────────────

    /// Preamble injected at the top of every script before evaluation.
    /// Reads context JSON and exposes DSL helpers + collections API.

    /// Find the Zest.Dsl.dll path — looks in several common locations.
    /// Copies the DLL to a temp directory to avoid FSharp.Core version
    /// conflicts (FSI uses SDK's FSharp.Core 10.0.0.0, but publish folder
    /// has NuGet's 10.1.0.0).
    let mutable private dslDllPath = ""
    let private findDslDll () : string =
        if not (String.IsNullOrEmpty dslDllPath) && File.Exists dslDllPath then dslDllPath
        else
            let engineDir =
                let loc = System.Reflection.Assembly.GetExecutingAssembly().Location
                if not (String.IsNullOrEmpty loc) && File.Exists loc then
                    Path.GetDirectoryName loc
                else
                    AppContext.BaseDirectory
            let candidates = [
                Path.Combine(engineDir, "Zest.Dsl.dll")
                Path.Combine(engineDir, "..", "..", "..", "..", "..", "Zest.Dsl", "bin", "Release", "net10.0", "Zest.Dsl.dll")
                Path.Combine(engineDir, "..", "..", "..", "..", "..", "Zest.Dsl", "bin", "Debug", "net10.0", "Zest.Dsl.dll")
            ]
            let result =
                match candidates |> List.tryFind File.Exists with
                | Some p -> p
                | None ->
                    let rec searchUp (dir: string) =
                        let c1 = Path.Combine(dir, "Zest.Dsl.dll")
                        if File.Exists c1 then Some c1
                        else
                            let c2 = Path.Combine(dir, "src", "Zest.Dsl", "bin", "Release", "net10.0", "Zest.Dsl.dll")
                            if File.Exists c2 then Some c2
                            else
                                let c3 = Path.Combine(dir, "src", "Zest.Dsl", "bin", "Debug", "net10.0", "Zest.Dsl.dll")
                                if File.Exists c3 then Some c3
                                else
                                    let parent = Path.GetDirectoryName(dir)
                                    if String.IsNullOrEmpty parent || parent = dir then None
                                    else searchUp parent
                    match searchUp (Directory.GetCurrentDirectory()) with
                    | Some p -> p
                    | None -> failwithf "Zest.Dsl.dll not found. Engine dir: %s" engineDir
            dslDllPath <- result
            result

    /// Copy Zest.Dsl.dll to an isolated temp directory (without FSharp.Core.dll)
    /// so FSI uses its own FSharp.Core instead of the publish folder's version.
    let private dslDllCache = Dictionary<string, string>()
    let private getIsolatedDslDll () : string =
        let srcPath = findDslDll ()
        let srcHash =
            use md5 = System.Security.Cryptography.MD5.Create()
            use stream = File.OpenRead(srcPath)
            let hash = md5.ComputeHash(stream)
            Convert.ToHexString(hash)
        match dslDllCache.TryGetValue(srcHash) with
        | true, cachedPath -> cachedPath
        | false, _ ->
            let tempDir = Path.Combine(Path.GetTempPath(), "zest-dsl-" + srcHash)
            Directory.CreateDirectory(tempDir) |> ignore
            let destPath = Path.Combine(tempDir, "Zest.Dsl.dll")
            if not (File.Exists destPath) then
                File.Copy(srcPath, destPath, true)
            dslDllCache.[srcHash] <- destPath
            destPath

    let private buildPreamble (ctxFile: string) =
        let dllPath = getIsolatedDslDll ()
        let sb = Text.StringBuilder()
        sb.AppendLine("#r @\"" + dllPath + "\"") |> ignore
        sb.AppendLine("open System") |> ignore
        sb.AppendLine("open System.Text.RegularExpressions") |> ignore
        sb.AppendLine("open System.Collections.Generic") |> ignore
        sb.AppendLine("open Zest.Dsl") |> ignore
        sb.AppendLine("Zest.Dsl.Context.current <- Some (Zest.Dsl.ZestContext(@\"" + ctxFile + "\"))") |> ignore
        sb.AppendLine("open Zest.Dsl.Dsl") |> ignore
        sb.AppendLine("open Zest.Dsl.DslComponents") |> ignore
        sb.AppendLine("open Zest.Dsl.DslCollections") |> ignore
        sb.AppendLine("open Zest.Dsl.DslUtilities") |> ignore
        sb.AppendLine("open Zest.Dsl.DslSeo") |> ignore
        sb.AppendLine("open Zest.Dsl.DslXml") |> ignore
        // Debug helper: prints a message to stderr during FSI evaluation
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
        // Set env vars to speed up .NET runtime startup
        psi.EnvironmentVariables.["DOTNET_System_GlobalizationInvariant"] <- "1"
        psi.EnvironmentVariables.["DOTNET_TieredPGO"] <- "0"
        psi.EnvironmentVariables.["DOTNET_ReadyToRun"] <- "1"
        use proc = Process.Start(psi)
        // IMPORTANT: read stdout/stderr asynchronously to avoid deadlock.
        // Synchronous ReadToEnd on one stream blocks the other, causing
        // deadlock when the stdout buffer fills before the process exits.
        let stdoutTask = proc.StandardOutput.ReadToEndAsync()
        let stderrTask = proc.StandardError.ReadToEndAsync()
        // Wait for process with timeout to avoid infinite hang
        if not (proc.WaitForExit(60_000)) then
            try proc.Kill() with _ -> ()
            Error "FSI process timed out (60s)"
        else
            let stdout = stdoutTask.Result
            let stderr = stderrTask.Result
            // Verbose mode: print FSI stderr to console for debugging
            if verboseRef && not (String.IsNullOrEmpty stderr) then
                Console.ForegroundColor <- ConsoleColor.DarkGray
                Console.Error.WriteLine("[FSI] ---- stderr ----")
                Console.Error.WriteLine(stderr)
                Console.Error.WriteLine("[FSI] ---- end stderr ----")
                Console.ResetColor()
            if proc.ExitCode = 0 then Ok stdout
            else
                // Build a comprehensive error report with formatted lines
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
                if verboseRef && errLines.Length > 0 then
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
            // Check cache first
            let hash = computeHash scriptText
            if cacheEnabled && scriptCache.ContainsKey(hash) then
                scriptCache.[hash].Result
            else
                // Ensure context file exists (resetSession may not have been called)
                if String.IsNullOrEmpty ctxFilePath || not (File.Exists ctxFilePath) then
                    resetSession ()

                // Strip metadata comments from script
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
                        let result = Error(sprintf "FSI evaluation reported errors — %s" msg)
                        // Don't cache errors
                        result
                finally
                    if File.Exists tmpFsx then File.Delete tmpFsx
        with ex ->
            Error(sprintf "ScriptRunner threw: %s" ex.Message)

    // ── Batch evaluation: evaluate multiple scripts in ONE FSI process ────
    // This dramatically improves build performance by avoiding repeated
    // `dotnet fsi` process startups (each takes 3-5 seconds).

    ///<summary>
    /// Evaluate multiple page scripts in a single FSI subprocess.
    /// Returns a map from script hash → result string.
    ///</summary>
    let evaluatePageScriptsBatch (scripts: (string * string) list) : Map<string, Result<string, string>> =
        if scripts.IsEmpty then Map.empty
        else
            try
                // Ensure context file exists
                if String.IsNullOrEmpty ctxFilePath || not (File.Exists ctxFilePath) then
                    resetSession ()

                // Build a single combined script: preamble + each page wrapped with markers
                let preamble = buildPreamble ctxFilePath

                // Sentinel markers to split outputs
                let marker id = sprintf "___ZEST_PAGE_START_%s___" id
                let endMarker id = sprintf "___ZEST_PAGE_END_%s___" id

                let body = StringBuilder()
                for (id, scriptText) in scripts do
                    // Strip metadata comments
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
                        // Parse output and split by markers
                        let results = Dictionary<string, Result<string, string>>()
                        for (id, _) in scripts do
                            let s = marker id
                            let e = endMarker id
                            let sIdx = stdout.IndexOf(s)
                            let eIdx = stdout.IndexOf(e)
                            if sIdx >= 0 && eIdx > sIdx then
                                let content = stdout.Substring(sIdx + s.Length, eIdx - (sIdx + s.Length))
                                // Strip leading newline from printfn
                                let trimmed = content.TrimStart('\r', '\n')
                                results.[id] <- Ok trimmed
                            else
                                results.[id] <- Error "Script output not found in batch"
                        // Cache successful results
                        for (id, scriptText) in scripts do
                            match results.TryGetValue(id) with
                            | true, Ok html when cacheEnabled ->
                                let hash = computeHash scriptText
                                scriptCache.[hash] <- { Hash = hash; Result = Ok html; Timestamp = DateTime.Now }
                            | _ -> ()
                        results |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq
                    | Error msg ->
                        // Batch failed — fall back to individual evaluation
                        if verboseRef then
                            Console.Error.WriteLine(sprintf "[FSI] Batch failed, falling back to individual: %s" msg)
                        scripts |> List.map (fun (id, scriptText) ->
                            id, evaluatePageScript scriptText) |> Map.ofList
                finally
                    if File.Exists tmpFsx then File.Delete tmpFsx
            with ex ->
                scripts |> List.map (fun (id, _) ->
                    id, Error(sprintf "Batch evaluation threw: %s" ex.Message)) |> Map.ofList
