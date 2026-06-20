// @title 欢迎来到我的博客
// @permalink /
// @layout default

let posts = recent_pages 6

render [
    div [
        elem "section" [attr "class" "hero"] [
            divC "container" [
                h1 [text (site_data "site.title")]
                p  [text (site_data "site.description")]
            ]
        ]
        divC "container" [
            h2 [text "最新文章"]
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
            if posts.Length = 0 then p [text "暂无文章，请添加内容到 content/posts/ 目录。"] else ""
        ]
    ]
]
