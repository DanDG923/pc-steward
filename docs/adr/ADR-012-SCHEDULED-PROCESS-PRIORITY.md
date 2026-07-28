# ADR-012 — Scheduled process priority

**Status:** Proposed
**Date:** 2026-07-28
**Decision owners:** Product owner and implementation reviewer
**Related requirements:** `FR-RSC-04`, `NFR-PERF-07`, `AC-NFR-11`
**Related open decision:** `OD-12`

## Context

Scheduled checks should minimize disruption without a resident coordinator or unreliable self-throttling.

## Options considered

### Option A — BelowNormal process priority plus Task Scheduler conditions

Uses Windows mechanisms and exits after work.

### Option B — Normal priority with application-only throttling

Portable but less explicit and easier to regress.

## Decision

Propose Option A where supported, with documented fallback to bounded Normal priority. Status remains Proposed.

## Consequences

Scheduler behavior varies by edition/power state and needs measurement.

### Follow-up controls and tests

Typed task definitions, process-tree priority capture, final timeout and no-resident-process assertion.

## Evidence the upcoming work must collect

- Task 26 Task Scheduler support across target editions and process priority evidence.
- Task 29 completion-time/resource results under the selected policy.
- Clean-VM create/update/delete/idempotence evidence.

## Reconsider when

BelowNormal causes unacceptable timeout rates or Task Scheduler conditions are not portable.
