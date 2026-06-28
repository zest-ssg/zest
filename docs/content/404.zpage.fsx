// @title 404 — 页面未找到
// @layout default
// @permalink /404/

render [
    sectionC "hero" [
        divC "hero-inner" [
            h1 [text "404"]
            pC "hero-desc" [text "你访问的页面不存在。"]
            divC "hero-actions" [
                aC "btn btn-primary" "/" [text "返回首页"]
                aC "btn btn-secondary" "/blog/" [text "浏览博客"]
            ]
        ]
    ]
]
