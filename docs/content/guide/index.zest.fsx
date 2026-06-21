// @title  使用指南
// @layout default
// @permalink /guide/
// @tags   指南, 入门
// @description Zest SSG 完整使用指南 — 安装、项目结构、内容创建、布局系统、配置详解

# Zest 使用指南

## 安装

### 从源码构建

```bash
git clone https://github.com/example/zest
cd zest
dotnet build
```

构建完成后，`zest` 命令位于 `src/Zest.App/bin/Release/net10.0/win-x64/publish/` 目录。

### 创建新项目

```bash
zest init my-site    # 创建新项目
cd my-site
zest serve           # 启动开发服务器
```

访问 `http://localhost:8080` 即可预览站点，修改文件后浏览器自动刷新。

## 项目结构

```
my-site/
├── _config.toml       ← 站点配置（TOML）
├── _data/             ← 全局数据文件（.toml）
│   └── site.toml      ←   作者、社交链接等
├── _layouts/          ← HTML 布局模板
│   ├── default.html   ←   默认布局
│   └── post.html      ←   文章布局
├── _includes/         ← HTML 片段（可复用）
│   └── header.html
├── content/           ← 内容文件（.zest.fsx / .md）
│   ├── index.zest.fsx ←   首页
│   └── posts/         ←   按集合组织内容
├── assets/            ← 静态资源
│   ├── css/style.zss ←   ZSS 编译为 CSS
│   └── js/site-init.js
└── _site/             ← 构建产物（自动生成）
```

## 创建内容

### 方式一：`.zest.fsx`（推荐）

F# 脚本模板，支持完整类型安全和 HTML DSL：

```fsharp
// @title  我的页面
// @layout default
// @permalink /my-page/
// @tags   教程, fsharp
// @date   2026-06-20

# 我的页面

这里写 **Markdown** 内容，支持所有标准 Markdown 语法。
```

### 方式二：`.md` 文件

标准 Markdown 文件，YAML frontmatter：

```markdown
---
title: 我的页面
layout: default
permalink: /my-page/
tags: ["教程"]
date: 2026-06-20
---

# 我的页面

标准 Markdown 内容。
```

### 方式三：HTML DSL（高级）

完全用 F# 构建页面，适合动态内容：

```fsharp
// @title 动态页面
// @layout default

let posts = recent_pages 5

render [
    h1 [ text "最新文章" ]
    ul [
        for p in posts ->
            li [ a p.url [ text p.title ] ]
    ]
]
```

## 元数据字段

| 字段 | 说明 | 示例 |
|------|------|------|
| `title` | 页面标题 | `// @title 首页` |
| `layout` | 布局模板 | `// @layout default` |
| `permalink` | 永久链接 | `// @permalink /about/` |
| `description` | 页面描述 | `// @description 关于我们` |
| `tags` | 标签列表 | `// @tags fsharp, 教程` |
| `date` | 发布日期 | `// @date 2026-06-20` |

## 布局系统

布局文件是 HTML 模板，用 `{{ content }}` 作为内容插入点：

```html
<!DOCTYPE html>
<html lang="zh-CN">
<head>
  <title>{{ page.title }} | {{ site.title }}</title>
  <meta name="description" content="{{ page.description }}">
  <link rel="stylesheet" href="/assets/css/style.css">
</head>
<body>
  <nav><!-- 导航 --></nav>
  <main>{{ content }}</main>
  <footer><!-- 页脚 --></footer>
</body>
</html>
```

### 可用模板变量

| 变量 | 说明 |
|------|------|
| `{{ content }}` | 页面内容插入点 |
| `{{ page.title }}` | 页面标题（从 frontmatter） |
| `{{ page.date }}` | 发布日期 |
| `{{ page.permalink }}` | 永久链接 |
| `{{ site.title }}` | 站点标题（从 `_config.toml`） |
| `{{ site.description }}` | 站点描述 |
| `{{ site.author }}` | 作者（从 `_data/site.toml`） |

### 引入局部模板

使用 `{{include filename}}` 引入 `_includes/` 中的片段：

```html
{{include header.html}}
{{include nav.html}}
```

## 配置

### 站点配置 `_config.toml`

```toml
# 基础信息
title        = "我的 Zest 站点"
description  = "用 Zest SSG 构建"
base_url     = "http://localhost:8080"
language     = "zh-CN"

# 目录配置
content_dir  = "./content"
output_dir   = "./_site"
layouts_dir  = "./_layouts"
includes_dir = "./_includes"
data_dir     = "./_data"
assets_dir   = "./assets"

# 构建选项
default_layout          = "default"
permalink_format        = "/:slug/"
dev_server_port         = 8080
live_reload_port        = 35729
enable_parallel_build   = true
enable_incremental_build = true
```

> 所有字段都有默认值——空 `_config.toml` 也能正常工作！

### 全局数据 `_data/site.toml`

```toml
author    = "张三"
copyright = "2026"
language  = "zh-CN"

[social]
github  = "https://github.com/example"
twitter = "https://twitter.com/example"
```

在模板中通过 `{{ data site.author }}` 或脚本中通过 `site_data "site.author"` 访问。

## 命令参考

| 命令 | 说明 |
|------|------|
| `zest init [name]` | 创建新项目 |
| `zest build` | 构建站点到 `_site/` |
| `zest serve` | 启动开发服务器（热重载） |
| `zest serve --verbose` | 显示 FSI 详细输出 |
| `zest clean` | 清理构建产物 |

## 下一步

- [ZSS 2.0 样式指南](/zss/) — 三种语法、变量、混入、颜色函数
- [模板语言参考](/templates/) — HTML DSL、短代码、集合 API
- [F# 脚本编程](/programming/) — 构建时计算、工作原理
- [ZSS vs CSS 对比](/posts/zss-vs-css/) — 代码量对比
