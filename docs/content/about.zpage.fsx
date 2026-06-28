// @title 关于
// @layout default
// @permalink /about/
// @description 了解 Zest SSG 项目

render [
    divC "page-header" [
        h1 [text "关于 Zest"]
        p [text "Zenith Efficient Static Toolkit"]
    ]

    divC "container" [
        h2 [text "什么是 Zest?"]
        p [text "Zest 是一个基于 F# 和 .NET 的现代化静态站点生成器（SSG）。它融合了 F# 的优雅与静态站点的效率，为 .NET 开发者提供了一个类型安全、功能完整的站点构建方案。"]

        h2 [text "核心理念"]
        ul [
            li [strong [text "F# 原生"]; text " — 使用真实 F# 脚本生成 HTML，类型安全、可计算、可组合"]
            li [strong [text "多模板支持"]; text " — .zpage.fsx（F# DSL）、.znjk（ZestNjk 兼容 Nunjucks）、.zhtml（纯 HTML）、.md（Markdown）"]
            li [strong [text "ZCSS"]; text " — CSS 超集，支持 F# 风格变量、管道运算符、颜色函数、混入"]
            li [strong [text "TOML 配置"]; text " — 零 YAML，_config.toml + _data/*.toml"]
            li [strong [text "Collections API"]; text " — site_pages、pages_by_tag、recent_pages 等内建 API"]
            li [strong [text "高性能"]; text " — 增量构建 + 并行构建 + 开发服务器热重载"]
        ]

        h2 [text "技术栈"]
        yield raw "<table>
  <thead><tr><th>组件</th><th>技术</th></tr></thead>
  <tbody>
    <tr><td>语言</td><td>F# + C#</td></tr>
    <tr><td>运行时</td><td>.NET 10</td></tr>
    <tr><td>模板引擎</td><td>ZestNjk (.znjk) + F# DSL (.zpage.fsx)</td></tr>
    <tr><td>样式</td><td>ZCSS — CSS 超集</td></tr>
    <tr><td>配置</td><td>TOML（Tomlyn）</td></tr>
    <tr><td>许可证</td><td>MIT</td></tr>
  </tbody>
</table>"

        h2 [text "版本"]
        yield raw "<p>当前版本 <strong>2.0.0</strong> — 主要特性：</p>"
        ul [
            li [text "ZestNjk 模板引擎（Nunjucks 兼容 + Zest API）"]
            li [text "ZCSS 样式超集（三种语法风格）"]
            li [text "增量构建 + 并行构建"]
            li [text "Collections API（site_pages, pages_by_tag 等）"]
            li [text "_init.fsx 初始化脚本"]
            li [text "开发服务器实时热重载"]
        ]

        h2 [text "链接"]
        ul [
            li [a "https://github.com/example/zest" [text "GitHub 仓库"]]
            li [a "/blog/" [text "官方博客"]]
            li [a "/showcase/" [text "Showcase"]]
        ]
    ]
]
