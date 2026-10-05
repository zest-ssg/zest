# .claude

Claude Code configuration for the Zest repository.

## Files

| File            | Purpose                                                        |
|-----------------|----------------------------------------------------------------|
| `settings.json` | Shared, committed settings: tool permissions for this repo.     |
| `settings.local.json` | Per-developer overrides. **Not committed** (git-ignored).  |

## Project contract

The binding engineering contract lives in [`../CLAUDE.md`](../CLAUDE.md).
Claude Code loads it automatically from the repository root; it is not
duplicated here so the two copies cannot drift apart. Before changing code,
read it — it fixes the C#/F# architecture boundary, the two-word naming rule,
comment quality, and the commit-message style.

## Conventions enforced here

- Commands allowed without prompting: `dotnet build`, `dotnet test`,
  `dotnet format`, and read-only `git` inspection.
- Commands always refused: force push, `git reset --hard`, `git clean -fd`,
  and recursive deletes.
- `.husky/commit-msg` rejects commits whose subject breaks the house style
  defined in `CLAUDE.md` section 7.
