namespace Zest.Dsl

open System
open System.Text

// ============================================================
// DslMigration — Config & frontmatter conversion helpers
// ============================================================
// Provides functions for converting between YAML and TOML config
// formats, and for rewriting frontmatter blocks in content files.
// Used by the `zest migrate` / `zest convert-config` CLI commands
// and available to FSI scripts.
//
// Dependencies: DslUtilities (parseYaml/parseToml), Tomlyn.
// ============================================================

module DslMigration =
    open DslUtilities

    /// Split a dotted key ("author.name") into table path + leaf key.
    let private splitKey (key: string) =
        let parts = key.Split('.')
        if parts.Length = 1 then [||], parts.[0]
        else parts.[..parts.Length-2], parts.[parts.Length-1]

    /// Render a Map<string,string> as TOML text. Dotted keys become
    /// nested tables: "author.name" → [author] \n name = "value".
    let mapToToml (data: Map<string, string>) : string =
        let sb = StringBuilder()
        let byTable =
            data
            |> Seq.map (fun kv -> splitKey kv.Key, kv.Value)
            |> Seq.groupBy (fun ((path, _), _) -> path)
            |> Seq.sortBy (fun (path, _) -> if Array.isEmpty path then "" else String.concat "." path)
        for (path, entries) in byTable do
            if not (Array.isEmpty path) then
                sb.AppendLine(sprintf "[%s]" (String.concat "." path)) |> ignore
            for ((_, leaf), value) in entries do
                // Quote the value unless it looks like a number or bool.
                let rendered =
                    let isNum = Double.TryParse(value) |> fst
                    let isBool = value = "true" || value = "false"
                    if isNum || isBool then value else sprintf "\"%s\"" (value.Replace("\\", "\\\\").Replace("\"", "\\\""))
                sb.AppendLine(sprintf "%s = %s" leaf rendered) |> ignore
            if not (Array.isEmpty path) then sb.AppendLine() |> ignore
        sb.ToString().TrimEnd()

    /// Quote a scalar for YAML output.
    let private yamlScalar (v: string) =
        let needsQuote = v.Contains(":") || v.Contains("#") || v.StartsWith(" ") || v.EndsWith(" ")
                           || v = "" || v = "true" || v = "false" || v = "null"
        if needsQuote then sprintf "\"%s\"" (v.Replace("\"", "\\\"")) else v

    /// Render a Map<string,string> as YAML text with dotted keys
    /// expanded into nested mappings.
    let mapToYaml (data: Map<string, string>) : string =
        let sb = StringBuilder()
        // Group by top-level segment for nesting.
        let groups =
            data
            |> Seq.groupBy (fun kv -> let p = kv.Key.Split('.') in p.[0])
            |> Seq.sortBy fst
        for (top, entries) in groups do
            for kv in entries do
                let parts = kv.Key.Split('.')
                if parts.Length = 1 then
                    sb.AppendLine(sprintf "%s: %s" parts.[0] (yamlScalar kv.Value)) |> ignore
                else
                    // Flatten remaining as nested dotted under top
                    let rest = String.concat "." parts.[1..]
                    sb.AppendLine(sprintf "%s:" top) |> ignore
                    sb.AppendLine(sprintf "  %s: %s" rest (yamlScalar kv.Value)) |> ignore
        sb.ToString().TrimEnd()

    /// Convert YAML text to TOML text.
    let convertYamlToToml (yamlText: string) : string =
        parseYaml yamlText |> mapToToml

    /// Convert TOML text to YAML text.
    let convertTomlToYaml (tomlText: string) : string =
        parseToml tomlText |> mapToYaml

    /// Detect the frontmatter delimiter format of a content file.
    /// Returns "yaml" (---), "toml" (+++) or "none".
    let detectFrontmatter (fileContent: string) : string =
        let t = fileContent.TrimStart()
        if t.StartsWith("+++") then "toml"
        elif t.StartsWith("---") then "yaml"
        else "none"

    /// Extract (frontmatterText, bodyText, format) from a content file.
    /// Returns ("", fullContent, "none") when no frontmatter is present.
    let splitFrontmatter (fileContent: string) : string * string * string =
        let fmt = detectFrontmatter fileContent
        if fmt = "none" then "", fileContent, "none"
        else
            let delim = if fmt = "toml" then "+++" else "---"
            let lines = fileContent.Split('\n')
            // Find opening delimiter (first non-empty line)
            let mutable startIdx = -1
            let mutable endIdx = -1
            for i in 0 .. lines.Length - 1 do
                let lt = lines.[i].Trim()
                if startIdx < 0 then
                    if lt = delim then startIdx <- i
                elif endIdx < 0 && lt = delim then
                    endIdx <- i
            if startIdx < 0 || endIdx < 0 then "", fileContent, "none"
            else
                let fm = lines.[startIdx+1 .. endIdx-1] |> String.concat "\n"
                let body =
                    if endIdx + 1 < lines.Length then
                        lines.[endIdx+1..] |> String.concat "\n"
                    else ""
                fm, body, fmt

    /// Convert the frontmatter of a content file to the target format.
    /// `targetFormat` is "yaml" or "toml". Body is preserved verbatim.
    let convertFrontmatter (fileContent: string) (targetFormat: string) : string =
        let fm, body, srcFmt = splitFrontmatter fileContent
        if srcFmt = "none" then fileContent
        elif srcFmt = targetFormat then fileContent
        else
            let converted =
                if srcFmt = "yaml" && targetFormat = "toml" then convertYamlToToml fm
                elif srcFmt = "toml" && targetFormat = "yaml" then convertTomlToYaml fm
                else fm
            let delim = if targetFormat = "toml" then "+++" else "---"
            sprintf "%s\n%s\n%s\n\n%s" delim converted delim body

    /// Register a custom migration function (callable from `zest migrate`).
    /// Migration functions take a source directory and target directory and
    /// return a list of (relativePath, content) pairs written.
    let mutable private customMigrations: (string -> string -> (string * string) list) list = []

    /// Register a custom migration handler.
    let registerMigration (fn: string -> string -> (string * string) list) =
        customMigrations <- fn :: customMigrations

    /// Run all registered custom migrations against a source/target dir.
    let runMigrations (sourceDir: string) (targetDir: string) : (string * string) list =
        customMigrations |> List.collect (fun fn -> fn sourceDir targetDir)