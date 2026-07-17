namespace Zest.Engine.Scripting

open System
open System.Collections.Generic
open System.IO
open System.Diagnostics
open System.Text
open System.Text.Json

// ============================================================
// InitEngine — evaluates _init.zest.fsx at project root before build
// ============================================================
// _init.zest.fsx runs as a `dotnet fsi` subprocess. It can:
//   - addGlobal "key" value            → inject data into globalData
//   - addFilter "name" "spec"          → register a Nunjucks filter pipeline
//   - addGlobalFunction "name" value   → provide a global value to templates
//   - registerMigration "note"         → log a custom migration note
//   - loadJson "path"                  → parse JSON file to dictionary
//   - loadToml "path"                  → parse TOML file to dictionary
//   - loadEnv "KEY"                    → read environment variable
//   - console_log "message"            → debug print to stderr
//   - exec "command" "args"            → run shell command, get stdout
//
// Because F# functions can't cross the process boundary, `addFilter`
// takes a pipeline-spec string and `addGlobalFunction` takes a
// pre-evaluated value. The build pipeline registers these on the
// in-process template engine after the script exits.
// ============================================================

/// Result from running _init.zest.fsx
type InitResult = {
    /// Additional global data to merge into the build context.
    GlobalData: IDictionary<string, obj>
    /// Custom template filters declared via `addFilter name spec`.
    /// The spec is a Nunjucks filter-pipeline string (e.g. "upper | trim")
    /// applied to the filter's input value.
    Filters: IDictionary<string, string>
    /// Global template functions declared via `addGlobalFunction name value`.
    /// Stored as pre-evaluated values (functions can't cross the process
    /// boundary, so nullary value-providing is the supported form).
    GlobalFunctions: IDictionary<string, obj>
    /// Script had errors
    HasErrors: bool
    /// Error messages (if any)
    Errors: string list
}

