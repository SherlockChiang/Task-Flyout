# Harness Workflow

This workflow keeps long-cycle optimization work repeatable. Run it before and after a batch when you need a quick confidence check that the app still builds, tests pass, and Git has no content diff.

## Command

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-harness.ps1
```

Default behavior:

- Refreshes Git index metadata.
- Fails if there is a real unstaged or staged content diff.
- Builds `Task_Flyout.csproj` with `Debug` and `x64`.
- Runs `Tests\Task_Flyout.Tests\Task_Flyout.Tests.csproj`.
- Writes logs under `.harness\runs\yyyyMMdd-HHmmss\`.

Useful options:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-harness.ps1 -Configuration Debug -Platform x64
powershell -ExecutionPolicy Bypass -File .\scripts\run-harness.ps1 -AllowDirty
powershell -ExecutionPolicy Bypass -File .\scripts\run-harness.ps1 -SkipBuild
powershell -ExecutionPolicy Bypass -File .\scripts\run-harness.ps1 -SkipTests
```

Use `-AllowDirty` only when deliberately checking a work-in-progress diff before committing.

## Long-Cycle Loop

1. Start with `git status --short` and the harness passing.
2. Pick one small roadmap item.
3. Make the smallest code or documentation change that completes it.
4. Run the harness.
5. Commit only files for that item.
6. Run the harness again if the commit touched build or test behavior.
7. Update `docs/optimization-roadmap.md` when an item changes status.

## Current Manual Gaps

- OAuth least-privilege consent paths still require interactive Google/Microsoft accounts.
- DPAPI, PasswordVault, and Azure Identity token-cache cleanup need a disposable Windows user profile or VM.
- UI-only checks such as keyboard navigation, high contrast, scaling, and screen reader labels remain manual.
