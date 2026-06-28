// @title 功能演示中心
// @layout default
// @description 全面展示 Zest 文档站点 DSL 特性：集合 API、条件渲染、tag_cloud、page_count 等

let pages = site_pages ()
let total = page_count ()
let allTg = all_tags ()
let cols = all_collections ()
let demoPages = pages_by_collection "demo"
let hasDemos = demoPages.Length > 0

render [
    sectionC "hero" [
        divC "hero-inner" [
            h1 [text "功能演示中心"]
            pC "hero-tagline" [text (sprintf "全站共 %d 个页面 · %d 个标签 · %d 个集合"
                total allTg.Length cols.Length)]
        ]
    ]

    divC "container" [

        // ── 站点数据（来自 settings.zest.fsx / _init.zest.fsx） ──
        h2 [text "站点配置数据"]
        divC "callout callout-info" [
            p [
                text (sprintf "主题色：%s · 强调色：%s · 字体：%s"
                    (site_data "theme.primary")
                    (site_data "theme.accent")
                    (site_data "theme.font_sans"))
            ]
        ]

        // ── 条件渲染 showIf / hideIf ────────────────────────
        h2 [text "条件渲染"]
        yield showIf hasDemos (sprintf "<p>有 <strong>%d</strong> 个演示页面可用。</p>" demoPages.Length)
        yield hideIf (not hasDemos) "<p class=\"text-muted\">暂无演示页面。</p>"

        // ── choose 多分支 ────────────────────────────────────
        h2 [text "choose 分支"]
        yield raw (sprintf "<p>构建环境：<strong>%s</strong></p>"
            (choose (site_data "env" = "production") "生产环境" "开发环境"))

        // ── 标签云 tag_cloud ────────────────────────────────
        h2 [text "标签云"]
        divC "tags-page" [
            divC "tag-cloud" [
                for tag, count in tag_cloud 1 do
                    yield aC "tag-item" (sprintf "/tags/%s/" tag) [
                        text (sprintf "%s (%d)" tag count)
                    ]
            ]
        ]

        // ── 集合列表 all_collections ────────────────────────
        h2 [text "所有集合"]
        ul [
            for col in cols do
                let colPages = pages_by_collection col
                yield li [
                    a (sprintf "/%s/" col) [text col]
                    text (sprintf "（%d 篇）" colPages.Length)
                ]
        ]

        // ── 局部模板 include_partial ────────────────────────
        h2 [text "局部模板（include_partial）"]
        yield raw (include_partial "header.html")

        // ── 统计表格（使用 elem 构造） ──────────────────────
        h2 [text "站点统计"]
        yield raw (sprintf "
<table>
  <thead>
    <tr><th>指标</th><th>数值</th></tr>
  </thead>
  <tbody>
    <tr><td>总页面数</td><td>%d</td></tr>
    <tr><td>总标签数</td><td>%d</td></tr>
    <tr><td>总集合数</td><td>%d</td></tr>
    <tr><td>构建时间</td><td>%s</td></tr>
    <tr><td>Git 提交</td><td>%s</td></tr>
  </tbody>
</table>" total allTg.Length cols.Length (site_data "build_time") (site_data "git_hash"))

        // ── 搜索功能 ────────────────────────────────────────
        h2 [text "页面搜索（search_pages）"]
        let results = search_pages "演示"
        yield raw (sprintf "<p>搜索“演示”找到 <strong>%d</strong> 个结果：</p>" results.Length)
        if results.Length > 0 then
            yield ul [
                for r in results do
                    yield li [a r.url [text r.title]]
            ]

        // ── 排序示例 ────────────────────────────────────────
        h2 [text "排序（sort_pages_by）"]
        let sorted = sort_pages_by "title" "asc"
        yield ol [
            for r in sorted |> Array.truncate 5 do
                yield li [a r.url [text r.title]]
        ]
        yield raw (sprintf "<p class=\"text-muted\">共 %d 个页面，仅显示前 5 个。</p>" sorted.Length)

        // ── 分组 ────────────────────────────────────────────
        h2 [text "按年分组（group_pages_by_year）"]
        let byYear = group_pages_by_year ()
        yield ul [
            for year, yearPages in byYear do
                yield li [
                    strong [text year]
                    text (sprintf " — %d 篇" yearPages.Length)
                ]
        ]
    ]
]
