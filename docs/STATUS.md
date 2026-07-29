# PC Steward build status

**Current phase:** Phase 3 — Safe update execution and app-owned cache
**Current task:** 90 — final P3R-14/P3R-15 remediation complete; fresh Task 25 review required
**Last green tree:** this Task 90 commit — focused 61, security 19, architecture 5, Release build 0 warnings/errors, full suite 182, hostile 42/1 skip, WinGet boundary 11, installed-helper host evidence 6/6
**Current branch:** `phase/3-execution`
**Build command:** `pwsh -NoProfile -File .\scripts\Build.ps1`
**Test command:** `pwsh -NoProfile -File .\scripts\Test.ps1`
**Performance command:** `dotnet run --project .\tests\PCSteward.Performance\PCSteward.Performance.csproj --configuration Release --no-build`
**Phase gate:** FAIL — do not merge Phase 3 or begin Task 26

## Decisions

| Decision | Status | ADR |
|---|---|---|
| Product name/identity | Accepted | ADR-001 / OD-01 |
| Packaging model | Provisional — protected machine binary root required | ADR-002 / OD-02 |
| WinGet integration route | Provisional split route — official COM candidate authority plus constrained CLI execution/display | ADR-003 / OD-03 |
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

## Task 90 final remediation handoff

`P3R-01–13` remain closed. `P3R-14` is now implemented through the actual desktop: the official WinGet COM projection supplies the sole executable candidate authority, bound to predefined trusted source, exact ID, installed version and newer candidate version. Display-table text remains non-executable. The current-tree composition test reaches review, runner and exact post-update verification.

`P3R-15` now has production-path host evidence. The real default transport/validator completed a protected, non-reparse, valid same-signer `%ProgramFiles%\PC Steward` no-op UAC path. Actual tamper, trusted-wrong-signer, caller-writable, helper-reparse and between-validation replacement layouts all stopped before the helper marker. The exact root, temporary root and certificate-store entries were verified absent after teardown.

The remediation record is [Task 90 P3R-14/P3R-15 evidence](review/TASK_90_P3R14_P3R15_EVIDENCE.md), with [raw host evidence](review/TASK_90_P3R15_HOST_EVIDENCE.json). All required local gates pass. The phase gate remains procedurally `FAIL` until a fresh independent Task 25 review evaluates this commit.

## Residual risks and accepted deferrals

The recorded host substitute and personal-use UI waiver retain their narrow approved scope. Clean-image installer lifecycle, per-user no-prompt behavior, provider-disconnect cancellation, unconstrained hostile races, same-signer rollback, same-user substitution of the name-based temporary export, imperfect package-ID/process-name correlation and the un-replayed outcome dialog remain residual risks. ADR-002, ADR-003 and ADR-011 remain Provisional. `AC-UX-01–09` are Optional under ADR-021.

## Next task

Repeat Task 25 under fresh GPT-5.6 Sol / Extra High. Do not merge Phase 3 or begin Task 26 before that review changes the gate.

## Rules for updating this file

Keep it below 120 lines. Record only current state, unresolved blockers, last green commit, and next task. Historical detail belongs in Git, ADRs, review reports, or failure reports.
