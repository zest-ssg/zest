namespace Zest.Engine.Scripting

open System
open System.Collections.Generic
open System.IO
open Zest.Engine

/// Collections API: page queries, global data, and Nunjucks helpers.
/// Mutable state is shared with ScriptRunner for context serialisation.
module PageQuery =

    // ── Global state ──────────────────────────────────────────────────────

    let internal allPagesRef : ContentPage list ref = ref []
    let internal includesRef : IDictionary<string, string> ref = ref (dict [])
    let internal verboseRef   : bool ref = ref false

    let setAllPages (pages: ContentPage list) = allPagesRef := pages
    let setIncludes (includes: IDictionary<string, string>) = includesRef := includes
    let setVerbose (v: bool) = verboseRef := v

    let getPages () = !allPagesRef
    let getPagesByTag (tag: string) =
        !allPagesRef |> List.filter (fun p -> p.Tags |> List.exists (fun t -> t.Equals(tag, StringComparison.OrdinalIgnoreCase)))
    let getPagesByDir (dirName: string) =
        !allPagesRef |> List.filter (fun p ->
            p.SourcePath.Contains(Path.DirectorySeparatorChar.ToString() + dirName + Path.DirectorySeparatorChar.ToString())
            || p.SourcePath.Contains("/" + dirName + "/"))
    let getRecentPages (n: int) =
        !allPagesRef |> List.filter (fun p -> p.Date.IsSome) |> List.sortByDescending (fun p -> p.Date.Value) |> List.truncate n
    let includePartial (name: string) =
        match (!includesRef).TryGetValue(name) with true, c -> c | _ -> sprintf "<!-- include '%s' not found -->" name

    // ── Global data ───────────────────────────────────────────────────────

    let internal globalDataRef : IDictionary<string, obj> ref =
        ref (dict [] :> IDictionary<string, obj>)

    let setGlobalData (data: IDictionary<string, obj>) =
        System.Threading.Interlocked.Exchange(globalDataRef, data) |> ignore

    let getDataString (key: string) : string =
        let mutable v : obj = null
        if (!globalDataRef).TryGetValue(key, &v) then (if isNull v then "" else v.ToString())
        else ""

    let getDataSection (prefix: string) : IDictionary<string, obj> =
        let d = Dictionary<string, obj>()
        for kv in !globalDataRef do
            if kv.Key.StartsWith(prefix + ".") then
                d.[kv.Key.Substring(prefix.Length + 1)] <- kv.Value
        d :> _

    // ── Extended Collections API ──────────────────────────────────────────

    /// Get pages sorted by date (newest first).
    let getPagesByDate () =
        !allPagesRef |> List.filter (fun p -> p.Date.IsSome) |> List.sortByDescending (fun p -> p.Date.Value)

    /// Get pages by collection (first URL segment).
    let getPagesByCollection (collection: string) =
        !allPagesRef |> List.filter (fun p ->
            let parts = p.Url.Trim('/').Split('/')
            parts.Length > 0 && parts.[0].Equals(collection, StringComparison.OrdinalIgnoreCase))

    /// Get all unique tags from all pages.
    let getAllTags () =
        !allPagesRef |> List.collect (fun p -> p.Tags) |> List.distinct |> List.sort

    /// Get all unique collections (first URL segment).
    let getAllCollections () =
        !allPagesRef
        |> List.map (fun p ->
            let parts = p.Url.Trim('/').Split('/')
            if parts.Length > 0 && parts.[0] <> "" then parts.[0] else "root")
        |> List.distinct |> List.sort

    /// Search pages by title (case-insensitive).
    let searchPages (query: string) =
        !allPagesRef |> List.filter (fun p -> p.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)

    /// Get page count.
    let getPageCount () = (!allPagesRef).Length

    /// Get pages within a date range.
    let getPagesByDateRange (fromDate: string) (toDate: string) =
        let fromDt = DateTime.Parse(fromDate)
        let toDt   = DateTime.Parse(toDate)
        !allPagesRef
        |> List.filter (fun p ->
            p.Date.IsSome &&
            p.Date.Value >= fromDt &&
            p.Date.Value <= toDt)

    // ── Nunjucks data helpers ────────────────────────────────────────────

    /// Convert a Page record to a plain dict for Nunjucks template context.
    let pageToNunjucksDict (p: ContentPage) : IDictionary<string, obj> =
        let d = Dictionary<string, obj>()
        d.["url"]    <- box p.Url
        d.["title"]  <- box p.Title
        d.["slug"]   <- box p.Slug
        d.["date"]   <- box (p.Date |> Option.map (fun d -> d.ToString("yyyy-MM-dd")) |> Option.defaultValue "")
        d.["tags"]   <- box (p.Tags |> Array.ofList)
        match p.Data.TryGetValue "description" with
        | true, v -> d.["description"] <- box v
        | _ -> ()
        d :> IDictionary<string, obj>

    /// Get all pages as plain dict arrays for Nunjucks template injection.
    let getPagesForNunjucks () : IDictionary<string, obj>[] =
        !allPagesRef |> List.map pageToNunjucksDict |> Array.ofList

    /// Get all unique tags as string array.
    let getTagsForNunjucks () : string[] =
        !allPagesRef
        |> List.collect (fun p -> p.Tags)
        |> List.distinct
        |> List.sort
        |> Array.ofList

    /// Get all collections (first URL segment) as string array.
    let getCollectionsForNunjucks () : string[] =
        !allPagesRef
        |> List.map (fun p ->
            let parts = p.Url.Trim('/').Split('/')
            if parts.Length > 0 && parts.[0] <> "" then parts.[0] else "root")
        |> List.distinct
        |> List.sort
        |> Array.ofList
