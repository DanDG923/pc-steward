# Task 54A — WinGet COM projection refresh spike

**Status:** Evidence collected 2026-11-12. Spike only: no production source, package pin, effective ADR or packaging script changed. The recommendation is in the Proposed [ADR-003 amendment 1](../../docs/adr/ADR-003-AMENDMENT-1-WINGET-COM-PROJECTION.md); the owner accepts or rejects it.

**Requirement references:** `NFR-MNT-01; NFR-MNT-03; NFR-MNT-05; FR-CAT-01–06; FR-SRC-01–05` (retained meanings only).

**Baseline:** `phase/4-beta` at `c045fe5`. Production pins `Microsoft.WindowsPackageManager.ComInterop` and `.InProcCom` `1.10.340`; every Windows project targets `net10.0-windows10.0.22000.0` with no explicit minimum OS.

## Authority and boundaries

- **Entry gate deviation, owner-agreed.** The 54A prompt's entry condition is "independent 54 passed". The owner agreed on 2026-11-12 (13:13Z, Phase C close-out and Phase D plan, stage D0) to run D0 in parallel with Phase C, as "desk research and a throwaway CI build only". Task 54 has not run. Nothing here claims a Task 54 pass.
- **Package acquisition.** Under that D0 agreement, the five exact versions below were downloaded from nuget.org into a cloud sandbox and into disposable GitHub-hosted runners only. Nothing was installed or run on the owner's PC, and no host action, catalogue query or COM activation happened there.
- **Catalogue queries** ran only on disposable GitHub-hosted runners, against the public `winget` catalogue, for the single exact ID `Microsoft.PowerShell`, read-only. They are runner evidence, not owner-host evidence.
- Throwaway files live in this folder. The temporary workflow that ran them (`.github/workflows/spike-winget-com-projection.yml`, added in `0020f9a`) is removed again in this PR; to re-run, restore it from that commit.

## Versions compared

| Version | Published on nuget.org | Why it is in the set |
|---|---|---|
| 1.10.340 | 2025-02-28 | Current production pin. Last 1.10 release. |
| 1.11.560 | 2025-11-07 | Last 1.11 release; the smallest step past 1.10. |
| 1.12.440 | 2026-01-06 | Last 1.12 release. |
| 1.28.240 | 2026-06-01 | Last 1.28 release (version numbering jumped from 1.12 to 1.28). |
| 1.29.380 | 2026-09-17 | Newest stable release on 2026-11-12. |

## Evidence runs

