# PC Steward build status

**Current phase:** Phase 3 — Safe update execution and app-owned cache
**Current task:** 25 — repeat independent security review under GPT-5.6 Sol / Extra High
**Last green commit:** Task 90 remediation commit — Release build and full suite must remain green
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

## Residual risks and accepted deferrals

Task 22 adds an on-demand, one-shot UAC helper with a current-user/SYSTEM local pipe, mutual PID/user/session/path/signature/signer checks, nonce/expiry binding and fixed machine-scope WinGet reconstruction. The temporary host-test exception covers disposable signed no-op UAC and scratch VHD/reparse evidence; a separately user-approved, one-time Firefox exercise supplied real machine-scope update evidence. Its temporary certificate was removed from both current-user stores and Firefox was uninstalled afterward. Clean-image behavior, a real provider-disconnect path, and per-user no-prompt behavior remain residual risks. The personal-use exception waives only manual cleanup-UI evidence, not functional cleanup safety or cache-exclusion requirements.

ADR-003 remains Provisional under the product-owner exception: capture a sanitized real update row/source-column shape during Phase 3 when an update becomes available. The exception does not relax fixed executable/source/argument, bounded-output, validation or fail-closed controls.

The supported-Windows, native-tooling, isolated reparse-point and fixed-performance-baseline limitations from Phase 0 remain open. `AC-UX-01–09` are Optional — best effort under ADR-021.

## Task 25 result

Task 90 remediated `P3R-01`–`P3R-06`: provider resolution is machine-trusted and fail-closed; verification requires an exact ID/source query plus concrete local compatibility evidence; cache deletion is root-handle relative and reconciles filesystem orphans; confirmation refreshes local installed evidence; and started interrupted work remains verification-required. Focused regressions, Release build and the full suite are recorded in the remediation commit. The gate is still FAIL until the fresh Task 25 reviewer independently confirms no Blocker or High finding remains.

## Next task

Stop here. Repeat Task 25 in a fresh GPT-5.6 Sol / Extra High review. Do not merge Phase 3 or begin Task 26.

## Rules for updating this file

Keep it below 120 lines. Record only current state, unresolved blockers, last green commit, and next task. Historical detail belongs in Git, ADRs, review reports, or failure reports.
