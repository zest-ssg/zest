# Zest SSG Engineering Contract
**Version**: 3.1
**Scope**: A binding contract for both AI Agents and human maintainers working on the `zest-ssg/zest` repository (.NET 10+).
**Goal**: Preserve the C#/F# architecture boundary while producing code that is strictly conventional, grammatically precise, and genuinely readable.

---

## 0. Prime Directive: Readability Serves the Reader

Every rule below exists to reduce comprehension cost. When a rule fights readability, readability wins, and the deviation must be justified in a single comment.

- Consistency is a means, not an end.
- Never rename something that is already clear merely to satisfy a pattern.
- Test: can a new maintainer understand this unit within 30 seconds? If not, fix clarity before enforcing style.

---

## 1. Architecture Boundary (Non-Negotiable)

| Layer | Language | Responsibility |
| :--- | :--- | :--- |
| CLI, infrastructure, I/O, composition root | C# | User-facing entry points, filesystem, processes, configuration loading |
| Engine, DSL, domain logic, pure functions | F# | Template rendering, parsing, build pipeline, immutable data flow |

Rules:
- No cross-layer calls. F# must not reference C# CLI types, and C# must not reference F# engine internals.
- Cross-boundary data uses explicit DTOs. Never use anonymous types or `dynamic` across the boundary.
- Prefer LINQ in C#. Prefer `|>`, `List`, and `Seq` in F#. Avoid `for` and `while` unless performance evidence demands otherwise.

---

## 2. Naming Convention

### 2.1 Canonical Form: Two Words
- File, directory, type: `PascalCase` → `TemplateRenderer`, `PageBuilder`
- Method, variable: `camelCase` → `renderTemplate`, `cacheBuffer`

### 2.2 Semantic Structure
- First word: the domain object (`Template`, `Config`, `Build`, `Style`).
- Second word: the action or role (`Renderer`, `Parser`, `Compiler`, `Loader`).

### 2.3 Permitted Exceptions
Only **framework-mandated names** may violate the two-word rule. Every other exception is rejected.

Allowed:
- `Program.fs`, `Startup.cs`, `App.razor`, `Main`, `Index` — dictated by .NET, ASP.NET, or build tooling.
- Language-mandated constructs: `module`, `namespace` keywords aside, F# `Program` entry module.

Rejected:
- Community-convenient single words (`Router`, `Lexer`, `Parser`) are **not** exceptions. Rename them.
  - `Router` → `RequestRouter`
  - `Lexer` → `TokenScanner`
  - `Parser` → `TemplateParser`
- "It's already used elsewhere" is not an exception. Fix the usage.

Each permitted exception **must** carry a one-line justification:

```csharp
// Framework-mandated: ASP.NET Core requires the Startup class name.
public sealed class Startup { ... }
```

### 2.4 Forbidden Patterns
- Vague nouns: `Utils`, `Helper`, `Manager`, `Data`, `Common`, `Misc`, `Core`.
- Casual abbreviations: `Tmp`, `Cfg`, `Req`, `Mgr`, `Svc`, `Impl`.
- Vague verbs: `process`, `handle`, `get`, `do`, `run`. Use specific verbs: `parseContent`, `fetchMetadata`, `compileStyle`.
- Three-word-or-longer combinations: `TemplateRenderEngine` → `TemplateRenderer`.

### 2.5 Rename Discipline
- **New code**: fully compliant.
- **Touched files**: rename only when the current name is genuinely harmful. A clear legacy name beats an awkward new one.
- **Pure renames**: one dedicated commit. Never mixed with logic changes.
- **No rename without a reason.** If you cannot articulate the reason in one sentence, do not rename.

### 2.6 Reference Table

| Context | Forbidden | Required | Framework Exception |
| :--- | :--- | :--- | :--- |
| File | `Render.fs` | `TemplateRenderer.fs` | `Program.fs` |
| Type | `Builder.cs` | `PageBuilder.cs` | `Startup`, `Main` |
| Directory | `Zcss/` | `StyleCompiler/` | `Properties/` |
| Variable | `temp` | `cacheBuffer` | `i` (short loop index) |
| Module | `Utils` | `PathResolver` | — |

---

## 3. Documentation and Comments

### 3.1 File Header
Every non-trivial file must state its responsibility, dependencies, and any non-obvious invariant. Do not write a decorative one-liner.

```fsharp
// TemplateRenderer.fs
//
// Compiles Nunjucks templates and caches parsed results in memory.
// Caching prevents redundant disk reads on every page render.
//
// Invariant: cache keys are absolute, normalized paths.
// Callers must pass paths produced by PathResolver.
//
// Dependencies: Zest.Engine.Domain, System.IO
```

