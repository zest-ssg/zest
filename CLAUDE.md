
# Zest SSG 代码质量提升指南
## 📋 快速导航
- [核心原则](#核心原则) - 开发必读
- [命名规范](#命名规范) - 代码命名标准
- [注释规范](#注释规范) - 文档编写标准
- [代码审查](#代码审查) - 重构执行流程
- [Git规范](#git规范) - 提交管理标准
- [检查清单](#检查清单) - 快速验证表
---
## 核心原则
### 项目背景
- **仓库**: zest-ssg/zest
- **平台**: .NET 8+
- **架构**: C# (CLI/Infra) + F# (Engine/DSL)
- **阶段**: 功能基本完成，进入质量提升期
### 三大铁律
1. **命名必须双词** - 所有新增/重构的文件、目录、核心类型必须使用两个语义清晰的英语单词
2. **注释必须精准** - 编写简洁、语义干净的英语注释，解释"为什么"而非"怎么做"
3. **架构必须清晰** - 保持 C#/F# 边界，不混用编程范式
---
## 命名规范
### 双词命名法核心规则
#### 格式要求
```
文件/类/目录: WordOneWordTwo (PascalCase)
方法/变量:    wordOneWordTwo (camelCase)
```
#### 语义结构
| 位置 | 含义 | 示例 |
|------|------|------|
| 第一个词 | 领域/对象 | Template, Config, Build, Content |
| 第二个词 | 动作/角色/特性 | Renderer, Parser, Manager, Cache |
#### ❌ 禁止项清单
- **单字命名**: `Utils.fs`, `Helper.cs`, `Render.fs`, `Cache.cs`
- **三词以上**: `TemplateRenderEngine.fs` → 改用 `TemplateRenderer.fs`
- **非通用缩写**: `TmpParser.fs`, `CfgLoader.cs`
- **模糊动词**: `get()`, `process()`, `handle()`
### 命名对照表
| 上下文 | 错误示例 | 正确示例 | 改进说明 |
|--------|----------|----------|----------|
| F# 文件 | `Utils.fs` | `CommonHelpers.fs` | 明确职责范围 |
| F# 文件 | `Render.njk` | `TemplateRenderer.fs` | 使用全称+动作 |
| C# 类 | `Builder.cs` | `PageBuilder.cs` | 添加领域限定 |
| C# 命令 | `Migrate.cs` | `MigrationCommand.cs` | 明确类型角色 |
| 目录 | `Template/` | `TemplateEngine/` | 表达完整概念 |
| 目录 | `Zcss/` | `StyleCompiler/` | 或保留专有名词但内部双词 |
| 函数 | `get()` | `fetchData()` | 明确数据来源 |
| 函数 | `process()` | `parseOptions()` | 明确处理对象 |
### 命名验证工具脚本
```bash
# 检查不符合双词命名的文件
find . -name "*.fs" -o -name "*.cs" | grep -vE '([A-Z][a-z]+[A-Z][a-z]+\.fs|\.cs$)'
```
---
## 注释规范
### 文件头注释模板
```fsharp
// <FileName>.fs
//
// <一句话概括模块职责>.
// <可选：关键逻辑或依赖细节说明>.
//
// Dependencies: <关键外部模块或命名空间>
```
**示例**:
```fsharp
// ContentCollector.fs
//
// Traverses the content directory to discover source files and extract
// frontmatter metadata for the build pipeline.
//
// Dependencies: System.IO, FSharp.Data
```
### 模块/类注释标准
```fsharp
/// <summary>
/// 一句话概括功能（不超过 20 词）。
/// </summary>
module ModuleName =
    // 实现...
```
**检查要点**:
- ✅ 使用标准 XML 文档注释
- ✅ 解释"是什么"和"为什么"
- ❌ 不重复标识符名称
### 函数注释最佳实践
#### ✅ 正确示例
```fsharp
/// <summary>
/// Reads the entire content of a file at the specified path.
/// </summary>
/// <param name="path">Absolute or relative path to the target file.</param>
/// <returns>File content as string, or empty string if file not found.</returns>
let readFile (path: string) : string =
    // 实现...
```
#### ❌ 错误示例
```fsharp
/// <summary>
/// This function takes a path string as input and returns a string.
/// It reads the file content.
/// </summary>
/// <param name="path">The path.</param>
let readFile (path: string) : string =
    // 实现...
```
**关键差异**:
- 去除冗余描述（如"This function..."）
- 参数说明补充上下文（"Absolute or relative..."）
- 返回值说明边界情况
### 行内注释规则
**黄金法则**: 解释"为什么这样做"，而非"代码在做什么"
#### ✅ 正确：解释业务规则/设计决策
```fsharp
// Offset by 1 to convert from 0-based index to 1-based page number.
let pageNumber = index + 1
```
#### ❌ 错误：描述显而易见
```fsharp
// increment i by 1
let i = i + 1
```
### 特殊标记规范
```fsharp
// TODO: [描述未来计划，必须说明原因]
// Example: TODO: Replace this regex with a proper parser to handle nested brackets.
// HACK: [解释丑陋代码的必要性，说明外部限制]
// Example: HACK: Bypassing the strict type checker here because the external API returns dynamic JSON.
```
---
## 代码审查
### 重构执行流程（标准化步骤）
#### 步骤 1: 命名合规性检查
```bash
1. 扫描所有新增/修改文件，验证是否双词命名
2. 检查变量/函数命名，替换模糊词:
   - data → requestData, contentData
   - temp → buffer, cacheKey
   - process → parseOptions, renderTemplate
```
#### 步骤 2: 注释质量清洗
```bash
1. 删除所有废话注释（"set variable", "return result"）
2. 为所有公开 API 添加英语 XML 文档
3. 翻译非英语注释为标准技术英语
4. 验证文件头注释完整性
```
#### 步骤 3: 代码语义化改进
```fsharp
// F# 优先使用管道操作符
let result = 
    source
    |> parseContent
    |> validateData
    |> renderTemplate
// C# 优先使用 LINQ 和明确类型
var result = items
    .Where(item => item.IsValid)
    .Select(item => Transform(item))
    .ToList();
```
### 代码生成标准模板
```fsharp
// TemplateRenderer.fs
//
// Handles the compilation and rendering of template files using
// the configured template engine (e.g., Nunjucks).
//
// Dependencies: Zest.Engine.Domain, Zest.Engine.Config
namespace Zest.Engine.Template
open System
open Zest.Engine.Domain
/// <summary>
/// Compiles template strings into executable render functions.
/// </summary>
module TemplateCompiler =
    /// <summary>
    /// Parses the template source and caches the compiled result.
    /// Throws TemplateException if syntax is invalid.
    /// </summary>
    /// <param name="source">Raw template string content.</param>
    /// <param name="path">File path used for error reporting.</param>
    /// <returns>Compiled template ready for rendering.</returns>
    let compile (source: string) (path: string) : CompiledTemplate =
        // Normalize line endings to prevent cross-platform mismatch.
        let normalizedSource = source.Replace("\r\n", "\n")
        
        try
            // Actual compilation logic here...
            { Id = path; Render = (fun _ -> "") }
        with ex ->
            // Rethrow with contextual information for easier debugging.
            raise (TemplateException(path, ex.Message))
```
---
## Git规范
### 提交消息格式
```
<type>(<scope>): <summary>
<body>
```
### 类型定义表
| Type | 含义 | 典型场景 | 示例 |
|------|------|----------|------|
| `feat` | 新功能 | 添加新特性 | `feat(theme): add git source support` |
| `fix` | Bug 修复 | 修复缺陷 | `fix(cache): resolve TOCTOU race` |
| `refactor` | 重构 | 改进结构不改变行为 | `refactor(layout): extract merge logic` |
| `perf` | 性能优化 | 提升速度/内存 | `perf(build): parallelize asset copy` |
| `test` | 测试 | 添加/修改测试 | `test(infra): add dev server tests` |
| `docs` | 文档 | 更新说明 | `docs(themes): add theme guide` |
| `style` | 格式 | 注释/命名调整 | `style(engine): clean up notes` |
| `chore` | 杂务 | 依赖/构建变更 | `chore: bump xunit to 2.9.3` |
### 高质量提交示例
#### 功能添加
```
feat(theme): add local and git theme source support
- _themes/{name}/ provides file-override fallback for layouts, includes, and assets
- Git source clones to .zest/themes/{name}/ with depth-1 and single-branch for speed
- Theme _theme.zest.fsx executes before user _init.zest.fsx; filters can be overridden
```
#### Bug 修复
```
fix(cache): replace lock keyword with Monitor.Enter/Exit
The F# lock keyword caused indentation errors in BuildCache module when combined 
with mutable state. Switch to explicit Monitor calls to fix compilation while 
maintaining thread safety.
```
### 提交规则强制项
1. **单一职责**: 一个提交 = 一个逻辑变更
2. **编译通过**: 提交前必须 `dotnet build` 成功
3. **测试分离**: 实现代码和测试代码分不同提交（回归测试除外）
---
## 检查清单
### 代码提交前检查表
#### 命名检查
- [ ] 所有新增文件符合双词命名法
- [ ] 无模糊变量名（data, temp, result, process）
- [ ] 无禁止的缩写或单字命名
- [ ] 目录名表达完整概念
#### 注释检查
- [ ] 文件头注释完整（职责+依赖）
- [ ] 公开 API 有 XML 文档注释
- [ ] 无废话注释（"set variable", "return"）
- [ ] TODO/HACK 标记有明确说明
- [ ] 注释使用标准英语
#### 代码质量
- [ ] 无编译警告
- [ ] F# 代码优先使用管道操作符
- [ ] C# 代码优先使用 LINQ
- [ ] 无明显的性能问题
#### Git 规范
- [ ] 提交消息格式正确（type(scope): summary）
- [ ] 提交内容单一职责
- [ ] 不包含无关文件
---
## 附录：快速参考卡
### 命名决策树
```
需要命名？
├─ 是 → 是否文件/类/目录？
│       ├─ 是 → 双词 PascalCase（TemplateRenderer）
│       └─ 否 → 双词 camelCase（renderTemplate）
└─ 否 → 不适用
```
### 注释决策树
```
需要注释？
├─ 公开 API → 添加 XML 文档
├─ 复杂逻辑 → 行内注释解释"为什么"
├─ 文件头 → 添加模块摘要+依赖
└─ 显而易见 → 不需要注释
```