# ADR-013 — Factory-reset mechanism

**Status:** Accepted
**Date:** 2026-07-29
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

Select Option B: a next-launch cleanup marker at the literal `%LocalAppData%\PCSteward\reset\pending.v1.json`. After two confirmations and an exclusive cross-process data-operation lease, the UI atomically writes and flushes the fixed marker, then exits without reopening application state. Both desktop and scheduled-host startup check marker presence before opening SQLite, logs, settings, providers, or other state. The scheduled host exits while reset is pending. Desktop reset bootstrap removes the exact current-user task and each compiled reset-manifest category, verifies absence, then removes the marker and creates a fresh store. Malformed marker content and every residual fail closed into the blocking partial-reset state.

Option A is rejected even though current SQLite connections are non-pooled and per-operation and current logs are memory-only: desktop startup migrates SQLite before window composition, the scheduled host can independently open the same store, and an in-process delete cannot as simply prove that no old object is reopened. Option C is rejected because a helper adds unnecessary executable/protocol/process authority while next-launch cleanup is implementable without elevation. The reset operation accepts no paths.

The exact manifest, ordering, lease, transaction, failure, verification, UI, hostile-test, and uninstall contracts are accepted in `docs/architecture/FACTORY_RESET_DESIGN.md`.

## Consequences

Reset requires closing the UI and a verified next launch. The marker is the point of no return: failure before its atomic commit changes nothing; failure after it is recovered only by idempotent retry. Partial reset remains visible, old state is never loaded, and onboarding is not shown until verification and fresh-store checks succeed.

### Follow-up controls and tests

Task 28 must prove double confirmation, shared desktop/scheduled-host lease, marker-before-store startup, the deterministic manifest, handle-relative non-following deletion, exact schedule removal, idempotence, crash boundaries, locked-file residuals, hostile link/mount/race fixtures, accurate partial UI and onboarding verification.

## Decision evidence

- Task 03 established the compiled-category, denial, locked-file, idempotence and marker/helper option evidence.
- Tasks 24/25 supplied the production handle-relative deletion primitive and approved isolated symbolic-link, scratch-volume and ancestor-race evidence.
- Task 26A assembled the owned-root, preserve, active-work, scheduler, startup and UI readiness package.
- Task 27 inspected actual connection/handle lifetimes and accepted the exact mechanism and manifest.
- Task 28 remains the implementation and reset-manifest hostile-integration gate.

## Task 03 evidence

`spikes/security-boundaries/SPIKE_REPORT.md` records 42 passing assertions for a ten-category fixed reset manifest, canonical containment, hostile path rejection, junction detection, locked-file partial results, bounded retry, idempotence and a final-component reparse swap. Later approved isolated fixtures exercised file/directory/root symbolic links, a mounted scratch VHD, and deterministic root/ancestor races against the production handle-relative cache primitive without outside deletion. Reset-manifest replay remains a Task 28 implementation gate, not an unresolved mechanism decision.

## Reconsider when

A supported deployment changes the known-folder or scheduled-task ownership model; Task 28 cannot make marker preflight precede all state opening; the shared lease cannot cover the scheduled host and update/provider processes; the production non-following primitive fails reset-manifest hostile fixtures; or standard-user exact-task removal/verification cannot be implemented.
