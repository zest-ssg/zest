# Terminal 主题迁移 Bug 追踪与升级计划

> 记录将 `hugo-theme-terminal` 迁移到 `zest-theme-terminal` 过程中遇到的所有坑。
> 用于追踪已知问题、当前 workaround，并为 Zest 引擎未来升级/重构提供清单。
> 最后更新：2026-07-19

---

## 引擎升级进度（2026-07-19 本批）

本次按"仿 `md` 三引号内联机制"完成了 JS 内嵌能力（§九 L2/L3），修复全部 P0/P1 引擎
Bug，并系统性增强了 ZCSS 与 F# DSL 的兼容性/稳健性/拓展性/性能。全部通过 342 项测试
（296 引擎 + 46 App，0 失败）。

### 引擎 Bug 修复

| 项 | 状态 | 说明 |
|----|------|------|
| §1.1 `md` FS0074 | ✅ 已修复（既有） | `ScriptDiscovery.getIsolatedDslDll` 已复制全部依赖 DLL |
| §1.2/1.3 TOML 数组/嵌套 | ✅ 已修复 | `BuildData.tomlToNative` 递归转 `Dictionary`/`array`/标量，Nunjucks 可遍历 |
| §1.4 过滤器参数内管道 | ✅ 已修复 | 新增 `splitTopLevelPipes`/`splitTopLevelArgs`，参数走 `evalExpr` |
| §1.5 `int` 过滤器小数 | ✅ 已修复 | `try int (float s)` |
| §1.6 `set` 标签 | ✅ 已修复（既有） | 已实现内联与块级 set |
| §1.7 算术+嵌套管道 | ✅ 已修复 | 新增 `evalPipe`（最低优先级），`a/b \| round` 正确解析为 `(a/b)\|round` |
| §1.9 DevServer 空引用 | ✅ 已修复 | `OnChange`/`Rebuild` 加 null 守卫 |
| §1.10 ZCSS 连字符变量 | ✅ 已修复 | `Evaluator.bareVarRe` 捕获组 `[\w-]*`；同步修正 `ParserCore.resolveVarRefs` |
| §2.1 `pages_by_collection` | ✅ 已修复（既有） | `ZestContext.Pages` 已含 date/tags/description |
| §2.2 `by_collection` 排除索引 | ✅ 已修复 | 新增 `exclude_index` 可选第二参数 |
| §3.2 `md` 缩进不渲染 | ✅ 已修复 | 新增 `dedent`/`mdDedent` |
| §3.3 tags 分隔符 | ✅ 已修复 | 正则 `[,;\s]+` 分割，支持逗号/空格/分号 |
| §九 L2 `js` 内联块 | ✅ 已实现 | `js`/`jsModule`，仿 `md` 三引号 + 自动去缩进 |
| §九 L3 `jsonBlock` | ✅ 已实现 | `jsonBlock name data`，JSON 序列化 + `jsSafe` 转义 |

### ZCSS 增强（兼容性 / 稳健性 / 性能）

| 增强项 | 说明 |
|--------|------|
| CSS 函数名透传 | `cssKeywords` 新增 50+ CSS 函数名（`calc`/`clamp`/`color-mix`/`linear-gradient`/transform/filter/色彩空间等），防止误解析为变量 |
| `calc()` 单位兼容性 | `MathEvaluator` 对 `+`/`-` 操作数单位不兼容时（如 `100% - 2rem`）回退原样输出，不再错误预计算 |
| 结果缓存 | `Processor.processTextWithBase` 按 FNV-1a 内容哈希缓存，dev-server 重建跳过未变更文件 |
| 错误防护 | 管线 try/catch，malformed 输入返回 `/* ZCSS ERROR */` 注释而非崩溃 |

### F# DSL 增强（语法糖 / 实用功能）

