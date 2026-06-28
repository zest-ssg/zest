---
title: "Zest 模板系统全面指南"
layout: default
permalink: /blog/template-guide/
tags: ["模板", "fsharp", "指南"]
date: 2026-06-05
description: "详解 Zest 模板系统的四种文件格式、Collections API 和 ZestNjk 过滤器。"
---

# Zest 模板系统全面指南

Zest 支持四种互补的模板方式，覆盖从简单到复杂的全部场景。

## 四种模板方式

### 1. `.zpage.fsx` — F# 脚本模板

类型安全的 HTML DSL + Markdown，适合需要动态计算的页面：

```fsharp
// @title 文章列表
// @layout default

let posts = recent_pages 5

render [
    h1 [ text "最新文章" ]
    divC "post-grid" [
        for r in posts ->
            articleC "post-card" [
                h2 [ a r.url [ text r.title ] ]
                p  [ text r.description ]
            ]
    ]
]
```

### 2. `.znjk` — ZestNjk 模板

Nunjucks 兼容语法，适合需要模板继承的布局场景：

```html
{% extends "base.znjk" %}

{% block content %}
  <h1>{{ page.title }}</h1>
  <div class="content">{{ content }}</div>

  <h3>相关文章</h3>
  {% for p in pages | pages_by_tag("zest") | recent(3) %}
    <a href="{{ p.url }}">{{ p.title }}</a>
  {% endfor %}
{% endblock %}
```

### 3. `.zhtml` — 纯 HTML

构建速度最快，适合着陆页等静态场景：

```html
<!-- @title 关于我们 -->
<!-- @layout default -->
<h1>关于我们</h1>
<p>纯 HTML 页面，不经 FSI 编译。</p>
```

### 4. `.md` — Markdown

标准 Markdown 格式，兼容任意编辑器：

```markdown
---
title: Hello World
layout: default
---

# Hello World

This is a **Markdown** page.
```

## Collections API

脚本中可直接调用以下 API：

```fsharp
let all     = site_pages ()           // 全部页面
let recent  = recent_pages 5          // 最新 5 篇
let tagged  = pages_by_tag "fsharp"   // 按标签过滤
let dir     = pages_by_dir "blog"     // 按目录过滤
let col     = pages_by_collection "blog"  // 按集合过滤
let tags    = all_tags ()             // 所有标签
let results = search_pages "query"    // 按标题搜索
let data    = site_data "key"         // 读取全局数据
let nav     = include_partial "nav.html"  // 引入局部模板
let byYear  = group_pages_by_year ()  // 按年分组
let sorted  = sort_pages_by "title" "asc"  // 排序
```

## ZestNjk 过滤器

在 `.znjk` 模板中可使用的 Zest API 过滤器：

| 过滤器 | 用法 | 说明 |
|--------|------|------|
| `pages_by_tag` | `pages \| pages_by_tag("zest")` | 按标签筛选 |
| `recent` | `pages \| recent(5)` | 最近 N 篇 |
| `by_collection` | `pages \| by_collection("blog")` | 按集合筛选 |
| `search` | `pages \| search("Zest")` | 全文搜索 |
| `where` | `pages \| where("draft", "false")` | 属性筛选 |

了解更多：
- [Showcase: F# DSL](/showcase/fsharp-dsl/)
- [Showcase: ZestNjk 演示](/showcase/zestnjk-demo/)
- [快速指南](/guide/)
