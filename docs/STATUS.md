# PC Steward build status

**Current phase:** Phase 4 — approved visual foundation integration
**Current task:** Task 29 blocked — idle-resource gate requires process-attributed diagnosis
**Last green tree:** Task 28 working tree — Release build 0 warnings/errors; full suite 190 passed
**Current branch:** `phase/4-beta`
**Build command:** `pwsh -NoProfile -File .\scripts\Build.ps1`
**Test command:** `pwsh -NoProfile -File .\scripts\Test.ps1`
**Performance command:** `dotnet run --project .\tests\PCSteward.Performance\PCSteward.Performance.csproj --configuration Release --no-build`
**Phase gate:** PASS — Phase 3 is merged through `b473792`; visual foundation may proceed, while Task 26 scheduling remains separately scoped

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
| Factory-reset mechanism | Accepted | ADR-013 / OD-13 |
| Solution/component boundaries | Accepted | ADR-014 |
| SQLite ownership/migrations | Accepted | ADR-015 |
| Structured logging/redaction | Accepted | ADR-016 |
| Immutable update jobs | Accepted | ADR-017 |
| Helper IPC/elevation | Accepted | ADR-018 |
| App-owned artifact roots | Accepted | ADR-019 |
| Scheduling without resident process | Accepted | ADR-020 |
| Accessibility best-effort priority | Accepted | ADR-021 |

## Task 25 independent gate

Fresh GPT-5.6 Sol / Extra High review at exact commit `5013c94` closes `P3R-01–15` with no remaining Blocker, High or Medium finding. `P3R-14` uses the official WinGet COM projection as the sole executable candidate authority, bound to predefined trusted source, exact ID, installed version and newer candidate version. Display-table text remains non-executable. Current-tree evidence reaches review, runner and exact post-update verification.

`P3R-15` has production-path evidence for the real default transport/validator: a protected, non-reparse, valid same-signer `%ProgramFiles%\PC Steward` no-op UAC path completed. Actual tamper, trusted-wrong-signer, caller-writable, helper-reparse and between-validation replacement layouts all stopped before the helper marker. A fresh read-only check corroborates that the exact installed root, RunId temporary root and both certificate thumbprints are absent.

The final verdict and residual-risk disposition are in [Phase 3 security review](review/PHASE_3_SECURITY_REVIEW.md). Fresh gates pass: focused 61, security 19, architecture 5, Release build 0 warnings/errors, full suite 182, hostile boundary 42/1 approved skip, WinGet boundary 11, NuGet known-vulnerability count zero, and immutable installed-helper evidence 6/6.

## Residual risks and accepted deferrals

The recorded host substitute and personal-use UI waiver retain their narrow approved scope. Clean-image installer lifecycle, a new real update/live external COM query, per-user no-prompt behavior, provider-disconnect cancellation, unconstrained hostile races, same-signer rollback, same-user substitution of the name-based temporary export, imperfect package-ID/process-name correlation and the un-replayed outcome dialog remain accepted residual risks. ADR-002, ADR-003 and ADR-011 remain Provisional. `AC-UX-01–09` are Optional under ADR-021.

## Next task

Continue Task 29: accessibility, reliability, and performance hardening (GPT-5.6 Terra / High). The five-minute Release-desktop capture reports 4.9883% average CPU against the <=1% `NFR-PERF-05` budget; peak working set is 240.53 MiB and disk I/O is zero, but process-attributed network is unverified. A second Windows performance-counter attempt could not resolve the desktop instance. See `docs/failures/29-idle-resource-gate.md` and `docs/review/PERFORMANCE_AND_ACCESSIBILITY_EVIDENCE.md`; do not begin Task 30 until Task 29 has a diagnosed, rerun idle pass.

## Rules for updating this file

Keep it below 120 lines. Record only current state, unresolved blockers, last green commit, and next task. Historical detail belongs in Git, ADRs, review reports, or failure reports.
