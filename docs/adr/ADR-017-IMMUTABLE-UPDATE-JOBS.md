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

## 2026-10-08 v0.3 revision — operation kinds and batches (D-007)

**Related requirements:** `FR-INS-01/02/05`, `FR-BAT-04–07`, `FR-UNI-01–03` (optional), `AC-V3-05/06`

- **Operation kinds.** The immutable job generalises to a `PackageOperation` with a kind: `Upgrade` (today's job), `Install`, and optionally `Uninstall`.
  - Install has no installed version. Its preconditions are no matching installed instance, a unique exact live resolution and a recorded agreement acceptance where one is required.
  - The canonical text and digest include the kind and the agreement-acceptance hash.
  - Existing `approved_update_jobs` rows stay readable as `Upgrade` operations.
- **Batches.** Confirmation creates one `OperationBatch`:
  - ordered items, with a digest over the canonical text of every item in order;
  - at most 50 items;
  - expiry 30 minutes after approval, after which it must be reviewed again;
  - no additions after approval.
- **Revalidation and ordering.**
  - Every item is still revalidated immediately before it starts. A changed invariant skips that item as non-actionable. It is never adapted silently.
  - User-scope items run first, then machine-scope items in one helper session (ADR-018 revision).
  - Cancellation prevents the next unstarted item.
- **Verification.** Install Success needs both the provider's exact listing at the target version and a matching local re-inventory instance. Uninstall Success, if built, needs both to show absence.

### Task 48 implementation note (2026-10-09)

- **Naming.** The `PackageOperation` is the existing `ImmutableUpdateJob`/`UpdateJobSelection`, kept to avoid churn. `UpdateJobSelection` gains:
  - `Operation` (`Upgrade` or `Install`);
  - `AgreementHash`, WinGet only, upper-case SHA-256;
  - `RunsElevated`, which is true for machine scope and for installers that need or raise elevation.

  An Install has a null `InstalledVersion`, and a Microsoft Store install is refused.
- **Digest compatibility.** The first ten canonical lines are unchanged, so persisted v0.2 Upgrade digests stay valid. Any v0.3 evidence appends labelled `operation=`, `agreement=` and `elevated=` lines.
- **Legacy path.** Only `IsPlainUpgrade` jobs may use the v0.2 runners, the upgrade argv builders or helper protocol v1. Everything else waits for the Task 49 batch path.
- **Batches.** `OperationBatchReview.Confirm` creates `ApprovedOperationBatch`:
  - 1–50 items, unique by product key and by provider package;
  - every job bound to the batch correlation and approval time;
  - unelevated items first, then elevated items;
  - a digest chaining each index, job ID and job digest;
  - usable from 15 s before approval until 30 minutes after it.
- **Persistence.** Migration 10 rebuilds `approved_update_jobs` transactionally (nullable installed version; `operation`, `agreement_hash`, `runs_elevated`). It adds `operation_batches`, `operation_batch_items` and `agreement_acceptances`. Reads recheck membership, order, every job digest and the batch digest.
- **Agreements.** `OperationBatchApprovalService` refuses to confirm an item whose agreement hash has no recorded acceptance for that package and version. Acceptances are cleared with mappings and policies.
