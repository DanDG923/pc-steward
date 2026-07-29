# PC Steward build status

**Current phase:** Phase 3 — Safe update execution and app-owned cache
**Current task:** 25 — repeated Phase 3 security review complete; gate failed
**Last green tree:** `30ba65a` — focused 56, security 19, architecture 4, Release build 0 warnings/errors, full suite 176, hostile 42/1 skip, WinGet boundary 11
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
| WinGet integration route | Provisional — display route non-actionable without structured authority | ADR-003 / OD-03 |
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

## Task 25 repeated security-review result

`P3R-01–13` are closed. The latest code removes table-derived candidate execution authority, stops a newly detected final running target before queue/provider execution, and checks the fixed helper trust boundary twice before `runas`. Fresh focused/security/architecture/Release/full-suite and hostile validations all pass.

The gate still fails. `P3R-14` is security-treated but its mandatory gate evidence remains open: the actual desktop supplies no `IStructuredUpdateCandidateAuthority`, so no current production update can reach review/execution and the prior Firefox exercise cannot prove the current tree's `AC-UPD-06` path. `P3R-15` remains an evidence Blocker: its tests inject trust booleans/validators instead of exercising real wrong-signer, reparse, caller-writable and replacement rejection through the production validator, and no clean signed protected `%ProgramFiles%\PC Steward` positive/UAC path exists.

Fresh read-only inspection found the fixed installed root absent and both current Release executables unsigned; the development output is caller-writable. Production therefore fails before UAC. See the [Task 25 review](review/PHASE_3_SECURITY_REVIEW.md) and [Task 90 handoff](review/TASK_90_P3R7_EVIDENCE.md).

## Residual risks and accepted deferrals

The recorded host substitute and personal-use UI waiver retain their narrow approved scope. Per-user no-prompt behavior, provider-disconnect cancellation, unconstrained hostile races, same-signer rollback, same-user substitution of the name-based temporary export, imperfect package-ID/process-name correlation and the un-replayed outcome dialog remain residual risks. They do not waive the current-production candidate-authority or protected signed pre-launch evidence blockers. ADR-002, ADR-003 and ADR-011 remain Provisional. `AC-UX-01–09` are Optional under ADR-021.

## Next task

Run Task 90 to close the `P3R-14` mandatory production-path evidence and `P3R-15` trust-boundary evidence blocker, then repeat Task 25 under fresh GPT-5.6 Sol / Extra High. Do not merge Phase 3 or begin Task 26.

## Rules for updating this file

Keep it below 120 lines. Record only current state, unresolved blockers, last green commit, and next task. Historical detail belongs in Git, ADRs, review reports, or failure reports.
