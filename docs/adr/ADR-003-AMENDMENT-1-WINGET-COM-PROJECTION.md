# ADR-003 amendment 1 — WinGet COM projection refresh

**Status:** Accepted, Option A (owner decision 2026-11-12 14:40 UTC). Implemented by Task 54B (1.29.380; [record](../review/TASK_54B_WINGET_COM_1_29_ADOPTION.md))  
**Date:** 2026-11-12  
**Decision owners:** Product owner and implementation reviewer  
**Amends:** [ADR-003](ADR-003-WINGET-INTEGRATION-ROUTE.md) (stays Provisional and unchanged by this file)  
**Related requirements:** `NFR-MNT-01`, `NFR-MNT-03`, `NFR-MNT-05`, `FR-CAT-01–06`, `FR-SRC-01–05`  
**Evidence:** [Task 54A spike report](../../spikes/winget-com-projection/SPIKE_REPORT.md)

## Context

ADR-003 makes Microsoft's WinGet COM API the only candidate-version authority. PC Steward pins `Microsoft.WindowsPackageManager.ComInterop` and `Microsoft.WindowsPackageManager.InProcCom` at `1.10.340` (February 2025) and runs the WinGet engine in process, from its own copy of `WindowsPackageManager.dll`. Windows projects target `net10.0-windows10.0.22000.0`, so the Windows 11 22000 minimum follows from the target framework. Phase D (ADR-023/024) will lean on the same API for search, install and uninstall. Task 54A asks whether to move to 1.11 or later before Task 55, or to defer the move to Task 72.

What the spike established (details and CI runs in the report):

1. **API.** 1.11.560 has exactly the 1.10.340 API surface. Later versions only add things (fonts, download options, engine version, catalogue editing and priority, a server-certificate callback); nothing is removed. 1.10.340 already exposes everything ADR-023/024 can use, and the gaps they must design around (no dependency list, no "do not upgrade" install switch, no uninstall architecture or instance discriminator) are present in every version up to 1.29.380.
2. **Target framework.** Every ComInterop from 1.11 on is built for `net8.0-windows10.0.26100` only; a 22000 target cannot restore it (`NU1202`). Targeting `net10.0-windows10.0.26100.0` with `SupportedOSPlatformVersion` and `TargetPlatformMinVersion` set to `10.0.22000.0` keeps the declared minimum. On that basis the whole repository built with zero warnings (CA1416 included) and passed the full suite (3,451 passed, 1 skipped, as the baseline), the Native AOT helper check and the desktop launch smoke, for 1.11.560 and 1.29.380. Runtime on a real 22000 machine is unproven.
3. **Activation.** In-process activation and an exact-ID query shaped like production behave the same for 1.10.340, 1.11.560 and 1.29.380 on a hosted runner, elevated and with a non-administrator token. A brand-new user profile fails to load the runtime for every version (fails closed). The helper never activates COM (ADR-033).
4. **Runtime dependency.** Up to 1.12.440 the in-process runtime needs the Visual C++ runtime DLLs (`vcruntime140`, `vcruntime140_1`, `msvcp140`, `concrt140`), which PC Steward does not ship; without them the WinGet authority fails closed and no WinGet update is ever offered. From 1.28.240 the runtime is statically linked.
5. **Currency and security.** No 1.10 servicing exists after 1.10.340. CVE-2026-68821 (Windows Package Manager elevation of privilege) is fixed in 1.29.280; whether PC Steward's in-process use was exposed is not established, but an older bundled engine cannot carry the fix. The PC's own `winget`, which executes updates, is on the current line.
6. **Packaging evidence.** For 1.11.560 the package and its evidence check pass with only version strings and one output path changed. For 1.28+ the shipped `Microsoft.Management.Deployment.winmd` comes from ComInterop and no longer byte-matches the InProcCom runtime copy, so the evidence ownership map must change.

## Decision drivers

- Fail closed, but do not fail closed for avoidable reasons (a missing runtime DLL).
- Keep the Windows 11 22000 baseline unless evidence forces a change.
- Ship a maintained engine for a component that reads remote catalogue data.
- Keep discovery and execution on comparable WinGet behaviour before install arrives.
- Smallest change with proven build, test and package evidence.

## Options considered

