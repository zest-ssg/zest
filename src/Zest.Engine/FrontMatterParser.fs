namespace Zest.Engine.Parsing

open System
open System.Text.RegularExpressions

/// 页面元数据，从 YAML front matter 或 // @key value 注释中解析。
type FrontMeta = {
    Layout:      string option
    Title:       string option
    Permalink:   string option
    Tags:        string list
    Date:        DateTime option
    Description: string option
    Extra:       Map<string, string>
}

module FrontMeta =
    let empty = {
        Layout = None; Title = None; Permalink = None
        Tags = []; Date = None; Description = None; Extra = Map.empty
    }

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

    /// 解析 YAML front matter（--- 分隔符），返回 (meta, body)。
    let parseYaml (text: string) : FrontMeta * string =
        let s = text.TrimStart()
        if not (s.StartsWith("---")) then (FrontMeta.empty, text)
        else
            let endIdx = s.IndexOf("---", 3)
            if endIdx < 0 then (FrontMeta.empty, text)
            else
                let fm   = s.Substring(3, endIdx - 3)
                let body = s.Substring(endIdx + 3).TrimStart('\n', '\r')
                let meta = parsePairs (fm.Split('\n')) FrontMeta.empty
                (meta, body)

    /// 解析 .zest.fsx 文件头部的 // @key value 注释。
    /// 只解析文件开头的连续注释块，遇到非注释行即停止，
    /// 避免误解析代码块中的示例元数据。
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
                    inHeader <- false  // 遇到非注释、非空行，结束元数据解析
            else
                ()  // 跳过文件其余部分
        parsePairs metaLines FrontMeta.empty

    /// 统一入口：先尝试 YAML，再尝试注释风格。返回 (meta, body)。
    let parse (ext: string) (text: string) : FrontMeta * string =
        match ext with
        | ".md" | ".markdown" -> parseYaml text
        | _ ->
            let yamlMeta, body = parseYaml text
            if yamlMeta <> FrontMeta.empty then (yamlMeta, body)
            else (parseFsxComments text, text)
