# PC Steward build status

**Current phase:** Phase 3 — Safe update execution and app-owned cache
**Current task:** 25 — security review FAILED; Task 90 remediation required
**Last green commit:** `949fc3b` — pre-review head; Release build and 142-test suite green
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

Task 25 failed with two Blocker, two High and two Medium findings in `docs/review/PHASE_3_SECURITY_REVIEW.md`. The elevated helper can start a current-user-writable WinGet alias; the production post-update verifier cannot reach Success because WinGet rows always carry Unknown scope/architecture; the documented ancestor-component deletion race remains unresolved; and cleanup can claim Success while orphaned owned files remain. Installed evidence is not refreshed at confirmation, and interrupted started jobs can be relabelled queued/cancelled. Focused security/architecture/update/cleanup suites, 42 hostile-root assertions (one skip), the zero-warning Release build and all 142 tests passed but do not cover these composed failures.

## Next task

Stop here. Task 90 requires GPT-5.6 Terra / High. Remediate the Task 25 findings, update evidence/traceability, then repeat Task 25 under GPT-5.6 Sol / Extra High.

## Rules for updating this file

Keep it below 120 lines. Record only current state, unresolved blockers, last green commit, and next task. Historical detail belongs in Git, ADRs, review reports, or failure reports.
