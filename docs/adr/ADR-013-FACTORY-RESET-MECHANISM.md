# ADR-013 — Factory-reset mechanism

**Status:** Proposed
**Date:** 2026-07-28
**Decision owners:** Product owner and security reviewer
**Related requirements:** `FR-DAT-12–15`, `AC-DAT-12–16`, section 15.5
**Related open decision:** `OD-13`

## Context

Reset must delete all per-user state and schedule, preserve binaries/provider caches, avoid elevation/arbitrary paths and prove first-launch state despite open handles or locked files.

## Options considered

### Option A — In-process reset then close/restart

Smallest surface, but the process may hold files.

### Option B — Next-launch cleanup marker

Avoids most handles; must prevent old-state reload and accurately retain failure state.

### Option C — Short-lived unelevated cleanup helper

Can run after UI exit but adds protocol/process attack surface.

## Decision

Prefer A, then B if handle evidence requires it; permit C only when Tasks 03/27 prove A/B cannot meet verification. The operation accepts a fixed reset verb/manifest, never paths. Status remains Proposed.

## Consequences

The safest mechanism may require a close and verified next launch; partial reset remains visible.

### Follow-up controls and tests

Double confirmation, active-work lock, deterministic manifest, canonical/reparse-safe deletion, schedule removal, idempotence, locked-file residuals and onboarding verification.

## Evidence the upcoming spike must collect

- Task 03 disposable-root path/reparse/TOCTOU and no-op helper protocol evidence.
- Task 27 actual SQLite/log/settings/scheduler handle behavior and ordering.
- Task 28 hostile integration and next-launch verification.

## Task 03 evidence

`spikes/security-boundaries/SPIKE_REPORT.md` records 42 passing assertions for a ten-category fixed reset manifest, canonical containment, hostile path rejection, junction detection, locked-file partial results, bounded retry, idempotence and a final-component reparse swap. It also prototypes a strict authenticated no-op helper request. Symbolic-link and volume-mount fixtures plus an ancestor-component race remain clean-VM/manual evidence, so Option A then B remains the preference, C remains exceptional, and this ADR stays Proposed.

## Reconsider when

Actual handle lifetime or packaging prevents complete standard-user cleanup with the preferred mechanism.
