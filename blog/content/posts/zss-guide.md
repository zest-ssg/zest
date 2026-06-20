---
title: "ZSS 语法指南"
date: 2026-06-10
tags: ["zss", "css", "教程"]
description: "深入了解 ZSS — Zest 的 CSS 超集语言，支持变量、混入、@each 循环和响应式断点简写。"
layout: post
---

# ZSS 语法指南

ZSS 是 Zest SSG 内置的 CSS 预处理语言，语法比 SCSS 更简洁。

## 变量

```zss
$primary: #3b82f6
$radius:  0.5r   // → 0.5rem

.btn {
  bgc: $primary       // background-color
  bdr: $radius        // border-radius
  px: 1r; py: 0.5r   // padding-inline / padding-block
}
```

## 混入 @mixin / @include

```zss
@mixin card($shadow: none) {
  bgc: #fff
  bdr: 0.5r
  p: 1.5r
  bxsh: $shadow
}

.post-card {
  @include card(0 2px 8px rgba(0,0,0,0.1))
}
```

## @each 循环

```zss
@each $size in (sm, md, lg) {
  .text-#{$size} {
    fs: var(--text-$size)
  }
}
```

## 响应式断点简写

```zss
.grid {
  d: grid
  gtc: 1fr

  @md { gtc: repeat(2, 1fr) }
  @lg { gtc: repeat(3, 1fr) }
}
```

等价于标准 CSS 的 `@media (min-width: 768px)` / `@media (min-width: 1024px)`。

## CSS 变量导出

```zss
$brand: #6366f1
@export $brand   // 生成 :root { --brand: #6366f1; }
```
