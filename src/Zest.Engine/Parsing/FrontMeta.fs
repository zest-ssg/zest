namespace Zest.Engine.Parsing

open System

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
