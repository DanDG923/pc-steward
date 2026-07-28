# ADR-011 — Download-cache quota and retention

**Status:** Provisional
**Date:** 2026-07-28
**Decision owners:** Product owner and implementation reviewer
**Related requirements:** `FR-DAT-08–11`, `NFR-PERF-10`, `AC-NFR-10`
**Related open decision:** `OD-11`

## Context

App-owned artifacts need bounded storage without deleting active work, provider caches or useful retry evidence.

## Options considered

### Option A — Fixed 2 GB / 7-day success / 24-hour failure defaults

Matches the specification and is simple.

### Option B — Size-informed defaults derived from fixture installers

May better fit real packages but requires representative evidence.

## Decision

Provisionally select Option A: a configurable 2 GB soft quota, seven-day retention after verified success and 24-hour retention for failed/incomplete artifacts. Oldest inactive app-owned artifacts are eligible first; active artifacts are exempt and provider-owned caches are out of scope.

Option B is rejected for the current baseline because Task 04 intentionally used synthetic records and collected no representative installer-size distribution.

## Consequences

Large packages may exceed the soft quota; active artifacts are always exempt.

### Follow-up controls and tests

Oldest inactive eviction, visible size, active lease, locked residuals and provider-cache exclusion.

## Evidence the upcoming work must collect

- Task 04 representative synthetic size scenarios and harness capability.
- Task 24 real provider ownership/retention behavior and quota tests.
- Task 29 beta-candidate storage measurements.

## Task 03 evidence

`spikes/security-boundaries/SPIKE_REPORT.md` proves generic fixed-root deletion can return accurate locked-file residuals, retry safely and remain idempotent. It does not test quota, retention order, active leases, provider ownership or representative artifact sizes; those limitations make this ADR Provisional.

## Reconsider when

Task 24 cannot prove active-artifact protection/provider-cache exclusion, or representative installer sizes in Tasks 24/29 cause frequent churn, blocked retries or excessive retention.
