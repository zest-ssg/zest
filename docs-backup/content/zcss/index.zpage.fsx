// @title  ZSS 2.0 样式指南
// @layout default
// @permalink /zcss/
// @tags   zss, 样式, 指南, F#

# ZSS 2.0 样式指南

ZSS（Zest Style Sheets）2.0 是 **CSS 超集**，编译为标准 CSS。任何合法 CSS 文件都是合法 ZSS 文件。

2.0 版本新增：**F#/C# 风格语法**、**Python 缩进风格**、**管道运算符**、**数学表达式**、**@apply 工具类**、**更多颜色函数**、**嵌套属性简写**。

## 三种语法风格

ZSS 2.0 支持三种写法，可按需选择或混用：

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

> 三种风格可自动检测并在同一文件中混用。解析器会根据缩进和大括号自动判断模式。

## 变量

支持两种变量声明方式：

```zss
// SCSS 风格
$primary: #6c63ff
$radius:  0.5r

// F# 风格（推荐）
let primary = #6c63ff
let radius  = 0.5r

// !default 标志：仅当变量未定义时才赋值
$base-font: 16px !default
let baseFont = 16px !default
```

变量引用统一使用 `$name` 语法（无论用哪种方式声明）：

```zss
.button
  bgc = $primary
  bdr = $radius
```

## F# 管道运算符

使用 `|>` 将值传递给函数，让代码更易读：

```zss
let base = #6c63ff

.btn-primary
  bgc = base |> lighten(10%)
  bdc = base |> darken(20%) |> alpha(0.3)
  bxsh = #000 |> mix(base, 25%) |> alpha(0.1)
```

等价于：

```zss
.btn-primary
  bgc = lighten(base, 10%)
  bdc = alpha(darken(base, 20%), 0.3)
```

## 嵌套属性简写

使用点号访问嵌套属性，自动转换为连字符：

```zss
.card
  margin.top    = 10px     // → margin-top: 10px
  margin.bottom = 20px     // → margin-bottom: 20px
  border.radius = 0.5r     // → border-radius: 0.5rem
  font.size     = 1.2r     // → font-size: 1.2rem
  font.weight   = 600       // → font-weight: 600
```

## 数学表达式

在值中直接使用算术运算：

```zss
let base-size = 16px

.title
  fs = $base-size * 1.5        // → 24px
  p  = $base-size / 2 + 4px    // → 12px
  w  = (100% - 40px) / 2       // → calc((100% - 40px) / 2)
  mt = $base-size + 10px       // → 26px
```

支持的运算符：`+` `-` `*` `/` `()`，支持单位自动推导。

## 颜色函数

ZSS 2.0 提供完整的颜色操作工具箱：

```zss
let brand = #6c63ff

// 明度调整
lighten(brand, 20%)       // 变亮 20%
darken(brand, 20%)        // 变暗 20%

// 透明度
alpha(brand, 0.5)          // 50% 透明度
transparentize(brand, 0.3) // 减少 30% 不透明度

// 混合
mix(#000, brand, 25%)      // 25% 黑 + 75% brand
tint(brand, 30%)           // 混入白色
shade(brand, 30%)          // 混入黑色

// 色相调整
adjust-hue(brand, 30deg)   // 色相旋转 30°
complement(brand)          // 互补色
grayscale(brand)           // 灰度
invert(brand)              // 反色

// 饱和度
saturate(brand, 20%)
desaturate(brand, 20%)

// RGB/HSL 构造
rgba(108, 99, 255, 0.8)
rgb(108, 99, 255)
hsl(250, 100%, 60%)
hsla(250, 100%, 60%, 0.5)
```

## 属性简写

| 简写 | 完整属性 | 简写 | 完整属性 |
|------|----------|------|----------|
| `m` | margin | `p` | padding |
| `mt/mr/mb/ml` | margin-* | `pt/pr/pb/pl` | padding-* |
| `mx` | margin-inline | `my` | margin-block |
| `d` | display | `pos` | position |
| `c` | color | `fs` | font-size |
| `fw` | font-weight | `ff` | font-family |
| `bdr` | border-radius | `bxsh` | box-shadow |
| `tr` | transition | `trf` | transform |
| `ai` | align-items | `jc` | justify-content |
| `gtc` | grid-template-columns | `gap` | gap |
| `bgc` | background-color | `bgi` | background-image |
| `bdc` | border-color | `bd` | border |
| `bxz` | box-sizing | `ov` | overflow |
| `td` | text-decoration | `ta` | text-align |
| `lh` | line-height | `ls` | list-style |
| `o` | opacity | `z` | z-index |
| `w` | width | `h` | height |
| `mw` | max-width | `mh` | max-height |
| `anim` | animation | `asp` | aspect-ratio |

### `bdr` 简写的歧义解析

`bdr` 同时可能是 `border` 或 `border-radius`，编译器会**按值内容动态判断**：

- 值**包含** border-style 关键字（`solid` / `dashed` / `dotted` / `double` / `groove` 等）→ 解析为 `border`
- 否则 → 解析为 `border-radius`

```zss
.btn      { bdr: 1px solid #ccc }   // → border:        1px solid #ccc
.card     { bdr: 8px            }    // → border-radius: 8px
```

### 多段属性简写

带连字符的属性名（如 `min-width`）通过**连字符不敏感的最长公共子串匹配**解析。
下列写法全部等价：

```zss
.x { mn-width: 0 }   // → min-width: 0
.x { mnw:      0 }   // → min-width: 0
.x { blc: #f00 }     // → border-left-color: #f00
.x { mnh: 100vh }    // → min-height: 100vh
```

