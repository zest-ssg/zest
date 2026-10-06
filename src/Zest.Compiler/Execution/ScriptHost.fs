namespace Zest.Compiler.Execution
open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Text
open System.Text.Json

// ============================================================
// ScriptHost — shared plumbing for Zest's project-root hook scripts
// ============================================================
// Both hooks (_prebuild.fsx and _finalize.fsx) are assembled the same way:
// a generated preamble that injects helper bindings, followed by the user's
// script, executed through the shared FSI session with a one-shot
// `dotnet fsi` fallback. This module owns that machinery so the two hooks
// cannot drift apart in logging, timeout, error text or process lifetime.
//
// Success is decided by a completion marker file, not by scanning stderr.
// Interactive FSI reports a top-level exception (e.g. `failwith`) without any
// of the "error FS" / "error:" markers that diagnostic parsing looks for, so
// stderr alone would silently accept a script that died halfway through. A
// marker file written as the script's final statement is unambiguous.
// ============================================================

/// Outcome of running one generated hook script.
type ScriptRun = {
    /// True when the script ran to its final statement.
    Succeeded: bool
    /// `<script name> failed: <detail>` when <see cref="Succeeded"/> is false.
    ErrorText: string
}

module ScriptHost =

    /// Longest a one-shot fallback process may run before it is killed.
    let private timeoutMs = 60_000

    /// Number of stderr lines kept in a fallback error message.
    let private maxErrorLines = 20

    /// Render a string as a valid F# string literal, for generated source.
    let fsharpLiteral (value: string) : string =
        let sb = StringBuilder(value.Length + 2)
        sb.Append('"') |> ignore
        for ch in value do
            match ch with
            | '\\' -> sb.Append("\\\\") |> ignore
            | '"'  -> sb.Append("\\\"") |> ignore
            | '\n' -> sb.Append("\\n") |> ignore
            | '\r' -> sb.Append("\\r") |> ignore
            | '\t' -> sb.Append("\\t") |> ignore
            | c when c < ' ' -> sb.Append("\\u").Append(int c |> sprintf "%04x") |> ignore
            | c -> sb.Append(c) |> ignore
        sb.Append('"') |> ignore
        sb.ToString()

    /// The mutable state dictionary every generated script writes into.
    /// Hook preambles append this line before their own bindings.
    let stateDeclaration = "let private __scriptState = Dictionary<string, obj>()"

    /// Serialize `__scriptState` to the given result file. Appended to a
    /// preamble so a hook can hand structured results back to the build.
    let resultWriterSource (resultFile: string) : string =
        sprintf
            "let private __writeResult () =\n    File.WriteAllText(@%s, JsonSerializer.Serialize(__scriptState))"
            (fsharpLiteral resultFile)

    /// Declare `__writeCompleted`, the completion marker writer. A hook appends
    /// `__writeCompleted ()` as the very last statement of its generated
    /// script; if that file is missing afterwards, the script aborted.
    let completionWriterSource (completionFile: string) : string =
        sprintf "let private __writeCompleted () = File.WriteAllText(@%s, \"ok\")"
            (fsharpLiteral completionFile)

    /// The call a hook appends after the user's script.
    let completionCall = "__writeCompleted ()"

    /// Temp file name for a generated script, tagged with the hook name so
    /// a stray file is identifiable in the temp directory.
    let private tempScriptPath (label: string) =
        let clean = Path.GetFileNameWithoutExtension label
        Path.Combine(Path.GetTempPath(), sprintf "zest-%s-%s.fsx" clean (Guid.NewGuid().ToString("N")))

    /// Build the failure text for a run that did not reach its last statement.
    let private describeFailure (label: string) (stderr: string) : string =
        let detail =
            if FsiSession.hasErrors stderr then
                FsiSession.formatError stderr
            else
                let trimmed = stderr.Trim()
                if trimmed = "" then
                    "the script did not run to completion."
                else
                    trimmed.Split('\n')
                    |> Array.map (fun l -> l.Trim())
                    |> Array.filter (fun l -> l <> "")
                    |> Array.truncate maxErrorLines
                    |> String.concat "\n"
        sprintf "%s failed: %s" label detail

    /// One-shot `dotnet fsi --exec` fallback, used when the shared session
    /// could not be started.
    let private runOneShot (label: string) (completionFile: string) (scriptPath: string) : ScriptRun =
        let psi = ProcessStartInfo("dotnet", sprintf "fsi --quiet --nologo --exec \"%s\"" scriptPath)
        psi.UseShellExecute <- false
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.StandardOutputEncoding <- Encoding.UTF8
        psi.StandardErrorEncoding <- Encoding.UTF8
        psi.CreateNoWindow <- true

        use proc = Process.Start(psi)
        // Read both pipes concurrently: reading one to completion first can
        // deadlock when the other fills its buffer.
        let stdoutTask = proc.StandardOutput.ReadToEndAsync()
        let stderrTask = proc.StandardError.ReadToEndAsync()

        if not (proc.WaitForExit(timeoutMs)) then
            try proc.Kill() with _ -> ()
            { Succeeded = false; ErrorText = sprintf "%s timed out (%ds)" label (timeoutMs / 1000) }
        else
            let stderr = stderrTask.Result
            let stdout = stdoutTask.Result
            // The fallback runs out of process, so its output has to be
            // forwarded for a failed hook to be diagnosable.
            if not (String.IsNullOrWhiteSpace stdout) then eprintfn "%s" (stdout.TrimEnd())
            if not (String.IsNullOrWhiteSpace stderr) then eprintfn "%s" (stderr.TrimEnd())

            if File.Exists completionFile && proc.ExitCode = 0 then
                { Succeeded = true; ErrorText = "" }
            else
                { Succeeded = false; ErrorText = describeFailure label stderr }

    /// Forward a hook's own stderr even when it succeeded.
    ///
    /// The shared FSI session captures the script's streams instead of letting
    /// them reach the terminal, so without this a hook's `console_log` report —
    /// the documented way to report from a hook — would never be seen. FSI's
    /// own warnings are dropped; they are not the hook's message.
    let private forwardDiagnostics (stderr: string) =
        for line in stderr.Split('\n') do
            let trimmed = line.TrimEnd()
            if trimmed.Length > 0 && not (trimmed.Contains "warning FS") then
                eprintfn "%s" trimmed

    /// Write `source` to a temp file and run it.
    ///
    /// Prefers the long-running <see cref="FsiSession"/> (no cold start per
    /// build) and falls back to a one-shot `dotnet fsi --exec` process.
    /// <paramref name="completionFile"/> is written by the script's final
    /// statement, so its presence is what proves the script finished.
    let runSource (label: string) (completionFile: string) (source: string) : ScriptRun =
        // A marker left over from an earlier run must never read as success.
        try if File.Exists completionFile then File.Delete completionFile with _ -> ()

        let tmpFsx = tempScriptPath label
        try
            try
                File.WriteAllText(tmpFsx, source, Encoding.UTF8)

                match FsiSession.tryRunScript tmpFsx with
                | Some (_, stderr) ->
                    if File.Exists completionFile then
                        forwardDiagnostics stderr
                        { Succeeded = true; ErrorText = "" }
                    else
                        { Succeeded = false; ErrorText = describeFailure label stderr }
                | None ->
                    runOneShot label completionFile tmpFsx
            with ex ->
                { Succeeded = false; ErrorText = sprintf "%s failed: %s" label ex.Message }
        finally
            try if File.Exists tmpFsx then File.Delete tmpFsx with _ -> ()

    // ── Result-file plumbing (host side) ───────────────────────

    /// Convert a JSON element into native .NET objects (Dictionary / array /
    /// primitives) so hook results can be consumed without JsonElement leaking
    /// into strongly typed data.
    let rec jsonToObj (el: JsonElement) : obj =
        match el.ValueKind with
        | JsonValueKind.Object ->
            let d = Dictionary<string, obj>()
            for p in el.EnumerateObject() do d.[p.Name] <- jsonToObj p.Value
            box d
        | JsonValueKind.Array ->
            el.EnumerateArray() |> Seq.map jsonToObj |> Seq.toArray |> box
        | JsonValueKind.String -> box (el.GetString())
        | JsonValueKind.Number ->
            match el.TryGetInt64() with
            | true, l -> box l
            | _ -> box (el.GetDouble())
        | JsonValueKind.True -> box true
        | JsonValueKind.False -> box false
        | _ -> null

    /// Read a hook's JSON result file. Returns an empty dictionary when the
    /// file is missing (the hook failed before writing it) or unparsable.
    let readResultFile (resultFile: string) : IDictionary<string, obj> =
        if File.Exists resultFile then
            try
                let parsed = JsonSerializer.Deserialize<IDictionary<string, JsonElement>>(File.ReadAllText resultFile)
                let dict = Dictionary<string, obj>()
                for kv in parsed do
                    match jsonToObj kv.Value with
                    | null -> ()
                    | v -> dict.[kv.Key] <- v
                dict :> IDictionary<string, obj>
            with _ ->
                dict [] :> IDictionary<string, obj>
        else
            dict [] :> IDictionary<string, obj>

    /// Path for a temporary marker file, tagged so it is identifiable.
    let tempMarkerPath (kind: string) : string =
        Path.Combine(Path.GetTempPath(), sprintf "zest-%s-%s.txt" kind (Guid.NewGuid().ToString("N")))

    // ── Shared hook API ────────────────────────────────────────

    /// Helper bindings that mean exactly the same thing in both hooks.
    ///
    /// Generated from one place on purpose: `_prebuild.fsx` and `_finalize.fsx`
    /// are two files an author reads side by side, and two hand-kept copies of
    /// `exec` / `loadEnv` had already drifted into different signatures under
    /// the same name. Anything hook-specific stays in that hook's own preamble.
    ///
    /// The generated declarations rely on the caller's preamble declaring
    /// `__projectRoot` first.
    let commonHelpersSource (logTag: string) : string =
        let sb = StringBuilder()

        // ExecResult — one contract serves both hooks: a pre-build script reads
        // `stdout` to inject data, a post-build script reads `code` to react to
        // a tool that failed.
        sb.AppendLine("type ExecResult = { code: int; stdout: string; stderr: string }") |> ignore

        // loadJson — parse a JSON file into dictionaries, arrays and scalars.
        // JsonElement values are unusable from F# without walking them by hand.
        sb.AppendLine("let rec private __jsonNative (el: JsonElement) : obj =") |> ignore
        sb.AppendLine("    match el.ValueKind with") |> ignore
        sb.AppendLine("    | JsonValueKind.Object -> el.EnumerateObject() |> Seq.map (fun p -> p.Name, __jsonNative p.Value) |> dict |> box") |> ignore
        sb.AppendLine("    | JsonValueKind.Array -> el.EnumerateArray() |> Seq.map __jsonNative |> Seq.toArray |> box") |> ignore
        sb.AppendLine("    | JsonValueKind.String -> box (el.GetString())") |> ignore
        sb.AppendLine("    | JsonValueKind.Number -> match el.TryGetInt64() with | true, l -> box l | _ -> box (el.GetDouble())") |> ignore
        sb.AppendLine("    | JsonValueKind.True -> box true") |> ignore
        sb.AppendLine("    | JsonValueKind.False -> box false") |> ignore
        sb.AppendLine("    | _ -> null") |> ignore
        sb.AppendLine("let loadJson (path: string) : obj =") |> ignore
        sb.AppendLine("    let full = if Path.IsPathRooted path then path else Path.Combine(__projectRoot, path)") |> ignore
        sb.AppendLine("    __jsonNative (JsonSerializer.Deserialize<JsonElement>(File.ReadAllText full))") |> ignore

        // loadToml — read a flat TOML table (sections and simple key = value).
        sb.AppendLine("let loadToml (path: string) : IDictionary<string, obj> =") |> ignore
        sb.AppendLine("    let full = if Path.IsPathRooted path then path else Path.Combine(__projectRoot, path)") |> ignore
        sb.AppendLine("    let dict = Dictionary<string, obj>()") |> ignore
        sb.AppendLine("    for line in File.ReadAllLines full do") |> ignore
        sb.AppendLine("        let t = line.Trim()") |> ignore
        sb.AppendLine("        if not (t.StartsWith(\"#\") || t.StartsWith(\"[\") || String.IsNullOrWhiteSpace t) then") |> ignore
        sb.AppendLine("            let ci = t.IndexOf('=')") |> ignore
        sb.AppendLine("            if ci > 0 then") |> ignore
        sb.AppendLine("                let k = t.[..ci-1].Trim()") |> ignore
        sb.AppendLine("                let v = t.[ci+1..].Trim().Trim('\"', '\\'')") |> ignore
        sb.AppendLine("                dict.[k] <- box v") |> ignore
        sb.AppendLine("    dict :> IDictionary<string, obj>") |> ignore

        // loadEnv — read an environment variable. Option, not "" for absent:
        // "unset" and "set to empty" are different answers.
        sb.AppendLine("let loadEnv (key: string) : string option =") |> ignore
        sb.AppendLine("    match Environment.GetEnvironmentVariable(key) with") |> ignore
        sb.AppendLine("    | null -> None") |> ignore
        sb.AppendLine("    | v when v = \"\" -> None") |> ignore
        sb.AppendLine("    | v -> Some v") |> ignore

        // console_log — the hook's reporting channel, tagged for the hook name.
        sb.AppendLine("let console_log (message: string) = eprintfn \"[" + logTag + "] %s\" message") |> ignore

        // exec — run an external command. Output is captured, never echoed:
        // the caller decides what to print, so a hook reported in the build log
        // always looks deliberate.
        sb.AppendLine("let exec (command: string) (args: string list) : ExecResult =") |> ignore
        sb.AppendLine("    use proc = new Process()") |> ignore
        sb.AppendLine("    let quote (a: string) = if a.Contains ' ' then \"\\\"\" + a + \"\\\"\" else a") |> ignore
        sb.AppendLine("    proc.StartInfo.FileName <- command") |> ignore
        sb.AppendLine("    proc.StartInfo.Arguments <- args |> List.map quote |> String.concat \" \"") |> ignore
        sb.AppendLine("    proc.StartInfo.UseShellExecute <- false") |> ignore
        sb.AppendLine("    proc.StartInfo.RedirectStandardOutput <- true") |> ignore
        sb.AppendLine("    proc.StartInfo.RedirectStandardError <- true") |> ignore
        sb.AppendLine("    proc.Start()") |> ignore
        sb.AppendLine("    let outTask = proc.StandardOutput.ReadToEndAsync()") |> ignore
        sb.AppendLine("    let errTask = proc.StandardError.ReadToEndAsync()") |> ignore
        sb.AppendLine("    proc.WaitForExit()") |> ignore
        sb.AppendLine("    { code = proc.ExitCode; stdout = outTask.Result; stderr = errTask.Result }") |> ignore

        sb.ToString()
