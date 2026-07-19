namespace Zest.Engine

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open Tomlyn.Model

/// Global data (_data/*.toml) loading and caching — single-pass file traversal.
module BuildData =

    let private globalDataCache = ConcurrentDictionary<string, struct(DateTime * IDictionary<string, obj>)>()

    /// Recursively convert Tomlyn container objects to plain .NET types so they
    /// are directly iterable/traversable in Nunjucks and F# scripts. In Tomlyn
    /// 0.17 scalars are already native (`string`/`int64`/`double`/`bool`), so
    /// only `TomlTable`/`TomlArray`/`TomlTableArray` need unwrapping. Fixes
    /// MIGRATION_NOTES §1.2/§1.3 (TOML arrays/tables lost when injected).
    let rec private tomlToNative (v: obj) : obj =
        match v with
        | :? TomlTable as t ->
            // Preserve nested structure as a mutable IDictionary so Nunjucks
            // dotted access (`site.nav.items`) and `{% for %}` iteration work.
            let d = Dictionary<string, obj>()
            for kv in t do d.[kv.Key] <- tomlToNative kv.Value
            d :> obj
        | :? TomlArray as a ->
            a |> Seq.map tomlToNative |> Array.ofSeq |> box
        | :? TomlTableArray as ta ->
            // `[[tables]]` → array of dictionaries.
            ta |> Seq.map tomlToNative |> Array.ofSeq |> box
        | null -> null
        | _ -> v  // native scalar (string/int64/double/bool/…): leave untouched

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
                            // Flatten with dotted keys (site.nav.items) AND keep
                            // the whole table (site.nav) — both as native types
                            // so Nunjucks can traverse/iterate either form.
                            for kv in model do dict.[name + "." + kv.Key] <- tomlToNative kv.Value
                            dict.[name] <- tomlToNative model
                    with ex -> eprintfn "[Zest] Failed to load data '%s': %s" file ex.Message
            let result = dict :> IDictionary<string, obj>
            globalDataCache.[cacheKey] <- struct(mtime, result)
            result
