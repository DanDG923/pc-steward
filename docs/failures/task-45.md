# Task 45 — Resolved validation and commit identity failures

**Date:** 2026-10-29. **Checkout:** `<CLONE>`. **Branch:** `phase/5-v2-planning`.

**Current disposition:** Owner-authorized SDK/project restoration resolves validation: all 18 projects restore, SQLite 19/19, Architecture 28/28, Release build 0 warnings/errors and full suite 290/290 pass. Git initially refused committing for unset author identity. The owner then authorized their established identity per invocation, resolving that blocker without persistent Git configuration changes. Build repair is `659b58f`; task documentation is committed separately. Initial failures below are historical.

## Commands and exact errors

Attempt 1:

```powershell
pwsh -NoProfile -File .\scripts\Build.ps1
```

```text
pwsh : The term 'pwsh' is not recognized as the name of a cmdlet, function, script file, or operable program. Check
the spelling of the name, or if a path was included, verify that the path is correct and try again.
FullyQualifiedErrorId : CommandNotFoundException
```

Attempt 2 (installed Windows PowerShell invoking the unchanged repository scripts):

```powershell
& powershell.exe -NoProfile -File .\scripts\Build.ps1
& powershell.exe -NoProfile -File .\scripts\Test.ps1
```

Both command invocations returned tool exit code `1`, before compilation or test discovery. Build stderr:

```text
The command could not be loaded, possibly because:
  * You intended to execute a .NET application:
      The application 'build' does not exist.
  * You intended to execute a .NET SDK command:
      A compatible .NET SDK was not found.

Requested SDK version: 10.0.302
global.json file: <CLONE>\global.json

Installed SDKs:
No .NET SDKs were found.

Install the [10.0.302] .NET SDK or update [<CLONE>\global.json] to match an installed SDK.

Download a .NET SDK:
https://aka.ms/dotnet/download

Learn about SDK resolution:
https://aka.ms/dotnet/sdk-not-found
```

Test stderr is identical except `The application 'test' does not exist.` No test ran and no build passed; Task 42's historical results are not refreshed.

## Task 45 changed files

- `docs/adr/ADR-023-CATALOGUE-INSTALL-OPERATIONS.md`
- `docs/adr/ADR-024-INSTANCE-BOUND-MANUAL-UNINSTALL.md`
- `docs/adr/ADR-025-TYPED-OPERATION-JOBS-AND-HELPER.md`
- `docs/adr/ADR-026-WS5-AUTOMATION-AUTHORIZATION-DECISION.md`
- `docs/review/TASK_45_PROPOSED_OPERATION_ADRS.md`
- `docs/STATUS.md` and `docs/requirements/TRACEABILITY.md` (additive Task 45 updates over existing Task 44 edits)
- `docs/failures/task-45.md` (this record)
- `global.json` (subsequently authorized 10.0.403 SDK selection only)

Task 44's specification/amendment/failure files and existing ADRs are preserved. No code, script, dependency or toolchain setting is implicated.

## Smallest suspected cause and unblock

The original shell lacked PowerShell 7 and a compatible SDK. The owner authorized installing .NET, selecting patched SDK 10.0.403 and changing global.json accordingly. Only PowerShell 7 remains absent; Windows PowerShell can invoke these unchanged scripts. The current blocker is the missing Persistence.Sqlite project described below. Owner ADR acceptance and a documentation-only test-gate exemption remain separate decisions; neither is inferred.

No commit is made under AGENTS.md's required-test gate. No push, release, host application action or package-manager operation is attempted.

## Superseding SDK, removal and build evidence

This section describes the intermediate state before the owner's project-restoration instruction; the final section records its resolution.

Installed under `%USERPROFILE%\AppData\Local\Microsoft\dotnet`, from official Windows x64 archives with SHA-512 verified against Microsoft's release metadata:

