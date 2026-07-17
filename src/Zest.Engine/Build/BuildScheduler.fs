namespace Zest.Engine.Build

open System
open System.Collections.Concurrent
open System.Threading.Tasks

// ============================================================
// BuildScheduler — Parallel page rendering with backpressure
// ============================================================
// Groups pages into batches and renders them in parallel with a
// configurable degree of parallelism (default: processor count).
// Preserves FSI batch-evaluation semantics while allowing the
// surrounding render/write work to scale across cores.
//
// Dependency: none (pure scheduling utilities).
// ============================================================

module BuildScheduler =

    /// The default parallelism: the number of logical processors.
    let defaultParallelism () = Environment.ProcessorCount

    /// Run a work function over every item in parallel, bounded by
    /// `parallelism`. Exceptions per-item are captured into the returned
    /// list rather than aborting the whole batch.
    let mapParallel (parallelism: int) (fn: 'a -> 'b) (items: 'a seq) : ('a * Result<'b, exn>) list =
        if parallelism <= 1 then
            items
            |> Seq.map (fun item ->
                try item, Ok (fn item)
                with e -> item, Error e)
            |> Seq.toList
        else
            let results = ConcurrentBag<'a * Result<'b, exn>>()
            let dopt = max 1 parallelism
            Parallel.ForEach(items, ParallelOptions(MaxDegreeOfParallelism = dopt), fun item ->
                try
                    results.Add(item, Ok (fn item))
                with e ->
                    results.Add(item, Error e)) |> ignore
            Seq.toList results

    /// Split a sequence of items into `batchCount` roughly-equal groups,
    /// so each group can be handed to a worker (useful when FSI scripts
    /// must be batch-evaluated together within a group).
    let partition (batchCount: int) (items: 'a seq) : 'a list list =
        let arr = Array.ofSeq items
        if arr.Length = 0 then []
        elif batchCount <= 1 then [Array.toList arr]
        else
            let n = arr.Length
            let per = (n + batchCount - 1) / batchCount |> max 1
            [ for i in 0 .. per .. n - 1 ->
                arr.[i .. min (i + per - 1) (n - 1)] |> Array.toList ]

    /// Schedule page rendering across `parallelism` workers. `render` is
    /// called once per page; results and errors are collected. Returns
    /// (successes, failures).
    let renderPages (parallelism: int)
                    (render: 'page -> string)
                    (pages: 'page seq)
                    : ('page * string) list * ('page * exn) list =
        let outcomes = mapParallel parallelism render pages
        let successes = outcomes |> List.choose (function (p, Ok h) -> Some(p, h) | _ -> None)
        let failures = outcomes |> List.choose (function (p, Error e) -> Some(p, e) | _ -> None)
        successes, failures
