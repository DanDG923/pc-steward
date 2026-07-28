# PC Steward build status

**Current phase:** Phase 3 — Safe update execution and app-owned cache
**Current task:** 22 — awaiting GPT-5.6 Sol / High
**Last green commit:** `fd0cb3e` — `feat: add immutable update jobs and constrained runner`
**Current branch:** `phase/3-execution`
**Build command:** `pwsh -NoProfile -File .\scripts\Build.ps1`
**Test command:** `pwsh -NoProfile -File .\scripts\Test.ps1`
**Performance command:** `dotnet run --project .\tests\PCSteward.Performance\PCSteward.Performance.csproj --configuration Release --no-build`
**Phase gate:** IN PROGRESS — Task 21 passed

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

Task 21 structurally constrains execution to immutable, explicitly confirmed, integrity-checked WinGet jobs. Machine-scope jobs return elevation-required without execution; the Task 22 helper/UAC boundary is not implemented or proven yet. No real software update was run.

ADR-003 remains Provisional under the product-owner exception: capture a sanitized real update row/source-column shape during Phase 3 when an update becomes available. The exception does not relax fixed executable/source/argument, bounded-output, validation or fail-closed controls.

The supported-Windows, native-tooling, isolated reparse-point and fixed-performance-baseline limitations from Phase 0 remain open. `AC-UX-01–09` are Optional — best effort under ADR-021.

## Task 21 result

Task 21 result: `PASS`. Focused suites: Domain 49, Application integration 51 and Security fixture 6 passed. Release build: zero warnings/errors; full suite: 109 passed. Negative fixtures cover command/path/argument-shaped values, public execution surfaces, SQLite mutation, evidence substitution, non-zero exit, timeout, cancellation, output overflow and sanitization.

## Next task

Run Task 22 — short-lived elevated helper and secure IPC — in a fresh GPT-5.6 Sol / High task. Stop before starting it under Terra.

## Rules for updating this file

Keep it below 120 lines. Record only current state, unresolved blockers, last green commit, and next task. Historical detail belongs in Git, ADRs, review reports, or failure reports.
