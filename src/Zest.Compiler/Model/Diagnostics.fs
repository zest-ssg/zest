// Diagnostics.fs
//
// The build's non-fatal problem channel.
//
// Generators run deep inside Parallel.ForEach loops where returning a Result
// would mean threading a collector through a dozen signatures. They report here
// instead, and BuildRunner drains the sink at the end of the build and folds
// the entries into BuildResult.
//
// This exists because the previous behaviour — eprintfn and carry on — meant a
// page that failed to render still produced a green build with a zero exit
// code, so CI and the author both believed the site was complete.
//
// Dependencies: System.Collections.Concurrent

namespace Zest.Compiler.Model
open System.Collections.Concurrent

/// How serious a reported diagnostic is.
[<RequireQualifiedAccess>]
type DiagnosticSeverity =
    /// Worth telling the author about; does not fail the build.
    | Warning
    /// Fails the build.
    | Error

/// One reported problem.
type Diagnostic = {
    Severity: DiagnosticSeverity
    Message: string
}

/// Process-wide, thread-safe sink for build diagnostics.
module Diagnostics =

    let private sink = ConcurrentQueue<Diagnostic>()

    let private enqueue (severity: DiagnosticSeverity) (message: string) =
        sink.Enqueue({ Severity = severity; Message = message })

    /// Report a non-fatal problem. Takes a printf-style format string.
    let warn format =
        Printf.ksprintf (enqueue DiagnosticSeverity.Warning) format

    /// Report a problem that must fail the build.
    let error format =
        Printf.ksprintf (enqueue DiagnosticSeverity.Error) format

    /// Remove and return everything reported since the last drain, in the
    /// order it was reported.
    let drain () : Diagnostic list =
        let acc = ResizeArray<Diagnostic>()
        let mutable item = Unchecked.defaultof<Diagnostic>
        while sink.TryDequeue(&item) do
            acc.Add item
        List.ofSeq acc

    /// Discard everything reported so far. Called at the start of a build so a
    /// failed build cannot leak messages into the next one.
    let clear () =
        let mutable item = Unchecked.defaultof<Diagnostic>
        while sink.TryDequeue(&item) do
            ()
