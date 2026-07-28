# PC Steward build status

**Current phase:** Phase 0 — Evidence, architecture, and scaffolding
**Current task:** 01 — Architecture, threat model, and execution plan (complete)
**Last green commit:** `9823855` — Task 01 architecture and threat model
**Current branch:** `phase/0-foundation`
**Build command:** To be established by Task 06
**Test command:** To be established by Task 06
**Performance command:** To be established by Tasks 04 and 12
**Phase gate:** Not run

## Decisions

| Decision | Status | ADR |
|---|---|---|
| Product name/identity | Proposed | ADR-001 / OD-01 |
| Packaging model | Proposed | ADR-002 / OD-02 |
| WinGet integration route | Proposed | ADR-003 / OD-03 |
| GitHub Releases MVP scope | Proposed | ADR-004 / OD-04 |
| Runtime grouping | Proposed | ADR-005 / OD-05 |
| Performance baseline | Proposed | ADR-006 / OD-06 |
| Default schedule | Proposed | ADR-007 / OD-07 |
| Licence/source availability | Proposed | ADR-008 / OD-08 |
| Application self-update | Proposed | ADR-009 / OD-09 |
| Diagnostic path redaction | Proposed | ADR-010 / OD-10 |
| Download-cache quota/retention | Proposed | ADR-011 / OD-11 |
| Scheduled process priority | Proposed | ADR-012 / OD-12 |
| Factory-reset mechanism | Proposed | ADR-013 / OD-13 |

## Planned spike order

1. Task 02 — packaging and WinGet evidence for OD-02/03.
2. Task 03 — privilege boundary and safe-deletion evidence for OD-13.
3. Task 04 — performance baseline/fixture evidence for OD-06 and inputs to OD-05/11.
4. Task 05 — review evidence and lock, defer or reject every proposed ADR.

## Active blockers

Task 00 recorded a BLOCKER for the reported Windows 10 Home host versus the Windows 11 x64 target, plus warnings for missing native build/package tooling, unavailable `vswhere`, unverified Developer Mode/VM evidence, and a failing Microsoft Store WinGet source search. These do not block documentation but do block unsupported native build/package claims.

All OD-01–OD-13 decisions remain unresolved until their cited evidence and product-owner decisions are reviewed. No ADR is Accepted.

## Task 01 result

PASS — architecture, trust boundaries, threat model, test strategy, phased implementation plan and 13 proposed ADRs created. Structural coverage and `git diff --check` passed. Deliverable commit: `9823855`.

## Next task

Run `prompts/02_SPIKE_PACKAGING_AND_WINGET_INTEGRATION.md`. Required model/reasoning: GPT-5.6 Terra / High. Branch: `phase/0-foundation`.

## Rules for updating this file

Keep it below 120 lines. Record only current state, unresolved blockers, last green commit, and next task. Historical detail belongs in Git, ADRs, review reports, or failure reports.
