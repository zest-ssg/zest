// PermalinkRouter.fs
//
// Computes URL slugs and output routes for content pages. Routing is a pure
// transformation: the same relative path and slug always produce the same
// (url, outputPath) pair, which keeps incremental builds deterministic.
//
// Invariants:
//   - URLs and output paths both use forward slashes, on every platform.
//     Output paths reach the file system through SitePaths.assertWithinOutput,
//     which converts to the platform separator exactly once, at the boundary.
//     Keeping one spelling in between is what makes an output path from a
//     generator comparable to one from a content file.
//   - Slugs contain only Unicode word characters and single hyphens.
//   - A permalink never contains a relative path segment.
//
// Dependencies: System, System.IO, Zest.Compiler.Model

namespace Zest.Compiler.Build
open System
open System.IO
open Zest.Compiler.Model

/// URL slug and route computation.
module PermalinkRouter =

    /// <summary>
    /// Convert arbitrary text into a URL-safe slug. Delegates to
    /// Zest.Core.SlugFormatter so the DSL and the router cannot drift apart.
    /// </summary>
    /// <param name="text">Raw text such as a page title or file name stem.</param>
    /// <returns>A lowercase slug, or an empty string for null or empty input.</returns>
    let slugify (text: string) : string = Zest.Core.SlugFormatter.slugify text

    /// Split a permalink into its path segments, rejecting anything that would
    /// escape the output directory.
    ///
    /// A permalink is author input and it lands directly in an output path, so
    /// `/../../etc/hosts` used to resolve outside the site. Rejecting the
    /// request with a message naming the permalink is strictly better than
    /// writing the file somewhere unintended.
    let private segments (permalink: string) : string[] =
        let parts =
            permalink.Split('/')
            |> Array.filter (fun s -> s.Length > 0)
        for part in parts do
            if part = "." || part = ".." then
                invalidArg "permalink"
                    (sprintf "'%s' is not a valid permalink: it contains the relative segment '%s'."
                             permalink part)
            if part.IndexOfAny [| '\r'; '\n'; '\000' |] >= 0 then
                invalidArg "permalink"
                    (sprintf "'%s' is not a valid permalink: it contains a control character." permalink)
        parts

    /// <summary>
    /// Parse an explicit permalink into its URL and output path.
    /// A trailing slash marks a directory-style URL that resolves to index.html.
    /// </summary>
    /// <param name="permalink">Permalink override, e.g. "/about/" or "/feed.xml".</param>
    /// <returns>The absolute URL and the output path relative to the build root, using forward slashes.</returns>
    /// <exception cref="ArgumentException">The permalink contains a relative or invalid segment.</exception>
    let computePermalink (permalink: string) : string * string =
        if String.IsNullOrEmpty permalink then
            ("/", "index.html")
        else
            let parts = segments permalink
            if parts.Length = 0 then
                ("/", "index.html")
            else
                let joined = String.concat "/" parts
                if permalink.EndsWith("/", StringComparison.Ordinal) then
                    ("/" + joined + "/", joined + "/index.html")
                else
                    ("/" + joined, joined)

    /// <summary>
    /// Derive the default route for a content file when no permalink is set.
    /// Files named "index" or "default" map to their directory; all other files
    /// map to a per-slug directory containing index.html.
    /// </summary>
    /// <param name="relPath">Path relative to the content root.</param>
    /// <param name="slug">Already-slugified file name stem.</param>
    /// <returns>The absolute URL and the output path relative to the build root, using forward slashes.</returns>
    let defaultRoute (relPath: string) (slug: string) : string * string =
        let dirName = Path.GetDirectoryName relPath
        // One normalized directory string, used for both the URL and the file
        // path, so the two can never disagree about separators or trailing slashes.
        let dir =
            SitePaths.normalizeOutputRel ((if isNull dirName then "" else dirName).Trim('/'))
        let isIndex =
            slug.Equals("index", StringComparison.OrdinalIgnoreCase) ||
            slug.Equals("default", StringComparison.OrdinalIgnoreCase)
        if isIndex then
            let url = if String.IsNullOrEmpty dir then "/" else "/" + dir + "/"
            let outPath = if String.IsNullOrEmpty dir then "index.html" else dir + "/index.html"
            (url, outPath)
        else
            let url =
                if String.IsNullOrEmpty dir then "/" + slug + "/"
                else "/" + dir + "/" + slug + "/"
            let outPath =
                if String.IsNullOrEmpty dir then slug + "/index.html"
                else dir + "/" + slug + "/index.html"
            (url, outPath)