| 模块 | 新增 |
|------|------|
| `DslHtml` | 语义元素：`aside`/`figure`/`figcaption`/`time`/`details`/`summary`/`dl`/`dt`/`dd`/`address`/`cite`/`q`/`sub`/`sup`/`kbd`/`samp`/`progress`/`meter`/`output` 等；属性糖：`data'`/`aria`/`id'`/`cls`/`role`/`href`/`src`/`type'`/`boolAttr`；通用构造器 `el`/`elVoid`/`attrsOf`；`comment`/`nbsp`/`fragment`/`fragmentLines` |
| `DslSugar` | Option 渲染：`opt_str`/`opt_or`/`opt_map`/`opt_when`；连接：`intersperse`/`join_lines`/`join_comma`/`join_with`；文本：`truncate_str`/`pad_right`/`pad_left`/`pluralize`/`pluralize_with`/`capitalize`/`titleize` |
| `DslComponents` | 导航：`navLink`/`navList`/`breadcrumb`；徽章：`tagBadges`/`badgeC`；媒体：`icon`/`figureResponsive`/`videoEmbed`；状态：`progressBar`/`meterBar`；社交：`socialLink`/`contactList` |

### 新增测试

- `DslInlineBlocksTests.fs`：dedent/mdDedent/js/jsModule/jsonBlock + DslSugar（Option/连接/文本格式化）+ DslHtml 属性糖
- `NunjucksEngineTests`：迁移修复模块（int 小数/参数管道/算术嵌套管道）
- `ZcssProcessorTests`：连字符变量 + CSS 函数透传（color-mix/calc/clamp/gradient/transform）+ 错误防护 + 缓存

### 模板转换器优化

| 转换器 | 优化 |
|--------|------|
| `HamlConverter` | 全部 regex 提升为模块级静态；`indentLevel` 支持制表符和任意宽度缩进；文本/属性 HTML 转义；实现 `:css`/`:javascript`/`:markdown` 过滤器；HTML5 void 标签；转换结果缓存 |
| `PugConverter` | 同上 regex 静态化/indentLevel/HTML 转义/HTML5 void；`include` → `{% include %}`；属性正则解析；转换缓存 |
| `HandlebarsMustacheConverter` | 全部 regex 提升为模块级静态；修复 `{{this}}` 双重处理；新增 `{{#with}}`/`{{else if}}`/`{{#unless}} else`/`{{lookup}}`；转换缓存 |
| `TemplateCompat` | 统一 `convertIfNeeded` 路由（Haml/Pug/Handlebars/Mustache 一站式） |
| `TemplateManager` | `clearCaches` 同步清理转换缓存 |
| `TemplateUtils`（新增） | 共享 `htmlEncode`/`attrEncode`/`isVoidElement`/`cachedConvert`/`hashSource` |

### 文档、Starters、Theme 升级

