# Task 45 follow-up — Restore the missing persistence project

**Date:** 2026-10-29. **Branch:** `phase/5-v2-planning`. **Authority:** owner instruction “Ok restore the project”, following the Task 45 validation blocker and explicit SDK 10.0.403 selection.

**Outcome:** Missing-project blocker resolved. Solution restore, focused tests, Release build and the full suite pass on SDK 10.0.403. This is build-graph recovery; no new install/uninstall/automation capability or deployed application is accepted.

## Cause and minimal repair

The solution and existing host/test references named `src/PCSteward.Persistence.Sqlite/PCSteward.Persistence.Sqlite.csproj`, but HEAD omitted it. The ignore pattern `*.sqlite` matched the parent source directory, hiding its untracked contents. The existing tracked SqliteDatabase.cs was unaffected, so Git status did not show a missing tracked project.

Restored the exact five-line project from local commit `32215135b74d5d1405ead7b609ff009c7e6318a1`, without cherry-picking that branch or changing product source. Its Git blob is `75d10aeea20a5c42198f7f6eeafa52b0b366fd62`. It references the centrally pinned Microsoft.Data.Sqlite package and Application project and exposes internals only to the existing Application integration test assembly.

Added only `!/src/PCSteward.Persistence.Sqlite/` to .gitignore, with a comment. Database patterns remain in force: root `example.sqlite`, nested `src/PCSteward.Persistence.Sqlite/example.sqlite` and nested `example.db` still match their data exclusions, while the source project is visible to Git. All **18/18** solution project paths now exist.

global.json retains the owner's selected **10.0.403**, latestPatch and no-prerelease policy. The user-local dotnet root is `%LOCALAPPDATA%\Microsoft\dotnet`. No application dependency version, production C# source, existing ADR, helper/provider behavior, script, deployment or global Git setting changes.

## Validation

| Check | Result |
|---|---|
| Before repair: unchanged Restore.ps1 | Expected failure: exit 1, MSB3202 for missing Persistence.Sqlite.csproj. |
| After repair: project identity | Restored Git blob exactly matches 3221513. |
| Git ignore boundary | Source project not ignored; the three synthetic SQLite/database paths above remain ignored without creating data files. |
| After repair: unchanged Restore.ps1 | Exit 0; all 18 projects restored. |
| Focused SqliteDatabaseTests | **19/19**, no failures/skips. |
| Architecture suite | **28/28**, no failures/skips. |
| Unchanged Build.ps1, Release solution | Exit 0, **0 warnings, 0 errors**. Includes desktop, scheduled host and elevated-helper compilation. |
| Unchanged Test.ps1, complete suite | Exit 0, **290/290**: Domain 59, Application integration 181, Architecture 28, Security 22; no failures/skips. |

PowerShell 7 remains absent. The same repository scripts were run using installed Windows PowerShell after setting the local SDK root/PATH for that command. Telemetry and SDK development-certificate generation were disabled; MSBuild node reuse and shared compilation were disabled to avoid retaining SDK locks. No script/test was skipped or rewritten to obtain the passing result.

```powershell
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:MSBUILDDISABLENODEREUSE = '1'
$env:UseSharedCompilation = 'false'
& powershell.exe -NoProfile -File .\scripts\Restore.ps1
& "$env:DOTNET_ROOT\dotnet.exe" test tests/PCSteward.Application.IntegrationTests/PCSteward.Application.IntegrationTests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~SqliteDatabaseTests' -p:UseSharedCompilation=false
& "$env:DOTNET_ROOT\dotnet.exe" test tests/PCSteward.ArchitectureTests/PCSteward.ArchitectureTests.csproj --configuration Release --no-restore -p:UseSharedCompilation=false
& powershell.exe -NoProfile -File .\scripts\Build.ps1
& powershell.exe -NoProfile -File .\scripts\Test.ps1
```

Local logs are `%TEMP%\pc-steward-task45-project-red.log`, `pc-steward-task45-project-restore.log`, `pc-steward-task45-sqlite-focused.log`, `pc-steward-task45-architecture.log`, `pc-steward-task45-project-build.log` and `pc-steward-task45-project-full-suite.log`.

## Requirement and completion boundary

This repair supports `NFR-MNT-01–04`, `NFR-REL-02` and `AC-DAT-01` through the existing ADR-014/015 boundaries and SQLite/architecture/full-suite evidence. It does not implement the 27 new 2.0 requirements. Tasks 44/45's missing build/test evidence is now collected; status/traceability and completion reports record that closure separately from owner acceptance of the Proposed ADRs. Existing distribution/owner-only deployment gates and all future exact-action approval requirements remain.

No real PC Steward database, installed application, source refresh, provider operation, deployment, signing trust, schedule, live AI call, push or release was touched. Older local 10.0.11 runtimes remain a separately recorded cleanup item; SDK 10.0.303 removal was independently verified in the earlier follow-up.

After the initial commit failed for missing Git identity, the owner explicitly authorized using their identity. The established identity from the repository's recent commits is supplied per commit invocation only; repository/global Git configuration is unchanged. The build repair is committed separately from the accepted requirement/Proposed ADR documentation.
