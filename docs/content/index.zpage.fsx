// @title  Zest SSG — Zenith Efficient Static Toolkit
// @layout default
// @permalink /
// @description Zest 是基于 F# 的现代化静态站点生成器 — .zpage.fsx 模板 + ZCSS 样式 + TOML 配置

// ── Hero Section ──
render [
    sectionC "hero" [
        divC "hero-inner" [
            spanC "hero-badge" [text "✦ v2.0 — 基于 .NET 10"]
            h1 [raw "用 <span class=\"gradient\">F#</span> 构建静态站点"]
            pC "hero-desc" [text "类型安全的 HTML DSL + ZCSS 样式超集 + TOML 配置 — 为 .NET 开发者打造的 SSG 框架"]
            divC "hero-actions" [
                aC "btn btn-primary" "/guide/" [text "快速开始"]
                aC "btn btn-secondary" "/showcase/" [text "查看 Showcase"]
                aC "btn btn-ghost" "https://github.com/example/zest" [text "GitHub ↗"]
            ]
            divC "hero-stats" [
                divC "stat" [ spanC "stat-num" [text (string (site_pages().Length))]; spanC "stat-label" [text "Pages"] ]
                divC "stat" [ spanC "stat-num" [text (string (all_tags().Length))]; spanC "stat-label" [text "Tags"] ]
                divC "stat" [ spanC "stat-num" [text (string (all_collections().Length))]; spanC "stat-label" [text "Collections"] ]
            ]
        ]
    ]
]

// ── Features Section ──
render [
    divC "section" [
        yield divC "section-title" [
            h2 [text "为什么选择 Zest?"]
            p [text "集模板引擎、样式系统与开发体验于一体的完整 SSG 方案"]
        ]
        yield divC "feature-grid" [
            yield divC "feature-card" [
                divC "feat-icon" [text "◆"]
                h3 [text "F# 即模板"]
                p [text ".zpage.fsx 文件是真实 F# 脚本 — 类型安全、可计算、可组合。不是字符串，是代码。"]
                spanC "feat-tag" [text ".zpage.fsx"]
            ]
            yield divC "feature-card" [
                divC "feat-icon" [text "◆"]
                h3 [text "ZCSS 样式超集"]
                p [text "任何 CSS 都是合法 ZCSS。支持 F# 风格 let 绑定、管道运算符、颜色函数、混入。减少 40% 样板代码。"]
                spanC "feat-tag" [text ".zcss"]
            ]
            yield divC "feature-card" [
                divC "feat-icon" [text "◆"]
                h3 [text "ZestNjk 模板"]
                p [text "Nunjucks 兼容语法 + Zest API 深度集成。支持 extends/block、macros、pages_by_tag 等过滤器。"]
                spanC "feat-tag" [text ".znjk"]
            ]
            yield divC "feature-card" [
                divC "feat-icon" [text "◆"]
                h3 [text "纯 HTML 支持"]
                p [text ".zhtml 页面不经 FSI 编译，构建速度最快。可选 ZestNjk 模板语法渲染。极简场景的首选。"]
                spanC "feat-tag" [text ".zhtml"]
            ]
            yield divC "feature-card" [
                divC "feat-icon" [text "◆"]
                h3 [text "增量 & 并行构建"]
                p [text "文件变更检测跳过未修改页面。并行构建充分利用多核 CPU。开发服务器热重载即时反馈。"]
                spanC "feat-tag" [text "Performance"]
            ]
            yield divC "feature-card" [
                divC "feat-icon" [text "◆"]
                h3 [text "TOML 配置生态"]
                p [text "零 YAML。_config.toml + _data/*.toml + _init.fsx 动态数据注入。优雅而简洁。"]
                spanC "feat-tag" [text ".toml"]
            ]
        ]
    ]
]

