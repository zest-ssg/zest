// PaginationGenerator.fs
//
// Generates paginated listing pages for collections (e.g. /posts/ and
// /posts/page/2/) so long post lists stay navigable. A content file opts in
// by declaring `<!-- @paginate 5 -->` (or `<!-- @paginate posts, 5 -->`) in its
// HTML front matter — or `// @paginate 5` in a Zest Page's header; the
// generator then takes over rendering that URL — the content pipeline skips
// these files, so no output conflict occurs.
//
// Runs after the content pipeline so PageStore already knows every page.
// Templates access the current window via `pagination`: currentPage,
// totalPages, totalItems, items, prevUrl and nextUrl. A Zestucks template reads
// it as a context object; a `.zest.fsx` template gets the same fields injected
// as an F# value above the script, so either spelling of a listing template
// reads the same either way.
//
// Dependencies: Zest.Compiler.Model, Zest.Compiler.Execution, Zest.Compiler.Zestucks, Zest.Compiler.Rendering

namespace Zest.Compiler.Build
open System
open System.Collections.Generic
open System.IO
open System.Text.RegularExpressions
open Zest.Compiler.Model
open Zest.Compiler.Execution
open Zest.Compiler.Zestucks
open Zest.Compiler.Rendering

/// Generates paginated listing pages for collections that opt in via the
/// `@paginate` front-matter directive in an index content file.
module PaginationGenerator =

    // Matches `<!-- @paginate 5 -->` and `<!-- @paginate posts, 5 -->`.
    let private paginatePattern = Regex(@"<!--\s*@paginate\s*([\w,\s]+?)\s*-->", RegexOptions.Compiled)

    // The same directive in a Zest Page header, where it is an F# line comment:
    // `// @paginate 5`, `// @paginate posts, 5`.
    let private paginateScriptPattern =
        Regex(@"^\s*//\s*@paginate\s+([^\r\n]+?)\s*$", RegexOptions.Compiled ||| RegexOptions.Multiline)

    /// The `@paginate` directive of a content file, if it declares one: an HTML
    /// comment in a Zestucks template, an F# comment in a Zest Page.
    let private findPaginateDirective (filePath: string) (text: string) : string option =
        let pattern =
            if FileTypes.isZestPage filePath then paginateScriptPattern else paginatePattern
        let m = pattern.Match text
        if m.Success then Some m.Groups.[1].Value else None

    /// Parse the directive value into (collection, perPage). The collection
    /// defaults to the file's parent directory when omitted (e.g. posts/).
    ///
    /// The window size is clamped to at least 1: `@paginate 0` used to divide
    /// by zero and `@paginate -5` made `List.skip` throw, either of which
    /// aborted the whole build from inside a listing page.
    let private parseDirective (value: string) (collectionFallback: string) (perPageFallback: int) : string * int =
        let parts =
            value.Split([| ',' |], StringSplitOptions.RemoveEmptyEntries)
            |> Array.map (fun p -> p.Trim())
            |> Array.filter (fun p -> p.Length > 0)
        let windowOrDefault n = Operators.max 1 n
        match parts with
        | [||] -> collectionFallback, windowOrDefault perPageFallback
        | [| a |] ->
            match Int32.TryParse a with
            | true, n -> collectionFallback, windowOrDefault n
            | _ -> a, windowOrDefault perPageFallback
        | _ ->
            let col = parts.[0]
            match Int32.TryParse parts.[parts.Length - 1] with
            | true, n -> col, windowOrDefault n
            | _ -> col, windowOrDefault perPageFallback

    /// Snapshot one window into the shape Zestucks templates expect: a shallow
    /// array of page dicts (url/title/date/tags/description/...).
    let private windowItems (items: ContentPage list) : IDictionary<string, obj>[] =
        items
        |> List.map PageStore.pageToZestucksDict
        |> Array.ofList

    /// A generated page's identity for batching: the FSI batch, the layout batch
    /// and the writer all address a pagination page by its SourcePath.
    let private sourcePath (slugName: string) (pageIndex: int) : string =
        sprintf "<pagination:%s:%d>" slugName pageIndex

    /// The window a Zestucks template reads as the `pagination` object.
    let private paginationDict (items: IDictionary<string, obj>[]) (pageIndex: int)
                               (totalPages: int) (totalItems: int) (perPage: int)
                               (prevUrl: string) (nextUrl: string) : IDictionary<string, obj> =
        let d = Dictionary<string, obj>()
        d.["currentPage"] <- box pageIndex
        d.["totalPages"] <- box totalPages
        d.["totalItems"] <- box totalItems
        d.["perPage"] <- box perPage
        d.["items"] <- box items
        d.["prevUrl"] <- box prevUrl
        d.["nextUrl"] <- box nextUrl
        d :> IDictionary<string, obj>

    /// F# source for the `pagination` value injected above a Zest Page listing
    /// template: the same window the Zestucks path exposes as a context object,
    /// carrying the same page fields as the context's `sitePages()`, so one
    /// listing template reads the same either way.
    let private paginationBindings (items: ContentPage list) (pageIndex: int)
                                   (totalPages: int) (totalItems: int) (perPage: int)
                                   (prevUrl: string) (nextUrl: string) : string =
        let quote (s: string) =
            if isNull s then "\"\""
            else
                let escaped =
                    s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                     .Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n")
                "\"" + escaped + "\""
        // Front-matter extras live in Data; absent ones read as "" — the same
        // defaults the Zestucks dictionaries carry.
        let dataString (page: ContentPage) (key: string) =
            match page.Data.TryGetValue key with
            | true, value when not (isNull value) -> string value
            | _ -> ""
        let pageRecord (page: ContentPage) =
            sprintf "{| url = %s; title = %s; date = %s; slug = %s; description = %s; tags = [|%s|]; author = %s; category = %s |}"
                (quote page.Url)
                (quote page.Title)
                (quote (page.Date |> Option.map (fun d -> d.ToString("yyyy-MM-dd")) |> Option.defaultValue ""))
                (quote page.Slug)
                (quote (dataString page "description"))
                (page.Tags |> List.map quote |> String.concat "; ")
                (quote (dataString page "author"))
                (quote (dataString page "category"))
        // The annotation is what keeps an empty window usable: without it `[||]`
        // leaves the element type unconstrained and the template's field accesses
        // fail to infer.
        let pageShape =
            "{| url: string; title: string; date: string; slug: string; description: string; tags: string[]; author: string; category: string |}"
        String.concat "\n"
            [ sprintf "let paginationItems : %s array = [| %s |]" pageShape
                  (items |> List.map pageRecord |> String.concat "; ")
              sprintf "let pagination = {| currentPage = %d; totalPages = %d; totalItems = %d; perPage = %d; prevUrl = %s; nextUrl = %s; items = paginationItems |}"
                  pageIndex totalPages totalItems perPage (quote prevUrl) (quote nextUrl) ]

    /// One window of a paginated listing: the pages it shows and the navigation
    /// URLs around it. Every window of a template is described before any of
    /// them renders, so the F# path can evaluate the whole file in one FSI pass.
    type private PageWindow =
        { PageIndex: int
          Url: string
          OutputPath: string
          Items: ContentPage list
          PrevUrl: string
          NextUrl: string }

    /// Generate all pagination pages for a single opt-in index file.
    /// The index URL (/posts/) renders the first window; subsequent windows
    /// live at /posts/page/N/. Returns the generated pages; the caller batches
    /// the layout pass and writes them.
    let private generateCollection (allPages: ContentPage list) (filePath: string) (text: string)
                                   (collection: string) (perPage: int)
                                   (config: SiteConfig) (outputDir: string)
                                   (globalData: IDictionary<string, obj>)
                                   : ContentPage list =
        let meta, _ = FrontMatterParser.parse (Path.GetExtension filePath) text
        let templateBody = ArchiveSupport.stripFrontMatter text
        let byDateDesc (p: ContentPage) = p.Date |> Option.defaultValue DateTime.MinValue
        let title =
            match meta.Title with
            | Some t -> t
            | None when String.IsNullOrEmpty collection -> config.Title
            | None -> collection + " archive"

        // All pages in the collection, newest first. The index page itself is
        // excluded because it is the template, not a list item.
        // An empty collection name paginates the site root: every dated page
        // (posts across directories) forms the window, matching the 11ty home
        // page which lists posts globally.
        let isRoot = collection.Trim('/').Length = 0
        let collectionPages =
            if isRoot then
                allPages
                |> List.filter (fun p -> p.Date.IsSome)
                |> List.sortByDescending byDateDesc
            else
                PageStore.getPagesByCollection collection
                |> List.filter (fun p -> not (p.Url.Trim('/').Equals(collection, StringComparison.OrdinalIgnoreCase)))
                |> List.sortByDescending byDateDesc

        let totalItems = collectionPages.Length
        // `max` is shadowed by Attributes.max (the HTML attribute builder),
        // so qualify the numeric maximum explicitly.
        let totalPages = Operators.max 1 (int (ceil (float totalItems / float perPage)))
        // Root pagination emits `/` and `/page/N/`; collection pagination
        // emits `/<collection>/` and `/<collection>/page/N/`.
        let baseUrl = if isRoot then "/" else "/" + collection.Trim('/') + "/"
        let baseRel = collection.Trim('/')
        let slugName = if isRoot then "home" else collection

        // Clear the per-page directory before regenerating: incremental builds
        // never delete outputs, so a shrunken page count would otherwise leave
        // orphaned page/N/ files from an earlier build. Root pages live directly
        // under <output>/page/; collection pages under <output>/<collection>/page/.
        ArchiveSupport.clearGeneratedDir outputDir (Path.Combine(outputDir, baseRel, "page"))

        let windows =
            [ for pageIndex in 1 .. totalPages ->
                let url, outputPath =
                    if pageIndex = 1 then
                        baseUrl,
                        (if isRoot then "index.html"
                         else SitePaths.normalizeOutputRel (Path.Combine(baseRel, "index.html")))
                    else
                        sprintf "%spage/%d/" baseUrl pageIndex,
                        SitePaths.normalizeOutputRel (
                            Path.Combine(baseRel, "page", string pageIndex, "index.html"))
                let prevUrl =
                    match pageIndex with
                    | 1 -> ""
                    | 2 -> baseUrl
                    | n -> sprintf "%spage/%d/" baseUrl (n - 1)
                let nextUrl =
                    if pageIndex < totalPages then sprintf "%spage/%d/" baseUrl (pageIndex + 1)
                    else ""
                { PageIndex = pageIndex
                  Url = url
                  OutputPath = outputPath
                  Items = collectionPages |> List.skip ((pageIndex - 1) * perPage) |> List.truncate perPage
                  PrevUrl = prevUrl
                  NextUrl = nextUrl } ]

        // The body of each window. A Zestucks template interpolates per window;
        // a Zest Page is F#, so all of its windows go through one batched FSI
        // pass with their own injected `pagination` binding.
        let bodies =
            if FileTypes.isZestPage filePath then
                windows
                |> List.map (fun w ->
                    sourcePath slugName w.PageIndex,
                    paginationBindings w.Items w.PageIndex totalPages totalItems perPage w.PrevUrl w.NextUrl,
                    templateBody)
                |> FsiRunner.evaluatePageScriptsBatchWithBindings
                |> Map.map (fun _ result ->
                    match result with
                    | Ok html -> html
                    | Error err ->
                        Diagnostics.error "[Zest] Pagination script error in '%s': %s" filePath err
                        "")
            else
                windows
                |> List.map (fun w ->
                    sourcePath slugName w.PageIndex,
                    ArchiveSupport.renderFragment "Pagination" templateBody
                        (ArchiveSupport.buildContext config globalData
                            [ "collection", box collection
                              "pagination", box (paginationDict (windowItems w.Items) w.PageIndex
                                                     totalPages totalItems perPage w.PrevUrl w.NextUrl) ]))
                |> Map.ofList

        windows
        |> List.map (fun w ->
            { ContentPage.empty with
                Url = w.Url
                OutputPath = w.OutputPath
                Layout = meta.Layout
                Title = title
                Content = bodies |> Map.tryFind (sourcePath slugName w.PageIndex) |> Option.defaultValue ""
                Slug = if w.PageIndex = 1 then slugName else sprintf "%s-%d" slugName w.PageIndex
                Data = readOnlyDict [ "description", box (sprintf "%s — page %d of %d" collection w.PageIndex totalPages) ]
                SourcePath = sourcePath slugName w.PageIndex })

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
        let generatedPages = ResizeArray<ContentPage>()
        // Snapshot the page set once: it does not change during generation, and
        // the root-collection windows need it for every opt-in file.
        let allPages = PageStore.getPages()
        // Output paths already claimed, so two index files paginating the same
        // collection cannot silently overwrite each other's windows.
        let claimed = HashSet<string>(StringComparer.OrdinalIgnoreCase)
        if Directory.Exists contentDir then
            for filePath in Directory.EnumerateFiles(contentDir, "*.*", SearchOption.AllDirectories) do
                let ext = Path.GetExtension(filePath).ToLowerInvariant()
                let processable =
                    FileTypes.isZestPage filePath
                    || ([ FileTypes.Zestucks; FileTypes.Nunjucks
                          FileTypes.Markdown; FileTypes.MarkdownLong ]
                        |> List.exists ((=) ext))
                if processable && not (SitePaths.isExcludedWithConfig contentDir config filePath) then
                    try
                        let text = File.ReadAllText(filePath)
                        match findPaginateDirective filePath text with
                        | Some directive ->
                            let relPath = SitePaths.normalizeOutputRel (Path.GetRelativePath(contentDir, filePath))
                            let dirFallback =
                                let d = Path.GetDirectoryName(relPath)
                                if String.IsNullOrEmpty d then "" else SitePaths.normalizeOutputRel d
                            let collection, perPage = parseDirective directive dirFallback perPageDefault
                            // A root index file (content/index.zest.fsx) without
                            // an explicit collection paginates the site root; the
                            // directive may also name a collection explicitly.
                            let effectiveCollection =
                                if dirFallback.Length = 0 && collection = dirFallback then "" else collection
                            let pages = generateCollection allPages filePath text effectiveCollection perPage
                                            config outputDir globalData
                            for page in pages do
                                if claimed.Add page.OutputPath then
                                    generatedPages.Add page
                                    generated <- generated + 1
                                else
                                    Diagnostics.error
                                        "[Zest] Pagination page '%s' would overwrite an already generated page. \
                                         Check for two @paginate directives on the same collection."
                                        page.OutputPath
                        | None -> ()
                    with ex ->
                        Diagnostics.error "[Zest] Pagination scan failed for '%s': %s" filePath ex.Message
        ArchiveSupport.batchRenderAndWrite "Pagination" (Seq.toList generatedPages)
            config outputDir layouts includes globalData
        generated
