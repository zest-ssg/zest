---
title: ".zest.fsx 模板脚本"
date: 2026-06-05
tags: ["fsharp", "模板", "zest"]
description: "使用 F# DSL 编写强类型、可组合的 HTML 模板，并访问 collections API 进行动态内容生成。"
layout: post
---

# .zest.fsx 模板脚本

`.zest.fsx` 文件是 Zest 的核心特性：用 F# 编写模板，获得完整的类型安全和语言能力。

## 文件结构

每个 `.zest.fsx` 文件由两部分组成：

```fsharp
// 1. 元数据注释（// @ 前缀）
// @title 我的页面
// @layout default
// @permalink /my-page/
// @tags fsharp, demo
// @date 2026-06-20

// 2. 内容（Markdown 或 F# DSL）
# 我的页面

这里写 **Markdown** 内容。
```

## HTML DSL

使用 `render` 函数将 F# 构建的 HTML 节点列表渲染为页面：

```fsharp
// @title 动态页面
// @layout default

render [
    h1 [ text "Hello, Zest!" ]
    p  [ text "这是一个 F# 模板。" ]
    ul [
        li [ text "类型安全" ]
        li [ text "可组合" ]
        li [ text "可编程" ]
    ]
]
```

### 带类名的快捷构造

```fsharp
divC    "card"  [ p [ text "内容" ] ]    // <div class="card">
spanC   "badge" [ text "New" ]           // <span class="badge">
sectionC "hero" [ h1 [ text "标题" ] ]   // <section class="hero">
articleC "post" [ p [ text "正文" ] ]    // <article class="post">
```

### 条件与列表

```fsharp
showIf (posts.Length > 0) (div [ text "有文章" ])
hideIf isLoading (div [ text "加载完成" ])
each items (fun item -> li [ text item ])
```

## Collections API

脚本中可直接调用（无需 `open`）：

```fsharp
// 获取最新 5 篇文章
let posts = recent_pages 5

render [
    for post in posts ->
        divC "post-card" [
            h2 [a post.url [text post.title]]
            pC "date" [text post.date]
        ]
]
```

### 全部 API

| 函数 | 说明 |
|------|------|
| `site_pages ()` | 全部页面 |
| `recent_pages n` | 最新 N 篇（按日期降序） |
| `pages_by_tag tag` | 按标签过滤 |
| `pages_by_dir dir` | 按目录过滤 |
| `pages_by_collection col` | 按集合（首段 URL）过滤 |
| `all_tags ()` | 所有唯一标签 |
| `search_pages query` | 按标题搜索 |
| `page_count ()` | 页面总数 |
| `site_data key` | 读取 `_data/*.toml` 数据 |
| `include_partial name` | 引入 `_includes/` 片段 |

## 读取站点数据

`_data/site.toml` 中的值通过 `site_data` 访问：

```fsharp
let author = site_data "site.author"
let github = site_data "site.social.github"
```

## 引入局部模板

```fsharp
let nav = include_partial "nav.html"
```

## 构建时计算

F# 脚本在构建时真实执行，可进行任意计算：

```fsharp
let evenSquares = [1..10] |> List.filter (fun x -> x % 2 = 0) |> List.map (fun x -> x * x)

render [
    h1 [ text "构建时计算" ]
    p  [ text (sprintf "偶数平方和 = %d" (List.sum evenSquares)) ]
    p  [ text (sprintf "当前时间：%s" (DateTime.Now.ToString("yyyy-MM-dd"))) ]
]
```

## 完整示例

```fsharp
// @title 文章列表
// @layout default
// @permalink /articles/

let posts = pages_by_dir "posts" |> Array.sortByDescending (fun r -> r.date)

render [
    h1 [ text "所有文章" ]
    pC "count" [ text (sprintf "共 %d 篇" posts.Length) ]
    divC "post-grid" [
        for r in posts ->
            articleC "post-card" [
                pC "date" [ text r.date ]
                h2 [ a r.url [ text r.title ] ]
                p  [ text r.description ]
                divC "tags" [
                    for tag in r.tags ->
                        aC "tag" ("/tags/" + tag + "/") [ text tag ]
                ]
            ]
    ]
]
```

## 下一步

- [ZSS 语法指南](/posts/zss-guide/) — 样式表写法
- [快速入门](/posts/getting-started/) — 项目搭建
