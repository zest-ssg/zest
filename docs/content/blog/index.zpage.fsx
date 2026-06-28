// @title 博客
// @permalink /blog/
// @layout default
// @description Zest SSG 博客 — 技术文章、教程、最佳实践

let posts = recent_pages 6

render [
    sectionC "hero" [
        divC "hero-inner" [
            h1 [text "博客"]
            pC "hero-tagline" [text "Zest SSG 相关技术文章、教程与最佳实践"]
        ]
    ]

    divC "section-title" [
        h2 [text "最新文章"]
    ]

    divC "container" [
        divC "post-grid" [
            for r in posts ->
                articleC "post-card" [
                    pC "post-date" [text (r.date)]
                    h2 [a r.url [text r.title]]
                    p  [text r.description]
                    divC "tags" [
                        for tag in r.tags ->
                            aC "tag" ("/tags/" + tag + "/") [text tag]
                    ]
                ]
        ]
        if posts.Length = 0 then
            pC "empty-state" [text "暂无文章"]
    ]
]
