// MetaParser.fs
//
// Parses page front matter from three interchangeable header formats and
// normalizes them into the flat ContentMeta record. It also strips the header
// from the body so downstream renderers never see metadata comments.
//
// Invariants:
//   - Text is normalized once (\r\n and \r collapse to \n) and reused by every
//     sub-parser; sub-parsers never re-split the input themselves.
//   - When a +++ TOML block yields no metadata, the body is reported unchanged
//     so a malformed header degrades into normal content instead of a failure.
//
// Dependencies: Tomlyn, System, System.Text.RegularExpressions, Zest.Engine

namespace Zest.Engine.Parsing

open System
open System.Text.RegularExpressions
open Tomlyn
open Tomlyn.Model
open Zest.Engine

/// <summary>
/// Frontmatter metadata parser — fully compatible with three header formats:
///
///   1. <b>TOML front matter</b>  — triple-plus delimiters on their own lines (+++)
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

    /// Splits a tag list on commas, semicolons, or whitespace.
    let private tagSplitPat = Regex(@"[,;\s]+", RegexOptions.Compiled)

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
        | "tags" | "tag" ->
            // Split on commas, semicolons OR whitespace so all of these work:
            // `@tags hugo, terminal` / `@tags hugo terminal` /
            // `@tags ["hugo", "terminal"].
            let tags =
                tagSplitPat.Split(v.Trim('[', ']'))
                |> Array.map (fun t -> t.Trim().Trim('"', '\''))
                |> Array.filter (fun t -> t.Length > 0)
                |> Array.toList
            { m with Tags = m.Tags @ tags }
        | "categories" | "category" ->
            // Categories use the same loose list syntax as tags but stay in a
            // separate field; they must never feed tag archives.
            let categories =
                tagSplitPat.Split(v.Trim('[', ']'))
                |> Array.map (fun t -> t.Trim().Trim('"', '\''))
                |> Array.filter (fun t -> t.Length > 0)
                |> Array.toList
            { m with Categories = m.Categories @ categories }
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

        let parseTermList (value: obj) =
            match value with
            | :? TomlArray as arr ->
                arr |> Seq.map (fun x -> x.ToString()) |> Seq.toList
            | :? string as s ->
                s.Split(',') |> Array.map (fun t -> t.Trim().Trim('"', '\'')) |> Array.filter (fun t -> t <> "") |> Array.toList
            | _ -> []

        match tryGetAny ["tags"; "tag"] with
        | Some v -> m <- { m with Tags = m.Tags @ parseTermList v }
        | _ -> ()

        match tryGetAny ["categories"; "category"] with
        | Some v -> m <- { m with Categories = m.Categories @ parseTermList v }
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

    /// Parse TOML front matter using pre-normalized lines.
    /// Returns Some (meta, body) only when a non-empty +++ block parses successfully;
    /// otherwise None so the caller can try the extension-specific header format.
    let private parseTomlWithLines (lines: string[]) : (ContentMeta * string) option =
        match findTomlBlock lines with
        | Some (_openIdx, _closeIdx, tomlBlock, body) when tomlBlock.Length > 0 ->
            try
                let table = Toml.ToModel(tomlBlock)
                if not (isNull table) && table.Count > 0 then
                    Some (metaFromTomlTable table, body)
                else
                    None
            with ex ->
                // Degrade to the extension-specific parser, but surface the cause
                // so a malformed header is not silently ignored.
                eprintfn "[Zest] WARN: Failed to parse TOML front matter: %s" ex.Message
                None
        | _ -> None

    /// <summary>
    /// Parse TOML front matter from raw text.
    /// Returns the parsed metadata and body, or empty metadata and the
    /// normalized text when no valid +++ block is present.
    /// </summary>
    let parseToml (text: string) : ContentMeta * string =
        let lines = normalizeLines text
        match parseTomlWithLines lines with
        | Some result -> result
        | None -> (ContentMeta.empty, String.Join("\n", lines))

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  F# comment header parser
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    let private fsxMetaPat = Regex(@"^//\s*@(\w+)\s*(.*)$", RegexOptions.Compiled)

    let private parseFsxCommentsWithLines (lines: string[]) : ContentMeta =
        let pairs = ResizeArray<string>()
        let mutable inHeader = true
        let mutable pendingKey : string option = None
        let mutable i = 0

        // The header ends at the first content line, so stop scanning there
        // instead of walking the whole (potentially large) body. A single Match
        // both detects and captures a metadata line.
        while inHeader && i < lines.Length do
            let t = lines.[i].Trim()
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
            elif pendingKey.IsSome && t.StartsWith("//") then
                let plain = t.TrimStart('/').Trim()
                pairs.Add(pendingKey.Value + ": " + plain)
                pendingKey <- None
            elif t = "" then ()
            elif not (t.StartsWith("//")) then
                inHeader <- false
            i <- i + 1
        parsePairs pairs ContentMeta.empty

    /// <summary>
    /// Parse an F# comment header (leading contiguous `// @key value` lines).
    /// Multi-line values continue with a `// @key` line carrying no value.
    /// </summary>
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
                else
                    // The first non-comment, non-blank line ends the header.
                    cleanedLines.Add(lines.[i])
                    inHeader <- false
            else
                cleanedLines.Add(lines.[i])

        let meta = parsePairs metaPairs ContentMeta.empty
        let body = String.Join("\n", cleanedLines)
        (meta, body)

    /// <summary>
    /// Parse an HTML comment header (`&lt;!-- @key value --&gt;` lines) and
    /// strip those comments from the returned body.
    /// </summary>
    let parseHtmlComments (text: string) : ContentMeta * string =
        parseHtmlCommentsWithLines (normalizeLines text)

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  Page defaults application
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// Apply a single default key/value to a ContentMeta.
    /// Only sets fields that are not already present (defaults don't override).
    let applyDefault (meta: ContentMeta) (key: string) (value: string) : ContentMeta =
        // Lowercase once so recognized keys and Extra entries share one spelling;
        // mixing cases would otherwise produce duplicate Extra keys.
        let k = key.ToLowerInvariant()
        match k with
        | "layout" when meta.Layout.IsNone    -> { meta with Layout      = Some value }
        | "title" when meta.Title.IsNone       -> { meta with Title       = Some value }
        | "permalink" when meta.Permalink.IsNone -> { meta with Permalink = Some value }
        | "description" when meta.Description.IsNone -> { meta with Description = Some value }
        | "author" when meta.Author.IsNone     -> { meta with Author      = Some value }
        | "template" when meta.Template.IsNone -> { meta with Template    = Some value }
        | "collection" when meta.Collection.IsNone -> { meta with Collection = Some value }
        | "date" when meta.Date.IsNone ->
            match DateTime.TryParse value with
            | true, dt -> { meta with Date = Some dt }
            | _ -> meta
        | _ -> { meta with Extra = meta.Extra |> Map.add k value }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    //  Unified entry point
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    /// <summary>
    /// Parse front matter from page text and return the metadata plus the body
    /// with any header comments removed. Text is normalized once and reused.
    /// The +++ TOML block is tried first; when it yields nothing, the header
    /// format is chosen by file extension (HTML comments for template files,
    /// F# comments otherwise).
    /// </summary>
    /// <param name="ext">File extension including the leading dot, e.g. ".md".</param>
    /// <param name="text">Raw page text.</param>
    /// <returns>The parsed metadata and the body text.</returns>
    let parse (ext: string) (text: string) : ContentMeta * string =
        let lines = normalizeLines text
        // Always try TOML first — it's the canonical format.
        match parseTomlWithLines lines with
        | Some (meta, body) when meta <> ContentMeta.empty -> (meta, body)
        | _ ->
            match ext with
            | FileExtensions.Nunjucks | FileExtensions.Liquid | FileExtensions.Handlebars
            | FileExtensions.Mustache | FileExtensions.Haml | FileExtensions.Pug
            | FileExtensions.WebC ->
                parseHtmlCommentsWithLines lines
            | _ ->
                let meta = parseFsxCommentsWithLines lines
                (meta, String.Join("\n", lines))
