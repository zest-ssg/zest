<p align="center">
  <img src="zest.png" alt="Zest" width="128" height="128">
</p>

<h1 align="center">Zest SSG</h1>

<p align="center"><em>Zenith Efficient Static Toolkit</em></p>

<p align="center">
  <a href="LICENSE">License</a> · <a href="#快速开始">快速开始</a> · <a href="#文档">文档</a>
</p>

---

**Zest** 是一个 F# + C# 混合静态站点生成器，模板即真实代码——不是字符串。核心理念：模板语言和宿主语言应合二为一。

## 特性

- **模板即代码** — `.zpage.fsx` 是真正的 F# 脚本，构建时通过 `dotnet fsi` 执行。完整 F# 能力：列表推导、模式匹配、字符串插值、任意计算。
- **`.zhtml` 轻量页面** — 纯 HTML 页面，可选 Nunjucks 模板语法。零 FSI 开销。
- **HTML DSL** — 声明式组合 HTML：`render [ h1 []; p [] ]`。
- **Markdown** — 标准 `.md` 文件，支持 frontmatter。
- **ZCSS** — CSS 超集，支持嵌套、F# 风格 `let` 绑定、数学表达式、颜色函数、mixin——编译为标准 CSS。
- **ZestNjk 模板** — Nunjucks 兼容模板引擎，用于布局：过滤器、表达式、宏、`{% if %}`、`{% for %}`、模板继承、Zest API 集成。使用 `.znjk` 扩展名。
- **`_init.fsx`** — 可选的初始化脚本（构建前运行），用于注入动态数据、加载 JSON/TOML、读取环境变量。
- **TOML 配置** — 零配置默认值；通过 `_config.toml` 和 `_data/*.toml` 自定义。不用 YAML。
- **热重载** — `zest serve` 监视文件变化并自动重建。
- **批量求值** — 多个 F# 页面脚本在单个 FSI 进程中求值，构建速度快。
- **增量构建** — 文件变更检测跳过未变化的页面和静态资源。
- **跨平台** — 构建 Windows x64、Linux x64/ARM64、macOS ARM64。

## 快速开始

```bash
# 创建新项目
zest init my-site

# 开发模式（热重载）
cd my-site && zest serve --port 8080

# 生产构建
zest build

# 预览构建后的站点
zest preview
```

## 示例：`.zpage.fsx` 页面

```fsharp
// @title Hello World
// @layout default
// @description 我的第一个 Zest 页面

let pageTitle = "来自 F# 的问候"
let items = ["F#"; "Zest"; "SSG"]

render [
    h1 [ text pageTitle ]
    p  [ text "此页面由真实的 F# 代码在构建时生成。" ]
    ul [ for i in items -> li [ text i ] ]
]
```

## 示例：ZCSS 样式表

```zcss
// F# 风格 let 绑定与数学表达式
let primary    = #3b82f6
let space1     = 0.25r
let space4     = space1 * 4     // 1rem
let primary-light = primary |> lighten(45%)

.tag
  color: $primary
  background-color: $primary-light
  padding-block: $space4
  border-radius: 9999px
```

编译为：

```css
.tag {
  color: #3b82f6;
  background-color: #adf4ff;
  padding-block: 1rem;
  border-radius: 9999px;
}
```

## 示例：`_init.fsx`

```fsharp
// _init.fsx — 每次构建前运行
addGlobal "api_url" "https://api.example.com"

let team = loadJson "data/team.json"
addGlobal "team" team

let env = loadEnv "ZEST_ENV"
if env = "production" then
    addGlobal "analytics_id" "UA-XXXXX-Y"
```

## 项目结构

```
my-site/
├── _config.toml            # 站点配置（TOML）
├── _init.zpage.fsx         # 可选的初始化脚本（构建前运行）
├── _data/
│   └── site.toml           # 全局数据（可供脚本/模板访问）
├── content/
│   ├── index.zpage.fsx     # 首页（F# 脚本模板）
│   ├── about.md            # 关于页（Markdown）
│   └── posts/
│       ├── hello-world.zpage.fsx
│       └── contact.zhtml   # 纯 HTML（无 FSI 开销）
├── _layouts/
│   ├── default.html        # 布局（Nunjucks 或原生替换）
│   └── post.html
├── assets/
│   └── css/
│       └── style.zcss      # ZCSS → 自动编译为 style.css
└── _site/                  # 构建输出（自动生成）
```

## 架构

