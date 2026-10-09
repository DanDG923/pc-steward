# ADR-025 — Opt-in automatic updates (Stage A and Stage B)

**Status:** Accepted. Stage B implementation starts only after about two weeks of clean Stage A use.
**Date:** 2026-10-08
**Decision owners:** Product owner and security reviewer
**Related requirements:** `FR-AUT-01–10`, `FR-UPD-12`, `FR-SCH-03`, `FR-BAT-08`, `AC-V3-08/09/10`
**Related decisions:** D-006, D-007, Q-005; amends ADR-020

## Context

Patch My PC–style use means apps update themselves without the owner opening PC Steward. Admin-scope apps need elevation, and an unattended elevated run is ambient privileged execution. PC Steward's per-user SQLite database is writable by any process running as the owner, so it must never decide what an elevated run installs.

## Decision drivers

- Unattended updates for the owner's opted-in apps
- No path from a user-writable store to admin-level installation
- No resident service and no stored password
- The same verification and history as manual runs

## Options considered

### Option A — Stage A user-scope host, then Stage B elevated task with an admin-protected policy store

### Option B — Stage A only, with a one-click approval toast for admin-scope items

This is the safest option, but admin-scope apps never update unattended, which falls short of the requested Patch-style experience.

### Option C — Elevated task that reads the per-user database

This is simple, but malware running as the owner could then edit the database and obtain admin-level installs without a UAC prompt. Rejected.

## Decision

Accept Option A.

- **Eligibility.** `AutomationEligibility` is a pure domain predicate, exhaustively unit-tested, with the conditions in `FR-AUT-02`. Both stages use it.
- **Stage A.** A separate current-user Task Scheduler task runs `PCSteward.AutomaticUpdateHost` with no arguments, after a successful scheduled check and inside the optional maintenance window. It skips on battery or a metered connection.
  - It executes only user-scope WinGet items, up to the per-run cap, through the normal immutable-operation and verification path. It never elevates.
  - The project has no reference to `PCSteward.ElevatedHelper` or `PCSteward.Execution` elevated transport, enforced by an architecture test.
  - Eligible machine-scope items produce one notification that opens a pre-selected review.
- **Stage B.** When the owner enables it through a UAC-approved helper operation:
  - The helper writes a policy (opted-in catalog IDs, per-run cap, window, Chocolatey per-app opt-ins) to `%ProgramData%\PC Steward\automation-policy.json`. The file's ACL allows writes by Administrators/SYSTEM only.
  - The helper also creates a Task Scheduler task: run as the owner, highest privileges, only while the owner is signed in, no stored password, fixed installed executable path, no arguments.
  - The elevated host trusts only that store, the bundled catalog (ADR-023) and live provider evidence. It never opens the per-user database.
  - A per-user pause marker is honoured only as a restriction.
  - The host revalidates the installed layout as ADR-018 does, runs eligible items sequentially with verification, writes results to a bounded results file in the protected store, and exits. The desktop imports those results into history on the next launch.
  - Disabling Stage B or running factory reset removes the task and the store through the helper. If UAC is declined, the reset reports a partial result.
- **No forced closes.** Automation never closes or force-terminates an app. A running app defers its update.

## Consequences

### Positive

Opted-in apps stay current unattended. A compromised user process cannot widen what is installed with administrator rights.

### Negative / trade-offs

Stage B adds a scheduled elevated entry point and a second policy store that must be kept in sync through the helper. Results reach the desktop history only on its next launch.

### Follow-up controls and tests

- Task 54 adds predicate truth-table tests, Stage A host isolation (architecture test), battery/metered/window and pause behaviour.
- Task 55 adds:
  - store ACL and reparse tests;
  - proof that tampering with the per-user database does not change Stage B behaviour;
  - pause-marker restriction-only tests;
  - task definition allowlist tests: no arguments, fixed path, no password;
  - reset removal and partial reporting.
- Task 58 runs a hostile pass over both hosts.

## Reconsider when

Stage A shows failures or unwanted updates, the owner no longer wants unattended admin installs, or Windows changes Task Scheduler elevation semantics.
