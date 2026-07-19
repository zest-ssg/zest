namespace Zest.Engine.Scripting

open System
open System.Collections.Generic
open Zest.Engine.Template

/// Centralised Nunjucks custom filter registration for Zest.
/// Used by both content rendering and layout rendering paths.
module FilterRegistry =

    /// Init-script-declared filters: name → pipeline spec (e.g. "upper | trim").
    /// Set by BuildEngine after running _init.zest.fsx, applied during
    /// `registerAllFilters` so every engine instance picks them up.
    let private initFilters = Dictionary<string, string>()

    /// Whether to register Zest extension filters (pages_by_tag, recent,
    /// by_collection, search). When `NunjucksCompatibility = "strict"`,
    /// these are skipped so only official-Nunjucks-compatible filters
    /// remain available. User-declared init filters are always registered.
    let private strictMode = ref false

    /// Set the init-script-declared filter specs. Called once per build
    /// after _init.zest.fsx executes.
    let setInitFilters (filters: IDictionary<string, string>) =
        initFilters.Clear()
        for kv in filters do initFilters.[kv.Key] <- kv.Value

    /// Toggle strict Nunjucks compatibility mode. When true, Zest-specific
    /// extension filters are not registered on engine instances.
    let setStrictMode (enabled: bool) = strictMode := enabled

    /// Apply a filter pipeline spec (e.g. "upper | trim") to a value by
    /// rendering a minimal template through the engine. This avoids needing
    /// a public ApplyFilter method on ITemplateEngine and works for any
    /// engine that supports the `|` filter syntax.
    let private applyPipeline (engine: ITemplateEngine) (spec: string) (value: obj) : obj =
        let ctx = Dictionary<string, obj>()
        ctx.["__zv"] <- value
        let template = sprintf "{{ __zv | %s }}" (spec.Trim())
        match engine.Render template ctx with
        | Ok s -> box s
        | Error _ -> value  // fall back to original on error

    /// Register all Zest-specific filters on the given template engine,
    /// including any init-script-declared filters.
    ///
    /// In strict Nunjucks mode (setStrictMode true), the Zest extension
    /// filters (pages_by_tag / recent / by_collection / search) are skipped
    /// so templates behave like official Nunjucks. Init-declared filters
    /// are always registered because they are user-owned, not Zest builtins.
    let registerAllFilters (engine: ITemplateEngine) =
        // ── Zest extension filters (skipped in strict mode) ──
        if not !strictMode then
            // ── pages_by_tag: filter pages by a tag ────────────
            engine.RegisterFilter "pages_by_tag" (fun value args ->
                let tag = if args.Length > 0 then args.[0] else ""
                let pages = PageQuery.getPagesForNunjucks ()
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
                PageQuery.getPagesForNunjucks ()
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
                let col = if args.Length > 0 then args.[0] else ""
                // 2nd arg `exclude_index` arrives as a string ("true"/"True"/"1").
                let excludeIndex =
                    args.Length > 1 &&
                    (match args.[1].Trim().ToLowerInvariant() with "true" | "yes" | "1" -> true | _ -> false)
                PageQuery.getPagesForNunjucks ()
                |> Array.filter (fun p ->
                    match p.TryGetValue "url" with
                    | true, (:? string as u) ->
                        let parts = u.Trim('/').Split('/')
                        let inCol = parts.Length > 0 && parts.[0].Equals(col, StringComparison.OrdinalIgnoreCase)
                        let isIndex = parts.Length <= 1
                        inCol && (not excludeIndex || not isIndex)
                    | _ -> false)
                |> Array.map (fun d -> d :> obj) |> box)

            // ── search: simple full-text search across pages ───
            engine.RegisterFilter "search" (fun value args ->
                let query = if args.Length > 0 then args.[0].ToLowerInvariant() else ""
                let pages = PageQuery.getPagesForNunjucks ()
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

        // ── where: generic attribute filter (Liquid-style, also in 11ty) ──
        // Kept available even in strict mode because Liquid and 11ty users
        // expect `where` to work.
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

        // ── init-script-declared filters (from _init.zest.fsx) ──
        // Each spec is a Nunjucks filter pipeline applied via a mini-render.
        // Always registered — these are user-owned, not Zest builtins.
        for kv in initFilters do
            let spec = kv.Value
            let name = kv.Key
            engine.RegisterFilter name (fun value _args -> applyPipeline engine spec value)
