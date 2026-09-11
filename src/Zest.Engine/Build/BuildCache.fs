namespace Zest.Engine

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open System.Security.Cryptography

/// Incremental build cache: tracks each source file's modification time and
/// content hash, plus a dependency graph, so only the pages whose own content
/// or a template they depend on actually changed are rebuilt.
///
/// Cache files (written to the output directory):
///   .zest-cache.log     — per-page timestamp, output path, and content hash
///   .zest-deps.log      — dependency graph (layout/include → dependent pages)
///   .zest-templates.log — content hash of every layout/include
///
/// Timestamps are captured at millisecond precision and serve only as a fast
/// hint; the per-file content hash is the source of truth. The hash catches an
/// edit that preserved the timestamp and avoids rebuilding a file that was
/// merely touched. When a template's hash changes, the dependency graph is
/// walked to mark only its dependent pages stale instead of discarding the
/// whole cache.
///
/// The first line of each file is a header comment carrying a cache-format
/// version and an "engine signature" (Zest.Engine.dll mtime + size). If the
/// engine DLL changes (upgrade, recompile), the signature mismatches on load
/// and the entire cache is ignored — forcing a full rebuild. This prevents
/// stale pages built by a previous engine version from being served.
module BuildCache =

    // ── Cache format ──
    let private CACHE_FORMAT_VERSION = 2
    let private cacheFilePath (outputDir: string) = Path.Combine(outputDir, ".zest-cache.log")
    let private depsFilePath  (outputDir: string) = Path.Combine(outputDir, ".zest-deps.log")
    let private templatesFilePath (outputDir: string) = Path.Combine(outputDir, ".zest-templates.log")

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

    /// Modification time truncated to millisecond precision. Truncating keeps
    /// comparisons stable across file systems whose native resolution is
    /// coarser than a .NET tick; edits finer than a millisecond are still
    /// caught by the content hash. Both the write and the check side use this
    /// function, so a persisted value never differs spuriously from a fresh read.
    let internal fileTimestamp (path: string) : DateTime =
        let utc = File.GetLastWriteTimeUtc(path)
        DateTime(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Utc)

    /// Compute a short SHA-256 content hash for a file's text.
    let internal contentHashOf (text: string) =
        use sha = SHA256.Create()
        let bytes = Text.Encoding.UTF8.GetBytes(text)
        let hash = sha.ComputeHash(bytes)
        hash.[0..7] |> Array.map (fun b -> b.ToString("x2")) |> String.concat ""

    /// Deterministic content signature over every include body. Used to
    /// invalidate the include-substituted layout cache without relying on
    /// timestamps. Keys are sorted so the signature is stable regardless of
    /// dictionary order.
    let internal computeIncludesSignature (includes: IDictionary<string, string>) : string =
        use sha = SHA256.Create()
        let sb = System.Text.StringBuilder()
        for kv in includes |> Seq.sortBy (fun kv -> kv.Key) do
            sb.Append(kv.Key).Append('\t').AppendLine(kv.Value) |> ignore
        Convert.ToHexString(sha.ComputeHash(Text.Encoding.UTF8.GetBytes(sb.ToString())))

    [<Struct>]
    type internal CacheEntry = {
        Mtime: DateTime
        OutputPath: string
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
    /// Reconstructed from dependencyGraph on load.
    let internal srcDependencies = ConcurrentDictionary<string, HashSet<string>>()

    /// Content hash of every layout/include recorded by the previous build,
    /// keyed by absolute path. Diffing this against the current templates is
    /// what scopes a template edit to just its dependent pages.
    let internal templateHashes = ConcurrentDictionary<string, string>()
    let private templatesDirty = ref false

    /// Pages invalidated because a template they depend on changed. The rebuild
    /// predicates consult this so an otherwise-unchanged page is still
    /// regenerated, and the entry is cleared as soon as the page is written.
    let private stalePages = ConcurrentDictionary<string, bool>()
    let internal isStale (srcPath: string) = stalePages.ContainsKey srcPath
    let internal clearStale (srcPath: string) = stalePages.TryRemove(srcPath) |> ignore

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

    /// Clear all cached entries, dependency graphs, template hashes, and stale
    /// markers. Defined before loadCache because a signature mismatch during
    /// load must be able to discard a stale cache immediately.
    let clearCache () =
        buildCache.Clear()
        dependencyGraph.Clear()
        srcDependencies.Clear()
        templateHashes.Clear()
        stalePages.Clear()
        cacheDirty := true
        depsDirty := true
        templatesDirty := true

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

    /// Transitive closure of pages affected by a changed file, following the
    /// reverse dependency graph (a layout edit reaches every page that uses it,
    /// including pages reached through a nested layout chain).
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

    // ── Template reconciliation ──

    /// Compare the current layout/include bodies against the hashes recorded by
    /// the previous build and mark every dependent page stale for a changed
    /// template. Returns true when a change cannot be scoped to a known
    /// dependency set (a previously tracked template with no recorded
    /// dependents), signalling that the caller must fall back to a full rebuild.
    let private reconcileTemplates (templates: (string * string) list) : bool =
        let current = Dictionary<string, string>()
        for (path, text) in templates do
            current.[path] <- contentHashOf text
        let mutable unscoped = false
        let markDependents (templatePath: string) =
            let dependents =
                getAffectedPages templatePath
                |> List.filter (fun p -> not (p.StartsWith "<") && not (String.IsNullOrEmpty p))
            if dependents.IsEmpty then unscoped <- true
            else for p in dependents do stalePages.TryAdd(p, true) |> ignore
        // Modified templates invalidate their dependents. Added templates have
        // no previous dependents, so they are recorded without forcing a rebuild.
        for kv in current do
            match templateHashes.TryGetValue kv.Key with
            | true, previous when previous = kv.Value -> ()
            | true, _ -> markDependents kv.Key
            | _ -> ()
        // Deleted templates no longer exist on disk but their dependents still
        // reference them, so they must be rebuilt.
        for kv in templateHashes do
            if not (current.ContainsKey kv.Key) then markDependents kv.Key
        let changed =
            templateHashes.Count <> current.Count
            || current |> Seq.exists (fun kv ->
                match templateHashes.TryGetValue kv.Key with
                | true, previous -> previous <> kv.Value
                | _ -> true)
        if changed then
            templateHashes.Clear()
            for kv in current do templateHashes.[kv.Key] <- kv.Value
            templatesDirty := true
        unscoped

    // ── Load / save ──

    let private loadPageCache (outputDir: string) =
        let path = cacheFilePath outputDir
        if File.Exists path then
            try
                use reader = new StreamReader(path, Text.Encoding.UTF8)
                reader.ReadLine() |> ignore   // header already validated
                let mutable line = reader.ReadLine()
                while line <> null do
                    if not (line.StartsWith("#")) then
                        let parts = line.Split([|'\t'|])
                        if parts.Length >= 4 then
                            match Int64.TryParse(parts.[1]) with
                            | true, ticks when File.Exists(parts.[0]) ->
                                buildCache.[parts.[0]] <-
                                    { Mtime = DateTime(ticks, DateTimeKind.Utc)
                                      OutputPath = parts.[2]
                                      ContentHash = parts.[3] }
                            | _ -> ()
                    line <- reader.ReadLine()
            with ex ->
                eprintfn "[Zest] WARN: Failed to load cache: %s" ex.Message

    let private loadDependencyGraph (outputDir: string) =
        let depsPath = depsFilePath outputDir
        if File.Exists depsPath then
            try
                use reader = new StreamReader(depsPath, Text.Encoding.UTF8)
                reader.ReadLine() |> ignore   // header shares engine/ver sig with the cache file
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

    /// Load the persisted template index. Returns false when the file is
    /// absent, which callers treat as "template changes cannot be detected".
    let private loadTemplateHashes (outputDir: string) : bool =
        let path = templatesFilePath outputDir
        if not (File.Exists path) then false
        else
            try
                use reader = new StreamReader(path, Text.Encoding.UTF8)
                reader.ReadLine() |> ignore
                let mutable line = reader.ReadLine()
                while line <> null do
                    if not (line.StartsWith("#")) then
                        let parts = line.Split([|'\t'|], 2)
                        if parts.Length = 2 then templateHashes.[parts.[0]] <- parts.[1]
                    line <- reader.ReadLine()
                true
            with ex ->
                eprintfn "[Zest] WARN: Failed to load template index: %s" ex.Message
                false

    /// Load the persistent cache and reconcile the template set. The engine
    /// signature and cache-format version gate the on-disk page cache; template
    /// reconciliation runs on every call so the in-process rebuild path (dev and
    /// preview servers) reuses resident pages but still notices template edits.
    let internal loadCache (outputDir: string) (templates: (string * string) list) =
        // Clean up legacy cache files from older Zest versions
        // (.json from v0, .toml from transitional naming, and bare files).
        for oldSuffix in [ ".json"; ".toml"; "" ] do
            for baseName in [ ".zest-cache"; ".zest-deps"; ".zest-templates" ] do
                let oldPath = Path.Combine(outputDir, baseName + oldSuffix)
                try if File.Exists(oldPath) then File.Delete(oldPath)
                with _ -> ()

        if buildCache.IsEmpty then
            let path = cacheFilePath outputDir
            let header =
                if File.Exists path then
                    try
                        use reader = new StreamReader(path, Text.Encoding.UTF8)
                        reader.ReadLine()
                    with _ -> null
                else null
            if header = null then
                clearCache ()
            else
                let currentSig = engineSignature ()
                let engineOk =
                    match headerToken header "engine" with
                    | Some sig' -> sig' = currentSig
                    | None -> true
                let versionOk =
                    match headerToken header "ver" with
                    | Some v -> v = string CACHE_FORMAT_VERSION
                    | None -> false
                if not engineOk then
                    eprintfn "[Zest] Engine changed since last build — forcing full rebuild."
                    clearCache ()
                elif not versionOk then
                    eprintfn "[Zest] Cache format changed — forcing full rebuild."
                    clearCache ()
                else
                    loadPageCache outputDir
                    loadDependencyGraph outputDir
                    let templatesLoaded = loadTemplateHashes outputDir
                    if not templatesLoaded && not buildCache.IsEmpty then
                        // Without the template index, a template edit could go
                        // unnoticed. Rebuild everything rather than serve a
                        // potentially stale page.
                        eprintfn "[Zest] Template index missing — forcing full rebuild."
                        clearCache ()
                    else
                        // Record the engine signature so DevServer can detect
                        // mid-serve engine upgrades on subsequent rebuilds.
                        lock sigLock (fun () ->
                            if lastWrittenSig = "" then lastWrittenSig <- currentSig)

        if reconcileTemplates templates then
            eprintfn "[Zest] Template dependencies unknown — forcing full rebuild."
            clearCache ()

    /// Save the persistent cache (atomic write, stale entries pruned).
    let internal saveCache (outputDir: string) =
        let engSig = engineSignature ()
        let header = sprintf "# zest-cache v%d | engine=%s | ver=%d" CACHE_FORMAT_VERSION engSig CACHE_FORMAT_VERSION
        lock sigLock (fun () -> lastWrittenSig <- engSig)

        if !cacheDirty then
            atomicWrite (cacheFilePath outputDir) (fun writer ->
                writer.WriteLine(header)
                for kv in buildCache do
                    if File.Exists(kv.Key) then
                        writer.Write(kv.Key); writer.Write('\t')
                        writer.Write(kv.Value.Mtime.Ticks); writer.Write('\t')
                        writer.Write(kv.Value.OutputPath); writer.Write('\t')
                        writer.WriteLine(kv.Value.ContentHash))
            cacheDirty := false

        if !depsDirty then
            atomicWrite (depsFilePath outputDir) (fun writer ->
                writer.WriteLine(header)
                for kv in dependencyGraph do
                    writer.Write(kv.Key); writer.Write('\t')
                    writer.WriteLine(String.concat "," kv.Value))
            depsDirty := false

        if !templatesDirty then
            atomicWrite (templatesFilePath outputDir) (fun writer ->
                writer.WriteLine(header)
                for kv in templateHashes do
                    writer.Write(kv.Key); writer.Write('\t')
                    writer.WriteLine(kv.Value))
            templatesDirty := false

    // ── Rebuild checks ──

    /// Whether a source file must be regenerated. A stale marker from a
    /// template change always wins. Otherwise the millisecond timestamp is a
    /// fast hint: an unchanged timestamp still prompts a content-hash check, so
    /// an edit that preserved the timestamp is not missed, and a changed
    /// timestamp with identical content does not discard a valid cached output.
    let internal needsRebuildWithText (srcPath: string) (sourceText: string) : bool =
        if isStale srcPath then true
        else
            match buildCache.TryGetValue srcPath with
            | true, e when File.Exists e.OutputPath ->
                let mtime = fileTimestamp srcPath
                if mtime <> e.Mtime then
                    if contentHashOf sourceText = e.ContentHash then
                        // The timestamp moved but the bytes did not. Refresh the
                        // recorded time so later builds keep taking the fast path.
                        buildCache.[srcPath] <- { e with Mtime = mtime }
                        false
                    else true
                else
                    e.ContentHash = "" || contentHashOf sourceText <> e.ContentHash
            | _ -> true

    // ── Cache updates ──

    /// Update the build cache for srcPath after writing its output. Captures the
    /// timestamp before hashing so a concurrent modification leaves a mismatch
    /// that forces a rebuild on the next check rather than persisting
    /// inconsistent data.
    let internal updateCacheWithHash (srcPath: string) (outPath: string) (html: string) (sourceText: string) =
        buildCache.[srcPath] <-
            { Mtime = fileTimestamp srcPath
              OutputPath = outPath
              ContentHash = contentHashOf sourceText }
        clearStale srcPath
        cacheDirty := true

    // ── Cache management ──

    /// Clear on-disk cache files for a given output directory.
    /// Called by `zest clean --cache`.
    let clearDiskCache (outputDir: string) =
        clearCache ()
        let files = [ cacheFilePath outputDir; depsFilePath outputDir; templatesFilePath outputDir ]
        for f in files do
            try if File.Exists(f) then File.Delete(f)
            with ex -> eprintfn "[Zest] WARN: Could not delete %s: %s" f ex.Message

    /// Force the next saveCache to write even if no entries changed (e.g.
    /// after an engine upgrade to refresh the header signature).
    let markDirty () =
        cacheDirty := true
        depsDirty := true
        templatesDirty := true
