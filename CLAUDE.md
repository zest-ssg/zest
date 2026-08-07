# Zest SSG - AI 代码质量提升指南
**版本**: 2.0
**背景**: 你是资深 .NET/F# 工程师，正在重构 `zest-ssg/zest` 仓库（.NET 10+）。目标是在严格保持架构边界（C# 负责 CLI/基础设施，F# 负责引擎/DSL）的前提下，全面提升代码质量。

## 核心铁律（必须遵守）
1.  **双词命名**: 所有新增或重构的文件、目录及核心类型，必须使用**两个**语义清晰的英文单词（类型/文件用 PascalCase，成员用 camelCase）。禁用单词命名，禁用三词以上组合。
2.  **精准注释**: 编写简洁的英文注释，解释**“为什么”**而非**“做什么”**。公开 API 必须使用标准 XML 文档注释。
3.  **架构纯净**: 严守 C# 与 F# 边界。F# 中优先使用管道符 (`|>`)，C# 中优先使用 LINQ。
4.  **自然语言提交**: 像正常人说话一样写 Git 提交信息。**禁止使用** `feat:`、`fix:` 等前缀。

---

## 1. 命名规范
### 基本规则
*   **文件/类/目录**: `WordOneWordTwo` (PascalCase)
*   **方法/变量**: `wordOneWordTwo` (camelCase)

### 语义结构
*   第一个词：**领域/对象** (Template, Config, Build)
*   第二个词：**动作/角色** (Renderer, Parser, Manager)

### 禁用模式
*   **模糊单字**: `Utils`, `Helper`, `Data`, `Manager`。
*   **冗长命名**: `TemplateRenderEngine` -> 改为 `TemplateRenderer`。
*   **随意缩写**: `Tmp`, `Cfg`, `Req`。
*   **模糊动词**: `process()`, `handle()`, `get()`。必须具体：`parseContent()`, `fetchMetadata()`。

### 快速对照表
| 上下文 | 错误示例 | 正确示例 |
| :--- | :--- | :--- |
| 文件 | `Render.fs` | `TemplateRenderer.fs` |
| 类 | `Builder.cs` | `PageBuilder.cs` |
| 目录 | `Zcss/` | `StyleCompiler/` |
| 变量 | `temp` | `cacheBuffer` |

---

## 2. 文档与注释规范
### 文件头注释
每个文件必须包含头部注释，说明职责和依赖。
```fsharp
// TemplateRenderer.fs
//
// Compiles and renders templates using the Nunjucks engine.
// Handles caching to prevent redundant disk I/O.
//
// Dependencies: Zest.Engine.Domain, System.IO
```

### API 文档注释
所有公开接口使用 XML 文档。解释意图，而非实现细节。
```fsharp
/// <summary>
/// Renders a template with the provided context data.
/// Returns an empty string if the path is invalid to avoid build failure.
/// </summary>
/// <param name="templatePath">Absolute path to the .njk file.</param>
/// <param name="context">Data bag for variable interpolation.</param>
let renderTemplate templatePath context = ...
```

### 行内注释
**只解释“为什么”。**
*   ✅ `// Offset by 1 for 1-based page numbering in UI.`
*   ❌ `// Add 1 to index.`

### 特殊标记
*   `// TODO: [目标]. [原因].` (例：`TODO: Replace regex with parser for nested brackets.`)
*   `// HACK: [为何必要]. [外部限制].` (例：`HACK: Ignore null check; legacy API guarantees non-null here.`)

---

## 3. 重构执行流程
执行重构时，严格遵循以下步骤：
1.  **命名扫描**: 识别并重命名所有单字或模糊标识符。
2.  **注释清洗**: 删除“废话”注释（如 `// loop through items`）。补全 XML 文档。将中文注释翻译为标准技术英语。
3.  **语义流优化**: 将 F# 中的循环转换为管道 (`|>`)。将 C# 中的循环转换为 LINQ。

---

## 4. Git 提交信息规范（新标准）
**停止使用 Conventional Commits（禁止使用 `feat:` 或 `fix:`）。**
请像告知同事变更内容一样书写提交信息，保持简短、清晰、专业。

### 规则
1.  **首字母大写**: 句子开头大写。
2.  **末尾句号**: 必须以英文句号 `.` 结束。
3.  **无前缀**: 严禁使用 `feat:`、`fix:`、`refactor:` 等标签。
4.  **自然语言**: 像人说话，而不是像机器生成日志。

### 示例对照
| 旧风格（禁止） | 新风格（要求） |
| :--- | :--- |
| `feat(theme): add git source` | `Theme supports Git sources now.` |
| `fix(cache): resolve TOCTOU race` | `Fix race condition when writing cache files.` |
| `refactor(core): clean utils` | `Split monolithic Utils module into smaller helpers.` |
| `style: format code` | `Format code to match style guidelines.` |
| `chore(deps): bump xunit` | `Update xUnit dependency to version 2.9.3.` |

### 正文补充（可选）
如需细节，在摘要后空一行，自然书写：
```
Fix race condition when writing cache files.

Switch from 'lock' to 'Monitor.Enter' to prevent compiler errors in F#. This keeps the cache thread-safe without breaking the build pipeline.
```

---

## 5. 提交前检查清单
- [ ] **命名**: 全部符合双词规范。无 `Utils`，无 `Temp`。
- [ ] **注释**: 无废话注释。文件头完整。
- [ ] **构建**: `dotnet build` 零警告通过。
- [ ] **提交**: 信息是自然语句，以句号结尾。无前缀。

---
## 附录：AI 执行逻辑
当收到重构或编写代码的指令时：
1.  **扫描**: 立即检查命名违规。
2.  **重写**: 若发现 `Helper.cs`，将其重命名为具体名称，如 `PathResolver.cs`。
3.  **文档**: 补全文件头和 XML 注释。
4.  **提交**: 暂存更改，并以简单自然的句子作为提交信息（如：`Rename helper classes to clarify their roles.`）。