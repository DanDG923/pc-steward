# Task 90 — remediate repeated Phase 1 review

## Goal

Resolve the code-side P1R findings without enabling WinGet source access, elevation, update execution, or other later-phase scope. Preserve unexecuted final-application/manual acceptance evidence as unproven unless separately authorized.

## Requirement IDs

`FR-INV-06–09`, `FR-RSC-03`, `FR-DAT-06/10`, `FR-ONB-04`, `FR-UI-02/05`, `NFR-PERF-01/03/05/06/08/09`, `NFR-MNT-01/05`, `NFR-OBS-02`, `AC-INV-01–02/04–05`, `AC-DAT-06`, `AC-NFR-01/03/04/06–07/09`, `AC-UX-01/02/04/06/09`.

## Steps

1. Completed — normalization now groups duplicate provider products deterministically; orchestration coverage passes (P1R-01).
2. Completed — WinGet cancellation and timeout terminate the process tree; provider timeout coverage proves cooperative cancellation (P1R-02).
3. Completed — clear-data removes scan freshness transactionally; persistence coverage proves reload has no last-completed value (P1R-03).
4. Completed with limitation — deterministic measurement output was verified; final-application/UI/manual acceptance remains unexecuted (P1R-04).
5. Completed in part — connector selection persists, unapproved WinGet is unavailable, source/scope/sort controls render, and the size label is truthful. Publisher/status filters and an approved enabled-WinGet flow remain for later work (P1R-05).
6. Completed in part — the misleading byte label is corrected. Typed normalized provenance, structured scan events and the direct desktop adapter boundary remain for the fresh review to assess (P1R-06/07).
7. Completed — focused integration suite (22), Release build, full suite (29), deterministic workload and fixture integrity (14 assertions) passed.
8. Completed — commit only verified results, then stop for fresh Task 15 review.

## Evidence limits

There is no approval for real WinGet source disclosure or manual desktop exercises. Those acceptance criteria remain unexecuted and cannot be reported as passed. A supported isolated Windows environment is acceptable where safety testing requires one; a VM is optional.

## Completion record

- Commit: `58ca1eb` — `fix: remediate phase review findings`
- Tests: Release build (0 warnings/errors); full suite 29 passed; deterministic workload 30 runs (median 1.09 ms, p95 1.61 ms); performance fixture integrity 14 assertions passed.
- Remaining risks: supported performance baseline, full process tree/idle/UI metrics, manual accessibility, approved enabled-WinGet behavior, typed scan provenance/events, and the desktop adapter boundary.
