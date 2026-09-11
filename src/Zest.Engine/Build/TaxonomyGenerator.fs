// TaxonomyGenerator.fs
//
// Auto-generates taxonomy archive pages (e.g. /tags/ and /tags/<term>/) so
// that adding a tag to a post is enough — no need to hand-author a .njk file
// per tag. Runs after the content pipeline so PageQuery already knows every
// page and tag.
//
// Content files always win: if /tags/foo/ is produced by a content file, the
// generator skips it. Every other archive page is rewritten on each build so
// template or config changes never leave a stale page behind.
//
// Dependencies: Zest.Engine.Domain, Zest.Engine.Scripting, Zest.Engine.Template, Zest.Engine.Html

namespace Zest.Engine.Build

open System
open System.Collections.Generic
open System.IO
open System.Text.RegularExpressions
open Zest.Engine
open Zest.Engine.Scripting
open Zest.Engine.Template
open Zest.Engine.Html

/// Generates listing pages for taxonomy terms (tags by default).
module TaxonomyGenerator =

    /// Built-in fallback for a single term listing, used when the theme does
    /// not ship `_layouts/<singular>.njk`. Keeps the generator useful standalone.
    let private defaultTermTemplate = """
<div class="posts tag-posts">
  <h2>{{ term }}</h2>
  {% for p in term_pages %}
  <article class="post">
    <div class="post-title"><a href="{{ p.url }}">{{ p.title }}</a></div>
    <div class="post-meta">{{ p.date | date("MMM d, yyyy") }}</div>
    {% if p.description %}<div class="post-summary">{{ p.description }}</div>{% endif %}
  </article>
  {% else %}
  <p>No posts found.</p>
  {% endfor %}
</div>
"""

    /// Built-in fallback for the terms index, used when the theme does not
    /// ship `_layouts/<plural>.njk`.
    let private defaultIndexTemplate = """
<div class="terms terms-index">
  <h2>{{ taxonomy.plural | capitalize }}</h2>
  <ul class="terms-tags">
    {% for t in tags %}
    <li class="term-tag"><a href="/{{ taxonomy.plural }}/{{ t }}/">#{{ t }}</a></li>
    {% endfor %}
  </ul>
</div>
"""

    /// Strip `<!-- @title ... -->` / `<!-- @layout ... -->` frontmatter
    /// comments so they do not leak into the rendered inner HTML. The
    /// generator wraps the fragment in a layout itself, so these directives
    /// are redundant here.
    let private stripFrontMatter (text: string) : string =
        Regex.Replace(text, @"^\s*<!--\s*@[a-zA-Z]+[^>]*-->\s*$", "",
                      RegexOptions.Multiline).TrimStart('\n')

    /// Resolve a template body from the loaded layouts, falling back to a
    /// built-in default so generation works even without a theme template.
    let private resolveTemplate (layouts: Map<string, string * string>)
                                (keys: string list) (fallback: string) : string =
        let rec tryFind =
            function
            | [] -> None
            | k :: rest ->
                match layouts.TryFind k with
                | Some (_, body) -> Some body
                | None -> tryFind rest
        match tryFind keys with Some b -> stripFrontMatter b | None -> fallback

    /// Build the standard render-context pairs: site.* (mirroring
    /// ScriptEvaluator.getNunjucksSiteContext) plus the taxonomy extras.
    let private buildContext (config: SiteConfig)
                             (globalData: IDictionary<string, obj>)
                             (extras: (string * obj) list)
                             : IDictionary<string, obj> =
        let pairs = ResizeArray<string * obj>()
        pairs.Add("site.title", box config.Title)
        pairs.Add("site.description", box config.Description)
        pairs.Add("site.base_url", box config.BaseUrl)
        pairs.Add("site.version", box config.SiteVersion)
        pairs.Add("site.author", box config.Author)
        pairs.Add("site.language", box config.Language)
        // Surface every global data key under site. so site.params.*,
        // site.nav.*, site.socials, etc. resolve in content templates too.
        for kv in globalData do
            pairs.Add("site." + kv.Key, kv.Value)
        // Collection data shared with all templates.
        pairs.Add("pages", box (PageQuery.getPagesForNunjucks () |> Array.map box))
        pairs.Add("tags", box (PageQuery.getTagsForNunjucks ()))
        pairs.Add("collections", box (PageQuery.getCollectionsForNunjucks ()))
        for (k, v) in extras do pairs.Add(k, v)
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
                eprintfn "[Zest] Taxonomy template error: %O" err
                templateBody
        | None -> templateBody

    /// Apply the layout chain to all generated pages in ONE batched FSI pass
    /// and write the results. Rendering layout per page entered FSI ~25 times
    /// (once per tag), which dominated the whole build; batching cuts that to
    /// one FSI run per layout-chain level.
    let private batchRenderAndWrite (pages: ContentPage list)
                                    (config: SiteConfig) (outputDir: string)
                                    (layouts: Map<string, string * string>)
                                    (includes: IDictionary<string, string>)
                                    (globalData: IDictionary<string, obj>) : unit =
        if pages.IsEmpty then ()
        else
            let tasks =
                pages |> List.map (fun p -> p, (p.Layout |> Option.defaultValue config.DefaultLayout))
            let batchedHtml =
                LayoutEngine.applyLayoutsBatched tasks layouts includes config globalData

            // Post-processing (format or minify) is pulled out of the write loop.
            // Formatting takes priority over minification.
            let htmlPostProcess =
                if config.EnableHtmlFormatting then HtmlFormatter.formatDefault
                else HtmlFormatter.minifySafe
            let needsHtmlPostProcess = config.EnableHtmlFormatting || config.EnableHtmlMinification
            let processedHtml =
                if needsHtmlPostProcess then
                    pages
                    |> Seq.map (fun (page: ContentPage) ->
                        let raw =
                            match batchedHtml.TryFind page.SourcePath with
                            | Some html -> html
                            | None -> page.Content
                        page.SourcePath, htmlPostProcess raw)
                    |> Map.ofSeq
                else Map.empty

            System.Threading.Tasks.Parallel.ForEach(pages, fun (page: ContentPage) ->
                try
                    let finalHtml =
                        if needsHtmlPostProcess then processedHtml.[page.SourcePath]
                        else
                            match batchedHtml.TryFind page.SourcePath with
                            | Some html -> html
                            | None -> page.Content
                    let outPath = Path.Combine(outputDir, page.OutputPath)
                    let dir = Path.GetDirectoryName outPath
                    if dir <> null then Directory.CreateDirectory(dir) |> ignore
                    // Atomic replace keeps the preview server's open read handles valid.
                    AtomicFile.write outPath (System.Text.Encoding.UTF8.GetBytes finalHtml)
                with ex ->
                    // A single failing term must not abort the whole build.
                    eprintfn "[Zest] Taxonomy page '%s' failed: %s" page.Url ex.Message) |> ignore

    /// True when a real content page already owns this output path or URL.
    /// Checking the in-memory page list (not the file system) is what lets
    /// generated archive pages be rewritten on every build while still letting
    /// hand-authored content files claim the same URL.
    let private urlOccupiedByPage (pages: ContentPage list) (outRel: string) (url: string) : bool =
        pages
        |> List.exists (fun p -> p.OutputPath = outRel || p.Url = url)

    /// Generate a listing page for one taxonomy term.
    let private generateTerm (tax: TaxonomyConfig) (term: string) (pages: ContentPage list)
                             (config: SiteConfig)
                             (layouts: Map<string, string * string>)
                             (includes: IDictionary<string, string>)
                             (globalData: IDictionary<string, obj>) : ContentPage option =
        let url = sprintf "/%s/%s/" tax.Plural term
        let outRel = Path.Combine(tax.Plural, term, "index.html").Replace('\\', '/')
        if urlOccupiedByPage pages outRel url then
            // Content file already produced this URL — keep it.
            None
        else
            let termPages =
                pages
                |> List.filter (fun p -> p.Tags |> List.exists (fun t -> t.Equals(term, StringComparison.OrdinalIgnoreCase)))
                |> List.sortByDescending (fun p -> p.Date |> Option.defaultValue DateTime.MinValue)
                |> List.map PageQuery.pageToNunjucksDict
                |> Array.ofList
            let taxDict = dict [
                "name", box tax.Name
                "plural", box tax.Plural
                "term", box term
            ]
            let extras = [
                "term", box term
                "term_pages", box termPages
                "taxonomy", box taxDict
            ]
            let ctx = buildContext config globalData extras
            let body = resolveTemplate layouts [ tax.Name; "taxonomy" ] defaultTermTemplate
            let inner = renderFragment body ctx
            Some { ContentPage.empty with
                    Url = url
                    OutputPath = outRel
                    Layout = Some "base"
                    Title = sprintf "Posts tagged %s" term
                    Content = inner
                    Slug = term
                    Data = dict [ "description", box (sprintf "Posts tagged %s" term) ]
                    SourcePath = sprintf "<taxonomy:%s:%s>" tax.Name term }

    /// Generate the terms index page for a taxonomy.
    let private generateIndex (tax: TaxonomyConfig) (pages: ContentPage list)
                              (config: SiteConfig)
                              (layouts: Map<string, string * string>)
                              (includes: IDictionary<string, string>)
                              (globalData: IDictionary<string, obj>) : ContentPage option =
        let outRel = Path.Combine(tax.Plural, "index.html").Replace('\\', '/')
        let url = sprintf "/%s/" tax.Plural
        if urlOccupiedByPage pages outRel url then None
        else
            let taxDict = dict [
                "name", box tax.Name
                "plural", box tax.Plural
            ]
            let extras = [ "taxonomy", box taxDict ]
            let ctx = buildContext config globalData extras
            let body = resolveTemplate layouts [ tax.Plural; "terms" ] defaultIndexTemplate
            let inner = renderFragment body ctx
            Some { ContentPage.empty with
                    Url = sprintf "/%s/" tax.Plural
                    OutputPath = outRel
                    Layout = Some "base"
                    Title = sprintf "%s" (tax.Plural.Substring(0,1).ToUpper() + tax.Plural.Substring(1))
                    Content = inner
                    Slug = tax.Plural
                    Data = dict [ "description", box (sprintf "%s index" tax.Plural) ]
                    SourcePath = sprintf "<taxonomy:%s:index>" tax.Name }

    /// <summary>
    /// Generate taxonomy archive pages for every term discovered across pages.
    /// Currently handles the <c>tag</c> taxonomy (terms from page frontmatter
    /// <c>@tags</c>). Other taxonomies are skipped — extensible per-taxonomy.
    /// </summary>
    let generate (config: SiteConfig) (outputDir: string)
                 (layouts: Map<string, string * string>)
                 (includes: IDictionary<string, string>)
                 (globalData: IDictionary<string, obj>) : int =
        let mutable generated = 0
        // Page list is fixed for the whole generation — snapshot it once so
        // URL-occupancy checks do not re-filter the full page set per term.
        let pages = PageQuery.getPages()
        let generatedPages = ResizeArray<ContentPage>()
        for tax in config.Taxonomies do
            // Terms are only extractable for the tag taxonomy today; pages
            // carry tags via ContentPage.Tags, which PageQuery.getAllTags uses.
            if tax.Name = "tag" then
                let terms = PageQuery.getAllTags()
                for term in terms do
                    match generateTerm tax term pages config layouts includes globalData with
                    | Some page ->
                        generatedPages.Add(page); generated <- generated + 1
                    | None -> ()
                match generateIndex tax pages config layouts includes globalData with
                | Some page ->
                    generatedPages.Add(page); generated <- generated + 1
                | None -> ()
        batchRenderAndWrite (Seq.toList generatedPages) config outputDir layouts includes globalData
        generated
