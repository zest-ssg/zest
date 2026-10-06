// ArchiveSupport.fs
//
// Shared plumbing for the two archive generators — taxonomy terms and
// pagination windows.
//
// Both do the same four things: resolve a fragment template from the loaded
// layouts, render it with the standard site context, push every generated page
// through the layout chain in one batched FSI pass, and write the results. That
// used to exist twice, verbatim, in TaxonomyGenerator and PaginationGenerator;
// one copy means a fix to the render path or the write path lands in both.
//
// Dependencies: Zest.Compiler.Model, Zest.Compiler.Execution, Zest.Compiler.Zestucks, Zest.Compiler.Rendering

namespace Zest.Compiler.Build
open System
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.RegularExpressions
open Zest.Compiler.Model
open Zest.Compiler.Execution
open Zest.Compiler.Zestucks
open Zest.Compiler.Rendering

/// Render-context construction, fragment rendering and batched writing for
/// generated archive pages.
module ArchiveSupport =

    /// Matches a whole line that is only a front-matter directive comment.
    let private frontMatterLine =
        Regex(@"^\s*<!--\s*@[a-zA-Z]+[^>]*-->\s*$", RegexOptions.Compiled ||| RegexOptions.Multiline)

    /// Strip `<!-- @title ... -->` / `<!-- @layout ... -->` front-matter lines so
    /// they do not leak into the rendered inner HTML. The generators wrap the
    /// fragment in a layout themselves, so those directives are redundant here.
    let stripFrontMatter (text: string) : string =
        frontMatterLine.Replace(text, "").TrimStart('\n')

    /// Resolve a fragment template body from the loaded layouts, trying each key
    /// in turn and falling back to a built-in default so generation still works
    /// for a project with no theme templates.
    let resolveTemplate (layouts: Map<string, string * string>)
                        (keys: string list) (fallback: string) : string =
        let rec tryFind =
            function
            | [] -> None
            | k :: rest ->
                match layouts.TryFind k with
                | Some (_, body) -> Some body
                | None -> tryFind rest
        match tryFind keys with
        | Some body -> stripFrontMatter body
        | None -> fallback

    /// The `site.*`, `pages`, `tags` and `collections` pairs every generated
    /// page sees. Mirrors PageEvaluator's site context so an archive template
    /// and a content template can share partials.
    let siteContextPairs (config: SiteConfig)
                         (globalData: IDictionary<string, obj>) : ResizeArray<string * obj> =
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
        pairs.Add("pages", box (PageStore.getPagesForZestucks () |> Array.map box))
        pairs.Add("tags", box (PageStore.getTagsForZestucks ()))
        pairs.Add("collections", box (PageStore.getCollectionsForZestucks ()))
        pairs

    /// Site context plus generator-specific pairs.
    let buildContext (config: SiteConfig)
                     (globalData: IDictionary<string, obj>)
                     (extras: (string * obj) list) : IDictionary<string, obj> =
        let pairs = siteContextPairs config globalData
        for (k, v) in extras do pairs.Add(k, v)
        EngineHost.buildContext pairs

    /// Render a fragment template to inner HTML via the Zestucks engine.
    ///
    /// A template error does not throw: it is reported as a build diagnostic
    /// and the template source is written through unchanged, which makes the
    /// broken template visible in the output instead of silently blank.
    let renderFragment (kind: string) (templateBody: string)
                       (ctx: IDictionary<string, obj>) : string =
        let engine = EngineHost.instance
        ZestucksFilters.registerAllFilters engine |> ignore
        match engine.Render templateBody ctx with
        | Ok html -> html
        | Error err ->
            Diagnostics.error "[Zest] %s template error: %O" kind err
            templateBody

    /// Apply the layout chain to every generated page in ONE batched FSI pass
    /// and write the results.
    ///
    /// Rendering the layout per page entered FSI once per page (once per tag,
    /// once per pagination window), which dominated the build; batching cuts
    /// that to one FSI run per layout-chain level.
    let batchRenderAndWrite (kind: string)
                            (pages: ContentPage list)
                            (config: SiteConfig) (outputDir: string)
                            (layouts: Map<string, string * string>)
                            (includes: IDictionary<string, string>)
                            (globalData: IDictionary<string, obj>) : unit =
        if not pages.IsEmpty then
            let tasks =
                pages |> List.map (fun p -> p, p.Layout |> Option.defaultValue config.DefaultLayout)
            let batchedHtml =
                LayoutChain.applyLayoutsBatched tasks layouts includes config globalData

            // Output shaping lives in _finalize.fsx now; the generator writes
            // exactly what the layout chain produced.
            System.Threading.Tasks.Parallel.ForEach(pages, fun (page: ContentPage) ->
                try
                    let finalHtml =
                        match batchedHtml.TryFind page.SourcePath with
                        | Some html -> html
                        | None -> page.Content
                    // Resolves under outputDir and rejects an escaping path.
                    let outPath = SitePaths.assertWithinOutput outputDir page.OutputPath
                    let dir = Path.GetDirectoryName outPath
                    if not (String.IsNullOrEmpty dir) then Directory.CreateDirectory(dir) |> ignore
                    // Atomic replace keeps the preview server's open read handles valid.
                    AtomicFile.write outPath (Encoding.UTF8.GetBytes finalHtml)
                with ex ->
                    // One bad archive page must not abort the whole build, but
                    // it must not be swallowed either — the diagnostic fails the
                    // build at the end.
                    Diagnostics.error "[Zest] %s page '%s' failed: %s" kind page.Url ex.Message)
            |> ignore

    /// Delete a generated directory if it exists, but only when it is a strict
    /// subdirectory of the output directory.
    ///
    /// Pagination regenerates `<output>/<collection>/page/` from scratch to drop
    /// orphaned windows from a shrunken page count. A malformed collection name
    /// must not turn that into a delete of the output directory or the project.
    let clearGeneratedDir (outputDir: string) (dir: string) : unit =
        if Directory.Exists dir then
            if SitePaths.isSafeToClean outputDir dir then
                try
                    Directory.Delete(dir, recursive = true)
                with ex ->
                    Diagnostics.warn "[Zest] Could not clear generated directory '%s': %s" dir ex.Message
            else
                Diagnostics.warn
                    "[Zest] Refusing to clear '%s': it is not inside the output directory '%s'."
                    dir outputDir
