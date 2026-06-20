// @title  ZSS 样式参考
// @layout default
// @permalink /zss/
// @tags   zss, 样式, 参考

# ZSS 样式参考

ZSS（Zest Style Sheets）是 **CSS 超集**，编译为标准 CSS。任何合法 CSS 文件都是合法 ZSS 文件。

## 变量

```zss
$primary: #6c63ff;
$radius:  0.5r;   // 编译为 0.5rem
$gap:     1.5r;   // 编译为 1.5rem

.button {
  bgc: $primary;  // 等价于 background-color
  bdr: $radius;   // 等价于 border-radius
  p:   0.6r 1.2r; // 等价于 padding
}
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

## 值简写

```
1r   → 1rem     50p  → 50%
100v → 100vh    0.3s → 0.3s
```

## 嵌套

```zss
nav {
  d: flex;
  gap: 1r;

  ul {
    ls: none;
    d: flex;
  }

  a {
    td: none;
    c: $primary;

    &:hover {
      td: underline;
    }
  }
}
```

## 混入

```zss
@mixin card($pad: 1.5r) {
  bgc: #fff;
  bdr: $radius;
  p: $pad;
  bxsh: 0 2px 12px rgba(0,0,0,0.07);
}

.post { @include card(); }
.sidebar { @include card(1r); }
```

## 颜色函数

```zss
.btn {
  bgc: lighten(#6c63ff, 20%);
  c:   darken(#fff, 80%);
  bdc: alpha(#6c63ff, 0.3);
  bxsh: 0 2px mix(#000, #6c63ff, 25%);
}
```

## 完整示例

```zss
// 按钮组件示例
$primary: #6c63ff;

.btn-primary {
  @include btn($primary, #fff);
  bgc: lighten($primary, 10%);

  &:hover {
    bgc: darken($primary, 10%);
  }
}
```

## @export — CSS 变量导出

将 ZSS 变量输出为 CSS 自定义属性（`:root { --xxx: ... }`）：

```zss
$primary: #3b82f6
$radius:  0.5r
@export $primary
@export $radius
// 生成：
// :root { --primary: #3b82f6; }
// :root { --radius: 0.5rem; }
```

## @each — 循环生成

```zss
@each $size in (sm, md, lg, xl) {
  .text-#{$size} {
    fs: var(--text-$size)
  }
}
// 展开为 .text-sm / .text-md / .text-lg / .text-xl
```

## 响应式断点简写

在规则集内用 `@sm`、`@md`、`@lg`、`@xl`、`@2xl` 代替完整的 `@media` 查询：

```zss
.grid {
  d: grid
  gtc: 1fr

  @md { gtc: repeat(2, 1fr) }   // min-width: 768px
  @lg { gtc: repeat(3, 1fr) }   // min-width: 1024px
}
```

| 简写 | 等价 media query |
|------|-----------------|
| `@sm` | `(min-width:640px)` |
| `@md` | `(min-width:768px)` |
| `@lg` | `(min-width:1024px)` |
| `@xl` | `(min-width:1280px)` |
| `@2xl` | `(min-width:1536px)` |

## 集成

`assets/` 目录下的 ZSS 文件在 `zest build` 时**自动编译为 CSS**。输出目录结构保持镜像：

```
assets/css/style.zss  →  _site/assets/css/style.css
```

[← 返回首页](/)
