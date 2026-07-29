# PC Steward build status

**Current phase:** Phase 3 — Safe update execution and app-owned cache
**Current task:** 90 — final Phase 3 review remediation complete; fresh Task 25 required
**Last green tree:** Task 90 remediation of `9bc817f` — Release build 0 warnings/errors; full suite 176 passed
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

## Task 90 final remediation result

`P3R-15` is closed in the remediation tree: only the exact `%ProgramFiles%\PC Steward` desktop/helper pair can reach `runas`, after repeated pre-launch non-reparse, current-token write/delete/DACL/owner, Authenticode and exact-signer checks. Post-connect identity validation remains. Negative trust/replacement fixtures prove zero launch calls.

`P3R-14` is closed by an exact bounded structured-candidate authority gate. WinGet display tables never implement that authority, and the production desktop remains non-actionable because no supported structured available-version provider is evidenced. `P3R-13` is closed: a target first detected at the final pre-provider snapshot stops before queue/provider execution and requires a fresh target-specific review/acknowledgement.

Focused 56, security 19, architecture 4, Release build 0 warnings/errors, full suite 176, hostile deletion/IPC 42 with one elevated-only skip and WinGet boundary 11 all pass. Safe local inspection found the fixed installed root absent and current Release binaries unsigned/caller-writable, so the new production gate rejects them before UAC. See [Task 90 final evidence](review/TASK_90_P3R7_EVIDENCE.md).

## Residual risks and accepted deferrals

The recorded host substitute and personal-use UI waiver retain their narrow approved scope. A clean signed `%ProgramFiles%\PC Steward` installed-path/UAC replay was not available; machine-scope execution therefore remains fail-closed on this host. Clean-image behavior, per-user no-prompt behavior, provider-disconnect cancellation, unconstrained hostile races, same-signer rollback, same-user substitution of the name-based temporary export, imperfect package-ID/process-name correlation and the un-replayed outcome dialog remain residual risks. ADR-002, ADR-003 and ADR-011 remain Provisional. `AC-UX-01–09` are Optional under ADR-021.

## Next task

Repeat Task 25 under fresh GPT-5.6 Sol / Extra High. Do not merge Phase 3 or begin Task 26.

## Rules for updating this file

Keep it below 120 lines. Record only current state, unresolved blockers, last green commit, and next task. Historical detail belongs in Git, ADRs, review reports, or failure reports.
