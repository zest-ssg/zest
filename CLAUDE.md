# 重构增强 Zest SSG —— 提示词
## 一、总体目标与约束
你正在对开源项目 **zest-ssg/zest** 进行一次系统性的重构与增强。仓库地址：  
https://github.com/zest-ssg/zest
### 1.1 核心目标
在不破坏现有功能与现有用户项目的前提下，实现：
1. **模板兼容层全面升级**
   - 让现有的 `.njk` / `.liquid` / `.hbs` / `.mustache` / `.webc` / `.haml` / `.pug` 等模板的兼容性更好，接近甚至超越 11ty 的体验。
   - 提升 **Nunjucks 的 Zest 实现** 与官方 Nunjucks 的兼容性，做到语法、过滤器、宏、继承等行为几乎一致。
2. **构建性能优化**
   - 在大中型站点（数百到数千页面）上显著提升构建速度。
   - 支持更细粒度的增量构建、并行化、缓存策略。
3. **ZCSS 功能全面增强**
   - 在保持现有 CSS 生成的基础上，扩展 ZCSS 的 DSL 能力，提供更丰富的内置函数、模块系统和实用工具。
4. **DSL 语法与 API 增强**
   - 让 `.zest.fsx` / `_init.fsx` 等 F# 脚本的 API 更好用、更易发现、更易组合。
   - 提供更多“开箱即用”的数据加载、内容处理、SEO 等辅助函数。
5. **迁移与兼容体验增强**
   - 新增 CLI 和项目模板，帮助用户从 **Jekyll / Hexo / Hugo / 11ty** 等其他 SSG 快速迁移到 Zest。
   - 提供配置转换工具（例如 YAML → TOML）和“一键迁移脚本”。
6. **文档清晰易懂**
   - 编写结构清晰、层次分明、示例丰富的 README 与扩展文档。
   - 提供从“快速开始”到“高级扩展”的完整路径。
### 1.2 技术约束
- 目标平台：.NET 8+（与现有 Zest.App / Zest.Engine / Zest.Dsl / Zest.Infra 项目兼容）。
- 语言组合：C# 用于 CLI / 基础设施，F# 用于引擎和 DSL。
- **不引入 Node.js 运行时依赖**；前端 JS 仅作为客户端代码生成结果，构建时不依赖 Node。
- 命名与目录结构要求：
  - 新增代码文件和目录采用 **双词命名法**：两个语义准确、简洁清晰的英文单词，驼峰命名（如 `ContentCollector.fs`、`TemplateCompat.fs`）。
  - 参考 `Zest.Dsl` 中已存在的命名风格（如 `DslCollections.fs`、`DslComponents.fs`、`DslHtml.fs` 等）。
  - 目录名同样使用双词，如 `PageGenerator`、`AssetPipeline` 等。
