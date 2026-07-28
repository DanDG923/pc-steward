# ADR-020 — Scheduling without a resident process

**Status:** Accepted
**Date:** 2026-07-28
**Decision owners:** Implementation reviewer
**Related requirements:** `FR-SCH-01–05`, `FR-RSC-04`, `NFR-PERF-07`, `AC-NFR-11`

## Context

Optional checks must run without a service or tray coordinator and must never become unattended update execution.

## Options considered

### Option A — Current-user Task Scheduler task invoking a scan-only host

Windows owns wake-up; a capability-minimal process acquires the normal scan lease, checks and exits.

### Option B — Resident tray process, service or background timer

This makes scheduling easy but violates explicit resource, consent and least-privilege boundaries.

## Decision

Accept Option A. With explicit consent, `PCSteward.Scheduling.Windows` creates one typed current-user task whose action is the fixed installed `PCSteward.ScheduledHost` path with no update verb or arbitrary arguments. The task stores no password, requests no elevation and is created/updated/deleted idempotently under a stable technical name.

ScheduledHost references scan and notification capabilities but not Execution or ElevatedHelper. It reads validated schedule/source settings, acquires the same cross-process single-scan lease, uses bounded provider timeouts and connector concurrency, requests BelowNormal priority per ADR-012, emits only attention/repeated-failure notifications and exits after completion, cancellation or final timeout handling. Closing the desktop leaves no coordinator.

Option B is rejected because idle recurring work and ambient capability violate the no-resident-process requirement.

## Consequences

### Positive

Scheduling is resource-light, standard-user and structurally unable to install updates.

### Negative / trade-offs

Task Scheduler behavior varies with edition, power and user-session conditions and needs clean-VM tests.

### Follow-up controls and tests

Task 26 proves task XML/action allowlists, current-user context, consent, daily/weekly/manual settings, idempotent create/update/delete, no execution references, overlap handling, notifications, process priority and exit within 30 seconds. Uninstall and factory reset must remove the task or report a partial result.

## Reconsider when

Supported Windows 11 editions cannot reliably run the current-user task. Any replacement must remain scan-only, non-elevated and non-resident.
