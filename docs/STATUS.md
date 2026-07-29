# PC Steward build status

**Current phase:** Phase 3 — Safe update execution and app-owned cache
**Current task:** 25 — repeat security review complete; Task 90 remediation required
**Last green tree:** Task 25 review of `b5cc577` — Release build 0 warnings/errors; full suite 168 passed
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

## Task 25 repeat review result

`P3R-10` is closed narrowly: installed WinGet table inventory performs one bounded display-only call and retains `Unknown` compatibility; post-update Success combines export JSON with the reviewed structured local product. `P3R-01–12` otherwise retain their closed dispositions.

The gate fails on three actionable findings. `P3R-14` (High) promotes a WinGet available-update display-table version into the immutable `--version` argument. `P3R-15` (Blocker) launches the caller-writable helper through `runas` before signature/signer validation. `P3R-13` (Medium) remains open because a target first detected by the coordinator immediately before provider execution is recorded but not user-visible or acknowledgement-gated.

Focused 51, security 16, architecture 4, Release build 0 warnings/errors, full suite 168, hostile deletion/IPC 42 with one elevated-only skip, WinGet boundary 11 and read-only machine-provider 6 all pass. Direct Release evidence reproduced table-to-argument authority and found the unsigned helper path grants the caller `FullControl`; passing counts do not override either security boundary. See [Phase 3 security review](review/PHASE_3_SECURITY_REVIEW.md).

## Residual risks and accepted deferrals

The recorded host substitute and personal-use UI waiver retain their narrow approved scope. Clean-image behavior, per-user no-prompt behavior, provider-disconnect cancellation, unconstrained hostile races, same-signer rollback, same-user substitution of the name-based temporary export, imperfect package-ID/process-name correlation and the un-replayed outcome dialog remain residual risks. ADR-002, ADR-003 and ADR-011 remain Provisional. `AC-UX-01–09` are Optional under ADR-021.

## Next task

Run Task 90 for `P3R-13–15`, then repeat Task 25 under fresh GPT-5.6 Sol / Extra High. Do not merge Phase 3 or begin Task 26.

## Rules for updating this file

Keep it below 120 lines. Record only current state, unresolved blockers, last green commit, and next task. Historical detail belongs in Git, ADRs, review reports, or failure reports.
