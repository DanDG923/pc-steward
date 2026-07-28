# Task 05 — Architecture decision review

**Date:** 2026-07-28
**Outcome:** PASS WITH FOUR PROVISIONAL DECISIONS
**Scope:** Phase 0 architecture only; no production implementation claim

## Evidence reviewed

- Phase 0 architecture and test-strategy documents.
- Task 02 WinGet/packaging report and 11-assertion non-executing planner test.
- Task 03 privilege/deletion report and 42-assertion disposable-root test, with one VM/manual skip category.
- Task 04 320-record fixture, 14-assertion fixture test, baseline/protocol and raw 30-local/20-simulated-online aggregate.
- Product specification sections 12–15 and 18–24.

Task 04 measured local p95 584.68 ms, simulated-online p95 1,764.93 ms, cancellation acknowledgement 503.54 ms and connector concurrency 3. These validate the harness, not final application acceptance.

## Decision disposition

| ADR | Decision | Status |
|---|---|---|
| 001 | Stable `PCSteward` technical identity; replaceable display brand | Accepted |
| 002 | Signed unpackaged app and traditional per-user installer | Provisional |
| 003 | Capability-gated constrained WinGet CLI adapter | Provisional |
| 004 | Exclude GitHub Releases from core MVP | Accepted |
| 005 | Conservative identity-first runtime grouping | Accepted |
| 006 | Fixed mid-range clean Windows 11 VM baseline | Accepted |
| 007 | Weekly schedule offered but disabled until consent | Accepted |
| 008 | Private repository through private beta | Accepted |
| 009 | Manual signed PC Steward upgrades; no self-updater | Accepted |
| 010 | Diagnostic paths redacted by default | Accepted |
| 011 | 2 GB / 7-day success / 24-hour failure cache defaults | Provisional |
| 012 | BelowNormal scheduled host with bounded Normal fallback | Accepted |
| 013 | Verified next-launch factory-reset marker | Provisional |
| 014 | Layered capability-specific projects and minimal hosts | Accepted |
| 015 | Persistence-owned SQLite and transactional migrations | Accepted |
| 016 | Allowlisted structured logs sanitized before persistence | Accepted |
| 017 | Immutable approved jobs with append-only events | Accepted |
| 018 | One-shot signed helper over identity-checked named pipe | Accepted |
| 019 | Compiled categories beneath fixed per-user artifact root | Accepted |
| 020 | Current-user Task Scheduler invoking scan-only host | Accepted |

## Key provisional decisions

| Decision | Rejected alternative and evidence | Mandatory tests | Reconsideration trigger |
|---|---|---|---|
| OD-02 / ADR-002 | MSIX is not selected because no target-VM evidence proves helper, scheduler and uninstall data-choice compatibility. Task 02 only supports unpackaged feasibility by boundary fit. | Clean-VM install/launch/helper/UAC/schedule/upgrade/rollback/uninstall retain/remove-data and signing tests. | MSIX proves the full lifecycle, or the selected installer fails any mandatory lifecycle case. |
| OD-03 / ADR-003 | A programmatic route is not selected because Task 02 found none to prove. WinGet 1.29.280 help supports exact/source/version/architecture/scope arguments but exposes no structured list/upgrade output. | Capability, locale/malformed parser, bounded output, source isolation, timeout/process-tree cancellation and exact-ID argument rejection tests. | A supported API proves equivalence, CLI capabilities disappear, or compatibility fixtures fail. |
| OD-11 / ADR-011 | Size-derived defaults are not selected because Task 04 contains no representative installer-size distribution. Task 03 proves only deletion primitives. | Quota boundary, oldest inactive eviction, active lease, provider-cache exclusion, locked residual and real-size measurements. | Active protection/provider exclusion fails, or Tasks 24/29 show churn or excessive retention. |
| OD-13 / ADR-013 | In-process reset is rejected for the current path because real app handles do not exist; a helper is rejected as unnecessary extra surface. Task 03 supports fixed manifests and partial results. | Marker-before-open, schedule removal, every manifest category, locked residual, idempotence, restart verification and clean-VM symlink/mount/ancestor-race tests. | Task 27 proves a simpler equally verifiable path, or the selected mechanism cannot complete standard-user cleanup. |

## Accepted performance decision

OD-06 / ADR-006 accepts a clean supported Windows 11 x64 VM with at least 4 vCPU, 8 GB RAM, SSD-equivalent storage and a fixed recorded power plan. Product-owner-PC results remain exploratory. Acceptance still requires 30 warmed local and 20 healthy-online final-application runs, every raw sample, median/p95, full process-tree CPU/memory, UI recovery, idle, cold start and scheduled-exit measurements.

## Contradictions reconciled

- Task 02 reported Windows 10 Home while Task 04 captured Windows 11 Home. Neither is treated as a clean target baseline; the discrepancy remains a clean-VM prerequisite, not evidence for or against packaging.
- “Table parsing” is limited to bounded display/inventory evidence and cannot authorize a job; exact immutable identifiers and execution-time validation are authoritative.
- Factory reset now selects next-launch cleanup rather than “A then B.” The marker is checked before SQLite/logs open, and residuals prevent false onboarding.
- SQLite rows and artifact relative paths are evidence only. Provider operations and deletion roots are reconstructed from trusted code.
- The scheduled path is a separate host with no execution/helper reference, resolving the previous generic “scan-only entry point.”
- The helper transport is fixed to a one-request identity-checked named pipe; the Task 03 HMAC object was protocol-shape evidence, not a production key-establishment decision.

## Scaffold readiness

Task 06 can create projects, dependency checks, contracts and test infrastructure without selecting packaging, execution authority, persistence ownership, logging, IPC, storage roots or scheduling architecture. Provisional ADRs permit scaffolding only; their stated evidence gates still block external distribution or acceptance claims.
