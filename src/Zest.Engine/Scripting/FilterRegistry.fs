namespace Zest.Engine.Scripting

open System
open System.Collections.Generic
open Zest.Engine.Template

/// Centralised Nunjucks custom filter registration for Zest.
/// Used by both content rendering and layout rendering paths.
module FilterRegistry =

    /// Register all Zest-specific filters on the given template engine.
    let registerAllFilters (engine: ITemplateEngine) =
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
            PageQuery.getPagesForNunjucks ()
            |> Array.filter (fun p ->
                match p.TryGetValue "url" with
                | true, (:? string as u) ->
                    let parts = u.Trim('/').Split('/')
                    parts.Length > 0 && parts.[0].Equals(col, StringComparison.OrdinalIgnoreCase)
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