### 3.2 Public API: XML Documentation
Required for every public type and member in both C# and F#.

Cover:
- **Intent**: what the caller achieves, not how it is implemented.
- **Contract**: preconditions, postconditions, invariants.
- **Failure modes**: what happens on invalid input, and why that behavior was chosen.
- **Thread safety**: state it explicitly when relevant.

```fsharp
/// <summary>
/// Renders a template with the supplied context data.
/// Returns an empty string for invalid paths so a single
/// broken template cannot fail the whole build pipeline.
/// </summary>
/// <param name="templatePath">Absolute, normalized path to the .njk file.</param>
/// <param name="context">Data bag used for variable interpolation.</param>
/// <returns>The rendered output, or an empty string on path failure.</returns>
let renderTemplate templatePath context = ...
```

Private members do not require XML documentation. Add a comment only when the code is not self-evident.

### 3.3 Inline Comments: Explain *Why*, Never *What*
- ✅ `// Offset by 1 to match the 1-based page numbers shown in the UI.`
- ❌ `// Add 1.`
- ✅ `// HACK: nunjucks caches by relative path; keying on absolute path survives cwd changes.`
- ❌ `// Loop through templates.`

Rule of thumb: if the comment restates the code, delete it.

### 3.4 Comment Quality Standard
High-quality comments share these traits:

1. **They answer a question the code cannot.**
   - Why this approach over an obvious alternative.
   - Why this edge case matters.
   - Why a surprising value was chosen.

2. **They are grammatically complete sentences.**
   - Capitalize the first word.
   - End with a period.
   - Use `that` and `which` correctly.
   - No sentence fragments, no telegraphic style.

3. **They are concise.**
   - One idea per comment.
   - If a comment needs a paragraph, the code likely needs refactoring.

4. **They age well.**
   - Reference stable facts (specs, RFCs, tickets), not transient state ("currently", "for now").
   - If the code changes, the comment must change with it. A stale comment is worse than no comment.

5. **They avoid hedging and filler.**
   - Ban `simply`, `just`, `basically`, `obviously`, `of course`, `note that`.
   - Ban jokes, sarcasm, and personal remarks.

6. **They use correct technical English.**
   - Prefer active voice: "the parser rejects X" over "X is rejected by the parser".
   - Use present tense for behavior: "Returns an empty string" not "Will return an empty string".
   - Define domain terms on first use.

**Examples:**

Bad:
```csharp
// increment counter because we need to count
counter++;
```

Good:
```csharp
// Track parsed files to detect duplicate includes across templates.
counter++;
```

Bad:
```fsharp
// loop and check
for page in pages do
    validate page
```

Good:
```fsharp
// Fail fast on the first invalid page so the build log points to a single source.
for page in pages do
    validate page
```

### 3.5 Special Markers
- `// TODO: [Goal]. [Trigger or condition].`
  Example: `// TODO: Replace regex with a parser once nested brackets are supported.`
- `// HACK: [Why necessary]. [External constraint].`
  Example: `// HACK: Legacy API returns null instead of an empty list; guard at the boundary.`
- `// NOTE: [Non-obvious context].`
  Example: `// NOTE: Order matters here; later overrides depend on earlier defaults.`

### 3.6 Language
- All comments, documentation, and commit messages are written in English.
- Non-English text is permitted only when legally required, prefixed with `// LEGAL:`.

---

## 4. Refactoring Workflow

AI Agents must proceed in explicit steps and **wait for confirmation between them**:

1. **Scan and report**: list naming, comment, and structural issues without editing.
2. **Scope**: state exactly which files are in scope. Do not touch anything else.
3. **Propose**: outline renames, splits, and documentation additions, with risks.
4. **Execute incrementally**: one class of change per commit (rename / comment / structure) so each is reviewable and revertible.
5. **Verify**: `dotnet build` passes with zero warnings; `dotnet test` is fully green.

Forbidden:
- Repo-wide bulk renames without confirmation.
- Unrelated refactors smuggled into a functional fix.
- Deleting existing comments without justification.

---

## 5. Code Style

### C#
- Prefer LINQ over explicit loops unless profiling justifies otherwise.
- Use `record` for immutable data.
- Suffix asynchronous methods with `Async` and return `Task` or `ValueTask`.
- Avoid `dynamic`. Avoid `object` parameters unless the boundary is documented.

