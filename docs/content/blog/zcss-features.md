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
  background-color: #fff;
  border-radius: 0.5rem;
  padding: 1.5rem;
}
```

### 2. Python 缩进风格（无大括号）

```zcss
.card
  background-color: #fff
  border-radius: 0.5rem
  padding: 1.5rem
```

### 3. F#/C# 风格（等号赋值 + let 绑定）

```zcss
let primary = #6c63ff
let radius  = 0.5rem

.card
  background-color = primary
  border-radius = radius
  padding   = 1.5rem
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
  background-color = base |> lighten(10%)
  box-shadow = #000 |> mix(base, 25%) |> alpha(0.1)
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
  background-color: #fff
  border-radius: 0.5rem
  padding: $pad
  box-shadow: 0 2px 8px rgba(0,0,0,0.1)

.post-card { @include card() }
.sidebar  { @include card(1rem) }
```

## 响应式断点简写

```zcss
.grid
  display: grid
  grid-template-columns: 1fr

  @md
    grid-template-columns: repeat(2, 1fr)   // min-width: 768px

  @lg
    grid-template-columns: repeat(3, 1fr)   // min-width: 1024px
```

## 完整示例

```zcss
@use "zest:palette"
@use "zest:utilities"

let radius = 0.5rem
let shadow = 0 4px 12px rgba(0,0,0,0.1)

@mixin card
  @apply d-block bg-white p-4
  border-radius = radius
  box-shadow = shadow

.feature-card
  @include card
  transition = all 0.2s

  &:hover
    transform = translateY(-2px)

  h3
    font-size = 1.1rem
    color = $primary |> darken(10%)
```

所有 ZCSS 编译为**标准 CSS**，零运行时开销。
