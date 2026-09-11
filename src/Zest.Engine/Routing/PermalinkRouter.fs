// PermalinkRouter.fs
//
// Computes URL slugs and output routes for content pages. Routing is a pure
// transformation: the same relative path and slug always produce the same
// (url, outputPath) pair, which keeps incremental builds deterministic.
//
// Invariants:
//   - URLs always use forward slashes; output paths use the platform separator.
//   - Slugs contain only Unicode word characters and single hyphens.
//
// Dependencies: System, System.IO, System.Text.RegularExpressions

namespace Zest.Engine.Routing

open System
open System.IO
open System.Text.RegularExpressions

/// URL slug and route computation.
module PermalinkRouter =

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

    /// <summary>
    /// Parse an explicit permalink into its URL and output path.
    /// A trailing slash marks a directory-style URL that resolves to index.html.
    /// </summary>
    /// <param name="permalink">Permalink override, e.g. "/about/" or "/feed.xml".</param>
    /// <returns>The absolute URL and the output path relative to the build root.</returns>
    let computePermalink (permalink: string) : string * string =
        let separator = Path.DirectorySeparatorChar
        if String.IsNullOrEmpty permalink then
            ("/", "index.html")
        else
            let normalized = permalink.Trim('/')
            if String.IsNullOrEmpty normalized then
                ("/", "index.html")
            elif permalink.EndsWith("/", StringComparison.Ordinal) then
                let relative = normalized.Replace('/', separator)
                ("/" + normalized + "/", relative + string separator + "index.html")
            else
                ("/" + normalized, normalized.Replace('/', separator))

    /// <summary>
    /// Derive the default route for a content file when no permalink is set.
    /// Files named "index" or "default" map to their directory; all other files
    /// map to a per-slug directory containing index.html.
    /// </summary>
    /// <param name="relPath">Path relative to the content root.</param>
    /// <param name="slug">Already-slugified file name stem.</param>
    /// <returns>The absolute URL and the output path relative to the build root.</returns>
    let defaultRoute (relPath: string) (slug: string) : string * string =
        let dirName = Path.GetDirectoryName(relPath)
        let isIndex =
            slug.Equals("index", StringComparison.OrdinalIgnoreCase) ||
            slug.Equals("default", StringComparison.OrdinalIgnoreCase)
        // Normalize the directory once; URL segments and file paths share it.
        let dir =
            if String.IsNullOrEmpty dirName then ""
            else dirName.Replace('\\', '/').Trim('/')
        if isIndex then
            let url = if String.IsNullOrEmpty dir then "/" else "/" + dir + "/"
            let outPath =
                if String.IsNullOrEmpty dirName then "index.html"
                else Path.Combine(dirName, "index.html")
            (url, outPath)
        else
            let url =
                if String.IsNullOrEmpty dir then "/" + slug + "/"
                else "/" + dir + "/" + slug + "/"
            let outPath =
                if String.IsNullOrEmpty dirName then Path.Combine(slug, "index.html")
                else Path.Combine(dirName, slug, "index.html")
            (url, outPath)
