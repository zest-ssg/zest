---
title: "Zest SSG 快速入门"
layout: default
permalink: /blog/getting-started/
tags: ["zest", "教程"]
date: 2026-06-15
description: "5 分钟内搭建你的第一个 Zest 静态站点，了解核心概念与工作流。"
---

# Zest SSG 快速入门

Zest 是一个基于 F# 的现代化静态站点生成器，支持 `.zpage.fsx` 模板脚本和 ZCSS 样式表。

## 核心概念

Zest 围绕四种文件类型构建：

| 文件 | 作用 |
|------|------|
| `.zpage.fsx` | F# 脚本模板 — 类型安全 + HTML DSL + Markdown |
| `.zhtml` | 纯 HTML 页面（可选 ZestNjk 模板语法） |
| `.zcss` | CSS 超集样式表（编译为 CSS） |
| `.znjk` | ZestNjk 模板（Nunjucks 兼容 + Zest API） |
| `.toml` | 站点配置与全局数据 |

## 安装

```bash
git clone https://github.com/example/zest
cd zest
dotnet build
```

## 创建项目

```bash
zest init my-site
cd my-site
```

## 目录结构

```
my-site/
  _config.toml            # 站点配置
  _init.zest.fsx           # 初始化脚本（可选，构建前执行）
  _data/                  # 全局数据（.toml）
  _layouts/               # HTML 布局模板（.znjk 或原生 HTML）
  _includes/              # 可复用 HTML 片段
  content/                # 内容文件
    index.zpage.fsx       # 首页
    blog/                 # 博客文章
  assets/
    css/style.zcss        # ZCSS 样式
  _site/                  # 构建产物（自动生成）
```

## 创建第一篇文章

在 `content/blog/` 下创建 `hello.zpage.fsx`：

```fsharp
// @title 你好，Zest
// @layout default
// @date 2026-06-20
// @tags hello

# 你好，Zest

这是我的第一篇文章。
```

## 启动开发服务器

```bash
zest serve
```

访问 `http://localhost:8080` 预览站点，修改文件后浏览器自动刷新。

## 构建生产版本

```bash
zest build
```

输出到 `_site/` 目录，可直接部署。
