namespace Zest.Engine.Scripting

open System
open System.Diagnostics
open System.IO
open System.Text
open System.Threading
open System.Threading.Tasks
open System.Collections.Concurrent

/// Long-running `dotnet fsi` session shared across builds.
/// FSI cold start on Windows costs 3-10s per process; reusing one
/// interactive process removes that cost from every build after the first.
/// Scripts are executed via `#load` of a temp file; completion is signalled
/// by a marker line emitted through an interactive `printfn` command.
/// The session is best-effort: any failure (startup error, crash, hang)
/// returns None so callers can fall back to a one-shot `--exec` process.
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

    /// Execute a script file inside the shared session.
    ///   Some (Ok stdout)  - finished normally (marker consumed)
    ///   Some (Error msg)  - script reported an error (stderr captured)
    ///   None              - session unavailable / crashed / hung;
    ///                       callers should fall back to a one-shot process.
    let tryRunScript (scriptPath: string) : Result<string, string> option =
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
                    let rec loop () : Result<string, string> option =
                        if p.HasExited then None
                        else
                            let mutable line = null
                            if stdoutLines.TryDequeue (&line) then
                                if not (isNull line) && line.Contains marker then
                                    // FSI keeps going after a failed #load, so the
                                    // marker can still appear on errors - check stderr.
                                    let sbErr = StringBuilder ()
                                    let err = drain stderrLines sbErr
                                    if err.Length > 0 then
                                        kill ()
                                        Some (Error ("FSI: " + err.Trim ()))
                                    else
                                        Some (Ok (sbOut.ToString ()))
                                else
                                    sbOut.AppendLine line |> ignore
                                    loop ()
                            else
                                // A script error makes FSI print to stderr - report it.
                                let sbErr = StringBuilder ()
                                let err = drain stderrLines sbErr
                                if err.Length > 0 then
                                    kill ()
                                    Some (Error ("FSI: " + err.Trim ()))
                                elif sw.ElapsedMilliseconds > 60_000L then
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
