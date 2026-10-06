// Slug.fs
//
// One slug policy for the whole compiler. Taxonomy term archives, permalink
// routing and the `slugify` template filter must agree: if they disagree, a
// term that appears in a URL and in a generated filename can be spelled two
// different ways and the page 404s.
//
// Dependencies: System.Security.Cryptography, System.Text

namespace Zest.Compiler.Model
open System
open System.Security.Cryptography
open System.Text
open System.Text.RegularExpressions

/// URL slug generation.
module Slug =

    let private dashRun = Regex("-{2,}", RegexOptions.Compiled)
    let private nonAscii = Regex("[^a-z0-9-]", RegexOptions.Compiled)
    let private whitespace = Regex(@"\s+", RegexOptions.Compiled)

    /// ASCII slug for a term: lower-cased, whitespace runs to single dashes,
    /// everything outside [a-z0-9-] dropped, leading/trailing dashes trimmed.
    ///
    /// Returns "" when nothing survives — a purely non-Latin term, or one made
    /// entirely of punctuation. Use `ofTerm` unless you have your own fallback.
    let private asciiSlug (term: string) : string =
        whitespace.Replace(term.ToLowerInvariant(), "-")
        |> fun s -> nonAscii.Replace(s, "")
        |> fun s -> dashRun.Replace(s, "-")
        |> fun s -> s.Trim('-')

    /// Short deterministic hex digest of a term, used to keep terms that reduce
    /// to the same ASCII slug apart.
    let private digest (term: string) : string =
        term
        |> Encoding.UTF8.GetBytes
        |> SHA256.HashData
        |> Array.take 4
        |> Array.map (fun b -> b.ToString("x2"))
        |> String.concat ""

    /// Slug for a taxonomy term or a page title.
    ///
    /// Terms that survive ASCII folding use the folded form (`"Hello World"` →
    /// `hello-world`). Terms that do not — Chinese, Cyrillic, emoji — become
    /// `term-<hash>`.
    ///
    /// The fallback is hashed rather than a constant like `untitled` on
    /// purpose: two distinct non-Latin terms must never resolve to one output
    /// path, where the second archive page would overwrite the first and the
    /// term would silently disappear from the site.
    let ofTerm (term: string) : string =
        if String.IsNullOrWhiteSpace term then "term-" + digest ""
        else
            match asciiSlug term with
            | "" -> "term-" + digest term
            | slug -> slug

    /// Slug with a caller-supplied value used when ASCII folding yields nothing.
    /// The fallback is returned verbatim, so it must already be URL-safe and,
    /// if it can collide, unique.
    let ofTermWithFallback (fallback: string) (term: string) : string =
        match asciiSlug term with
        | "" -> fallback
        | slug -> slug

    /// True when `slug` is non-empty and URL-safe (letters, digits, dash).
    /// Used to reject a configured alias that would break out of its segment.
    let isSafe (slug: string) =
        not (String.IsNullOrEmpty slug)
        && (slug |> Seq.forall (fun c ->
                (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c = '-' || c = '_'))
