// @paginate 8
// @layout default

// ---------------------------------------------------------------------------
// index.zest.fsx - the home listing: the site's posts, eight per window.
//
// `// @paginate 8` hands this URL to the pagination generator, which renders
// this file once per window with `pagination` in scope: currentPage,
// totalPages, totalItems, perPage, prevUrl, nextUrl and items — the same
// fields a Zestucks listing reads off its own `pagination` object. The listing
// is the only thing printed here; `// @layout default` wraps each window in
// the paper page, which supplies the title, the navigation and the frame.
// ---------------------------------------------------------------------------

open Zest.Markup

let data = Context.get().SiteData
let siteDescription = if data.ContainsKey "site.description" then data.["site.description"].ToString() else ""

// `pagination.items` carries the same fields as sitePages(): url, title, date,
// slug, description, tags, author and category. Dates are "yyyy-MM-dd" strings.
let items =
    pagination.items
    |> Array.map (fun post ->
        let children =
            [ h2Class "post-list__title" [ aHref post.url post.title ]
              // Inline children are passed straight to the element, so the meta
              // line keeps its spacing exact.
              pClass "post-list__meta"
                  [ if post.date <> "" then
                        text (Dates.formatDateCustom post.date "MMMM d, yyyy")
                    if post.tags.Length > 0 then
                        raw " &middot; "
                        text (post.tags |> String.concat ", ") ] ]
            @ (if post.description = "" then
                   []
               else
                   [ pClass "post-list__excerpt" [ text post.description ] ])

        liClass "post-list__item" [ fragmentLines children ])
    |> String.concat "\n"

let paginationStep (label: string) (url: string) =
    if url = "" then
        spanClass "pagination__step pagination__step--off" [ text label ]
    else
        aClass "pagination__step" url [ text label ]

let body =
    [ pClass "list-intro" [ text siteDescription ]
      olClass "post-list" [ items ]
      elem "nav" [ attr "class" "pagination"; attr "aria-label" "Pagination" ]
          [ fragmentLines
                [ paginationStep "Previous" pagination.prevUrl
                  spanClass "pagination__folio"
                      [ text (sprintf "Page %d of %d" pagination.currentPage pagination.totalPages) ]
                  paginationStep "Next" pagination.nextUrl ] ] ]
    |> fragmentLines

printfn "%s" body
