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
/// Cache format (tab-delimited, one entry per line):
///   <srcPath>\t<mtimeTicks>\t<outputHash>\t<contentHash>
/// The contentHash field is optional when loading legacy 3-field caches.
module BuildCache =

    [<Struct>]
    type internal CacheEntry = {
        Mtime: DateTime
        OutputHash: int
        ContentHash: string
    }

    let internal buildCache = ConcurrentDictionary<string, CacheEntry>()
    let private cacheDirty = ref false

    /// Dependency graph: maps a file (e.g. a layout/include) to the set of
    /// source pages that depend on it. When a dependency changes, those
    /// pages are rebuilt even if their own mtime is unchanged.
    let internal dependencyGraph = ConcurrentDictionary<string, HashSet<string>>()
    let private depsDirty = ref false

    let private cacheFilePath (outputDir: string) = Path.Combine(outputDir, ".zest-cache.json")
    let private depsFilePath (outputDir: string) = Path.Combine(outputDir, ".zest-deps.json")

    /// Compute a short SHA-256 content hash for a file's text.
    let internal contentHashOf (text: string) =
        use sha = SHA256.Create()
        let bytes = Text.Encoding.UTF8.GetBytes(text)
        sha.ComputeHash(bytes)
        |> fun h -> h.[0..7] |> Array.map (fun b -> b.ToString("x2")) |> String.concat ""

    let internal loadCache (outputDir: string) =
        if not (buildCache.IsEmpty) then ()
        else
            let path = cacheFilePath outputDir
            if File.Exists path then
                try
                    use reader = new StreamReader(path, Text.Encoding.UTF8)
                    let mutable line = reader.ReadLine()
                    while line <> null do
                        let parts = line.Split([|'\t'|])
                        if parts.Length >= 3 then
                            match Int64.TryParse(parts.[1]), Int32.TryParse(parts.[2]) with
                            | (true, ticks), (true, hash) ->
                                // 4th field (contentHash) is optional for legacy caches.
                                let ch = if parts.Length >= 4 then parts.[3] else ""
                                buildCache.[parts.[0]] <- { Mtime = DateTime(ticks, DateTimeKind.Utc); OutputHash = hash; ContentHash = ch }
                            | _ -> ()
                        line <- reader.ReadLine()
                with _ -> ()

            // Load dependency graph.
            let depsPath = depsFilePath outputDir
            if File.Exists depsPath then
                try
                    use reader = new StreamReader(depsPath, Text.Encoding.UTF8)
                    let mutable line = reader.ReadLine()
                    while line <> null do
                        // Format: <depFile>\t<page1>,<page2>,...
                        let parts = line.Split([|'\t'|], 2)
                        if parts.Length = 2 then
                            let pages = parts.[1].Split(',') |> Array.filter (fun s -> s <> "")
                            let set = HashSet<string>(pages)
                            dependencyGraph.[parts.[0]] <- set
                        line <- reader.ReadLine()
                with _ -> ()

    let internal saveCache (outputDir: string) =
        if !cacheDirty then
            try
                let path = cacheFilePath outputDir
                use writer = new StreamWriter(path, false, Text.Encoding.UTF8)
                for kv in buildCache do
                    writer.Write(kv.Key); writer.Write('\t')
                    writer.Write(kv.Value.Mtime.Ticks); writer.Write('\t')
                    writer.Write(kv.Value.OutputHash); writer.Write('\t')
                    writer.WriteLine(kv.Value.ContentHash)
                cacheDirty := false
            with _ -> ()
        if !depsDirty then
            try
                let path = depsFilePath outputDir
                use writer = new StreamWriter(path, false, Text.Encoding.UTF8)
                for kv in dependencyGraph do
                    writer.Write(kv.Key); writer.Write('\t')
                    writer.WriteLine(String.concat "," kv.Value)
                depsDirty := false
            with _ -> ()

    /// Whether a source file needs rebuilding based on mtime alone.
    let internal needsRebuild (srcPath: string) (outPath: string) =
        let mtime = File.GetLastWriteTimeUtc(srcPath)
        match buildCache.TryGetValue(srcPath) with
        | true, e when e.Mtime = mtime && File.Exists(outPath) -> false
        | _ -> true

    /// Whether a source file needs rebuilding, also considering its
    /// dependencies (layouts/includes). Returns true if the page itself
    /// changed OR any dependency file is newer than the page's last build.
    let internal needsRebuildWithDeps (srcPath: string) (outPath: string) =
        if needsRebuild srcPath outPath then true
        else
            // Page mtime unchanged — check if any dependency is newer than
            // the page's cached build time.
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
    /// Catches cases where mtime was touched but content is unchanged
    /// (avoids spurious rebuilds), and where content changed but mtime
    /// wasn't updated (e.g. git checkout).
    let internal needsRebuildByContent (srcPath: string) (outPath: string) =
        let mtime = File.GetLastWriteTimeUtc(srcPath)
        match buildCache.TryGetValue(srcPath) with
        | true, e when e.Mtime = mtime && e.ContentHash <> "" && File.Exists(outPath) ->
            // mtime matches — verify content hash to be sure.
            let ch = contentHashOf (File.ReadAllText(srcPath))
            ch <> e.ContentHash
        | _ -> true

    /// Record that `srcPath` depends on `dependencyPath` (e.g. a page
    /// depends on its layout or an include). Used by the incremental
    /// builder to compute affected pages when a dependency changes.
    let internal recordDependency (srcPath: string) (dependencyPath: string) =
        let set = dependencyGraph.GetOrAdd(dependencyPath, fun _ -> HashSet<string>())
        lock set (fun () -> set.Add(srcPath) |> ignore)
        depsDirty := true

    /// Get the set of source pages that (transitively) depend on a changed
    /// file. If `changedFile` is itself a page, it is included.
    let internal getAffectedPages (changedFile: string) : string list =
        let result = HashSet<string>()
        let queue = System.Collections.Generic.Queue<string>()
        queue.Enqueue(changedFile)
        let visited = HashSet<string>()
        while queue.Count > 0 do
            let cur = queue.Dequeue()
            if visited.Add(cur) then
                // cur may itself be a page that changed.
                result.Add(cur) |> ignore
                // find pages that depend on cur
                match dependencyGraph.TryGetValue(cur) with
                | true, dependents ->
                    for d in dependents do
                        if not (visited.Contains d) then queue.Enqueue(d)
                | _ -> ()
        Seq.toList result

    let internal updateCache (srcPath: string) (html: string) =
        let ch = try contentHashOf (File.ReadAllText(srcPath)) with _ -> ""
        buildCache.[srcPath] <- { Mtime = File.GetLastWriteTimeUtc(srcPath); OutputHash = html.GetHashCode(); ContentHash = ch }
        cacheDirty := true

    /// Update cache with an explicit content hash (avoids re-reading the file
    /// when the caller already has the text).
    let internal updateCacheWithHash (srcPath: string) (html: string) (sourceText: string) =
        buildCache.[srcPath] <- { Mtime = File.GetLastWriteTimeUtc(srcPath); OutputHash = html.GetHashCode(); ContentHash = contentHashOf sourceText }
        cacheDirty := true

    /// Clear all cached entries and dependencies (e.g. `zest clean --cache`).
    /// Public so the CLI layer (Zest.Infra.BuildService / Zest.App.CleanController)
    /// can invoke it without InternalsVisibleTo plumbing.
    let clearCache () =
        buildCache.Clear()
        dependencyGraph.Clear()
        cacheDirty := true
        depsDirty := true
