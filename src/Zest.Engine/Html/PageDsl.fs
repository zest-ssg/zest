namespace Zest.Engine.Html

open System
open System.Collections.Generic
open System.Net
open System.Text
open System.Text.RegularExpressions
open Zest.Engine

// ============================================================
// Page Computation Expression
// ============================================================

type PageBuilder () =
    member _.Yield _ = Page.empty
    member _.Run(state: Page) = { state with Content = state.ContentNodes |> HtmlRenderer.render }

    [<CustomOperation "layout">]
    member _.Layout(s: Page, v)    = { s with Layout = Some v }
    [<CustomOperation "title">]
    member _.Title(s: Page, v)     = { s with Title = v }
    [<CustomOperation "permalink">]
    member _.Permalink(s: Page, v) = { s with Permalink = Some v; Url = v }
    [<CustomOperation "slug">]
    member _.Slug(s: Page, v)      = { s with Slug = v }
    [<CustomOperation "tags">]
    member _.Tags(s: Page, v)      = { s with Tags = v }
    [<CustomOperation "tag">]
    member _.Tag(s: Page, v)       = { s with Tags = v :: s.Tags }
    [<CustomOperation "date">]
    member _.Date(s: Page, v)      = { s with Date = Some v }
    [<CustomOperation "description">]
    member _.Description(s: Page, v) =
        let d = Dictionary<string, obj>()
        for kv in s.Data do d.[kv.Key] <- kv.Value
        d.["description"] <- box v
        { s with Data = d }
    [<CustomOperation "data">]
    member _.Data(s: Page, key, value : obj) =
        let d = Dictionary<string, obj>()
        for kv in s.Data do d.[kv.Key] <- kv.Value
        d.[key] <- value
        { s with Data = d }
    [<CustomOperation "content">]
    member _.Content(s: Page, nodes : HtmlNode list) =
        { s with ContentNodes = nodes }

module PageDsl =
    /// Computation expression entry point: `page { title "..."; content [...] }`.
    let page = PageBuilder()
