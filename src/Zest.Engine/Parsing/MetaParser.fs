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

    let private knownKeys =
        set [ "layout"; "title"; "permalink"; "description"
              "date"; "tags"; "tag"; "categories"; "draft"
              "author"; "updated"; "weight"; "order"
              "template"; "collection" ]

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

    /// Parse a sequence of "key: value" lines into ContentMeta.
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

    /// Find the line indices of a +++ delimited TOML block within pre-split lines.
    /// Returns (openIdx, closeIdx, tomlBlock, bodyText).
    let private findTomlBlock (lines: string[]) : (int * int * string * string) option =
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
                Some (openIdx, closeIdx, tomlBlock, body)
            | None -> None
        | None -> None

    let private metaFromTomlTable (table: TomlTable) : ContentMeta =
        let mutable m = ContentMeta.empty

        let tryGetAny (keys: string list) =
            keys |> List.tryPick (fun k ->
                match table.TryGetValue(k) with
                | true, v -> Some v
                | _ -> None)

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

        let tryParseDate (value: obj) =
            match value with
            | :? DateTimeOffset as dto -> Some dto.UtcDateTime
            | :? DateTime as dt       -> Some dt
            | :? string as s ->
                match DateTime.TryParse s with
                | true, dt -> Some dt
                | _ -> None
            | other ->
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

        match table.TryGetValue("draft") with
        | true, (:? bool as b)  -> m <- { m with Draft = b }
        | true, (:? string as s) ->
            m <- { m with Draft = s.ToLowerInvariant() = "true" || s = "1" || s = "yes" }
        | _ -> ()

        match tryGetAny ["weight"; "order"] with
        | Some (:? int64 as n)  -> m <- { m with Weight = Some (int n) }
        | Some (:? int as n)    -> m <- { m with Weight = Some n }
        | Some (:? string as s) ->
            match Int32.TryParse s with
            | true, n -> m <- { m with Weight = Some n }
            | _ -> ()
        | _ -> ()

        for kv in table do
            let k = kv.Key.ToLowerInvariant()
            if not (knownKeys.Contains k) then
                m <- { m with Extra = m.Extra |> Map.add k (kv.Value.ToString()) }

        m

    /// Normalize text: \r\n → \n, split to lines. Call once at entry, reuse lines everywhere.
    let private normalizeLines (text: string) : string[] =
        text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')

    /// Parse TOML front matter using pre-normalized lines. Returns (meta, body).
    let private parseTomlWithLines (lines: string[]) : ContentMeta * string =
        match findTomlBlock lines with
        | Some (_openIdx, _closeIdx, tomlBlock, body) when tomlBlock.Length > 0 ->
            try
                let table = Toml.ToModel(tomlBlock)
                if table <> null && table.Count > 0 then
                    (metaFromTomlTable table, body)
                else
                    (ContentMeta.empty, String.Join("\n", lines))
            with _ ->
                (ContentMeta.empty, String.Join("\n", lines))
        | _ -> (ContentMeta.empty, String.Join("\n", lines))

    let parseToml (text: string) : ContentMeta * string =
        parseTomlWithLines (normalizeLines text)

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  F# comment header parser
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    let private isMetaLinePat = Regex(@"^//\s*@\w+", RegexOptions.Compiled)
    let private fsxMetaPat    = Regex(@"^//\s*@(\w+)\s*(.*)$", RegexOptions.Compiled)

    let private isMetadataLine (line: string) = isMetaLinePat.IsMatch(line.Trim())

    let private parseFsxCommentsWithLines (lines: string[]) : ContentMeta =
        let pairs = ResizeArray<string>()
        let mutable inHeader = true
        let mutable pendingKey : string option = None

        for line in lines do
            let t = line.Trim()
            if inHeader then
                if isMetadataLine t then
                    let m = fsxMetaPat.Match(t)
                    if m.Success then
                        let k = m.Groups.[1].Value.ToLowerInvariant()
                        let v = m.Groups.[2].Value.Trim().Trim('"', '\'')
                        if v.Length > 0 then
                            pairs.Add(k + ": " + v)
                            pendingKey <- None
                        else
                            pendingKey <- Some k
                elif pendingKey.IsSome && t <> "" && not (t.StartsWith("//")) then
                    pairs.Add(pendingKey.Value + ": " + t)
                    pendingKey <- None
                elif pendingKey.IsSome && t.StartsWith("//") && not (isMetadataLine t) then
                    let plain = t.TrimStart('/').Trim()
                    pairs.Add(pendingKey.Value + ": " + plain)
                    pendingKey <- None
                elif t = "" then ()
                elif not (t.StartsWith("//")) then
                    inHeader <- false
        parsePairs pairs ContentMeta.empty

    let parseFsxComments (text: string) : ContentMeta =
        parseFsxCommentsWithLines (normalizeLines text)

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  HTML comment header parser
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    let private htmlMetaRegex = Regex(@"^<!--\s*@(\w+)\s*(.*?)\s*-->$", RegexOptions.Compiled)

    let private parseHtmlCommentsWithLines (lines: string[]) : ContentMeta * string =
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
                elif t = "" then
                    cleanedLines.Add(lines.[i])
                elif t.StartsWith("<!--") then
                    cleanedLines.Add(lines.[i])
                    inHeader <- false
                elif not (t.StartsWith("<!--")) then
                    cleanedLines.Add(lines.[i])
                    inHeader <- false
                else
                    cleanedLines.Add(lines.[i])
            else
                cleanedLines.Add(lines.[i])

        let meta = parsePairs metaPairs ContentMeta.empty
        let body = String.Join("\n", cleanedLines)
        (meta, body)

    let parseHtmlComments (text: string) : ContentMeta * string =
        parseHtmlCommentsWithLines (normalizeLines text)

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  Unified entry point
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// Unified entry point. Returns (meta, body).
    /// Text is normalized once (\r\n → \n, split to lines) and reused across all parsers.
    let parse (ext: string) (text: string) : ContentMeta * string =
        let lines = normalizeLines text
        // Always try TOML first — it's the canonical format
        let tomlMeta, tomlBody = parseTomlWithLines lines
        if tomlMeta <> ContentMeta.empty then
            (tomlMeta, tomlBody)
        else
            match ext with
            | ".njk" | ".liquid" | ".hbs" | ".mustache" | ".haml" | ".pug" | ".webc" ->
                parseHtmlCommentsWithLines lines
            | _ ->
                let meta = parseFsxCommentsWithLines lines
                (meta, String.Join("\n", lines))
