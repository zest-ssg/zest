namespace Zest.Core

open System
open System.Text.RegularExpressions

// TextMetrics.fs
//
// Counts prose in HTML fragments: word counts, reading time, and tag
// stripping. Zest.Compiler uses these for the `wordCount` / `readingTime`
// template filters; Zest.Markup exposes the same numbers to page scripts. Both
// must agree, otherwise a feed and its page report different lengths.
//
// Invariant: every count is computed on prose only. Fenced and inline code
// blocks are removed first, so code samples never inflate a statistic.
//
// Dependencies: System, System.Text.RegularExpressions

/// Prose measurement shared by the engine and the DSL.
module TextMetrics =

    /// CJK ideographs (CJK Unified Ideographs + Extension A).
    let private cjkPat = Regex(@"[一-鿿㐀-䶿]", RegexOptions.Compiled)

    /// Latin/digit word groups.
    let private latinPat = Regex(@"[a-zA-Z0-9]+", RegexOptions.Compiled)

    /// <summary>Strip HTML tags plus fenced and inline code blocks.</summary>
    /// <param name="text">Raw HTML fragment, or null.</param>
    /// <returns>Prose with all markup removed, or an empty string for null.</returns>
    let stripProse (text: string) : string =
        if isNull text then ""
        else
            Regex(@"<pre[^>]*>[\s\S]*?<\/pre>").Replace(text, "")
            |> fun s -> Regex(@"<code[^>]*>[\s\S]*?<\/code>").Replace(s, "")
            |> fun s -> Regex(@"<[^>]+>").Replace(s, " ")
            |> fun s -> s.Trim()

    /// <summary>Count CJK ideographs plus Latin/digit word groups in HTML prose.</summary>
    /// <param name="text">Raw HTML fragment, or null.</param>
    /// <returns>Each Hanzi counts as one word; English words are contiguous
    /// letter/digit runs. Returns 0 for null input.</returns>
    let countWords (text: string) : int =
        let stripped = stripProse text
        cjkPat.Matches(stripped).Count + latinPat.Matches(stripped).Count

    /// <summary>Estimate reading time in whole minutes.</summary>
    /// <param name="text">Raw HTML fragment, or null.</param>
    /// <returns>At least 1 minute. Chinese reads at ~350 characters per
    /// minute, English at ~220 words per minute.</returns>
    let readingMinutes (text: string) : int =
        let stripped = stripProse text
        let chineseChars = cjkPat.Matches(stripped).Count
        let englishWords = latinPat.Matches(stripped).Count
        Math.Max(1, Math.Ceiling(float chineseChars / 350. + float englishWords / 220.) |> int)
