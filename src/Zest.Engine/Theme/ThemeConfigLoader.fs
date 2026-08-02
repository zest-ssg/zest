namespace Zest.Engine

open System
open System.Collections.Generic
open System.IO
open Tomlyn
open Tomlyn.Model

// ============================================================
// ThemeConfigLoader — Load _theme.toml from a theme directory
// ============================================================
// Replaces the old _theme.zest.fsx approach with a declarative
// TOML config file. This is simpler, faster (no FSI evaluation),
// and consistent with how _config.toml works.
//
// The loaded data is merged into global data as `site.theme.*`
// and top-level keys become template-accessible globals (e.g.
// `{{ theme.author }}`), mirroring how _init.zest.fsx addGlobal works.
//
// Supported sections:
//   [theme]       — metadata (name, version, author, desc)
//   [data]        — arbitrary key/value pairs exposed as globals
//   [filters]     — filter declarations (name = "module::function")
//   [[afterBuild]] — post-build command array entries
// ============================================================

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
            let d = Dictionary<string, obj>()
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
        | true, v when v <> null -> Some v
        | _ -> None

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
                    // ── [meta] / [theme] section ──
                    // Accept either [meta] or [theme] as the metadata table.
                    // [theme] is preferred; [meta] is a fallback for clarity.
                    let metaDict = Dictionary<string, obj>()
                    let metaTable =
                        match tryGet model "theme" with
                        | Some (:? TomlTable as tt) -> Some tt
                        | _ ->
                            match tryGet model "meta" with
                            | Some (:? TomlTable as mt) -> Some mt
                            | _ -> None
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
                    let dataDict = Dictionary<string, obj>()
                    match tryGet model "data" with
                    | Some (:? TomlTable as dt) ->
                        for kv in dt do dataDict.[kv.Key] <- tomlToNative kv.Value
                    | _ -> ()

                    // ── [filters] section ──
                    let filtersDict = Dictionary<string, string>()
                    match tryGet model "filters" with
                    | Some (:? TomlTable as ft) ->
                        for kv in ft do
                            filtersDict.[kv.Key] <-
                                if isNull kv.Value then "" else kv.Value.ToString()
                    | _ -> ()

                    // ── [[afterBuild]] array of tables ──
                    let afterBuild =
                        match tryGet model "afterBuild" with
                        | Some (:? TomlTableArray as arr) ->
                            arr
                            |> Seq.map (fun t ->
                                let cmd =
                                    match tryGet t "cmd" with
                                    | Some c -> c.ToString()
                                    | None ->
                                        match tryGet t "command" with
                                        | Some c2 -> c2.ToString()
                                        | None -> ""
                                let args =
                                    match tryGet t "args" with
                                    | Some a -> a.ToString()
                                    | None ->
                                        match tryGet t "arguments" with
                                        | Some a2 -> a2.ToString()
                                        | None -> ""
                                cmd, args)
                            |> Seq.toList
                        | _ -> []

                    { Meta = metaDict; Data = dataDict; Filters = filtersDict; AfterBuild = afterBuild }
            with ex ->
                eprintfn "[Zest] Failed to parse _theme.toml: %s" ex.Message
                emptyManifest
