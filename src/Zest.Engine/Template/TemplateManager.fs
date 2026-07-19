namespace Zest.Engine.Template

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open Zest.Engine

// ============================================================
// TemplateManager — Unified template engine entry point
// ============================================================
// Handles engine selection, template caching, error reporting.
// ============================================================

/// Configuration for the template engine.
type TemplateConfig = {
    /// Which engine to use. "nunjucks" or "native" (old placeholder system).
    Engine: string
    /// Whether to cache parsed templates in memory.
    EnableCache: bool
    /// Template file extension (e.g. ".html", ".njk").
    Extension: string
    /// Optional custom filters to register.
    Filters: (string * FilterFn) list
}

module TemplateManager =

    let private engines = ConcurrentDictionary<string, ITemplateEngine>()
    let private cache = ConcurrentDictionary<string, struct(DateTime * string)>()

    let private defaultConfig: TemplateConfig = {
        Engine = "native"
        EnableCache = true
        Extension = FileExtensions.Html
        Filters = []
    }

    /// Build an engine instance for the given engine type name.
    let private createEngineInstance (engineType: string) (config: TemplateConfig) : ITemplateEngine option =
        match engineType with
        | "nunjucks" | "njk" ->
            let engine = NunjucksEngine()
            for (fnName, fn) in config.Filters do
                (engine :> ITemplateEngine).RegisterFilter fnName fn
            Some (engine :> ITemplateEngine)
        | _ ->
            None

    /// Initialize a template engine by name.
    let initEngine (name: string) (config: TemplateConfig) : ITemplateEngine option =
        match createEngineInstance name config with
        | Some engine ->
            engines.[name] <- engine
            Some engine
        | None ->
            None

    /// Get a registered engine by name.
    let getEngine (name: string) : ITemplateEngine option =
        match engines.TryGetValue name with
        | true, e -> Some e
        | _ -> None

    /// Get or create an engine.
    let getOrCreateEngine (name: string) (config: TemplateConfig) : ITemplateEngine option =
        match engines.TryGetValue name with
        | true, e -> Some e
        | _ ->
            match createEngineInstance config.Engine config with
            | Some engine ->
                engines.[name] <- engine
                Some engine
            | None ->
                None

    /// Render a template with the given engine.
    let render (engine: string) (templateText: string) (variables: IDictionary<string, obj>) : Result<string, TemplateError> =
        match engines.TryGetValue engine with
        | true, e -> e.Render templateText variables
        | _ -> Error(TemplateError.RuntimeError(sprintf "Engine '%s' not initialized" engine, 0))

    /// Render a template file with caching.
    let renderFile (engine: string) (filePath: string) (variables: IDictionary<string, obj>) : Result<string, TemplateError> =
        match engines.TryGetValue engine with
        | true, e -> e.RenderFile filePath variables
        | _ -> Error(TemplateError.RuntimeError(sprintf "Engine '%s' not initialized" engine, 0))

    /// Render a layout file, optionally using the Nunjucks engine when configured.
    /// Falls back to the native placeholder system if the engine is "native" or not available.
    let renderLayout (config: TemplateConfig) (layoutPath: string) (layoutText: string)
                     (variables: IDictionary<string, string>) : Result<string, TemplateError> =

        if config.Engine = "native" then
            Error(TemplateError.RuntimeError("use_native_fallback", 0))
        else
            match getOrCreateEngine config.Engine config with
            | Some engine ->
                let objDict = Dictionary<string, obj>()
                for kv in variables do
                    objDict.[kv.Key] <- box kv.Value
                engine.Render layoutText (objDict :> IDictionary<string, obj>)
            | None ->
                Error(TemplateError.RuntimeError("use_native_fallback", 0))

    /// Clear all engine caches (and the converter result cache).
    let clearCaches () =
        for kv in engines do
            kv.Value.ClearCache()
        cache.Clear()
        TemplateUtils.clearConversionCache ()

    /// Check if an engine is available.
    let isEngineAvailable (name: string) : bool =
        engines.ContainsKey name

    /// Get list of available engines.
    let listEngines () : string list =
        engines.Keys |> Seq.toList

    /// Convert flat key-value pairs (e.g. "site.title" → "Zest SSG")
    /// into a nested dictionary for Nunjucks engine resolution.
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

    /// Register standard Zest filters on a Nunjucks engine.
    /// Now a no-op; filter registration is handled by Scripting.FilterRegistry.
    let registerZestFilters (_engine: ITemplateEngine) (_getPages: unit -> IDictionary<string, obj>[]) =
        ()
