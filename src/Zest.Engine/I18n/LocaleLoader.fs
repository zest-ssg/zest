// LocaleLoader.fs
//
// Loads locale files (_locales/{lang}.toml and _locales/{lang}.json) and
// provides key-based translations with a deterministic fallback chain and
// {name} parameter interpolation for templates and DSL scripts.
//
// Invariants:
//   - Keys are dot-separated; nested TOML/JSON objects flatten to the same shape.
//   - Theme locales load first, then project locales merge over them per key.
//
// Dependencies: Tomlyn, System.Text.Json, System.IO, System.Collections.Generic

namespace Zest.Engine.I18n

open System.IO
open System.Collections.Generic
open System.Text
open System.Text.Json

/// <summary>
/// Loads locale files and resolves translation keys for templates.
/// </summary>
module LocaleLoader =

    /// <summary>
    /// Flatten nested TOML tables ([nav] home = "Home" → "nav.home" = "Home").
    /// Array values are joined so a menu list renders as one string.
    /// </summary>
    let rec flattenToml (prefix: string) (table: Tomlyn.Model.TomlTable) (dict: Dictionary<string, string>) =
        let keyOf (name: string) = if prefix = "" then name else prefix + "." + name
        for kv in table do
            match kv.Value with
            | :? Tomlyn.Model.TomlTable as sub -> flattenToml (keyOf kv.Key) sub dict
            | :? Tomlyn.Model.TomlArray as arr ->
                let joined = [ for i in 0 .. arr.Count - 1 -> arr.[i].ToString() ] |> String.concat ", "
                dict.[keyOf kv.Key] <- joined
            | _ -> dict.[keyOf kv.Key] <- kv.Value.ToString()

    /// <summary>
    /// Flatten a JSON locale document into a flat key/value dictionary,
    /// mirroring the TOML loader's dot-separated nesting convention.
    /// </summary>
    let rec flattenJson (node: JsonElement) (prefix: string) (dict: Dictionary<string, string>) =
        match node.ValueKind with
        | JsonValueKind.Object ->
            for prop in node.EnumerateObject() do
                flattenJson prop.Value (if prefix = "" then prop.Name else prefix + "." + prop.Name) dict
        | JsonValueKind.Array ->
            // Read strings verbatim; JsonElement.ToString() would re-quote them.
            let items =
                node.EnumerateArray()
                |> Seq.map (fun e ->
                    match e.ValueKind with
                    | JsonValueKind.String -> e.GetString()
                    | _ -> e.ToString())
                |> String.concat ", "
            dict.[prefix] <- items
        | JsonValueKind.String ->
            let v = node.GetString()
            if not (isNull v) then dict.[prefix] <- v
        | _ -> dict.[prefix] <- node.ToString()   // numbers / booleans / null

    /// <summary>
    /// Parse a single locale file. The extension decides the format:
    /// .json uses System.Text.Json, anything else is parsed as TOML.
    /// </summary>
    let private loadLocaleFile (path: string) : Dictionary<string, string> =
        let dict = Dictionary<string, string>()
        if File.Exists path then
            if path.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase) then
                use doc = JsonDocument.Parse(File.ReadAllText(path))
                flattenJson doc.RootElement "" dict
            else
                let model = Tomlyn.Toml.ToModel(File.ReadAllText(path))
                if not (isNull model) then flattenToml "" model dict
        dict

    /// <summary>
    /// Load all locale files (.toml / .json) from one directory into the
    /// result map. When a language already exists (e.g. theme locale), new
    /// keys merge in so later directories win per-key. Parse failures are
    /// reported instead of silently swallowed.
    /// </summary>
    let private loadLocaleDir (dir: string) (result: Dictionary<string, IDictionary<string, string>>) =
        if Directory.Exists dir then
            for file in Directory.EnumerateFiles(dir) do
                let ext = Path.GetExtension(file).ToLowerInvariant()
                if ext = ".toml" || ext = ".json" then
                    let lang = Path.GetFileNameWithoutExtension(file)
                    try
                        let dict = loadLocaleFile file
                        match result.TryGetValue lang with
                        | true, existing -> for kv in dict do existing.[kv.Key] <- kv.Value
                        | _ -> result.[lang] <- dict :> IDictionary<string, string>
                    with ex ->
                        eprintfn "[Zest] WARN: Failed to parse locale file %s: %s" file ex.Message

    /// <summary>
    /// Load all locale files from _locales/ and theme _locales/.
    /// Returns a map of language code → flat key/value dictionary.
    /// Theme locales load first, then project locales overwrite them.
    /// </summary>
    let loadLocales (projectRoot: string) (themeDir: string option) : IDictionary<string, IDictionary<string, string>> =
        // The inner dictionaries are built directly as IDictionary values so no
        // second map and copy is needed to widen the result.
        let result = Dictionary<string, IDictionary<string, string>>()

        // Theme locales first (fallback layer for project overrides).
        match themeDir with
        | Some td -> loadLocaleDir (Path.Combine(td, "_locales")) result
        | None -> ()

        // Project locales overwrite theme keys.
        loadLocaleDir (Path.Combine(projectRoot, "_locales")) result

        result :> IDictionary<string, IDictionary<string, string>>

    /// <summary>
    /// Replace {name} placeholders in a translation with argument values.
    /// Unknown placeholders are left untouched so typos surface in output.
    /// </summary>
    let formatText (text: string) (args: IDictionary<string, string>) : string =
        if isNull text || args.Count = 0 then text
        else
            let sb = StringBuilder(text.Length)
            let mutable i = 0
            // Start of the current literal run. Runs are appended in bulk so a
            // 100-character template costs a few appends instead of 100.
            let mutable runStart = 0
            while i < text.Length do
                if text.[i] = '{' then
                    let closeIdx = text.IndexOf('}', i + 1)
                    if closeIdx > i + 1 then
                        let name = text.Substring(i + 1, closeIdx - i - 1).Trim()
                        match args.TryGetValue name with
                        | true, v ->
                            sb.Append(text, runStart, i - runStart) |> ignore
                            sb.Append(v) |> ignore
                            i <- closeIdx + 1
                            runStart <- i
                        | _ -> i <- i + 1   // Unknown placeholder: keep it in the run.
                    else
                        i <- i + 1
                else
                    i <- i + 1
            sb.Append(text, runStart, text.Length - runStart) |> ignore
            sb.ToString()

    /// <summary>
    /// Resolve a translation key with {name} interpolation.
    /// Fallback chain: requested language → default language → the key itself.
    /// </summary>
    /// <param name="locales">Language map built by loadLocales.</param>
    /// <param name="defaultLang">Language used when no explicit lang matches.</param>
    /// <param name="key">Dot-separated translation key.</param>
    /// <param name="lang">Explicit language; defaults to defaultLang when omitted.</param>
    /// <param name="args">Interpolation values for {name} placeholders.</param>
    let translateWithArgs (locales: IDictionary<string, IDictionary<string, string>>)
                          (defaultLang: string)
                          (key: string)
                          (lang: string option)
                          (args: IDictionary<string, string>) : string =
        let requested = lang |> Option.defaultValue defaultLang
        let lookup (langKey: string) =
            match locales.TryGetValue langKey with
            | true, dict ->
                match dict.TryGetValue key with
                | true, v -> Some v
                | _ -> None
            | _ -> None
        // Prefer the requested language, then the site default, then the key.
        let resolved =
            match lookup requested with
            | Some v -> v
            | _ when requested <> defaultLang ->
                match lookup defaultLang with
                | Some v -> v
                | _ -> key
            | _ -> key
        formatText resolved args

    /// Shared empty argument bag so a lookup without interpolation allocates nothing.
    let private emptyArgs : IDictionary<string, string> =
        Dictionary<string, string>() :> IDictionary<string, string>

    /// <summary>
    /// Resolve a translation key for a given language without interpolation.
    /// </summary>
    let translate (locales: IDictionary<string, IDictionary<string, string>>)
                  (defaultLang: string)
                  (key: string)
                  (lang: string option) : string =
        translateWithArgs locales defaultLang key lang emptyArgs

    /// <summary>
    /// Get all available locale codes, sorted for stable ordering.
    /// </summary>
    let availableLocales (locales: IDictionary<string, IDictionary<string, string>>) : string list =
        locales.Keys |> Seq.toList |> List.sort
