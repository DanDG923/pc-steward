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

### Adapter gates Part A, portable slice: independent review PASS (`e0da653`)

A fresh-context reviewer (Fable 5.1) returned **PASS** with Low/Info notes only. It confirmed:

- the strict EvidenceId format and the stored-row matrix (barriers retained, Final untrusted);
- the A.6.1 rule is confined to the route, fixed-time and identical at all `Matches` sites;
- no throwing `Supports` can escape, claim or dispatch;
- the closed ledger key sets match each real writer exactly (Pending 9, Rollback 10, Legacy 9, Pinned 12), and the writer-quirk leniencies are safe;
- the ADR-022 note is owner-gated and ADR-022 is unchanged.

Windows CI on `e0da653` is green. Recorded Lows:

- **L1.** The three-site agreement test proves only the false outcome until the route is admitted.
- **L2.** The policy is intentionally stricter than the spec on a completed-ledger `rollbackRequired` and on case-insensitive path de-duplication.
- **L3.** An empty `originalFailure` reads Unreadable rather than Incomplete. That is diagnostic-only.
- **Info.** `Incomplete` is reported before the SID/root mismatch check. Source-text architecture tests are formatting-sensitive.

## Adapter gates Part A (Windows slice): interfaces before callers

Contract: [protocol-2 adapter gates rev 2](TASK_50_ADAPTER_GATES.md) Part A §A.2, §A.4 steps 2–6 and step 3 (3.1, 3.4, 3.5), §A.5, and §R A2-L3/A2-L5, under the owner-accepted ADR-022 alignment note (`FR-UPD-15`, `FR-UPD-01`, `TM-03/04/07/13`). This slice adds the Windows producer of the capability seam, the held binding and the `FileIdInfo` primitives in Execution. All of them are `internal`. **No production caller exists**: there is no route dispatcher, no protocol-2 transport or endpoint, and no route admission. The protocol-1 `WindowsElevatedHelperTransport` is unchanged, and an architecture test keeps it free of the new types.

| Type / member | Project | Producer | Consumer (future) | Meaning |
|---|---|---|---|---|
| `IHelperProtocolCapability.TryHold(route, out HeldHelperBinding?)` | Execution | `OwnerOnlyDeploymentCapabilityReader` | protocol-2 transport, at final pre-launch trust (A.4 step 3.1) | Same evaluation as `Evaluate`, but the binding set stays held. The binding is non-null only for `Supported`, and the caller owns it. Both members are synchronous bounded I/O (G3: `RunAsync` off the UI thread). |
| `OwnerOnlyDeploymentCapabilityReader` | Execution | — | `ChocolateyRuntimeLimitsRouteDispatcher.Supports`/`StillSupported` (future), transport | Fixed layout from `SpecialFolder.ProgramFiles`, `Environment.ProcessPath` and the current SID only. It works in stages and stops at the first failure. Step 2: process path equals `<PF>\PC Steward\PCSteward.Desktop.exe`, otherwise Unreadable. A missing state root or ledger gives Absent. The state root is then checked for no reparse and caller-cannot-modify (`ProtectedLaunchLayout`). Step 3: ledger, at most 1 MiB, through `DeploymentCapabilityPolicy.PassesThrough(Ledger)`. Steps 4–5: manifest (at most 1 MiB), then the install-root protection check, then the statement read through its held handle (`PassesThrough(Statement)`). Step 6: every leaf is held and hashed (at most 32 MiB per file and 64 MiB per set, bounds before hashing), followed by a full `Evaluate`. A route outside the compiled allowlist is Mismatch with no I/O. Any exception is Unreadable, and no handle survives a refusal. There is no cache and no database, environment-variable, registry or remote input. |
| `DeploymentCapabilityLayout`, `DeploymentLayoutProtectionCheck` | Execution | Production resolver / `IsProtectedLayout` | Reader | Test seam only: fixture layouts and a substitute protection check. Production uses the parameterless constructor. |
| `HeldHelperBinding : IDisposable` | Execution | Reader (`TryCreate`, only after a full Supported) | protocol-2 transport | Read-only, `FileShare.Read`-only handles on all 12 binding-set leaves, including lazily loaded closure DLLs and the statement. It also carries `VerifiedHashes` and `HelperIdentity`, the helper apphost's `FileIdInfo` captured before launch. It is disposed only after helper exit is observed and the transport is sealed, or after teardown on the no-exit path (A-M2). Every check fails closed once it is disposed. |
| `HeldHelperBinding.PeerImageMatchesHelper(peerImagePath)` | Execution | — | transport, after `VerifyPeer` (A.4 step 3.4, A-M1) | Opens the peer main-module image with no write or delete share and compares its `FileIdInfo` with `HelperIdentity`. False, including on a read failure, means the request is not written (`HelperStartedRequestNotWritten`). |
| `HeldHelperBinding.RevalidateAfterPeer(peerImagePath)` | Execution | — | transport, only if A-Q1 evidence rejects the hold (A2-L5) | The fallback. It runs the peer identity check against the **pre-launch reference identity** `HelperIdentity`, then re-opens and re-hashes every leaf by its fixed path against `VerifiedHashes`. It detects **replacement only**: a swap that is restored before this check is not detected, so the ACL and `VerifyPeer` remain the controls for that window. |
| `WindowsFileIdentity(VolumeSerialNumber, UInt128 FileId)`, `DeploymentFileAccess` | Execution | — | Reader, binding | `GetFileInformationByHandleEx(FileIdInfo)` and `GetFinalPathNameByHandleW` (`DllImport`, `SetLastError`, `SafeFileHandle`, as in the existing transport code). `TryOpenShareRead` requires the handle's final path to equal the requested path, which refuses a followed link, junction or mount point after the open. Bounded `TryReadBounded`/`TryHashBounded` use `RandomAccess` and require EOF at the observed length. |
| `DeploymentCapabilityPolicy.PassesThrough(inputs, route, stage, out failure)`, `DeploymentCapabilityStage` | Execution (pure) | — | Reader | The Ledger or Statement stage of the unchanged policy. `true` means only "no failure so far". Only `Evaluate` can return Supported, and the `BindingSet` stage is refused. |

