// @title ZCSS 语法指南
// @layout default
// @permalink /blog/zcss-guide/
// @tags "zcss", "css", "教程"
// @date 2026-06-10
// @description 深入了解 ZCSS — 三种语法风格、变量、管道运算符、颜色函数与响应式断点。

# ZCSS 语法指南

ZCSS（Zest Style Sheets）是 **CSS 超集**，编译为标准 CSS。任何合法 CSS 都是合法 ZCSS。

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
  padding = 1.5rem
```

> 三种风格可在同一文件中混用，解析器自动检测模式。

## 变量

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
.btn
  color = $primary
  border-radius = $radius
```

## 管道运算符

```zcss
let base = #6c63ff

.btn-primary
  background-color = base |> lighten(10%)
  box-shadow = #000 |> mix(base, 25%) |> alpha(0.1)
```

## 数学表达式

```zcss
let base-size = 16px

.title
  font-size = $base-size * 1.5        // → 24px
  padding = $base-size / 2 + 4px      // → 12px
```

## 颜色函数

```zcss
let brand = #6c63ff

lighten(brand, 20%)       // 变亮
darken(brand, 20%)        // 变暗
alpha(brand, 0.5)         // 透明度
mix(#000, brand, 25%)     // 混合
complement(brand)         // 互补色
```

## 混入 @mixin / @include

```zcss
@mixin card($pad: 1.5rem)
  background-color: #fff
  border-radius: 0.5rem
  padding: $pad

.post-card { @include card() }
.sidebar  { @include card(1rem) }
```

## @each 循环

```zcss
@each $size in (sm, md, lg)
  .text-#{$size} {
    font-size: var(--text-$size)
  }
```

## @for 循环

```zcss
@for $i from 1 through 4
  .col-#{$i} {
    width: calc($i * 25%)
  }
```

## 响应式断点简写

```zcss
.grid
  display: grid
  grid-template-columns: 1fr

  @md
    grid-template-columns: repeat(2, 1fr)

  @lg
    grid-template-columns: repeat(3, 1fr)
```

| 简写 | 等价 media query |
|------|-----------------|
| `@sm` | `(min-width:640px)` |
| `@md` | `(min-width:768px)` |
| `@lg` | `(min-width:1024px)` |
| `@xl` | `(min-width:1280px)` |

## CSS 变量导出

```zcss
let brand = #6366f1
@export $brand   // 生成 :root { --brand: #6366f1; }
```

## 自动前缀

ZCSS 自动为需要前缀的属性添加 `-webkit-`、`-moz-`、`-ms-`：

```zcss
.modal
  backdrop-filter: blur(10px)
  user-select: none
```
