// @title .zpage.fsx 模板脚本
// @layout default
// @permalink /blog/fsx-templates/
// @tags "fsharp", "模板", "zest"
// @date 2026-06-05
// @description 使用 F# DSL 编写强类型、可组合的 HTML 模板，并访问 collections API 进行动态内容生成。

# .zpage.fsx 模板脚本

`.zpage.fsx` 文件是 Zest 的核心特性：用 F# 编写模板，获得完整的类型安全和语言能力。

## 文件结构

```fsharp
// 1. 元数据注释（// @ 前缀）
// @title 我的页面
// @layout default

// 2. 内容（Markdown 或 F# DSL）
# 我的页面

这里写 **Markdown** 内容。
```

## HTML DSL

```fsharp
// @title 动态页面
// @layout default

render [
    h1 [ text "Hello, Zest!" ]
    p  [ text "这是一个 F# 模板。" ]
    ul [ li [ text "类型安全" ]; li [ text "可组合" ] ]
]
```

### 带类名的快捷构造

```fsharp
divC    "card"  [ p [ text "内容" ] ]    // <div class="card">
spanC   "badge" [ text "New" ]           // <span class="badge">
```

### Collections API

脚本中可直接调用：

```fsharp
let posts = recent_pages 5
let all   = site_pages ()
let tagged = pages_by_tag "fsharp"
let author = site_data "site.author"
let nav = include_partial "nav.html"
```

### 完整 API

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

### 构建时计算

```fsharp
let evenSquares = [1..10] |> List.filter (fun x -> x % 2 = 0) |> List.map (fun x -> x * x)

render [
    h1 [ text "构建时计算" ]
    p  [ text (sprintf "偶数平方和 = %d" (List.sum evenSquares)) ]
    p  [ text (sprintf "当前时间：%s" (DateTime.Now.ToString("yyyy-MM-dd"))) ]
]
```

### 完整示例

```fsharp
// @title 文章列表
// @layout default
// @permalink /blog/articles/

let posts = pages_by_dir "posts" |> List.sortByDescending (fun p -> p.Date)

render [
    h1 [ text "所有文章" ]
    p  [ text (sprintf "共 %d 篇" posts.Length) ]
    divC "post-grid" [
        for r in posts ->
            articleC "post-card" [
                p  [ text r.date ]
                h2 [ a r.url [ text r.title ] ]
                p  [ text r.description ]
            ]
    ]
]
```
