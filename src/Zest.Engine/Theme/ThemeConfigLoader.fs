// ThemeConfigLoader.fs
//
// Loads a theme's declarative _theme.toml manifest. The manifest replaces the
// old _theme.zest.fsx scripting approach: it is simpler, avoids FSI evaluation
// at build time, and matches how _config.toml is handled.
//
// The loaded data is merged into global data as `site.theme.*`, and top-level
// scalar keys become template-accessible globals (e.g. `{{ theme.author }}`),
// mirroring how _init.zest.fsx addGlobal works.
//
// Supported sections:
//   [theme]       — metadata (name, version, author, desc); [meta] also accepted
//   [data]        — arbitrary key/value pairs exposed as globals
//   [filters]     — filter declarations (name = "module::function")
//   [[afterBuild]] — post-build command array entries
//
// Invariant: a missing or malformed file yields an empty manifest rather than
// an exception, so a broken theme never aborts the build.
//
// Dependencies: Tomlyn, System, System.Collections.Generic, System.IO

namespace Zest.Engine

open System
open System.Collections.Generic
open System.IO
open Tomlyn
open Tomlyn.Model

/// Theme-level configuration loaded from _theme.toml.
type ThemeManifest = {
    /// Theme metadata (name, version, author, desc).
    Meta: IDictionary<string, obj>
    /// Arbitrary data section — exposed as global template data.
    Data: IDictionary<string, obj>
    /// Filter declarations: filter name → "module::function" spec.
    Filters: IDictionary<string, string>
    /// After-build commands: list of (command, args).
    AfterBuild: (string * string) list
}

module ThemeConfigLoader =

    /// Recursively convert Tomlyn container objects to plain .NET types.
    /// (Same logic as BuildData.tomlToNative, kept local for independence.)
    let rec private tomlToNative (v: obj) : obj =
        match v with
        | :? TomlTable as t ->
            let d = Dictionary<string, obj>(t.Count)
            for kv in t do d.[kv.Key] <- tomlToNative kv.Value
            d :> obj
        | :? TomlArray as a ->
            a |> Seq.map tomlToNative |> Array.ofSeq |> box
        | :? TomlTableArray as ta ->
            ta |> Seq.map tomlToNative |> Array.ofSeq |> box
        | null -> null
        | _ -> v

    /// Empty manifest (used when _theme.toml is absent or fails to parse).
    let private emptyManifest = {
        Meta = dict [] :> IDictionary<string, obj>
        Data = dict [] :> IDictionary<string, obj>
        Filters = dict [] :> IDictionary<string, string>
        AfterBuild = []
    }

    /// Try to get a value from a TomlTable safely.
    let private tryGet (table: TomlTable) (key: string) : obj option =
        match table.TryGetValue(key) with
        | true, v when not (isNull v) -> Some v
        | _ -> None

    /// Read an entry as a string, or None when it is absent or null.
    let private tryGetString (table: TomlTable) (key: string) : string option =
        match tryGet table key with
        | Some v -> Some (v.ToString())
        | None -> None

    /// Load _theme.toml from the given theme directory.
    /// Returns an empty manifest if the file doesn't exist or fails to parse.
    let load (themeDir: string) : ThemeManifest =
        let themeTomlPath = Path.Combine(themeDir, "_theme.toml")
        if not (File.Exists themeTomlPath) then emptyManifest
        else
            try
                let model = Toml.ToModel(File.ReadAllText(themeTomlPath))
                if isNull model then emptyManifest
                else
                    // ── [theme] / [meta] section ──
                    // [theme] is preferred; [meta] is a fallback.
                    let metaDict = Dictionary<string, obj>(model.Count)
                    let metaTable =
                        [ "theme"; "meta" ]
                        |> List.tryPick (fun key ->
                            match tryGet model key with
                            | Some (:? TomlTable as t) -> Some t
                            | _ -> None)
                    match metaTable with
                    | Some mt ->
                        for kv in mt do metaDict.[kv.Key] <- tomlToNative kv.Value
                    | None -> ()

                    // Also surface top-level scalar keys (name, version, etc.)
                    // as meta if not already in a [theme]/[meta] table.
                    for kv in model do
                        if not (metaDict.ContainsKey kv.Key) then
                            match kv.Value with
                            | :? TomlTable | :? TomlTableArray -> ()  // skip nested tables
                            | _ -> metaDict.[kv.Key] <- tomlToNative kv.Value

                    // ── [data] section ──
                    let dataDict =
                        match tryGet model "data" with
                        | Some (:? TomlTable as dt) ->
                            let d = Dictionary<string, obj>(dt.Count)
                            for kv in dt do d.[kv.Key] <- tomlToNative kv.Value
                            d
                        | _ -> Dictionary<string, obj>()

                    // ── [filters] section ──
                    let filtersDict =
                        match tryGet model "filters" with
                        | Some (:? TomlTable as ft) ->
                            let d = Dictionary<string, string>(ft.Count)
                            for kv in ft do
                                d.[kv.Key] <- if isNull kv.Value then "" else kv.Value.ToString()
                            d
                        | _ -> Dictionary<string, string>()

                    // ── [[afterBuild]] array of tables ──
                    let afterBuild =
                        match tryGet model "afterBuild" with
                        | Some (:? TomlTableArray as arr) ->
                            arr
                            |> Seq.map (fun t ->
                                let cmd =
                                    tryGetString t "cmd"
                                    |> Option.orElse (tryGetString t "command")
                                    |> Option.defaultValue ""
                                let args =
                                    tryGetString t "args"
                                    |> Option.orElse (tryGetString t "arguments")
                                    |> Option.defaultValue ""
                                cmd, args)
                            |> Seq.toList
                        | _ -> []

                    { Meta = metaDict; Data = dataDict; Filters = filtersDict; AfterBuild = afterBuild }
            with ex ->
                eprintfn "[Zest] Failed to parse _theme.toml: %s" ex.Message
                emptyManifest
