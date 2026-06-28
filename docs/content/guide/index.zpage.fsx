// @title 快速指南
// @layout default
// @description 完整了解 Zest SSG 的安装、使用和所有核心功能

render [
    divC "page-header" [
        h1 [text "快速指南"]
        p [text "从零开始使用 Zest SSG 构建静态站点"]
    ]

    divC "container" [
        h2 [text "安装"]
        codeBlock "bash" "git clone https://github.com/example/zest\ncd zest\ndotnet build"

        h2 [text "初始化项目"]
        codeBlock "bash" "zest init my-site\ncd my-site"

        h2 [text "目录结构"]
        yield raw "<pre><code>my-site/\n  _config.toml         # 站点配置\n  _init.zest.fsx        # 初始化脚本（可选）\n  _data/                # 全局数据\n  _layouts/             # HTML 布局\n  _includes/            # 可复用片段\n  content/              # 页面内容\n  assets/               # 资源文件\n  _site/                # 构建输出（自动）</code></pre>"

        h2 [text "创建第一篇文章"]
        p [text "在 content/blog/ 下创建 hello.zpage.fsx："]
        codeBlock "fsharp" "// @title 你好，Zest\n// @layout default\n// @date 2026-06-20\n// @tags hello\n\nrender [\n    h1 [text \"你好，Zest\"]\n    p [text \"这是我的第一篇文章。\"]\n]"

        h2 [text "启动开发服务器"]
        p [text "运行以下命令，Zest 会在 localhost:8080 启动开发服务器，支持热重载："]
        codeBlock "bash" "zest serve"

        h2 [text "构建生产版本"]
        p [text "输出到 _site/ 目录，可直接部署到任意静态托管服务："]
        codeBlock "bash" "zest build"

        h2 [text "预定义宏"]
        p [text "在 F# 脚本中可用的 HTML 辅助函数："]
        yield raw "<table>
  <thead><tr><th>函数</th><th>说明</th></tr></thead>
  <tbody>
    <tr><td><code>h1, h2, h3, h4, h5, h6</code></td><td>标题元素</td></tr>
    <tr><td><code>p, a, span, div</code></td><td>文本元素</td></tr>
    <tr><td><code>divC, pC, spanC, aC, sectionC</code></td><td>带类名快捷构造</td></tr>
    <tr><td><code>ul, ol, li</code></td><td>列表</td></tr>
    <tr><td><code>img</code></td><td>图片</td></tr>
    <tr><td><code>table, thead, tbody, tr, th, td</code></td><td>表格</td></tr>
    <tr><td><code>text, html, raw</code></td><td>文本/HTML/原始输出</td></tr>
    <tr><td><code>render</code></td><td>将 HTML 片段列表输出到页面</td></tr>
    <tr><td><code>codeBlock</code></td><td>代码块</td></tr>
    <tr><td><code>include_partial</code></td><td>引入局部模板</td></tr>
  </tbody>
</table>"

        h2 [text "元数据"]
        p [text "在 .zpage.fsx 文件开头使用 F# 注释声明元数据："]
        yield raw "<table>
  <thead><tr><th>元数据</th><th>说明</th></tr></thead>
  <tbody>
    <tr><td><code>@title</code></td><td>页面标题</td></tr>
    <tr><td><code>@layout</code></td><td>使用的布局</td></tr>
    <tr><td><code>@permalink</code></td><td>URL 路径（默认 /:slug/）</td></tr>
    <tr><td><code>@tags</code></td><td>标签</td></tr>
    <tr><td><code>@date</code></td><td>发布日期</td></tr>
    <tr><td><code>@description</code></td><td>描述</td></tr>
  </tbody>
</table>"

        h2 [text "Collections API"]
        p [text "在 F# 脚本中可直接调用的 API："]
        yield raw "<ul>
  <li><code>site_pages()</code> — 所有页面</li>
  <li><code>recent_pages(n)</code> — 最新 N 篇</li>
  <li><code>pages_by_tag(tag)</code> — 按标签过滤</li>
  <li><code>pages_by_dir(dir)</code> — 按目录过滤</li>
  <li><code>pages_by_collection(col)</code> — 按集合过滤</li>
  <li><code>all_tags()</code> — 所有标签</li>
  <li><code>tag_cloud(min)</code> — 标签云</li>
  <li><code>all_collections()</code> — 所有集合</li>
  <li><code>search_pages(q)</code> — 按标题搜索</li>
  <li><code>sort_pages_by(key, order)</code> — 排序</li>
  <li><code>group_pages_by_year()</code> — 按年分组</li>
  <li><code>site_data(key)</code> — 读取全局数据（_data/ 或 _config.toml）</li>
</ul>"

        h2 [text "ZestNjk 过滤器"]
        yield raw "<table>
  <thead><tr><th>过滤器</th><th>用法</th><th>说明</th></tr></thead>
  <tbody>
    <tr><td>pages_by_tag</td><td><code>pages | pages_by_tag(\"tag\")</code></td><td>按标签筛选</td></tr>
    <tr><td>recent</td><td><code>pages | recent(5)</code></td><td>最近 N 篇</td></tr>
    <tr><td>by_collection</td><td><code>pages | by_collection(\"col\")</code></td><td>按集合筛选</td></tr>
    <tr><td>search</td><td><code>pages | search(\"query\")</code></td><td>搜索</td></tr>
    <tr><td>where</td><td><code>pages | where(\"key\", \"val\")</code></td><td>属性筛选</td></tr>
  </tbody>
</table>"

        h2 [text "ZCSS 快速参考"]
        p [text "ZCSS 是 CSS 超集，任何合法 CSS 都是合法 ZCSS。核心特性："]
        yield raw "<ul>
  <li><strong>变量绑定：</strong><code>let primary = #6c63ff</code></li>
  <li><strong>管道运算符：</strong><code>color = base |> lighten(20%)</code></li>
  <li><strong>颜色函数：</strong>lighten、darken、alpha、mix、complement</li>
  <li><strong>混入：</strong><code>@mixin / @include</code></li>
  <li><strong>属性简写：</strong>bgc、bdr、fs、fw、bxsh 等 60+</li>
  <li><strong>响应式简写：</strong>@sm、@md、@lg、@xl</li>
  <li><strong>@apply：</strong>复用工具类</li>
  <li><strong>三种语法：</strong>SCSS 风格 / 缩进风格 / F# 风格 — 可混用</li>
</ul>"
    ]
]
