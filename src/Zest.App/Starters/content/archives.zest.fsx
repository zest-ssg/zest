// @title Archives
// @layout default
// @description A yearly archive of every post on the blog.
//
// Groups the post collection by year with DslCollections.page helpers and a
// plain F# Array.groupBy — no hand-maintained lists, just page data.

open Zest.Dsl

let posts =
    site_pages ()
    |> Array.filter (fun p -> p.url.StartsWith("/posts/") && p.url <> "/posts/")
    |> Array.sortByDescending (fun p -> p.date)

let years =
    posts
    |> Array.groupBy (fun p -> try p.date.[..3] with _ -> "unknown")
    |> Array.sortByDescending fst
    |> Array.map (fun (year, yearPosts) ->
        year, yearPosts |> Array.sortByDescending (fun p -> p.date) |> Array.toList)
    |> Array.toList

let archiveHtml =
    if years.IsEmpty then
        p [ text (t "meta.no_posts") ]
    else
        divC "archives" [
            for (year, yearPosts) in years ->
                sectionC "archives__year" [
                    h2C "archives__year-title" [
                        text year
                        spanC "archives__count" [ text (sprintf " (%d)" yearPosts.Length) ]
                    ]
                    ulC "archives__list" [
                        for p in yearPosts ->
                            liC "archives__item" [
                                timeC "archives__date" p.date [ text p.date ]
                                aHref p.url p.title
                            ]
                    ]
                ]
        ]

render [
    h1 [ text (t "nav.archives") ]
    pC "archives__lead" [ text (t "archives.lead") ]
    archiveHtml
]
