---
title: "ZSS 2.0 语法指南"
date: 2026-06-10
tags: ["zss", "css", "教程"]
description: "深入了解 ZSS 2.0 — 三种语法风格、变量、管道运算符、颜色函数、@apply 工具类与响应式断点。"
layout: post
---

# ZSS 2.0 语法指南

ZSS（Zest Style Sheets）2.0 是 **CSS 超集**，编译为标准 CSS。任何合法 CSS 都是合法 ZSS。

## 三种语法风格

### 1. 传统大括号风格（SCSS 风格）

```zss
.card {
  bgc: #fff
  bdr: 0.5r
  p: 1.5r
}
```

### 2. Python 缩进风格（无大括号）

```zss
.card
  bgc: #fff
  bdr: 0.5r
  p: 1.5r
```

### 3. F#/C# 风格（等号赋值 + let 绑定）

```zss
let primary = #6c63ff
let radius  = 0.5r

.card
  bgc = primary
  bdr = radius
  p   = 1.5r
```

> 三种风格可在同一文件中混用，解析器自动检测模式。

## 变量

```zss
// SCSS 风格
$primary: #3b82f6
$radius:  0.5r   // → 0.5rem

// F# 风格（推荐）
let primary = #3b82f6
let radius  = 0.5r

// !default：仅当变量未定义时才赋值
let baseFont = 16px !default
```

引用统一使用 `$name`：

```zss
.btn
  bgc = $primary
  bdr = $radius
```

## 管道运算符

使用 `|>` 链式传递，代码更易读：

```zss
let base = #6c63ff

.btn-primary
  bgc = base |> lighten(10%)
  bdc = base |> darken(20%) |> alpha(0.3)
  bxsh = #000 |> mix(base, 25%) |> alpha(0.1)
```

## 数学表达式

值中直接使用算术运算：

```zss
let base-size = 16px

.title
  fs = $base-size * 1.5        // → 24px
  p  = $base-size / 2 + 4px    // → 12px
  w  = (100% - 40px) / 2       // → calc((100% - 40px) / 2)
```

## 属性简写

| 简写 | 完整属性 | 简写 | 完整属性 |
|------|----------|------|----------|
| `bgc` | background-color | `bdr` | border-radius |
| `d` | display | `pos` | position |
| `c` | color | `fs` | font-size |
| `m`/`p` | margin/padding | `mt`/`pt` | margin-top/padding-top |
| `bxsh` | box-shadow | `tr` | transition |
| `ai`/`jc` | align/justify-items | `gap` | gap |

## 颜色函数

```zss
let brand = #6c63ff

lighten(brand, 20%)       // 变亮
darken(brand, 20%)        // 变暗
alpha(brand, 0.5)          // 透明度
mix(#000, brand, 25%)      // 混合
complement(brand)          // 互补色
grayscale(brand)           // 灰度
```

## 混入 @mixin / @include

```zss
@mixin card($pad: 1.5r)
  bgc: #fff
  bdr: 0.5r
  p: $pad
  bxsh: 0 2px 12px rgba(0,0,0,0.07)

.post-card { @include card() }
.sidebar  { @include card(1r) }
```

## @apply — 工具类应用

```zss
@use "zest:utilities"

.custom-card
  @apply d-block p-4 bg-white rounded-lg shadow-md
  bdr: 0.5r
```

## @each 循环

```zss
@each $size in (sm, md, lg)
  .text-#{$size}
    fs: var(--text-$size)
```

## @for 循环

```zss
@for $i from 1 through 4
  .col-#{$i}
    w: calc($i * 25%)
```

## 响应式断点简写

```zss
.grid
  d: grid
  gtc: 1fr

  @md
    gtc: repeat(2, 1fr)   // min-width: 768px

  @lg
    gtc: repeat(3, 1fr)   // min-width: 1024px
```

| 简写 | 等价 media query |
|------|-----------------|
| `@sm` | `(min-width:640px)` |
| `@md` | `(min-width:768px)` |
| `@lg` | `(min-width:1024px)` |
| `@xl` | `(min-width:1280px)` |

## CSS 变量导出

```zss
let brand = #6366f1
@export $brand   // 生成 :root { --brand: #6366f1; }
```

## 自动前缀

ZSS 自动为需要前缀的属性添加 `-webkit-`、`-moz-`、`-ms-`：

```zss
.modal
  backdrop-filter: blur(10px)
  user-select: none
```

## 下一步

- [ZSS vs CSS 全面对比](/posts/zss-vs-css/) — 代码量对比
- [.zest.fsx 模板脚本](/posts/fsx-templates/) — F# DSL 编程