// ── File Types Section ──
render [
    divC "section" [
        yield divC "section-title" [
            h2 [text "六种文件格式"]
            p [text "各尽其责，协同构建高效站点"]
        ]
        yield divC "file-types" [
            yield divC "file-type-card" [ spanC "file-ext" [text ".zpage.fsx"]; spanC "file-desc" [text "F# 脚本模板"] ]
            yield divC "file-type-card" [ spanC "file-ext" [text ".znjk"];     spanC "file-desc" [text "ZestNjk 模板"] ]
            yield divC "file-type-card" [ spanC "file-ext" [text ".zhtml"];    spanC "file-desc" [text "纯 HTML 页面"] ]
            yield divC "file-type-card" [ spanC "file-ext" [text ".zcss"];     spanC "file-desc" [text "CSS 超集样式"] ]
            yield divC "file-type-card" [ spanC "file-ext" [text ".md"];       spanC "file-desc" [text "Markdown 内容"] ]
            yield divC "file-type-card" [ spanC "file-ext" [text ".toml"];     spanC "file-desc" [text "配置 & 数据"] ]
        ]
    ]
]

// ── Showcase / Demo Preview ──
render [
    divC "section" [
        yield divC "section-title" [
            h2 [text "Showcase"]
            p [text "体验 Zest 全部特性"]
        ]
        yield divC "showcase-grid" [
            yield divC "showcase-card" [
                spanC "showcase-label" [text "F# DSL"]
                h3 [text "HTML DSL 实战"]
                p [text "用 F# 的 render 函数和 HTML 构造器生成类型安全的页面。divC、h1、p、ul — 全部是代码。"]
                a "/showcase/fsharp-dsl/" [text "查看详情"]
            ]
            yield divC "showcase-card" [
                spanC "showcase-label" [text "ZestNjk"]
                h3 [text "ZestNjk 模板引擎"]
                p [text "extends/block 继承、macros、pages_by_tag 过滤器、搜索 API。Nunjucks 语法 + Zest 超能力。"]
                a "/showcase/zestnjk-demo/" [text "查看详情"]
            ]
            yield divC "showcase-card" [
                spanC "showcase-label" [text "ZCSS"]
                h3 [text "ZCSS 样式系统"]
                p [text "三种语法风格、F# 风格变量、管道运算符、颜色函数、混入。CSS 超集，零运行时。"]
                a "/showcase/zcss-showcase/" [text "查看详情"]
            ]
            yield divC "showcase-card" [
                spanC "showcase-label" [text "Collections API"]
                h3 [text "页面集合 & 过滤器"]
                p [text "site_pages、pages_by_tag、recent_pages、search_pages、group_pages_by_year — 直接在模板中调用。"]
                a "/showcase/" [text "查看详情"]
            ]
        ]
    ]
]

// ── Recent Blog Posts ──
let posts = recent_pages 3

render [
    divC "section" [
        yield divC "section-title" [
            h2 [text "最新博客"]
            p [text "技术文章、教程与最佳实践"]
        ]
        yield divC "blog-grid" [
            for r in posts do
                yield articleC "blog-card" [
                    divC "post-meta" [text r.date]
                    h2 [a r.url [text r.title]]
                    p [text r.description]
                    divC "tags" [
                        for tag in r.tags do
                            yield aC "tag" ("/tags/" + tag + "/") [text tag]
                    ]
                ]
        ]
        if posts.Length = 0 then
            yield pC "empty-state" [text "暂无博客文章"]
        else
            yield pC "section-title" [aC "btn btn-secondary" "/blog/" [text "查看全部文章 →"]]
    ]
]

// ── Tags Cloud ──
let allTg = all_tags ()

render [
    divC "section-sm" [
        yield divC "section-title" [h2 [text "标签云"]]
        if allTg.Length > 0 then
            yield divC "tag-cloud" [
                for tag in allTg do
                    yield aC "tag-item" ("/tags/" + tag + "/") [text tag]
            ]
        else
            yield pC "text-muted" [text "暂无标签"]
    ]
]
