namespace Zest.Dsl

open System

// ============================================================
// DslCollections — Collection and page query APIs for FSI scripts
// ============================================================

[<AutoOpen>]
module DslCollections =
    open Dsl
    open Context

    /// Structural type of a page exposed to DSL scripts. Matches the record
    /// produced by ZestContext.Pages (including front-matter `author` and
    /// `category`, which default to "" when absent).
    type PageInfo =
        {| url: string
           title: string
           date: string
           slug: string
           description: string
           tags: string[]
           author: string
           category: string |}

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
        | true, v -> v.ToString()
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
    let filter_pages_by (pred: PageInfo -> bool) =
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

    // ── Pagination & grouping (11ty-style) ──────────────────────────────

    /// A single page of paginated results.
    type Page<'a> = {
        Items: 'a list
        PageNumber: int      // 1-based
        TotalPages: int
        TotalItems: int
        HasPrev: bool
        HasNext: bool
        PrevUrl: string
        NextUrl: string
    }

    /// Split a sequence of items into pages of `perPage` items each.
    /// Returns the list of pages with navigation metadata. The `urlFor`
    /// function builds the URL for a given 1-based page number.
    let paginate (perPage: int) (urlFor: int -> string) (items: 'a seq) : Page<'a> list =
        let arr = Array.ofSeq items
        let total = arr.Length
        let totalPages = if perPage <= 0 then 1 else (total + perPage - 1) / perPage |> max 1
        [ for p in 1 .. totalPages ->
            let startIdx = (p - 1) * perPage
            let count = min perPage (total - startIdx) |> max 0
            let pageItems = if count > 0 then arr.[startIdx .. startIdx + count - 1] else [||]
            { Items = List.ofArray pageItems
              PageNumber = p
              TotalPages = totalPages
              TotalItems = total
              HasPrev = p > 1
              HasNext = p < totalPages
              PrevUrl = if p > 1 then urlFor (p - 1) else ""
              NextUrl = if p < totalPages then urlFor (p + 1) else "" } ]

    /// Convenience paginator for site pages, splitting by date-descending
    /// order (common blog pattern). `urlFor` receives the 1-based page index.
    let paginate_pages (perPage: int) (urlFor: int -> string) =
        (get ()).Pages
        |> Array.filter (fun r -> r.date <> "")
        |> Array.sortByDescending (fun r -> r.date)
        |> paginate perPage urlFor

    /// Group items by a key selector function. Returns (key, items) pairs.
    let group (keyFn: 'a -> string) (items: 'a seq) : (string * 'a list) list =
        items
        |> Seq.groupBy keyFn
        |> Seq.map (fun (k, g) -> k, List.ofSeq g)
        |> List.ofSeq

    /// Group site pages by a field name ("tag", "collection", "year", "author").
    /// Returns (key, pages) pairs so callers can iterate uniformly regardless
    /// of the grouping field.
    let group_pages_by (field: string) : (string * obj list) list =
        let pages = (get ()).Pages
        match field.ToLowerInvariant() with
        | "tag" ->
            // Each page may carry multiple tags — expand to (tag, page) pairs,
            // group by tag, then drop the tag from the inner list so the
            // return type matches the other branches (string * page list).
            pages
            |> Array.collect (fun r -> r.tags |> Array.map (fun t -> t, r))
            |> group fst
            |> List.map (fun (k, pairs) -> k, pairs |> List.map snd |> List.map box)
        | "collection" ->
            pages
            |> group (fun r -> r.url.Trim('/').Split('/').[0])
            |> List.map (fun (k, ps) -> k, ps |> List.map box)
        | "year" ->
            pages
            |> Array.filter (fun r -> r.date <> "")
            |> group (fun r -> try r.date.[..3] with _ -> "unknown")
            |> List.map (fun (k, ps) -> k, ps |> List.map box)
        | _ ->
            pages |> group (fun _ -> "unknown")
                  |> List.map (fun (k, ps) -> k, ps |> List.map box)

    /// Filter items by a property value: `where(items, "tags", "tutorial")`.
    /// Supports the page anonymous record's fields (url, title, date, slug,
    /// description, author, category).
    let where (prop: string) (value: string) (items: _ array) =
        let getProp (r: PageInfo) =
            match prop.ToLowerInvariant() with
            | "url" -> r.url
            | "title" -> r.title
            | "date" -> r.date
            | "slug" -> r.slug
            | "description" -> r.description
            | "author" -> r.author
            | "category" -> r.category
            | _ -> ""
        items |> Array.filter (fun r -> getProp r = value)

    /// Look up a single page by URL. Returns Some page or None.
    let get_page (url: string) =
        (get ()).Pages
        |> Array.tryFind (fun r -> r.url.TrimEnd('/').Equals(url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))

    /// Look up all pages in a named collection (first URL segment).
    let get_collection (name: string) = pages_by_collection name

    // ── Enhanced collection APIs ────────────────────────────────

    /// Get pages by multiple tags (AND logic - must have ALL tags).
    let pages_by_all_tags (tags: string list) =
        (get ()).Pages
        |> Array.filter (fun r ->
            tags |> List.forall (fun tag ->
                r.tags |> Array.exists (fun t -> t.Equals(tag, StringComparison.OrdinalIgnoreCase))))

    /// Get pages by multiple tags (OR logic - must have ANY tag).
    let pages_by_any_tag (tags: string list) =
        (get ()).Pages
        |> Array.filter (fun r ->
            r.tags |> Array.exists (fun t ->
                tags |> List.exists (fun tag -> t.Equals(tag, StringComparison.OrdinalIgnoreCase))))

    /// Get pages published in a specific year.
    let pages_by_year (year: string) =
        (get ()).Pages
        |> Array.filter (fun r -> r.date.StartsWith(year))

    // ── Phase 5: author / category / result shaping ───────────────

    /// Pages written by a specific author (case-insensitive, from the
    /// `author` front-matter field; pages without the field match "").
    let pages_by_author (author: string) =
        (get ()).Pages
        |> Array.filter (fun r ->
            r.author.Equals(author, StringComparison.OrdinalIgnoreCase))

    /// Pages in a specific category (case-insensitive, from the `category`
    /// front-matter field; pages without the field match "").
    let pages_by_category (category: string) =
        (get ()).Pages
        |> Array.filter (fun r ->
            r.category.Equals(category, StringComparison.OrdinalIgnoreCase))

    /// Sort pages by a field ("title", "date", "slug") and direction
    /// ("asc"/"desc"). Canonical `pages_sorted_by` name; delegates to
    /// `sort_pages_by`.
    let pages_sorted_by (field: string) (direction: string) =
        sort_pages_by field direction

    /// Restrict results to the first N pages (negative N behaves like 0).
    let pages_limit (n: int) =
        (get ()).Pages |> Array.truncate (max 0 n)

    /// Skip the first N pages (negative N behaves like 0).
    let pages_offset (n: int) =
        (get ()).Pages |> Array.skip (max 0 n)

    // ── Phase 5: time-based grouping ───────────────────────────────

    /// Group pages by year-month (yyyy-MM) from the date field, sorted
    /// newest-first. Pages with no usable date land under "unknown".
    let group_pages_by_month () =
        (get ()).Pages
        |> Array.filter (fun r -> r.date <> "")
        |> Array.groupBy (fun r ->
            if r.date.Length >= 7 then r.date.[..6]
            else try r.date.[..3] with _ -> "unknown")
        |> Array.sortByDescending fst
        |> Array.map (fun (month, pages) -> month, pages |> Array.toList)
        |> Array.toList

    // ── Phase 5: advanced search ────────────────────────────────────

    /// Regex-based search across title, url, description and tags
    /// (case-insensitive). Invalid patterns match nothing instead of throwing.
    let search_pages_advanced (pattern: string) =
        let re =
            try
                Some (System.Text.RegularExpressions.Regex(
                    pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            with _ -> None
        match re with
        | None -> [||]
        | Some re ->
            (get ()).Pages
            |> Array.filter (fun r ->
                re.IsMatch(r.title) || re.IsMatch(r.url) || re.IsMatch(r.description)
                || r.tags |> Array.exists (fun t -> re.IsMatch(t)))

    // ── Phase 5: content relationships ──────────────────────────────

    /// Related pages sharing the same category, excluding the page itself.
    /// Returns at most `count` results (empty when the current page has no
    /// category or cannot be found).
    let related_pages_by_category (page_url: string) (count: int) =
        let current =
            (get ()).Pages |> Array.tryFind (fun r ->
                r.url.TrimEnd('/').Equals(page_url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        match current with
        | None -> [||]
        | Some cur when cur.category = "" -> [||]
        | Some cur ->
            (get ()).Pages
            |> Array.filter (fun r ->
                not (r.url.TrimEnd('/').Equals(page_url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                && r.category.Equals(cur.category, StringComparison.OrdinalIgnoreCase))
            |> Array.truncate (max 0 count)

    /// Tag cloud with weighted importance: weight = count ^ `weight` (default
    /// 1.0 gives plain counts; a higher exponent amplifies frequent tags).
    /// Returns (tag, weight, count) triples sorted by weight descending.
    let tag_cloud_weighted (weight: float) =
        (get ()).Pages
        |> Array.collect (fun r -> r.tags)
        |> Array.groupBy id
        |> Array.map (fun (tag, occurrences) ->
            let count = occurrences.Length
            tag, (float count) ** weight, count)
        |> Array.sortByDescending (fun (_, w, _) -> w)
        |> Array.toList
