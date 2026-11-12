# Task 53A — Windows App SDK 1.8 servicing (release 0.1.3)

Phase C stage C2 (owner-agreed plan, 2026-11-12). Base: `phase/4-beta` at `92c735e`. Requirements: `NFR-MNT-01; NFR-MNT-05; NFR-REL-01–05`. Prompt: [53A](../../prompts/53A_DEPENDENCY_SERVICING.md).

## Entry conditions

| Condition | State |
|---|---|
| Tasks 49–52 committed | Yes. 49 and 50 done; 51 and 52 merged in PR #63 (`92c735e`). |
| Servicing plan accepted | Yes, 2026-10-30 ([proposal](V2_DEPENDENCY_SERVICING_PROPOSAL.md)). |
| PR #8 reconciliation | Already on this branch: `eef3784` is an ancestor of `92c735e` and of the 0.1.2 release commit `9322ca1`. Installed releases already carry its versions. |
| Owner approval of the exact package set | **Approved 2026-11-12 14:42Z** in the stage thread: Windows App SDK 1.8.260921001, shipped as Microsoft built it, including the new ML and Tensors packages. |

## Package delta

Only `Directory.Packages.props` changes: `Microsoft.WindowsAppSDK` **1.8.250907003 → 1.8.260921001**, the newest 1.8 servicing release on nuget.org (published 2026-09-24). The metapackage pins its components exactly:

| Package | Before | After |
|---|---|---|
| Microsoft.WindowsAppSDK | 1.8.250907003 | 1.8.260921001 |
| Microsoft.WindowsAppSDK.Base | 1.8.250831001 | 1.8.251216001 |
| Microsoft.WindowsAppSDK.Foundation | 1.8.250906002 | 1.8.260803002 |
| Microsoft.WindowsAppSDK.InteractiveExperiences | 1.8.250906004 | 1.8.260708001 |
| Microsoft.WindowsAppSDK.WinUI | 1.8.250906003 | 1.8.260803003 |
| Microsoft.WindowsAppSDK.DWrite | 1.8.25090401 | 1.8.25122902 |
| Microsoft.WindowsAppSDK.Widgets | 1.8.250904007 | 1.8.251231004 |
| Microsoft.WindowsAppSDK.AI | 1.8.37 | 1.8.79 |
| Microsoft.WindowsAppSDK.Runtime | 1.8.250907003 | 1.8.260921001 |
| **Microsoft.WindowsAppSDK.ML** | — | **1.8.2223 (new)** |
| **System.Numerics.Tensors** | — | **9.0.0 (new, from ML)** |

Unchanged: Microsoft.Web.WebView2 1.0.3179.45, WinGet ComInterop/InProcCom 1.10.340, Microsoft.Data.Sqlite 10.0.14, SQLitePCLRaw 2.1.13/2.1.14, MSTest 4.5.2, Microsoft.NET.Test.Sdk 18.11.0. No WinAppSDK 2.x and no WinGet projection change.

**New packages.** The prompt expected none. 1.8.260921001 adds `Microsoft.WindowsAppSDK.ML` (Windows ML) to the metapackage. For `net8.0-windows` and later it ships two managed assemblies, `Microsoft.Windows.AI.MachineLearning.Projection.dll` (84,608 bytes) and `Microsoft.ML.OnnxRuntime.dll` (234,384 bytes), and depends on `System.Numerics.Tensors` 9.0.0, which is not part of the .NET 10 shared framework (`System.Numerics.Tensors.dll`, 410,936 bytes). Its build files only declare project capabilities; it adds no source file, auto-initializer or startup code. PC Steward references none of these types, so .NET never loads them. Native ML binaries live in the Windows App Runtime framework package, not in the payload. The owner chose to ship them as Microsoft built them (2026-11-12), so the build stays standard and later servicing needs no extra exclusions.

The component lib folders of AI, Foundation, InteractiveExperiences, Widgets and WinUI contain the same assembly names before and after.

## Runtime prerequisite

