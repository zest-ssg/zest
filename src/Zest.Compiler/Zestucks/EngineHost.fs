namespace Zest.Compiler.Zealucks
open System
open System.Collections.Generic
open System.IO
open Zest.Compiler.Model

// ============================================================
// EngineHost — the shared Zealucks engine, wired for the build
// ============================================================
// Zest ships exactly one template language. This module owns the shared engine
// instance, the bare-name search directories behind {% include %} /
// {% extends %}, and the context shaper every call site needs. Nothing here is
// engine-agnostic: there is only Zealucks.
//
// Dependencies: Engine, Tokens, FileTypes
// ============================================================

/// <summary>
/// The Zealucks template engine as the build pipeline uses it.
/// </summary>
module EngineHost =

    /// Directories searched by the Zealucks file loader after the working
    /// directory, most importantly the project's includes directory. The
    /// include/extends tags resolve bare names ("head") against these paths so
    /// templates do not need relative paths that leak the on-disk layout.
    ///
    /// Insertion order is the resolution order, so it must stay deterministic
    /// and registration must happen from one thread before the first render.
    let private searchDirs = ResizeArray<string>()
    let private searchDirGate = obj()
    let private registeredDirs = HashSet<string>(StringComparer.OrdinalIgnoreCase)

    /// Register a directory for bare-name template resolution. Directories are
    /// searched in registration order; re-registering one is a no-op. Missing
    /// directories are ignored.
    let addSearchDir (dir: string) =
        if not (String.IsNullOrWhiteSpace dir) && Directory.Exists dir then
            let canonical = Path.GetFullPath dir
            lock searchDirGate (fun () ->
                if registeredDirs.Add canonical then
                    searchDirs.Add canonical)

    /// Drop every registered search directory. Called between sites/builds.
    let clearSearchDirs () =
        lock searchDirGate (fun () ->
            searchDirs.Clear()
            registeredDirs.Clear())

    /// Candidate paths for one include/extends reference, most specific first.
    /// The reference as written always wins, so an explicit path never changes
    /// meaning; an extension-less reference then gets each Zealucks file
    /// extension appended, which is what lets a bare "base" resolve to either
    /// `base.zlk` or `base.njk`.
    let private candidatePaths (path: string) : string seq =
        let fileName = Path.GetFileName path
        let hasKnownExt =
            FileTypes.ZealucksFileExtensions
            |> List.exists (fun e -> fileName.EndsWith(e, StringComparison.OrdinalIgnoreCase))
        seq {
            // As written.
            yield path
            // Same file with each recognised extension, when none was given.
            if not hasKnownExt then
                for ext in FileTypes.ZealucksFileExtensions do
                    yield path + ext
            // Same rules, resolved against every registered search directory.
            for dir in searchDirs do
                yield Path.Combine(dir, fileName)
                if not hasKnownExt then
                    for ext in FileTypes.ZealucksFileExtensions do
                        yield Path.Combine(dir, fileName + ext)
        }

    /// The shared Zealucks engine, wired to the search directories above.
    /// Construction is deferred because build setup registers those
    /// directories after this module is loaded.
    let private sharedEngine =
        lazy
            let engine = Engine()
            engine.SetLoadFile(fun path ->
                match candidatePaths path |> Seq.tryFind File.Exists with
                | Some f ->
                    try Ok(File.ReadAllText f)
                    with ex -> Error ex.Message
                | None -> Error(sprintf "Template not found: %s" path))
            engine

    /// <summary>Get the shared Zealucks engine.</summary>
    /// <returns>The process-wide engine instance. Callers must not dispose it.</returns>
    let instance : Engine = sharedEngine.Value

    /// Render template source against a flat or nested context bag.
    let render (templateText: string) (variables: IDictionary<string, obj>) =
        instance.Render templateText variables

    /// Clear every engine-owned cache (parsed templates, tokens, file cache).
    let clearCaches () = instance.ClearCache()

    /// <summary>
    /// Convert flat key-value pairs (e.g. "site.title" → "Zest SSG")
    /// into a nested dictionary for engine resolution.
    /// "site.title" becomes { "site": { "title": "Zest SSG" } },
    /// while "content" stays as { "content": "..." }.
    /// </summary>
    let buildContext (pairs: (string * obj) seq) : IDictionary<string, obj> =
        let root = Dictionary<string, obj>()
        for key, value in pairs do
            let parts = key.Split('.')
            if parts.Length = 1 then
                root.[key] <- value
            else
                let mutable current = root :> IDictionary<string, obj>
                for i in 0..parts.Length - 2 do
                    let part = parts.[i]
                    match current.TryGetValue part with
                    | true, (:? IDictionary<string, obj> as sub) ->
                        current <- sub
                    | _ ->
                        let sub = Dictionary<string, obj>()
                        current.[part] <- box sub
                        current <- sub
                current.[parts.[parts.Length - 1]] <- value
        root :> IDictionary<string, obj>
