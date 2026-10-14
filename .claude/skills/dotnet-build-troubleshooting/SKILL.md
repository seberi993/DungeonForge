---
name: dotnet-build-troubleshooting
description: Diagnose flaky or failing `dotnet build` / `dotnet test` in DungeonForge (MSB4018 static web assets, MSB3026/MSB3027 file locks, warnings that block the zero-warnings rule). Use before cleaning, deleting bin/obj, or building other commits.
---

# .NET Build Troubleshooting (DungeonForge)

The solution uses `TreatWarningsAsErrors=true`, so "zero warnings" is a hard requirement. Work through these in order; each step is cheap and non-destructive. Stop at the first one that fixes it.

## 1. File-lock warnings (MSB3026 / MSB3027 / MSB3021)
Cause: a running `DungeonForge.Api.exe` locks `src/DungeonForge.Api/bin/Debug/net10.0/DungeonForge.Api.exe`.
- Find it: `Get-Process DungeonForge.Api`. Inspect before acting (PID, start time, parent).
- Do NOT kill it unless the user confirms that specific PID. It may be their dev server or IDE launch. A new PID after an earlier one exited is a different process; earlier approval does not carry over.
- Never start the API yourself with `dotnet run`/`dotnet watch` (a hook blocks them).

## 2. `MSB4018 ApplyCompressionNegotiation: An item with the same key has already been added` (DungeonForge.Web)
Cause: stale state in the persistent MSBuild / compiler servers, not the code. It reproduces on every plain `dotnet build` while `-m:1` or a lone Web build succeeds, which looks like a parallel-build race.
**Fix first: `dotnet build-server shutdown`, then `dotnet build`.**

What did NOT help (don't repeat these first): `dotnet clean` on the Web project, deleting every `bin`/`obj`, building from a different path casing (`claude` vs `Claude`), and building other commits in worktrees. A fresh worktree builds cleanly because it starts a clean build context. That means the commit is fine, not that the working tree is wrong.

Fallback if it still fails after the shutdown: `dotnet build -m:1` gives a clean serial build.

## 3. Verify
- `dotnet build` twice in a row: 0 warnings, 0 errors both times (the second run catches incremental-build problems).
- `dotnet test`: report the passed/failed counts for both test projects.
- Report outcomes faithfully. Do not claim a clean build from a Release run or from `-m:1` alone.

## Rules
- Diagnose before deleting. `bin`/`obj` removal and process kills need the user's OK.
- Don't go to `git worktree` bisecting until `dotnet build-server shutdown` has been tried.
- Remove any temp worktree you create (`git worktree remove --force`) and confirm `git worktree list` is clean.
