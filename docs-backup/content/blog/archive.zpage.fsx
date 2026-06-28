// @title 归档
// @permalink /blog/archive/
// @layout default
// @description 按年份归档的博客文章列表

let posts =
    site_pages ()
    |> Array.filter (fun p -> p.url.StartsWith "/blog/")

let byYear = group_pages_by_year ()
let blogByYear =
    byYear
    |> List.filter (fun (_, pages) -> pages |> List.exists (fun p -> p.url.StartsWith "/blog/"))

render [
    divC "container" [
        h1 [text "文章归档"]
        pC "text-muted" [text (sprintf "共 %d 篇" posts.Length)]
        for year, ys in blogByYear do
            yield h2 [text year]
            yield ulC "archive-list" [
                for r in ys do
                    yield li [
                        a r.url [text r.title]
                        elem "time" [] [text (if r.date.Length >= 10 then r.date.[5..9] else "")]
                    ]
            ]
        if posts.Length = 0 then
            pC "empty-state" [text "还没有文章。"]
    ]
]
