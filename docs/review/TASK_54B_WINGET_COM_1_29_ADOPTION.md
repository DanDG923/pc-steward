# Task 54B — Adopt WinGet COM 1.29.380

**Status:** Code, packaging and CI only (2026-11-12). No host action. Ships in release 0.1.3 next to [Task 53A](TASK_53A_DEPENDENCY_SERVICING.md).
**Authority:** [ADR-003 amendment 1](../adr/ADR-003-AMENDMENT-1-WINGET-COM-PROJECTION.md) (Accepted, Option A, owner decision 2026-11-12 14:40 UTC) and its "Follow-up controls and tests". Evidence base: [Task 54A spike report](../../spikes/winget-com-projection/SPIKE_REPORT.md).
**Requirements:** `NFR-MNT-01/03/05`, retained `FR-CAT-01–06`, `FR-SRC-01–05`.

## 1. Version re-check

The amendment allows "1.29.380, or a newer stable release published by then". On 2026-11-12 nuget.org lists, for both `Microsoft.WindowsPackageManager.ComInterop` and `.InProcCom`, `1.29.380` as the newest stable release; everything newer is `1.30.*-preview`. So the spike's surface, import and layout results apply unchanged. Re-checked locally from the two 1.29.380 packages:

- ComInterop still ships only `lib/net8.0-windows10.0.26100.0` (nuspec group `net8.0-windows10.0.26100`) and no package dependencies; its `build/*.targets` are byte-identical to 1.10.340 apart from the framework folder name, so `MicrosoftManagementDeployment-Platform`, `-FactoryLinkage` and `-OutputWinMD` mean what they did.
- InProcCom keeps the x64 layout PC Steward ships from (`runtimes/win-x64/native/` `Microsoft.Management.Deployment.InProc.dll`, `.dll.manifest`, `Microsoft.Management.Deployment.winmd`, `WindowsPackageManager.dll`); an arm64 folder is added and not shipped.
- ComInterop's `lib/uap10.0/Microsoft.Management.Deployment.winmd` (SHA-256 `B101A7A5…`) differs from InProcCom's runtime winmd (`DC9E8A40…`); in 1.10.340 all three copies were the same file (`CE75EC81…`).

## 2. Changes

| Area | Change |
|---|---|
| `Directory.Packages.props` | ComInterop and InProcCom `1.10.340` → `1.29.380` (one exact pin each, same release). |
| 13 Windows projects (10 in `src/`, 3 in `tests/`) | `TargetFramework` `net10.0-windows10.0.22000.0` → `net10.0-windows10.0.26100.0`, plus `SupportedOSPlatformVersion` and `TargetPlatformMinVersion` `10.0.22000.0` stated in each project. The supported minimum stays Windows 11 22000. |
| `spikes/installed-helper-trust-evidence` (2 projects) | Same retarget, because they reference `PCSteward.Execution` and a 22000 project cannot reference a 26100 one. Not built by CI. |
| `PCSteward.Desktop.csproj` | `MicrosoftManagementDeployment-OutputWinMD` `false`, and the InProcCom `runtimes/win-x64/native/Microsoft.Management.Deployment.winmd` shipped as content beside the other three runtime leaves (§3). |
| `scripts/New-PrivateBetaPackage.ps1` | `$inProcComVersion`, the two licence-map keys, and the WinUI output path for the new target framework. |
| `scripts/Test-PrivateBetaPackageEvidence.ps1` | `$expectedInProcComKey`. The four-leaf ownership map is unchanged. |
| `DeploymentCapabilityScriptTests` | Helper build-output folder follows the new target framework. |
| New `WindowsPlatformTargetTests` (architecture) | Pins the target framework and both explicit 22000 minimums in every Windows project (unconditional, stated once, and the set of Windows projects is listed so a new one has to be added on purpose); no shared `Directory.Build.*` may set or override the minimum; both WinGet COM packages share one exact stable pin that the packaging script, licence map and evidence check follow; the desktop ships the InProcCom winmd, not ComInterop's. |
| ADR-008 | Licence re-check entry (§4). |
| `docs/review/task50-owner-build-toolchain-pins.json` | Refreshed for the 54B build commit (§5). |

Not changed: any source file, ADR-003 behaviour (COM is still the only candidate-version authority; predefined `winget`/`msstore` catalogues only; exact-ID filter with the two-result bound; 20-second bound; any failure gives no authority; the CLI still executes), ADR-018/033 (the helper still has no WinRT or COM code), the release version (`VersionPrefix` stays 0.1.3 from Task 53A).

## 3. Packaging evidence: who owns the winmd

From 1.28 the winmd that ComInterop's build targets copy into the output no longer equals the InProcCom runtime copy, which is what the evidence check compares with. The amendment allowed either re-owning the leaf to ComInterop or shipping the InProcCom copy explicitly. 54B ships the InProcCom copy:

