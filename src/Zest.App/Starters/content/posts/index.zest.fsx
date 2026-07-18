// @title Blog
// @layout default
// @description All posts on the Zest Blog.

open Zest.Dsl
open Zest.Dsl.DslCollections

let posts =
    site_pages ()
    |> Array.filter (fun p -> p.url.StartsWith("/posts/") && p.url <> "/posts/")
    |> Array.sortByDescending (fun p -> p.date)

let postList =
    if posts.Length = 0 then
        p [ text "No posts yet — add one in content/posts/." ]
    else
        ulC "post-list" [
            for p in posts ->
                liC "post-list__item" [
                    h2C "post-list__title" [ aHref p.url p.title ]
                    (if p.date <> "" then pC "post-list__meta" [ text p.date ] else "")
                    (if p.description <> "" then pC "post-list__excerpt" [ text p.description ] else "")
                ]
        ]

render [
    sectionC "posts" [
        h1 [ text "Blog" ]
        p [ text "Thoughts on F#, static sites, and building fast." ]
        postList
    ]
]
