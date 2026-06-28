// @title Showcase — Zest 全特性演示中心
// @layout default
// @description 全面展示 Zest 所有核心特性：六种文件格式、Collections API、ZCSS、ZestNjk

let pages = site_pages ()
let total = pages.Length
let allTg = all_tags ()
let cols = all_collections ()

render [
    divC "page-header" [
        h1 [text "Showcase"]
        p [text (sprintf "%d 个页面 · %d 个标签 · %d 个集合" total allTg.Length cols.Length)]
    ]

    divC "container-wide" [

        // ── File Type Demos ──
        h2 [text "文件格式演示"]
        divC "demo-grid" [
            divC "demo-card" [
                spanC "demo-ext" [text ".zpage.fsx"]
                h3 [text "F# HTML DSL"]
                p [text "render 函数 + 类型安全的 HTML 构造器。divC、h1、ul、for 循环、条件判断。"]
                a "/showcase/fsharp-dsl/" [text "查看"]
            ]
            divC "demo-card" [
                spanC "demo-ext" [text ".znjk"]
                h3 [text "ZestNjk 模板"]
                p [text "Nunjucks 兼容语法 + Zest API 过滤器。extends/block/macro/pages_by_tag。"]
                a "/showcase/zestnjk-demo/" [text "查看"]
            ]
            divC "demo-card" [
                spanC "demo-ext" [text ".zhtml"]
                h3 [text "纯 HTML 页面"]
                p [text "不经 FSI 编译，构建速度最快。可选 ZestNjk 语法渲染。"]
                a "/showcase/raw-html/" [text "查看"]
            ]
        ]

        // ── Stats (build-time computation) ──
        h2 [text "构建时统计数据"]
        yield raw (sprintf "
<table>
  <thead><tr><th>统计项</th><th>数值</th></tr></thead>
  <tbody>
    <tr><td>总页面</td><td>%d</td></tr>
    <tr><td>总标签</td><td>%d</td></tr>
    <tr><td>总集合</td><td>%d</td></tr>
    <tr><td>构建时间</td><td>%s</td></tr>
    <tr><td>Git 提交</td><td>%s</td></tr>
    <tr><td>偶数平方和</td><td>%d</td></tr>
  </tbody>
</table>" total allTg.Length cols.Length (site_data "build_time") (site_data "git_hash")
            ([1..10] |> List.filter (fun x -> x % 2 = 0) |> List.map (fun x -> x * x) |> List.sum))

        // ── Collections API ──
        h2 [text "页面集合"]
        h3 [text "按标签分组"]
        let tagCloud = tag_cloud 1
        divC "tag-cloud" [
            for tag, count in tagCloud do
                yield aC "tag-item" ("/tags/" + tag + "/") [text (sprintf "%s (%d)" tag count)]
        ]

        h3 [text "所有集合"]
        ul [
            for col in cols do
                let colPages = pages_by_collection col
                yield li [a (sprintf "/%s/" col) [text col]; text (sprintf "（%d 篇）" colPages.Length)]
        ]

        h3 [text "按年分组"]
        let byYear = group_pages_by_year ()
        ul [
            for year, yearPages in byYear do
                yield li [strong [text year]; text (sprintf " — %d 篇" yearPages.Length)]
        ]

        // ── Search ──
        h2 [text "页面搜索"]
        let results = search_pages "Zest"
        yield raw (sprintf "<p>搜索\"Zest\"找到 <strong>%d</strong> 个结果：</p>" results.Length)
        if results.Length > 0 then
            yield ul [for r in results -> li [a r.url [text r.title]]]

        // ── Sorting ──
        h2 [text "排序示例"]
        let sorted = sort_pages_by "title" "asc"
        yield ol [
            for r in sorted |> Array.truncate 6 do
                yield li [a r.url [text r.title]]
        ]

        // ── Build info ──
        h2 [text "构建信息"]
        yield raw (sprintf "
<ul>
  <li><strong>构建环境：</strong>%s</li>
  <li><strong>构建时间：</strong>%s</li>
  <li><strong>Git 提交：</strong>%s</li>
</ul>
" (choose (site_data "env" = "production") "生产环境" "开发环境")
  (site_data "build_time") (site_data "git_hash"))
    ]
]
