namespace Zest.Engine

open System.Collections.Generic
open System.IO
open Zest.Engine.Zcss

/// 资产（assets/）复制与 ZCSS 编译。
module BuildAssets =

    let internal copyAssets (projectRoot: string) (outputDir: string) =
        let src = Path.Combine(projectRoot, "assets")
        if not (Directory.Exists src) then 0
        else
            let dst = Path.Combine(outputDir, "assets")
            Directory.CreateDirectory(dst) |> ignore
            let createdDirs = HashSet<string>()
            let ensureDir (target: string) =
                let dir = Path.GetDirectoryName(target)
                if dir <> null && createdDirs.Add(dir) then
                    Directory.CreateDirectory(dir) |> ignore
            let mutable n = 0
            for file in Directory.GetFiles(src, "*", SearchOption.AllDirectories) do
                let ext = Path.GetExtension(file).ToLowerInvariant()
                let rel = Path.GetRelativePath(src, file)
                let srcLastWrite = File.GetLastWriteTimeUtc(file)
                if ext = ".zcss" then
                    let target = Path.Combine(dst, Path.ChangeExtension(rel, ".css"))
                    ensureDir target
                    if not (File.Exists target) || srcLastWrite > File.GetLastWriteTimeUtc(target) then
                        Processor.processFileTo file target |> ignore
                else
                    let target = Path.Combine(dst, rel)
                    ensureDir target
                    if not (File.Exists target) || srcLastWrite > File.GetLastWriteTimeUtc(target) then
                        File.Copy(file, target, overwrite = true)
                n <- n + 1
            n
