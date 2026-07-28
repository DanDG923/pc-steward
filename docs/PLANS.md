# Task 90 — remediate repeated Phase 1 review

## Goal

Resolve the code-side P1R findings without enabling WinGet source access, elevation, update execution, or other later-phase scope. Preserve unexecuted final-application/manual acceptance evidence as unproven unless separately authorized.

## Requirement IDs

`FR-INV-06–09`, `FR-RSC-03`, `FR-DAT-06/10`, `FR-ONB-04`, `FR-UI-02/05`, `NFR-PERF-01/03/05/06/08/09`, `NFR-MNT-01/05`, `NFR-OBS-02`, `AC-INV-01–02/04–05`, `AC-DAT-06`, `AC-NFR-01/03/04/06–07/09`, `AC-UX-01/02/04/06/09`.

## Steps

1. Completed — P1RR-01 fails closed for legacy WinGet state; approved mode is explicit.
2. Completed — P1RR-02 contains late faults and blocks replacement scans until an unresponsive provider stops.
3. Completed — P1RR-03 recorded physical Windows baseline, one approved final-app scan and restart evidence; remaining mandatory samples are explicitly unproven.
4. Completed — P1RR-04 adds publisher/status controls and durable filter state.
5. Completed — P1RR-05 preserves typed metadata and exposes bounded diagnostic events/records.
6. Completed — P1RR-06 moves desktop composition behind the application workflow and guards it with an architecture test.
7. Pending — full suite, benchmark/fixture integrity, scope review and commit.

## Evidence limits

There is no approval for real WinGet source disclosure or manual desktop exercises. Those acceptance criteria remain unexecuted and cannot be reported as passed. A supported isolated Windows environment is acceptable where safety testing requires one; a VM is optional.

## Completion record

- Commit: `dde92df` — `fix: remediate phase review findings`
- Tests: Release build (0 warnings/errors); full suite 29 passed; deterministic workload 30 runs (median 1.09 ms, p95 1.61 ms); performance fixture integrity 14 assertions passed.
- Remaining risks: supported performance baseline, full process tree/idle/UI metrics, manual accessibility, approved enabled-WinGet behavior, typed scan provenance/events, and the desktop adapter boundary.
