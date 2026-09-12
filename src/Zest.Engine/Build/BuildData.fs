namespace Zest.Engine

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open System.Text.Json
open Tomlyn.Model

/// Global data (_data/*.toml and _data/*.json) loading and caching —
/// single-pass file traversal per format.
module BuildData =

    let private globalDataCache = ConcurrentDictionary<string, struct(DateTime * IDictionary<string, obj>)>()

    /// Recursively convert Tomlyn container objects to plain .NET types so they
    /// are directly iterable/traversable in Nunjucks and F# scripts. In Tomlyn
    /// 0.17 scalars are already native (`string`/`int64`/`double`/`bool`), so
    /// only `TomlTable`/`TomlArray`/`TomlTableArray` need unwrapping.
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

    /// Convert a JSON document into plain .NET types (Dictionary/array/scalar).
    /// JsonElement values cannot be traversed by the Nunjucks renderer, so every
    /// node must be unwrapped the same way TOML containers are.
    let rec private jsonToNative (el: JsonElement) : obj =
        match el.ValueKind with
        | JsonValueKind.String  -> box (el.GetString())
        | JsonValueKind.Number  ->
            match el.TryGetInt64() with
            | true, l -> box l
            | false, _ -> box (el.GetDouble())
        | JsonValueKind.True    -> box true
        | JsonValueKind.False   -> box false
        | JsonValueKind.Null    -> null
        | JsonValueKind.Array   ->
            el.EnumerateArray() |> Seq.map jsonToNative |> Array.ofSeq |> box
        | JsonValueKind.Object  ->
            let d = Dictionary<string, obj>()
            for p in el.EnumerateObject() do d.[p.Name] <- jsonToNative p.Value
            d :> obj
        | _ -> box (el.ToString())

    /// Cache key and validity timestamp for one data directory. The timestamp
    /// is the newest write time across both supported formats and the directory
    /// itself, so adding or editing either file kind invalidates the snapshot.
    let private computeMtime (dataDir: string) : DateTime =
        if not (Directory.Exists dataDir) then DateTime.MinValue
        else
            let files =
                Seq.append
                    (Directory.EnumerateFiles(dataDir, "*.toml", SearchOption.AllDirectories))
                    (Directory.EnumerateFiles(dataDir, "*.json", SearchOption.AllDirectories))
            files
            |> Seq.map (fun f -> File.GetLastWriteTimeUtc(f).Ticks)
            |> Seq.append [ Directory.GetLastWriteTimeUtc(dataDir).Ticks ]
            |> Seq.max |> DateTime

    /// Load one TOML file into the flat dictionary under both the file name
    /// (`site`) and its dotted keys (`site.title`).
    let private loadTomlFile (file: string) (dict: Dictionary<string, obj>) =
        let name  = Path.GetFileNameWithoutExtension(file)
        let model = Tomlyn.Toml.ToModel(File.ReadAllText(file))
        if model <> null then
            for kv in model do dict.[name + "." + kv.Key] <- tomlToNative kv.Value
            dict.[name] <- tomlToNative model

    /// Load one JSON file into the flat dictionary under both the file name
    /// and its dotted top-level keys. JSON data files describe one object per
    /// file (e.g. `analytics.json` → `analytics.umami.id`), matching the
    /// per-file namespace convention used by TOML data.
    let private loadJsonFile (file: string) (dict: Dictionary<string, obj>) =
        let name = Path.GetFileNameWithoutExtension(file)
        use doc = JsonDocument.Parse(File.ReadAllText(file))
        let native = jsonToNative doc.RootElement
        match native with
        | :? IDictionary<string, obj> as table ->
            for kv in table do dict.[name + "." + kv.Key] <- kv.Value
            dict.[name] <- native
        | _ ->
            // A non-object JSON root still enters under the file name so scalar
            // or array data files are not silently dropped.
            dict.[name] <- native

    let internal loadGlobalData (dataDir: string) : IDictionary<string, obj> =
        let cacheKey = dataDir
        let mtime = computeMtime dataDir
        match globalDataCache.TryGetValue(cacheKey) with
        | true, (cachedMtime, cachedData) when cachedMtime = mtime -> cachedData
        | _ ->
            let dict = Dictionary<string, obj>()
            if Directory.Exists dataDir then
                for file in Directory.EnumerateFiles(dataDir, "*.toml", SearchOption.AllDirectories) do
                    try loadTomlFile file dict
                    with ex -> eprintfn "[Zest] Failed to load data '%s': %s" file ex.Message
                for file in Directory.EnumerateFiles(dataDir, "*.json", SearchOption.AllDirectories) do
                    try loadJsonFile file dict
                    with ex -> eprintfn "[Zest] Failed to load data '%s': %s" file ex.Message
            let result = dict :> IDictionary<string, obj>
            globalDataCache.[cacheKey] <- struct(mtime, result)
            result
