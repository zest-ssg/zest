namespace Zest.Core

open System
open System.Text.RegularExpressions

// SlugFormatter.fs
//
// Turns arbitrary text into a URL-safe slug. Both the build pipeline
// (Zest.Compiler.Content) and the authoring DSL (Zest.Markup) need slugs, and a
// permalink that differs between the two would silently 404, so the rule
// lives here once.
//
// Invariant: slugify is idempotent — slugify(slugify s) = slugify s.
// Callers rely on that when a page supplies its own slug override.
//
// Dependencies: System, System.Text.RegularExpressions

/// URL-slug construction shared by the engine and the DSL.
module SlugFormatter =

    /// Matches any character that is neither a word character nor a hyphen.
    let private invalidCharsPat = Regex(@"[^\w\-]", RegexOptions.Compiled)

    /// Matches runs of two or more hyphens.
    let private multiDashPat = Regex(@"-{2,}", RegexOptions.Compiled)

    /// <summary>
    /// Convert arbitrary text into a URL-safe slug.
    /// Spaces and underscores become hyphens, invalid characters are removed,
    /// hyphen runs collapse to one, and leading/trailing hyphens are trimmed.
    /// </summary>
    /// <param name="text">Raw text such as a page title or file name stem.</param>
    /// <returns>A lowercase slug, or an empty string for null or empty input.</returns>
    let slugify (text: string) : string =
        if String.IsNullOrEmpty text then ""
        else
            // Remove invalid characters before collapsing hyphens so punctuation
            // sitting between two words cannot leave a doubled hyphen behind.
            text.ToLowerInvariant().Replace(' ', '-').Replace('_', '-')
            |> fun s -> invalidCharsPat.Replace(s, "")
            |> fun s -> multiDashPat.Replace(s, "-")
            |> fun s -> s.Trim('-')
