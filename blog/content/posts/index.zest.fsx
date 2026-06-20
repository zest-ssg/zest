// @title 所有文章
// @permalink /posts/
// @layout default

let posts =
    pages_by_dir "posts"
    |> Array.filter (fun r -> r.date <> "")
    |> Array.sortByDescending (fun r -> r.date)

render [
    divC "container" [
        h1 [text "所有文章"]
        pC "text-muted" [text (sprintf "共 %d 篇" posts.Length)]
        divC "post-grid" [
            for r in posts ->
                divC "post-card" [
                    pC "post-date" [text r.date]
                    h2 [a r.url [text r.title]]
                    p  [text r.description]
                    divC "tags" [
                        for tag in r.tags ->
                            elem "a" [attr "href" ("/tags/" + tag + "/"); attr "class" "tag"] [text tag]
                    ]
                ]
        ]
    ]
]
