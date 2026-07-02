namespace Zest.Engine.Html

open System
open System.Net
open System.Text.RegularExpressions
open Zest.Engine

// ============================================================
// Template DSL — Partial views and layout inheritance
// ============================================================

[<AutoOpen>]
module TemplateDsl =

    /// Create a named partial view.
    let partial (name: string) = PartialBuilder(name)

    /// Render a partial view by name from the registry.
    let renderPartial (name: string) (content: HtmlNode list) =
        Element("div", ["data-partial", name], content)

    /// Slot — a placeholder for content injection (like Vue/Blazor slots).
    let slot (name: string) =
        Element("slot", ["name", name], [])

    /// Inject content into a named slot.
    let fillSlot (name: string) (content: HtmlNode list) =
        Element("template", ["slot", name], content)

    // ============================================================
    // Control Flow DSL — Match/When/Switch expressions
    // ============================================================

    /// Switch expression — evaluate a value against cases.
    let switch (value: 'T) (cases: ('T * HtmlNode list) list) (defaultCase: HtmlNode list) =
        cases |> List.tryFind (fun (v, _) -> v = value)
        |> Option.map snd
        |> Option.defaultValue defaultCase

    /// Cond expression — evaluate conditions in order, return first match.
    let cond (cases: (bool * HtmlNode) list) (fallback: HtmlNode) =
        cases |> List.tryFind fst |> Option.map snd |> Option.defaultValue fallback

    /// CondList expression — evaluate conditions, return first match as a list.
    let condList (cases: (bool * HtmlNode list) list) (fallback: HtmlNode list) =
        cases |> List.tryFind fst |> Option.map snd |> Option.defaultValue fallback

    // ============================================================
    // Expression DSL — String interpolation, pipe chains
    // ============================================================

    /// String interpolation: interp "Hello {name}" ["name", "World"]
    let interp (template: string) (vars: (string * string) list) =
        let dict = dict vars
        Regex.Replace(template, @"\{(\w+)\}", fun m ->
            match dict.TryGetValue(m.Groups.[1].Value) with
            | true, v -> v | _ -> m.Value)

    /// Safe string interpolation with HTML encoding.
    let interpSafe (template: string) (vars: (string * string) list) =
        let dict = dict vars
        Regex.Replace(template, @"\{(\w+)\}", fun m ->
            match dict.TryGetValue(m.Groups.[1].Value) with
            | true, v -> WebUtility.HtmlEncode v | _ -> m.Value)

    /// Conditional expression: choose between two values.
    let inline choose (cond: bool) (ifTrue: 'T) (ifFalse: 'T) =
        if cond then ifTrue else ifFalse

    /// Chain of conditions — like a switch on conditions.
    let chainCond (conditions: (bool * 'T) list) (fallback: 'T) =
        conditions |> List.tryFind fst |> Option.map snd |> Option.defaultValue fallback

    // ============================================================
    // Collection DSL — Enhanced list operations
    // ============================================================

    /// Filter and render: render only items matching a predicate.
    let filterRender (items: 'a list) (pred: 'a -> bool) (render: 'a -> HtmlNode) =
        items |> List.filter pred |> List.map render |> Fragment

    /// Take first N items and render.
    let takeRender (n: int) (items: 'a list) (render: 'a -> HtmlNode) =
        items |> List.truncate n |> List.map render |> Fragment

    /// Skip first N items and render.
    let skipRender (n: int) (items: 'a list) (render: 'a -> HtmlNode) =
        items |> List.skip n |> List.map render |> Fragment

    /// Chunk items into groups and render each group.
    let chunkRender (size: int) (items: 'a list) (render: 'a list -> HtmlNode) =
        items
        |> List.chunkBySize size
        |> List.map render
        |> Fragment

    /// Zip two lists and render pairs.
    let zipRender (list1: 'a list) (list2: 'b list) (render: 'a -> 'b -> HtmlNode) =
        List.zip list1 list2 |> List.map (fun (a, b) -> render a b) |> Fragment

    /// Group items by a key function and render groups.
    let groupRender (items: 'a list) (keyFn: 'a -> string) (render: string -> 'a list -> HtmlNode) =
        items
        |> List.groupBy keyFn
        |> List.map (fun (k, g) -> render k (List.ofSeq g))
        |> Fragment

    /// Paginate items and render a page.
    let paginate (page: int) (perPage: int) (items: 'a list) (render: 'a list -> HtmlNode) =
        let skipped = (page - 1) * perPage
        items |> List.skip skipped |> List.take perPage |> render

    /// Intersperse a separator between rendered items.
    let intersperse (sep: HtmlNode) (items: HtmlNode list) =
        items |> List.collect (fun n -> [sep; n]) |> List.tail |> Fragment

    // ══════════════════════════════════════════════════════════════
    // ── Collection helpers from DslSugar ─────────────────────────
    // ══════════════════════════════════════════════════════════════

    /// Map items to nodes and wrap in a container element.
    /// Usage: `map_in "ul" items (fun i -> li_text i)`
    let map_in (containerTag: string) (items: 'a list) (render: 'a -> HtmlNode) =
        Element(containerTag, [], items |> List.map render)

    /// Map items with an index.
    /// Usage: `mapi items (fun idx item -> li_text (sprintf "%d. %s" (idx+1) item))`
    let mapi (items: 'a list) (render: int -> 'a -> HtmlNode) =
        items |> List.mapi render |> Fragment
