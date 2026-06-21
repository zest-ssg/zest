// @title 首页
// @permalink /
// @layout default

let posts = recent_pages 6

render [
    sectionC "hero" [
        divC "container" [
            h1 [text (site_data "site.title")]
            pC "hero-tagline" [text (site_data "site.description")]
        ]
    ]
    divC "container" [
        divC "section-header" [
            h2 [text "最新文章"]
            aC "see-all" "/posts/" [text "查看全部 →"]
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
            pC "empty-state" [text "暂无文章，请添加内容到 content/posts/ 目录。"]
        else ""
    ]
]
