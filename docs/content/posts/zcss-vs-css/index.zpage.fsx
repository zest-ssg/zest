// @title  ZSS vs CSS 全面对比
// @layout default
// @permalink /posts/zcss-vs-css/
// @tags   zss, 教程
// @date   2026-06-19

# ZSS vs CSS：全面对比

ZSS 省去了大量样板代码。下面是同一份样式的对比：

## CSS 原始写法

```css
.card {
  background-color: #fff;
  border-radius: 0.5rem;
  padding: 1.5rem;
  box-shadow: 0 2px 8px rgba(0,0,0,0.1);
}
.card:hover {
  box-shadow: 0 4px 16px rgba(0,0,0,0.15);
}
.card h3 {
  font-size: 1.1rem;
  font-weight: 700;
  margin-bottom: 0.5rem;
}
```

## ZSS 等价写法

```zss
$radius: 0.5r;

.card {
  bgc: #fff;
  bdr: $radius;
  p:   1.5r;
  bxsh: 0 2px 8px rgba(0,0,0,0.1);

  &:hover {
    bxsh: 0 4px 16px rgba(0,0,0,0.15);
  }

  h3 {
    fs: 1.1r;
    fw: 700;
    mb: 0.5r;
  }
}
```

## 数据对比

| 指标 | CSS | ZSS |
|------|-----|-----|
| 代码行数 | 16 | 15 |
| 字符数 | **412** | **240** |
| 嵌套层级 | 无 | 3 层 |
| 变量 | 需预处理器 | 内置 |
| 简写属性 | 无 | 60+ 项 |

## 核心优势

- **减少约 42% 字符数**
- **内置嵌套**消除选择器重复
- **变量系统**无需 Sass/Less
- **`@mixin` / `@include`** 复用样式模式
- **颜色函数**：`lighten`、`darken`、`alpha`、`mix`
- **值简写**：`1.5r` → `1.5rem`、`50p` → `50%`
- **属性简写**：`bgc` → `background-color`

## 输出

所有 ZSS 编译为**标准 CSS**，零运行时开销。

[← 返回指南](/guide/)
