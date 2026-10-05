namespace Zest.Core

open System
open System.Globalization

// DateFormatter.fs
//
// Parses and formats the date strings that arrive from TOML front matter and
// the `[params]` table. Zest.Engine formats template dates and RSS/Atom
// timestamps; Zest.Dsl formats the same values inside page scripts. A single
// parser keeps "2026-08-02" from meaning two different things.
//
// Invariant: parsing never throws. Unparseable input is returned unchanged so
// a malformed date degrades to its raw text instead of failing a build.
//
// Dependencies: System, System.Globalization

/// Date parsing and formatting shared by the engine and the DSL.
module DateFormatter =

    /// Parse a date string. Prefers the invariant culture so a machine-written
    /// ISO date is never reinterpreted under the host locale.
    let tryParse (text: string) : DateTime option =
        match DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None) with
        | true, d -> Some d
        | _ -> None

    /// <summary>Format a date string with a .NET format string.</summary>
    /// <param name="format">A .NET format string, e.g. "yyyy-MM-dd".</param>
    /// <param name="text">The raw date value.</param>
    /// <returns>The formatted date, or the input unchanged when unparseable.</returns>
    let format (format: string) (text: string) : string =
        match tryParse text with
        | Some d -> d.ToString(format, CultureInfo.InvariantCulture)
        | None -> text

    /// Format a date string as yyyy-MM-dd.
    let toIsoDate (text: string) : string = format "yyyy-MM-dd" text

    /// Format a date string as an ISO 8601 UTC timestamp (RSS/Atom friendly).
    let toIso8601 (text: string) : string =
        match tryParse text with
        | Some d -> d.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        | None -> text

    /// Format a date string as RFC 2822 (used by RSS pubDate).
    let toRfc2822 (text: string) : string =
        match tryParse text with
        | Some d ->
            d.ToUniversalTime().ToString("ddd, dd MMM yyyy HH:mm:ss", CultureInfo.InvariantCulture)
            + " GMT"
        | None -> text

    /// Percent-encode a string for use inside a URL.
    let urlEncode (text: string) : string = Uri.EscapeDataString(text)

    /// Decode a percent-encoded string. Returns the input unchanged when the
    /// value is not valid escaped data (Uri.UnescapeDataString throws on a
    /// trailing lone '%').
    let urlDecode (text: string) : string =
        try Uri.UnescapeDataString(text)
        with _ -> text