Tests:
- **Windows-only** (Security `DeploymentCapabilityReaderTests`; GUID-named temp fixtures; these fail with `PlatformNotSupportedException` off Windows and are Windows-CI evidence):
  - the share-read hold refuses delete, rename, replace-by-move and write, while reads and loader-style opens work;
  - `FileIdInfo` is equal for one file and differs after replacement or for a copy; a same-volume move keeps the identity;
  - A2-L3: a write probe on a protected, held file still returns `ERROR_ACCESS_DENIED`, and a caller-writable held file returns a sharing violation, which fails closed;
  - end-to-end Supported on a real-writer-shaped fixture tree, with all leaves held, the peer identity and fallback checks, and handles released on dispose;
  - no cache;
  - 25 on-disk status cases (Absent, Incomplete, Unreadable, Mismatch, with ledger-stage failures never touching the install root);
  - the production protection refuses a caller-writable tree;
  - a symbolic-link leaf is refused even when the protection check is bypassed.
- **Portable:**
  - the out-of-allowlist route gives Mismatch with no I/O;
  - unresolvable or throwing layouts fail closed;
  - an incomplete handle set gives no binding;
  - staged-policy agreement with `Evaluate` on 11 scenarios, plus the stage-misuse refusals.
- **Architecture:**
  - the reader is the only seam implementation;
  - reader sources have no Sqlite, registry, environment-variable, current-directory, process, cache, or write/delete-share text;
  - `Environment` use is limited to `ProcessPath`/`GetFolderPath(ProgramFiles)`;
  - there is no static state;
  - the protocol-1 transport does not reference the seam, and nothing in `src` constructs the reader.

Deferred (not in this slice):
- the protocol-2 transport that calls `TryHold`, `PeerImageMatchesHelper` and the A-M2 disposal point;
- the route dispatcher and its `Supports` wiring;
- the helper-side G1 check;
- the Profile V2 observer (F′);
- route admission.

A-Q1 remains native evidence required before admission. The CI fixtures show read-share compatibility, not a `runas` launch under a hold on the exact artifact, which needs a separately authorized host test.

### Windows capability binding: independent review (`6014d85`) and remediation

A fresh-context reviewer (Opus) returned **CONDITIONAL PASS**, with no safety defect. It confirmed:

- the `FILE_ID_INFO` layout and class, and the final-path handling;
- single ownership of each `SafeFileHandle`, with leak-checked failure paths;
- read-share-only opens, reparse refusal both before and after open, and hashing through the held handle (no hash-to-hold gap);
- fail-closed bounds, no cache, and no DB/env/wire input;
- all 12 leaves are held, including Desktop.dll and the lazy closure, and nothing in `src` uses the new types yet.

Windows CI on `6014d85`: Security **478/478, zero skipped**, so all 32 Windows-only cases ran and passed; Application 883/883.

