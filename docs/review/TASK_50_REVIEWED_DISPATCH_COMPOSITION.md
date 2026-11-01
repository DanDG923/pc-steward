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
7. The verifier runs only after an `OwnedWorkStopped` settlement and a zero-exit Completed status, still under the gate. Expected version means Success; reliable restart pending means RestartRequired; anything else means Unverified. (Superseded by the post-review corrections below: denial and refusal have their own outcomes; started timeout/failure/cancellation records Unverified; only provider-reported failure or a non-restart non-zero exit records Failed.)
8. If final recording fails, the disposition is Uncertain and the committed DispatchClaimed barrier remains.

## Tests (`ReviewedUpdateDispatchWorkflowTests`)

Fake dispatcher/evidence/verifier over real SQLite claims. Positive verified Success and RestartRequired. Unverified. Unsupported route or mode never claims. Mode/route mutation in fresh evidence is rejected (recomputed-digest canonical mutation is covered by the operation and claim suites). Expired review. Busy gate. Cancellation before claim. Lost permit with a retained claim. Unknown invocation and unsettled owned work give Uncertain plus a barrier. Started-then-cancelled is Unverified. A final write failure gives Uncertain. The dispatcher receives the exact frozen operation and claim. The result carries the frozen mode and guard; Chocolatey routes cannot reach dispatch (review confirmation refuses them). New-route disclosure is covered by the history and argument suites.

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


## Post-review corrections

See the [Task 90 remediation](TASK_90_TASK_50_DISPATCH_REVIEW_REMEDIATION.md). Appended outcomes: `ElevationDenied` and `Refused`. Observation gains `RestartReported` and a 2048-char diagnostic bound. Admission gains `ClaimAsync`, which returns a committed claim without a permit for NotInvoked finalization. Started-then-timed-out/failed/cancelled work is `Unverified`.

## Protocol-2 interpreter — interface before callers

Contract: [protocol-2 reviewed fact extension rev 3](TASK_50_PROTOCOL2_REVIEWED_FACT_EXTENSION.md) §3.3, §4.1, §8 and §10 N1–N4 (`FR-UPD-15`, `FR-UPD-01`, `TM-03/04/07/13`). This adds a pure, passive Execution component. No transport, endpoint, helper, route dispatcher, `Supports`/manifest capability seam, history format or verifier is added, and no caller exists yet. Application workflow semantics are unchanged.

| Type (`src/PCSteward.Execution/ReviewedUpdateProtocol2Interpreter.cs`) | Producer | Consumer | Meaning |
|---|---|---|---|
| `ReviewedHelperTransportOutcome` | Future protocol-2 transport (after the permit is consumed) | Interpreter | Closed phase fact: `LaunchRefusedBeforeElevation`, `ElevationDeclined`, `HelperStartedRequestNotWritten`, `RequestWrittenNoValidResponse`, `ResponseFrameReceived` (exactly one authenticated frame, then pipe EOF, within the budget). |
| `ReviewedHelperDesktopObservation` | Future transport (desktop-observed only) | Interpreter | Owned-root snapshot, helper exit observed within `ExitTimeout`, transport sealed, desktop termination failure, desktop owned-I/O completion and pipe EOF. `H` = exit observed ∧ sealed ∧ ¬termination failure. |
| `ReviewedUpdateProtocol2Interpreter.Interpret(operation, outcome, frame, nonce, desktop)` | Execution (pure) | Future `IReviewedUpdateRouteDispatcher` for `ChocolateyMachineRuntimeLimitsV3` | Re-reads the frame through `ReviewedUpdateWireCodec.TryReadResponse` against the frozen `JobId`/`Digest` and the request nonce (correlation, S1–S13), checks the route shape and `F` from `EvidenceId = "choco-profile-v2:" + F`, then P1–P6 and H. Any failure discards all wire facts: `Unknown`/`Failed`/`ExecutionUncertain`, diagnostic `helper-response-rejected`, so the workflow records Uncertain. Otherwise it applies the §4.1 status table and observation-member rules. `RestartReported` is always false (O4). |
| `ReviewedUpdateProtocol2Interpretation` | Interpreter | Future dispatcher | The `ReviewedUpdateDispatchObservation` plus the candidate check. |
| `ReviewedHelperCandidateCheck` (+ `ReviewedHelperCandidateCheckKind { NotObserved, Confirmed, Refused, Unconfirmed }`) | Interpreter only (private constructor; the single factory is referenced only from the interpreter file, enforced by an architecture test) | Future history persistence (§4.2) and live display | Desktop-derived value: `Matched` → `Confirmed`, `Refused` → `Refused` with the reason and the reported installed/candidate versions, `Undetermined` → `Unconfirmed`, anything rejected or pre-response → `NotObserved`. It never changes status, settlement or outcome, and it has no conversion to or from `ReviewedCandidateProvenance`. Only `Confirmed` has live text, the fixed §4.1 sentence. |

