namespace Zest.Engine.Template

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO

// ============================================================
// TemplateManager — Unified template engine entry point
// ============================================================
// Handles engine selection, template caching, error reporting.
// ============================================================

/// Configuration for the template engine.
type TemplateConfig = {
    /// Which engine to use. "znjk" or "native" (old placeholder system).
    Engine: string
    /// Whether to cache parsed templates in memory.
    EnableCache: bool
    /// Template file extension (e.g. ".html", ".znjk").
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
        Extension = ".html"
        Filters = []
    }

    /// Initialize a template engine by name.
    let initEngine (name: string) (config: TemplateConfig) : ITemplateEngine option =
        match name with
        | "znjk" ->
            let engine = ZestNjkEngine()
            for (fnName, fn) in config.Filters do
                (engine :> ITemplateEngine).RegisterFilter fnName fn
            engines.[name] <- engine
            Some (engine :> ITemplateEngine)
        | _ ->
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
        | _ -> initEngine name config

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

    /// Clear all engine caches.
    let clearCaches () =
        for kv in engines do
            kv.Value.ClearCache()
        cache.Clear()

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
    /// These filters provide access to Zest's page data from templates.
    /// The `getPages` callback returns the current page data array.
    let registerZestFilters (engine: ITemplateEngine) (getPages: unit -> IDictionary<string, obj>[]) =
        // ── pages_by_tag: filter pages by a tag ────────────
        engine.RegisterFilter "pages_by_tag" (fun value args ->
            let tag = if args.Length > 0 then args.[0] else ""
            let pages = getPages ()
            pages
            |> Array.filter (fun p ->
                match p.TryGetValue "tags" with
                | true, (:? (string[]) as tags) ->
                    tags |> Array.exists (fun t -> t.Equals(tag, StringComparison.OrdinalIgnoreCase))
                | _ -> false)
            |> Array.map (fun d -> d :> obj) |> box)

        // ── recent: get N most recent pages ────────────────
        engine.RegisterFilter "recent" (fun value args ->
            let n = if args.Length > 0 then (try int args.[0] with _ -> 5) else 5
            getPages ()
            |> Array.filter (fun p ->
                match p.TryGetValue "date" with
                | true, (:? string as d) -> d <> ""
                | _ -> false)
            |> Array.sortByDescending (fun p ->
                match p.TryGetValue "date" with
                | true, (:? string as d) -> d
                | _ -> "")
            |> Array.truncate n
            |> Array.map (fun d -> d :> obj) |> box)

        // ── by_collection: filter pages by collection name ─
        engine.RegisterFilter "by_collection" (fun value args ->
            let name = if args.Length > 0 then args.[0] else ""
            let pages = getPages ()
            pages
            |> Array.filter (fun p ->
                match p.TryGetValue "collection" with
                | true, (:? string as c) ->
                    c.Equals(name, StringComparison.OrdinalIgnoreCase)
                | _ -> false)
            |> Array.map (fun d -> d :> obj) |> box)

        // ── search: simple full-text search across pages ───
        engine.RegisterFilter "search" (fun value args ->
            let query = if args.Length > 0 then args.[0].ToLowerInvariant() else ""
            let pages = getPages ()
            if query = "" then pages |> Array.map (fun d -> d :> obj) |> box
            else
                pages
                |> Array.filter (fun p ->
                    [ "title"; "content"; "excerpt"; "description" ]
                    |> List.exists (fun key ->
                        match p.TryGetValue key with
                        | true, (:? string as s) ->
                            s.ToLowerInvariant().Contains(query)
                        | _ -> false))
                |> Array.map (fun d -> d :> obj) |> box)

        // ── where: generic attribute filter (like Liquid's where) ──
        engine.RegisterFilter "where" (fun value args ->
            let key = if args.Length > 0 then args.[0] else ""
            let expected = if args.Length > 1 then args.[1] else ""
            let toStr (v: obj) = if isNull v then "" else v.ToString()
            match value with
            | :? System.Collections.IEnumerable as ie ->
                ie |> Seq.cast<obj>
                |> Seq.filter (fun item ->
                    match item with
                    | :? IDictionary<string, obj> as d ->
                        match d.TryGetValue key with
                        | true, v -> toStr v = expected
                        | _ -> false
                    | _ -> false)
                |> Array.ofSeq :> obj
            | _ -> value)