### F#
- Prefer pipelines (`|>`) over nested calls.
- Prefer `Option` over `null`. Use `Result` across boundaries.
- Keep modules small and single-purpose.
- Avoid `mutable` except on measured hot paths, and document the reason.

### Shared
- Functions stay under 40 lines. Split when longer.
- Parameters stay under 5. Beyond that, introduce a configuration record.
- Nesting stays under 4 levels. Use early returns or extracted functions.

---

## 6. Testing

- New behavior requires tests. Refactors must keep the suite green.
- Test naming: `[Subject]_[Condition]_[Expectation]`.
  Example: `renderTemplate_InvalidPath_ReturnsEmpty`.
- Test files mirror the source layout under `tests/`.
- Prefer fakes or in-memory implementations over mocks of concrete types.

---

## 7. Git Commit Convention

### 7.1 Style
Write commit messages in natural English, as if briefing a colleague.

- Capitalize the first word.
- End with a period.
- Do not use prefixes: no `feat:`, `fix:`, `chore:`, `refactor:`, `style:`.
- Keep the subject line under 72 characters.

### 7.2 Reference Table

| Forbidden | Required |
| :--- | :--- |
| `feat(theme): add git source` | `Theme supports Git sources now.` |
| `fix(cache): resolve TOCTOU race` | `Fix race condition when writing cache files.` |
| `refactor(core): clean utils` | `Split the monolithic Utils module into focused resolvers.` |
| `chore(deps): bump xunit` | `Update xUnit to 2.9.3.` |
| `style: format` | `Format code to match style guidelines.` |

### 7.3 Body
Optional. Separate from the subject by a blank line. Explain the reason and any side effects.

```
Fix race condition when writing cache files.

Switch from 'lock' to 'Monitor.Enter' to avoid a compiler error
in F#. The cache stays thread-safe without breaking the build.
```

### 7.4 Granularity
- One concern per commit.
- Pure renames stand alone.
- Large refactors are split into reviewable steps.

---

## 8. AI Agent Behavior

The agent collaborates; it does not rewrite the repository unsupervised.

### 8.1 Require Confirmation Before
- Renaming across more than three files.
- Deleting or rewriting existing comments.
- Changing a public API signature.
- Adding a new dependency.
- Touching architecture-boundary code.

### 8.2 When Uncertain
- Ask rather than guess.
- Offer two options with a recommendation and rationale.
- Preserve the status quo and mark it: `// TODO: [Issue]. [Suggested action].`

### 8.3 Every Delivery Includes
1. Change summary (what and why).
2. Impact scope (affected modules and callers).
3. Verification results (build, test, lint).
4. Proposed commit message following Section 7.

### 8.4 Hard Limits
- No repository-wide renames without confirmation.
- No deleting tests or comments to make checks pass.
- No introducing paradigms that conflict with existing style.
- No skipping build and test verification.

---

## 9. Pre-Commit Checklist

- [ ] **Naming**: new and touched identifiers follow the two-word rule, or carry a framework-exception comment.
- [ ] **Comments**: no restating comments; file headers complete; public APIs documented.
- [ ] **Comment quality**: complete sentences, capitalized, punctuated, no filler, correct grammar.
- [ ] **Architecture**: C#/F# boundary intact; cross-layer data uses DTOs.
- [ ] **Style**: F# uses pipelines, C# uses LINQ; no deep nesting; functions stay short.
- [ ] **Tests**: new behavior covered; refactors green.
- [ ] **Build**: `dotnet build` reports zero warnings; `dotnet test` passes.
- [ ] **Commit**: natural sentence, capitalized, ends with a period, no prefix.
- [ ] **Scope**: no unrelated refactors bundled in.

---

## 10. Quick Reference

| Scenario | Rule |
| :--- | :--- |
| New file name | Two words, PascalCase. |
| Existing clear single word | Rename unless framework-mandated. |
| Framework exception | Allowed only for `Program`, `Startup`, `Main`, `Index`, and equivalents. Justify in one line. |
| Public API | XML documentation covering intent, contract, and failure modes. |
| Inline comment | Explains *why*. Complete sentence. No filler. |
| Comment grammar | Capitalize, punctuate, use active voice, present tense. |
| F# control flow | Pipelines first. |
| C# control flow | LINQ first. |
| Rename | Independent commit, minimal scope. |
| Commit message | Natural English, ends with a period, no prefix. |
| Uncertainty | Ask; do not guess. |

---

**One-line principle**: Write as if handing the code to your future self — rules exist so humans can read, not so machines can look tidy.