Deliberate limits:
- **Carrying the candidate check.** §4.1 wants it appended to the observation. Application cannot reference an Execution type, so the check is returned beside the observation. The integration slice must define the Application-level member and its history format. That is a reviewed Application change.
- **Workflow admission.** `ReviewedUpdateReview.HasSupportedMode` still admits only `WinGetUserExactV3`. The composition test therefore drives the real workflow with a WinGet carrier operation, while the interpreter judges a separate frozen `ChocolateyMachineRuntimeLimitsV3` operation.
- **`Matched ∧ NotStarted ∧ outputLimitExceeded`.** This is rejected explicitly (§10 nit).

### Protocol-2 interpreter — independent review PASS (`956068c`)

A fresh-context reviewer (Fable 5.1) returned **PASS** with only Low findings. It confirmed there is no path from forged, partial, uncorrelated or inconsistent input, or a missing H, to Confirmed, a false NotInvoked, unverified Success, a non-Uncertain unknown invocation, or `RestartReported`. P1–P6 match the spec word for word. Dropping any predicate, the H check, the Refused-first rule or the launch-failure rule fails a named test. Dispositions:

- **L1, fixed.** Pre-request rows (launch refused, elevation declined, helper started without a request) now fail closed when a response frame accompanies them or the desktop facts are unsettled (pending root or failed termination).
- **L4, fixed.** `ExecutionUncertain` explicitly includes `Invocation == Unknown` as defence in depth.
- **L2, transport-slice obligation.** `HelperStartedRequestNotWritten` must mean zero request bytes written; a partial write is `RequestWrittenNoValidResponse`.
- **L3, N5-gated adapter obligation.** Enforce the O2 `EvidenceId` format (`choco-profile-v2:` + 64 uppercase hex) in `ReviewedUpdateOperation` for the runtime-limit route. Today only `TryGetProfileDigest` enforces it, and it fails closed.
- **L5.** Test gaps whose failure mode is conservative: individual Channel/Architecture/Mode negatives for `TryGetProfileDigest`, and an explicit "Refused with a pending desktop root" case. To be added with the adapter slice.
- **Info.** The §4.1 Undetermined row versus the precedence sentence. Both settle to Failed, so the recorded outcome is the same.

## Helper candidate check in history (adapter gates Part B) — interface before callers

Contract: [protocol-2 adapter gates rev 2](TASK_50_ADAPTER_GATES.md) Part B and §R B2-L1/B2-L2 (`FR-HIS-01`, `FR-UPD-15`, `FR-UPD-01`, `TM-03/04/07/13`). Part A (capability seam, ledger, transport binding, A.6.1) is **not** in this slice. The value is history data only: it never changes `Status`, settlement or outcome, and it never feeds review, approval, admission, dispatch, reconciliation or provenance.

