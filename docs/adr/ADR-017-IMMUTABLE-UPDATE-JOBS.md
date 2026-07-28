# ADR-017 — Immutable update jobs

**Status:** Accepted
**Date:** 2026-07-28
**Decision owners:** Implementation reviewer and security reviewer
**Related requirements:** `FR-UPD-01–11`, `AC-UPD-01–11`, `TM-03`, `TM-07`, `TM-13`

## Context

The operation reviewed by the user must be the operation executed after provider, database and privilege boundaries are crossed.

## Options considered

### Option A — Immutable approved job plus append-only events

Identity and compatibility evidence are frozen at confirmation; later state is recorded as events and revalidated before execution.

### Option B — Mutable queue row used as the execution request

This is simple but permits local state or later code to substitute a package, version or command after review.

## Decision

Accept Option A. Confirmation creates an immutable job containing only the job ID, target stable identity, provider enum, exact source ID, native package ID, requested version, architecture/scope/channel evidence, mapping evidence version and approval timestamp. It contains no executable path, URL, command string, working directory, environment, output path or free-form flags.

The persisted canonical job and digest provide mutation detection but not trust. Before starting, trusted code revalidates job state, current mapping, source/provider allowlists, freshness, installed version and compatibility, then constructs the fixed provider operation from code. A changed or stale invariant makes the job non-actionable.

Progress and outcomes are append-only events under an explicit state machine. Provider exit is evidence, not Success; targeted inventory verification or a reliable restart-pending result is required. Batch ordering is sequential and cancellation prevents the next unstarted job.

Option B is rejected because SQLite is untrusted and mutable rows cannot preserve the reviewed operation.

## Consequences

### Positive

Review-to-execution substitution is fail-closed and job history remains auditable.

### Negative / trade-offs

A stale approved job may require user review again rather than silently adapting.

### Follow-up controls and tests

Tasks 21/23/25 test field mutation, tampered database rows, stale mappings, source/package substitution, invalid transitions, replay, crash recovery, cancellation, exit-zero/no-version-change and restart-pending semantics.

## Reconsider when

A provider cannot target the immutable identifiers without display-name or remote-command authority; that provider remains non-actionable until a new ADR proves an equally strict binding.
