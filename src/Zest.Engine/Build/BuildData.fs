namespace Zest.Engine

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO

/// Global data (_data/*.toml) loading and caching — single-pass file traversal.
module BuildData =

    let private globalDataCache = ConcurrentDictionary<string, struct(DateTime * IDictionary<string, obj>)>()

    let internal loadGlobalData (dataDir: string) : IDictionary<string, obj> =
        let cacheKey = dataDir
        let mtime =
            if not (Directory.Exists dataDir) then DateTime.MinValue
            else
                Directory.EnumerateFiles(dataDir, "*.toml", SearchOption.AllDirectories)
                |> Seq.map (fun f -> File.GetLastWriteTimeUtc(f).Ticks)
                |> Seq.append [Directory.GetLastWriteTimeUtc(dataDir).Ticks]
                |> Seq.max |> DateTime
        match globalDataCache.TryGetValue(cacheKey) with
        | true, (cachedMtime, cachedData) when cachedMtime = mtime -> cachedData
        | _ ->
            let dict = Dictionary<string, obj>()
            if Directory.Exists dataDir then
                // Single traversal: enumerate once, use for both reading and loading
                for file in Directory.EnumerateFiles(dataDir, "*.toml", SearchOption.AllDirectories) do
                    try
                        let name  = Path.GetFileNameWithoutExtension(file)
                        let model = Tomlyn.Toml.ToModel(File.ReadAllText(file))
                        if model <> null then
                            for kv in model do dict.[name + "." + kv.Key] <- kv.Value
                            dict.[name] <- model :> obj
                    with ex -> eprintfn "[Zest] Failed to load data '%s': %s" file ex.Message
            let result = dict :> IDictionary<string, obj>
            globalDataCache.[cacheKey] <- struct(mtime, result)
            result
