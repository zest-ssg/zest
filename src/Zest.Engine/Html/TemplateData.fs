namespace Zest.Engine.Html

open System
open System.Collections.Generic
open System.Net
open System.Text
open System.Text.RegularExpressions
open Zest.Engine

// ============================================================
// Template data context helpers
// ============================================================

module TemplateData =

    /// Safely get a value from site global data.
    let siteData (data: IDictionary<string, obj>) (key: string) : string =
        match data.TryGetValue key with
        | true, (:? string as s) -> s
        | true, v -> string v
        | _       -> ""

    /// Get a sub-object dictionary from site global data.
    let siteSection (data: IDictionary<string, obj>) (prefix: string) : IDictionary<string, obj> =
        let d = Dictionary<string, obj>()
        for kv in data do
            if kv.Key.StartsWith(prefix + ".") then
                d.[kv.Key.Substring(prefix.Length + 1)] <- kv.Value
        d :> _

    /// Get current page data.
    let pageData (page: ContentPage) (key: string) : string =
        match page.Data.TryGetValue key with
        | true, v -> string v
        | _       -> ""