---
## 二、架构与模块总览（重构后目标）
请参考现有架构表：
| 现有项目 | 职责（目标） |
|---------|-------------|
| Zest.App (C#) | CLI 入口、命令路由、迁移工具、配置转换 |
| Zest.Engine (F#) | 核心构建引擎、模板引擎、ZCSS 编译、内容管道 |
| Zest.Dsl (F#) | 面向 FSI 的 DSL 辅助函数、HTML DSL、SEO 工具等 |
| Zest.Infra (C#) | 配置加载、文件监视、开发服务器、缓存管理 |
在此基础上，新增/重构的模块建议：
- `src/Zest.Engine/Template`：保持现有模板相关文件（如 `NunjucksEngine.fs`、`TemplateCompat.fs`、`TemplateManager.fs` 等），重点增强：
  - `NunjucksCompat.fs` —— 针对 Nunjucks 兼容性的专门封装。
  - `TemplateCompat.fs` —— 升级为统一“兼容层”抽象，适配多种模板来源。
- `src/Zest.Engine/Build`：
  - 保持现有 `BuildEngine.fs`、`BuildCache.fs` 等。
  - 新增 `BuildScheduler.fs`、`IncrementalBuilder.fs` 等用于并行化与增量构建。
- `src/Zest.Engine/Zcss`：
  - 保持现有解析与编译模块（如 `ZcssParser.fs`、`ZcssCompiler.fs`、`ColorPipeline.fs` 等）。
  - 新增 `ZcssModules.fs`、`ZcssUtilities.fs` 扩展模块。
- `src/Zest.Dsl`：
  - 保持现有 DSL 文件（如 `DslHtml.fs`、`DslCss.fs`、`DslSeo.fs` 等）。
  - 新增 `DslMigration.fs`、`DslCompat.fs` 等迁移与兼容相关 API。
- `src/Zest.App`：
  - 在现有 `CommandLine`、`Controllers` 目录基础上，新增：
    - `MigrateCommand.cs` —— 迁移 CLI 入口。
    - `ConfigConverter.cs` —— YAML/TOML 等配置转换器。
    - `ScaffoldCommand.cs` —— 从其他 SSG 生成脚手架。
---
## 三、模板兼容层与 Nunjucks 兼容性增强
### 3.1 Nunjucks 兼容性目标
在现有 `NunjucksEngine.fs` 和 `TemplateCompat.fs` 的基础上，增强 Zest 的 Nunjucks 实现，使其：
1. **语法层面**：
   - 完整支持 Nunjucks 所有核心语法：
     - `{{ variable }}` / `{{ obj.prop }}` / `{{ arr[0] }}`。
     - `{% if %}` / `{% elif %}` / `{% else %}` / `{% endif %}`。
     - `{% for item in items %}` / `{% endfor %}`。
     - `{% set var = value %}` / `{% set %}` 块级赋值。
     - `{% include 'xxx.njk' %}` / `{% import 'xxx.njk' as macros %}` / `{% from 'xxx.njk' import macro as m %}`。
     - `{% macro name(args) %}...{% endmacro %}`。
     - `{% call macroName(args) %}...{% endcall %}`。
     - `{% filter filter1 | filter2 %}...{% endfilter %}`。
     - `{% raw %}...{% endraw %}`。
   - 支持模板继承 `{% extends "base.njk" %}` 和 `{% block name %}...{% endblock %}`。
2. **过滤器和全局函数**：
   - 兼容 Nunjucks 内置过滤器：
     - `upper`, `lower`, `title`, `capitalize`, `trim`, `strip`, `escape`, `e`, `first`, `last`, `length`, `list`, `join`, `sort`, `reverse`, `batch`, `slice`, `urlencode`, `json`, `default`, `round`, `abs`, `float`, `int`, `string`, `items`, `values`, `keys`, `merge`, `date` 等。
   - 在此基础上新增 Zest 特有过滤器：
     - `dateRfc822`、`dateIso`、`dateRss` 等方便 SEO / RSS。
     - `slugize`、`slugizePath` 用于 URL 安全处理。
     - `toHtml`、`toText` 用于 Markdown / HTML 互转。
   - 提供“兼容开关”，允许用户选择严格 Nunjucks 模式或 Zest 扩展模式。
3. **模板上下文与 API**：
   - 确保在模板中可访问：
     - `page`（标题、日期、分类、标签、集合、URL 等）。
     - `site`（站点级配置与全局数据）。
     - `collections` / `tags` / `categories` 等 11ty 风格的数据结构。
   - 提供 Zest 特有的全局函数：
     - `getPage(url)`、`getCollection(name)`、`where(items, prop, value)` 等。
### 3.2 模板兼容层架构升级
在 `src/Zest.Engine/Template` 目录下：
1. **接口抽象**：
   - 保持并扩展 `ITemplateEngine.fs`，明确统一接口：
     - `Render(templatePath, model)` → 渲染结果。
     - `Compile(templatePath)` → 可复用编译模板。
     - `AddFilter(name, filter)` / `AddGlobal(name, value)`。
   - 为不同来源（Nunjucks / Liquid / Haml / Pug 等）实现适配器。
2. **兼容层统一封装**：
   - 将 `TemplateCompat.fs` 升级为统一兼容层，负责：
     - 根据文件扩展名选择引擎（`.njk` → Nunjucks，`.liquid` → Liquid 等）。
     - 处理不同引擎之间的语法差异（例如 `{{ }}` vs `{{ }}` 空格行为）。
     - 提供统一的“模板错误上下文”信息（文件名、行号、片段）。
3. **Nunjucks 兼容性测试用例**：
   - 在 `tests/` 目录下新增 `NunjucksCompatTests.fs`：
     - 覆盖所有核心语法、过滤器和模板继承。
     - 每个 filter 至少一个正向/一个反向测试。
     - 对比官方 Nunjucks 文档中的示例，确保行为一致。
---
## 四、ZCSS 功能全面增强
在现有 `src/Zest.Engine/Zcss` 目录基础上：
### 4.1 语法与解析增强
1. **模块系统**：
   - 新增 `ZcssModules.fs`，实现：
     - `@use "zest:utilities"` —— 提供常用工具类（spacing、layout、visually-hidden 等）。
     - `@use "zest:palette"` —— 提供调色板、品牌色、语义色。
     - `@use "./my-theme.zcss"` —— 用户自定义模块。
   - 支持命名空间：`@use "zest:palette" as p;` 后通过 `p.primary` 访问。
2. **更多内置函数**：
   - 在 `BuiltinFunctions.fs` 中新增：
     - `contrast-color($bg)` —— 自动返回对比度较高的文本颜色。
     - `scale-color($color, $amount)` —— 更细粒度的亮度调整。
     - `tint($color, $amount)` / `shade($color, $amount)`。
   - 支持 CSS 环境变量生成：`env-color("primary")` → `var(--primary)`。
3. **布局与实用工具**：
   - 在 `BuiltinStyles.fs` 中新增：
     - `@layout responsive-container` —— 响应式容器 mixin。
     - `@layout grid-auto` —— 自动生成响应式网格。
   - 提供“Tailwind 风格”的实用工具类生成：
     - `@use "zest:utilities" as u;` 后通过 `u.spacing-4`、`u.flex-center` 等。
### 4.2 性能与缓存
- ZCSS 编译结果可缓存到 `.zcss-cache` 目录，避免重复解析。
- 支持“增量 ZCSS”——仅重新编译修改过的 `.zcss` 文件。
---
## 五、构建性能优化
在 `src/Zest.Engine/Build` 目录中：
1. **增量构建增强**：
   - 改造 `BuildCache.fs`，支持：
     - 文件哈希缓存（内容哈希 + 元数据）。
     - 依赖图记录（某个模板修改后，哪些页面需要重建）。
   - 新增 `IncrementalBuilder.fs`，封装：
     - `GetAffectedPages(changedFile)` → 受影响页面列表。
     - `RebuildPages(pagePaths)` → 最小重建范围。
2. **并行构建**：
   - 新增 `BuildScheduler.fs`：
     - 将页面分组，并行执行 F# 脚本和模板渲染。
     - 控制并行度（默认按 CPU 核心数）。
   - 确保 FSI 脚本执行仍然支持“批量求值”（现有特性）。
3. **构建缓存与持久化**：
   - 支持 `--cache-dir` 和 `--cache-max-age` 参数。
   - 提供 `zest clean --cache` 清理缓存。
4. **基准测试与性能监控**：
   - 新增 `BuildBenchmark.fs` / `BenchmarkCommand.cs`：
     - 输出各阶段耗时（解析、渲染、ZCSS 编译、写入）。
     - 提供性能回归检测（CI 中可选运行）。
---
## 六、DSL 语法与 API 增强
在 `src/Zest.Dsl` 目录下：
1. **新增 DSL 模块**：
   - `DslMigration.fs` —— 提供迁移相关 API：
     - `convertYamlToToml(yamlText)` / `convertTomlToYaml(tomlText)`。
     - `convertFrontmatter(fileContent, format)`。
   - `DslCompat.fs` —— 提供兼容层 API：
     - `compatPageFromJekyll(page)` —— 将 Jekyll 风格的 page 对象转换为 Zest 结构。
     - `compatPageFromHexo(page)` / `compatPageFromHugo(page)`。
2. **增强现有 DSL 模块**：
   - `DslCollections.fs`：
     - 新增 `paginate(items, perPage)` 返回分页数据结构。
     - 新增 `group(items, key)` —— 按某个属性分组。
   - `DslSeo.fs`：
     - 新增 `openGraphHtml(page)` —— 生成完整 Open Graph 标签块。
     - 新增 `twitterCardHtml(page, type)`。
     - 新增 `canonicalUrl(page)`。
   - `DslUtilities.fs`：
     - 新增 `readAllText(path)` / `writeAllText(path, content)`。
     - 新增 `loadYaml(path)` / `loadToml(path)`。
   - `DslHtml.fs`：
     - 新增 `htmlSafe(text)` —— HTML 转义。
     - 新增 `attrSafe(value)` —— 属性安全处理。
3. **_init.fsx API 增强**：
在现有 `_init.fsx` API 基础上：
- 新增：
  - `addFilter(name, fn)` —— 在 `_init.fsx` 中动态注册模板过滤器。
  - `addGlobalFunction(name, fn)` —— 向模板提供全局函数。
  - `registerMigration(migrateFn)` —— 注册自定义迁移函数，用于 CLI `zest migrate` 调用。
---
## 七、CLI 与迁移体验增强
在 `src/Zest.App` 的 `CommandLine` 和 `Controllers` 目录基础上：
1. **新增 CLI 命令**：
- `zest migrate <source-ssg> [options]`
  - `source-ssg`：可选 `jekyll`、`hexo`、`hugo`、`eleventy`。
  - 选项：
    - `--from <dir>` 源站点目录。
    - `--to <dir>` 目标 Zest 项目目录。
    - `--dry-run` 仅输出迁移计划，不写入文件。
  - 行为：
    - 扫描源 SSG 的配置、布局、内容、静态文件。
    - 生成对应的 Zest 项目结构（`_config.toml`、`_init.zest.fsx`、`content/`、`_layouts/` 等）。
    - 将 YAML 配置转换为 TOML（可选保留原格式）。
- `zest convert-config <from> <to> [options]`
  - 例如：
    - `zest convert-config yaml toml` —— 将当前目录下的 `_config.yml` 转为 `_config.toml`。
    - `zest convert-config toml yaml` —— 反向转换。
- `zest scaffold <template> [options]`
  - `template`：预置模板（`blog`、`docs`、`portfolio`、`empty`）。
  - 生成标准目录结构和示例文件。
2. **配置与兼容层**：
- `_config.toml` 新增：
  - `[compat]` 表：
    - `compat.jekyll = true` —— 启用 Jekyll 兼容行为（如 permalink 风格、默认布局等）。
    - `compat.hexo = true` —— 启用 Hexo 兼容行为。
    - `compat.hugo = true` —— 启用 Hugo 兼容行为。
  - `[template]` 表：
    - `template.engine = "nunjucks"`（默认）。
    - `template.nunjucks.compatibility = "strict"` 或 `"zest"`。
---
## 八、文档与示例
### 8.1 README 结构优化
在现有 README 的基础上：
- 重组为：
  1. 快速开始（5 分钟上手）。
  2. 核心概念（Template-as-Code、ZCSS、TOML 合约）。
  3. 文件类型与处理流程（表格）。
  4. CLI 命令速查。
  5. ZCSS 参考。
  6. HTML DSL 参考。
  7. _init.fsx API 参考。
  8. 迁移指南（从 Jekyll / Hexo / Hugo / 11ty）。
### 8.2 新增文档文件
- `docs/migrate-from-jekyll.md`
- `docs/migrate-from-hexo.md`
- `docs/migrate-from-hugo.md`
- `docs/zcss-modules.md`
- `docs/template-compat.md`
- `docs/performance-tuning.md`
每个文档要求：
- 有完整目录结构。
- 包含真实可运行的代码示例。
- 包含常见问题（FAQ）小节。
---
## 九、代码质量与注释规范
所有新增/重构的代码文件需满足：
1. **文件级注释**：
   - 每个文件顶部包含：
     - 模块名称（双词）。
     - 功能简述（2–3 句话）。
     - 依赖说明（如有）。
   - 示例：
     ```fsharp
     // DslMigration.fs
     //
     // Provides helper functions for converting existing SSG projects
     // (Jekyll/Hexo/Hugo) to Zest, including frontmatter and config
     // transformation from YAML to TOML.
     ```
2. **函数/类型注释**：
   - 每个公开函数必须有 `<summary>` 与 `<param>` 文档注释。
   - 对复杂逻辑添加行内注释解释“为什么”。
3. **命名要求**：
   - 文件名：`DslMigration.fs`、`NunjucksCompat.fs`、`BuildScheduler.fs` 等。
   - 目录名：`PageGenerator`、`AssetPipeline`、`TemplateCompat` 等。
   - 避免单字母或缩写，除非是广泛接受的（如 `Html`、`Css`）。
---
## 十、实施步骤建议（可按阶段推进）
建议 Claude 在执行时按以下阶段拆解任务：
1. **阶段 0：代码熟悉与基准测试**
   - 阅读 Zest 现有 README、架构表和关键源文件。
   - 构建一个包含多种文件类型（`.zest.fsx`、`.njk`、`.zcss`、`.md`）的示例站点，用于后续回归测试。
2. **阶段 1：模板兼容层增强**
   - 实现/升级 `NunjucksCompat.fs`，补充缺失的过滤器和语法。
   - 新增 `NunjucksCompatTests.fs`，确保核心行为与官方一致。
3. **阶段 2：ZCSS 功能增强**
   - 实现 `ZcssModules.fs`，引入模块系统。
   - 扩展 `BuiltinFunctions.fs` 和 `BuiltinStyles.fs`。
4. **阶段 3：构建性能优化**
   - 改造 `BuildCache.fs`，新增 `IncrementalBuilder.fs` 和 `BuildScheduler.fs`。
   - 添加性能基准测试。
5. **阶段 4：CLI 与迁移工具**
   - 实现 `MigrateCommand.cs`、`ConfigConverter.cs` 和相关迁移 API。
   - 提供 Jekyll / Hexo / Hugo 迁移文档和示例。
6. **阶段 5：文档与示例完善**
   - 重构 README，编写新文档，确保所有新功能都有对应示例。