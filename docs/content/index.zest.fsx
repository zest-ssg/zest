// @title  Zest SSG — F# 静态站点生成器
// @layout default
// @permalink /
// @description Zest 是基于 F# 的现代化静态站点生成器，支持 .zest.fsx 模板、ZSS 样式表和 TOML 配置

# Zest SSG

> **Z**enith **E**fficient **S**tatic **T**oolkit — 基于 F# 的现代化静态站点生成器

用 `.zest.fsx`（F# 模板 + Markdown）写内容，`.zss`（CSS 超集）写样式，`.toml` 做配置，`.js` 做客户端增强。

## 核心特性

### 模板引擎

- **`.zest.fsx`** — F# 脚本模板，完整类型安全 + HTML DSL + Markdown 三合一
- **HTML DSL** — `divC "card" [ p [ text "Hello" ] ]` 风格的 F# HTML 构造器
- **11ty 风格短代码** — `{{ md "**粗体**" }}`、`{{ year }}`、`{{ data key }}`
- **集合与分页** — 按标签/目录分组、自动分页导航
- **布局继承** — `{{ content }}` 插入点 + 嵌套布局

### 样式系统

- **`.zss`** — CSS 超集，任何合法 CSS 都是合法 ZSS
- **三种语法** — SCSS 大括号、Python 缩进、F#/C# 等号赋值
- **变量系统** — `let primary = #6c63ff` 或 `$primary: #6c63ff`
- **管道运算符** — `bgc = base |> lighten(10%) |> alpha(0.5)`
- **数学表达式** — `fs = 16px * 1.5` 自动计算
- **60+ 属性简写** — `d: flex`、`bgc: #fff`、`bdr: 0.5r`
- **颜色函数** — `lighten`、`darken`、`alpha`、`mix`、`complement` 等
- **工具箱** — `@use "zest:utilities"` 引入 200+ 工具类
- **自动前缀** — `-webkit-`、`-moz-`、`-ms-` 自动添加

### 开发体验

- **热重载** — `zest serve` 文件修改自动刷新浏览器
- **增量构建** — 只重新构建修改过的页面
- **TOML 配置** — 零配置设计，全类型安全
- **数据级联** — `_data/*.toml` 全局数据自动注入

## 快速开始

```bash
zest init my-site    # 创建新项目
cd my-site
zest serve           # 启动 http://localhost:8080
zest build           # 构建到 _site/
```

## 生态体系

| 文件 | 用途 | 示例 |
|------|------|------|
| `.zest.fsx` | 内容模板（F# + Markdown） | `page { title "首页"; content [...] }` |
| `.zss` | 样式表（CSS 超集） | `d: flex; jc: center; gap: 1r` |
| `.toml` | 配置与全局数据 | `title = "我的站点"` |
| `.md` | 标准 Markdown 内容 | 兼容任意 Markdown 编辑器 |
| `.js` | 客户端脚本 | 原样复制到输出目录 |

## 文档导航

- 📖 [使用指南](/guide/) — 安装、项目结构、布局系统、配置详解
- 🎨 [ZSS 2.0 样式指南](/zss/) — 三种语法、变量、混入、颜色函数、工具箱
- 📝 [模板语言参考](/templates/) — 元数据、HTML DSL、短代码、集合 API
- 🖥️ [F# 脚本编程](/programming/) — 构建时计算、DSL 元素、工作原理
- 📰 [ZSS vs CSS 对比](/posts/zss-vs-css/) — 代码量对比与优势分析
- 🎯 [功能演示](/hello-world/) — Markdown 全特性展示
- 📅 [文章归档](/archive/) — 按时间浏览所有文档
