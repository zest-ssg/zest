namespace Zest.Engine.Template

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open Zest.Engine

// ============================================================
// TemplateManager — Zealucks template engine entry point
// ============================================================
// Zest ships exactly one template language: Zealucks, the Nunjucks-compatible
// engine that renders `.zlk` files. Because there is no engine choice left,
// this module owns a single shared engine instance instead of a registry, plus
// the bare-name search directories behind {% include %} / {% extends %}.
// ============================================================

/// Configuration for the Zealucks template engine.
type TemplateConfig = {
    /// Whether to cache parsed templates in memory.
    EnableCache: bool
    /// Template file extension. Always `.zlk`; kept so call sites that
    /// describe a template source stay self-documenting.
    Extension: string
    /// Optional custom filters to register.
    Filters: (string * FilterFn) list
}

module TemplateManager =

    let private defaultConfig: TemplateConfig = {
        EnableCache = true
        Extension = FileExtensions.Zealucks
        Filters = []
    }

    /// Additional directories searched by the Zealucks file loader after the
    /// working directory, most importantly the project's includes directory.
    /// The `{% include %}` / `{% extends %}` tags resolve bare names
    /// ("head.zlk") against these paths so templates do not need relative
    /// paths that leak the on-disk layout.
    let private templateSearchDirs = ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase)

    /// Register a directory for bare-name template resolution. No-op when the
    /// directory does not exist.
    let addTemplateSearchDir (dir: string) =
        if not (String.IsNullOrWhiteSpace dir) && Directory.Exists dir then
            templateSearchDirs.[Path.GetFullPath dir] <- dir

    /// Drop every registered search directory. Called between sites/builds.
    let clearTemplateSearchDirs () = templateSearchDirs.Clear()

    /// Build the Zealucks engine and wire its file loader to the search
    /// directories registered above.
    let private createEngine (config: TemplateConfig) : ITemplateEngine =
        let engine = ZealucksEngine()
        // Resolve includes/extends against registered directories (e.g.
        // _includes) before falling back to the working-directory path
        // produced by TemplateUtils.resolveWithinRoot.
        engine.SetLoadFile(fun path ->
            let loaded =
                seq {
                    yield path
                    let fileName = Path.GetFileName path
                    for dir in templateSearchDirs.Values do
                        yield Path.Combine(dir, fileName)
                }
                |> Seq.tryFind File.Exists
            match loaded with
            | Some f ->
                try Ok(File.ReadAllText f)
                with ex -> Error ex.Message
            | None -> Error(sprintf "Template not found: %s" path))
        for (fnName, fn) in config.Filters do
            (engine :> ITemplateEngine).RegisterFilter fnName fn
        engine :> ITemplateEngine

    /// The shared Zealucks engine. Construction is deferred because build
    /// setup registers the search directories after this module is loaded.
    let private sharedEngine = lazy createEngine defaultConfig

    /// <summary>Get the shared Zealucks engine.</summary>
    /// <returns>The process-wide engine instance. Callers must not dispose it.</returns>
    let getEngine () : ITemplateEngine = sharedEngine.Value

    /// Clear every engine-owned cache (parsed templates, tokens, file cache).
    let clearCaches () = sharedEngine.Value.ClearCache()

    /// Convert flat key-value pairs (e.g. "site.title" → "Zest SSG")
    /// into a nested dictionary for Zealucks engine resolution.
    /// "site.title" becomes { "site": { "title": "Zest SSG" } },
    /// while "content" stays as { "content": "..." }.
    let buildNestedContext (pairs: (string * obj) seq) : IDictionary<string, obj> =
        let root = Dictionary<string, obj>()
        for key, value in pairs do
            let parts = key.Split('.')
            if parts.Length = 1 then
                root.[key] <- value
            else
                let mutable current = root :> IDictionary<string, obj>
                for i in 0..parts.Length - 2 do
                    let part = parts.[i]
                    match current.TryGetValue part with
                    | true, (:? IDictionary<string, obj> as sub) ->
                        current <- sub
                    | _ ->
                        let sub = Dictionary<string, obj>()
                        current.[part] <- box sub
                        current <- sub
                current.[parts.[parts.Length - 1]] <- value
        root :> IDictionary<string, obj>
