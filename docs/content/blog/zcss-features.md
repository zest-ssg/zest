---
title: "ZCSS 样式系统深度解析"
layout: default
permalink: /blog/zcss-features/
tags: ["zcss", "样式", "教程"]
date: 2026-06-19
description: "深入 ZCSS 的三种语法风格、变量系统、管道运算符、颜色函数与混入机制。"
---

# ZCSS 样式系统深度解析

ZCSS（Zest Style Sheets）是 **CSS 超集** — 任何合法 CSS 都是合法 ZCSS。

## 三种语法风格

### 1. 传统大括号风格（SCSS 风格）

```zcss
.card {
  bgc: #fff;
  bdr: 0.5rem;
  p: 1.5rem;
}
```

### 2. Python 缩进风格（无大括号）

```zcss
.card
  bgc: #fff
  bdr: 0.5rem
  p: 1.5rem
```

### 3. F#/C# 风格（等号赋值 + let 绑定）

```zcss
let primary = #6c63ff
let radius  = 0.5rem

.card
  bgc = primary
  bdr = radius
  p   = 1.5rem
```

三种风格可在同一文件中混用，解析器自动检测模式。

## 变量系统

```zcss
// SCSS 风格
$primary: #3b82f6
$radius:  0.5rem

// F# 风格（推荐）
let primary = #3b82f6
let radius  = 0.5rem
```

引用统一使用 `$name`：

```zcss
.btn {
  color = $primary;
  border-radius = $radius;
}
```

## 管道运算符

使用 `|>` 将值传递给函数，让代码更易读：

```zcss
let base = #6c63ff

.btn-primary
  bgc = base |> lighten(10%)
  bxsh = #000 |> mix(base, 25%) |> alpha(0.1)
```

## 颜色函数

```zcss
let brand = #6c63ff

lighten(brand, 20%)       // 变亮
darken(brand, 20%)        // 变暗
alpha(brand, 0.5)         // 透明度
mix(#000, brand, 25%)     // 混合
complement(brand)         // 互补色
grayscale(brand)          // 灰度
adjust-hue(brand, 30deg)  // 色相旋转
```

## 混入 @mixin / @include

```zcss
@mixin card($pad: 1.5rem)
  bgc: #fff
  bdr: 0.5rem
  p: $pad
  bxsh: 0 2px 8px rgba(0,0,0,0.1)

.post-card { @include card() }
.sidebar  { @include card(1rem) }
```

## 响应式断点简写

```zcss
.grid
  d: grid
  gtc: 1fr

  @md
    gtc: repeat(2, 1fr)   // min-width: 768px

  @lg
    gtc: repeat(3, 1fr)   // min-width: 1024px
```

## 属性简写

ZCSS 提供 60+ 个属性简写，大幅减少样板代码：

| 简写 | 完整属性 | 简写 | 完整属性 |
|------|----------|------|----------|
| `bgc` | background-color | `bdr` | border-radius |
| `c` | color | `fs` | font-size |
| `fw` | font-weight | `d` | display |
| `p` | padding | `m` | margin |
| `bxsh` | box-shadow | `bdc` | border-color |

## 完整示例

```zcss
@use "zest:palette"
@use "zest:utilities"

let radius = 0.5rem
let shadow = 0 4px 12px rgba(0,0,0,0.1)

@mixin card
  @apply d-block bg-white p-4
  bdr = radius
  bxsh = shadow

.feature-card
  @include card
  tr = all 0.2s

  &:hover
    trf = translateY(-2px)

  h3
    fs = 1.1rem
    c = $primary |> darken(10%)
```

所有 ZCSS 编译为**标准 CSS**，零运行时开销。
