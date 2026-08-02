namespace Zest.Engine.Build

open System
open System.Threading

// ============================================================
// BuildProgress — Thread-safe real-time build progress reporting
// ============================================================
// Provides a lightweight progress sink that BuildEngine and
// ContentPipeline update during a build. The C# BuildAnimator
// polls these counters to render a live terminal animation.
//
// All counters use Interlocked for lock-free thread safety,
// since Parallel.ForEach updates them from multiple threads.
//
// Dependency: none (pure counter module).
// ============================================================

/// Build phase labels shown to the user.
type BuildPhase =
    | Initializing = 0
    | Discovering  = 1
    | Evaluating   = 2
    | Writing      = 3
    | Assets       = 4
    | Finalizing   = 5

/// Thread-safe progress tracker for a single build run.
type BuildProgress() =
    let mutable phase: int = 0  // BuildPhase.Initializing
    let mutable totalFiles: int = 0
    let mutable processed: int = 0
    let mutable cached: int = 0
    let mutable errors: int = 0
    let mutable assetsCopied: int = 0
    let mutable startedAt: DateTime = DateTime.UtcNow
    let mutable outputDir: string = ""

    /// Current build phase.
    member _.Phase with get () : BuildPhase = Volatile.Read(&phase) |> int |> enum<BuildPhase> and set (v: BuildPhase) = Volatile.Write(&phase, int v)
    /// Total content files discovered.
    member _.TotalFiles with get () = Volatile.Read(&totalFiles) and set (v) = Volatile.Write(&totalFiles, v)
    /// Files processed (rendered + written) so far.
    member _.Processed with get () = Volatile.Read(&processed) and set (v) = Volatile.Write(&processed, v)
    /// Files served from cache (incremental builds).
    member _.Cached with get () = Volatile.Read(&cached) and set (v) = Volatile.Write(&cached, v)
    /// Build errors encountered.
    member _.Errors with get () = Volatile.Read(&errors) and set (v) = Volatile.Write(&errors, v)
    /// Asset files copied.
    member _.AssetsCopied with get () = Volatile.Read(&assetsCopied) and set (v) = Volatile.Write(&assetsCopied, v)
    /// Build start time (UTC).
    member _.StartedAt with get () = startedAt and set (v) = startedAt <- v
    /// Output directory path (for summary).
    member _.OutputDir with get () = outputDir and set (v) = outputDir <- v

    /// Atomically increment processed count.
    member this.IncProcessed() = Interlocked.Increment(&processed) |> ignore
    /// Atomically increment cached count.
    member this.IncCached() = Interlocked.Increment(&cached) |> ignore
    /// Atomically increment error count.
    member this.IncErrors() = Interlocked.Increment(&errors) |> ignore
    /// Atomically increment assets count.
    member this.IncAssets() = Interlocked.Increment(&assetsCopied) |> ignore

/// Module-level singleton — set at the start of each build, cleared after.
module ProgressTracker =

    /// Active progress instance for the current build (None if no build running).
    let mutable current: BuildProgress option = None

    /// Start tracking a new build. Returns the progress instance.
    let start () =
        let p = BuildProgress(StartedAt = DateTime.UtcNow)
        current <- Some p
        p

    /// Clear the active progress instance (build complete).
    let clear () = current <- None

    /// Get the active progress instance, if any.
    let tryGet () = current