> **避坑提示**：避免把 `let border = #e2e8f0` 之类的变量名与 CSS 关键字重名。
> 编译器会按词边界替换变量，但如果你的变量名恰好是 `border`、`box` 等
> CSS 关键字，utility 中的 `border-box`、`box-shadow` 等值会被误替换。
> 推荐用 `let bdColor = #e2e8f0`、`let shadowColor = #000` 这类带语义前缀的名字。

## 值简写

```
1r   → 1rem       50p  → 50%
100v → 100vh      0.3s → 0.3s
```

## 嵌套

```zss
nav
  d: flex
  gap: 1r

  ul
    ls: none
    d: flex

  a
    td: none
    c: $primary

    &:hover
      td: underline
```

## 混入（Mixin）

```zss
@mixin card($pad: 1.5r)
  bgc: #fff
  bdr: $radius
  p: $pad
  bxsh: 0 2px 12px rgba(0,0,0,0.07)

.post { @include card() }
.sidebar { @include card(1r) }
```

## @apply — 工具类应用

使用 `@apply` 在规则中直接引入预定义的工具类：

```zss
@use "zest:utilities"

.custom-card
  @apply d-block p-4 bg-white rounded-lg shadow-md
  bdr: 0.5r

.icon-button
  @apply d-inline-flex ai-center jc-center
  w: 2.5r
  h: 2.5r
  bdr: 50%
```

## @use — 模块系统

引入内置工具箱：

```zss
@use "zest:utilities"   // 基础工具类（display, spacing, flex 等）
@use "zest:reset"       // CSS Reset
@use "zest:palette"     // 颜色调色板 + 语义色变量
@use "zest:animations"  // 动画关键帧 + 动画类
@use "zest:gradients"   // 渐变背景工具
@use "zest:filters"     // 滤镜 + 变换工具
@use "zest:layout"      // 容器 + 宽高比 + 多列布局
@use "zest:all"         // 全部引入
```

### 内置调色板

引入 `zest:palette` 后可直接使用：

```zss
@use "zest:palette"

// 语义色（带 !default，可覆盖）
$primary: #6c63ff   // 覆盖默认值

.btn-primary
  bgc: $primary
.btn-success
  bgc: $success
.btn-danger
  bgc: $danger

// 调色板色值
.bg-blue   { bgc: $palette-blue }
.bg-purple { bgc: $palette-purple }
```

### 动画工具箱

```zss
@use "zest:animations"

.loading
  @apply animate-spin

.notification
  @apply animate-slide-in-right

.pulse-button
  @apply animate-pulse
```

### 渐变工具箱

```zss
@use "zest:gradients"

.hero-bg
  @apply bg-gradient-primary

.rainbow-header
  @apply bg-gradient-rainbow
```

### 滤镜与变换

```zss
@use "zest:filters"

.glass-card
  @apply backdrop-blur-md
  bgc: rgba(255,255,255,0.8)

.hover-zoom
  @apply scale-100
  &:hover
    @apply scale-110
    tr: transform 0.2s
```

## @export — CSS 变量导出

将 ZSS 变量输出为 CSS 自定义属性：

```zss
let primary = #3b82f6
let radius  = 0.5r

@export $primary
@export $radius

// 生成：
// :root { --primary: #3b82f6; }
// :root { --radius: 0.5rem; }
```

## @each — 循环生成

```zss
@each $size in (sm, md, lg, xl)
  .text-#{$size}
    fs: var(--text-$size)

// 展开为 .text-sm / .text-md / .text-lg / .text-xl
```

## @for — 数值循环

```zss
@for $i from 1 through 4
  .col-#{$i}
    w: calc($i * 25%)
```

## @if / @else — 条件

```zss
@mixin theme($dark: false)
  @if $dark
    bgc: #1a1a2e
    c: #e0e0e0
  @else
    bgc: #ffffff
    c: #1a1a2e
```

## 响应式断点简写

在规则集内用 `@sm`、`@md`、`@lg`、`@xl`、`@2xl` 代替完整的 `@media` 查询：

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
| `@2xl` | `(min-width:1536px)` |

## 自动前缀

ZSS 自动为需要浏览器前缀的属性添加 `-webkit-`、`-moz-`、`-ms-` 前缀：

```zss
.modal
  backdrop-filter: blur(10px)
  // 自动生成：
  // -webkit-backdrop-filter: blur(10px);
  // backdrop-filter: blur(10px);

  user-select: none
  // 自动生成 -webkit-、-moz-、-ms- 前缀
```

## 完整示例

```zss
// 使用 F# 风格语法 + 全部新特性
@use "zest:palette"
@use "zest:utilities"

let radius = 0.5r
let shadow = 0 4px 12px rgba(0,0,0,0.1)

@mixin card
  @apply d-block bg-white p-4
  bdr = radius
  bxsh = shadow

.feature-card
  @include card
  tr = box-shadow 0.2s

  &:hover
    bxsh = shadow |> alpha(0.15)
    trf = scale(1.02)

  h3
    fs = 1.1r
    c = $primary |> darken(10%)

  p
    fs = 0.9r
    c = $text-muted
    lh = 1.6
```

## 集成

`assets/` 目录下的 ZSS 文件在 `zest build` 时**自动编译为 CSS**。输出目录结构保持镜像：

```
assets/css/style.zcss  →  _site/assets/css/style.css
```

[← 返回首页](/)