- **docs/**：`dsl-api.md` 新增 js/jsModule/jsonBlock/dedent/mdDedent + 20+ 语义元素 + 属性糖 + 新组件 + 语法糖文档；`zcss.md` 新增 CSS 函数透传/calc 单位兼容/缓存/错误防护；`templates.md` 新增转换器特性详解 + `by_collection` exclude_index + TOML 可遍历 + 管道优先级说明；`README.md` 特性列表更新
- **Starters/**：`_init.zest.fsx` 用原生 TOML 数组注入 socials/features；新增 `_data/nav.toml`；`header.html` 改为遍历 `site.nav.items`；新增 `content/features.zest.fsx` 演示页（js/jsonBlock/breadcrumb/tagBadges/progressBar/icon/mdDedent/pluralize）；README 更新
- **theme/**：`_init.zest.fsx` 移除 `nav_html` workaround（§1.2/1.3 已修复）；`header.html` 改为 Nunjucks `{% for %}` 遍历 `site.nav.items`；`footer.html` 新增 social links 遍历 `site.socials`；`index.njk`/`posts/index.njk` 用 `by_collection("posts", true)` 排除索引页 + 显示标签；`tags.njk` 显示每标签文章数；新增 `posts/zest-dsl-playground.zest.fsx` 特性演示文章

---

## 下一步建议

1. **§1.8**：`LayoutEngine.processIncludes` 支持 `.zest.fsx` 经 FSI 求值的 include。
2. **§3.1**：补充 permalink 约定文档（`/:collection/:slug/`）。
3. **§4.1**：验证 `color-mix()` 在各 CSS 属性中的表现（本批已确保透传，可移除 workaround）。

---


## 概述

迁移涉及 Hugo（Go 模板）→ Zest（F# DSL `.zest.fsx` + Nunjucks `.njk` + ZCSS `.zcss`）。
混合模板机制下，Nunjucks 与 F# 脚本之间的数据桥接、ZCSS 的 F# 风格语法、
以及引擎对 TOML/过滤器/算术的支持缺口，暴露出一系列问题。

按层级分类如下，标注 ★ 的为引擎层 Bug（需升级引擎根除），标注 ◎ 的为主题层 workaround。

---

## 一、引擎层 Bug（需升级 Zest 引擎）

### 1.1 ★ `md` 函数 FS0074 程序集引用缺失（Zest 版本过低）

- **现象**：所有调用 `md` 的 `.zest.fsx`（about + 3 篇文章）编译失败，fallback 到 Markdown 模式：
  ```
  error FS0074: 通过"Zest.Engine.Html.MarkdownEngine"引用的类型是在一个未被引用的程序集中定义的。
  必须添加对程序集"Zest.Engine"的引用。
  ```
- **根因**：`md`（`src/Zest.Dsl/DslUtilities.fs:176`）内部调用 `Zest.Engine.Html.MarkdownEngine.toHtml`，
  但 FSI 脚本宿主 preamble（`src/Zest.Engine/Scripting/ScriptRunner.fs:61-78` `buildPreamble`）
  只 `#r "Zest.Dsl.dll"`，未 `#r "Zest.Engine.dll"`。隔离 DLL 机制（`getIsolatedDslDll`）
  仅复制 `Zest.Dsl.dll`，`Zest.Engine.dll` 未进入 FSI 解析路径。
  **最终确认根因是系统环境中的 Zest 版本过低（旧版隔离 `Zest.Dsl.dll` 缓存）。**
- **影响**：`.zest.fsx` 无法用 `md` 渲染 Markdown，文章页降级为纯 Markdown 文本。
- **当前处理**：用户升级 Zest 版本解决。
- **引擎修复建议**：`buildPreamble` 增加 `#r "Zest.Engine.dll"`（与 Zest.Dsl 同目录），
  或让 `getIsolatedDslDll` 一并复制 `Zest.Engine.dll` 到隔离目录。

### 1.2 ★ Nunjucks 侧 `_data` TOML 数组被 ToString

- **现象**：`{{ site.nav.items }}` 输出为 `[object Object]` 类字符串，不可遍历，导航空白。
- **根因**：`BuildData.loadGlobalData`（`src/Zest.Engine/Build/BuildData.fs:33-34`）把
  `nav.items`（TomlArray）存入 globalData；但注入 Nunjucks 时数组对象被 `ToString()`，
  丢失结构。F# 侧 `SiteData`（`ScriptRunner.fs:48-51`）同样 `ToString()` 所有值。
- **影响**：无法用 `nav.toml` 的 `[[items]]` 数组驱动 Nunjucks 导航。
- **当前 workaround（◎）**：用 `_init.zest.fsx` 预构建钩子（F#）读取 `nav.toml`，
  手动解析 `[[items]]`，`addGlobalFunction "nav_html"` 注入渲染好的导航 HTML 字符串，
  `header.html` 用 `{{ site.nav_html | safe }}`。
- **引擎修复建议**：`globalData` 注入 Nunjucks 时保留 TOML 数组/表为可遍历结构
  （`IDictionary`/`IList`），而非 ToString。

### 1.3 ★ `site.params.*` 嵌套访问失败

- **现象**：`{{ site.params.showReadTime }}`、`{{ site.params.dateFormat }}` 等返回空/异常。
- **根因**：同 1.2，`params` 表整体被 ToString，嵌套属性不可访问。
- **影响**：`single.njk` 无法用 `showReadTime` 控制阅读时间显示；`date(site.params.dateFormat|...)` 失效。
- **当前 workaround（◎）**：去掉 `showReadTime` 判断，总是显示；date 格式用字面量 `"MMM d, yyyy"`。
- **引擎修复建议**：同 1.2。

### 1.4 ★ Nunjucks 过滤器参数内的管道不求值

- **现象**：`{{ page.date | date(site.params.dateFormat | default('MMM d, yyyy')) }}`
  输出乱码（如 `0iAe.para00.10aAeor0a`）。
- **根因**：Zest NunjucksEngine（`src/Zest.Engine/Template/NunjucksEngine.fs`）不正确支持
  过滤器**参数**中的管道 `|`，args.[0] 未被求值为 `default()` 的结果。
- **影响**：任何 `filter(x | subfilter(y))` 写法失效。
- **当前 workaround（◎）**：去掉参数管道，用字面量 `date("MMM d, yyyy")`。
- **引擎修复建议**：过滤器参数解析时递归求值管道表达式。

### 1.5 ★ Nunjucks `int` 过滤器对小数返回 0

- **现象**：`((content | striptags | wordcount) / 200) | int` 始终输出 `0`。
- **根因**：`int` 过滤器（`NunjucksEngine.fs:578`）实现为 `try int s with _ -> 0`，
  `int "1.245"`（F# 对含小数点字符串 Parse）失败 → 返回 0。
- **影响**：阅读时间永远显示 0 min。
- **当前 workaround（◎）**：改为显示词数 `{{ content | striptags | wordcount }} words`。
- **引擎修复建议**：`int` 过滤器先 `float` 再 `int`（`try int (float s) with _ -> 0`），
  或用 `Math.Floor`/`Math.Round`。

### 1.6 ★ Nunjucks `set` 语句不支持

- **现象**：`{% set words = content | striptags | wordcount %}` 后 `words` 未定义。
- **根因**：Zest NunjucksEngine 未实现 `set` 标签（`ParserIndent`/渲染器无 set 处理）。
- **影响**：无法在模板内缓存中间值，复杂表达式无法拆分。
- **当前 workaround（◎）**：用内联表达式，避免 set。
- **引擎修复建议**：实现 `set` 标签，写入模板上下文。

### 1.7 ★ Nunjucks 算术在嵌套括号+管道组合下解析失败

- **现象**：`(((content | striptags | wordcount) + 199) / 200) | round` 结果仍为 0。
- **根因**：`+` 算术（`evalAdd`，`NunjucksEngine.fs:354`）虽存在，但在
  `(过滤器链) + 数字` 的嵌套组合下求值异常。
- **影响**：无法实现 ceil 等算术逻辑。
- **当前 workaround（◎）**：同 1.5，改用词数。
- **引擎修复建议**：审查 `evalAdd`/`evalMul` 与管道过滤器的优先级交互。

### 1.8 ★ `{{ include }}` 不执行 `.zest.fsx`

- **现象**：把 `_includes/header.html` 改名为 `.zest.fsx` 后，include 只插入 F# 源码文本，不执行。
- **根因**：`LayoutEngine.processIncludes`（`src/Zest.Engine/Build/LayoutEngine.fs:119-128`）
  仅做文本替换（`includes.TryGetValue name` → `ReadAllText`），不调用 FSI。
- **影响**：无法用 F# 渲染 include 片段。
- **当前 workaround（◎）**：用 `_init.zest.fsx` 注入全局变量（如 `nav_html`），include 用 Nunjucks 变量。
- **引擎修复建议**：支持脚本 include（`.zest.fsx` include 经 FSI 求值），
  或提供 `{{ render_partial "name" }}` 机制。

### 1.9 ★ `zest serve` DevServer 文件监视器 NullReferenceException

- **现象**：`zest serve` 文件变更时崩溃：
  ```
  Unhandled exception. System.NullReferenceException
    at Zest.Infra.Services.DevServer.<StartFileWatcher>g__OnChange|24_1(...)
  ```
- **根因**：`DevServer.StartFileWatcher` 的 `OnChange` 回调访问了 null 对象。
- **影响**：serve 模式下修改文件即崩溃，需重启。
- **当前 workaround**：用 `zest build` 替代 serve 验证，或重启 serve。
- **引擎修复建议**：`OnChange` 加 null 检查；排查 filewatcher 状态生命周期。

### 1.10 ★ ZCSS 含连字符的 `let` 变量名不解析

- **现象**：`let font-size = 1rem` 后引用 `font-size`，生成 CSS 为
  `--font-size: font-size;`（原样字符串，无效）。
- **根因**：`resolveVarRefs`（`src/Zest.Engine/Zcss/ParserCore.fs:155`）正则
  `(?<![a-zA-Z0-9#-])([\w-]+)(?![a-zA-Z0-9])` 对 `font-size` 匹配到 `font`
  （后跟 `-` 满足前瞻 `(?![a-zA-Z0-9])`），`TryGetValue("font")` 失败，不替换。
- **影响**：`--font-size`/`--line-height`/`max-width` 失效 → 字体异常 + container 撑满两边。
- **当前 workaround（◎）**：变量名改无连字符（`fontsize`/`lineheight`/`pagewidth`/`fontmono`）。
- **引擎修复建议**：修正正则前瞻为 `(?![\w-])`（排除连字符），使贪婪匹配整个含连字符变量名。

---

## 二、模板互操作 Bug

### 2.1 ◎★ SeoPage 互传：`pages_by_collection` 返回无 date 的 SeoPage

- **现象**：`.zest.fsx` 列表页 `pages_by_collection("posts")` 返回的 `p` 被推断为 `SeoPage`
  （仅 url/title/description/image，无 date/tags），访问 `p.date` 报 FS0039。
- **根因**：引擎把 Nunjucks 的 `getPagesForNunjucks()`（SeoPage 裁剪版）桥接进 F# 脚本，
  而非 `ZestContext.Pages`（完整匿名记录含 date/tags）。
- **影响**：`.zest.fsx` 无法用于列表页。
- **当前 workaround（◎）**：列表页改用 Nunjucks `.njk`，因 `pageToNunjucksDict`
  （`PageQuery.fs:84-94`）注入的是含 date 的完整字典。
- **引擎修复建议**：F# 脚本侧 `pages_by_collection` 应返回 `ZestContext.Pages` 的完整记录。

### 2.2 ◎ `by_collection` 含归档页自身

- **现象**：`pages | by_collection("posts")` 包含 `posts/index.njk`（归档页 url=`/posts/`），
  归档页无 date，`date` 过滤器 fallback 到当前日期。
- **根因**：`by_collection`（`FilterRegistry.fs:82-91`）按 url 首段匹配，归档页 url 首段也是 `posts`。
- **影响**：首页/归档列表多出归档页条目；`single.njk` 的 prev/next 指向归档页。
- **当前 workaround（◎）**：循环加 `{% if p.date %}` 过滤无 date 页；prev/next 加
  `and loop.previtem.date` 检查。
- **引擎修复建议**：`by_collection` 可增加排除 index 页的选项，或文档约定。

---

## 三、主题内容 Bug

### 3.1 ◎ permalink 路由：`// @permalink /` 在 F# 体系异常

- **现象**：`// @permalink /` 路由不符合预期（F#/.NET 路由机制与 HTML5 不同）。
- **根因**：`PermalinkRouter.computePermalink`（`Routing/PermalinkRouter.fs:19-28`）对 `/` 处理
  与内容页预期不符。
- **当前 workaround（◎）**：省略 permalink（`defaultRoute` 自动映射 index → `/`），
  或显式 `// @permalink index.html`。
- **建议**：文档明确 permalink 写法约定。

### 3.2 ◎ Markdown 裸语法：`md """..."""` 缩进导致不渲染

- **现象**：文章 `## What you get`、` ``` ` 代码块原样显示为文本。
- **根因**：`md """..."""` 三引号字符串内每行带 F# 代码缩进（8 空格），
  Markdown 标题 `##` 前 ≥4 空格不被识别为 ATX 标题；围栏 ` ``` ` 同理。
- **当前 workaround（◎）**：`md` 字符串内容顶格（无缩进）。
- **建议**：DSL 可提供 `md` 的去缩进选项，或在文档强调顶格约定。

### 3.3 ◎ tags 分隔符：空格 vs 逗号

- **现象**：`// @tags hugo terminal` 被当成单个标签 `"hugo terminal"`。
- **根因**：`MetaParser.applyPair`（`Parsing/MetaParser.fs:47-53`）按**逗号** `Split(',')` 分割 tags，
  空格不分。
- **当前 workaround（◎）**：用逗号 `// @tags hugo, terminal`。
- **建议**：文档明确 tags 用逗号；或 MetaParser 兼容空格分隔。

### 3.4 ◎ about 双重 `.content` 嵌套

- **现象**：about 页 `<div class="content"><div class="content">...` 双重嵌套。
- **根因**：`about.zest.fsx` 用 `divC "content"`，但 `base.njk` 已有 `.content` div。
- **当前 workaround（◎）**：去掉 `divC "content"`，直接输出内容。

---

## 四、ZCSS 语法 Bug

### 4.1 ◎ `color-mix` 缺逗号（原有）

- **现象**：`pagination.zcss` `color: color-mix(in srgb var(--foreground) 30%, transparent)`
  规则失效。
- **根因**：`srgb` 后缺逗号，CSS 语法错误（原 hugo 移植时带入）。
- **当前 workaround（◎）**：补逗号 `color-mix(in srgb, var(--foreground) 30%, transparent)`。
- **建议**：ZCSS TypeChecker 可校验 `color-mix` 参数语法。

### 4.2 ◎ `terminal.zcss` 用途混淆

- **现象**：`terminal.zcss` 原为空；hugo 原版 `terminal.css` 是用户自定义占位。
- **当前处理**：恢复为用户自定义占位注释，滚动条/选区等环境样式移入 `main.zcss`（核心）。
- **约定**：`terminal.zcss` 留给用户覆盖；核心样式在 `main/header/...zcss`。

---

## 五、样式 Bug

### 5.1 ◎ 顶栏宽度随内容跳动（滚动条）

- **现象**：长页面（文章）有垂直滚动条，短页面（about/404）无，可视宽度变化 ~15px，
  顶栏/内容宽度跳动。
- **当前 workaround（◎）**：`html { scrollbar-gutter: stable }`（`main.zcss`）预留滚动条空间；
  `.container { margin: 0 auto }` 居中统一宽度。

---

## 六、升级重构计划（按优先级）

### P0 — 引擎核心修复（阻塞 .zest.fsx 使用）

1. **[1.1]** `buildPreamble` 加 `#r "Zest.Engine.dll"`（或隔离复制），根除 `md` 的 FS0074。
   —— 用户已通过升级 Zest 版本确认解决，验证升级后 preamble 是否已包含。
2. **[1.10]** ZCSS `resolveVarRefs` 正则前瞻修正，支持含连字符变量名。
3. **[2.1]** F# 脚本侧 `pages_by_collection` 等返回完整记录（含 date/tags），消除 SeoPage 互传。

### P1 — Nunjucks 兼容性（影响模板表达力）

4. **[1.2]/[1.3]** globalData 注入 Nunjucks 时保留 TOML 数组/表结构（可遍历），
   消除 `_init.zest.fsx` workaround。
5. **[1.4]** 过滤器参数内管道递归求值。
6. **[1.5]** `int` 过滤器支持小数（先 float 再 int）。
7. **[1.6]** 实现 `set` 标签。
8. **[1.7]** 审查算术与管道优先级。

### P2 — 引擎健壮性

9. **[1.8]** 脚本 include 支持（或 render_partial 机制）。
10. **[1.9]** DevServer filewatcher null 检查。
11. **[2.2]** `by_collection` 排除 index 页选项。

### P3 — 文档与约定

12. **[3.1]** permalink 写法约定文档。
13. **[3.3]** tags 逗号分隔约定文档。
14. **[4.1]** ZCSS `color-mix` 语法校验。
15. **[3.2]** `md` 顶格约定文档。

---

## 七、主题层当前 workaround 汇总

升级引擎后可逐步移除以下 workaround：

| Workaround | 对应 Bug | 文件 |
|-----------|---------|------|
| 列表页用 `.njk` 而非 `.zest.fsx` | 2.1 | `content/*.njk` |
| `_init.zest.fsx` 读 nav.toml 注入 nav_html | 1.2 | `_init.zest.fsx` |
| 导航 active 用客户端 JS（nav_html 全局静态） | 1.2 | `assets/js/menu.js` |
| 去掉 `showReadTime` 判断 | 1.3 | `_layouts/single.njk` |
| date 用字面量 `"MMM d, yyyy"` | 1.4 | `_layouts/single.njk` |
| read-time 显示词数而非分钟 | 1.5/1.6/1.7 | `_layouts/single.njk` |
| ZCSS 变量名无连字符（fontsize 等） | 1.10 | `assets/css/main.zcss` |
| `md` 字符串内容顶格 | 3.2 | 文章页 |
| tags 用逗号 | 3.3 | 文章 frontmatter |
| 循环加 `{% if p.date %}` / prev/next 加 date 检查 | 2.2 | `index.njk`/`single.njk` |

---

*本文件随迁移进展持续更新。引擎升级后请逐项验证 workaround 是否可移除。*

---

## 八、设计讨论：是否需要 FSX → JS 翻译器？

### 结论：不建议内置 FSX→JS 翻译器

### 理由

1. **SSG 场景定位**：Zest 是构建时生成静态 HTML，客户端交互通常是少量增强
   （菜单/代码复制/主题切换），手写 JS 更直接高效。.zest.fsx 的价值是声明式
   HTML 生成，而非客户端逻辑载体。
2. **成熟方案已存在**：Fable（F#→JS 编译器）是工业级方案。需要用 F# 写客户端
   逻辑时直接用 Fable 编译独立模块，引擎不必重新造轮子。
3. **DSL 翻译价值低**：F# DSL（`divC`/`h1C` 等 HTML 构建器）翻译成 JS 意义不大
   ——JS 侧操作 DOM 或用模板（Nunjucks/WebC）更自然。翻译 HTML 构建器到 JS
   等于在 JS 里重建一套 DOM API，与原生 DOM/模板竞争无优势。
4. **维护负担**：翻译器需处理 F# 类型系统、模式匹配、异步、可区分联合等，
   复杂度高，与 Zest 轻量定位冲突，且需持续跟进 F# 语言演进。
5. **已有 WebC**：Zest 支持 `.webc` 组件（可含 `<script webc:setup>`），
   覆盖组件化交互需求，是更标准的组件 JS 方案。

### 何时考虑

- **不建议自研**：若 Zest 未来转向"全栈 F#"（SSR + 客户端同构），应集成 Fable
  而非自研翻译器。
- **Fable 集成路径**：需 F# 共享类型/逻辑到客户端时，用 Fable 编译独立 `.fs`
  模块为 `.js`，`.zest.fsx` 通过 L1（外部引用）加载。
- **判断标准**：仅当"用 F# 写客户端逻辑"成为高频需求且 Fable 集成体验不佳时，
  才重新评估。

---

## 九、设计建议：.zest.fsx 内嵌 JavaScript 的机制

### 设计原则

- **关注点分离**：HTML 结构（F# DSL）、样式（ZCSS）、交互（JS）各司其职。
- **F# 不拼 JS 字符串**：F# 擅长数据/结构，`sprintf "var x = %d" n` 式拼接易错、
  难维护、无语法检查。
- **复用现有机制**：参照 `md """..."""` 的三引号块模式，降低学习成本。
- **构建时 vs 运行时**：.zest.fsx 是构建时求值，内嵌 JS 是运行时执行，二者
  通过生成的 HTML `<script>` 桥接，不混淆。

### 三层方案

#### L1：外部 JS 引用（推荐，默认）

JS 放 `assets/js/*.js`，`.zest.fsx` 不直接写 JS，由布局的 `_includes/scripts.html`
统一 `<script src>` 引用。

```html
<!-- _includes/scripts.html（现有） -->
<script src="/assets/js/menu.js"></script>
<script src="/assets/js/code.js"></script>
```

- **现状**：已采用，`menu.js`/`code.js` 即此模式。
- **适用**：站点级通用交互。
- **优点**：浏览器缓存、可被构建工具压缩、F# 不耦合 JS、JS 有独立 lint/类型检查。
- **优先级**：★★★（默认方式，文档化即可，无需新开发）。

#### L2：内联脚本块 `js`（DSL 新增函数，类似 `md`）

提供 `js """..."""` 函数，原样输出到 `<script>...</script>`，用于页面级少量内联脚本。

```fsharp
let html =
    divC "page" [
        h1 [ text "Hello" ]
        js """
        document.querySelector('.page h1').addEventListener('click', () => {
          alert('hi')
        })
        """
    ]
// → <div class="page"><h1>Hello</h1><script>...</script></div>
```

- **实现**：DSL 新增 `let js (code: string) = scriptTag code`，
  与 `md` 同构（三引号原样输出）。
- **适用**：单页一次性脚本、传递页面级配置、无需外部文件的轻量交互。
- **注意**：JS 内 `"""` 需转义；F# 不校验 JS 语法（同 `md` 不校验 Markdown）；
  构建时原样透传。
- **优先级**：★★（成本低，建议下一版本加入 DSL）。

#### L3：数据注入 `jsonBlock`（F# 计算 → JS 消费）

F# 生成 JSON 数据，注入 `<script>` 供客户端 JS 读取，实现类型安全的数据传递。

```fsharp
let cfg = {| theme = "dark"; postCount = 10; tags = [|"fsharp"; "ssg"|] |}
let html =
    divC "page" [
        jsonBlock "__PAGE_CFG__" cfg
        // → <script>window.__PAGE_CFG__ = {"theme":"dark",...}</script>
        // 客户端 assets/js/page.js 读 window.__PAGE_CFG__
    ]
```

- **实现**：DSL 新增
  `let jsonBlock (name: string) (data: obj) = sprintf "<script>window.%s = %s</script>" name (JsonSerializer.Serialize data)`。
- **适用**：F# 处理数据（统计、列表、配置），JS 负责渲染交互（图表、搜索、过滤）。
- **优点**：类型安全的数据传递，避免 F# 拼 JS，JSON 序列化处理转义。
- **优先级**：★（按需加入，配合客户端组件/数据驱动场景）。

### 与现有机制的关系

| 机制 | 定位 | 适用场景 |
|------|------|---------|
| **L1 外部引用** | 站点级 JS | 通用交互（菜单、代码复制） |
| **L2 `js` 内联块** | 页面级 JS | 单页一次性脚本 |
| **L3 `jsonBlock`** | 数据传递 | F# 数据 → JS 渲染 |
| **WebC**（已有） | 组件级 JS | 可复用交互组件（`<script webc:setup>`） |
| **Fable**（外部） | F# 写客户端 | 复杂客户端逻辑，编译后 L1 引用 |
| **Nunjucks** | 模板内 JS | `.njk` 内 `{% raw %}<script>{% endraw %}` |

### 不建议的做法

- **F# 字符串拼接 JS**（`sprintf "var x = %d" n`）：易错、难维护、无语法检查、
  转义地狱。用 L3 `jsonBlock` 替代。
- **.zest.fsx 内 `#r` 引用 JS 引擎（Jint/ClearScript）执行 JS**：偏离 SSG 构建时
  定位，增加运行时依赖，且构建时执行 JS 无意义（应输出给浏览器）。
- **自研 FSX→JS 翻译器**：见第八节，不建议。

### 实现路径建议

1. **L1** 已具备，在主题文档/README 说明约定即可。
2. **L2**（`js` 函数）：在 `Zest.Dsl/DslComponents.fs` 新增，与 `md` 并列，
   成本低、收益直接，建议下一版本。
3. **L3**（`jsonBlock`）：在 `Zest.Dsl/DslUtilities.fs` 新增，依赖 `System.Text.Json`，
   配合未来的客户端组件/数据驱动场景按需加入。

### 与 Bug 追踪的关联

- 当前主题的 `menu.js`（active 高亮）即为 L1 模式，因 `_init.zest.fsx` 注入的
  `nav_html` 是全局静态字符串，active 需客户端按 `location.pathname` 计算（见 1.2）。
  若未来 L3 落地，可将 `page.url` 经 `jsonBlock` 注入，JS 高亮更精准。
- L2/L3 不解决现有引擎 Bug，但提供了 `.zest.fsx` 与 JS 协作的标准范式，
  避免未来各主题自行发明内嵌 JS 写法。

---

*第八、九节为设计讨论，非已实现特性。实现前需在引擎侧评审 DSL 函数签名与安全性
（如 L2 的 CSP 兼容、L3 的 JSON 转义）。*
