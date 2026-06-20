// @title  功能演示：.zest.fsx 实战
// @layout default
// @permalink /hello-world/
// @tags   演示, fsharp
// @date   2026-06-20

# .zest.fsx 实战演示

本文展示了 `.zest.fsx` 文件的工作方式——通过 `// @` 注解设置元数据，Markdown 编写内容。

## 元数据注解

```fsharp
// @title       Demo: .zest.fsx in Action
// @layout      default
// @permalink   /hello-world/
// @tags        demo, fsharp
// @date        2026-06-20
```

## Markdown 特性

### 行内格式

- **粗体**：`**双星号**`
- *斜体*：`*单星号*`
- ~~删除线~~：`~~双波浪线~~`
- `行内代码`：反引号
- [链接](/guide/)：`[文字](地址)`

### 代码块

带语法高亮的围栏代码块：

```fsharp
// F# 非常适合构建 DSL
let greet name = sprintf "你好，%s！" name
let message = greet "Zest"
```

### 表格

| 特性 | 语法 | 示例 |
|------|------|------|
| 元数据 | `// @关键词 值` | `// @title 首页` |
| 粗体 | `**文字**` | **文字** |
| 链接 | `[标签](地址)` | [Zest](/guide/) |
| 图片 | `![说明](src)` | ![图标](https://via.placeholder.com/40) |

### 列表

1. 有序列表第一项
2. 第二项
   - 嵌套无序
   - 更多项
3. 第三项

### 引用

> Zest 是一个基于 F# 的现代静态站点生成器。
> 它将 F# 的表达力与 Markdown 的简洁性融于一身。

### 分割线

---

## TOML 数据集成

Zest 从 `_data/*.toml` 加载全局数据。例如 `site.toml` 提供了 `site.author` 和 `site.social` 等值，在整个模板中都可访问。

## 继续阅读

- [使用指南](/guide/)
- [ZSS 样式系统](/zss/)
- [模板语言参考](/templates/)
- [ZSS vs CSS 对比](/posts/zss-vs-css/)
