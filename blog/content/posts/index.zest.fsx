// @title 所有文章
// @permalink /posts/
// @layout default

let posts =
    pages_by_dir "posts"
    |> Array.filter (fun r -> r.date <> "")
    |> Array.sortByDescending (fun r -> r.date)

render [
    divC "container" [
        divC "section-header" [
            h1 [text "所有文章"]
            pC "text-muted" [text (sprintf "共 %d 篇" posts.Length)]
        ]
        divC "post-grid" [
            for r in posts ->
                articleC "post-card" [
                    pC "post-date" [text r.date]
                    h2 [a r.url [text r.title]]
                    p  [text r.description]
                    divC "tags" [
                        for tag in r.tags ->
                            aC "tag" ("/tags/" + tag + "/") [text tag]
                    ]
                ]
        ]
        if posts.Length = 0 then
            pC "empty-state" [text "还没有文章。在 content/posts/ 目录下创建 .md 或 .zest.fsx 文件即可开始。"]
        else ""
    ]
]
