namespace Zest.Engine.Scripting

open System
open System.Collections.Generic
open System.IO
open Zest.Engine

/// Collections API: page queries, global data, and Nunjucks helpers.
/// Optimized with on-demand caching for Nunjucks data.
module PageQuery =

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

    let getPagesByDate () =
        !allPagesRef |> List.filter (fun p -> p.Date.IsSome) |> List.sortByDescending (fun p -> p.Date.Value)

    let getPagesByCollection (collection: string) =
        !allPagesRef |> List.filter (fun p ->
            let parts = p.Url.Trim('/').Split('/')
            parts.Length > 0 && parts.[0].Equals(collection, StringComparison.OrdinalIgnoreCase))

    let getAllTags () =
        !allPagesRef |> List.collect (fun p -> p.Tags) |> List.distinct |> List.sort

    let getAllCollections () =
        !allPagesRef
        |> List.map (fun p ->
            let parts = p.Url.Trim('/').Split('/')
            if parts.Length > 0 && parts.[0] <> "" then parts.[0] else "root")
        |> List.distinct |> List.sort

    let searchPages (query: string) =
        !allPagesRef |> List.filter (fun p -> p.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)

    let getPageCount () = (!allPagesRef).Length

    let getPagesByDateRange (fromDate: string) (toDate: string) =
        let fromDt = DateTime.Parse(fromDate)
        let toDt   = DateTime.Parse(toDate)
        !allPagesRef
        |> List.filter (fun p ->
            p.Date.IsSome &&
            p.Date.Value >= fromDt &&
            p.Date.Value <= toDt)

    // ── Nunjucks data helpers ────────────────────────────────────────────

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

    // ── Cached Nunjucks data — computed once per build pass ──────────────

    let mutable private _cachedPagesForNunjucks : IDictionary<string, obj>[] option = None
    let mutable private _cachedTagsForNunjucks : string[] option = None
    let mutable private _cachedCollectionsForNunjucks : string[] option = None

    /// Reset cached Nunjucks data (call at build start).
    let internal resetNunjucksCache () =
        _cachedPagesForNunjucks <- None
        _cachedTagsForNunjucks <- None
        _cachedCollectionsForNunjucks <- None

    let getPagesForNunjucks () : IDictionary<string, obj>[] =
        match _cachedPagesForNunjucks with
        | Some cached -> cached
        | None ->
            let result = !allPagesRef |> List.map pageToNunjucksDict |> Array.ofList
            _cachedPagesForNunjucks <- Some result
            result

    let getTagsForNunjucks () : string[] =
        match _cachedTagsForNunjucks with
        | Some cached -> cached
        | None ->
            let result = !allPagesRef |> List.collect (fun p -> p.Tags) |> List.distinct |> List.sort |> Array.ofList
            _cachedTagsForNunjucks <- Some result
            result

    let getCollectionsForNunjucks () : string[] =
        match _cachedCollectionsForNunjucks with
        | Some cached -> cached
        | None ->
            let result =
                !allPagesRef
                |> List.map (fun p ->
                    let parts = p.Url.Trim('/').Split('/')
                    if parts.Length > 0 && parts.[0] <> "" then parts.[0] else "root")
                |> List.distinct |> List.sort |> Array.ofList
            _cachedCollectionsForNunjucks <- Some result
            result
