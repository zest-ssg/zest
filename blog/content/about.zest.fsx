// @title 关于
// @permalink /about/
// @layout default

render [
    divC "post-layout" [
        elem "article" [attr "class" "post-content"] [
            h1 [text "关于本博客"]
            p  [text (sprintf "作者：%s" (site_data "site.author"))]
            p  [text "这是一个使用 Zest SSG 构建的示例博客，展示了以下功能："]
            ul [
                li [text ".zest.fsx 脚本模板 — F# DSL 生成 HTML"]
                li [text "ZSS 样式表 — CSS 超集，支持变量、混入、@each、响应式断点简写"]
                li [text "collections API — site_pages()、recent_pages()、pages_by_tag()"]
                li [text "增量构建缓存 — 只重新构建修改过的页面"]
                li [text "TOML 配置 — taxonomies、menus、build 开关"]
            ]
            h2 [text "技术栈"]
            p  [raw """<a href="https://fsharp.org">F# / .NET 10</a> + Zest SSG"""]
        ]
    ]
]
