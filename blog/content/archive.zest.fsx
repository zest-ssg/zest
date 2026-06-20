// @title 归档
// @permalink /archive/
// @layout default

let posts =
    pages_by_dir "posts"
    |> Array.filter (fun r -> r.date <> "")
    |> Array.sortByDescending (fun r -> r.date)

let byYear =
    posts
    |> Array.groupBy (fun r -> r.date.[..3])
    |> Array.sortByDescending fst

render [
    divC "container" [
        h1 [text "文章归档"]
        pC "text-muted" [text (sprintf "共 %d 篇" posts.Length)]
        yield! [
            for year, ys in byYear do
                yield h2 [text year]
                yield elem "ul" [attr "class" "archive-list"] [
                    for r in ys ->
                        li [
                            a r.url [text r.title]
                            elem "time" [] [text (if r.date.Length >= 7 then r.date.[5..] else "")]
                        ]
                ]
        ]
    ]
]
