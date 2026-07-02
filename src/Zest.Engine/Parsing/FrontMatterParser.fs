namespace Zest.Engine.Parsing

open System
open System.Text.RegularExpressions
open Tomlyn
open Tomlyn.Model

/// Metadata parser — supports TOML front matter (+++), F# comments, and HTML comments. YAML/JSON are forbidden.
module FrontMatterParser =

    let private parsePairs (lines: string seq) (acc: FrontMeta) =
        let mutable m = acc
        for line in lines do
            let t = line.Trim()
            let kv = t.Split([|':'|], 2)
            if kv.Length = 2 then
                let k = kv.[0].Trim().ToLowerInvariant()
                let v = kv.[1].Trim().Trim('"', '\'')
                match k with
                | "layout"      -> m <- { m with Layout = Some v }
                | "title"       -> m <- { m with Title = Some v }
                | "permalink"   -> m <- { m with Permalink = Some v }
                | "description" -> m <- { m with Description = Some v }
                | "date"        ->
                    match DateTime.TryParse v with
                    | true, dt -> m <- { m with Date = Some dt }
                    | _        -> ()
                | "tags" | "tag" ->
                    let tags =
                        v.Trim('[', ']').Split(',')
                        |> Array.map (fun t -> t.Trim().Trim('"', '\''))
                        |> Array.filter (fun t -> t.Length > 0)
                        |> Array.toList
                    m <- { m with Tags = m.Tags @ tags }
                | _ -> m <- { m with Extra = m.Extra |> Map.add k v }
        m

    /// Extract FrontMeta from a TomlTable.
    let private metaFromTomlTable (table: TomlTable) : FrontMeta =
        let mutable m = FrontMeta.empty
        let tryGet key =
            match table.TryGetValue(key) with
            | true, v -> Some v | _ -> None

        tryGet "layout"      |> Option.iter (fun v -> m <- { m with Layout = Some (v.ToString()) })
        tryGet "title"       |> Option.iter (fun v -> m <- { m with Title = Some (v.ToString()) })
        tryGet "permalink"   |> Option.iter (fun v -> m <- { m with Permalink = Some (v.ToString()) })
        tryGet "description" |> Option.iter (fun v -> m <- { m with Description = Some (v.ToString()) })
        tryGet "date"        |> Option.iter (fun v ->
            match v with
            | :? DateTimeOffset as dto -> m <- { m with Date = Some (dto.UtcDateTime) }
            | :? DateTime as dt -> m <- { m with Date = Some dt }
            | :? string as s ->
                match DateTime.TryParse s with
                | true, dt -> m <- { m with Date = Some dt }
                | _ -> ()
            | _ -> ())

        tryGet "tags" |> Option.iter (fun v ->
            match v with
            | :? TomlArray as arr ->
                let tags = arr |> Seq.map (fun x -> x.ToString()) |> Seq.toList
                m <- { m with Tags = m.Tags @ tags }
            | :? string as s ->
                let tags = s.Split(',') |> Array.map (fun t -> t.Trim()) |> Array.toList
                m <- { m with Tags = m.Tags @ tags }
            | _ -> ())

        // Put unknown keys into Extra
        for kv in table do
            let k = kv.Key.ToLowerInvariant()
            if not (List.contains k ["layout"; "title"; "permalink"; "description"; "date"; "tags"; "tag"]) then
                m <- { m with Extra = m.Extra |> Map.add k (kv.Value.ToString()) }

        m

    /// Parse TOML front matter (+++ delimiters), returns (meta, body).
    let parseToml (text: string) : FrontMeta * string =
        let s = text.TrimStart()
        if not (s.StartsWith("+++")) then (FrontMeta.empty, text)
        else
            let endIdx = s.IndexOf("+++", 3)
            if endIdx < 0 then (FrontMeta.empty, text)
            else
                let tomlBlock = s.Substring(3, endIdx - 3).Trim()
                let body = s.Substring(endIdx + 3).TrimStart('\n', '\r')
                try
                    let table = Toml.ToModel(tomlBlock)
                    if table = null then (FrontMeta.empty, body)
                    else (metaFromTomlTable table, body)
                with _ ->
                    // Return empty metadata on TOML parse failure
                    (FrontMeta.empty, body)

    /// Parse // @key value F# comments from .zpage.fsx file headers.
    /// Only parses the leading contiguous comment block, stopping at the first non-comment line.
    let parseFsxComments (text: string) : FrontMeta =
        let lines = text.Split('\n')
        let metaLines = ResizeArray<string>()
        let mutable inHeader = true
        for line in lines do
            let t = line.Trim()
            if inHeader then
                let m = Regex.Match(t, @"^//\s*@(\w+)\s+(.+)$")
                if m.Success then
                    metaLines.Add(m.Groups.[1].Value.ToLowerInvariant() + ": " + m.Groups.[2].Value.Trim().Trim('"', '\''))
                elif t <> "" && not (t.StartsWith("//")) then
                    inHeader <- false
            else
                ()
        parsePairs metaLines FrontMeta.empty

    /// Parse <!-- @key value --> HTML comments from .znjk file headers.
    /// Returns (meta, cleanedBody) where cleanedBody has metadata comment lines stripped.
    let parseHtmlComments (text: string) : FrontMeta * string =
        let lines = text.Split('\n')
        let metaLines = ResizeArray<string>()
        let cleanedLines = ResizeArray<string>()
        let mutable inHeader = true
        for line in lines do
            let t = line.Trim()
            if inHeader then
                let m = Regex.Match(t, @"^<!--\s*@(\w+)\s+(.+?)\s*-->$")
                if m.Success then
                    metaLines.Add(m.Groups.[1].Value.ToLowerInvariant() + ": " + m.Groups.[2].Value.Trim().Trim('"', '\''))
                elif t.StartsWith("<!--") && t.Contains("-->") then
                    cleanedLines.Add(line)
                    inHeader <- false
                elif t <> "" && not (t.StartsWith("<!--")) then
                    cleanedLines.Add(line)
                    inHeader <- false
                else
                    cleanedLines.Add(line)
            else
                cleanedLines.Add(line)
        let meta = parsePairs metaLines FrontMeta.empty
        let body = String.concat "\n" cleanedLines
        (meta, body)

    /// Unified entry point. Priority: TOML front matter (+++) → HTML comments → F# comments.
    /// Returns (meta, body).
    let parse (ext: string) (text: string) : FrontMeta * string =
        match ext with
        | ".md" | ".markdown" ->
            // Prefer TOML front matter, otherwise empty metadata (title extracted from # heading)
            let tomlMeta, body = parseToml text
            if tomlMeta <> FrontMeta.empty then (tomlMeta, body)
            else (FrontMeta.empty, text)
        | ".znjk" ->
            // Prefer TOML front matter, then HTML comments
            let tomlMeta, body = parseToml text
            if tomlMeta <> FrontMeta.empty then (tomlMeta, body)
            else parseHtmlComments text
        | _ ->
            // .zpage.fsx / .fsx: prefer TOML, then F# comments
            let tomlMeta, body = parseToml text
            if tomlMeta <> FrontMeta.empty then (tomlMeta, body)
            else
                let meta = parseFsxComments text
                (meta, text)
