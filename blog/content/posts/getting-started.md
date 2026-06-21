---
title: "Zest SSG 快速入门"
date: 2026-06-15
tags: ["zest", "教程"]
description: "5 分钟内搭建你的第一个 Zest 静态站点，了解核心概念与工作流。"
layout: post
---

# Zest SSG 快速入门

Zest 是一个基于 F# 的现代化静态站点生成器，支持 `.zest.fsx` 模板脚本和 ZSS 2.0 样式表。

## 核心概念

Zest 围绕四种文件类型构建：

| 文件 | 作用 |
|------|------|
| `.zest.fsx` | F# 脚本模板 — 类型安全 + HTML DSL + Markdown |
| `.md` | 标准 Markdown 内容文件 |
| `.zss` | CSS 超集样式表（编译为 CSS） |
| `.toml` | 站点配置与全局数据 |

## 安装

### 从源码构建

```bash
git clone https://github.com/example/zest
cd zest
dotnet build
```

### 创建项目

```bash
zest init my-blog
cd my-blog
```

## 目录结构

```
my-blog/
  _config.toml      # 站点配置
  _data/            # 全局数据（.toml）
  _layouts/         # HTML 布局模板
  _includes/        # 可复用 HTML 片段
  content/          # 内容文件
    index.zest.fsx  # 首页
    posts/          # 文章集合
  assets/
    css/style.zss   # ZSS 样式
  _site/            # 构建产物（自动生成）
```

## 创建第一篇文章

在 `content/posts/` 下创建 `hello.md`：

```markdown
---
title: "你好，Zest"
date: 2026-06-20
tags: ["hello"]
---

# 你好，Zest

这是我的第一篇文章。
```

或使用 `.zest.fsx` 获得完整 F# 能力：

```fsharp
// @title 你好，Zest
// @layout post
// @date 2026-06-20
// @tags hello

# 你好，Zest

这是用 F# 脚本生成的文章。
```

## 启动开发服务器

```bash
zest serve
```

访问 `http://localhost:8080` 预览站点。修改文件后浏览器自动刷新。

## 构建生产版本

```bash
zest build
```

输出到 `_site/` 目录，可直接部署到 GitHub Pages、Netlify、Vercel 等任意静态托管。

## 下一步

- [ZSS 语法指南](/posts/zss-guide/) — 学习样式表写法
- [.zest.fsx 模板脚本](/posts/fsx-templates/) — 掌握 F# DSL
