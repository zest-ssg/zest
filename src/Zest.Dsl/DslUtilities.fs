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

    // ── Triple-quote inline content blocks ──────────────────────
    // Mirrors the `md """..."""` ergonomics: raw triple-quoted strings
    // dropped straight into the DSL tree as plain `string` values, so they
    // compose with `render [ ... ]` like any other node. `dedent` strips the
    // common leading whitespace so authors may keep F# source indentation
    // without breaking the embedded language's own indentation-sensitive
    // syntax (Markdown ATX headings, JS blocks). See MIGRATION_NOTES §3.2/§九.

    /// Strip the common leading whitespace from every non-blank line of a
    /// triple-quoted string. Blank lines are preserved (and trimmed) but do
    /// not count toward the minimum indent. Returns the input unchanged when
    /// there is nothing to strip. Fixes the indentation issue noted in
    /// MIGRATION_NOTES §3.2: `md """..."""` / `js """..."""` bodies that
    /// inherit F# source indentation no longer fail to render.
    let dedent (text: string) : string =
        if String.IsNullOrEmpty text then ""
        else
            let lines = text.Split([| "\r\n"; "\n"; "\r" |], StringSplitOptions.None)
            let leadingWs (line: string) =
                if String.IsNullOrWhiteSpace line then Int32.MaxValue
                else line.Length - line.TrimStart().Length
            let nonBlank = lines |> Array.filter (fun l -> not (String.IsNullOrWhiteSpace l))
            if nonBlank.Length = 0 then text.Trim() else
            let minIndent = nonBlank |> Array.map leadingWs |> Array.fold min Int32.MaxValue
            if minIndent = Int32.MaxValue || minIndent = 0 then text
            else
                lines
                |> Array.map (fun line ->
                    if String.IsNullOrWhiteSpace line then line.Trim()
                    elif line.Length >= minIndent then line.[minIndent..]
                    else line.TrimStart())
                |> String.concat "\n"

    // ── Inline Markdown ────────────────────────────────────────

    /// Render an inline Markdown string to an HTML string, so Markdown content
    /// can be mixed directly into the (string-based) F# DSL tree. Returns a
    /// plain `string`, identical in kind to everything else the DSL emits, so it
    /// drops straight into a `render [ ... ]` block. Delegates to
    /// `Zest.Engine.Html.MarkdownEngine.toHtml`. Content is passed through
    /// verbatim — for indented triple-quoted bodies use `mdDedent`.
    let md (markdownText: string) : string =
        Zest.Engine.Html.MarkdownEngine.toHtml markdownText

    /// Like `md`, but first strips common leading indentation via `dedent`.
    /// Use this when the triple-quoted Markdown body is indented to match the
    /// surrounding F# source (the recommended style), so ATX headings (`##`)
    /// and fenced code blocks are recognised. Fixes MIGRATION_NOTES §3.2.
    let mdDedent (markdownText: string) : string =
        dedent markdownText |> Zest.Engine.Html.MarkdownEngine.toHtml

    // ── Inline JavaScript (L2) ──────────────────────────────────
    // Embed page-level one-off scripts via `js """..."""`, mirroring `md`.
    // The raw JS source is wrapped in `<script>…</script>` and emitted as-is:
    // F# does not validate JS syntax at build time (just as `md` does not
    // validate Markdown). For site-wide behaviour prefer external
    // `assets/js/*.js` referenced via `script src` (L1). See MIGRATION_NOTES
    // §九 L2.

    /// Embed an inline JavaScript block. Common leading indentation is
    /// stripped via `dedent`, so the triple-quoted body may follow F# source
    /// formatting. The body is run through `jsSafe` to neutralise `</`,
    /// U+2028, and U+2029 — which would prematurely terminate the `<script>`
    /// block in the browser's HTML parser. Use `jsonBlock` for type-safe data
    /// injection (it applies jsSafe internally).
    let js (code: string) : string =
        sprintf "<script>%s</script>" (dedent code |> jsSafe)

    /// Like `js`, but emits `<script type="module">` for ES module scripts
    /// (`import`/`export`, top-level `await`). Same dedent + jsSafe rules.
    let jsModule (code: string) : string =
        sprintf "<script type=\"module\">%s</script>" (dedent code |> jsSafe)

    // ── Data injection (L3) ─────────────────────────────────────
    // F# computes typed data; the client consumes it as JSON. Avoids the
    /// error-prone `sprintf "var x = %d" n` string-concatenation pattern.
    // See MIGRATION_NOTES §九 L3.

    /// Inject F#-computed data as a JSON literal consumed by client-side JS.
    /// Emits `<script>window.NAME = JSON</script>` where JSON is produced by
    /// `System.Text.Json` (handles all string escaping) and then run through
    /// `jsSafe` so `</`, U+2028 and U+2029 — which would prematurely terminate
    /// the `<script>` block — are neutralised. Client code reads
    /// `window.NAME`. This is the type-safe alternative to F# string-built JS.
    let jsonBlock (name: string) (data: obj) : string =
        let json =
            System.Text.Json.JsonSerializer.Serialize(data)
            |> jsSafe
        sprintf "<script>window.%s = %s</script>" name json
