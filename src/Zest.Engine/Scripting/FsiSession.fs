// FsiSession.fs
//
// Reuses one long-running `dotnet fsi` process across builds to avoid the
// 3-10s cold-start cost of spawning a new interpreter per script. Scripts are
// executed via `#load` of a temp file; a marker line emitted through an
// interactive `printfn` command signals completion.
//
// The session is resilient: script errors never destroy it — FSI keeps running
// after a failed #load — so one bad script cannot force every subsequent
// evaluation to cold-start a fresh interpreter. Only a crash or a 60s hang
// invalidates the session. Callers receive both stdout and stderr so they can
// classify warnings versus genuine failures themselves.
//
// Dependencies: System.Diagnostics, System.IO

namespace Zest.Engine.Scripting

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Threading
open System.Threading.Tasks
open System.Collections.Concurrent

/// Long-running `dotnet fsi` session shared across builds.
module FsiSession =

    let private sync = obj ()
    let mutable private proc : Process option = None
    let mutable private stdinWriter : StreamWriter option = None
    let mutable private stdoutLines = ConcurrentQueue<string> ()
    let mutable private stderrLines = ConcurrentQueue<string> ()

    let private kill () =
        match proc with
        | Some p ->
            try
                if not p.HasExited then p.Kill (entireProcessTree = true)
            with _ -> ()
            try p.Dispose () with _ -> ()
        | None -> ()
        proc <- None
        stdinWriter <- None

    /// Best-effort session shutdown (also wired to ProcessExit).
    let shutdown () =
        lock sync (fun () -> kill ())

    let private startSession () : bool =
        try
            let psi = ProcessStartInfo "dotnet"
            psi.ArgumentList.Add "fsi"
            psi.ArgumentList.Add "--quiet"
            psi.ArgumentList.Add "--nologo"
            psi.ArgumentList.Add "--readline-"
            psi.UseShellExecute <- false
            psi.RedirectStandardInput <- true
            psi.RedirectStandardOutput <- true
            psi.RedirectStandardError <- true
            psi.CreateNoWindow <- true
            psi.StandardOutputEncoding <- Encoding.UTF8
            psi.StandardErrorEncoding <- Encoding.UTF8
            let p = Process.Start psi
            stdoutLines <- ConcurrentQueue<string> ()
            stderrLines <- ConcurrentQueue<string> ()
            proc <- Some p
            stdinWriter <- Some p.StandardInput

            let startReader (getLine: unit -> string) (q: ConcurrentQueue<string>) =
                Task.Run (fun () ->
                    try
                        let mutable line = getLine ()
                        while not (isNull line) do
                            q.Enqueue line
                            line <- getLine ()
                    with _ -> ())
                |> ignore

            startReader (fun () -> p.StandardOutput.ReadLine ()) stdoutLines
            startReader (fun () -> p.StandardError.ReadLine ()) stderrLines
            true
        with _ -> false

    let private drain (q: ConcurrentQueue<string>) (sb: StringBuilder) : string =
        let mutable line = null
        while q.TryDequeue (&line) do
            sb.AppendLine line |> ignore
        sb.ToString ()

    /// True when FSI diagnostics indicate a real failure (compiler errors or
    /// runtime exceptions) rather than benign debug output (e.g. console_log).
    let hasErrors (stderr: string) =
        stderr.Split('\n')
        |> Array.exists (fun line ->
            let t = line.Trim()
            t.Contains("error FS") || t.Contains("error:")
            || t.StartsWith("stdin(", StringComparison.Ordinal)
            || t.Contains("Unhandled exception"))

    /// Compact, readable error message extracted from FSI stderr. Warning and
    /// info lines are stripped; a fallback message keeps the diagnostic
    /// non-empty when stderr carries only noise.
    let formatError (stderr: string) =
        let errLines =
            stderr.Split('\n')
            |> Array.filter (fun l ->
                not (String.IsNullOrWhiteSpace l)
                && not (l.Contains("warning FS"))
                && not (l.Contains("info :")))
            |> Array.truncate 30
        if errLines.Length = 0 then
            "FSI reported a failure but produced no readable diagnostics."
        else
            errLines
            |> Array.mapi (fun i line ->
                if line.Contains("error FS") || line.Contains("error:") then
                    sprintf "  ▶ %s" (line.Trim())
                else
                    sprintf "    %s" (line.Trim()))
            |> String.concat "\n"

    /// Execute a script file inside the shared session.
    ///   Some (stdout, stderr) - finished normally; stderr may carry warnings
    ///                           or error diagnostics for the caller to classify
    ///   None                  - session unavailable / crashed / hung;
    ///                           callers should fall back to a one-shot process
    ///
    /// The session is deliberately NOT killed on script errors: FSI stays
    /// interactive after a failed #load, so the next evaluation can reuse it.
    /// Only a timeout (60s) or a process crash tears the session down.
    let tryRunScript (scriptPath: string) : (string * string) option =
        lock sync (fun () ->
            try
                let pOpt =
                    match proc with
                    | Some p when not p.HasExited -> Some p
                    | _ -> if startSession () then proc else None
                match pOpt with
                | None -> None
                | Some p ->
                    // Drop leftovers from a previous evaluation.
                    drain stdoutLines (StringBuilder ()) |> ignore
                    drain stderrLines (StringBuilder ()) |> ignore

                    let marker = "___ZEST_DONE_" + (Guid.NewGuid ()).ToString ("N") + "___"
                    let w = stdinWriter.Value
                    // Interactive FSI requires `;;` to terminate each command.
                    w.WriteLine (sprintf "#load @\"%s\";;" scriptPath)
                    w.WriteLine (sprintf "printfn \"%s\";;" marker)
                    w.Flush ()

                    let sbOut = StringBuilder ()
                    let sw = Stopwatch.StartNew ()
                    let rec loop () : (string * string) option =
                        if p.HasExited then None
                        else
                            let mutable line = null
                            if stdoutLines.TryDequeue (&line) then
                                if not (isNull line) && line.Contains marker then
                                    // A script may end without a trailing newline
                                    // (e.g. `printf`-based render), so the marker
                                    // lands on the same line as the final output.
                                    // Append everything before the marker so that
                                    // line is not silently dropped.
                                    let markerIdx = line.IndexOf marker
                                    if markerIdx > 0 then
                                        sbOut.AppendLine (line.Substring(0, markerIdx)) |> ignore
                                    // Error diagnostics can trail the completion
                                    // marker; wait for stderr to go quiet before
                                    // capturing so the error is not lost.
                                    let mutable lastCount = stderrLines.Count
                                    let mutable idleCount = 0
                                    let mutable settled = false
                                    while not settled && not p.HasExited do
                                        Thread.Sleep 25
                                        let count = stderrLines.Count
                                        if count = lastCount then
                                            idleCount <- idleCount + 1
                                            if idleCount >= 2 then settled <- true
                                        else
                                            idleCount <- 0
                                            lastCount <- count
                                    let sbErr = StringBuilder ()
                                    let err = drain stderrLines sbErr
                                    Some (sbOut.ToString (), err)
                                else
                                    sbOut.AppendLine line |> ignore
                                    loop ()
                            else
                                if sw.ElapsedMilliseconds > 60_000L then
                                    kill ()
                                    None
                                else
                                    Thread.Sleep 10
                                    loop ()
                    loop ()
            with _ ->
                kill ()
                None)

    do AppDomain.CurrentDomain.ProcessExit.Add (fun _ -> kill ())
