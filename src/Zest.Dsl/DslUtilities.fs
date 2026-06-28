namespace Zest.Dsl

open System
open System.Text
open System.Text.Json
open System.Text.RegularExpressions

// ============================================================
// DslUtilities — Control flow, string, date, JSON, URL, math helpers
// ============================================================

module DslUtilities =
    open Dsl

    // ---- Control flow helpers ----
    let switch_str (value: string) (cases: (string * string) list) (defaultCase: string) =
        cases
        |> List.tryFind (fun (v, _) -> v = value)
        |> Option.map snd
        |> Option.defaultValue defaultCase

    let cond_str (cases: (bool * string) list) (fallback: string) =
        cases |> List.tryFind fst |> Option.map snd |> Option.defaultValue fallback

    let chain_cond (conditions: (bool * string) list) (fallback: string) =
        conditions |> List.tryFind fst |> Option.map snd |> Option.defaultValue fallback

    let choose (cond: bool) (ifTrue: string) (ifFalse: string) =
        if cond then ifTrue else ifFalse

    // ---- String interpolation ----
    let interp (template: string) (vars: (string * string) list) =
        let dict = dict vars
        Regex.Replace(
            template,
            @"\{(\w+)\}",
            fun m ->
                match dict.TryGetValue(m.Groups.[1].Value) with
                | true, v -> v
                | _ -> m.Value
        )

    let interp_safe (template: string) (vars: (string * string) list) =
        let dict = dict vars
        Regex.Replace(
            template,
            @"\{(\w+)\}",
            fun m ->
                match dict.TryGetValue(m.Groups.[1].Value) with
                | true, v -> htmlEncode v
                | _ -> m.Value
        )

    // ---- Collection helpers ----
    let take_n (n: int) (items: string list) = items |> List.truncate n

    let skip_n (n: int) (items: string list) =
        items |> List.skip (min n items.Length)

    let filter_by (pred: string -> bool) (items: string list) =
        items |> List.filter pred

    let map_by (f: string -> string) (items: string list) =
        items |> List.map f

    let group_by (keyFn: string -> string) (items: string list) =
        items
        |> List.groupBy keyFn
        |> List.map (fun (k, g) -> k, List.ofSeq g)

    let chunk (size: int) (items: string list) =
        items |> List.chunkBySize size

    let intersperse_str (sep: string) (items: string list) =
        items |> List.collect (fun x -> [sep; x]) |> List.tail

    let zip_lists (a: string list) (b: string list) = List.zip a b

    // ---- Data type helpers ----
    let kv (k: string) (v: obj) = (k, v)

    let kv_list (pairs: (string * obj) list) = pairs

    let kv_get (key: string) (pairs: (string * obj) list) =
        pairs |> List.tryFind (fun (k, _) -> k = key) |> Option.map snd

    // ---- Math helpers ----
    let inline sum (items: ^a list) = items |> List.sum

    let inline avg (items: ^a list) =
        if items.IsEmpty then
            LanguagePrimitives.GenericZero
        else
            (List.sum items) / (LanguagePrimitives.GenericOne * items.Length)

    let inline min_val (items: ^a list) = items |> List.min
    let inline max_val (items: ^a list) = items |> List.max

    // ---- Date helpers ----
    let format_date (dateStr: string) =
        match DateTime.TryParse(dateStr) with
        | true, d -> d.ToString("yyyy-MM-dd")
        | _ -> dateStr

    let format_date_custom (dateStr: string) (fmt: string) =
        match DateTime.TryParse(dateStr) with
        | true, d -> d.ToString(fmt)
        | _ -> dateStr

    let format_date_iso (dateStr: string) =
        match DateTime.TryParse(dateStr) with
        | true, d -> d.ToString("yyyy-MM-ddTHH:mm:ssZ")
        | _ -> dateStr

    let format_date_rfc (dateStr: string) =
        match DateTime.TryParse(dateStr) with
        | true, d -> d.ToString("ddd, dd MMM yyyy HH:mm:ss GMT")
        | _ -> dateStr

    let date_add_days (dateStr: string) (days: int) =
        match DateTime.TryParse(dateStr) with
        | true, d -> d.AddDays(float days).ToString("yyyy-MM-dd")
        | _ -> dateStr

    let date_diff (date1: string) (date2: string) =
        match DateTime.TryParse(date1), DateTime.TryParse(date2) with
        | (true, d1), (true, d2) -> int (d2 - d1).TotalDays
        | _ -> 0

    let now () = DateTime.Now.ToString("yyyy-MM-dd")
    let current_year () = DateTime.Now.Year.ToString()

    // ---- JSON helpers ----
    let json_encode (obj: obj) = JsonSerializer.Serialize(obj)
    let json_decode (json: string) = JsonDocument.Parse(json).RootElement

    // ---- URL helpers ----
    let url_encode (s: string) = Uri.EscapeDataString(s)
    let url_decode (s: string) = Uri.UnescapeDataString(s)

    // ── New: Extended string helpers ──────────────────────────────────────

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

    /// Truncate string to N characters with optional ellipsis.
    let truncate (maxLen: int) (s: string) =
        if String.length s <= maxLen then s
        else s.[..maxLen - 1] + "…"

    /// Strip all HTML tags from a string.
    let strip_html (s: string) =
        Regex.Replace(s, @"<[^>]+>", "").Trim()

    /// Estimate reading time in minutes based on average reading speed (200 wpm).
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

    /// Convert a string to Title Case (capitalize first letter of each word).
    let title_case (s: string) =
        if String.IsNullOrEmpty s then s
        else
            s.Split(' ')
            |> Array.map (fun w ->
                if String.length w <= 1 then w.ToUpperInvariant()
                else w.[0..0].ToUpperInvariant() + w.[1..].ToLowerInvariant())
            |> String.concat " "

    // ── New: Default value helpers ────────────────────────────────────────

    /// Return the value if non-null and non-empty, otherwise the fallback.
    let default_value (fallback: string) (value: string) =
        if String.IsNullOrEmpty value then fallback else value

    /// Return the first non-null, non-empty string from a list.
    let coalesce (values: string list) =
        values |> List.tryFind (fun v -> not (String.IsNullOrEmpty v)) |> Option.defaultValue ""

    // ── New: Sequence helpers ─────────────────────────────────────────────

    /// Generate a range of integers as strings from start to end (inclusive).
    let range (start: int) (endInclusive: int) =
        [ start..endInclusive ] |> List.map string

    /// Repeat a string N times.
    let repeat (count: int) (s: string) =
        StringBuilder().Insert(0, s, count).ToString()

    // ── New: Extended URL helpers ─────────────────────────────────────────

    /// Join a base URL with a relative path, handling slashes correctly.
    let url_join (baseUrl: string) (path: string) =
        let base' = baseUrl.TrimEnd('/')
        let path' = path.TrimStart('/')
        base' + "/" + path'

    /// Check whether a URL is absolute (starts with scheme).
    let is_absolute_url (url: string) =
        Regex.IsMatch(url, @"^[a-zA-Z][a-zA-Z0-9+\-.]*://")

    // ── New: Type conversion helpers ──────────────────────────────────────

    /// Convert a value to its string representation.
    let to_string (value: obj) = value.ToString()

    /// Try to parse an integer; return fallback on failure.
    let to_int (fallback: int) (s: string) =
        match Int32.TryParse(s) with
        | true, v -> v
        | _ -> fallback

    /// Try to parse a boolean; return fallback on failure.
    let to_bool (fallback: bool) (s: string) =
        match Boolean.TryParse(s) with
        | true, v -> v
        | _ -> fallback