- **M1, fixed.** The A2-L5 fallback could never succeed: releasing the hold for launch was only possible via `Dispose`, which disabled verification. `ReleaseHandles()` now closes the handles but keeps `HelperIdentity`/`VerifiedHashes`, so the peer check and re-hash revalidation still work. Only `Dispose` ends verification. The test now proves the post-release fallback detects a replaced leaf.
- **L4, fixed.** A vacuous A2-L3 assertion was removed; the `ProbeWriteData` results are the evidence.
- **Info items, fixed.** A zero file ID is unreadable on its own; relative paths are refused rather than resolved against the current directory.
- **L2, transport-slice obligations.** Add an architecture test that `HeldHelperBinding.TryCreate` is called only by the reader and `Dispose` only after the exit/seal point. The transport must keep a strong reference to the binding until seal.
- **L3, recorded.** The symlink test can be Inconclusive on a host without link privilege; it ran on CI. A junction variant is optional hardening.

## Protocol-2 desktop transport and Chocolatey route dispatcher

Contract:
- adapter gates rev 2 §A.2 (`ChocolateyRuntimeLimitsRouteDispatcher`) and §A.4 steps 1–3;
- reviewed fact extension rev 3 §4.1 (three phases, H) and §6 (budgets 30 s / 11 min / 5 s, one frame then pipe EOF, no retry);
- the review-16 L2 transport obligations.

Requirements: `FR-UPD-15`, `FR-UPD-01`, `TM-03/04/07/13`. All new types are `internal` in Execution. **No production composition constructs them.** `HasSupportedMode` still admits only WinGet, the helper endpoint does not exist, and route admission waits for host evidence (O5, A-Q1, A2-L3).

| Type / member | Role |
|---|---|
| `IReviewedHelperTransport.ExchangeAsync(operation, nonce, ct)` → `ReviewedHelperExchange(Outcome, ResponseFrame, Desktop)` | One exchange. It returns only the interpreter's closed `ReviewedHelperTransportOutcome`, the single frame, and desktop-observed facts. It interprets nothing. |
| `ReviewedHelperExchangeSession.RunAsync` (portable) | The post-launch part. Connection races the helper's exit, the connection budget and cancellation. Then `VerifyPeer`, then the request is built (so its 2-minute freshness starts after UAC). No connection, a failed or throwing peer check, cancellation, or a request-build failure gives **`HelperStartedRequestNotWritten`**: nothing written, helper terminated, exit awaited for H. From the first write, everything is **`RequestWrittenNoValidResponse`** unless exactly one frame (at most 16 KiB) is read and a further read returns EOF within the execution budget, giving **`ResponseFrameReceived`**. The exit wait (5 s) follows; a missing exit is torn down and leaves H false. The transport is always sealed in `finally` before reporting. `TerminationFailed` records a failed kill. `OwnedIoComplete`/`PipeEndOfFile` are set only after the desktop's own write, read and EOF read complete. |
| `WindowsReviewedHelperTransport` | Pre-launch order (A.4 step 3): route/EvidenceId check, request-build validation, Chocolatey desktop caller trust, fixed paths, initial launch trust, restricted per-request pipe (`pcsteward-reviewed-…`), **`TryHold`**, helper-path equality with the binding, **final launch trust**, cancellation, then one `runas` launch with `--protocol 2 --desktop-pid --pipe --nonce`. Any failure before launch is `LaunchRefusedBeforeElevation` with no UAC; `ERROR_CANCELLED` is `ElevationDeclined`. The channel's peer check is the client-PID check, then `WindowsPeerIdentityVerifier.VerifyPeer`, then the peer's main-module image path, then `binding.IsHolding && binding.PeerImageMatchesHelper(path)` (A.4 step 3.4, A-M1). |
| Binding lifetime (A-M2, L2) | Exactly two release points: `ReleaseWithoutHelper` when no helper process exists (refusal, declined, no process, launcher exception), and `ReleaseAfterOwnership`, a continuation on `HelperProcessWork.OwnedCompletion`, which completes only after an observed exit **and** `SealTransport`. A helper whose exit is never observed keeps the binding held. The release is registered before the exchange starts, and `GC.KeepAlive(binding)` follows the exchange, so the strong reference outlives the seal. The A2-L5 fallback (`ReleaseHandles` plus `RevalidateAfterPeer`) is **not** used: it applies only if A-Q1 evidence shows the hold blocks the launch. |
| `ChocolateyRuntimeLimitsRouteDispatcher` | `Supports` = `TryGetProfileDigest` (route shape plus O2) and only then `capability.Evaluate(route).IsSupported`; any exception gives false. It is also the workflow's `StillSupported`. `DispatchAsync` draws one fresh nonce, runs one exchange and returns `Interpret(…).WorkflowObservation`. An off-route ticket gives `NotStarted(Failed)` with no exchange. A transport exception propagates to the workflow, which records Unknown, i.e. Uncertain. |

