# PC Steward build status

**Current phase:** Phase 3 — Safe update execution and app-owned cache
**Current task:** 90 — Phase 3 remediation evidence complete; fresh review pending
**Last green commit:** `117b25b27f204bf5d646d9d5cd9df89dd0c5bcab` — Release build 0 warnings/errors; full suite 154 passed
**Current branch:** `phase/3-execution`
**Build command:** `pwsh -NoProfile -File .\scripts\Build.ps1`
**Test command:** `pwsh -NoProfile -File .\scripts\Test.ps1`
**Performance command:** `dotnet run --project .\tests\PCSteward.Performance\PCSteward.Performance.csproj --configuration Release --no-build`
**Phase gate:** FAIL — do not merge Phase 3 or begin Task 26

## Decisions

| Decision | Status | ADR |
|---|---|---|
| Product name/identity | Accepted | ADR-001 / OD-01 |
| Packaging model | Provisional | ADR-002 / OD-02 |
| WinGet integration route | Provisional — Phase 3 evidence exception | ADR-003 / OD-03 |
| GitHub Releases MVP scope | Accepted | ADR-004 / OD-04 |
| Runtime grouping | Accepted | ADR-005 / OD-05 |
| Performance baseline | Accepted | ADR-006 / OD-06 |
| Default schedule | Accepted | ADR-007 / OD-07 |
| Licence/source availability | Accepted | ADR-008 / OD-08 |
| Application self-update | Accepted | ADR-009 / OD-09 |
| Diagnostic path redaction | Accepted | ADR-010 / OD-10 |
| Download-cache quota/retention | Provisional | ADR-011 / OD-11 |
| Scheduled process priority | Accepted | ADR-012 / OD-12 |
| Factory-reset mechanism | Provisional | ADR-013 / OD-13 |
| Solution/component boundaries | Accepted | ADR-014 |
| SQLite ownership/migrations | Accepted | ADR-015 |
| Structured logging/redaction | Accepted | ADR-016 |
| Immutable update jobs | Accepted | ADR-017 |
| Helper IPC/elevation | Accepted | ADR-018 |
| App-owned artifact roots | Accepted | ADR-019 |
| Scheduling without resident process | Accepted | ADR-020 |
| Accessibility best-effort priority | Accepted | ADR-021 |

## Task 25 result

The repeat review closes the earlier scope/architecture collision, current-inventory confirmation, interrupted recovery, NT open-disposition, and orphan-inclusion defects. The current remediation working tree passes Release build (0 warnings/errors), focused evidence (22), security 16, architecture 4, integration 87, full suite 158, legacy hostile script 42 assertions/1 non-elevated skip, and the approved elevated local-PC production-cache fixture (14 assertions).

The gate remains FAIL pending fresh independent review, not an uncollected evidence prerequisite. `P3R-08` now uses bounded structured export data rather than display output, `P3R-09` evicts crash orphans oldest-first, and `P3R-03` now has production-cache evidence for file/directory/root links, scratch-VHD mount, deterministic ancestor swap, and deterministic root swap. See [the remediation evidence](review/TASK_90_P3RRR_EVIDENCE.md), [resolved evidence record](failures/90-phase3-production-hostile-evidence.md), and [the review](review/PHASE_3_SECURITY_REVIEW.md).

## Residual risks and accepted deferrals

The recorded host substitute and personal-use UI waiver retain their narrow approved scope. Clean-image behavior, per-user no-prompt behavior, provider-disconnect cancellation, unconstrained hostile races, same-signer rollback and the un-replayed outcome dialog remain residual risks. ADR-003 and ADR-011 remain Provisional. `AC-UX-01–09` are Optional under ADR-021.

## Next task

Repeat Task 25 under GPT-5.6 Sol / Extra High against this evidence set. Do not merge Phase 3 or begin Task 26.

## Rules for updating this file

Keep it below 120 lines. Record only current state, unresolved blockers, last green commit, and next task. Historical detail belongs in Git, ADRs, review reports, or failure reports.
