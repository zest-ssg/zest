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
///   .zest-cache.toml — per-page mtime + content hash
///   .zest-deps.toml  — dependency graph (layout/include → dependent pages)
///
/// The first line of each file is a header comment carrying a cache-format
/// version and an "engine signature" (Zest.Engine.dll mtime + size). If the
/// engine DLL changes (upgrade, recompile), the signature mismatches on load
/// and the entire cache is ignored — forcing a full rebuild. This prevents
/// stale pages built by a previous engine version from being served.
module BuildCache =

    // ── Cache format ──
    let private CACHE_FORMAT_VERSION = 2
    let private cacheFilePath (outputDir: string) = Path.Combine(outputDir, ".zest-cache.toml")
    let private depsFilePath  (outputDir: string) = Path.Combine(outputDir, ".zest-deps.toml")

    [<Struct>]
    type internal CacheEntry = {
        Mtime: DateTime
        OutputHash: int
        ContentHash: string
    }

    let internal buildCache = ConcurrentDictionary<string, CacheEntry>()
    let private cacheDirty = ref false

    /// Dependency graph: maps a file (e.g. a layout/include) to the set of
    /// source pages that depend on it.
    let internal dependencyGraph = ConcurrentDictionary<string, HashSet<string>>()
    let private depsDirty = ref false

    // ── Engine signature ──
    // A signature derived from the Zest.Engine assembly's last-write time and
    // file size. When the engine is recompiled or upgraded, the signature
    // changes and all cached entries are invalidated — guaranteeing a full
    // rebuild after any engine change.
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

    /// Returns true when the engine DLL has changed since the cache was
    /// last written (e.g. mid-serve upgrade). Callers should clear the
    /// cache and trigger a full rebuild when this returns true.
    let hasEngineChanged () =
        let current = engineSignature ()
        lastWrittenSig <> "" && current <> lastWrittenSig

    /// Compute a short SHA-256 content hash for a file's text.
    let internal contentHashOf (text: string) =
        use sha = SHA256.Create()
        let bytes = Text.Encoding.UTF8.GetBytes(text)
        sha.ComputeHash(bytes)
        |> fun h -> h.[0..7] |> Array.map (fun b -> b.ToString("x2")) |> String.concat ""

    // ── Atomic file write ──
    // Write to a temp file first, then rename. Prevents cache corruption if
    // the process is killed mid-write (e.g. Ctrl+C during dev server).
    let private atomicWrite (path: string) (write: StreamWriter -> unit) =
        let tmp = path + ".tmp"
        try
            use writer = new StreamWriter(tmp, false, Text.Encoding.UTF8)
            write writer
            writer.Flush()
        with ex ->
            eprintfn "[Zest] WARN: Failed to write cache %s: %s" path ex.Message
            try File.Delete(tmp) with _ -> ()
            ()
        try
            if File.Exists(path) then File.Delete(path)
            File.Move(tmp, path)
        with ex ->
            eprintfn "[Zest] WARN: Failed to finalise cache %s: %s" path ex.Message

    /// Load the persistent cache. If the engine signature has changed (engine
    /// upgrade/recompile) or the cache format is incompatible, the cache is
    /// ignored and a full rebuild is triggered.
    let internal loadCache (outputDir: string) =
        // Clean up legacy .json cache files from older Zest versions.
        for old in [ Path.Combine(outputDir, ".zest-cache.json")
                     Path.Combine(outputDir, ".zest-deps.json") ] do
            try if File.Exists(old) then File.Delete(old)
            with _ -> ()

        if not (buildCache.IsEmpty) then ()
        else
            let currentSig = engineSignature ()

            // ── Load page cache ──
            let path = cacheFilePath outputDir
            if File.Exists path then
                try
                    use reader = new StreamReader(path, Text.Encoding.UTF8)
                    let headerLine = reader.ReadLine()
                    let mutable sigMatched = true
                    if headerLine <> null && headerLine.StartsWith("#") then
                        // Parse: # zest-cache v2 | engine=<ticks>|<size>
                        if headerLine.Contains("engine=") then
                            let startIdx = headerLine.IndexOf("engine=") + 7
                            let endIdx = headerLine.IndexOf(' ', startIdx)
                            let cachedSig =
                                if endIdx > startIdx then headerLine.[startIdx..endIdx-1]
                                else headerLine.[startIdx..]
                            if cachedSig <> currentSig then
                                sigMatched <- false
                                eprintfn "[Zest] Engine changed since last build — forcing full rebuild."
                    if not sigMatched then ()
                    else
                        let mutable line = reader.ReadLine()
                        while line <> null do
                            if not (line.StartsWith("#")) then
                                let parts = line.Split([|'\t'|])
                                if parts.Length >= 3 then
                                    match Int64.TryParse(parts.[1]), Int32.TryParse(parts.[2]) with
                                    | (true, ticks), (true, hash) ->
                                        let ch = if parts.Length >= 4 then parts.[3] else ""
                                        // Skip stale entries (source file deleted)
                                        if File.Exists(parts.[0]) then
                                            buildCache.[parts.[0]] <- { Mtime = DateTime(ticks, DateTimeKind.Utc); OutputHash = hash; ContentHash = ch }
                                    | _ -> ()
                            line <- reader.ReadLine()
                with ex ->
                    eprintfn "[Zest] WARN: Failed to load cache: %s" ex.Message

            // ── Load dependency graph ──
            let depsPath = depsFilePath outputDir
            if File.Exists depsPath then
                try
                    use reader = new StreamReader(depsPath, Text.Encoding.UTF8)
                    let headerLine = reader.ReadLine()
                    // Re-check engine sig in deps file too
                    let mutable sigMatched = true
                    if headerLine <> null && headerLine.StartsWith("#") && headerLine.Contains("engine=") then
                        let startIdx = headerLine.IndexOf("engine=") + 7
                        let endIdx = headerLine.IndexOf(' ', startIdx)
                        let cachedSig =
                            if endIdx > startIdx then headerLine.[startIdx..endIdx-1]
                            else headerLine.[startIdx..]
                        if cachedSig <> currentSig then sigMatched <- false
                    if sigMatched then
                        let mutable line = reader.ReadLine()
                        while line <> null do
                            if not (line.StartsWith("#")) then
                                let parts = line.Split([|'\t'|], 2)
                                if parts.Length = 2 then
                                    let pages = parts.[1].Split(',') |> Array.filter (fun s -> s <> "")
                                    let set = HashSet<string>(pages)
                                    dependencyGraph.[parts.[0]] <- set
                            line <- reader.ReadLine()
                with ex ->
                    eprintfn "[Zest] WARN: Failed to load dep graph: %s" ex.Message

            // Record the engine signature at load time so DevServer can
            // detect mid-serve engine upgrades on subsequent rebuilds.
            if lastWrittenSig = "" then
                lastWrittenSig <- currentSig

    /// Save the persistent cache (atomic write, stale entries pruned).
    let internal saveCache (outputDir: string) =
        let engSig = engineSignature ()
        let header = sprintf "# zest-cache v%d | engine=%s" CACHE_FORMAT_VERSION engSig
        lastWrittenSig <- engSig

        if !cacheDirty then
            atomicWrite (cacheFilePath outputDir) (fun writer ->
                writer.WriteLine(header)
                for kv in buildCache do
                    // Prune entries for files that no longer exist
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

    /// Whether a source file needs rebuilding based on mtime alone.
    let internal needsRebuild (srcPath: string) (outPath: string) =
        let mtime = File.GetLastWriteTimeUtc(srcPath)
        match buildCache.TryGetValue(srcPath) with
        | true, e when e.Mtime = mtime && File.Exists(outPath) -> false
        | _ -> true

    /// Whether a source file needs rebuilding, also considering its
    /// dependencies (layouts/includes).
    let internal needsRebuildWithDeps (srcPath: string) (outPath: string) =
        if needsRebuild srcPath outPath then true
        else
            match buildCache.TryGetValue(srcPath) with
            | true, e ->
                match dependencyGraph.TryGetValue(srcPath) with
                | true, deps ->
                    deps
                    |> Seq.exists (fun d ->
                        File.Exists(d) && File.GetLastWriteTimeUtc(d) > e.Mtime)
                | _ -> false
            | _ -> true

    /// Whether a source file needs rebuilding, comparing content hash too.
    /// Catches cases where mtime was touched but content is unchanged, and
    /// where content changed but mtime wasn't updated (e.g. git checkout).
    let internal needsRebuildByContent (srcPath: string) (outPath: string) =
        let mtime = File.GetLastWriteTimeUtc(srcPath)
        match buildCache.TryGetValue(srcPath) with
        | true, e when e.Mtime = mtime && e.ContentHash <> "" && File.Exists(outPath) ->
            let ch = contentHashOf (File.ReadAllText(srcPath))
            ch <> e.ContentHash
        | _ -> true

    // ── Dependency tracking ──

    let internal recordDependency (srcPath: string) (dependencyPath: string) =
        let set = dependencyGraph.GetOrAdd(dependencyPath, fun _ -> HashSet<string>())
        lock set (fun () -> set.Add(srcPath) |> ignore)
        depsDirty := true

    let internal getAffectedPages (changedFile: string) : string list =
        let result = HashSet<string>()
        let queue = System.Collections.Generic.Queue<string>()
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

    let internal updateCache (srcPath: string) (html: string) =
        let ch = try contentHashOf (File.ReadAllText(srcPath)) with _ -> ""
        buildCache.[srcPath] <- { Mtime = File.GetLastWriteTimeUtc(srcPath); OutputHash = html.GetHashCode(); ContentHash = ch }
        cacheDirty := true

    let internal updateCacheWithHash (srcPath: string) (html: string) (sourceText: string) =
        buildCache.[srcPath] <- { Mtime = File.GetLastWriteTimeUtc(srcPath); OutputHash = html.GetHashCode(); ContentHash = contentHashOf sourceText }
        cacheDirty := true

    // ── Cache management ──

    /// Clear all cached entries and dependencies. Also deletes the on-disk
    /// cache files so the next build starts fresh.
    let clearCache () =
        buildCache.Clear()
        dependencyGraph.Clear()
        cacheDirty := true
        depsDirty := true

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
