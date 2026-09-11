namespace Zest.Dsl

open System
open System.Text.RegularExpressions

// ============================================================
// StringHelper — String manipulation, slug, and text utilities
// ============================================================

module StringHelper =

    /// Convert a string to a URL-safe slug.
    let slugify (s: string) =
        s.ToLowerInvariant()
            .Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss")
            .Replace("é", "e").Replace("è", "e").Replace("ê", "e").Replace("ë", "e")
            .Replace("á", "a").Replace("à", "a").Replace("â", "a").Replace("ã", "a").Replace("å", "a")
            .Replace("í", "i").Replace("ì", "i").Replace("î", "i").Replace("ï", "i")
            .Replace("ó", "o").Replace("ò", "o").Replace("ô", "o").Replace("õ", "o")
            .Replace("ú", "u").Replace("ù", "u").Replace("û", "u").Replace("ñ", "n")
            .Replace("ç", "c")
        |> (fun s -> Regex.Replace(s, @"[^\w\-\s]", ""))
        |> (fun s -> Regex.Replace(s, @"[\s\-]+", "-"))
        |> (fun s -> s.Trim('-'))

    /// Truncate string to N characters with ellipsis.
    let truncate (maxLen: int) (s: string) =
        if String.length s <= maxLen then s
        else s.[..maxLen - 1] + "…"

    /// Strip all HTML tags from a string.
    let strip_html (s: string) =
        Regex.Replace(s, @"<[^>]+>", "").Trim()

    /// Estimate reading time in minutes (200 wpm).
    let reading_time (s: string) =
        let wordCount = s.Split([| ' '; '\n'; '\t' |], StringSplitOptions.RemoveEmptyEntries).Length
        max 1 (wordCount / 200)

    /// Count words in a string.
    let word_count (s: string) =
        s.Split([| ' '; '\n'; '\t' |], StringSplitOptions.RemoveEmptyEntries).Length

    /// Extract an excerpt from HTML content.
    let excerpt (maxLen: int) (html: string) =
        strip_html html |> fun s -> truncate maxLen s

    /// Capitalize the first character of a string.
    let capitalize (s: string) =
        if String.IsNullOrEmpty s then s
        else s.[0..0].ToUpperInvariant() + s.[1..]

    /// Convert a string to Title Case.
    let title_case (s: string) =
        if String.IsNullOrEmpty s then s
        else
            s.Split(' ')
            |> Array.map (fun w ->
                if String.length w <= 1 then w.ToUpperInvariant()
                else w.[0..0].ToUpperInvariant() + w.[1..].ToLowerInvariant())
            |> String.concat " "

    /// Return the value if non-null/non-empty, otherwise the fallback.
    let default_value (fallback: string) (value: string) =
        if String.IsNullOrEmpty value then fallback else value

    /// Return the first non-null/non-empty string from a list.
    let coalesce (values: string list) =
        values |> List.tryFind (fun v -> not (String.IsNullOrEmpty v)) |> Option.defaultValue ""