| Type / member | Producer | Consumer | Meaning |
|---|---|---|---|
| `RecordedHelperCandidateCheck` (+ `RecordedHelperCheckKind`, `RecordedHelperRefusalReason`) (`Application/RecordedHelperCandidateCheck.cs`) | `ReviewedHelperCandidateCheck.ToRecorded()` in the interpreter file — the single `FromDesktopDerived` call site in `src` | Workflow finalization, store, history reader | Desktop-derived, recorded-at-the-time value; private constructor; structural rules R1–R4 and the version form; `IsConsistentWith(op, outcome, settlement)` holds all B.5 invariants and is shared by workflow pre-check, store write and reader. `TryRestore` is used only by `ReviewedUpdateTerminalRecord.TryParse`. |
| `IReviewedHelperVersionGrammar` / `ChocolateyHelperVersionGrammar` (Provider.Chocolatey) | Composition roots | `SqliteDatabase` (write readback and history read) | One injected grammar: `ChocolateyVersionGrammar.IsNormalized(value, 128)`, the codec's facade. |
| `ReviewedUpdateDispatchObservation.HelperCheck` (appended, optional; null = NotObserved) | Future protocol-2 dispatcher, via `ReviewedUpdateProtocol2Interpretation.WorkflowObservation` | Workflow | Carried to `FinalizeAsync` only; an inconsistent value records NotObserved and prefixes `helper-check-not-recorded` before truncation. |
| `UpdateDispatchTerminal.HelperCheck` (moved unchanged to `UpdateDispatchTerminal.cs`) | Workflow | Store | `GetReservedPayload` writes version 3 iff the check is not NotObserved; otherwise version-2 bytes are identical to before. |
| `ReviewedUpdateTerminalRecord.HelperCheck`, `TryParse(reserved, grammar, out record)` | Reserved slot | History reader | Strict v2/v3 parse; v3 needs a grammar; v3 with NotObserved is corruption. |
| `ReviewedUpdateDispatchResult.HelperCheck`, `ReviewedUpdateHistoryView.HelperReport` | Workflow / presentation | Desktop (later) | Result carries only the value actually persisted. `HelperReport` is separate from `Status` and is null for Unconfirmed, NotObserved and integrity failures. |
| `SqliteDatabase(string, IReviewedHelperVersionGrammar)` (public); `SqliteDatabase(string)` becomes internal (tests only) | Desktop and ScheduledHost composition roots | — | B2-L1: every production root injects the grammar. B2-L2: the store re-checks consistency and refuses to commit a v3 payload its own history reader would reject. |

### Helper-check history (adapter gates Part B): independent review PASS (`4129266`)

A fresh-context reviewer (Fable 5.1) returned **PASS** with Low findings only. It confirmed:

- the helper check cannot change an outcome, disposition or barrier;
- construction is confined to the single adapter;
- the v3 layout, strict parsing and v2 byte-identity behave as specified;
- every v3 row the store writes is one its own reader accepts;
- older builds still reconcile and still block on barriers;
- the grammar is injected in both production composition roots;
- the wording follows §B.6.

Windows CI on `4129266` is green, including the Desktop and ScheduledHost compiles. Dispositions:

- **L2, fixed.** A v3 terminal whose facts line is `None` (only producible by `GetReservedPayload(null)`; no live writer emits it) is now unreadable. Test added, and the grammar test now uses a NotInvoked settlement like live writers.
- **L1, route-admission obligation.** Add a workflow test on a `ChocolateyMachineRuntimeLimitsV3` draft with a consistent Confirmed/Refused check asserting `result.HelperCheck == record.Terminal.HelperCheck`, a v3 row and no marker. This cannot run until the route passes the review gate (`HasSupportedMode` admits only WinGet).
- **L3, accepted.** Application's existing `InternalsVisibleTo` to Storage means `TryRestore`'s confinement relies on the source-level architecture test.

## Adapter gates Part A (portable slice): interfaces before callers

