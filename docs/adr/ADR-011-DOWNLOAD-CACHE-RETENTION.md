# ADR-011 — Download-cache quota and retention

**Status:** Proposed
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

Use Option A provisionally; Task 24 must keep values configurable and Task 29 validates them. Status remains Proposed.

## Consequences

Large packages may exceed the soft quota; active artifacts are always exempt.

### Follow-up controls and tests

Oldest inactive eviction, visible size, active lease, locked residuals and provider-cache exclusion.

## Evidence the upcoming work must collect

- Task 04 representative synthetic size scenarios and harness capability.
- Task 24 real provider ownership/retention behavior and quota tests.
- Task 29 beta-candidate storage measurements.

## Reconsider when

Representative installer sizes cause frequent churn, blocked retries or excessive retention.
