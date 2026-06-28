// @title 博客
// @layout default
// @permalink /blog/
// @description Zest SSG 官方博客 — 技术文章、教程与最佳实践

let totalPosts = site_pages () |> Array.filter (fun p -> p.url.StartsWith "/blog/")
let byYear = group_pages_by_year ()
let blogByYear = byYear |> List.filter (fun (_, pages) -> pages |> List.exists (fun p -> p.url.StartsWith "/blog/"))

render [
    divC "page-header" [
        h1 [text "博客"]
        p [text (sprintf "共 %d 篇文章" totalPosts.Length)]
    ]
    divC "container-wide" [
        for year, ys in blogByYear do
            yield h2 [text year]
            yield divC "blog-grid" [
                for r in ys do
                    yield articleC "blog-card" [
                        divC "post-meta" [text (if r.date.Length >= 10 then r.date else "")]
                        h2 [a r.url [text r.title]]
                        p [text r.description]
                        divC "tags" [
                            for tag in r.tags ->
                                aC "tag" ("/tags/" + tag + "/") [text tag]
                        ]
                    ]
            ]
        if totalPosts.Length = 0 then
            yield pC "empty-state" [text "还没有文章。敬请期待！"]
    ]
]
