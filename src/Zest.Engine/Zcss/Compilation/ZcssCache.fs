// ZcssCache.fs
//
// File-level cache for ZCSS compilation: tracks last-write time (system file
// attribute) plus a SHA-256 content hash per source file, and persists the
// cache to disk so unchanged files are not re-read or re-compiled across
// application restarts. Also maintains the @use dependency graph so a change
// to an imported module invalidates only its dependents (incremental builds).
//
// Dependencies: System.IO, System.Security.Cryptography

namespace Zest.Engine.Zcss

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open System.Security.Cryptography
open System.Text

// ============================================================
// ZcssCache — mtime + SHA-256 file-level cache for ZCSS compilation
// ============================================================

module ZcssCache =

    /// A single cached file entry.
    type CacheEntry = {
        /// LastWriteTimeUtc.Ticks captured when the file was processed.
        Mtime: int64
        /// SHA-256 hex digest of the file content.
        ContentHash: string
        /// Compiled CSS output.
        Css: string
        /// Last-write ticks of each @use dependency at compile time. Used to
        /// detect a changed import without a full rebuild.
        DepMtimes: (string * int64) list
    }

    let private entries = ConcurrentDictionary<string, CacheEntry>()
    /// Forward graph: dependent file → set of files it @use-imports.
    let private dependencies = ConcurrentDictionary<string, HashSet<string>>()
    /// Reverse graph: dependency file → set of files that import it.
    let private dependents = ConcurrentDictionary<string, HashSet<string>>()
    let private CACHE_FORMAT_VERSION = 1

    /// SHA-256 hex digest (uppercase) of a UTF-8 string.
    let hashContent (text: string) : string =
        use sha = SHA256.Create()
        let bytes = Encoding.UTF8.GetBytes(text)
        Convert.ToHexString(sha.ComputeHash(bytes))

    /// Look up cached CSS for a file. When `contentHash` is empty (fast path)
    /// only the mtime is compared — no file I/O. When a content hash is
    /// supplied, a file touched-but-unchanged still hits via the hash.
    let tryGet (filePath: string) (mtime: int64) (contentHash: string) : string option =
        match entries.TryGetValue(filePath) with
        | true, e when e.Mtime = mtime -> Some e.Css
        | true, e when e.ContentHash <> "" && contentHash <> "" && e.ContentHash = contentHash -> Some e.Css
        | _ -> None

    /// Store or refresh the cached entry for a file, recording the @use
    /// dependencies (as path × mtime pairs) for later change detection.
    let set (filePath: string) (mtime: int64) (contentHash: string) (css: string) (deps: (string * int64) list) =
        entries.[filePath] <- { Mtime = mtime; ContentHash = contentHash; Css = css; DepMtimes = deps }
        for (depPath, _) in deps do
            let fwd = dependencies.GetOrAdd(filePath, fun _ -> HashSet<string>())
            lock fwd (fun () -> fwd.Add(depPath) |> ignore)
            let rev = dependents.GetOrAdd(depPath, fun _ -> HashSet<string>())
            lock rev (fun () -> rev.Add(filePath) |> ignore)

    /// True when any @use dependency of `file` has a different mtime than the
    /// one recorded when `file` was last compiled.
    let dependenciesChanged (filePath: string) : bool =
        match entries.TryGetValue(filePath) with
        | true, e ->
            e.DepMtimes |> List.exists (fun (depPath, recordedMtime) ->
                File.Exists(depPath) && File.GetLastWriteTimeUtc(depPath).Ticks <> recordedMtime)
        | _ -> false

    /// All files that (transitively) depend on `changed` — the minimal set to
    /// recompile when `changed` is modified.
    let getAffectedFiles (changed: string) : string list =
        let result = HashSet<string>()
        let visited = HashSet<string>()
        let queue = Queue<string>()
        queue.Enqueue(changed)
        while queue.Count > 0 do
            let cur = queue.Dequeue()
            if visited.Add(cur) then
                match dependents.TryGetValue(cur) with
                | true, ds ->
                    for d in ds do
                        result.Add(d) |> ignore
                        if not (visited.Contains d) then queue.Enqueue(d)
                | _ -> ()
        Seq.toList result

    /// Remove a single file from the cache (e.g. after a delete).
    let invalidate (filePath: string) =
        entries.TryRemove(filePath) |> ignore

    /// Explicitly clear all in-memory entries and the dependency graphs.
    let clearCache () =
        entries.Clear()
        dependencies.Clear()
        dependents.Clear()

    /// Number of cached files.
    let count () = entries.Count

    // ── Disk persistence ───────────────────────────────────

    // Write to a temp file first, then move into place so a crash mid-write
    // never leaves a truncated cache that would silently drop entries.
    let private atomicWrite (path: string) (write: StreamWriter -> unit) =
        let tmp = path + ".tmp"
        let mutable ok = false
        try
            use writer = new StreamWriter(tmp, false, Encoding.UTF8)
            write writer
            writer.Flush()
            ok <- true
        with ex ->
            eprintfn "[Zest] WARN: Failed to write ZCSS cache %s: %s" path ex.Message
            try File.Delete(tmp) with _ -> ()
        if ok then
            try
                File.Move(tmp, path, overwrite = true)
            with ex ->
                eprintfn "[Zest] WARN: Failed to finalise ZCSS cache %s: %s" path ex.Message
                try File.Delete(tmp) with _ -> ()

    // CSS and dependency data are Base64-encoded so multi-line output and
    // path characters never collide with the tab/newline field separators.
    let private b64 (s: string) = Convert.ToBase64String(Encoding.UTF8.GetBytes(s))
    let private unb64 (s: string) = Encoding.UTF8.GetString(Convert.FromBase64String(s))

    /// Persist all entries to disk (atomic write). Entries whose source file
    /// no longer exists are skipped so deleted sources cannot resurrect.
    let save (cacheFile: string) =
        try
            let dir = Path.GetDirectoryName(cacheFile)
            if not (String.IsNullOrEmpty dir) then Directory.CreateDirectory(dir) |> ignore
            atomicWrite cacheFile (fun writer ->
                writer.WriteLine(sprintf "# zest-zcss-cache v%d" CACHE_FORMAT_VERSION)
                for kv in entries do
                    if File.Exists(kv.Key) then
                        let depsPart =
                            kv.Value.DepMtimes
                            |> List.map (fun (p, m) -> b64 p + "|" + string m)
                            |> String.concat ","
                        writer.Write(kv.Key); writer.Write('\t')
                        writer.Write(kv.Value.Mtime); writer.Write('\t')
                        writer.Write(kv.Value.ContentHash); writer.Write('\t')
                        writer.Write(b64 kv.Value.Css); writer.Write('\t')
                        writer.WriteLine(depsPart))
        with ex ->
            eprintfn "[Zest] WARN: Failed to save ZCSS cache: %s" ex.Message

    /// Load entries from disk, skipping lines whose source file is gone.
    let load (cacheFile: string) =
        try
            if File.Exists cacheFile then
                use reader = new StreamReader(cacheFile, Encoding.UTF8)
                let mutable line = reader.ReadLine()
                while line <> null do
                    if not (line.StartsWith("#")) then
                        let parts = line.Split('\t')
                        if parts.Length >= 4 then
                            match Int64.TryParse(parts.[1]) with
                            | true, mtime when File.Exists(parts.[0]) ->
                                let deps =
                                    if parts.Length >= 5 && parts.[4].Length > 0 then
                                        parts.[4].Split(',')
                                        |> Array.choose (fun d ->
                                            let idx = d.LastIndexOf('|')
                                            if idx > 0 then
                                                let mutable m = 0L
                                                if Int64.TryParse(d.[idx+1..], &m) then Some(unb64 d.[..idx-1], m) else None
                                            else None)
                                        |> Array.toList
                                    else []
                                entries.[parts.[0]] <-
                                    { Mtime = mtime; ContentHash = parts.[2]
                                      Css = try unb64 parts.[3] with _ -> ""
                                      DepMtimes = deps }
                                // Rebuild the dependency graphs from the entry.
                                for (depPath, _) in deps do
                                    let rev = dependents.GetOrAdd(depPath, fun _ -> HashSet<string>())
                                    lock rev (fun () -> rev.Add(parts.[0]) |> ignore)
                            | _ -> ()
                    line <- reader.ReadLine()
        with ex ->
            eprintfn "[Zest] WARN: Failed to load ZCSS cache: %s" ex.Message

    /// Delete the on-disk cache file and clear in-memory entries.
    let clearDiskCache (cacheFile: string) =
        clearCache ()
        try if File.Exists(cacheFile) then File.Delete(cacheFile)
        with ex -> eprintfn "[Zest] WARN: Could not delete %s: %s" cacheFile ex.Message
