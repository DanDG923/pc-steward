# ADR-007 — Default schedule

**Status:** Accepted
**Date:** 2026-07-28
**Decision owners:** Product owner and implementation reviewer
**Related requirements:** `FR-SCH-01–05`, `FR-RSC-04`, `AC-NFR-11`
**Related open decision:** `OD-07`

## Context

Scheduling must provide awareness without surprise network activity, installation or resident processes.

## Options considered

### Option A — Manual-only default

Maximum consent; users may miss checks.

### Option B — Weekly schedule offered during onboarding, disabled until consent

Good awareness while preserving explicit opt-in.

## Decision

Accept Option B. Offer a weekly schedule during onboarding, but create no task until explicit consent. Scheduled work is scan-only, uses the dedicated scheduled host and never enables update execution.

## Consequences

Onboarding needs clear network/notification choices and independent toggles.

### Follow-up controls and tests

Task-definition, idempotence, no-install, overlap and process-exit tests.

## Evidence the upcoming work must collect

- Task 13 onboarding comprehension/accessibility evidence.
- Task 26 current-user scheduler behavior and disable/remove reliability.
- Task 29 idle/scheduled resource measurements.

## Reconsider when

Beta feedback shows surprise, fatigue or insufficient awareness.
