namespace Zest.Dsl

open System

// ============================================================
// DateHelper — Date formatting and URL encoding utilities
// ============================================================

module DateHelper =

    /// Format a date string to yyyy-MM-dd.
    let format_date (dateStr: string) =
        match DateTime.TryParse(dateStr) with
        | true, d -> d.ToString("yyyy-MM-dd")
        | _ -> dateStr

    /// Format a date string with a custom format.
    let format_date_custom (dateStr: string) (fmt: string) =
        match DateTime.TryParse(dateStr) with
        | true, d -> d.ToString(fmt)
        | _ -> dateStr

    /// Format a date string to ISO 8601.
    let format_date_iso (dateStr: string) =
        match DateTime.TryParse(dateStr) with
        | true, d -> d.ToString("yyyy-MM-ddTHH:mm:ssZ")
        | _ -> dateStr

    /// Format a date string to RFC 2822.
    let format_date_rfc (dateStr: string) =
        match DateTime.TryParse(dateStr) with
        | true, d -> d.ToString("ddd, dd MMM yyyy HH:mm:ss GMT")
        | _ -> dateStr

    /// Add days to a date string.
    let date_add_days (dateStr: string) (days: int) =
        match DateTime.TryParse(dateStr) with
        | true, d -> d.AddDays(float days).ToString("yyyy-MM-dd")
        | _ -> dateStr

    /// Compute difference in days between two date strings.
    let date_diff (date1: string) (date2: string) =
        match DateTime.TryParse(date1), DateTime.TryParse(date2) with
        | (true, d1), (true, d2) -> int (d2 - d1).TotalDays
        | _ -> 0

    /// Current date as yyyy-MM-dd.
    let now () = DateTime.Now.ToString("yyyy-MM-dd")

    /// Current year as string.
    let current_year () = DateTime.Now.Year.ToString()

    /// URL-encode a string.
    let url_encode (s: string) = Uri.EscapeDataString(s)

    /// URL-decode a string.
    let url_decode (s: string) = Uri.UnescapeDataString(s)