| SDK | Archive SHA-512 | Current state |
|---|---|---|
| 10.0.303 | `AD4EF6202E55BABDE1C65E1BE7B468C3ECB738CCC2FB8BD3C2BB408A4F45D247C8C5A5F57FEDC54EBEE7CB5FC4F487772997F2D053010D2E1903B974CC64216D` | Owner removed SDK after lock diagnosis; directory independently absent. Older 10.0.11 runtimes/packs/host/templates and archive remain. |
| 10.0.403 | `7A114C318FA7F1D9822991B534B2814D0E78386CEDA816B371F5141ACEFE741D87CCB606EDE06CADAC8F9AC5070E32B708A67BBD1619FF5E5A7858A27874EF48` | Sole SDK in local root; selected by updated global.json; host 10.0.14; Valid Microsoft host signature. |

Automatic approval review rejected recursive old-version cleanup and direct SDK-path deletion, stated reason “blocked by policy”. The owner ran the exact SDK-only PowerShell deletion command; DLL access errors and a final in-use error followed. Live process/module inspection proved 15 exact 10.0.303 MSBuild node-reuse workers held those modules. Only those workers were stopped after current executable/command-line rechecks, then zero remained. The owner retried; the SDK directory is now absent. This does not remove the separately installed 10.0.11 shared runtimes or prove other deployed apps patched. No further automated deletion attempt was made.

To reproduce current validation with the installed SDK (no permanent PATH change):

```powershell
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& "$env:DOTNET_ROOT\dotnet.exe" --version
& powershell.exe -NoProfile -File .\scripts\Restore.ps1
& powershell.exe -NoProfile -File .\scripts\Build.ps1
```

Selected SDK: `10.0.403`. Restore and build exit `1`; current first restore error:

```text
%USERPROFILE%\AppData\Local\Microsoft\dotnet\sdk\10.0.403\NuGet.targets(521,5): error MSB3202: The project file "<CLONE>\src\PCSteward.Persistence.Sqlite\PCSteward.Persistence.Sqlite.csproj" was not found. [<CLONE>\PCSteward.slnx]
```

Build repeats MSB3202 and NETSDK1004 missing-assets errors because restore failed: **0 warnings, 18 errors**. No tests ran against the unbuilt solution. Logs: `%TEMP%\pc-steward-task45-restore-10.0.403.log` and `%TEMP%\pc-steward-task45-build-10.0.403.log`.

Exactly one of 18 solution project paths is absent. HEAD and Task 42's tree both omit the project; this is not a Task 45 deletion or OneDrive hydration claim. `git check-ignore -v --no-index` identifies `.gitignore:18:*.sqlite` matching the parent directory. Local commit `32215135b74d5d1405ead7b609ff009c7e6318a1` already contains a five-line restoration; no source repair/cherry-pick is made in this scoped task. The smallest next code/build repair is to review that project restoration and ignore-rule handling separately, then rerun restore/build/full suite. Task 45 additionally changes global.json only under the owner's explicit SDK instruction; no product source/dependency/script changes.

## Validation closure and initial commit identity failure

Under the owner's subsequent instruction “Ok restore the project”, the exact five-line project from `3221513` was restored and .gitignore received only a narrow source-directory exception. [Recovery evidence](../review/TASK_45_PROJECT_RESTORE.md) records the matching blob, retained database exclusions, 18/18 restored projects, SQLite 19/19, Architecture 28/28, Release build 0 warnings/errors and full suite **290/290** on SDK 10.0.403. Existing scripts ran under Windows PowerShell; no product source/dependency/script/test changed or required check was skipped. The earlier toolchain/project blockers are closed.

The four reviewed build-repair files were staged, and `git diff --cached --check` passed. A local commit attempt then returned exit **128**:

```text
Author identity unknown
fatal: unable to auto-detect email address (got '<USER>@<PC>.(none)')
```

At that point AGENTS.md prohibited changing Git identity/global configuration, so no identity override or workaround was attempted. The owner then explicitly instructed “You can use me as the author”. Read-only history confirmed the same owner identity across recent commits, and the authenticated GitHub account matched the repository owner. That established name/email was supplied per Git invocation only, with repository/global configuration unchanged. Build repair is committed as `659b58f`; Task 44 accepted requirements and Task 45 Proposed ADR/completion records are committed separately. All validation/commit blockers in this record are resolved. Owner ADR acceptance remains separate; no push, release or deployment was attempted.