| Run | What it did | Result |
|---|---|---|
| Local metadata inspection (cloud sandbox) | Unzipped all ten packages; compared nuspecs, file layout, `.winmd` API surface (public types, enum fields, method names), native imports and exports. | Basis for the tables below. |
| [CI run 43226947910](https://github.com/DanDG923/pc-steward/actions/runs/43226947910) (`0020f9a`) | `surface`: the same comparison on Windows with a `System.Reflection.Metadata` tool and `dumpbin`. `probe`: probe build matrix and activation. `solution` × 3: whole repository unchanged, and retargeted to 1.11.560 and to 1.29.380. | All jobs completed; details below. |
| [CI run 43228228539](https://github.com/DanDG923/pc-steward/actions/runs/43228228539) (`d498444`) | `probe` again, adding a direct DLL-load and factory check and a non-administrator token run of the same account. | Details below. |

The probe (`probe/`) references WinGet COM exactly as `PCSteward.Desktop` does: no factory linkage from ComInterop, and the InProcCom runtime copied in as `Microsoft.Management.Deployment.dll`. Its query mirrors `WinGetComStructuredCatalogQuery`: predefined `winget` catalogue, composite catalogue with `RemotePackagesFromAllCatalogs`, `Id`/`Equals` filter, `ResultLimit = 2`.

## Question 1 — platform target

**Finding: 1.11 and later need the `windows10.0.26100` target framework, but the Windows 11 22000 minimum can stay.**

- Every ComInterop release from 1.11 on ships only `lib/net8.0-windows10.0.26100.0` (nuspec dependency group `net8.0-windows10.0.26100`); 1.10.340 ships `net8.0-windows10.0.22000.0`. Checked for 1.11.560, 1.12.440, 1.28.240 and 1.29.380.
- A project targeting `net10.0-windows10.0.22000.0` cannot restore them: probe builds p2 (1.11.560) and p3 (1.29.380) failed with `NU1202`.
- With `TargetFramework` `net10.0-windows10.0.26100.0` and `SupportedOSPlatformVersion` and `TargetPlatformMinVersion` both set to `10.0.22000.0`, the probe built with zero CA1416 platform-compatibility findings for 1.10.340, 1.11.560 and 1.29.380 (p4–p6).
- The whole repository, retargeted that way (13 Windows projects plus the helper target-framework constant in `DeploymentCapabilityScriptTests` and the output path in `New-PrivateBetaPackage.ps1`), passed for both 1.11.560 and 1.29.380: canonical restore and Release build with **0 warnings, 0 errors** (warnings are errors here, so CA1416 would have failed it); the full CI suite as a disposable standard user **3,451 passed, 1 skipped, 0 failed** (Domain 192, Architecture 222, Application 1,262, Security 1,775 + 1 skipped), identical to the unchanged baseline in the same run; the Native AOT helper check; and the desktop launch smoke (window up in 5–8 s, navigation tree complete).
- The 1.11+ projection assembly itself declares `SupportedOSPlatform("Windows10.0.17763.0")` with target platform `Windows10.0.26100.0`, and the InProcCom native DLLs keep PE OS version 6.0 and import only Windows 10 API sets.
- **Not proven:** running on an actual Windows 11 22000 machine. Hosted runners are Windows Server build 26100. This stays an open runtime check; no minimum-OS change is needed on the evidence so far.

## Question 2 — activation routes

**Finding: the in-process route behaves the same for 1.10.340, 1.11.560 and 1.29.380.**

| Route and token (runner, build 26100) | 1.10.340 | 1.11.560 | 1.29.380 |
|---|---|---|---|
| In-process, elevated administrator | Activates; exact-ID query OK, 1 match, `Id/Equals` | Same | Same; engine reports `1.29.380` |
| In-process, same account with a non-administrator token (`runas /trustlevel:0x20000`) | Same as elevated | Same | Same |
| In-process, brand-new disposable standard user | Runtime DLL fails to initialise (Win32 1114), activation `REGDB_E_CLASSNOTREG` | Same | Same |
| Out-of-process (App Installer 1.26.510, WinGet 1.11.510 on the runner), elevated | Activates; query OK | Same | Activates; reading the 1.12+ `PackageManager.Version` member fails against the older server |
| Out-of-process, brand-new standard user | `REGDB_E_CLASSNOTREG` (App Installer not registered for that user) | Same | Same |

- **Desktop process.** The non-administrator token row is the closest runner match to the owner's desktop (unelevated, in a profile where App Installer is registered); all three versions work there.
- **One-shot elevated helper.** Under ADR-033 the Native AOT helper has no WinRT or COM code, so it never activates WinGet COM. It still references ComInterop transitively (through Execution and Provider.WinGet), so it must take the same target framework; its AOT check passed with 1.11.560 and 1.29.380. Elevated in-process activation was proven anyway in the first row.
- **Brand-new profile failure.** Equal across versions, so it does not decide this question. The production authority catches it and returns no authority (fails closed). The cause is unknown; it is worth keeping in mind next to the unresolved Task 90 failure 4 (owner host, 2026-07-30: the structured COM authority returned nothing for `Microsoft.VisualStudioCode`).
- **Out-of-process with a newer projection.** A newer projection than the installed App Installer fails on members the server lacks. The in-process route avoids this because the engine ships with the app.
- **Two engines disagree.** For `Microsoft.PowerShell`, the in-process engine (all three versions) chose default install version `7.7.0.0` and found no applicable user-scope installer; the runner's App Installer chose `7.6.10.0` with an MSIX installer. A winget maintainer has tied in-process installer filtering to `VerifyVersionInfoW`, which reports an old Windows version to a process with no Windows 10 compatibility manifest ([winget-cli discussion 5450](https://github.com/microsoft/winget-cli/discussions/5450)); `PCSteward.Desktop` has no application manifest in source. This affects every version equally and matters for D1 (install design), where discovery and execution must agree on the installer.

## Question 3 — API need for ADR-023/024

**Finding: no 1.11+ API closes a known ADR-023/024 gap. 1.10.340 is as capable as any newer version for search, install and uninstall.**

API surface (public types, enum fields, method names) of each `Microsoft.Management.Deployment.winmd` compared with 1.10.340; nothing was removed in any version:

| Version | Lines added (CI tool) | What is new |
|---|---|---|
| 1.11.560 | 0 | Identical surface. |
| 1.12.440 | 16 | Font catalogue and installer type; `DownloadOptions` Platform, TargetOSVersion, SkipMicrosoftStoreLicense; `PackageManager.Version`. |
| 1.28.240 | 37 | Above, plus `EditPackageCatalog` (explicit flag); `PackageManagerSettings` CanUnloadPreference and TerminationSignalMonitoring. |
| 1.29.380 | 54 | Above, plus source priority (`AddPackageCatalogOptions`/`EditPackageCatalogOptions.Priority`, `PackageCatalogInfo.Priority`, `CatalogPackage.CatalogPriority`); `PackageCatalogReference.ConnectionValidationHandler` (server certificate callback). |

What ADR-023/024 need, and where 1.10.340 already has it:

| Need | 1.10.340 surface |
|---|---|
| Bounded search over approved predefined catalogues | `GetPredefinedPackageCatalog`, `PackageCatalogInfo.Origin`/`TrustLevel`, `FindPackagesOptions.Selectors`/`Filters`/`ResultLimit`, `PackageFieldMatchOption` (Equals … ContainsCaseInsensitive), `FindPackagesResult.WasLimitExceeded`, `MatchResult.MatchCriteria` |
| Search fields: name, publisher, ID, version, tags | `CatalogPackage.Id`/`Name`, `PackageVersionInfo.Publisher`/`Version`/`Channel`, `CatalogPackageMetadata.Publisher`/`Tags`/`License`/`ShortDescription` |
| Exact version, scope, architecture before review | `CatalogPackage.AvailableVersions`, `GetPackageVersionInfo`, `PackageVersionInfo.GetApplicableInstaller` → `PackageInstallerInfo.Scope`/`Architecture`/`InstallerType`/`ElevationRequirement` |
| Agreements shown and bound | `CatalogPackageMetadata.Agreements` → `PackageAgreement.Label`/`Text`/`Url`; `InstallResultStatus.PackageAgreementsNotAccepted` |
| Install with exact target | `InstallPackageAsync`; `InstallOptions.PackageVersionId`, `PackageInstallScope`, `AllowedArchitectures`, `PackageInstallMode`, `AcceptPackageAgreements`, `SkipDependencies`, `InstallerType` |
| Uninstall | `UninstallPackageAsync`; `UninstallOptions.PackageVersionId`, `PackageUninstallScope`, `PackageUninstallMode` |
| Restart outcome | `InstallResult.RebootRequired` |

Gaps that **no** version up to 1.29.380 closes, so D1 and Tasks 56/60 must design around them either way: the COM surface exposes no dependency list for a package version; `InstallOptions` has no explicit "do not upgrade an existing install" switch (only the `NoApplicableUpgrade` result); `UninstallOptions` has no architecture or instance discriminator.

## Question 4 — evidence impact

| Item | 1.11.560 | 1.29.380 |
|---|---|---|
| `Directory.Packages.props` (two pins) | Version only | Version only |
| 13 Windows `*.csproj` | Target framework 26100 + two minimum-OS properties | Same |
| `New-PrivateBetaPackage.ps1`: `$inProcComVersion`, two licence-map keys, `$winuiOutput` path | Change | Change |
| `Test-PrivateBetaPackageEvidence.ps1`: `$expectedInProcComKey` | Change | Change |
| `DeploymentCapabilityScriptTests.HelperTargetFramework` | Change | Change |
| Package and evidence check, with only the version strings and path changed | **Passed** (101 files, 54,223,843 bytes against baseline 101 files, 53,320,525 bytes) | **Failed**: the payload's `Microsoft.Management.Deployment.winmd` no longer byte-matches the InProcCom runtime copy |
| InProcCom x64 file layout | Unchanged (arm64 runtime added to the package, not shipped) | Unchanged |
| `docs/review/task50-owner-build-toolchain-pins.json` | New ComInterop/InProcCom SHA-512 pins and a `Microsoft.Windows.SDK.NET.Ref` 26100 pin replacing `10.0.22000.57` | Same |
| ADR-008 licence acceptance (names InProcCom 1.10.340, MIT + `NOTICE.txt`) | Fresh licence review; still MIT with `NOTICE.txt` | Same |

Why 1.29.380 fails the check: from 1.28 on, ComInterop's `lib/uap10.0` winmd and InProcCom's runtime winmd have the same API surface but different bytes; up to 1.12 they are identical files. The desktop publish copies ComInterop's winmd (`ReferenceCopyLocalPaths`), and the packaging script requires it to equal the InProcCom runtime copy. 54B must record that leaf as owned by ComInterop (or ship the InProcCom copy explicitly) and update the architecture test and evidence check to match. The 1.28+ InProcCom manifest also gains 13 `activatableClass` entries beside the `comClass` entries; PC Steward's activation path does not use the manifest, but it is a shipped file whose hash changes.

Not run in the spike (54B must run them): the owner-only candidate builder, MSI build and MSI install CI jobs, and the owner-build toolchain check.

## Differences outside the API

| | 1.10.340 – 1.12.440 | 1.28.240 – 1.29.380 |
|---|---|---|
| Visual C++ runtime | `WindowsPackageManager.dll` imports `vcruntime140`, `vcruntime140_1`, `msvcp140`, `concrt140`; the InProc DLL imports `vcruntime140`, `vcruntime140_1` (CI `dumpbin`). PC Steward does not ship them, so the WinGet authority depends on the Visual C++ Redistributable already being on the PC. Without it the DLL cannot load and the authority fails closed: no WinGet update is offered. The same DLLs were reported missing on a Windows 11 Cloud PC in [winget-cli issue 3049](https://github.com/microsoft/winget-cli/issues/3049). | None imported (static runtime). |
| Servicing | No 1.10.x after 2025-02-28. Fixes reach PC Steward only through a new pin, because the in-process engine is PC Steward's own copy. | Current line. |
| Security | [CVE-2026-68821](https://osv.dev/vulnerability/CVE-2026-68821) (Windows Package Manager elevation of privilege, published 2026-08-11, fixed in 1.29.280). Its records name App Installer; whether the in-process engine as PC Steward uses it (unelevated, in the user's own process) is affected is **not established**. A bundled engine older than 1.29.280 cannot carry the fix. | 1.29.380 is after the fix. |
| Engine against the PC's own WinGet | Discovery runs the bundled engine; execution runs the PC's `winget` CLI, which App Installer keeps current (Task 02 recorded `v1.29.280` on the owner's host). 1.29.280 changed default installer-type preference and added source priority, so an old discovery engine can disagree with the executor. | Same line as the executor. |

## Conclusion

- 1.10.340 already meets ADR-023/024's API needs as well as any newer version; no newer API closes a known gap.
- Moving is still worthwhile, and cheap on the evidence: it removes the Visual C++ runtime dependency (1.28+), brings the bundled engine up to the current line and past the CVE-2026-68821 fix (1.29.280+), and keeps discovery close to the executing `winget`. Proven cost: a 26100 target framework with an explicit 22000 minimum (no minimum-OS change), five version/path strings, one packaging-evidence ownership fix, toolchain pins and a licence re-check.
- 1.11.560 is the smallest step (identical API and file layout; packaging evidence passes unchanged), but it keeps the Visual C++ dependency and predates the CVE fix, so it buys little.
- Recommendation in the amendment: **adopt 1.29.380 (or the newest stable at 54B time) before Task 55**, through 54B.

## Open items carried forward

- Runtime on a real Windows 11 22000 machine (no such runner).
- Desktop activation and a live exact query on the owner's PC with the new engine. Needs separate host approval; the natural point is the first release carrying 54B.
- Cause of the brand-new-profile DLL initialisation failure (all versions), and Task 90 failure 4.
- Whether `PCSteward.Desktop` gets a Windows 10 compatibility manifest so the in-process engine sees the true OS version (D1 question, all versions).