Contract: [protocol-2 adapter gates rev 2](TASK_50_ADAPTER_GATES.md) Part A §A.2, §A.3, §A.4 steps 3–6 (pure part), §A.6, §A.6.1 and §R A2-L1/L4/L5/L6 (`FR-UPD-15`, `FR-UPD-01`, `TM-03/04/07/13`). This slice adds only portable/pure code. The Windows reader (`OwnerOnlyDeploymentCapabilityReader`), the share-read hold (`TryHold`/`HeldHelperBinding`), the `FileIdInfo` peer check, the protocol-2 transport, the route dispatcher, the helper-side G1 check, the Profile V2 observer that produces F′, and every deployment-script change are **not** in this slice; none of them has a production caller.

| Type / member | Project | Producer | Consumer | Meaning |
|---|---|---|---|---|
| `ReviewedUpdateOperation.ProfileEvidencePrefix`, `TryGetProfileEvidenceDigest(evidenceId, out digest)` | Domain | — | Constructor/`TryRestore` (O2), interpreter, `FreshUpdateDispatchEvidence.Matches` | Exact `choco-profile-v2:` + 64 uppercase hex (81 chars). The shared constructor throws `ArgumentException` for `ChocolateyMachineRuntimeLimitsV3` otherwise, so `TryRestore` returns false; other routes unchanged. |
| `FreshUpdateDispatchEvidence.Matches` (+ internal `ProfileEvidenceMatches`) | Application | — | `ClaimAsync` (pre-claim), store `TryClaimAsync`, `UpdateDispatchPermit.TryConsume` | A.6.1: for the runtime-limit route only, `fresh.EvidenceId` must have the exact profile form and its digest must equal F (fixed-time). Evidence and operation are immutable, so all three sites give the same result. WinGet (`fresh:1`) unaffected. |
| `ReviewedUpdateDispatchWorkflow.RunAsync` | Application | — | Desktop (later, off the UI thread — G3) | The pre-claim `Supports` uses the same exception-to-`false` wrapper as `StillSupported`: a throwing seam gives NotStarted with no claim. |
| `ReviewedUpdateWireCodec.ProtocolVersion` (`"2"`) | Execution (internal const) | — | Codec, capability policy | The single compiled protocol value. |
| `HelperProtocolCapabilityStatus`, `HelperProtocolCapabilityResult` | Execution (internal) | Policy / future reader | Future route dispatcher `Supports` | Closed result; one fixed diagnostic token per status; never path/hash/JSON text. |
| `IHelperProtocolCapability.Evaluate(route)` | Execution (internal) | Future Windows reader | Future `ChocolateyRuntimeLimitsRouteDispatcher` | Protocol version is not a parameter. `TryHold` and `HeldHelperBinding` are deferred to the Windows transport slice (they are handle-holding code). No implementation in `src`. |
| `DeploymentCapabilityInputs`, `DeploymentObservedFile` | Execution (internal) | Future reader (bytes and hashes already read) | Policy | Ledger, candidate-manifest and capability bytes (null = absent), current SID, expected install root, binding-set observations (length, SHA-256). |
| `DeploymentCapabilityPolicy.Evaluate(inputs, route)` | Execution (internal static, pure) | — | Future reader; helper stage A (G1) | Steps 3–6 of §A.4 over supplied bytes: closed ledger key set per writer variant (A2-L1), candidate-manifest pin, exact capability file, binding-set hashes incl. `PCSteward.Desktop.dll` (A2-L4). Any exception → `Unreadable`. |
| `DeploymentCapabilityPolicy.TryReadLedger`, `DeploymentLedger`, `DeploymentLedgerVariant` | Execution (internal) | — | Policy, fixture tests | Strict parse of the four writer variants: Pending, Rollback, CompletedLegacy, CompletedPinned. |
| `DeploymentCapabilityPolicy.CanonicalCapabilityBytes()` | Execution (internal) | — | Tests now; payload build later | Bytes generated from `ProtocolVersion` and the compiled operation allowlist (A.3). The checked-in payload file and its publish wiring are deferred with the script changes. |

Placement is enforced by architecture tests: capability types appear only in Execution; the policy file has no Sqlite/Registry/environment/HTTP/file-system or P/Invoke use; Application, Persistence and ScheduledHost never reference capability types.
