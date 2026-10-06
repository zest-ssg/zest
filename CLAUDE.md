# Zest SSG — Engineering Guidelines
**Scope**: This document governs AI agents and human maintainers working in the `zest-ssg/zest` repository.
**Purpose**: Keep the C#/F# architecture boundaries clean and the code simple, correct, and readable. Rules serve readers, not ceremony.
---
## 1. Philosophy
Every rule below exists to serve one goal: **a competent engineer should be able to open any file and understand it quickly.**
### 1.1 Principles
- **Readability first.** Code is read by humans far more often than it is written.
- **Simplicity over cleverness.** Direct implementation beats abstraction. Fewer layers beat more layers.
- **Minimal change.** Fix the problem at hand. Do not refactor unrelated code in passing.
- **Validate at boundaries, trust the interior.** Defend at the edges of the system; inside, trust types and call contracts.
- **No speculative design.** Do not introduce abstractions for requirements that do not exist yet.
### 1.2 Rule tiers
- **MUST** — always applies. Violations block merge: architecture boundaries, security, tests passing, reversible commits.
- **SHOULD** — the default. Deviate only with a clear, stated reason.
- **MAY** — context-dependent; use judgment.
### 1.3 Conflict resolution
When rules conflict, resolve in this order:
1. Correctness and security
2. Architecture boundaries
3. Readability
4. Consistency with surrounding code
5. Everything else
If readability conflicts with any stylistic rule, choose readability and note the reason in the PR or a code comment. If this document conflicts with a pattern already established in the codebase, follow the codebase and flag the conflict rather than silently diverging.
---
## 2. Architecture
### 2.1 Layers
| Layer | Project | Language | Responsibility |
| :--- | :--- | :--- | :--- |
| CLI, infrastructure, I/O, composition root | `Zest.App` | C# | Entry point, file system, processes, config loading |
| Site compilation, domain logic, pure functions | `Zest.Compiler` | F# | Template rendering, parsing, build pipeline, immutable data flow |
| Author-facing markup API | `Zest.Markup` | F# | HTML/ZCSS builders, SEO, Feed, page queries |
| Dependency-free primitives | `Zest.Core` | F# | Slugs, text metrics, date formatting |
### 2.2 Rules
- **MUST**: Dependencies point in one direction only. C# CLI types never leak into F# compiler code; C# never depends on F# internals.
- **MUST**: Cross-boundary data uses explicit DTOs, records, or interfaces. Never pass anonymous types or `dynamic` across a boundary.
- **MUST**: F# owns pure logic and domain models; C# owns I/O, processes, configuration, and composition.
- **MUST**: Cross-layer calls go through explicit interfaces. No reverse dependencies.
- **MUST**: Zest recognises exactly three special files, all at the project root: `_config.toml` (configuration), `_prebuild.fsx` (before the build) and `_finalize.fsx` (after the site is written). None of them is routed. Adding a fourth reserved name, or an alias for an existing one, is an escalated change (§8.3).
**Why**: F# excels at expressing domain logic as immutable pipelines; C# excels at interop and I/O. Blurring this split produces the worst of both languages and makes each layer harder to test in isolation.
Architectural boundary changes are **escalated changes** — see §8.3.
---
## 3. Naming
**Goal**: A name expresses intent. A reader should never have to guess.
### 3.1 Conventions
- **MUST**: Types, files, directories use `PascalCase`; methods, variables, parameters use `camelCase`.
- **MUST**: Use domain vocabulary. No meaningless abbreviations.
- **SHOULD**: A single clear word is fine — `Parser`, `Router`, `Compiler`, `Renderer`. Two-word names like `TemplateParser` are equally fine. Context decides.
- **SHOULD**: Use specific verbs: `parseContent`, `fetchMetadata`, `compileStyle`. Use `get`/`handle`/`process`/`run` only when the meaning is unambiguous.
### 3.2 Banned names
Avoid vague catch-alls: `Utils`, `Helper`, `Manager`, `Common`, `Misc`, `Data`.
Avoid arbitrary abbreviations: `Tmp`, `Cfg`, `Req`, `Mgr`, `Svc`, `Impl`.
**Why**: Names like `Utils` and `Helper` become gravity wells — every unrelated function eventually lands there, and the name stops telling you anything. A specific name forces a specific responsibility.
### 3.3 Renaming
- Renaming requires a reason. **If you cannot state the reason in one sentence, do not rename.**
- Small-scope renames may proceed directly; renames spanning more than three files are escalated (§8.3).
- **MUST**: Pure renames live in their own commit, never mixed with logic changes.
- **SHOULD**: Do not rename existing, already-clear names just to match style.
---
## 4. Comments & Documentation
**Goal**: Comments carry the context that code cannot. Documentation carries the contract that signatures cannot.
### 4.1 Comments
- Comments explain **why**, not what. If a comment restates the code, delete it.
- **MUST**: Comments stay current. If the code changes, the comment changes or dies.
- **SHOULD**: Comments are concise and complete. No filler words (`simply`, `just`, `basically`, `obviously`).
### 4.2 File headers
- **MAY**: Complex, stateful, or cross-boundary files carry a short header: responsibility, key dependencies, critical invariants.
- Simple files do not need decorative headers.
### 4.3 Public API documentation
- **SHOULD**: Document public packages, cross-module APIs, and non-obvious behavior. Cover: intent, contract, failure modes, thread-safety requirements.
- Simple getters and obvious members do not need full XML docs. Private members usually need none.
- Write documentation where a caller could otherwise misuse the API — not everywhere by default.
### 4.4 Markers
```csharp
// TODO: [goal]. [trigger condition for resolving it].
// HACK: [why this is necessary]. [external constraint forcing it].
// NOTE: [non-obvious context a reader would need].
// LEGAL: [legally required text].
```
### 4.5 Language
- **MUST**: Comments and commit messages are in English (this repository's default).
---
## 5. Errors & Validation
**Core rule: validate at boundaries, trust the interior.**
### 5.1 Validate at boundaries
- **SHOULD**: Check user input, config files, file system results, network responses, and external API payloads at the point they enter the system.
- **MUST**: Failure is explicit. No silent degradation unless the product explicitly requires it.
### 5.2 Trust the interior
- Internal functions trust their callers. Do not re-check nulls, types, or state at every layer.
- **Do not** write defensive branches for states that cannot occur.
- **Do not** wrap every layer in `try/catch`. Catch only exceptions you can actually handle; let the rest propagate.
### 5.3 Expressing constraints with types
- C#: nullable reference types, `record`, `enum`.
- F#: `Option` over `null`; `Result` at boundaries.
**Why**: Duplicated checks bury the main logic under noise and hide real design problems. A type-level constraint (`Option<T>`, a non-null record) is checked by the compiler on every call, forever, for free.
### 5.4 Error messages
- **SHOULD**: Error messages carry enough context (what was attempted, what failed, relevant values) to locate the problem without a debugger.
---
## 6. Code Style
### 6.1 C#
- Clarity first. LINQ is welcome where it reads naturally, but is never mandatory.
- **SHOULD**: Immutable data uses `record`.
- **MUST**: Async methods end with `Async` and return `Task` or `ValueTask`.
- **MUST NOT**: Use `dynamic`; pass anonymous types across boundaries; accept pointless `object` parameters.
### 6.2 F#
- **SHOULD**: Pipelines (`|>`) over deeply nested calls.
- **SHOULD**: `Option` over `null`; `Result` at boundaries.
- **MUST**: `mutable` appears only in measured hot paths, with a comment explaining why.
- **SHOULD**: Modules stay small and single-purpose. Prefer pure functions and immutable data.
### 6.3 Shared
- **SHOULD**: Functions stay short. Split when they grow — but there is **no hard line count**. A 60-line function that reads linearly can beat five fragmented helpers.
- **SHOULD**: Many parameters → introduce a configuration record. Deep nesting → early returns or extracted functions.
- **MUST**: No dead code, commented-out code, unused parameters, or unused usings.
- **SHOULD**: No abstraction before two real use cases exist. Simple and direct beats clever.
- **MUST NOT**: Delete tests or comments to make checks pass.
---
## 7. Testing
- **MUST**: New behavior comes with tests. Bug fixes come with a regression test first when practical.
- **MUST**: Refactoring keeps all tests green.
- Test naming: `[Subject]_[Condition]_[Expectation]` — e.g. `renderTemplate_InvalidPath_ReturnsEmpty`.
- Test files mirror source structure under `tests/`.
- **SHOULD**: Prefer fakes and in-memory implementations over mocking concrete types.
- **SHOULD**: Cover critical paths and boundaries. Do not chase a formal coverage number.
**Gates**: `dotnet build` introduces no new warnings. `dotnet test` passes. Both **MUST** hold before delivery.
---
## 8. Change Protocol
### 8.1 Workflow
1. Read the code and existing patterns first.
2. State scope, risk, and plan — briefly.
3. Change in small steps; each step builds, tests, and reverts cleanly.
4. Never mix unrelated refactoring into a feature fix or bug fix.
5. Run build and tests before delivering.
### 8.2 Commit rules
- **MUST**: One commit does one thing. Pure renames get their own commit.
- Commit messages read like you're explaining the change to a colleague: natural sentences, capitalized first letter, ending period.
- Subject line ≤ 72 characters. Body optional — use it for reasons and side effects.
- No conventional-commit prefixes (`feat:`, `fix:`) unless the repo has already converged on them; consistency wins.
```
Fix race condition when writing cache files.
Switch from lock to Monitor.Enter to avoid a compiler error
in F#. The cache stays thread-safe without breaking the build.
```
### 8.3 Escalated changes — confirm before acting
The following require explicit confirmation from the maintainer before execution:
| Change | Threshold |
| :--- | :--- |
| Renaming across files | More than 3 files |
| Public API breaking change | Any |
| Architecture boundary change | Any |
| Deleting or rewriting existing comments in bulk | Any |
| New dependency | Any |
| Large-scale refactor / rewrite | Any |
Everything not on this list: proceed with the normal workflow, deliver, and report.
---
## 9. AI Agent Behavior
### 9.1 Operating rules
- Read code and context before writing anything.
- Follow the patterns already present in the file/module; do not introduce a competing paradigm.
- Make the minimal change that solves the stated problem.
- **When uncertain, ask. Do not guess.** Asking costs a minute; a wrong guess costs a review cycle.
- When alternatives exist, offer up to two options with a recommendation and the reason for it.
### 9.2 Hard limits — never do these
- Never delete tests or comments to make a check pass.
- Never skip build/test verification.
- Never perform an escalated change (§8.3) without confirmation.
- Never introduce a paradigm that conflicts with the existing codebase style.
### 9.3 Delivery format
Every delivered change includes:
1. **Summary** — what changed and why.
2. **Impact** — affected modules and callers.
3. **Verification** — build / test / lint results.
4. **Suggested commit message** — ready to use.
---
## 10. Pre-Commit Checklist
- [ ] Names are clear — no vague catch-alls, no arbitrary abbreviations.
- [ ] Comments explain why; none restate code; none are stale.
- [ ] Complex files and public APIs carry necessary documentation.
- [ ] Architecture boundaries intact; cross-boundary data uses DTOs.
- [ ] Validation at boundaries only; no defensive noise inside.
- [ ] No dead code, duplicate checks, or speculative abstractions.
- [ ] New behavior tested; all tests pass after refactoring.
- [ ] `dotnet build` — no new warnings. `dotnet test` — green.
- [ ] Commit message is natural, clear, single-scope.
- [ ] No unrelated refactoring mixed in.
---
## 11. Quick Reference
| Situation | Rule |
| :--- | :--- |
| Naming | Clarity over form. `Parser` is as good as `TemplateParser`. |
| Vague names | Banned: `Utils`, `Helper`, `Manager`, `Common`, `Tmp`, `Mgr`. |
| Comments | Why, not what. Concise. Always current. |
| Public API docs | Where a caller could misuse it — not everywhere. |
| Errors | Validate at boundaries, trust the interior. |
| Defensive code | Use types (`Option`, `Result`, nullable) instead of layers of checks. |
| C# | Clarity first, `record`, `Async` suffix, no `dynamic`. |
| F# | Pipelines, `Option`/`Result`, `mutable` only in measured hot paths. |
| Function length | No hard limit; split when it stops reading linearly. |
| Abstraction | Only after two real use cases. |
| Refactoring | Small steps, each verifiable. |
| Renames | One-sentence reason or don't. Pure renames: own commit. |
| Commits | Natural sentence, capitalized, period, ≤ 72 chars subject. |
| Bigger than 3 files / API break / new dep / bulk comment deletion | Confirm first (§8.3). |
| Uncertain | Ask. Don't guess. |
**One-line principle**: Keep code simple, clean, and reliable. Rules serve readers, not form.