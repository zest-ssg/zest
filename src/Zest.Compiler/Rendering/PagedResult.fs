namespace Zest.Compiler.Rendering
open System
open System.Collections.Generic
open System.Net
open System.Text
open System.Text.RegularExpressions
open Zest.Compiler.Model
// ============================================================
// PagedResult helpers
// ============================================================

module PagedResult =

    type PaginatedResult<'a> = {
        Items: 'a list
        CurrentPage: int
        TotalPages: int
        TotalItems: int
        PreviousUrl: string option
        NextUrl: string option
        PageUrl: int -> string
    }

    /// Paginate a list of items.
    let paginate<'a> (items: 'a list) (pageSize: int) (pageUrlFn: int -> string) : PaginatedResult<'a> list =
        let total = items.Length
        // A non-positive page size would divide by zero (and `List.chunkBySize`
        // would throw); treat it as a single page-size of 1.
        let pageSize = if pageSize < 1 then 1 else pageSize
        let pages = (total + pageSize - 1) / pageSize
        [ for i in 0 .. pages - 1 ->
            let pageItems = items |> List.skip (i * pageSize) |> List.truncate pageSize
            { Items = pageItems
              CurrentPage = i + 1
              TotalPages = pages
              TotalItems = total
              PreviousUrl = if i > 0 then Some(pageUrlFn i) else None
              NextUrl = if i < pages - 1 then Some(pageUrlFn (i + 2)) else None
              PageUrl = pageUrlFn } ]

    /// Render pagination navigation.
    let renderPagination (p: PaginatedResult<'a>) : HtmlNode =
        nav [
            if p.PreviousUrl.IsSome then
                aHref p.PreviousUrl.Value "← Previous"
            span [ text (sprintf "Page %d of %d" p.CurrentPage p.TotalPages) ]
            if p.NextUrl.IsSome then
                aHref p.NextUrl.Value "Next →"
        ]
