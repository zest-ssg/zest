// @title  模板语言参考
// @layout default
// @permalink /templates/
// @tags   模板, fsharp, 参考
// @description .zest.fsx 模板语言完整参考 — 元数据、HTML DSL、短代码、Collections API

# .zest.fsx 模板语言

`.zest.fsx` 是 Zest 的原生模板格式——**F# 脚本 + Markdown + HTML DSL** 三合一。

## 元数据（Frontmatter）

### 注释风格（推荐）

```fsharp
// @title       关于我们
// @layout      default
// @permalink   /about/
// @description 了解 Zest 项目
// @tags        关于, 元数据
// @date        2026-01-15
```

### YAML 风格

```markdown
---
title: 关于我们
layout: default
---
```

## 内容编写

内容部分使用**标准 Markdown**：

```markdown
# 一级标题

**粗体** *斜体* ~~删除线~~ `行内代码`

- 无序列表
- 另一项

1. 有序列表
2. 第二项

[链接文字](/url/)
![图片说明](/image.png)

> 引用文本

| 表头 | 表头 |
|------|------|
| 单元格 | 单元格 |
```

## HTML DSL

高级页面使用 F# 计算表达式：

```fsharp
page {
  title "关于 Zest"
  layout "default"
  tags ["fsharp"; "dsl"]
  content [
    h1 [ text "关于 Zest" ]
    p  [ text "Zest 是静态站点生成器。" ]
    ul [
      li [ aHref "/guide/" "指南" ]
      li [ aHref "/zss/"   "ZSS" ]
    ]
  ]
}
```

### 可用 HTML 元素

| 函数 | 标签 | 函数 | 标签 |
|------|------|------|------|
| `div[...]` | `<div>` | `p[...]` | `<p>` |
| `h1`..`h6` | 标题 | `span[...]` | `<span>` |
| `ul/ol[...]` | 列表 | `li[...]` | `<li>` |
| `a href ch` | 链接 | `aHref url text` | 链接快捷 |
| `img src alt` | 图片 | `codeBlock lang code` | 代码块 |
| `section[...]` | `<section>` | `article[...]` | `<article>` |
| `nav[...]` | `<nav>` | `header/footer` | 页眉/页脚 |
| `table/tr/td` | 表格 | `form[...]` | `<form>` |

### CSS 类快捷构造

```fsharp
divC    "card"  [...]     // <div class="card">
spanC   "badge" [...]     // <span class="badge">
pC      "lead"  [...]     // <p class="lead">
sectionC "hero" [...]     // <section class="hero">
articleC "post" [...]     // <article class="post">
aC "tag" "/url/" [...]    // <a class="tag" href="/url/">
```

### 元素修饰器

```fsharp
p [text "你好"] |> withClass "greeting"
div [] |> withId "main" |> withStyle "color:red"
```

### 条件与列表

```fsharp
showIf (user.IsLoggedIn) (p [text "欢迎！"])
hideIf (page.Tags.IsEmpty) (div [text "无标签"])
each items (fun item -> li [text item])
```

## 11ty 风格短代码

```fsharp
// 内置短代码，模板中直接使用：
// {{ md "**Markdown**" }}  → 渲染行内 Markdown
// {{ year }}               → 当前年份
// {{ date }}               → 当前日期
// {{ data key }}           → 全局数据值
```

## Collections API

脚本中可直接调用（无需额外 `open`）：

```fsharp
let all    = site_pages ()          // 全部页面
let posts  = pages_by_dir "posts"   // 按目录过滤
let tagged = pages_by_tag "fsharp"  // 按标签过滤
let recent = recent_pages 5         // 最新 N 篇（按 date 降序）

// 读取站点数据
let author = site_data "site.author"

// 引入局部模板 (_includes/)
let nav = include_partial "nav.html"
```

### API 一览

| 函数 | 说明 |
|------|------|
| `site_pages ()` | 全部页面 |
| `recent_pages n` | 最新 N 篇 |
| `pages_by_tag tag` | 按标签过滤 |
| `pages_by_dir dir` | 按目录过滤 |
| `pages_by_collection col` | 按集合过滤 |
| `all_tags ()` | 所有唯一标签 |
| `search_pages query` | 按标题搜索 |
| `site_data key` | 读取全局数据 |
| `include_partial name` | 引入局部模板 |

### 列表页示例

```fsharp
render [
    h1 [Text "所有文章"]
    divC "post-grid" [
        for p in pages_by_dir "posts" |> List.sortByDescending (fun p -> p.Date) ->
            divC "card" [
                h2 [a p.Url [Text p.Title]]
                pC "date" [Text (p.Date |> Option.map string |> Option.defaultValue "")]
            ]
    ]
]
```

## TOML 数据

`_data/*.toml` 中的全局数据通过以下方式访问：

```fsharp
site_data "site.author"       // 脚本内直接用
TemplateData.siteSection globalData "site.social"  // 底层 API
```

## 分页

```fsharp
let pages = Pagination.paginate items 10 (fun n -> sprintf "/page/%d/" n)
for p in pages do
  Pagination.renderPagination p
```

## ZSS 集成

模板中直接使用 ZSS：

```fsharp
styleBlock "$primary: #333; body { c: $primary; }"  // 内联 <style>
stylesheet "/assets/css/style.zss"                    // 外部 .zss
```

[← 返回首页](/)