PC Steward is unpackaged and framework-dependent on the Windows App Runtime. The bootstrapper asks for the 1.8 framework with at least the SDK's runtime version (`WindowsAppSDK-VersionInfo.h` in the Runtime package):

| SDK | Required Windows App Runtime 1.8 |
|---|---|
| 1.8.250907003 | 8000.616.304.0 |
| 1.8.260921001 | **8000.994.2142.0** |

The last recorded host observation ([Task 48](TASK_48_LAUNCH_RUNTIME_STOP.md), October 2026) found `Microsoft.WindowsAppRuntime.1.8` **8000.994.2142.0 x64** on the owner's PC, which satisfies the new minimum exactly. That observation is old evidence, not current proof. The 0.1.3 publish paste re-reads it (`Get-AppxPackage`, read-only, no elevation) and stops before publishing if the installed 1.8 runtime is older. A runtime install is system-software installation and needs its own approval; none is proposed here.

## Licence and NOTICE map

`scripts/New-PrivateBetaPackage.ps1` `$licenseMap` was regenerated from each restored package's own nuspec (`<license>`, `<copyright>`) and the licence/NOTICE files present in the package root:

- Version bumps for WindowsAppSDK, AI, Foundation, InteractiveExperiences, Widgets and WinUI; the same licence files and NOTICE layout as before (WinUI keeps `NOTICE.txt` and `tools/NOTICE.txt`).
- `Microsoft.WindowsAppSDK.ML/1.8.2223`: file licence `license.txt`, notice `ThirdPartyNotices.txt`.
- `System.Numerics.Tensors/9.0.0`: SPDX `MIT`, notice `THIRD-PARTY-NOTICES.TXT`.

The script throws unless the map reconciles exactly to the packages in the published dependency manifest. Nothing ran it before this change, so a new CI job `package-licence-map` now runs it and `Test-PrivateBetaPackageEvidence.ps1` on every PR.

**Gap found, not fixed here.** The release payload is built by `New-OwnerOnlyCandidate.ps1`, not by `New-PrivateBetaPackage.ps1`, so released MSIs (0.1.0–0.1.2, and 0.1.3 as built here) carry no `THIRD-PARTY-NOTICES.txt`. Adding the generated notices to the release payload changes the release file set and the candidate builder; it is listed for Task 53/54 rather than mixed into this servicing change.

## Evidence (Windows CI run [43232683533](https://github.com/DanDG923/pc-steward/actions/runs/43232683533), head `c2e55c6`)

All 10 jobs green.

- **Release build:** 0 warnings, 0 errors (warnings are errors).
- **Full suite:** Domain 192/192, Architecture 222/222, Application integration 1270/1270, Security fixtures 1785 passed, 1 skipped.
- **Licence map:** `New-PrivateBetaPackage.ps1` and `Test-PrivateBetaPackageEvidence.ps1` pass: 17 packages, 17 SBOM components, 104-file legacy payload, mechanical validation Pass.
- **Release candidate builder:** 104 files, 66,966,895 bytes (base `92c735e` PR #63 run: 101 files, 66,212,673 bytes). The +3 files and +754,222 bytes match the three ML/Tensors assemblies plus a larger deps.json.
- **MSI install test:** installs, upgrades and refuses as before; installed files equal the manifest (105 installed, 104 listed plus the manifest itself, as on the base).
- **Desktop launch smoke** (informational; self-contained Windows App SDK on the runner): passed.
- Helper Native AOT, toolchain pins (pinned to its historical commit), signing diagnostics and the Task 50 cleanup paste: unchanged and green.

## Not claimed

- A launch of the serviced build on the owner's PC or on a synthetic profile with the framework-dependent runtime. The 0.1.3 update through the in-app updater is that first launch, after the owner's separate approval of the publish paste.
- Current runtime state on the owner's PC (see above).
- Task 48/49/50–52 live evidence belongs to the old payloads; it does not validate this one.
- Task 54B (WinGet COM 1.29) is a separate, owner-gated change.