- It is the metadata file that belongs to the in-process runtime PC Steward actually ships, released together with it.
- The desktop already ships the other three runtime leaves from InProcCom as explicit content; the winmd now follows the same rule, so one package owns all four runtime leaves and the evidence model (one owner, four hashed leaves, checked against the package source) stays as it was.
- The ComInterop copy is switched off with the package's own documented switch (`MicrosoftManagementDeployment-OutputWinMD=false`), so there is never a second file competing for the same payload path. The projection assembly (`Microsoft.Management.Deployment.CsWinRTProjection.dll`) still comes from ComInterop as before.

## 4. Licence re-check (ADR-008)

Both 1.29.380 nuspecs still declare the SPDX expression `MIT` and the same copyright line (`© Microsoft Corporation. All rights reserved.`), and both packages still carry `NOTICE.txt`, so the licence-map entries change only their version. The `NOTICE.txt` itself grew from 1,382 to 11,003 lines because Microsoft now lists the whole winget-cli repository's component inventory (build, test and tooling packages included). Its component licences are permissive (MIT, Apache-2.0, BSD-2/3-Clause, Zlib, BSL-1.0, HPND, curl, ISC); two entries use scancode references (`LicenseRef-scancode-ms-windows-sdk-win10-net-6` for `Microsoft.Windows.WDK.Win32Metadata`, build-time metadata, and `LicenseRef-scancode-generic-cla AND MIT` for `microsoft/xlang`). No GPL, LGPL, MPL, EPL or CDDL component is listed; the LGPL sentence in the header is Microsoft's standard boilerplate and present in 1.10.340 too. The generated `THIRD-PARTY-NOTICES.txt` carries the new file verbatim, as before. This is an engineering check recorded under the owner's Option A acceptance, which named "a fresh ADR-008 licence check" as part of the cost; it is not legal advice.

## 5. Owner-build toolchain pins

`docs/review/task50-owner-build-toolchain-pins.json` pins the SDK zip and the restored packages for one owner build commit. It still pinned `d9a754b` (before Task 53A, with `Microsoft.Windows.SDK.NET.Ref` `10.0.22000.57` and WinGet COM 1.10.340). 54B regenerates it in CI for the 54B code commit, as the spike recorded: new ComInterop/InProcCom SHA-512 pins, a `Microsoft.Windows.SDK.NET.Ref` 26100 pin in place of `10.0.22000.57`, and the Task 53A Windows App SDK packages. `Test-OwnerBuildToolchainCI.ps1` and its fixture test move to the same commit. The pinned commit has to stay reachable, so this PR must be merged with a merge commit, not squashed.

## 6. The C1 restart gap (MSI 3010)

Task 51 left one known limit: on a current WinGet client an MSI 3010 ("restart to finish") exits **0** and is reported only in text, so PC Steward records Verified or Installed, not verified, instead of Restart required. The coordinator asked to close it here where WinGet 1.29's COM API allows. It does not, within this amendment:

- The 1.29.380 CLI still treats `REBOOT_REQUIRED_TO_FINISH` as success (`InstallFlow.cpp` at tag `v1.29.380`, `ReportInstallerResult`: "treated as a success since installation has completed but is pending a reboot"), so the exit code is still 0.
- Typed evidence exists only as `InstallResult.RebootRequired`, returned by the COM `InstallPackageAsync`/`UpgradePackageAsync` calls. That property is already in 1.10.340; 1.29 adds nothing here. Reading it means **running the update through COM** instead of the CLI.
- ADR-003 amendment 1 decision 3 keeps the CLI as the execution adapter, and ADR-033 keeps the elevated helper free of COM, so machine-scope updates cannot use it at all; user-scope updates could only by changing ADR-003. That is a product decision for the owner, outside 54B.

So the limit stays as Task 51 recorded it, now with the reason pinned: it closes only if the owner later chooses COM execution for user-scope WinGet updates (a separate ADR-003 amendment, its own review), or WinGet changes the CLI exit code.

## 7. Evidence

Recorded on the PR once the Windows CI run on the final commit is green: canonical restore and Release build with 0 warnings (CA1416 included, since warnings are errors); full suite; private-beta package and evidence check (including Task 53A's `package-licence-map` job); owner-only candidate builder; Native AOT helper; MSI build, install and upgrade; desktop launch smoke; owner-build toolchain check with the refreshed pins.

Cloud (Linux `csc` with the CI analyzers): the architecture tests compile with warnings as errors; the four new tests pass and fail as intended when a minimum is removed or the two pins diverge; the rest match the base (compiled-IL scans need the Windows build).

## 8. Not claimed

- Runtime on a real Windows 11 22000 machine (no such runner).
- Desktop activation and a live exact query on the owner's PC with the new engine. That needs separate host approval; the natural point is installing 0.1.3.
- The brand-new-profile DLL initialisation failure (all versions) and Task 90 failure 4 stay open.
- Task 53 evidence and the independent Task 54 review (stage C3) are still to cover 53A and 54B together.
