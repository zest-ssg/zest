// PaginationGenerator.fs
//
// Generates paginated listing pages for collections (e.g. /posts/ and
// /posts/page/2/) so long post lists stay navigable. A content file opts in
// by declaring `<!-- @paginate 5 -->` (or `<!-- @paginate posts, 5 -->`) in
// its HTML front matter; the generator then takes over rendering that URL —
// the content pipeline skips these files, so no output conflict occurs.
//
// Runs after the content pipeline so PageQuery already knows every page.
// Templates access the current window via the `pagination` context object:
// currentPage, totalPages, totalItems, items, prevUrl and nextUrl.
//
// Dependencies: Zest.Engine.Domain, Zest.Engine.Parsing, Zest.Engine.Scripting,
//               Zest.Engine.Template, Zest.Engine.Html

namespace Zest.Engine.Build

open System
open System.Collections.Generic
open System.IO
open System.Text.RegularExpressions
open Zest.Engine
open Zest.Engine.Parsing
open Zest.Engine.Scripting
open Zest.Engine.Template
open Zest.Engine.Html

/// Generates paginated listing pages for collections that opt in via the
/// `@paginate` front-matter directive in an index content file.
module PaginationGenerator =

    // Matches `<!-- @paginate 5 -->` and `<!-- @paginate posts, 5 -->`.
    let private paginatePattern = Regex(@"<!--\s*@paginate\s*([\w,\s]+?)\s*-->", RegexOptions.Compiled)

    /// Detect whether a raw file declares pagination in its front matter.
    let hasPaginateDirective (text: string) : bool =
        paginatePattern.IsMatch(text)

    /// Parse the directive value into (collection, perPage). The collection
    /// defaults to the file's parent directory when omitted (e.g. posts/).
    let private parseDirective (value: string) (collectionFallback: string) (perPageFallback: int) : string * int =
        let parts =
            value.Split([| ',' |], StringSplitOptions.RemoveEmptyEntries)
            |> Array.map (fun p -> p.Trim())
            |> Array.filter (fun p -> p.Length > 0)
        match parts with
        | [||] -> collectionFallback, perPageFallback
        | [| a |] ->
            match Int32.TryParse a with
            | true, n -> collectionFallback, n
            | _ -> a, perPageFallback
        | _ ->
            let col = parts.[0]
            match Int32.TryParse parts.[parts.Length - 1] with
            | true, n -> col, n
            | _ -> col, perPageFallback

    /// Strip front-matter comment lines so directives never leak into the
    /// rendered inner HTML — the generator wraps the fragment itself.
    let private stripFrontMatter (text: string) : string =
        Regex.Replace(text, @"^\s*<!--\s*@[a-zA-Z]+[^>]*-->\s*$", "",
                      RegexOptions.Multiline).TrimStart('\n')

    /// Build the render-context pairs mirroring ScriptEvaluator's site context,
    /// plus the pagination window exposed to the template.
    let private buildContext (config: SiteConfig)
                             (globalData: IDictionary<string, obj>)
                             (pagination: IDictionary<string, obj>)
                             (collection: string)
                             : IDictionary<string, obj> =
        let pairs = ResizeArray<string * obj>()
        pairs.Add("site.title", box config.Title)
        pairs.Add("site.description", box config.Description)
        pairs.Add("site.base_url", box config.BaseUrl)
        pairs.Add("site.version", box config.SiteVersion)
        pairs.Add("site.author", box config.Author)
        pairs.Add("site.language", box config.Language)
        for kv in globalData do
            pairs.Add("site." + kv.Key, kv.Value)
        pairs.Add("pages", box (PageQuery.getPagesForNunjucks () |> Array.map box))
        pairs.Add("tags", box (PageQuery.getTagsForNunjucks ()))
        pairs.Add("collections", box (PageQuery.getCollectionsForNunjucks ()))
        pairs.Add("collection", box collection)
        pairs.Add("pagination", box pagination)
        TemplateManager.buildNestedContext pairs

    /// Render a fragment template to inner HTML via the Nunjucks engine.
    let private renderFragment (templateBody: string)
                               (ctx: IDictionary<string, obj>) : string =
        match TemplateManager.getOrCreateEngine "nunjucks"
                  { Engine = "nunjucks"; EnableCache = true
                    Extension = FileExtensions.Nunjucks; Filters = [] } with
        | Some engine ->
            FilterRegistry.registerAllFilters engine |> ignore
            match engine.Render templateBody ctx with
            | Ok html -> html
            | Error err ->
                eprintfn "[Zest] Pagination template error: %O" err
                templateBody
        | None -> templateBody

    /// Wrap inner HTML with the site layout, mirroring the content pipeline's
    /// write path so generated pages match hand-authored ones.
    let private wrapAndWrite (page: ContentPage) (layoutName: string)
                             (config: SiteConfig) (outputDir: string)
                             (layouts: Map<string, string * string>)
                             (includes: IDictionary<string, string>)
                             (globalData: IDictionary<string, obj>) : unit =
        try
            let replacements = BuildLayout.buildReplacements page config globalData
            let finalHtml = BuildLayout.applyLayout layoutName page.Content layouts
                                replacements includes page config globalData
            let formatted =
                if config.EnableHtmlFormatting then HtmlFormatter.formatDefault finalHtml
                else finalHtml
            let outPath = Path.Combine(outputDir, page.OutputPath)
            let dir = Path.GetDirectoryName outPath
            if dir <> null then Directory.CreateDirectory(dir) |> ignore
            File.WriteAllText(outPath, formatted, System.Text.Encoding.UTF8)
        with ex ->
            // A single failing page must not abort the whole build.
            eprintfn "[Zest] Pagination page '%s' failed: %s" page.Url ex.Message

    /// Snapshot a sorted collection window into the shape templates expect:
    /// a shallow array of page dicts (url/title/date/tags/description/...).
    let private windowItems (pages: ContentPage list) (skipCount: int)
                            (perPage: int) : IDictionary<string, obj>[] =
        pages
        |> List.skip skipCount
        |> List.truncate perPage
        |> List.map PageQuery.pageToNunjucksDict
        |> Array.ofList

    /// Generate all pagination pages for a single opt-in index file.
    /// The index URL (/posts/) renders the first window; subsequent windows
    /// live at /posts/page/N/.
    let private generateCollection (filePath: string) (text: string)
                                   (collection: string) (perPage: int)
                                   (config: SiteConfig) (outputDir: string)
                                   (layouts: Map<string, string * string>)
                                   (includes: IDictionary<string, string>)
                                   (globalData: IDictionary<string, obj>)
                                   : int =
        let meta, _ = MetaParser.parse (Path.GetExtension filePath) text
        let templateBody = stripFrontMatter text
        let layoutName = meta.Layout |> Option.defaultValue config.DefaultLayout
        let title = meta.Title |> Option.defaultValue (collection + " archive")

        // All pages in the collection, newest first. The index page itself is
        // excluded because it is the template, not a list item.
        let collectionPages =
            PageQuery.getPagesByCollection collection
            |> List.filter (fun p -> not (p.Url.Trim('/').Equals(collection, StringComparison.OrdinalIgnoreCase)))
            |> List.sortByDescending (fun p -> p.Date |> Option.defaultValue DateTime.MinValue)

        let totalItems = collectionPages.Length
        // `max` is shadowed by HtmlAttributes.max (the HTML attribute builder),
        // so qualify the numeric maximum explicitly.
        let totalPages = Operators.max 1 (int (ceil (float totalItems / float perPage)))
        let baseUrl = "/" + collection.Trim('/') + "/"
        let baseRel = collection.Trim('/')

        // Clear the per-page directory before regenerating: incremental builds
        // never delete outputs, so a shrunken page count would otherwise leave
        // orphaned /posts/page/N/ files from an earlier build.
        let pageDir = Path.Combine(outputDir, baseRel, "page")
        if Directory.Exists pageDir then Directory.Delete(pageDir, recursive = true)

        let mutable generated = 0
        for pageIndex in 1 .. totalPages do
            let skipCount = (pageIndex - 1) * perPage
            let items = windowItems collectionPages skipCount perPage
            let url, outputPath =
                if pageIndex = 1 then
                    baseUrl,
                    Path.Combine(baseRel, "index.html").Replace('\\', '/')
                else
                    sprintf "%spage/%d/" baseUrl pageIndex,
                    Path.Combine(baseRel, "page", string pageIndex, "index.html").Replace('\\', '/')
            let prevUrl =
                match pageIndex with
                | 1 -> ""
                | 2 -> baseUrl
                | n -> sprintf "%spage/%d/" baseUrl (n - 1)
            let nextUrl =
                if pageIndex < totalPages then sprintf "%spage/%d/" baseUrl (pageIndex + 1)
                else ""
            let pagination =
                let d = Dictionary<string, obj>()
                d.["currentPage"] <- box pageIndex
                d.["totalPages"] <- box totalPages
                d.["totalItems"] <- box totalItems
                d.["perPage"] <- box perPage
                d.["items"] <- box items
                d.["prevUrl"] <- box prevUrl
                d.["nextUrl"] <- box nextUrl
                d :> IDictionary<string, obj>
            let ctx = buildContext config globalData pagination collection
            let inner = renderFragment templateBody ctx
            let page = { ContentPage.empty with
                            Url = url
                            OutputPath = outputPath
                            Layout = meta.Layout
                            Title = title
                            Content = inner
                            Slug = if pageIndex = 1 then collection else sprintf "%s-%d" collection pageIndex
                            Data = dict [ "description", box (sprintf "%s — page %d of %d" collection pageIndex totalPages) ]
                            SourcePath = sprintf "<pagination:%s:%d>" collection pageIndex }
            wrapAndWrite page layoutName config outputDir layouts includes globalData
            generated <- generated + 1
        generated

    /// <summary>
    /// Generate paginated listing pages for every content file that declares
    /// <c>@paginate</c> in its front matter. Returns the number of pages written.
    /// </summary>
    let generate (config: SiteConfig) (contentDir: string) (outputDir: string)
                 (layouts: Map<string, string * string>)
                 (includes: IDictionary<string, string>)
                 (globalData: IDictionary<string, obj>) : int =
        let perPageDefault = Operators.max 1 config.PaginationPerPage
        let mutable generated = 0
        if Directory.Exists contentDir then
            for filePath in Directory.EnumerateFiles(contentDir, "*.*", SearchOption.AllDirectories) do
                let ext = Path.GetExtension(filePath).ToLowerInvariant()
                let processable =
                    [ FileExtensions.Nunjucks; FileExtensions.Liquid; FileExtensions.Handlebars
                      FileExtensions.Mustache; FileExtensions.WebC; FileExtensions.Haml
                      FileExtensions.Pug; FileExtensions.Markdown; FileExtensions.MarkdownLong ]
                    |> List.exists ((=) ext)
                if processable && not (PathResolver.isExcludedWithConfig contentDir config filePath) then
                    try
                        let text = File.ReadAllText(filePath)
                        let m = paginatePattern.Match text
                        if m.Success then
                            let relPath = Path.GetRelativePath(contentDir, filePath).Replace('\\', '/')
                            let dirFallback =
                                let d = Path.GetDirectoryName(relPath)
                                if String.IsNullOrEmpty d then "" else d.Replace('\\', '/')
                            let collection, perPage = parseDirective m.Groups.[1].Value dirFallback perPageDefault
                            if collection.Length > 0 then
                                generated <- generated + generateCollection filePath text collection perPage
                                    config outputDir layouts includes globalData
                    with ex ->
                        eprintfn "[Zest] Pagination scan failed for '%s': %s" filePath ex.Message
        generated
