# PC Steward 2.0 — Dependency servicing plan (PR #10)

**Status: planning addition accepted 2026-10-30 after main and Opus 5.5 review/recheck.** Source: [PR #10](https://github.com/DanDG923/pc-steward/pull/10), pinned head `042fa65ff4963f36e3ef2b06a443eba813e90a2f`, base `eef3784dece45b90abb80ca0319838a75f57806e`. The owner's instruction conditionally authorizes adding suitable planning changes once main and Opus 5.5 agree. This records task placement only; exact package changes, an effective ADR-003 amendment and host actions retain separate approval. No implementation starts from these rows alone.

## Verified baseline and branch distinction

[PR #8](https://github.com/DanDG923/pc-steward/pull/8) merged into `phase/4-beta` on 2026-10-30 at `eef3784`; its hosted [build-and-test](https://github.com/DanDG923/pc-steward/actions/runs/41115368919) passed. Its recorded package delta is:

| Package | Before → after on phase/4-beta |
|---|---|
| Microsoft.NET.Test.Sdk | 18.0.1 → 18.11.0 |
| MSTest.TestAdapter / TestFramework | 4.1.0 → 4.5.2 |
| Microsoft.Data.Sqlite | 10.0.1 → 10.0.14 |
| SQLitePCLRaw.lib.e_sqlite3 | 2.1.12 → 2.1.14 |

The current `phase/5-v2-planning` checkout at `2a13fe5` retains the **before** versions. This planning integration does not import PR #8 package/prototype changes. Task 53A must establish the actual branch baseline, reconcile the approved dependency delta and regenerate packaging evidence rather than assuming the new versions are present. Task 48 and the later Task 49 replacement each retain their own exact payload records; PR #8's branch merge neither updates an installed payload nor revalidates one.

## Placement and entry gates

| Component | Placement | Required decision/evidence |
|---|---|---|
| Windows App SDK 1.8 servicing | [53A](../../prompts/53A_DEPENDENCY_SERVICING.md), after 49–52 and before 53 | Exact package set approved; regenerated licence/NOTICE map and package validation; Release/full suite. Task 53 cites the delta; Task 54 reviews that snapshot. |
| WinGet COM projection refresh | [54A](../../prompts/54A_WINGET_COM_PROJECTION_SPIKE.md), after independent 54 and before 55 | Spike on actual package/API metadata; proposed ADR-003 amendment; owner accepts adoption or explicit deferral. |
| Conditional projection adoption | [54B](../../prompts/54B_ADOPT_WINGET_COM_PROJECTION.md), before 55 only on accepted adoption | Exact projection pair, target and minimum OS approved; unchanged candidate-authority behavior; licence/package checks; refresh 53 evidence and independently repeat affected 54 review before 55. |
| Windows App SDK 2.x | Candidate for [72](../../prompts/72_DEFERRED_ITEM_DECISIONS.md) after 2.0 | Individually requested reconsideration; no accepted migration or requirement-marker change. |
| Projection refresh deferred by 54A | Candidate for 72 | Explicit accepted deferral and evidence that retained APIs suffice for ADR-023/024; unsupported routes stay unavailable. |
| Archived prototype major updates | Outside 2.0 implementation | Retain archive/manifests. No removal or vinext/eslint/TypeScript major migration authorized. |

Windows App SDK `1.8.260921001` is a real 1.8 servicing release ([Microsoft release notes](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-1-8?pivots=stable)); it is an example, not the approved future version. The official [ComInterop 1.11.400 metadata](https://www.nuget.org/packages/Microsoft.WindowsPackageManager.ComInterop/1.11.400) includes `net8.0-windows10.0.26100`. That establishes a target-compatibility question, not a proof that an explicit 22000 runtime minimum works. Inspect the exact selected ComInterop/InProcCom pair in 54A; do not generalize one package's metadata to all future releases.

## ADR-003 amendment remains Proposed

54A compares retaining 1.10.340, a newer target with an explicit 22000 minimum (if proven), and raising the minimum OS. It must inspect both desktop and one-shot helper activation, ADR-023/024 API sufficiency and hard-coded packaging pins. ADR-003's historical WinGet observation is not a current installed-version probe. New package acquisition/restore and any live activation/query require their applicable separate approvals. Missing runtime/baseline proof remains open.

COM remains the sole candidate-version authority, predefined winget/msstore catalogs only, exact-ID/two-result bounds, 20-second query bound, fail-closed authority, CLI display/execution only. ADR-003 stays Provisional; its Consequences describes the current 22000 baseline. No source trust, privileged route or runtime baseline decision changes in this task. Task 53A must record the serviced build runtime prerequisite and obtain separate authority for any host probe/runtime installation. Existing Task 49 e_sqlite3 lock failure must be covered before a later separately approved replacement.

## Dependency automation

Record PR #10's NuGet `open-pull-requests-limit: 0` policy as the planning decision only: scheduled NuGet version PRs are disabled during 2.0 while separately enabled Dependabot security updates retain their separate limit ([GitHub documentation](https://docs.github.com/en/code-security/how-tos/secure-your-supply-chain/secure-your-dependencies/configure-security-updates)). This file does not enable the repository security setting. Existing version PRs are not approved or closed by it. GitHub Actions stays monthly. The prototype has no npm ecosystem stanza here; do not claim this configuration proves active npm security updates. Security settings and advisories are not audited by this planning task. No .github/dependabot.yml is added on this planning branch. Applying the policy to the default branch requires separate branch/publication authority. Security PRs also require approved servicing and must preserve the Microsoft.Data.Sqlite/e_sqlite3 pin invariant; existing groups do not group security updates unless applies-to: security-updates is configured. Actions updates change CI evidence and require verification.

## Review and validation record

Main identified and corrected missing 53/55 prerequisites, changed-snapshot review carry-forward, the spike-only versus ADR-output conflict, branch/payload attribution and implied prototype security coverage. Opus initial review found F1–F8 (branch/payload attribution, runtime prerequisite, changed-snapshot updater gate, prompt wiring, ADR output boundary, acquisition/activation approvals, automation policy and preservation). Main accepted those corrections, applied them and submitted the corrected files for a fresh Opus 5.5 recheck. Recheck PASS: all findings closed, no safety/requirement regressions; main agrees. Final marker edits remove stale Proposed/entry wording. Advisory exact-pair and isolated central-management notes were incorporated. If the owner declines the exact 53A package set, Task 53 remains blocked until an explicit plan amendment/deferral; no silent bypass.

### Validation and scope

- Current source baseline: 2a13fe5; PR #10 head 042fa65ff4963f36e3ef2b06a443eba813e90a2f. Requirement references: NFR-MNT-01/03/05, NFR-REL-01–05, FR-SRC-01–05, FR-CAT-01–06, FR-UPD-02–04. Retained meanings only.
- Canonical pwsh invocation was unavailable (CommandNotFoundException). Existing Task 45 fallback: command-scoped DOTNET_ROOT=%LOCALAPPDATA%/Microsoft/dotnet, prepend PATH, disable telemetry/certificate generation/node reuse/shared compilation; run unchanged scripts via powershell.exe -NoProfile -File .\scripts\Build.ps1 and .\scripts\Test.ps1. Release exit 0, zero warnings/errors; full 415/415 (59 Domain, 30 Architecture, 74 Security, 252 Application), zero skipped.
- Relative Markdown links 263/263 and git diff --check pass; final counts recorded in STATUS. No production/package-pin/ADR/config change. Existing Task 49 untracked evidence and prior status/traceability entries preserved.
- No package installation, live app/provider query, deployment, runtime installation, PR merge, push or publication. PR #10 remains unchanged remotely. Local planning files are left uncommitted for review alongside the pre-existing owner work.
