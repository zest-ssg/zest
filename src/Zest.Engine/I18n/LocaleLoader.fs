// LocaleLoader.fs
//
// Loads locale TOML files from _locales/ directory and provides
// key-based translations for templates and DSL scripts.
//
// Dependencies: Tomlyn, System.IO, System.Collections.Generic

namespace Zest.Engine.I18n

open System.IO
open System.Collections.Generic

/// <summary>
/// Loads locale TOML files from _locales/ directory and provides
/// key-based translations for templates and DSL scripts.
/// </summary>
module LocaleLoader =

    /// <summary>
    /// Load a single locale file (_locales/{lang}.toml) as a flat dictionary.
    /// Theme locales are loaded first, then project locales overwrite.
    /// </summary>
    let private loadLocaleFile (path: string) : IDictionary<string, string> =
        let dict = Dictionary<string, string>()
        if not (File.Exists path) then dict :> _
        else
            try
                let text = File.ReadAllText(path)
                let model = Tomlyn.Toml.ToModel(text)
                // Flatten nested tables: [nav] home = "Home" → "nav.home" = "Home"
                let rec flatten (prefix: string) (table: Tomlyn.Model.TomlTable) =
                    for kv in table do
                        match kv.Value with
                        | :? Tomlyn.Model.TomlTable as sub ->
                            flatten (if prefix = "" then kv.Key else prefix + "." + kv.Key) sub
                        | _ ->
                            let key = if prefix = "" then kv.Key else prefix + "." + kv.Key
                            dict.[key] <- kv.Value.ToString()
                if model <> null then flatten "" model
                dict :> _
            with _ -> dict :> _

    /// <summary>
    /// Load all locale files from _locales/ and theme _locales/.
    /// Returns a map of language code → flat key/value dictionary.
    /// </summary>
    let loadLocales (projectRoot: string) (themeDir: string option) : IDictionary<string, IDictionary<string, string>> =
        let result = Dictionary<string, IDictionary<string, string>>()

        // Load theme locales first (fallback)
        match themeDir with
        | Some td ->
            let themeLocalesDir = Path.Combine(td, "_locales")
            if Directory.Exists themeLocalesDir then
                for file in Directory.GetFiles(themeLocalesDir, "*.toml") do
                    let lang = Path.GetFileNameWithoutExtension(file)
                    result.[lang] <- loadLocaleFile file
        | None -> ()

        // Load project locales (overwrite theme)
        let localesDir = Path.Combine(projectRoot, "_locales")
        if Directory.Exists localesDir then
            for file in Directory.GetFiles(localesDir, "*.toml") do
                let lang = Path.GetFileNameWithoutExtension(file)
                let projectDict = loadLocaleFile file
                match result.TryGetValue(lang) with
                | true, baseDict ->
                    // Merge: project keys overwrite theme keys
                    for kv in projectDict do
                        baseDict.[kv.Key] <- kv.Value
                | _ ->
                    result.[lang] <- projectDict

        result :> _

    /// <summary>
    /// Resolve a translation key for a given language.
    /// Falls back to config.Language if no explicit lang is provided.
    /// </summary>
    let translate (locales: IDictionary<string, IDictionary<string, string>>)
                  (defaultLang: string)
                  (key: string)
                  (lang: string option) : string =
        let lang = lang |> Option.defaultValue defaultLang
        match locales.TryGetValue(lang) with
        | true, dict ->
            match dict.TryGetValue(key) with
            | true, v -> v
            | _ ->
                // Fallback to first locale that has the key
                locales
                |> Seq.tryPick (fun kv ->
                    if kv.Key = lang then None
                    else match kv.Value.TryGetValue(key) with true, v -> Some v | _ -> None)
                |> Option.defaultValue key
        | _ -> key

    /// <summary>
    /// Get all available locale codes.
    /// </summary>
    let availableLocales (locales: IDictionary<string, IDictionary<string, string>>) =
        locales.Keys |> Seq.toList
