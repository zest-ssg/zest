namespace Zest.Engine

open System
open System.Collections.Concurrent
open System.IO

/// Incremental build cache: tracks source file mtime and output hash.
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
                    for line in File.ReadAllLines(path) do
                        let parts = line.Split([|'\t'|], 3)
                        if parts.Length = 3 then
                            match Int64.TryParse(parts.[1]), Int32.TryParse(parts.[2]) with
                            | (true, ticks), (true, hash) ->
                                buildCache.[parts.[0]] <- { Mtime = DateTime(ticks, DateTimeKind.Utc); OutputHash = hash }
                            | _ -> ()
                with _ -> ()

    let internal saveCache (outputDir: string) =
        if not !cacheDirty then ()
        else
            try
                let path = cacheFilePath outputDir
                let sb = Text.StringBuilder(1024 * buildCache.Count)
                for kv in buildCache do
                    sb.AppendLine(sprintf "%s\t%d\t%d" kv.Key kv.Value.Mtime.Ticks kv.Value.OutputHash) |> ignore
                File.WriteAllText(path, sb.ToString(), Text.Encoding.UTF8)
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
