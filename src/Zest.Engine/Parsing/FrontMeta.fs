namespace Zest.Engine.Parsing

open System

/// Page metadata, parsed from TOML front matter (+++), F# comments (// @key value), or HTML comments (<!-- @key value -->).
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
