namespace Zest.Compiler.Build
open Zest.Compiler.Model
open System.Collections.Concurrent
open System.IO
open System.Threading.Tasks
open Zest.Compiler.Zcss

/// Asset (assets/) copying and ZCSS compilation — fully parallelized.
module AssetWriter =

    /// Copy assets/ to the output directory, compiling .zcss to .css on the way.
    let internal copyAssets (projectRoot: string) (outputDir: string) =
        let src = Path.Combine(projectRoot, "assets")
        if not (Directory.Exists src) then 0
        else
            let dst = Path.Combine(outputDir, "assets")
            Directory.CreateDirectory(dst) |> ignore
            // A stylesheet compiles to CSS that depends on the files it `@use`s,
            // so the compile cache has to survive between runs: editing a
            // partial does not move the timestamp of the entry sheet importing
            // it, and only the timestamps recorded here can tell that the two
            // are out of step. Without this the published main.css goes stale.
            let cssCacheFile = Path.Combine(dst, ".zcss-cache.log")
            Cache.load cssCacheFile
            let createdDirs = ConcurrentDictionary<string, byte>()
            let ensureDir (target: string) =
                let dir = Path.GetDirectoryName(target)
                if dir <> null then
                    createdDirs.GetOrAdd(dir, fun _ ->
                        Directory.CreateDirectory(dir) |> ignore
                        1uy) |> ignore
            let mutable n = 0
            // Single file system traversal, parallel processing.
            // Directory.GetFiles(AllDirectories) throws on an unreadable
            // sub-directory (and would follow symlinks out of the tree), which
            // would abort the whole asset copy. Walk manually, skipping
            // reparse points and swallowing per-directory failures.
            let enumerateFiles (root: string) =
                let results = ResizeArray<string>()
                let rec walk (dir: string) =
                    try
                        for f in Directory.EnumerateFiles dir do
                            let info = FileInfo f
                            if not (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) then
                                results.Add f
                    with _ -> ()
                    try
                        for d in Directory.EnumerateDirectories dir do
                            let info = DirectoryInfo d
                            if not (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) then walk d
                    with _ -> ()
                walk root
                results.ToArray()
            let files = enumerateFiles src
            Parallel.ForEach(files, fun (file: string) ->
                let ext = Path.GetExtension(file).ToLowerInvariant()
                // `_name.zcss` is a partial: it reaches the output only inlined
                // into the entry sheet that imports it with `@use`. Skipping it
                // keeps `_name.css` out of the published assets.
                let isPartial = ext = FileTypes.Zcss && FileTypes.isZcssPartial file
                if not isPartial then
                    let rel = Path.GetRelativePath(src, file)
                    let srcLastWrite = File.GetLastWriteTimeUtc(file)
                    if ext = FileTypes.Zcss then
                        let target = Path.Combine(dst, Path.ChangeExtension(rel, FileTypes.Css))
                        ensureDir target
                        let stale =
                            not (File.Exists target)
                            || srcLastWrite > File.GetLastWriteTimeUtc(target)
                            || Cache.dependenciesChanged file
                        if stale then
                            Zcss.processFileTo file target |> ignore
                    else
                        let target = Path.Combine(dst, rel)
                        ensureDir target
                        if not (File.Exists target) || srcLastWrite > File.GetLastWriteTimeUtc(target) then
                            try File.Copy(file, target, overwrite = true)
                            with ex -> eprintfn "[Zest] WARN: failed to copy asset '%s': %s" file ex.Message
                    System.Threading.Interlocked.Increment(&n) |> ignore) |> ignore
            // Persist the dependency timestamps so the next run can tell a
            // changed partial from an untouched one.
            Cache.save cssCacheFile
            n
