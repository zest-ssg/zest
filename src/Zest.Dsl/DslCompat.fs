namespace Zest.Dsl

open System

// ============================================================
// DslCompat — Cross-SSG page compatibility shims
// ============================================================
// Converts page records from Jekyll / Hexo / Hugo / 11ty into
// Zest's canonical page shape, so migrated content can be
// consumed by DslCollections and the template layer uniformly.
//
// Dependency: none (pure data shaping).
// ============================================================

module DslCompat =

    /// Zest's canonical page record (mirrors the engine's page shape).
    type ZestPage = {
        url: string
        title: string
        date: string
        slug: string
        description: string
        tags: string[]
    }

    /// Jekyll-style page (uses `permalink`, `categories`, `tags`).
    type JekyllPage = {
        title: string
        date: string          // "2024-01-15"
        slug: string
        permalink: string     // e.g. "/blog/2024/01/15/my-post/"
        categories: string[]
        tags: string[]
        excerpt: string
    }

    /// Hexo-style page (uses `path`, `categories`, `tags`).
    type HexoPage = {
        title: string
        date: string
        slug: string
        path: string          // e.g. "2024/01/15/my-post/"
        categories: string[]
        tags: string[]
        excerpt: string
    }

    /// Hugo-style page (uses `Permalink`, uses title-case fields).
    type HugoPage = {
        Title: string
        Date: string
        Slug: string
        Permalink: string
        Categories: string[]
        Tags: string[]
        Description: string
    }

    /// 11ty-style page (uses `url`, `data.tags`).
    type EleventyPage = {
        url: string
        title: string
        date: string
        slug: string
        description: string
        tags: string[]
    }

    /// Normalize a date string to ISO YYYY-MM-DD. Handles common
    /// input forms from Jekyll/Hexo/Hugo.
    let private normalizeDate (d: string) =
        if String.IsNullOrEmpty d then ""
        else
            match DateTime.TryParse(d) with
            | true, dt -> dt.ToString("yyyy-MM-dd")
            | _ -> d

    /// Ensure a URL starts with "/" and is trimmed of trailing whitespace.
    let private normalizeUrl (u: string) =
        let t = if isNull u then "" else u.Trim()
        if t = "" then "/"
        elif t.StartsWith("/") then t
        else "/" + t

    /// Merge categories + tags into a single tags array (Jekyll/Hexo treat
    /// categories as an organizational dimension that Zest folds into tags).
    let private mergeTags (categories: string[]) (tags: string[]) =
        Array.append categories tags
        |> Array.distinctBy (fun s -> s.ToLowerInvariant())
        |> Array.filter (not << String.IsNullOrWhiteSpace)

    /// Convert a Jekyll page to a Zest page.
    let compatPageFromJekyll (p: JekyllPage) : ZestPage =
        { url = normalizeUrl (if String.IsNullOrEmpty p.permalink then "/blog/" + p.slug else p.permalink)
          title = p.title
          date = normalizeDate p.date
          slug = p.slug
          description = p.excerpt
          tags = mergeTags p.categories p.tags }

    /// Convert a Hexo page to a Zest page.
    let compatPageFromHexo (p: HexoPage) : ZestPage =
        { url = normalizeUrl (if String.IsNullOrEmpty p.path then p.slug + "/" else p.path)
          title = p.title
          date = normalizeDate p.date
          slug = p.slug
          description = p.excerpt
          tags = mergeTags p.categories p.tags }

    /// Convert a Hugo page to a Zest page.
    let compatPageFromHugo (p: HugoPage) : ZestPage =
        { url = normalizeUrl p.Permalink
          title = p.Title
          date = normalizeDate p.Date
          slug = p.Slug
          description = p.Description
          tags = mergeTags p.Categories p.Tags }

    /// Convert an 11ty page to a Zest page (already close to Zest shape).
    let compatPageFromEleventy (p: EleventyPage) : ZestPage =
        { url = normalizeUrl p.url
          title = p.title
          date = normalizeDate p.date
          slug = p.slug
          description = p.description
          tags = p.tags }

    /// Apply a Jekyll-style permalink pattern to a page slug/date.
    /// Patterns: "/:categories/:year/:month/:day/:title/" etc.
    let applyJekyllPermalink (pattern: string) (slug: string) (date: string) (categories: string[]) : string =
        if String.IsNullOrEmpty pattern then "/" + slug + "/"
        else
            let dt = match DateTime.TryParse(date) with true, d -> d | _ -> DateTime.Now
            pattern.Replace(":year", dt.ToString("yyyy"))
                   .Replace(":month", dt.ToString("MM"))
                   .Replace(":day", dt.ToString("dd"))
                   .Replace(":title", slug)
                   .Replace(":categories", String.concat "/" categories)
            |> normalizeUrl