// @title  使用指南
// @layout default
// @permalink /guide/
// @tags   指南, 入门

# Zest 使用指南

## 安装

```bash
# 从源码构建
git clone https://github.com/example/zest
cd zest
dotnet build
cd docs
zest serve
```

## 项目结构

```
my-site/
├── _config.toml       ← 站点配置（TOML）
├── _data/             ← 全局数据文件（.toml）
│   └── site.toml      ←   例如：作者、社交链接
├── _layouts/          ← HTML 布局模板
│   ├── default.html   ←   默认布局
│   └── post.html      ←   文章布局
├── _includes/         ← HTML 片段
│   └── header.html
├── content/           ← 内容文件（.zest.fsx / .md）
│   ├── index.zest.fsx ← 首页
│   └── posts/         ← 按集合组织
├── assets/            ← 静态资源
│   ├── css/style.zss  ←   ZSS 编译为 CSS
│   └── js/site-init.js
└── _site/             ← 构建产物（自动生成）
```

## 创建内容

### 使用 `.zest.fsx`（推荐）

```fsharp
// @title  我的页面
// @layout default
// @permalink /my-page/
// @tags   教程, fsharp

# 我的页面

这里写 **Markdown** 内容。
```

### 使用 `.md` 文件

```markdown
---
title: 我的页面
layout: default
---

# 我的页面

标准 Markdown 内容。
```

## 布局系统

布局文件中用 `{{ content }}` 作为内容插入点：

```html
<!DOCTYPE html>
<html>
<head>
  <title>{{ page.title }} | {{ site.title }}</title>
</head>
<body>
  <nav><!-- ... --></nav>
  <main>{{ content }}</main>
</body>
</html>
```

### 布局嵌套

Frontmatter 可以指定嵌套布局：

```html
---
layout: "default"
---
<article>{{ content }}</article>
```

## 配置

```toml
# _config.toml
title = "我的 Zest 站点"
description = "用 Zest SSG 构建"
content_dir  = "./content"
output_dir   = "./_site"
layouts_dir  = "./_layouts"
data_dir     = "./_data"
assets_dir   = "./assets"
default_layout   = "default"
permalink_format = "/:slug/"
dev_server_port  = 8080
```

所有字段都有默认值——空 `_config.toml` 也能正常工作！

## 下一步

- [像 11ty.js 一样编程](/programming/) — F# 模版 DSL、短代码、集合、分页
- [ZSS 样式参考](/zss/) — 变量、嵌套、简写、颜色函数
- [模板语言参考](/templates/) — 元数据、HTML DSL、短代码、集合
- [功能演示](/hello-world/) — Markdown 全特性展示
- [ZSS vs CSS 对比](/posts/zss-vs-css/) — 为什么 ZSS 更高效
