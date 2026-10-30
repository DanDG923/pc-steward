# PC Steward 2.0 — Dependency servicing proposal

**Status: Proposed 2026-10-30; not owner-accepted.** This is a planning proposal only. It changes no Task 47 decision, ADR, requirement marker, product source or package version. Proposed tasks 53A, 54A and 54B start only after the owner accepts this proposal and records approval of the exact package changes under `AGENTS.md` ("Do not install packages merely to make a preferred design work; document the requirement and request approval").

## Why

A dependency review on 2026-10-30 updated `phase/4-beta` (PR #8, merge `eef3784`) and held back several updates because they need design or packaging work. The [2.0 roadmap](../product/PC_STEWARD_2_ROADMAP.md) §2 keeps the stack at .NET 10, WinUI 3 / Windows App SDK 1.8, SQLite and WinGet COM (ADR-003), and no Phase B–H task plans a platform package change. Without a slot, these updates either drift indefinitely or arrive as Dependabot PRs in the middle of an evidence-pinned phase.

## Already merged (record only)

PR #8 (`eef3784`, 2026-10-30) changed `Directory.Packages.props` and `scripts/New-PrivateBetaPackage.ps1`:

| Package | Before → after | Note |
|---|---|---|
| Microsoft.NET.Test.Sdk | 18.0.1 → 18.11.0 | Moved with MSTest; the adapter needs ObjectModel 18.10.1+. |
| MSTest.TestAdapter / TestFramework | 4.1.0 → 4.5.2 | Adds about 25 analyzers; `TreatWarningsAsErrors` is on. |
| Microsoft.Data.Sqlite | 10.0.1 → 10.0.14 | Brings SQLitePCLRaw 2.1.13. |
| SQLitePCLRaw.lib.e_sqlite3 (pin) | 2.1.12 → 2.1.14 | Must stay at or above what Microsoft.Data.Sqlite requires. Licence map keys updated. |

Hosted CI on PR #8 (`build-and-test`: restore, Release build, tests) passed. **Task 48 is unaffected:** it deployed the pinned pre-built payload on 2026-10-29, before this merge. Any later payload built from `phase/4-beta` includes these versions, so Task 53 should cite them in its dependency delta (via 53A).

PR #8 also updated `design/prototype` npm packages and added `.github/dependabot.yml`.

## Held-back updates and proposed placement

| Update | Blocker | Proposed placement |
|---|---|---|
| Microsoft.WindowsAppSDK 1.8.250907003 → 1.8 servicing (1.8.260921001 when proposed) | Sub-package versions change (Foundation, WinUI, AI, Widgets, InteractiveExperiences, WebView2); the exact-key licence/NOTICE map needs regeneration from the packages. | **53A** in Phase C, before Task 53, so updater evidence and the Task 54 gate cover it. |
| Microsoft.WindowsPackageManager.ComInterop / InProcCom 1.10.340 → 1.11+ | 1.11+ need a `windows10.0.26100` target; ADR-003 fixes the Windows 11 22000 baseline; the packaging scripts hard-code `1.10.340`. | **54A** spike plus Proposed ADR-003 amendment, then **54B** adoption, both before Task 55. 54A may instead recommend deferral to Task 72. |
| Microsoft.WindowsAppSDK 2.x | New packages outside the licence map; full WinUI regression; the roadmap fixes 1.8. | Candidate item for **Task 72**, targeting after 2.0. |
| `design/prototype`: vinext 1.x, eslint 10, TypeScript 6/7 | vinext 1.x breaks the prototype test setup; the others conflict with peer ranges. The folder is an archive and "not an implementation base". | Out of 2.0 scope. Owner decision: leave as-is (security updates only) or remove the archive's package manifests. |

### Why WinGet COM goes before Phase D

Phase D (WS3) and Phase E (WS4) extend the WinGet COM adapter. ADR-023 point 5 requires the adapter to prove exact version, architecture, scope and channel enforcement without fallback. ADR-003 records the WinGet client on this host as `v1.29.280`, while the projection is pinned at `1.10.340` because it was the newest version compatible with the 22000 target. Deciding the projection before Task 55 avoids building and evidencing WS3 against an API that is then replaced; doing it after Task 59 would invalidate Phase D install evidence.

## Proposed ADR-003 amendment (for 54A to complete)

To be drafted in 54A and accepted by the owner before 54B:

- **Context:** the compatible-projection clause in Consequences ("compile against the Windows 11 `22000` minimum, matching the documented x64 MVP baseline and the compatible official COM projection") ties the projection version to the target framework. ADR-003 already lists "supported Windows baselines change" under *Reconsider when*.
- **Decision options:** (A) keep 1.10.340 and the 22000 target; (B) move to the 26100 target with `TargetPlatformMinVersion`/`SupportedOSPlatformVersion` explicitly `10.0.22000.0`, keeping the Windows 11 22000 baseline; (C) raise the minimum OS.
- **Unchanged in every option:** COM remains the sole candidate-version authority; predefined `winget`/`msstore` catalogs only; exact-ID query with the two-result bound; the 20-second bound; fail-closed conversion of activation/catalog failures; the CLI stays display/execution-only and never authorizes.
- **Evidence required:** 54A answers on target/minimum OS, in-process activation from desktop and elevated helper, API sufficiency for ADR-023/024, and packaging-evidence impact.

## Proposed Task 72 candidate item

Add **Windows App SDK 2.x migration** to the items the owner may name in Task 72: need (servicing lifetime of 1.8), boundaries (no behaviour change; full WinUI and accessibility regression), packaging (new runtime packages in the licence/NOTICE map and evidence scripts), deployment (a new pinned payload under ADR-022 and the host-test plan), and follow-on planning gate. Recording it as a candidate does not reopen roadmap §2.

## Dependabot policy during 2.0

`.github/dependabot.yml` (added in PR #8) scheduled monthly NuGet version-update PRs. In an evidence-pinned phase workflow those can land mid-phase. This proposal sets NuGet `open-pull-requests-limit: 0`, which stops scheduled version-update PRs while Dependabot **security** updates continue. NuGet version changes then happen only in owner-approved servicing tasks such as 53A at phase boundaries. GitHub Actions updates stay monthly because they do not change the shipped payload.

## Owner decisions requested

1. Accept or reject proposed tasks 53A, 54A and 54B and their positions.
2. Approve the exact Windows App SDK 1.8 servicing version for 53A when it runs.
3. Accept the Windows App SDK 2.x candidate item for Task 72.
4. Accept the Dependabot NuGet security-only policy.
5. Decide whether to leave `design/prototype` package manifests in place or remove them.
