namespace Zest.Engine

open System.Collections.Concurrent
open System.IO
open System.Threading.Tasks
open Zest.Engine.Zcss

/// Asset (assets/) copying and ZCSS compilation — fully parallelized.
module BuildAssets =

    let internal copyAssets (projectRoot: string) (outputDir: string) =
        let src = Path.Combine(projectRoot, "assets")
        if not (Directory.Exists src) then 0
        else
            let dst = Path.Combine(outputDir, "assets")
            Directory.CreateDirectory(dst) |> ignore
            let createdDirs = ConcurrentDictionary<string, byte>()
            let ensureDir (target: string) =
                let dir = Path.GetDirectoryName(target)
                if dir <> null then
                    createdDirs.GetOrAdd(dir, fun _ ->
                        Directory.CreateDirectory(dir) |> ignore
                        1uy) |> ignore
            let mutable n = 0
            // Single file system traversal, parallel processing
            let files = Directory.GetFiles(src, "*", SearchOption.AllDirectories)
            Parallel.ForEach(files, fun (file: string) ->
                let ext = Path.GetExtension(file).ToLowerInvariant()
                let rel = Path.GetRelativePath(src, file)
                let srcLastWrite = File.GetLastWriteTimeUtc(file)
                if ext = FileExtensions.Zcss then
                    let target = Path.Combine(dst, Path.ChangeExtension(rel, FileExtensions.Css))
                    ensureDir target
                    if not (File.Exists target) || srcLastWrite > File.GetLastWriteTimeUtc(target) then
                        Processor.processFileTo file target |> ignore
                else
                    let target = Path.Combine(dst, rel)
                    ensureDir target
                    if not (File.Exists target) || srcLastWrite > File.GetLastWriteTimeUtc(target) then
                        File.Copy(file, target, overwrite = true)
                System.Threading.Interlocked.Increment(&n) |> ignore) |> ignore
            n
