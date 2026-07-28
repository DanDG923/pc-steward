# PC Steward build status

**Current phase:** Phase 3 — Safe update execution and app-owned cache
**Current task:** 24A — approved host-test evidence recorded; blocked on real-update evidence
**Last green commit:** current `HEAD` — `feat: enable reviewed desktop updates`
**Current branch:** `phase/3-execution`
**Build command:** `pwsh -NoProfile -File .\scripts\Build.ps1`
**Test command:** `pwsh -NoProfile -File .\scripts\Test.ps1`
**Performance command:** `dotnet run --project .\tests\PCSteward.Performance\PCSteward.Performance.csproj --configuration Release --no-build`
**Phase gate:** IN PROGRESS — Task 24A not ready for Task 25

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

## Residual risks and accepted deferrals

Task 22 adds an on-demand, one-shot UAC helper with a current-user/SYSTEM local pipe, mutual PID/user/session/path/signature/signer checks, nonce/expiry binding and fixed machine-scope WinGet reconstruction. A product-owner-approved, temporary host-test exception now permits disposable signed no-op UAC and scratch VHD/reparse evidence only; it does not permit real updates, production signing, or persistent trust. Clean-image behavior and real-update verification remain separate gaps. The personal-use exception waives only manual cleanup-UI evidence, not functional cleanup safety or cache-exclusion requirements.

ADR-003 remains Provisional under the product-owner exception: capture a sanitized real update row/source-column shape during Phase 3 when an update becomes available. The exception does not relax fixed executable/source/argument, bounded-output, validation or fail-closed controls.

The supported-Windows, native-tooling, isolated reparse-point and fixed-performance-baseline limitations from Phase 0 remain open. `AC-UX-01–09` are Optional — best effort under ADR-021.

## Task 24A result

Task 24A result: automated evidence is green (42 hostile-root assertions with one VM skip; 16 security, 4 architecture, 69 integration and 138 full-suite tests; Release build zero warnings/errors). Approved host evidence adds signed no-op UAC acceptance/denial, pre-provider helper teardown, and 10 isolated link/race/VHD assertions with complete teardown. `docs/review/PHASE_3_HOST_TEST_EXCEPTION.md` records exact scope/results and the personal-use manual-UI exception. The 2026-07-28 Firefox exercise exposed a missing desktop execution action; the desktop now supports fresh verified review, immutable confirmation/revalidation and controlled post-update verification, but no second real update was run. Real-update verification remains unapproved and Task 25 must not start.

## Next task

Obtain separate product-owner direction for the remaining real-update verification, or a scoped exception. Do not start Task 25 (Sol / Extra High) until every mandatory row in `docs/review/PHASE_3_EVIDENCE_READINESS.md` is Proven or explicitly product-owner-approved.

## Rules for updating this file

Keep it below 120 lines. Record only current state, unresolved blockers, last green commit, and next task. Historical detail belongs in Git, ADRs, review reports, or failure reports.
