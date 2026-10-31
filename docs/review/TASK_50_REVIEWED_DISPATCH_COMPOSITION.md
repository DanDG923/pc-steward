# Task 50 — reviewed dispatch composition (application layer)

`FR-UPD-15`, retained `FR-UPD-01/08/09/14`, `TM-03–04/06/07/13`. Cloud continuation by Opus 5.5, 2026-10-31. This record states the producing/consuming interfaces **before** callers change, as AGENTS requires. It composes the existing prerequisites: live review, draft, dispatch evidence, claim CAS, one-use permit, settlement and reconciliation. It adds no native route. No production dispatcher can start a provider. WinGet stays unavailable pending a guarded existing-only path. Chocolatey stays unavailable pending native profile/helper qualification.

## New interfaces (`src/PCSteward.Application/ReviewedUpdateDispatchWorkflow.cs`)

| Type | Producer | Consumer | Meaning |
|---|---|---|---|
| `IReviewedUpdateRouteDispatcher` | Future per-route Execution adapter (non-elevated runner / protocol-2 helper transport) | Workflow | `Supports(operation)` is a closed compiled check of route, guard contract, mode, provider, source and scope. `DispatchAsync(ticket, ct)` returns native observations only. Native runners/helpers must independently revalidate. A positive `Supports` is never eligibility evidence. |
| `ReviewedUpdateDispatchTicket` | Workflow only (internal constructor), issued after `UpdateDispatchPermit.TryConsume` succeeds | Dispatcher | Passive carrier of the exact committed claim and frozen operation. It is not restorable or persisted, and it is not helper request authority by itself. |
| `ReviewedUpdateDispatchObservation` | Dispatcher | Workflow → `UpdateDispatchSettlement` | Invocation `Unknown`/`NotStarted`/`Started`, the provider status, the exit code, a bounded diagnostic, and an immutable owned-root task snapshot with direct-exit/I/O/EOF/termination-failure/uncertainty facts. Unknown invocation is always unsettled. |
| `IReviewedUpdateDispatchEvidenceSource` | Future trusted live preparation | Workflow, under the gate, immediately before claim | Returns `FreshUpdateDispatchEvidence` or null. Data alone grants nothing; admission still compares it with the frozen review. |
| `IReviewedUpdateVerifier` | Future post-update re-inventory | Workflow, under the gate, only after positive settlement | Returns `PostUpdateInventoryResult` for the frozen operation. |
| `ReviewedUpdateDispatchResult` | Workflow | Desktop/history (later) | Disposition `NotStarted`, `NotStartedClaimRetained`, `Finalized` or `Uncertain`, plus the recorded outcome, message, frozen mode, guard contract and fixed disclosure text. |

`UpdateDispatchOutcome` gains the appended value **`Unverified`**. It means owned work was positively settled, but post-verification proved neither the target version nor a reliable restart-pending state. It is Final, never Success, and existing values keep their meaning.

The gate runner is supplied as a delegate: `(work, reviewDeadlineUtc, ct) => Task<ReviewedUpdateDispatchResult>`. Production passes `CurrentUserDataOperationLease.RunAsync` with `UpdateDispatchReconciliation.ReconcileAsync`. Application therefore keeps no Storage reference, and every acquisition reconciles.

## Ordering (unchanged contract, now composed)

1. Outside the gate: the approval must be current. The dispatcher's closed `Supports` and `ReviewedUpdateReview.HasSupportedMode` must both admit the frozen operation, otherwise NotStarted with no claim attempt. Nothing is held across the UI review wait.
2. Gate acquisition reconciles unresolved claims, and the review deadline (`ReviewedAt` + 30 min) bounds the wait.
3. Under the gate: fresh dispatch evidence, then a one-use claim attempt and durable commit (`UpdateDispatchAdmission`). A null permit with a committed claim is reported as `NotStartedClaimRetained`: the barrier stays until reconciliation, with no retry.
4. `permit.TryConsume` rechecks approval currency, ownership and evidence freshness immediately before launch. If it fails, the claim is finalized `Cancelled` with a NotInvoked settlement.
5. The dispatcher runs once, under the same ownership. An exception or cancellation without positive facts is an Unknown invocation, which means Uncertain.
6. Settlement comes from the observation (`BeforeInvocation` only for positively proven NotStarted). Unsettled facts record `Uncertain` and keep the barrier. The owned root is captured, never awaited again.
7. The verifier runs only after an `OwnedWorkStopped` settlement and a zero-exit Completed status, still under the gate. Expected version means Success; reliable restart pending means RestartRequired; anything else means Unverified. Non-zero/refused/denied/timed-out outcomes record Failed. A settled, started-then-cancelled run records Unverified, never Cancelled.
8. If final recording fails, the disposition is Uncertain and the committed DispatchClaimed barrier remains.

## Tests (`ReviewedUpdateDispatchWorkflowTests`)

Fake dispatcher/evidence/verifier over real SQLite claims. Positive verified Success and RestartRequired. Unverified. Unsupported route or mode never claims. Recomputed-digest mode/route mutation in fresh evidence is rejected. Expired review. Busy gate. Cancellation before claim. Lost permit with a retained claim. Unknown invocation and unsettled owned work give Uncertain plus a barrier. Started-then-cancelled is Unverified. A final write failure gives Uncertain. The dispatcher receives the exact frozen operation and claim. The result carries the frozen mode and guard, and the runtime-limit disclosure appears only for the new route.

## Limits

Fake adapters prove composition logic only. The following stay open: production dispatchers (none admitted), Desktop/ScheduledHost caller conversion, protocol-2 endpoint and reviewed fact extension, native Chocolatey profile/helper qualification, progress mailbox wiring, history UI projection, and fresh independent review.

## History projection — mode survives history (`FR-HIS-01`, `FR-UPD-15`)

New passive read model; no schema change, migration or authority.

| Type | Producer | Consumer | Meaning |
|---|---|---|---|
| `ReviewedUpdateTerminalRecord.TryParse` (`Application/ReviewedUpdateHistory.cs`) | Reserved version-2 slot written by `UpdateDispatchTerminal` | History reader | Strict parse: exact 16 KiB size, seven printable-ASCII header lines, exact enum/time/exit encodings, strict UTF-8 diagnostic and zero padding. Anything else is unreadable. |
| `IReviewedUpdateHistoryReader` / `SqliteDatabase.GetReviewedUpdateHistoryAsync(max ≤ 500)` (`Persistence.Sqlite/SqliteReviewedUpdateHistory.cs`) | SQLite `update_dispatch_claims` (untrusted) | Presentation | Every projected column is size/type-bounded. The canonical operation is restored only through `TryRestore` with its digest and must be schema 3 for the same job. The terminal digest must match its bytes, and Uncertain state must agree with the Uncertain outcome. A failed check sets `IntegrityVerified=false`; an unreadable state is presented as unresolved. |
| `ReviewedUpdateHistoryPresentation.Present` → `ReviewedUpdateHistoryView` | Application | Desktop (later) | Fixed text for mode (Selected vs package-script-controlled), candidate evidence (UserCacheIntent means "provisional intent; the helper must independently confirm"), the runtime-limit disclosure for the new route only, and status. Claimed, Uncertain and integrity-failed rows show as blocking. **"Helper qualified" is never displayed**, because stored data has no authenticated, correlated helper observation. |

Tests: `ReviewedUpdateHistoryTests` (10 cases). They cover a finalized row round trip, claimed then recovered-Uncertain blocking rows, the new-route disclosure and provisional-intent text only, six tamper/corruption variants, and the terminal parser round trip plus malformed-slot rejection. Desktop rendering remains UI integration work.
