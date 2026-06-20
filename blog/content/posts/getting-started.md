---
title: "Zest SSG 快速入门"
date: 2026-06-15
tags: ["zest", "教程"]
description: "5分钟内搭建你的第一个 Zest 静态站点。"
layout: post
---

# Zest SSG 快速入门

Zest 是一个基于 F# 的静态站点生成器，支持 `.zest.fsx` 模板脚本和 ZSS 样式表。

## 安装

```bash
dotnet tool install -g zest
```

## 创建项目

```bash
mkdir my-blog && cd my-blog
zest init
```

## 目录结构

```
my-blog/
  _config.toml      # 站点配置
  _layouts/         # 布局模板
  _includes/        # 局部模板
  content/          # 内容文件
  assets/           # 静态资源
```

## 启动开发服务器

```bash
zest serve
```

访问 `http://localhost:8080` 即可预览你的站点，支持文件修改自动热重载。
