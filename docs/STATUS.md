# PC Steward build status

**Current phase:** Phase 4 — final audit complete; release is NOT READY

**Current task:** Task 31 complete; next is Task 90 under GPT-5.6 Terra / High

**Audited candidate:** `632b7afb6fb97aa8597ffe8a66ef6e3599396b3f`

**Last green tree:** audited candidate — Release build 0 warnings/errors; full suite 237 passed

**Current branch:** `phase/4-beta`

**Build command:** `pwsh -NoProfile -File .\scripts\Build.ps1`

**Test command:** `pwsh -NoProfile -File .\scripts\Test.ps1`

**Performance command:** `dotnet run --project .\tests\PCSteward.Performance\PCSteward.Performance.csproj --configuration Release --no-build`

**Phase gate:** FAIL — 5 High, 1 Medium, and 1 Low finding remain open

## Fresh Task 31 verdict

The independent final audit is **NOT READY**. Mandatory acceptance is 45 Proven / 4 Failed / 5 Unverified; mandatory NFR status is 19 Proven / 0 Failed / 6 Unverified.

- `FRA-11` High — factory reset ignores an unexpected object directly below the owned state root and can report Success/onboarding while it remains.
- `FRA-12` High — packaged task cleanup treats every failed exact-task query as absence and does not verify absence after deletion.
- `FRA-13` Medium — the durable reset marker omits the accepted bounded identity/attempt/category/progress record.
- `FRA-14` High — final-app 30/20 timing, update responsiveness, idle I/O/network, scan process tree, and scheduled-timeout evidence remain incomplete.
- `FRA-15` High — mandatory destructive reset, replacement/removal, residual, and final-timeout lifecycle observations remain incomplete.
- `FRA-16` High — no approved release signer, trusted channel, recipient/support controls, or distributable rollback artifact exists.
- `FRA-17` Low — release-facing known limitations contain stale update/performance-exception wording.

See [final release audit](review/FINAL_RELEASE_AUDIT.md) for exact file/line evidence, remediation, verification, criterion grading, and residual risks.

## Decisions

| Decision | Status | ADR |
|---|---|---|
| Product name/identity | Accepted | ADR-001 / OD-01 |
| Packaging model | Accepted — standalone standard-user-only ZIP | ADR-002 / OD-02 |
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

## Verification snapshot

Fresh Task 31 checks: focused safety 164/164; hostile boundary 42 assertions with one approved manual symbolic-link skip; WinGet boundary 11; Release build 0 warnings/errors; full suite 237/237 (55 Domain, 145 Application integration, 17 Architecture, 20 Security); zero known vulnerable NuGet packages; zero tracked secret candidates. The package validator passes 94 files, 15 components, 16 SBOM nodes/32 edges, licences/notices, and exact SHA-256 `20617AEFCFE42822534060CC7902BECFF5911D5B27C5F2274449A84935272FBF`.

The archive remains unsigned and local-owner-only. Do not release, tag, publish, upload, share, or distribute it.

## Residual risks and accepted deferrals

Task 90 exceptions do not convert omitted evidence to Proven and expire before a release-ready or external-distribution claim. Provider-disconnect cancellation, same-user temporary-export substitution, imperfect package-ID/process-name correlation, and the historical outcome-dialog crash remain bounded risks. The standalone payload contains/composes no elevated helper; protected-helper evidence is historical/future-installed-mode only. ADR-003 and ADR-011 remain Provisional. `AC-UX-01–09` remain Optional under ADR-021.

## Next task

Stop here: Task 90 requires GPT-5.6 Terra / High, different from the current settings. After that settings change, remediate `FRA-11–13` first, using independent agents only for isolated non-overlapping changes per product-owner direction; integrate and re-test centrally. Then resolve or collect fresh evidence for `FRA-14–17`, rebuild/re-hash, run focused/host/package/build/full gates, commit only if green, and repeat Task 31 under GPT-5.6 Sol / Extra High.

## Rules for updating this file

Keep it below 120 lines. Record only current state, unresolved blockers, last green commit, and next task. Historical detail belongs in Git, ADRs, review reports, or failure reports.
