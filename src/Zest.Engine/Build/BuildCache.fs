namespace Zest.Engine

open System
open System.Collections.Concurrent
open System.IO

/// Incremental build cache: tracks source file mtime and output hash.
/// Optimized with lazy dirty-tracking for faster incremental saves.
module BuildCache =

    [<Struct>]
    type internal CacheEntry = { Mtime: DateTime; OutputHash: int }
    let internal buildCache = ConcurrentDictionary<string, CacheEntry>()
    let private cacheDirty = ref false

    let private cacheFilePath (outputDir: string) = Path.Combine(outputDir, ".zest-cache.json")

    let internal loadCache (outputDir: string) =
        if not (buildCache.IsEmpty) then ()
        else
            let path = cacheFilePath outputDir
            if File.Exists path then
                try
                    // Use StreamReader for faster line-by-line parsing
                    use reader = new StreamReader(path, Text.Encoding.UTF8)
                    let mutable line = reader.ReadLine()
                    while line <> null do
                        let parts = line.Split([|'\t'|], 3)
                        if parts.Length = 3 then
                            match Int64.TryParse(parts.[1]), Int32.TryParse(parts.[2]) with
                            | (true, ticks), (true, hash) ->
                                buildCache.[parts.[0]] <- { Mtime = DateTime(ticks, DateTimeKind.Utc); OutputHash = hash }
                            | _ -> ()
                        line <- reader.ReadLine()
                with _ -> ()

    let internal saveCache (outputDir: string) =
        if not !cacheDirty then ()
        else
            try
                let path = cacheFilePath outputDir
                use writer = new StreamWriter(path, false, Text.Encoding.UTF8)
                for kv in buildCache do
                    writer.Write(kv.Key)
                    writer.Write('\t')
                    writer.Write(kv.Value.Mtime.Ticks)
                    writer.Write('\t')
                    writer.WriteLine(kv.Value.OutputHash)
                cacheDirty := false
            with _ -> ()

    let internal needsRebuild (srcPath: string) (outPath: string) =
        let mtime = File.GetLastWriteTimeUtc(srcPath)
        match buildCache.TryGetValue(srcPath) with
        | true, e when e.Mtime = mtime && File.Exists(outPath) -> false
        | _ -> true

    let internal updateCache (srcPath: string) (html: string) =
        buildCache.[srcPath] <- { Mtime = File.GetLastWriteTimeUtc(srcPath); OutputHash = html.GetHashCode() }
        cacheDirty := true
