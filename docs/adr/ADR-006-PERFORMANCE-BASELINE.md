# ADR-006 — Performance baseline

**Status:** Proposed
**Date:** 2026-07-28
**Decision owners:** Product owner and implementation reviewer
**Related requirements:** `NFR-PERF-01–10`, `AC-NFR-01–11`
**Related open decision:** `OD-06`

## Context

Budgets need a repeatable Windows 11 x64 baseline and process-tree measurements.

## Options considered

### Option A — Product-owner PC only

Realistic but difficult to reproduce and may be overpowered.

### Option B — Fixed mid-range VM plus exploratory real PC

Repeatable gate with separate real-world evidence.

## Decision

Propose Option B: at least 4 vCPU, 8 GB RAM, SSD-equivalent storage and fixed power plan, with owner-PC results reported separately. Status remains Proposed.

## Consequences

VM variance must be recorded; final claims are comparable.

### Follow-up controls and tests

Raw results include environment/build IDs, warm-up, every sample, median and p95.

## Evidence the upcoming spike must collect

- Task 04 baseline capture and 300-record harness.
- Process-tree CPU/working set/cancellation/concurrency measurement reliability.
- 30 warmed local and 20 healthy online protocol repeatability.
- Task 29 confirmation on the final application.

## Reconsider when

Virtualization noise prevents stable thresholds or the target-user hardware floor changes.
