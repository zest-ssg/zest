namespace Zest.Engine.Parsing

open System
open System.Text.RegularExpressions
open Tomlyn
open Tomlyn.Model
open Zest.Engine

/// <summary>
/// Frontmatter metadata parser — fully compatible with three header formats:
///
///   1. <b>TOML front matter</b>  — tripe-plus delimiters on their own lines (+++)
///      Supports nested local-date / local-datetime / offset-datetime,
///      inline tables, arrays, booleans, integers, and [section] tables.
///
///   2. <b>F# comment headers</b>   — leading contiguous lines of /// @key value
///      Supports multi-line continuation via /// @key  (trailing blank value).
///
///   3. <b>HTML comment headers</b> — leading contiguous lines of &lt;!-- @key value --&gt;
///      Metadata comments are stripped from the body.
///
/// All three parsers populate the same flat ContentMeta record.
/// </summary>
module MetaParser =

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  Shared key-value helpers (used by all three parsers)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// Recognised metadata keys and how they map to ContentMeta fields.
    let private knownKeys =
        set [ "layout"; "title"; "permalink"; "description"
              "date"; "tags"; "tag"; "categories"; "draft"
              "author"; "updated"; "weight"; "order"
              "template"; "collection" ]

    /// Parse a single key-value string pair and merge into the accumulator.
    let private applyPair (m: ContentMeta) (key: string) (rawVal: string) =
        let v = rawVal.Trim('"', '\'')
        match key with
        | "layout"      -> { m with Layout      = Some v }
        | "title"       -> { m with Title       = Some v }
        | "permalink"   -> { m with Permalink   = Some v }
        | "description" -> { m with Description = Some v }

        | "date" ->
            match DateTime.TryParse v with
            | true, dt -> { m with Date = Some dt }
            | _ -> m

        | "updated" ->
            match DateTime.TryParse v with
            | true, dt -> { m with Updated = Some dt }
            | _ -> m

        | "tags" | "tag" | "categories" ->
            let tags =
                v.Trim('[', ']').Split(',')
                |> Array.map (fun t -> t.Trim().Trim('"', '\''))
                |> Array.filter (fun t -> t.Length > 0)
                |> Array.toList
            { m with Tags = m.Tags @ tags }

        | "draft" ->
            let isDraft =
                match v.ToLowerInvariant() with
                | "true" | "yes" | "1" -> true
                | _ -> false
            { m with Draft = isDraft }

        | "author"      -> { m with Author      = Some v }
        | "template"    -> { m with Template    = Some v }
        | "collection"  -> { m with Collection  = Some v }

        | "weight" | "order" ->
            match Int32.TryParse v with
            | true, n -> { m with Weight = Some n }
            | _ -> m

        | _ -> { m with Extra = m.Extra |> Map.add key v }

    /// Parse a sequence of "key: value" lines into ContentMeta (used by F# comment + HTML comment parsers).
    let private parsePairs (lines: string seq) (seed: ContentMeta) =
        let mutable m = seed
        for line in lines do
            let t = line.Trim()
            let idx = t.IndexOf(':')
            if idx > 0 then
                let k = t.[..idx - 1].Trim().ToLowerInvariant()
                let v = t.[idx + 1..].Trim().Trim('"', '\'')
                m <- applyPair m k v
        m

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  TOML front matter parser (primary format)
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// Find the line indices [start; end] of a +++ delimited TOML block.
    /// Returns None when no valid TOML block is found.
    let private findTomlBlock (text: string) : (int * int * string) option =
        let lines = text.Replace("\r\n", "\n").Split('\n')
        match lines |> Array.tryFindIndex (fun l -> l.Trim() = "+++") with
        | Some openIdx ->
            let after = lines |> Array.skip (openIdx + 1)
            match after |> Array.tryFindIndex (fun l -> l.Trim() = "+++") with
            | Some closeRel ->
                let closeIdx = openIdx + 1 + closeRel
                let tomlLines = lines.[openIdx + 1 .. closeIdx - 1]
                let tomlBlock = String.Join("\n", tomlLines).Trim()
                let body =
                    if closeIdx + 1 < lines.Length then
                        String.Join("\n", lines.[closeIdx + 1 ..]).TrimStart('\n', '\r')
                    else ""
                Some (openIdx, closeIdx, tomlBlock)
            | None -> None
        | None -> None

    /// Parse known fields from a TomlTable, falling back to Extra for unknowns.
    let private metaFromTomlTable (table: TomlTable) : ContentMeta =
        let mutable m = ContentMeta.empty

        // Helper: walk a candidate key through all recognised forms
        let tryGetAny (keys: string list) =
            keys |> List.tryPick (fun k ->
                match table.TryGetValue(k) with
                | true, v -> Some v
                | _ -> None)

        // ── string fields ────────────────────────────
        let applyStr key setter =
            match table.TryGetValue(key) with
            | true, v -> m <- setter m (Some (v.ToString()))
            | _ -> ()

        applyStr "layout"      (fun m' v -> { m' with Layout      = v })
        applyStr "title"       (fun m' v -> { m' with Title       = v })
        applyStr "permalink"   (fun m' v -> { m' with Permalink   = v })
        applyStr "description" (fun m' v -> { m' with Description = v })
        applyStr "author"      (fun m' v -> { m' with Author      = v })
        applyStr "template"    (fun m' v -> { m' with Template    = v })
        applyStr "collection"  (fun m' v -> { m' with Collection  = v })

        // ── date fields ──────────────────────────────
        let tryParseDate (value: obj) =
            match value with
            | :? DateTimeOffset as dto -> Some dto.UtcDateTime
            | :? DateTime as dt       -> Some dt
            | :? string as s ->
                match DateTime.TryParse s with
                | true, dt -> Some dt
                | _ -> None
            | other ->
                // Fallback: try .ToString() → DateTime.Parse for TOML local date types
                match DateTime.TryParse (other.ToString()) with
                | true, dt -> Some dt
                | _ -> None

        match table.TryGetValue("date") with
        | true, v ->
            match tryParseDate v with
            | Some dt -> m <- { m with Date = Some dt }
            | _ -> ()
        | _ -> ()

        match table.TryGetValue("updated") with
        | true, v ->
            match tryParseDate v with
            | Some dt -> m <- { m with Updated = Some dt }
            | _ -> ()
        | _ -> ()

        // ── tags / categories ────────────────────────
        match tryGetAny ["tags"; "tag"; "categories"] with
        | Some v ->
            match v with
            | :? TomlArray as arr ->
                let tags = arr |> Seq.map (fun x -> x.ToString()) |> Seq.toList
                m <- { m with Tags = m.Tags @ tags }
            | :? string as s ->
                let tags = s.Split(',') |> Array.map (fun t -> t.Trim().Trim('"', '\'')) |> Array.filter (fun t -> t <> "") |> Array.toList
                m <- { m with Tags = m.Tags @ tags }
            | _ -> ()
        | _ -> ()

        // ── draft (boolean) ───────────────────────────
        match table.TryGetValue("draft") with
        | true, (:? bool as b)  -> m <- { m with Draft = b }
        | true, (:? string as s) ->
            m <- { m with Draft = s.ToLowerInvariant() = "true" || s = "1" || s = "yes" }
        | _ -> ()

        // ── weight / order (integer) ──────────────────
        match tryGetAny ["weight"; "order"] with
        | Some (:? int64 as n)  -> m <- { m with Weight = Some (int n) }
        | Some (:? int as n)    -> m <- { m with Weight = Some n }
        | Some (:? string as s) ->
            match Int32.TryParse s with
            | true, n -> m <- { m with Weight = Some n }
            | _ -> ()
        | _ -> ()

        // ── extra: capture all unknown keys ───────────
        for kv in table do
            let k = kv.Key.ToLowerInvariant()
            if not (knownKeys.Contains k) then
                m <- { m with Extra = m.Extra |> Map.add k (kv.Value.ToString()) }

        m

    /// Parse TOML front matter delimited by +++ on their own lines.
    /// Returns (meta, body).  Falls back to empty meta on parse failure.
    let parseToml (text: string) : ContentMeta * string =
        match findTomlBlock text with
        | Some (_openIdx, closeIdx, tomlBlock) when tomlBlock.Length > 0 ->
            try
                let table = Toml.ToModel(tomlBlock)
                if table <> null && table.Count > 0 then
                    let lines = text.Replace("\r\n", "\n").Split('\n')
                    let body =
                        if closeIdx + 1 < lines.Length then
                            String.Join("\n", lines.[closeIdx + 1 ..]).TrimStart('\n', '\r')
                        else ""
                    (metaFromTomlTable table, body)
                else
                    (ContentMeta.empty, text)
            with _ ->
                (ContentMeta.empty, text)
        | _ -> (ContentMeta.empty, text)

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  F# comment header parser
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// Returns true if a line looks like a metadata comment:  // @key value
    let private isMetadataLine (line: string) =
        Regex.IsMatch(line.Trim(), @"^//\s*@\w+")

    /// Parse // @key value  F# comment metadata from the leading contiguous block.
    let parseFsxComments (text: string) : ContentMeta =
        let lines = text.Replace("\r\n", "\n").Split('\n')
        let pairs = ResizeArray<string>()
        let mutable inHeader = true
        let mutable pendingKey : string option = None

        for line in lines do
            let t = line.Trim()
            if inHeader then
                if isMetadataLine t then
                    let m = Regex.Match(t, @"^//\s*@(\w+)\s*(.*)$")
                    if m.Success then
                        let k = m.Groups.[1].Value.ToLowerInvariant()
                        let v = m.Groups.[2].Value.Trim().Trim('"', '\'')
                        if v.Length > 0 then
                            pairs.Add(k + ": " + v)
                            pendingKey <- None
                        else
                            // Empty value = continuation hint; next non-meta line is the value
                            pendingKey <- Some k
                elif pendingKey.IsSome && t <> "" && not (t.StartsWith("//")) then
                    // Consume the continuation value
                    pairs.Add(pendingKey.Value + ": " + t)
                    pendingKey <- None
                elif pendingKey.IsSome && t.StartsWith("//") && not (isMetadataLine t) then
                    // Plain comment after pending key → consume as value
                    let plain = t.TrimStart('/').Trim()
                    pairs.Add(pendingKey.Value + ": " + plain)
                    pendingKey <- None
                elif t = "" then
                    // Blank lines are allowed within the header
                    ()
                elif not (t.StartsWith("//")) then
                    inHeader <- false
                // else: non-metadata comments are ignored
            else
                ()

        parsePairs pairs ContentMeta.empty

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  HTML comment header parser
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// Matches: <!-- @key value -->
    let private htmlMetaRegex = Regex(@"^<!--\s*@(\w+)\s*(.*?)\s*-->$", RegexOptions.Compiled)

    /// Parse <!-- @key value --> metadata from the leading contiguous comment block.
    /// Metadata comments are removed from the body; non-metadata HTML comments are preserved.
    let parseHtmlComments (text: string) : ContentMeta * string =
        let lines = text.Replace("\r\n", "\n").Split('\n')
        let metaPairs = ResizeArray<string>()
        let cleanedLines = ResizeArray<string>()
        let mutable inHeader = true

        for i in 0 .. lines.Length - 1 do
            let t = lines.[i].Trim()
            if inHeader then
                let m = htmlMetaRegex.Match(t)
                if m.Success then
                    let k = m.Groups.[1].Value.ToLowerInvariant()
                    let v = m.Groups.[2].Value.Trim().Trim('"', '\'')
                    if v.Length > 0 then
                        metaPairs.Add(k + ": " + v)
                    // Metadata comments are NOT added to cleanedLines
                elif t = "" then
                    // Preserve blank lines in body
                    cleanedLines.Add(lines.[i])
                elif t.StartsWith("<!--") then
                    // Non-metadata HTML comment → end header, keep in body
                    cleanedLines.Add(lines.[i])
                    inHeader <- false
                elif not (t.StartsWith("<!--")) then
                    // Non-comment, non-blank line → end header
                    cleanedLines.Add(lines.[i])
                    inHeader <- false
                else
                    cleanedLines.Add(lines.[i])
            else
                cleanedLines.Add(lines.[i])

        let meta = parsePairs metaPairs ContentMeta.empty
        let body = String.Join("\n", cleanedLines)
        (meta, body)

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  Unified entry point
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// Unified entry point. Returns (meta, body).
    ///
    /// Priority for all file types: TOML front matter (+++) first,
    /// then falls back to the comment-based parser appropriate for the extension.
    let parse (ext: string) (text: string) : ContentMeta * string =
        // Always try TOML first — it's the canonical format
        let tomlMeta, tomlBody = parseToml text
        if tomlMeta <> ContentMeta.empty then
            (tomlMeta, tomlBody)
        else
            match ext with
            | ".znjk" ->
                parseHtmlComments text
            | _ ->
                // .zpage.fsx / .fsx / .md without +++ headers
                let meta = parseFsxComments text
                (meta, text)
