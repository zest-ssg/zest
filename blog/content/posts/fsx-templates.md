---
title: ".zest.fsx 模板脚本"
date: 2026-06-05
tags: ["fsharp", "模板", "zest"]
description: "使用 F# DSL 编写强类型、可组合的 HTML 模板，并访问 collections API。"
layout: post
---

# .zest.fsx 模板脚本

`.zest.fsx` 文件是 Zest 的核心特性：用 F# 编写模板，获得完整的类型安全和语言能力。

## 基础示例

```fsharp
// @title 我的页面
// @layout default

open Zest.Engine.Html.HtmlDsl

render [
    h1 [Text "Hello, Zest!"]
    p  [Text "这是一个 F# 模板。"]
]
```

## Collections API

```fsharp
// 获取最新 5 篇文章
let posts = recent_pages 5

render [
    for post in posts ->
        divC "post-card" [
            h2 [a post.Url [Text post.Title]]
            pC "date" [Text (post.Date.Value.ToString("yyyy-MM-dd"))]
        ]
]
```

## 按标签过滤

```fsharp
let fsharpPosts = pages_by_tag "fsharp"
```

## 引入局部模板

```fsharp
Raw (include_partial "cta.html")
```

## 读取站点数据

```fsharp
let author = site_data "site.author"
```

## 条件渲染

```fsharp
showIf (posts.Length > 0) (div [...])
hideIf isLoading spinner
each items (fun item -> li [Text item.Name])
```
