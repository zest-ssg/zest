namespace Zest.Engine.Html

open System
open System.Collections.Generic
open System.Net
open System.Text
open System.Text.RegularExpressions
open Zest.Engine

// ============================================================
// PageCollections — grouping pages by tags and collections
// ============================================================

module PageCollections =

    /// Group pages by their tags.
    let groupByTags (pages: ContentPage list) : (string * ContentPage list) list =
        pages
        |> List.collect (fun p -> p.Tags |> List.map (fun t -> t, p))
        |> List.groupBy fst
        |> List.map (fun (tag, items) -> tag, items |> List.map snd)

    /// Group pages by collection name (first URL segment).
    let groupByCollection (pages: ContentPage list) : (string * ContentPage list) list =
        pages
        |> List.groupBy (fun p ->
            let parts = p.Url.Trim('/').Split('/')
            if parts.Length > 0 && parts.[0] <> "" then parts.[0] else "root")
        |> List.sortBy fst
