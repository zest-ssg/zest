namespace Zest.Engine.Routing

open System
open System.IO
open System.Text.RegularExpressions

/// URL slug and route computation.
module PermalinkRouter =

    let slugify (text: string) : string =
        if String.IsNullOrEmpty text then ""
        else
            text.ToLowerInvariant()
                .Replace(" ", "-")
                .Replace("_", "-")
            |> fun s -> Regex.Replace(s, @"[^\w\-]", "")
            |> fun s -> Regex.Replace(s, @"-{2,}", "-")
            |> fun s -> s.Trim('-')

    /// Parse an explicit permalink string into (url, outputPath).
    let computePermalink (permalink: string) : string * string =
        let normalized = permalink.Trim('/')
        if String.IsNullOrEmpty normalized then
            ("/", "index.html")
        elif permalink.EndsWith "/" then
            ("/" + normalized + "/",
             normalized.Replace('/', Path.DirectorySeparatorChar)
             + string Path.DirectorySeparatorChar + "index.html")
        else
            ("/" + normalized, normalized.Replace('/', Path.DirectorySeparatorChar))

    /// Derive default route (url, outputPath) from filesystem path.
    let defaultRoute (relPath: string) (slug: string) : string * string =
        let dirName = Path.GetDirectoryName(relPath)
        let isIndex =
            slug.Equals("index", StringComparison.OrdinalIgnoreCase) ||
            slug.Equals("default", StringComparison.OrdinalIgnoreCase)
        let dir =
            if String.IsNullOrEmpty dirName then ""
            else dirName.Replace('\\', '/')
        if isIndex then
            let url = if String.IsNullOrEmpty dir then "/" else "/" + dir.Trim('/') + "/"
            let outPath =
                if String.IsNullOrEmpty dirName then "index.html"
                else Path.Combine(dirName, "index.html")
            (url, outPath)
        else
            let url =
                if String.IsNullOrEmpty dir then "/" + slug + "/"
                else "/" + dir.Trim('/') + "/" + slug + "/"
            let outPath =
                if String.IsNullOrEmpty dirName then Path.Combine(slug, "index.html")
                else Path.Combine(dirName, slug, "index.html")
            (url, outPath)
