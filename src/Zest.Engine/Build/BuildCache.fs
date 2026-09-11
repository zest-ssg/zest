namespace Zest.Engine

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open System.Security.Cryptography

/// Incremental build cache: tracks source file mtime, content hash, and a
/// dependency graph so layout/include changes trigger rebuilds of only the
/// affected pages.
///
/// Cache files (written to the output directory):
///   .zest-cache.log — per-page mtime + content hash
///   .zest-deps.log  — dependency graph (layout/include → dependent pages)
///
/// The first line of each file is a header comment carrying a cache-format
/// version and an "engine signature" (Zest.Engine.dll mtime + size). If the
/// engine DLL changes (upgrade, recompile), the signature mismatches on load
/// and the entire cache is ignored — forcing a full rebuild. This prevents
/// stale pages built by a previous engine version from being served.
module BuildCache =

    // ── Cache format ──
    let private CACHE_FORMAT_VERSION = 3
    let private cacheFilePath (outputDir: string) = Path.Combine(outputDir, ".zest-cache.log")
    let private depsFilePath  (outputDir: string) = Path.Combine(outputDir, ".zest-deps.log")

    /// Extract a `<key>=<value>` token from a cache header line. Header
    /// tokens are separated by `" | "` and every value is space-free, so
    /// splitting on spaces and matching the `key=` prefix is unambiguous —
    /// even though the engine signature itself contains an inner `|`.
    let private headerToken (header: string) (key: string) : string option =
        if String.IsNullOrEmpty header then None
        else
            header.Split(' ')
            |> Array.tryPick (fun token ->
                let marker = key + "="
                if token.StartsWith(marker, StringComparison.Ordinal) then
                    Some (token.Substring(marker.Length))
                else None)

    /// Compute a stable content signature over every layout and include so
    /// any template change — editing, adding, or deleting a file — produces a
    /// different signature and invalidates the page cache. Keys are sorted so
    /// the signature is deterministic regardless of Map/dictionary order.
    let internal computeTemplateSignature
        (layouts: Map<string, string * string>)
        (includes: IDictionary<string, string>) : string =
        use sha = SHA256.Create()
        let sb = System.Text.StringBuilder()
        for (name, (_, text)) in layouts |> Map.toSeq |> Seq.sortBy fst do
            sb.Append(name).Append('\t').AppendLine(text) |> ignore
        for kv in includes |> Seq.sortBy (fun kv -> kv.Key) do
            sb.Append(kv.Key).Append('\t').AppendLine(kv.Value) |> ignore
        Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(sb.ToString())))

    [<Struct>]
    type internal CacheEntry = {
        Mtime: DateTime
        OutputHash: int
        ContentHash: string
    }

    let internal buildCache = ConcurrentDictionary<string, CacheEntry>()
    let private cacheDirty = ref false

    /// Reverse dependency graph: maps a file (e.g. a layout/include) to the
    /// set of source pages that depend on it. Key = dependency, Value = dependents.
    let internal dependencyGraph = ConcurrentDictionary<string, HashSet<string>>()
    let private depsDirty = ref false

    /// Forward dependency graph: maps a source page to the set of files it
    /// depends on (layouts, includes). Key = srcPath, Value = dependencies.
    /// Reconstructed from dependencyGraph on load; used by needsRebuildWithDeps.
    let internal srcDependencies = ConcurrentDictionary<string, HashSet<string>>()

    // ── Engine signature ──
    let private engineSignature () : string =
        try
            let asmPath = System.Reflection.Assembly.GetExecutingAssembly().Location
            if String.IsNullOrEmpty asmPath || not (File.Exists asmPath) then "unknown"
            else
                let info = FileInfo(asmPath)
                sprintf "%d|%d" info.LastWriteTimeUtc.Ticks info.Length
        with _ -> "unknown"

    /// Tracks the engine signature at last cache write so DevServer can
    /// detect mid-serve engine upgrades and force a full rebuild.
    let mutable private lastWrittenSig = ""

    /// Lock for thread-safe access to lastWrittenSig.
    let private sigLock = Object()

    /// Returns true when the engine DLL has changed since the cache was
    /// last written (e.g. mid-serve upgrade). Callers should clear the
    /// cache and trigger a full rebuild when this returns true.
    let hasEngineChanged () =
        let current = engineSignature ()
        let prevSig = lock sigLock (fun () -> lastWrittenSig)
        prevSig <> "" && current <> prevSig

    /// Compute a short SHA-256 content hash for a file's text.
    let internal contentHashOf (text: string) =
        use sha = SHA256.Create()
        let bytes = Text.Encoding.UTF8.GetBytes(text)
        let hash = sha.ComputeHash(bytes)
        hash.[0..7] |> Array.map (fun b -> b.ToString("x2")) |> String.concat ""

    // ── Atomic file write ──
    // Write to a temp file first, then rename atomically. Uses
    // File.Move(tmp, path, overwrite: true) on .NET 6+ to avoid the
    // delete-then-move race window. If the write step fails, the function
    // returns early without touching the existing cache file.
    let private atomicWrite (path: string) (write: StreamWriter -> unit) =
        let tmp = path + ".tmp"
        let mutable ok = false
        try
            use writer = new StreamWriter(tmp, false, Text.Encoding.UTF8)
            write writer
            writer.Flush()
            ok <- true
        with ex ->
            eprintfn "[Zest] WARN: Failed to write cache %s: %s" path ex.Message
            try File.Delete(tmp) with _ -> ()
        if ok then
            try
                File.Move(tmp, path, overwrite = true)
            with ex ->
                eprintfn "[Zest] WARN: Failed to finalise cache %s: %s" path ex.Message
                try File.Delete(tmp) with _ -> ()

    /// Rebuild the forward dependency graph (srcDependencies) from the
    /// reverse graph (dependencyGraph). Called after loading the deps file.
    let private rebuildForwardGraph () =
        srcDependencies.Clear()
        for kv in dependencyGraph do
            for srcPath in kv.Value do
                let set = srcDependencies.GetOrAdd(srcPath, fun _ -> HashSet<string>())
                lock set (fun () -> set.Add(kv.Key) |> ignore)

    /// Clear all cached entries, dependency graphs, and forward graph. Defined
    /// before loadCache because a signature mismatch during load must be able
    /// to discard a stale cache immediately.
    let clearCache () =
        buildCache.Clear()
        dependencyGraph.Clear()
        srcDependencies.Clear()
        cacheDirty := true
        depsDirty := true

    /// Load the persistent cache. If the engine signature or the template
    /// signature has changed, the cache is ignored and a full rebuild is
    /// triggered. The template signature is a content hash over every layout
    /// and include, so editing, adding, or deleting a template invalidates the
    /// page cache while an unchanged template set still lets unchanged pages
    /// be reused for fast incremental builds.
    let internal loadCache (outputDir: string) (templateSig: string) =
        // Clean up legacy cache files from older Zest versions
        // (.json from v0, .toml from transitional naming, and bare files).
        for oldSuffix in [ ".json"; ".toml"; "" ] do
            for baseName in [ ".zest-cache"; ".zest-deps" ] do
                let oldPath = Path.Combine(outputDir, baseName + oldSuffix)
                try if File.Exists(oldPath) then File.Delete(oldPath)
                with _ -> ()

        if not (buildCache.IsEmpty) then ()
        else
            let currentSig = engineSignature ()
            let path = cacheFilePath outputDir

            // Read the header once and validate both signatures before
            // touching the cache body. A header without a `tpl=` token came
            // from a build before template tracking existed, so it is treated
            // as invalid to force one clean full rebuild.
            let header =
                if File.Exists path then
                    try
                        use reader = new StreamReader(path, System.Text.Encoding.UTF8)
                        reader.ReadLine()
                    with _ -> null
                else null
            let engineOk =
                match headerToken header "engine" with
                | Some sig' -> sig' = currentSig
                | None -> true
            let templateOk =
                match headerToken header "tpl" with
                | Some sig' -> sig' = templateSig
                | None -> false

            if not engineOk || not templateOk then
                if not engineOk then
                    eprintfn "[Zest] Engine changed since last build — forcing full rebuild."
                else
                    eprintfn "[Zest] Template files changed since last build — forcing full rebuild."
                clearCache ()
            else
                // ── Load page cache ──
                if File.Exists path then
                    try
                        use reader = new StreamReader(path, System.Text.Encoding.UTF8)
                        reader.ReadLine() |> ignore   // header already validated
                        let mutable line = reader.ReadLine()
                        while line <> null do
                            if not (line.StartsWith("#")) then
                                let parts = line.Split([|'\t'|])
                                if parts.Length >= 3 then
                                    match Int64.TryParse(parts.[1]), Int32.TryParse(parts.[2]) with
                                    | (true, ticks), (true, hash) ->
                                        let ch = if parts.Length >= 4 then parts.[3] else ""
                                        if File.Exists(parts.[0]) then
                                            buildCache.[parts.[0]] <-
                                                { Mtime = DateTime(ticks, DateTimeKind.Utc)
                                                  OutputHash = hash
                                                  ContentHash = ch }
                                    | _ -> ()
                            line <- reader.ReadLine()
                    with ex ->
                        eprintfn "[Zest] WARN: Failed to load cache: %s" ex.Message

                // ── Load dependency graph ──
                let depsPath = depsFilePath outputDir
                if File.Exists depsPath then
                    try
                        use reader = new StreamReader(depsPath, System.Text.Encoding.UTF8)
                        reader.ReadLine() |> ignore   // header shares engine/tpl sig with the cache file
                        let mutable line = reader.ReadLine()
                        while line <> null do
                            if not (line.StartsWith("#")) then
                                let parts = line.Split([|'\t'|], 2)
                                if parts.Length = 2 then
                                    let pages = parts.[1].Split(',') |> Array.filter (fun s -> s <> "")
                                    dependencyGraph.[parts.[0]] <- HashSet<string>(pages)
                            line <- reader.ReadLine()
                        rebuildForwardGraph ()
                    with ex ->
                        eprintfn "[Zest] WARN: Failed to load dep graph: %s" ex.Message

                // Record the engine signature at load time so DevServer can
                // detect mid-serve engine upgrades on subsequent rebuilds.
                lock sigLock (fun () ->
                    if lastWrittenSig = "" then
                        lastWrittenSig <- currentSig)

    /// Save the persistent cache (atomic write, stale entries pruned).
    let internal saveCache (outputDir: string) (templateSig: string) =
        let engSig = engineSignature ()
        let header = sprintf "# zest-cache v%d | engine=%s | tpl=%s" CACHE_FORMAT_VERSION engSig templateSig
        lock sigLock (fun () -> lastWrittenSig <- engSig)

        if !cacheDirty then
            atomicWrite (cacheFilePath outputDir) (fun writer ->
                writer.WriteLine(header)
                for kv in buildCache do
                    if File.Exists(kv.Key) then
                        writer.Write(kv.Key); writer.Write('\t')
                        writer.Write(kv.Value.Mtime.Ticks); writer.Write('\t')
                        writer.Write(kv.Value.OutputHash); writer.Write('\t')
                        writer.WriteLine(kv.Value.ContentHash))
            cacheDirty := false

        if !depsDirty then
            atomicWrite (depsFilePath outputDir) (fun writer ->
                writer.WriteLine(header)
                for kv in dependencyGraph do
                    writer.Write(kv.Key); writer.Write('\t')
                    writer.WriteLine(String.concat "," kv.Value))
            depsDirty := false

    // ── Rebuild checks ──

    let internal needsRebuild (srcPath: string) (outPath: string) =
        let mtime = File.GetLastWriteTimeUtc(srcPath)
        match buildCache.TryGetValue(srcPath) with
        | true, e when e.Mtime = mtime && File.Exists(outPath) -> false
        | _ -> true

    /// Whether a source file needs rebuilding, also considering its
    /// dependencies (layouts/includes). Queries the forward dependency
    /// graph (srcDependencies) to check if any dependency has been
    /// modified since the source was last built.
    let internal needsRebuildWithDeps (srcPath: string) (outPath: string) =
        if needsRebuild srcPath outPath then true
        else
            match buildCache.TryGetValue(srcPath) with
            | true, e ->
                match srcDependencies.TryGetValue(srcPath) with
                | true, deps ->
                    deps
                    |> Seq.exists (fun d ->
                        File.Exists(d) && File.GetLastWriteTimeUtc(d) > e.Mtime)
                | _ -> false
            | _ -> true

    /// Whether a source file needs rebuilding, comparing content hash too.
    let internal needsRebuildByContent (srcPath: string) (outPath: string) =
        let mtime = File.GetLastWriteTimeUtc(srcPath)
        match buildCache.TryGetValue(srcPath) with
        | true, e when e.Mtime = mtime && e.ContentHash <> "" && File.Exists(outPath) ->
            let ch = contentHashOf (File.ReadAllText(srcPath))
            ch <> e.ContentHash
        | _ -> true

    // ── Dependency tracking ──

    /// Record that srcPath depends on dependencyPath (e.g. a page depends on
    /// a layout or include). Updates both the reverse graph (dependencyPath →
    /// dependents) and the forward graph (srcPath → dependencies).
    let internal recordDependency (srcPath: string) (dependencyPath: string) =
        let revSet = dependencyGraph.GetOrAdd(dependencyPath, fun _ -> HashSet<string>())
        lock revSet (fun () -> revSet.Add(srcPath) |> ignore)
        let fwdSet = srcDependencies.GetOrAdd(srcPath, fun _ -> HashSet<string>())
        lock fwdSet (fun () -> fwdSet.Add(dependencyPath) |> ignore)
        depsDirty := true

    let internal getAffectedPages (changedFile: string) : string list =
        let result = HashSet<string>()
        let queue = Queue<string>()
        queue.Enqueue(changedFile)
        let visited = HashSet<string>()
        while queue.Count > 0 do
            let cur = queue.Dequeue()
            if visited.Add(cur) then
                result.Add(cur) |> ignore
                match dependencyGraph.TryGetValue(cur) with
                | true, dependents ->
                    for d in dependents do
                        if not (visited.Contains d) then queue.Enqueue(d)
                | _ -> ()
        Seq.toList result

    // ── Cache updates ──

    /// Update the build cache for srcPath. Captures mtime *before* reading
    /// content so that if the file is modified concurrently, the stale mtime
    /// will cause a rebuild on the next check rather than silently persisting
    /// inconsistent data.
    let internal updateCache (srcPath: string) (html: string) =
        let mtime = File.GetLastWriteTimeUtc(srcPath)
        let ch = try contentHashOf (File.ReadAllText(srcPath)) with _ -> ""
        buildCache.[srcPath] <- { Mtime = mtime; OutputHash = html.GetHashCode(); ContentHash = ch }
        cacheDirty := true

    let internal updateCacheWithHash (srcPath: string) (html: string) (sourceText: string) =
        let mtime = File.GetLastWriteTimeUtc(srcPath)
        buildCache.[srcPath] <- { Mtime = mtime; OutputHash = html.GetHashCode(); ContentHash = contentHashOf sourceText }
        cacheDirty := true

    // ── Cache management ──

    /// Clear on-disk cache files for a given output directory.
    /// Called by `zest clean --cache`.
    let clearDiskCache (outputDir: string) =
        clearCache ()
        let files = [ cacheFilePath outputDir; depsFilePath outputDir ]
        for f in files do
            try if File.Exists(f) then File.Delete(f)
            with ex -> eprintfn "[Zest] WARN: Could not delete %s: %s" f ex.Message

    /// Force the next saveCache to write even if no entries changed (e.g.
    /// after an engine upgrade to refresh the header signature).
    let markDirty () =
        cacheDirty := true
        depsDirty := true