| 项目 | 语言 | 职责 |
|---------|----------|----------------|
| **Zest.App** | C# | CLI 入口，命令路由 |
| **Zest.Engine** | F# | 核心引擎：构建、HTML DSL、ScriptRunner、Markdown、ZCSS 编译器、ZestNjk 模板引擎 |
| **Zest.Dsl** | F# | FSI 脚本求值的预编译 DSL 辅助函数 |
| **Zest.Infra** | C# | 配置加载、文件监视、开发服务器 |

## 从源码构建

```bash
git clone https://github.com/zest-ssg/zest
cd zest
dotnet build Zest.sln

# 为你的平台发布
dotnet publish src/Zest.App/Zest.App.csproj -c Release -r win-x64 --self-contained false
# Linux:  -r linux-x64
# macOS:  -r osx-arm64
```

## 文档

### 文件类型

| 扩展名 | 用途 | 处理方式 |
|-----------|---------|------------|
| `.zpage.fsx` | F# 脚本模板（F# + Markdown + HTML DSL） | 通过 `dotnet fsi` 编译 |
| `.znjk` | Zest Nunjucks 模板（Nunjucks 兼容语法 + Zest API 集成） | ZestNjkEngine 渲染 — 支持过滤器、表达式、`{% if %}`、`{% for %}`、宏、模板继承 |
| `.zcss` | ZCSS 样式表（CSS 超集） | 编译为 `.css` |
| `.md` | 标准 Markdown | 渲染为 HTML |
| `.toml` | 配置和数据（不用 YAML） | 构建时解析 |

### 命令

| 命令 | 描述 |
|---------|-------------|
| `zest build` | 构建站点到 `_site/` |
| `zest serve` | 启动开发服务器（热重载） |
| `zest preview` | 预览构建后的站点 |
| `zest init <name>` | 创建新项目 |
| `zest clean` | 清理构建输出 |

### ZCSS 参考

| 功能 | 语法 |
|---------|--------|
| 变量（SCSS） | `$name: value;` |
| 变量（F#） | `let name = value` |
| 数学 | `let x = 0.25r * 4` |
| 颜色函数 | `lighten(#hex, %)`, `darken(#hex, %)`, `mix(a, b, %)` |
| 管道运算符 | `value \|> fn(args)` → `fn(value, args)` |
| 单位简写 | `r`→`rem`, `p`→`%` |
| 属性简写 | `py`→`padding-block`, `mx`→`margin-inline`, `bgc`→`background-color` |
| 嵌套 | 缩进模式或大括号模式 |
| Mixin | `@mixin`, `@include` |
| 循环 | `@each`, `@for` |
| 条件 | `@if`, `@else` |
| 内置模块 | `@use "zest:utilities"`, `@use "zest:palette"` 等 |

### 布局引擎

| 引擎 | 配置值 | 功能 |
|--------|-------------|----------|
| **ZestNjk**（默认） | `template_engine = "znjk"` | 过滤器、表达式、`{% if %}`、`{% for %}`、宏、模板继承、Zest API 过滤器（`pages_by_tag`、`recent`、`by_collection`、`search`、`where`） |
| **原生替换** | `template_engine = "replace"` | 简单的 `{{ variable }}` 替换 |

### HTML DSL 参考

```fsharp
// 元素
h1 [ text "标题" ]
p  [ text "段落" ]
a  [ href "https://example.com"; text "链接" ]

// 属性
div [ class' "container"; id "main" ] [ ... ]

// CSS 类快捷方式
divC "card" [ p [ text "内容" ] ]   // <div class="card">
spanC "badge" [ text "新" ]         // <span class="badge">

// 列表推导
ul [ for item in items -> li [ text item ] ]

// 条件
if condition then
    p [ text "是" ]
else
    p [ text "否" ]
```

### `_init.fsx` API

| 函数 | 用途 |
|----------|---------|
| `addGlobal key value` | 注入键值到全局数据 |
| `loadJson path` | 解析 JSON 文件 |
| `loadToml path` | 解析 TOML 文件 |
| `loadEnv key` | 读取环境变量 |
| `console_log msg` | 调试输出到 stderr |
| `exec cmd args` | 运行 shell 命令 |

## 设计理念

**Zest 不是通用静态站点生成器。** 它是针对特定约束的特定解决方案。

1. **F# 即模板** — 模板即程序。`.zpage.fsx` 文件是真实的 F# 代码，不是字符串。
2. **ZCSS 即布局引擎** — 不是 CSS 预处理器，而是输出 CSS 的布局引擎。
3. **TOML 即契约** — 绝对不用 YAML。
4. **JavaScript 服从秩序** — 无 Node.js、无 npm、无打包器。JavaScript 仅用于客户端交互。
5. **狂热少数派** — 为热爱 F#、痛恨 YAML、偏好简单工具的人而建。

## License

Apache 2.0 — 详见 [LICENSE](LICENSE).
