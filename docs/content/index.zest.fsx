// @title  Zest SSG — F# 静态站点生成器
// @layout default
// @permalink /
// @description Zest 是基于 F# 的现代化静态站点生成器

// ── Hero ──
render [
    sectionC "hero" [
        divC "hero-inner" [
            divC "hero-badge" [text "基于 F# 的现代化 SSG 框架"]
            h1 [text "用 F# 构建静态站点"]
            pC "hero-tagline" [text ".zest.fsx 模板 + ZSS 样式超集 + TOML 配置，为开发者打造丝滑体验。"]
            divC "hero-actions" [
                aC "btn-primary" "/guide/" [text "快速开始"]
                aC "btn-secondary" "/zss/" [text "ZSS 样式"]
            ]
        ]
    ]
]

// ── Features ──
render [
    divC "section-title" [
        h2 [text "核心特性"]
        p [text "集模板引擎、样式系统与开发体验于一体"]
    ]

    divC "features" [
        divC "feature-grid" [
            divC "feature-card" [
                spanC "feat-icon" [text "01"]
                h3 [text "F# 模板引擎"]
                p [text "类型安全的 HTML DSL + Markdown 三合一，divC 风格构造器。"]
                a "/templates/" [text "了解模板"]
            ]
            divC "feature-card" [
                spanC "feat-icon" [text "02"]
                h3 [text "ZSS 样式超集"]
                p [text "任何 CSS 即 ZSS。支持 F# 语法、管道运算符、数学表达式。"]
                a "/zss/" [text "探索 ZSS"]
            ]
            divC "feature-card" [
                spanC "feat-icon" [text "03"]
                h3 [text "极致开发体验"]
                p [text "热重载实时预览、增量构建跳过未改动页面。zest serve 一键启动。"]
                a "/guide/" [text "阅读指南"]
            ]
            divC "feature-card" [
                spanC "feat-icon" [text "04"]
                h3 [text "11ty 风格短代码"]
                p [text "Markdown 渲染、模板插入、数据注入，动态转换内容。"]
                a "/templates/" [text "了解更多"]
            ]
            divC "feature-card" [
                spanC "feat-icon" [text "05"]
                h3 [text "集合与分页"]
                p [text "按标签/目录分组，自动分页导航。all_pages 即取即用。"]
                a "/programming/" [text "查看示例"]
            ]
            divC "feature-card" [
                spanC "feat-icon" [text "06"]
                h3 [text "零配置启动"]
                p [text "TOML 配置文件，全类型安全。zest init my-site 一键创建。"]
                a "/guide/" [text "快速开始"]
            ]
        ]
    ]
]

// ── Tech stack table ──
render [
    divC "section-title" [
        h2 [text "技术栈一览"]
        p [text "每个文件格式各尽其责，协作构建高效静态站点"]
    ]

    raw @"<table>
<tr><th>文件</th><th>用途</th><th>示例</th></tr>
<tr><td>.zest.fsx</td><td>内容模板 (F# + Markdown)</td><td><code>page { title Home }</code></td></tr>
<tr><td>.zss</td><td>样式表 (CSS 超集)</td><td><code>body { bgc: #fff }</code></td></tr>
<tr><td>.toml</td><td>配置与全局数据</td><td><code>title = Site</code></td></tr>
<tr><td>.md</td><td>标准 Markdown</td><td>兼容任意编辑器</td></tr>
<tr><td>.js</td><td>客户端脚本</td><td>原样复制到输出目录</td></tr>
</table>"
]
