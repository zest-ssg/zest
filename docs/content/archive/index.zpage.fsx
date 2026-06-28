// @title 归档
// @layout default
// @description 站点全部页面归档 — 按年份排列

let allPages = site_pages ()
let byYear = group_pages_by_year ()

render [
    divC "page-header" [
        h1 [text "归档"]
        p [text (sprintf "共 %d 个页面" allPages.Length)]
    ]

    divC "container-wide" [
        for year, yearPages in byYear do
            yield h2 [text year]
            yield ulC "archive-list" [
                for r in yearPages do
                    yield li [
                        yield spanC "text-muted" [text (if r.date.Length >= 10 then r.date else "")]
                        yield a r.url [text r.title]
                    ]
            ]
    ]
    if allPages.Length = 0 then
        yield pC "empty-state" [text "暂无页面"]
]