Decisions recorded for review:
- **Pre-launch cancellation** maps to `LaunchRefusedBeforeElevation`, i.e. `NotStarted(Failed)` and "nothing was started", because the interpreter's closed set has no pre-launch Cancelled row. This is truthful (NotInvoked) but labelled Failed rather than Cancelled.
- **Request build** happens after `VerifyPeer`, as in protocol 1, so a slow UAC answer cannot spend the helper's 2-minute freshness window. It is also validated once before launch, so an unbuildable request never prompts.
- **The helper endpoint is deferred.** `HelperBootstrap` still accepts only protocol 1, so a real helper given `--protocol 2` exits before connecting. The exchange then records `HelperStartedRequestNotWritten` with H, i.e. NotStarted, and never Uncertain. Stages A–D, G1 and the Profile V2 observer belong to the native qualification slice.

Tests: Security `ReviewedHelperProtocol2TransportTests` (28 portable, 12 Windows-only) and Architecture `DeploymentCapabilityBoundaryTests` (two new tests), detailed in the [traceability entry](../requirements/TRACEABILITY.md).

Linux results:
- Release build has zero warnings;
- Architecture 46/46, Domain 167/167;
- Application 780 passed, 103 failed of 883, the same platform failure set as before;
- Security 465 passed, 55 failed and 1 skipped of 521. The 55 failures are the 43 previous Windows-native failures plus the 12 new Windows-only cases.

### Independent review (`953ad78`) and remediation

A fresh-context reviewer (Opus), pinned to `953ad78` from start to end, returned **FAIL**. It found no safety defect: every finding failed closed. It verified:

- `RequestWritten` is set before the first write;
- `ResponseFrameReceived` needs exactly one frame plus EOF within the execution budget;
- NotStarted settles only with H and a completed ownership task;
- nothing is written before the connection, cancellation and peer checks pass and the request is built;
- both release points are correct, and the binding stays rooted;
- the budgets are correct, and a Windows broken-pipe read gives 0 bytes (EOF);
- there is no scope creep.

| # | Severity | Finding | Remediation |
|---|---|---|---|
| 1 | High | `RestrictedNamedPipe.Create` accepted only the protocol-1 locator `pcsteward-elevated-…`. The transport therefore always refused before `TryHold`, and 9 of the Windows-only tests would fail on Windows CI. | Added the protocol-2 locator pattern `^pcsteward-reviewed-[a-f0-9]{32}$` (`WindowsReviewedHelperTransport.PipeNamePattern`). The factory accepts either locator (`RestrictedNamedPipe.IsAcceptedLocator`). `HelperBootstrap` still accepts only protocol 1 and its locator. New portable test: the generated name passes the factory, malformed names fail, and the protocol-1 bootstrap rejects both the protocol-2 locator and `--protocol 2`. |
| 2 | Medium | No test covered the Windows channel's peer check. | New Windows test: a long-running stand-in child plus a client from the test process (different PID). The peer check refuses it, it reads 0 bytes, the outcome is `HelperStartedRequestNotWritten` with H, and the binding is released only after exit and seal. (The direct `PeerImageMatchesHelper` mismatch is already covered by `DeploymentCapabilityReaderTests`.) |
| 3 | Low | The post-write catch was narrower than the rest of the file, so an unexpected exception escaped without `Terminate` or an exit wait. | Post-write catch is now `when (exception is not OutOfMemoryException)`. |
| 4 | Low | A throwing `new HelperProcessWork(helper)` left the helper unterminated. | Wrapped: on failure, `TryTerminate`, the binding is left unreleased, and the result is `HelperStartedRequestNotWritten` with a pending ownership task and no H, i.e. Uncertain. |
| 5 | Low | The "held while running" check happened only at launch. | The new test (item 2) asserts `IsHolding && !IsDisposed` mid-exchange while the child runs. |
| 6 | Low | The source-text release check missed `using`, `ReleaseHandles` and late `ReleaseWithoutHelper` calls. | Architecture checks added: every `ReleaseWithoutHelper(binding)` precedes `owned = new(helper)`, and the transport has no `ReleaseHandles` and no using-scoped binding. |
| 7 | Info | If `ExitObserved` faults, the pre-existing `HelperProcessWork` parks ownership on an unreferenced task, so the binding can be collected. | Recorded. This is rare and existed before this slice. |
| 8 | Info | `Process.Start` with `UseShellExecute` could in theory return null after a launch. | Recorded. It is not expected for a direct `runas` `.exe`. |

The reviewer accepted the three recorded decisions. The endpoint wording above is accurate after fix 1: a real helper given `--protocol 2` fails its bootstrap and exits before connecting.

Linux after remediation:
- Architecture 46/46;
- the transport class: 29 portable tests pass, and 13 Windows-only tests fail as designed off Windows;
- the remaining Security and Application failures are the unchanged Windows-native set.

Windows CI on the remediation commit is pending.