### Option A — Adopt 1.29.380 (or the newest stable at 54B time) before Task 55

Removes the Visual C++ runtime dependency, brings the engine past the CVE-2026-68821 fix and onto the executor's line. Proven to build, test, compile the AOT helper and launch. Costs: target framework 26100 with an explicit 22000 minimum, one packaging-evidence ownership fix, new toolchain pins (including `Microsoft.Windows.SDK.NET.Ref` 26100 instead of `10.0.22000.57`), a fresh ADR-008 licence check, refreshed Task 53 evidence and an independent Task 54 review of the delta. Runtime on 22000 and on the owner's PC still unproven.

### Option B — Adopt 1.11.560 before Task 55

The smallest step: identical API and file layout, packaging evidence passes unchanged. Keeps the Visual C++ runtime dependency and an engine from before the CVE fix, so it pays most of Option A's cost (target framework, pins, refreshed 53/54) for little benefit.

### Option C — Defer to Task 72

Valid on API grounds alone (1.10.340 meets ADR-023/024 needs). Leaves the Visual C++ dependency, an unserviced engine with an unresolved CVE question, and growing distance from the executing `winget`, through all of Phase D.

## Decision

**Adopt (Option A) before Task 55**, through Task 54B. The owner chose this option, including the sequencing in item 5:

1. Pin `Microsoft.WindowsPackageManager.ComInterop` and `Microsoft.WindowsPackageManager.InProcCom` to the same exact stable version: `1.29.380`, or a newer stable release published by then, which 54B re-checks against this report's surface, import and layout checks.
2. Move every Windows-targeted project to `net10.0-windows10.0.26100.0` and set `SupportedOSPlatformVersion` and `TargetPlatformMinVersion` to `10.0.22000.0` in each. The supported minimum stays Windows 11 22000; this amendment does not change it.
3. Keep every ADR-003 behaviour unchanged: COM is the only candidate-version authority; only the predefined `winget`/`msstore` catalogues; exact-ID filter with the two-result bound; 20-second query bound; any API, activation or catalogue failure gives no authority; the CLI stays the constrained execution and display adapter.
4. Update the packaging evidence for the new layout: version strings, the output path, and ownership of `Microsoft.Management.Deployment.winmd` (ComInterop's copy from 1.28 on), with the architecture test and evidence check to match. Refresh the owner-build toolchain pins and the ADR-008 licence entry. *(54B implementation: the spike allowed either owner; 54B ships the InProcCom runtime copy explicitly and switches the ComInterop copy off, so InProcCom keeps all four runtime leaves. Record §3.)*
5. Sequencing: run 54B before Task 53/54 (stage C3), next to the Windows App SDK servicing in stage C2, so the Phase C evidence report and independent review cover the projection change once instead of being repeated before Task 55.

## Consequences

### Positive

- The WinGet authority no longer depends on the Visual C++ Redistributable being present.
- The bundled engine is current and past the CVE-2026-68821 fix; future servicing is a pin change on the same target framework.
- Discovery and the executing `winget` follow the same installer-selection rules, which D1 relies on.

### Negative / trade-offs

- Every Windows project changes target framework; the Windows SDK projection grows (payload +0.9 MB for 1.11.560 in the spike).
- New toolchain pins and a licence re-check; refreshed Task 53 evidence and an independent Task 54 review of the delta before Task 55.
- Runtime on a real 22000 machine and on the owner's PC remains unproven until a release carrying 54B is installed and checked under separate host approval.

### Not changed by this amendment

ADR-003's status (Provisional) and decisions, ADR-018/033 (the helper still has no COM), ADR-023/024 route gates, and the supported minimum OS.

## Follow-up controls and tests (54B)

- All existing ADR-003 production-adapter tests pass unchanged; Phase C updater regressions rerun; Release build zero warnings; full suite.
- Private-beta package and evidence check, owner-only candidate builder, MSI build and install CI jobs, and owner-build toolchain check all pass on the new pins.
- An architecture test pins the explicit `10.0.22000.0` minimum in every Windows project, so a later edit cannot silently raise it.
- Host checks (desktop activation and one live exact query with the new engine) only under separate approval.

## Reconsider when

The selected version's API, layout or target framework differs from this report; Microsoft drops the in-process package; a 22000 runtime check fails; or a 54B check fails.
