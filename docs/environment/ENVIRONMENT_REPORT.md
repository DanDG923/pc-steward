# Windows development environment report

Task 00 audit date: 2026-07-28. Checks were read-only except for launching a harmless PowerShell child process. Machine-specific identity and paths are redacted.

## Summary

| Area | Status | Evidence |
|---|---|---|
| Architecture | PASS | `Get-ComputerInfo`: 64-bit |
| Required OS | BLOCKER | Reports `Windows 10 Home`, build `26200`; MVP targets Windows 11 x64 |
| PowerShell | PASS | PowerShell 7 host; exact version available from `$PSVersionTable.PSVersion` |
| Git | PASS | Git 2.55.0.windows.3; branch is `phase/0-foundation`; user name and email configured (redacted) |
| .NET | PASS | SDKs 10.0.300–10.0.302; runtimes include .NET 6, 8, 9, and 10 desktop/runtime packs |
| Visual Studio / Build Tools | WARNING | `vswhere.exe` was not available |
| Windows SDK / WinUI | WARNING | No Windows Kits include directory was detected; installed package inventory shows Windows App Runtime packages, but not a build workload |
| WinGet | PASS/WARNING | `winget --version` returned `v1.29.280`; `winget source list` returned default sources; `winget list` returned installed records; Microsoft Store source search failed |
| Developer Mode | WARNING | No readable `AllowDevelopmentWithoutDevLicense` value returned |
| Signing/package tools | WARNING | `signtool.exe`, `makeappx.exe`, `msbuild.exe`, and `nuget.exe` were not found on PATH |
| Minimal process | PASS | Non-elevated `powershell.exe -NoProfile -NonInteractive -Command 'exit 0'` returned exit code 0 |
| Isolated environment | WARNING | No dedicated isolated environment or Windows Sandbox availability was established |

## Commands and concise outputs

- `Get-ComputerInfo | Select WindowsProductName,WindowsVersion,OsBuildNumber,OsArchitecture` → `Windows 10 Home`, `2009`, `26200`, `64-bit`.
- `$PSVersionTable.PSVersion` → PowerShell 7 host; the tabular host output was formatting-only and did not expose the individual version fields.
- `git --version` → `git version 2.55.0.windows.3`.
- `git config user.name` / `git config user.email` → configured; values intentionally omitted.
- `dotnet --list-sdks` → 10.0.300, 10.0.301, 10.0.302.
- `dotnet --list-runtimes` → .NETCore, ASP.NET Core, and WindowsDesktop runtimes for 6.0, 8.0, 9.0, and 10.0.
- `Get-Command vswhere.exe` → not found.
- `Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\Include'` → no result.
- `winget --version` → v1.29.280.
- `winget source list` → `msstore`, `winget`, and explicit `winget-font` sources listed.
- `winget list --disable-interactivity --accept-source-agreements` → installed inventory returned; source search reported `msstore` failure.
- Developer Mode registry read → no value returned.
- `Get-Command signtool.exe,makeappx.exe,msbuild.exe,nuget.exe` → no result.
- Child PowerShell process → exit code 0.
- Optional-feature/service checks for Hyper-V, Windows Sandbox, and `vmcompute` → no conclusive availability evidence returned.
- `git status --short --branch` → clean `## phase/0-foundation` before this report was created.

## Interpretation

The repository can proceed with documentation and architecture work. Task 06 build/scaffold work is not yet proven feasible on this host because the required Windows 11 target and native packaging/build tooling are not evidenced. WinGet read-only inventory is usable, subject to the Microsoft Store source warning. No system changes were made.
