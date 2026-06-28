namespace Zest.Dsl

open System

// ============================================================
// DslCollections — Collection and page query APIs for FSI scripts
// ============================================================

module DslCollections =
    open Dsl
    open Context

    /// All pages across the site.
    let site_pages () = (get ()).Pages

    /// Most recent N pages (sorted by date descending).
    let recent_pages n =
        (get ()).Pages
        |> Array.filter (fun r -> r.date <> "")
        |> Array.sortByDescending (fun r -> r.date)
        |> Array.truncate n

    /// Pages tagged with a specific tag.
    let pages_by_tag (tag: string) =
        (get ()).Pages
        |> Array.filter (fun r ->
            r.tags
            |> Array.exists (fun t -> t.Equals(tag, StringComparison.OrdinalIgnoreCase)))

    /// Pages whose URL contains the given directory segment.
    let pages_by_dir dir =
        (get ()).Pages
        |> Array.filter (fun r -> r.url.Contains("/" + dir + "/"))

    /// Pages belonging to a collection (first URL segment).
    let pages_by_collection col =
        (get ()).Pages
        |> Array.filter (fun r -> r.url.Trim('/').Split('/').[0] = col)

    /// All unique tags across the site.
    let all_tags () =
        (get ()).Pages
        |> Array.collect (fun r -> r.tags)
        |> Array.distinct
        |> Array.sort

    /// All unique collection names (first URL segments).
    let all_collections () =
        (get ()).Pages
        |> Array.map (fun r -> r.url.Trim('/').Split('/').[0])
        |> Array.distinct
        |> Array.sort

    /// Case-insensitive title search.
    let search_pages (query: string) =
        (get ()).Pages
        |> Array.filter (fun r ->
            r.title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)

    /// Total page count.
    let page_count () = (get ()).Pages.Length

    /// Render an include partial by name.
    let include_partial name =
        match (get ()).Includes.TryGetValue(name) with
        | true, c -> c
        | _ -> sprintf "<!-- include '%s' not found -->" name

    /// Look up a site data value by key.
    let site_data key =
        match (get ()).SiteData.TryGetValue(key) with
        | true, v -> v
        | _ -> ""

    /// Look up site data values under a prefix (e.g. "social.twitter").
    let site_section prefix =
        (get ()).SiteData
        |> Seq.filter (fun kv -> kv.Key.StartsWith(prefix + "."))
        |> Seq.map (fun kv -> kv.Key.Substring(prefix.Length + 1), kv.Value)
        |> dict

    // ── New APIs ──────────────────────────────────────────────────────────

    /// Sort pages by a field ("title", "date", "slug") and direction ("asc", "desc").
    let sort_pages_by (field: string) (direction: string) =
        let ordered =
            match field.ToLowerInvariant() with
            | "title" -> (get ()).Pages |> Array.sortBy (fun r -> r.title.ToLowerInvariant())
            | "date"  -> (get ()).Pages |> Array.sortBy (fun r -> r.date)
            | "slug"  -> (get ()).Pages |> Array.sortBy (fun r -> r.slug)
            | _       -> (get ()).Pages
        match direction.ToLowerInvariant() with
        | "desc" | "descending" -> ordered |> Array.rev
        | _ -> ordered

    /// Filter pages by a predicate function.
    let filter_pages_by (pred: {| url: string; title: string; date: string; slug: string; description: string; tags: string[] |} -> bool) =
        (get ()).Pages |> Array.filter pred

    /// Group pages by year (from date field).
    let group_pages_by_year () =
        (get ()).Pages
        |> Array.filter (fun r -> r.date <> "")
        |> Array.groupBy (fun r ->
            try r.date.[..3] with _ -> "unknown")
        |> Array.sortByDescending fst
        |> Array.map (fun (year, pages) -> year, pages |> Array.toList)
        |> Array.toList

    /// Find related pages by shared tags, excluding the current page.
    let related_pages (page_url: string) (count: int) =
        let current =
            (get ()).Pages |> Array.tryFind (fun r ->
                r.url.TrimEnd('/').Equals(page_url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        match current with
        | None -> [||]
        | Some cur ->
            (get ()).Pages
            |> Array.filter (fun r ->
                not (r.url.TrimEnd('/').Equals(page_url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                && r.tags |> Array.exists (fun t -> cur.tags |> Array.exists (fun ct -> ct.Equals(t, StringComparison.OrdinalIgnoreCase))))
            |> Array.sortByDescending (fun r ->
                r.tags |> Array.filter (fun t -> cur.tags |> Array.exists (fun ct -> ct.Equals(t, StringComparison.OrdinalIgnoreCase))) |> Array.length)
            |> Array.truncate count

    /// Tag cloud: list of (tag, count) pairs, optionally filtered by minimum count.
    let tag_cloud (min_count: int) =
        (get ()).Pages
        |> Array.collect (fun r -> r.tags)
        |> Array.groupBy id
        |> Array.map (fun (tag, occurrences) -> tag, occurrences.Length)
        |> Array.filter (fun (_, count) -> count >= min_count)
        |> Array.sortByDescending snd
        |> Array.toList
