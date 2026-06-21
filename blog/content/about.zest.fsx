// @title 关于
// @permalink /about/
// @layout default

render [
    divC "post-layout" [
        articleC "post-content" [
            h1 [text "关于本博客"]
            p  [text (sprintf "作者：%s" (site_data "site.author"))]
            p  [text "这是一个使用 Zest SSG 构建的示例博客，展示了以下功能："]
            ul [
                li [text ".zest.fsx 脚本模板 — F# DSL 生成 HTML，类型安全且可组合"]
                li [text "ZSS 2.0 样式表 — CSS 超集，支持变量、管道运算符、颜色函数、@apply 工具类"]
                li [text "collections API — site_pages()、recent_pages()、pages_by_tag()、pages_by_dir()"]
                li [text "增量构建缓存 — 只重新构建修改过的页面，提速开发"]
                li [text "TOML 配置 — taxonomies、menus、build 开关，零配置即可启动"]
                li [text "热重载开发服务器 — 文件修改自动刷新浏览器"]
            ]
            h2 [text "技术栈"]
            p  [raw """<a href="https://fsharp.org">F# / .NET 10</a> + Zest SSG"""]
            h2 [text "文件类型"]
            table [
                thead [
                    tr [
                        th [text "扩展名"]
                        th [text "用途"]
                    ]
                ]
                tbody [
                    tr [ td [text ".zest.fsx"]; td [text "F# 脚本模板（HTML DSL + Markdown）"] ]
                    tr [ td [text ".md"];         td [text "标准 Markdown 内容"] ]
                    tr [ td [text ".zss"];        td [text "ZSS 样式表（CSS 超集）"] ]
                    tr [ td [text ".toml"];       td [text "配置与全局数据"] ]
                    tr [ td [text ".html"];       td [text "布局与局部模板"] ]
                ]
            ]
            h2 [text "快速命令"]
            codeBlock "bash" "zest init my-blog\ncd my-blog\nzest serve\nzest build"
        ]
    ]
]
