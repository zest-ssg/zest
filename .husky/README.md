# .husky

Git hooks for the Zest repository. They are plain POSIX `sh` scripts wired up
through `core.hooksPath` — no Node, no npm install step.

## Enable

```sh
sh .husky/install.sh      # macOS / Linux / Git Bash
pwsh .husky/install.ps1   # Windows
```

That runs `git config core.hooksPath .husky` once per clone.

## Hooks

| Hook          | What it does                                                            |
|---------------|-------------------------------------------------------------------------|
| `pre-commit`  | `dotnet build zest.sln` — blocks commits that do not compile.            |
| `commit-msg`  | Rejects subject lines that break the style in `CLAUDE.md` section 7:     |
|               | capitalized, ends with a period, under 72 characters, no `feat:` prefix. |

`pre-commit` intentionally runs the build only. A full `dotnet test` on every
commit is slow enough that `--no-verify` becomes a habit; testing stays in CI
and in the developer's hands.

## Bypassing

`git commit --no-verify` skips both hooks. Use it for work-in-progress
branches, not for anything heading to `main`.
