# PC Steward build status

**Current phase:** Phase 4 — Task 90 implementation remediation complete; release remains NOT READY

**Current task:** Task 90 complete; next is a fresh Task 31 under GPT-5.6 Sol / Extra High

**Remediated tree:** Task 90 commit (this commit), based on audited candidate `632b7afb6fb97aa8597ffe8a66ef6e3599396b3f`

**Last green tree:** remediated tree — Release build 0 warnings/errors; full suite 246 passed

**Current branch:** `phase/4-beta`

**Build command:** `pwsh -NoProfile -File .\scripts\Build.ps1`

**Test command:** `pwsh -NoProfile -File .\scripts\Test.ps1`

**Performance command:** `dotnet run --project .\tests\PCSteward.Performance\PCSteward.Performance.csproj --configuration Release --no-build`

**Phase gate:** FAIL — fresh Task 31 required; `FRA-14–16` still require field/external evidence

## Task 90 outcome

- `FRA-11` implementation remediated — exact handle-bound root/control manifest, pre-migration onboarding validation, `UnexpectedOwnedObject`, durable Partial, late-race recheck and authority restoration pass independent review.
- `FRA-12` implementation remediated — packaged exact COM query/delete/re-query fails closed; an independent review caught and closed the initial setup-HRESULT false-absence path.
- `FRA-13` implementation remediated — bounded identity/time/attempt/state/category marker, exact attempt-cap recovery, and atomic progress/retry pass independent review.
- `FRA-17` release text remediated — narrow VS Code evidence and current blocker/exception boundaries are consistent.
- `FRA-14` remains High — final-app/live/idle/process-tree/update-responsiveness/final-timeout evidence needs separately authorized host actions.
- `FRA-15` remains High — destructive reset, real task/removal/replacement and final-timeout lifecycle evidence needs separately authorized actions.
- `FRA-16` remains High — no approved signer, channel, recipients, support contact, or rollback artifact exists.

The original [final release audit](review/FINAL_RELEASE_AUDIT.md) remains **NOT READY** until a fresh Task 31 re-grades this tree.

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

Reset 25/25; cleanup contract Pass; Security 20/20; Architecture 17/17; hostile boundary 42 assertions with one approved manual symbolic-link skip; WinGet boundary 11; Release build 0 warnings/errors; serialized full suite 249/249 (55 Domain, 157 Application integration, 17 Architecture, 20 Security). Independent Terra / High re-reviews end with no finding in the corrected reset or cleanup scopes; a concurrent-run failure was traced to the intentional global mutex, then fixture teardown/isolation was hardened before the green serialized rerun.

The mechanically validated unsigned local archive has 94 files / 49,950,826 bytes, 15 components, 16 SBOM nodes/32 edges, licences/notices, matching reviewed/package cleanup script SHA-256, and archive SHA-256 `A8FD4AF124019E44B23C9F067C49F49A3D61FE8025F6ED9D8AF5E51E4CBCA3FC`. Fixture performance was 30 local median 1.85 ms / p95 2.03 ms and 20 simulated-online median 15.51 ms / p95 15.96 ms; it is not acceptance evidence.

The prior `PO-EXC-90-*` approvals were bound to `20617AEF...72FBF` and do not apply to the rebuilt hash. No real scheduled task, owner data, application version, signer, channel, recipient, or external system was changed. Do not release, tag, publish, upload, share, or distribute the archive.

## Next task

Stop here. Run Task 31 in a fresh GPT-5.6 Sol / Extra High review of the Task 90 commit and exact `A8FD4AF1...4CBCA3FC` archive. A fresh audit must retain `FRA-14–16` unless the missing physical-host/destructive/signing/channel evidence is separately authorized and collected.

## Rules for updating this file

Keep it below 120 lines. Record only current state, unresolved blockers, last green commit, and next task. Historical detail belongs in Git, ADRs, review reports, or failure reports.