module InitEngine =

    let private verboseRef = ref false

    let setVerbose (v: bool) = verboseRef := v

    /// Stringify any value (null-safe).
    let private toStr (v: obj) = if isNull v then "" else v.ToString()

    /// Find the _init.zest.fsx in the project root.
    let private findInitScript (rootDir: string) : string option =
        let path = Path.Combine(rootDir, "_init.zest.fsx")
        if File.Exists path then Some path
        else
            // Fallback to _init.fsx for backward compatibility
            let legacy = Path.Combine(rootDir, "_init.fsx")
            if File.Exists legacy then
                eprintfn "[Zest] _init.fsx is deprecated, rename to _init.zest.fsx"
                Some legacy
            else None

    /// Build the FSI preamble with environment setup and helper APIs.
    /// The preamble injects helper APIs and writes a JSON result file on exit.
    let private buildPreamble (resultFile: string) =
        let sb = StringBuilder()
        sb.AppendLine("open System") |> ignore
        sb.AppendLine("open System.IO") |> ignore
        sb.AppendLine("open System.Diagnostics") |> ignore
        sb.AppendLine("open System.Collections.Generic") |> ignore
        sb.AppendLine("open System.Text.Json") |> ignore
        // The mutable store for globals
        sb.AppendLine("let private __initGlobals = Dictionary<string, obj>()") |> ignore
        sb.AppendLine("let private __initErrors = System.Collections.Concurrent.ConcurrentBag<string>()") |> ignore
        // addGlobal — adds a key-value pair to global data
        sb.AppendLine("""let addGlobal (key: string) (value: obj) = __initGlobals.[key] <- value""") |> ignore
        // addFilter — register a custom template filter by pipeline spec.
        // Since F# functions can't cross the process boundary, the spec is a
        // Nunjucks filter-pipeline string (e.g. "upper | trim") that the
        // engine applies to the filter's input value.
        sb.AppendLine("""let addFilter (name: string) (spec: string) = __initGlobals.["__filter:" + name] <- box spec""") |> ignore
        // addGlobalFunction — provide a global value to templates. Stored
        // pre-evaluated; nullary value-providing is the supported form.
        sb.AppendLine("""let addGlobalFunction (name: string) (value: obj) = __initGlobals.["__gfn:" + name] <- value""") |> ignore
        // registerMigration — register a custom migration spec (callable
        // from `zest migrate`). Stored as a string note for logging.
        sb.AppendLine("""let registerMigration (note: string) = __initGlobals.["__migration:" + note] <- box note""") |> ignore
        // loadJson — load and parse a JSON file
        sb.AppendLine("""let loadJson (path: string) : obj =""") |> ignore
        sb.AppendLine("""    let text = File.ReadAllText(path)""") |> ignore
        sb.AppendLine("""    JsonSerializer.Deserialize<obj>(text)""") |> ignore
        // loadToml — load and parse a TOML file
        sb.AppendLine("""let loadToml (path: string) : IDictionary<string, obj> =""") |> ignore
        sb.AppendLine("""    let text = File.ReadAllText(path)""") |> ignore
        sb.AppendLine("""    let dict = Dictionary<string, obj>()""") |> ignore
        sb.AppendLine("""    for line in text.Split('\n') do""") |> ignore
        sb.AppendLine("""        let t = line.Trim()""") |> ignore
        sb.AppendLine("""        if not (t.StartsWith("#") || t.StartsWith("[") || String.IsNullOrWhiteSpace t) then""") |> ignore
        sb.AppendLine("""            let ci = t.IndexOf('=')""") |> ignore
        sb.AppendLine("""            if ci > 0 then""") |> ignore
        sb.AppendLine("""                let k = t.[..ci-1].Trim()""") |> ignore
        sb.AppendLine("""                let v = t.[ci+1..].Trim().Trim('"', '\'')""") |> ignore
        sb.AppendLine("""                dict.[k] <- box v""") |> ignore
        sb.AppendLine("""    dict :> IDictionary<string, obj>""") |> ignore
        // loadEnv — read environment variable
        sb.AppendLine("""let loadEnv (key: string) : string =""") |> ignore
        sb.AppendLine("""    match Environment.GetEnvironmentVariable(key) with""") |> ignore
        sb.AppendLine("""    | null -> "" | v -> v""") |> ignore
        // console_log — debug print
        sb.AppendLine("""let console_log (message: string) = eprintfn "[_init] %s" message""") |> ignore
        // exec — run a shell command
        sb.AppendLine("""let exec (command: string) (args: string) : string =""") |> ignore
        sb.AppendLine("""    use proc = new Process()""") |> ignore
        sb.AppendLine("""    proc.StartInfo.FileName <- command""") |> ignore
        sb.AppendLine("""    proc.StartInfo.Arguments <- args""") |> ignore
        sb.AppendLine("""    proc.StartInfo.UseShellExecute <- false""") |> ignore
        sb.AppendLine("""    proc.StartInfo.RedirectStandardOutput <- true""") |> ignore
        sb.AppendLine("""    proc.Start() |> ignore""") |> ignore
        sb.AppendLine("""    proc.StandardOutput.ReadToEnd().Trim()""") |> ignore
        // Deferred result serialization function
        sb.AppendLine("let private __writeResult () =") |> ignore
        sb.AppendLine("    let data = JsonSerializer.Serialize(dict (__initGlobals |> Seq.map (fun kv -> kv.Key, kv.Value.ToString())))") |> ignore
        sb.AppendLine("    File.WriteAllText(@\"" + resultFile.Replace("\\", "\\\\") + "\", data)") |> ignore
        sb.ToString()

    /// Empty InitResult (used when no script or on early failure).
    let private emptyResult : InitResult =
        { GlobalData = dict [] :> IDictionary<string, obj>
          Filters = dict [] :> IDictionary<string, string>
          GlobalFunctions = dict [] :> IDictionary<string, obj>
          HasErrors = false; Errors = [] }

    /// Split the raw globals dict (keyed with `__filter:` / `__gfn:` prefixes
    /// for filters and global functions) into the three InitResult buckets.
    let private splitResult (raw: IDictionary<string, obj>) (hasErrors: bool) (errors: string list) : InitResult =
        let globals = Dictionary<string, obj>()
        let filters = Dictionary<string, string>()
        let globalFns = Dictionary<string, obj>()
        for kv in raw do
            if kv.Key.StartsWith("__filter:") then
                filters.[kv.Key.Substring(9)] <- toStr kv.Value
            elif kv.Key.StartsWith("__gfn:") then
                globalFns.[kv.Key.Substring(6)] <- kv.Value
            elif kv.Key.StartsWith("__migration:") then
                ()  // migration notes are informational only
            else
                globals.[kv.Key] <- kv.Value
        { GlobalData = globals :> IDictionary<string, obj>
          Filters = filters :> IDictionary<string, string>
          GlobalFunctions = globalFns :> IDictionary<string, obj>
          HasErrors = hasErrors; Errors = errors }

    /// Run the _init.zest.fsx script (if present) and return the result.
    let run (rootDir: string) (globalData: IDictionary<string, obj>) : InitResult =
        match findInitScript rootDir with
        | None -> emptyResult
        | Some initPath ->
            try
                let tmpResult = Path.Combine(Path.GetTempPath(), sprintf "zest-init-%s.json" (Guid.NewGuid().ToString("N")))
                let tmpFsx = Path.Combine(Path.GetTempPath(), sprintf "zest-init-%s.fsx" (Guid.NewGuid().ToString("N")))

                try
                    // Build the full _init.zest.fsx with preamble + user script + result writer
                    let preamble = buildPreamble tmpResult
                    let userScript = File.ReadAllText(initPath)
                    File.WriteAllText(tmpFsx, preamble + "\n" + userScript + "\n__writeResult ()", Encoding.UTF8)

                    // Execute via dotnet fsi
                    let psi = ProcessStartInfo("dotnet", sprintf "fsi --quiet --nologo --exec \"%s\"" tmpFsx)
                    psi.UseShellExecute <- false
                    psi.RedirectStandardOutput <- true
                    psi.RedirectStandardError <- true
                    psi.StandardOutputEncoding <- Encoding.UTF8
                    psi.StandardErrorEncoding <- Encoding.UTF8
                    psi.CreateNoWindow <- true

                    use proc = Process.Start(psi)
                    let stdoutTask = proc.StandardOutput.ReadToEndAsync()
                    let stderrTask = proc.StandardError.ReadToEndAsync()

                    if not (proc.WaitForExit(60_000)) then
                        try proc.Kill() with _ -> ()
                        { emptyResult with HasErrors = true; Errors = ["_init.zest.fsx timed out (60s)"] }
                    else
                        let stderr = stderrTask.Result

                        if !verboseRef && not (String.IsNullOrEmpty stderr) then
                            Console.ForegroundColor <- ConsoleColor.DarkGray
                            Console.Error.WriteLine("[_init] ---- stderr ----")
                            Console.Error.WriteLine(stderr)
                            Console.Error.WriteLine("[_init] ---- end stderr ----")
                            Console.ResetColor()

                        let extraGlobals =
                            if File.Exists tmpResult then
                                try
                                    let text = File.ReadAllText(tmpResult)
                                    let parsed = JsonSerializer.Deserialize<IDictionary<string, JsonElement>>(text)
                                    let dict = Dictionary<string, obj>()
                                    for kv in parsed do
                                        match kv.Value.ValueKind with
                                        | JsonValueKind.String -> dict.[kv.Key] <- box (kv.Value.GetString())
                                        | JsonValueKind.Number -> dict.[kv.Key] <- box (kv.Value.GetDouble())
                                        | JsonValueKind.True  -> dict.[kv.Key] <- box true
                                        | JsonValueKind.False -> dict.[kv.Key] <- box false
                                        | JsonValueKind.Null -> ()
                                        | _ -> dict.[kv.Key] <- box (kv.Value.GetRawText())
                                    dict :> IDictionary<string, obj>
                                with ex ->
                                    dict [] :> IDictionary<string, obj>
                            else
                                dict [] :> IDictionary<string, obj>

                        if proc.ExitCode = 0 then
                            splitResult extraGlobals false []
                        else
                            let errLines =
                                stderr.Split('\n')
                                |> Array.filter (fun l -> not (String.IsNullOrWhiteSpace l))
                                |> Array.truncate 20
                                |> Array.toList
                            splitResult extraGlobals true errLines

                finally
                    if File.Exists tmpFsx then File.Delete tmpFsx
                    try if File.Exists tmpResult then File.Delete tmpResult with _ -> ()

            with ex ->
                { emptyResult with HasErrors = true; Errors = [sprintf "_init.zest.fsx failed: %s" ex.Message] }
