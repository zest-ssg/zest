// @permalink /archive/
// @layout default
// @title Archive
// @description Every post, grouped by year.

open Zest.Markup

// sitePages() returns every content page with its front matter already parsed.
// The feed templates use the same call; dates are always "yyyy-MM-dd" strings.
let posts =
    sitePages ()
    |> Array.filter (fun p -> p.date <> "" && p.url <> "/archive/")
    |> Array.sortByDescending (fun p -> p.date)

let body =
    posts
    |> Array.groupBy (fun p -> p.date.Substring(0, 4))
    |> Array.sortByDescending fst
    |> Array.map (fun (year, items) ->
        let list =
            items
            |> Array.map (fun p ->
                Dsl.elem "li" [ Dsl.attr "class" "post-list__item" ] [
                    Dsl.elem "h3" [ Dsl.attr "class" "post-list__title" ] [
                        Dsl.elem "a" [ Dsl.attr "href" p.url ] [ Dsl.text p.title ]
                    ]
                    Dsl.elem "p" [ Dsl.attr "class" "post-list__meta" ] [ Dsl.text p.date ]
                ])
            |> String.concat "\n"
        Dsl.elem "section" [ Dsl.attr "class" "archive__year" ] [
            Dsl.elem "h2" [] [ Dsl.text year ]
            Dsl.elem "ol" [ Dsl.attr "class" "post-list" ] [ list ]
        ])
    |> String.concat "\n"

printfn "%s" body
