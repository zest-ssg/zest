namespace Zest.Markup

open System

// ============================================================
// DateHelper — Date formatting and URL encoding utilities
// ============================================================
// Thin authoring-facing wrappers over Zest.Core.DateFormatter. The engine
// formats template and feed dates with the same module, so a date written in
// front matter renders identically inside a page script and inside a layout.
// ============================================================

module DateHelper =

    /// Format a date string to yyyy-MM-dd.
    let format_date (dateStr: string) =
        Zest.Core.DateFormatter.toIsoDate dateStr

    /// Format a date string with a custom format.
    let format_date_custom (dateStr: string) (fmt: string) =
        Zest.Core.DateFormatter.format fmt dateStr

    /// Format a date string to ISO 8601.
    let format_date_iso (dateStr: string) =
        Zest.Core.DateFormatter.toIso8601 dateStr

    /// Format a date string to RFC 2822.
    let format_date_rfc (dateStr: string) =
        Zest.Core.DateFormatter.toRfc2822 dateStr

    /// Add days to a date string.
    let date_add_days (dateStr: string) (days: int) =
        match Zest.Core.DateFormatter.tryParse dateStr with
        | Some d -> d.AddDays(float days).ToString("yyyy-MM-dd")
        | None -> dateStr

    /// Compute difference in days between two date strings.
    let date_diff (date1: string) (date2: string) =
        match Zest.Core.DateFormatter.tryParse date1, Zest.Core.DateFormatter.tryParse date2 with
        | Some d1, Some d2 -> int (d2 - d1).TotalDays
        | _ -> 0

    /// Current date as yyyy-MM-dd.
    let now () = DateTime.Now.ToString("yyyy-MM-dd")

    /// Current year as string.
    let current_year () = DateTime.Now.Year.ToString()

    /// URL-encode a string.
    let url_encode (s: string) = Zest.Core.DateFormatter.urlEncode s

    /// URL-decode a string.
    let url_decode (s: string) = Zest.Core.DateFormatter.urlDecode s
