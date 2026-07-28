# ADR-013 — Factory-reset mechanism

**Status:** Provisional
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

Provisionally select Option B: a next-launch cleanup marker under the fixed PC Steward state root. After two confirmations and an exclusive data-operation lease, the UI writes the fixed marker and exits without reopening application state. The next launch checks the marker before opening SQLite or logs, removes the current-user scheduled task and each compiled reset-manifest category, then deletes the marker only after verification. Any residual keeps the reset state visible and prevents old state from loading.

Option A is rejected for the current path because actual application handles do not yet exist and an in-process reset could retain its own database/log handles. Option C is rejected because the spike proved only a protocol shape and a helper adds an unnecessary process boundary while next-launch cleanup remains implementable. The reset operation accepts no paths.

## Consequences

Reset requires closing the UI and a verified next launch; partial reset remains visible and onboarding is not shown until verification succeeds.

### Follow-up controls and tests

Double confirmation, active-work lock, deterministic manifest, canonical/reparse-safe deletion, schedule removal, idempotence, locked-file residuals and onboarding verification.

## Evidence the upcoming spike must collect

- Task 03 disposable-root path/reparse/TOCTOU and no-op helper protocol evidence.
- Task 27 actual SQLite/log/settings/scheduler handle behavior and ordering.
- Task 28 hostile integration and next-launch verification.

## Task 03 evidence

`spikes/security-boundaries/SPIKE_REPORT.md` records 42 passing assertions for a ten-category fixed reset manifest, canonical containment, hostile path rejection, junction detection, locked-file partial results, bounded retry, idempotence and a final-component reparse swap. It also prototypes a strict authenticated no-op helper request. Symbolic-link and volume-mount fixtures plus an ancestor-component race remain isolated-environment/manual evidence; those limitations make the next-launch choice Provisional.

## Reconsider when

Task 27 proves in-process cleanup is equally verifiable and materially simpler; isolated-environment reparse/ancestor-race evidence fails; actual handle lifetime or packaging prevents complete standard-user next-launch cleanup; or schedule removal cannot be completed and verified in the current-user context.
