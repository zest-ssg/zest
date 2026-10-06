// TaxonomyGenerator.fs
//
// Auto-generates taxonomy archive pages (e.g. /tags/ and /tags/<term>/) so
// that adding a tag to a post is enough — no need to hand-author a .ztk file
// per tag. Runs after the content pipeline so PageStore already knows every
// page and tag.
//
// Content files always win: if /tags/foo/ is produced by a content file, the
// generator skips it. Every other archive page is rewritten on each build so
// template or config changes never leave a stale page behind.
//
// Dependencies: Zest.Compiler.Model, Zest.Compiler.Execution, Zest.Compiler.Zestucks, Zest.Compiler.Rendering

namespace Zest.Compiler.Build
open System
open System.Collections.Generic
open System.IO
open Zest.Compiler.Model
open Zest.Compiler.Execution
open Zest.Compiler.Rendering

/// Generates listing pages for taxonomy terms (tags by default).
module TaxonomyGenerator =

    /// Built-in fallback for a single term listing, used when the theme does
    /// not ship `_layouts/<singular>.ztk`. Keeps the generator useful standalone.
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
    /// ship `_layouts/<plural>.ztk`.
    let private defaultIndexTemplate = """
<div class="terms terms-index">
  <h2>{{ taxonomy.plural | capitalize }}</h2>
  <ul class="terms-tags">
    {% for t in terms %}
    <li class="term-tag"><a href="/{{ taxonomy.plural }}/{{ t.slug }}/">#{{ t.name }}</a></li>
    {% endfor %}
  </ul>
</div>
"""

    /// The layout that wraps every generated archive page.
    ///
    /// `base` is tried first because that is the name the shipped themes use
    /// for their outermost template; failing that the configured default layout
    /// is used. Hardcoding one of them made a theme that renamed its root
    /// layout generate unwrapped pages.
    let private wrapperLayout (layouts: Map<string, string * string>) (config: SiteConfig) =
        if layouts.ContainsKey "base" then "base" else config.DefaultLayout

    /// Read the term-to-slug alias table for one taxonomy kind from global
    /// data (`site.params.taxonomy.<kind>`).
    let private aliasMapFor (globalData: IDictionary<string, obj>) (kind: string) : Map<string, string> =
        match globalData.TryGetValue "params.taxonomy" with
        | true, (:? IDictionary<string, obj> as tax) ->
            match tax.TryGetValue kind with
            | true, (:? IDictionary<string, obj> as table) ->
                table |> Seq.map (fun kv -> kv.Key, string kv.Value) |> Map.ofSeq
            | _ -> Map.empty
        | _ -> Map.empty

    /// Resolve a taxonomy term to its URL slug via the alias table.
    ///
    /// An alias is user input from `_data`, so it is only honoured when it is
    /// actually a URL-safe segment: an alias of `../evil` or `a/b` would
    /// otherwise build an escaping or nested output path.
    let private termSlug (aliases: Map<string, string>) (term: string) : string =
        match Map.tryFind term aliases with
        | Some alias when Slug.isSafe alias -> alias
        | Some alias ->
            Diagnostics.warn
                "[Zest] Taxonomy alias '%s' for term '%s' is not a URL-safe slug; using the derived slug instead."
                alias term
            Slug.ofTerm term
        | None -> Slug.ofTerm term

    /// Pages and terms belonging to one taxonomy. Categories live in the
    /// independent Categories field; tags remain in Tags.
    let private taxonomyMembers (tax: TaxonomyConfig) (pages: ContentPage list)
                               : ContentPage list * string list =
        if tax.Name.Equals("category", StringComparison.OrdinalIgnoreCase) then
            pages, PageStore.getAllCategories ()
        else
            pages, PageStore.getAllTags ()

    /// True when a real content page already owns this output path or URL.
    /// Checking the in-memory page list (not the file system) is what lets
    /// generated archive pages be rewritten on every build while still letting
    /// hand-authored content files claim the same URL.
    ///
    /// Paths are compared in the compiler's canonical forward-slash form:
    /// comparing a `\`-joined generator path against a `/`-joined content path
    /// silently failed on Windows and let a generated page overwrite a content
    /// page.
    let private urlOccupiedByPage (pages: ContentPage list) (outRel: string) (url: string) : bool =
        let canonical = SitePaths.normalizeOutputRel outRel
        pages
        |> List.exists (fun p ->
            SitePaths.normalizeOutputRel p.OutputPath = canonical || p.Url = url)

    /// Generate a listing page for one taxonomy term.
    let private generateTerm (tax: TaxonomyConfig) (aliases: Map<string, string>)
                             (term: string) (pages: ContentPage list)
                             (config: SiteConfig)
                             (layouts: Map<string, string * string>)
                             (globalData: IDictionary<string, obj>) : ContentPage option =
        let slug = termSlug aliases term
        let url = sprintf "/%s/%s/" tax.Plural slug
        let outRel = SitePaths.normalizeOutputRel (Path.Combine(tax.Plural, slug, "index.html"))
        if urlOccupiedByPage pages outRel url then
            // Content file already produced this URL — keep it.
            None
        else
            let isCategory = tax.Name.Equals("category", StringComparison.OrdinalIgnoreCase)
            let belongs (p: ContentPage) =
                if isCategory then
                    p.Categories |> List.exists (fun t -> t.Equals(term, StringComparison.OrdinalIgnoreCase))
                else
                    p.Tags |> List.exists (fun t -> t.Equals(term, StringComparison.OrdinalIgnoreCase))
            let termPages =
                pages
                |> List.filter belongs
                |> List.sortByDescending (fun p -> p.Date |> Option.defaultValue DateTime.MinValue)
                |> List.map PageStore.pageToZestucksDict
                |> Array.ofList
            let taxDict = dict [
                "name", box tax.Name
                "plural", box tax.Plural
                "term", box term
                "slug", box slug
            ]
            let extras = [
                "term", box term
                "term_slug", box slug
                "term_pages", box termPages
                "taxonomy", box taxDict
            ]
            let ctx = ArchiveSupport.buildContext config globalData extras
            let body = ArchiveSupport.resolveTemplate layouts [ tax.Name; "taxonomy" ] defaultTermTemplate
            let inner = ArchiveSupport.renderFragment "Taxonomy" body ctx
            let titlePrefix = if isCategory then "Posts in " else "Posts tagged "
            Some { ContentPage.empty with
                    Url = url
                    OutputPath = outRel
                    Layout = Some (wrapperLayout layouts config)
                    Title = sprintf "%s%s" titlePrefix term
                    Content = inner
                    Slug = slug
                    Tags = [ term ]
                    Data = readOnlyDict [ "description", box (sprintf "%s%s" titlePrefix term) ]
                    SourcePath = sprintf "<taxonomy:%s:%s>" tax.Name term }

    /// Generate the terms index page for a taxonomy.
    let private generateIndex (tax: TaxonomyConfig) (aliases: Map<string, string>)
                              (terms: string list) (pages: ContentPage list)
                              (config: SiteConfig)
                              (layouts: Map<string, string * string>)
                              (globalData: IDictionary<string, obj>) : ContentPage option =
        let outRel = SitePaths.normalizeOutputRel (Path.Combine(tax.Plural, "index.html"))
        let url = sprintf "/%s/" tax.Plural
        if urlOccupiedByPage pages outRel url then None
        else
            let termEntries =
                terms
                |> List.map (fun term ->
                    let d = Dictionary<string, obj>()
                    d.["name"] <- box term
                    d.["slug"] <- box (termSlug aliases term)
                    d :> IDictionary<string, obj>)
                |> Array.ofList
            let taxDict = dict [
                "name", box tax.Name
                "plural", box tax.Plural
            ]
            let extras = [
                "taxonomy", box taxDict
                "terms", box termEntries
            ]
            let ctx = ArchiveSupport.buildContext config globalData extras
            let body = ArchiveSupport.resolveTemplate layouts [ tax.Plural; "terms" ] defaultIndexTemplate
            let inner = ArchiveSupport.renderFragment "Taxonomy" body ctx
            // `Plural` is validated as non-empty at load time, but title-casing
            // it must not throw if a SiteConfig reaches here unvalidated.
            let title =
                match tax.Plural with
                | null | "" -> tax.Name
                | p -> string (Char.ToUpperInvariant p.[0]) + p.Substring 1
            Some { ContentPage.empty with
                    Url = url
                    OutputPath = outRel
                    Layout = Some (wrapperLayout layouts config)
                    Title = title
                    Content = inner
                    Slug = tax.Plural
                    Data = readOnlyDict [ "description", box (sprintf "%s index" tax.Plural) ]
                    SourcePath = sprintf "<taxonomy:%s:index>" tax.Name }

    /// <summary>
    /// Generate taxonomy archive pages for every term discovered across pages.
    /// Handles the built-in <c>tag</c> and <c>category</c> taxonomies. Term
    /// URLs use the configured alias slugs from <c>site.params.taxonomy</c>
    /// and fall back to derived slugs for unlisted terms.
    /// </summary>
    let generate (config: SiteConfig) (outputDir: string)
                 (layouts: Map<string, string * string>)
                 (includes: IDictionary<string, string>)
                 (globalData: IDictionary<string, obj>) : int =
        let mutable generated = 0
        // Page list is fixed for the whole generation — snapshot it once so
        // URL-occupancy checks do not re-filter the full page set per term.
        let pages = PageStore.getPages()
        let generatedPages = ResizeArray<ContentPage>()
        // Output paths already claimed in this pass. Two distinct terms that
        // slug the same would write one file twice and lose an archive silently.
        let claimed = HashSet<string>(StringComparer.OrdinalIgnoreCase)
        for tax in config.Taxonomies do
            let members, terms = taxonomyMembers tax pages
            // A taxonomy nobody uses produces no pages: the default tag and
            // category taxonomies must not litter a site that tags nothing.
            if not (List.isEmpty terms) then
                let aliases =
                    if tax.Name.Equals("category", StringComparison.OrdinalIgnoreCase) then
                        aliasMapFor globalData "categories"
                    else aliasMapFor globalData "tags"
                for term in terms do
                    match generateTerm tax aliases term members config layouts globalData with
                    | Some page ->
                        if claimed.Add page.OutputPath then
                            generatedPages.Add page
                            generated <- generated + 1
                        else
                            Diagnostics.error
                                "[Zest] Taxonomy term '%s' of '%s' would write '%s', which another term already wrote. \
                                 Give one of them a distinct alias under site.params.taxonomy."
                                term tax.Name page.OutputPath
                    | None -> ()
                match generateIndex tax aliases terms members config layouts globalData with
                | Some page ->
                    if claimed.Add page.OutputPath then
                        generatedPages.Add page
                        generated <- generated + 1
                    else
                        Diagnostics.error
                            "[Zest] Taxonomy index for '%s' would overwrite '%s'." tax.Name page.OutputPath
                | None -> ()
        ArchiveSupport.batchRenderAndWrite "Taxonomy" (Seq.toList generatedPages)
            config outputDir layouts includes globalData
        generated
