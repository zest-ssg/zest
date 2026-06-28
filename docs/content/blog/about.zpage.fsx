// @title 关于本博客
// @permalink /blog/about/
// @layout default
// @description 关于 Zest SSG 示例博客的介绍与技术栈说明

render [
    h1 [text "关于本博客"]
    p  [text (sprintf "作者：%s" (site_data "site.author"))]
    p  [text "这是一个基于 Zest SSG 构建的示例博客，展示了以下核心功能："]

    h2 [text "功能亮点"]
    ul [
        li [text ".zpage.fsx 脚本模板 — F# DSL 生成 HTML，类型安全且可组合"]
        li [text "ZCSS 样式表 — CSS 超集，支持变量、管道运算符、颜色函数"]
        li [text "collections API — site_pages()、recent_pages()、pages_by_tag()"]
        li [text "增量构建缓存 — 只重新构建修改过的页面"]
        li [text "TOML 配置 — 零配置即可启动"]
        li [text "热重载开发服务器 — 文件修改自动刷新浏览器"]
        li [text "Nunjucks 模板引擎 — 布局支持丰富的过滤器与表达式"]
        li [text "_init.fsx — 构建前初始化脚本，可动态注入数据"]
    ]

    h2 [text "文件类型"]
    table [
        thead [tr [th [text "扩展名"]; th [text "用途"]]]
        tbody [
            tr [td [text ".zpage.fsx"]; td [text "F# 脚本模板（HTML DSL + Markdown）"]]
            tr [td [text ".zhtml"];     td [text "纯 HTML 页面（可选 Nunjucks 模板语法）"]]
            tr [td [text ".zcss"];      td [text "ZCSS 样式表（CSS 超集）"]]
            tr [td [text ".md"];        td [text "标准 Markdown 内容"]]
            tr [td [text ".toml"];      td [text "配置与全局数据（无 YAML）"]]
        ]
    ]

    h2 [text "快速命令"]
    codeBlock "bash" "zest init my-site\ncd my-site\nzest serve\nzest build"
]
