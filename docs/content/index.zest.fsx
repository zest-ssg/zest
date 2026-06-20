// @title  Zest SSG 文档站
// @layout default
// @permalink /

# Zest SSG

**Zest** 是一个基于 **F#** 的现代化静态站点生成器。

> 递归式缩写：**Z**ealous **E**fficient **S**tatic **T**oolkit

用 `.zest.fsx`（F# 模板 + Markdown）写内容，`.zss`（CSS 超集）写样式，`.toml` 做配置，`.js` 做客户端增强。

## 特性一览

- **`.zest.fsx`** — F# 模板引擎，完整 Markdown + HTML DSL 双模语法
- **`.zss`** — CSS 超集：变量、嵌套、混入、简写属性（`d: flex`、`c: #333`）
- **`.toml`** — 零配置设计，通过 Tomlyn 实现全类型安全
- **F# 计算表达式** — `page { ... }` 类型安全构建页面
- **11ty 风格短代码** — `{{ md "**粗体**" }}`、`{{ year }}`、`{{ data key }}`
- **集合与分页** — 按标签分组、分页导航
- **热重载开发服务器** — `zest serve` 自动刷新浏览器
- **TOML 数据级联** — `_data/*.toml` 全局数据自动注入模板上下文

## 快速开始

```bash
zest init my-site    # 创建新项目
cd my-site
zest serve           # 启动 http://localhost:8080
```

## 生态体系

| 文件 | 用途 | 示例 |
|------|------|------|
| `.zest.fsx` | 内容模板（F# + Markdown） | `page { title "首页"; content [...] }` |
| `.zss` | 样式表（CSS 超集） | `d: flex; jc: center; gap: 1r` |
| `.toml` | 配置与全局数据 | `title = "我的站点"` |
| `.md` | 标准 Markdown 内容 | 兼容任意 Markdown 编辑器 |
| `.js` | 客户端脚本 | 原样复制到输出目录 |

## 更多资源

- 📖 [使用指南](/guide/) — 项目结构、布局系统、配置详解
- 🖥️ [像 11ty.js 一样编程](/programming/) — F# 模版 DSL、短代码、集合、分页
- 🎨 [ZSS 样式参考](/zss/) — 变量、嵌套、简写、颜色函数
- 📝 [模板语言参考](/templates/) — 元数据、HTML DSL、短代码、集合
- 📰 [ZSS vs CSS 对比](/posts/zss-vs-css/) — 为什么 ZSS 更高效
- 🎯 [功能演示](/hello-world/) — Markdown 全特性展示
