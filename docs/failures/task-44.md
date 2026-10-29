# Task 44 — Build/test toolchain unavailable

**Current disposition:** Historical toolchain block resolved by the owner-authorized SDK 10.0.403/project recovery in [Task 45](../review/TASK_45_PROJECT_RESTORE.md): restore/build and full suite 290/290 pass. The initial commit identity failure is also resolved: the owner authorized their established identity per invocation, without changing persistent Git configuration. Build recovery is `659b58f`; requirements/Proposed ADR records are committed separately. Original errors below are historical evidence.

**Date:** 2026-10-29. **Checkout:** `C:\Users\swigg\OneDrive\Documents\Codex\PC Steward\pc-steward`. **Branch:** `phase/5-v2-planning`.

The accepted requirement/specification documentation was written before a compatible toolchain was available. Required build/test commands could not start at that time, so the repository's commit gate was unmet. The historical commands and errors below are retained; the current disposition above and Task 45 recovery record describe their resolution.

## Commands and exact errors

Attempt 1 (canonical repository build):

```powershell
pwsh -NoProfile -File .\scripts\Build.ps1
```

```text
pwsh : The term 'pwsh' is not recognized as the name of a cmdlet, function, script file, or operable program. Check
the spelling of the name, or if a path was included, verify that the path is correct and try again.
FullyQualifiedErrorId : CommandNotFoundException
```

Attempt 2 (installed Windows PowerShell running the same repository scripts):

```powershell
& powershell.exe -NoProfile -File .\scripts\Build.ps1
& powershell.exe -NoProfile -File .\scripts\Test.ps1
```

Build error (exit `-2147450735`):

```text
The command could not be loaded, possibly because:
  * You intended to execute a .NET application:
      The application 'build' does not exist.
  * You intended to execute a .NET SDK command:
      A compatible .NET SDK was not found.

Requested SDK version: 10.0.302
global.json file: C:\Users\swigg\OneDrive\Documents\Codex\PC Steward\pc-steward\global.json

Installed SDKs:
No .NET SDKs were found.
```

Test error was identical except `The application 'test' does not exist.`; exit was also `-2147450735`. Both commands stopped before compilation or test discovery. Those initial attempts were not failing product tests or a passing build and did not refresh Task 42's historical evidence.

## Original Task 44 changed files

- `docs/product/PC_STEWARD_PRODUCT_SPEC.md`: accepted requirement deltas and narrow explicit amendments.
- `docs/requirements/TRACEABILITY.md`: 27 accepted IDs, retained-criteria clarifications and pending evidence tasks.
- `docs/STATUS.md`: Task 44 progress, environment block and next-task gate.
- `docs/review/TASK_44_ACCEPTED_REQUIREMENTS.md`: scope/amendment/validation record.
- `docs/failures/task-44.md`: this historical environment-block record.

## Original cause and subsequent resolution

The original shell lacked PowerShell 7 and a compatible .NET SDK; global.json requested SDK 10.0.302. No production code, dependency or script defect caused those initial toolchain errors. No gate exemption was inferred from Task 43's documentation-only exemption.

The owner later authorized SDK 10.0.403 and restoration of the missing persistence project. The unchanged repository scripts run successfully under installed Windows PowerShell with the local SDK on PATH: all 18 projects restore, Release build has 0 warnings/errors and the full suite passes 290/290. Owner-authorized per-command authorship then resolves the commit identity error without persistent configuration changes. No push, release, real application update or deployment follows from this recovery.
