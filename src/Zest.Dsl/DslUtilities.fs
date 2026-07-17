namespace Zest.Dsl

open System
open System.Text.RegularExpressions
open Tomlyn
open Tomlyn.Model

// ============================================================
// DslUtilities — Control flow, string interp, collections, math
// ============================================================

module DslUtilities =
    open Dsl

    // ── Control flow helpers ─────────────────────────────────────

    let switch_str (value: string) (cases: (string * string) list) (defaultCase: string) =
        cases
        |> List.tryFind (fun (v, _) -> v = value)
        |> Option.map snd
        |> Option.defaultValue defaultCase

    let cond_str (cases: (bool * string) list) (fallback: string) =
        cases |> List.tryFind fst |> Option.map snd |> Option.defaultValue fallback

    let chain_cond (conditions: (bool * string) list) (fallback: string) =
        conditions |> List.tryFind fst |> Option.map snd |> Option.defaultValue fallback

    let choose (cond: bool) (ifTrue: string) (ifFalse: string) =
        if cond then ifTrue else ifFalse

    // ── String interpolation ─────────────────────────────────────

    let interp (template: string) (vars: (string * string) list) =
        let dict = dict vars
        Regex.Replace(
            template,
            @"\{(\w+)\}",
            fun m ->
                match dict.TryGetValue(m.Groups.[1].Value) with
                | true, v -> v
                | _ -> m.Value
        )

    let interp_safe (template: string) (vars: (string * string) list) =
        let dict = dict vars
        Regex.Replace(
            template,
            @"\{(\w+)\}",
            fun m ->
                match dict.TryGetValue(m.Groups.[1].Value) with
                | true, v -> htmlEncode v
                | _ -> m.Value
        )

    // ── Collection helpers ───────────────────────────────────────

    let take_n (n: int) (items: string list) = items |> List.truncate n

    let skip_n (n: int) (items: string list) =
        items |> List.skip (min n items.Length)

    let filter_by (pred: string -> bool) (items: string list) =
        items |> List.filter pred

    let map_by (f: string -> string) (items: string list) =
        items |> List.map f

    let group_by (keyFn: string -> string) (items: string list) =
        items
        |> List.groupBy keyFn
        |> List.map (fun (k, g) -> k, List.ofSeq g)

    let chunk (size: int) (items: string list) =
        items |> List.chunkBySize size

    let intersperse_str (sep: string) (items: string list) =
        items |> List.collect (fun x -> [sep; x]) |> List.tail

    let zip_lists (a: string list) (b: string list) = List.zip a b

    // ── Math helpers ─────────────────────────────────────────────

    let sum (items: int list) = items |> List.sum

    let avg (items: int list) =
        if items.IsEmpty then 0
        else (items |> List.sum) / items.Length

    let min_val (items: int list) = items |> List.min
    let max_val (items: int list) = items |> List.max

    // ── File I/O helpers ─────────────────────────────────────────
    // Thin wrappers so FSI scripts can read/write files without opening
    // System.IO explicitly.

    /// Read all text from a file path. Returns "" if the file is missing.
    let readAllText (path: string) =
        if IO.File.Exists path then IO.File.ReadAllText(path) else ""

    /// Write text to a file path, creating parent directories as needed.
    let writeAllText (path: string) (content: string) =
        let dir = IO.Path.GetDirectoryName(path)
        if not (String.IsNullOrEmpty dir) then IO.Directory.CreateDirectory(dir) |> ignore
        IO.File.WriteAllText(path, content)

    /// Read all lines from a file. Returns empty list if missing.
    let readAllLines (path: string) =
        if IO.File.Exists path then IO.File.ReadAllLines(path) |> List.ofArray else []

    // ── Config loaders ───────────────────────────────────────────

    /// Parse a flat YAML mapping (key: value per line) into a Map.
    /// Handles the common frontmatter shape; nested mappings are flattened
    /// with dotted keys (e.g. `author:\n  name: x` → "author.name" → "x").
    let parseYaml (text: string) : Map<string, string> =
        let lines = text.Split('\n') |> Array.map (fun l -> l.TrimEnd('\r'))
        let result = ResizeArray<string * string>()
        let mutable prefixStack : (string * int) list = []
        for raw in lines do
            if raw.TrimStart().StartsWith("#") || String.IsNullOrWhiteSpace raw then () else
            // Leading-space count = indentation depth (cheap, allocation-free).
            let indent = raw.Length - raw.TrimStart(' ').Length
            // pop stack entries deeper than current indent
            while prefixStack.Length > 0 && (snd prefixStack.[0]) >= indent do
                prefixStack <- prefixStack.Tail
            let line = raw.Trim()
            let colon = line.IndexOf(':')
            if colon < 0 then () else
            let key = line.[..colon-1].Trim()
            let rest = line.[colon+1..].Trim().Trim('"', '\'')
            let fullKey =
                if prefixStack.IsEmpty then key
                else (fst prefixStack.[0]) + "." + key
            if rest = "" then
                // begins a nested block
                prefixStack <- (fullKey, indent) :: prefixStack
            else
                result.Add(fullKey, rest)
        Map.ofSeq result

    /// Load a YAML file into a Map<string, string>.
    let loadYaml (path: string) = readAllText path |> parseYaml

    /// Parse a TOML document into a Map<string, string> using Tomlyn.
    /// Nested tables are flattened to dotted keys.
    let parseToml (text: string) : Map<string, string> =
        let result = ResizeArray<string * string>()
        let rec walk (prefix: string) (tbl: TomlTable) =
            for kv in tbl do
                let key = if prefix = "" then kv.Key else prefix + "." + kv.Key
                match kv.Value with
                | :? TomlTable as nested -> walk key nested
                | :? TomlArray as arr ->
                    let vals = arr |> Seq.map (fun v -> v.ToString()) |> String.concat ","
                    result.Add(key, vals)
                | v -> result.Add(key, v.ToString())
        let model = Toml.ToModel(text)
        walk "" model
        Map.ofSeq result

    /// Load a TOML file into a Map<string, string>.
    let loadToml (path: string) = readAllText path |> parseToml

    /// Get a value from a Map with a default.
    let configGet (key: string) (defaultValue: string) (cfg: Map<string, string>) =
        match cfg.TryFind key with Some v -> v | None -> defaultValue
