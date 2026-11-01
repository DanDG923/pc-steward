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

**Windows CI hang (test harness only).** Windows CI on `953ad78` passed Architecture 46, Domain 167 and Application 883, but Security reached the 10-minute guard. The test harness created its server pipe with zero-size buffers. On Windows, every write to such a pipe blocks until the peer reads it. In the "second frame" case the transport correctly stops after reading the first surplus byte, so the fixture helper's write never completed, and the test awaited the helper before closing the desktop end. Linux socket buffers hide this.

Fix: the harness pipe now uses the production restricted-pipe buffer sizes (16 KiB). The surplus-byte test closes the desktop end before awaiting the helper and tolerates the resulting `IOException`. Every helper wait is bounded at 30 s. The transport itself is unchanged: production pipes have 16 KiB buffers, and the desktop never waits on a helper write.

**Windows CI on `52e81e4`** ([run 41296745848](https://github.com/DanDG923/pc-steward/actions/runs/41296745848)): green with nothing skipped. Architecture 46/46, Domain 167/167, Application 883/883, and Security **523/523**: 481 previous tests plus all 42 new transport tests, of which 13 are Windows-only. Those cover the real held binding, the real peer check and real child processes.

### Delta re-review of the remediation (`5ac5662`, `52e81e4`, `58740fc`): PASS

A fresh-context reviewer, pinned to `58740fc` from start to end, returned **PASS** with no safety defect. It confirmed:

- findings 1–6 are closed;
- protocol 1 is not weakened. `HelperBootstrap` still requires `--protocol 1` and the `pcsteward-elevated-` locator, the protocol-1 transport still creates only that locator, and any cross-connection would also fail the launched-helper PID check;
- the `HelperProcessWork` failure path cannot settle. Its ownership task never completes and H is false, so the interpreter rejects it;
- the hang diagnosis is sound, and production cannot deadlock the same way. Every desktop pipe operation is token-bounded on an overlapped pipe, and the desktop never waits on a helper write;
- the test changes do not weaken assertions.

Recorded, non-blocking:

- **L1.** The `HelperProcessWork` failure branch is effectively unreachable (its constructor only throws `ArgumentNullException`), untested, and does not dispose the `Process` handle. It fails closed.
- **L2.** The "held while running" sample is taken once, during the connection wait. Early release after peer refusal is guarded by the architecture test (exactly two release points; release only through the ownership continuation).
- **I1.** Only the PID check of the channel's peer verification is exercised. The signer and image-identity checks need the protocol-2 helper endpoint for an end-to-end test. `PeerImageMatchesHelper` itself is covered by `DeploymentCapabilityReaderTests`.
- **I2.** The late-release architecture check matches the literal `ReleaseWithoutHelper(binding);`; the exact two-`.Dispose()` count still guards direct disposal.

## Slice N0: native job-object launcher (`fca8837`) and review remediation

Contract: [helper endpoint and native observer design](TASK_50_HELPER_ENDPOINT_AND_NATIVE_OBSERVER.md) rev 6 §4.1 and §8 N0. Requirements: `FR-UPD-15`, `TM-03/07/13`.

Implemented in `PCSteward.Provider.Chocolatey.Native`, all internal:
- `NativeLaunchSpec`: private construction; test-only `ForTests`;
- `LaunchJob.RunCoreAsync`;
- `NativeProcessObservation`/`NativeInvocation`;
- `NativeCommandLine`: MSVCRT quoting, the environment block, and path and argument checks.

There is no `src` caller; the typed `RunAsync` and the compiled commands land with N3. Windows CI on `fca8837` was green: Security 572/572, including the 13 Windows-only launcher tests; Application 883; Domain 167.

The implementer's refinements, accepted by the reviewer:
- a started launch is settled only with zero active processes, the observed primary exit and both pipe EOFs;
- a settle window clipped by the deadline also sets `TimedOut`, so lingering descendants in that case end as `TimedOut` rather than leaving the outcome unchanged.

An independent code review (Opus, pinned `fca8837`) returned **CONDITIONAL** with no Blocker or High. It verified:
- P/Invoke layouts and the handle list;
- the environment block;
- the exact kill-on-close job and its readback;
- §4.1 classification, including L1;
- settlement and teardown;
- that every wait is bounded;
- that the pipes cannot be squatted.

| # | Severity | Finding | Remediation |
|---|---|---|---|
| M1 | Medium | The bans were text regexes, while rev 6 F4 requires compiled symbol references | New `NativeLauncherCompiledReferenceTests`: decodes the IL of every built `src` assembly (System.Reflection.Metadata plus an `OpCodes`-derived operand table) and fails on any call to `LaunchJob.RunCoreAsync`, `RunCoreWithFaultForTestsAsync` or `NativeLaunchSpec.ForTests` from outside the declaring type, nested types included. It requires the Provider.Chocolatey, Execution and ElevatedHelper assemblies to be present. Self-checks: more than 1,000 resolved calls, and exactly the two internal `StartAsync` calls. The text test is kept as a secondary check. |
| M2 | Medium | `OutputLimitExceeded` could be lost when a final over-budget chunk is dropped just before EOF, or drained during teardown | The settled return re-checks `budget.Exceeded` and otherwise falls through to teardown; the final result uses `overflow \|\| budget.Exceeded` |
| L1 | Low | A zero-byte pipe read was treated as EOF, but a zero-length write by the child also produces one | A zero read with the pipe still connected continues reading. At most 64 consecutive empty reads are allowed, so the reader cannot spin. |
| L2 | Low | Any image extension was accepted (BatBadBut) | `ValidateImagePath`: a local `.exe` only, no trailing separator; portable test for `.bat`/`.cmd`/`.com`/extensionless/directory |
| L3 | Low | Inheritable handles were visible to a concurrent launch | A process-wide gate spans handle creation through `ReleaseChildEnd`, and the NUL handle is closed inside it. Residual (documented): an unrelated `Process.Start` elsewhere in the same process. The helper runs one item at a time and launches only through this launcher. |
| I1 | Info | Zero active processes but no EOF after teardown gives `AggregateSettled=false` with `TerminationFailed=false` | Recorded. This is outside §4.1's binary teardown rule, but truthful and S11-safe. |
| I2 | Info | No Win32 error codes captured; brief window before job assignment | Recorded. `PROC_THREAD_ATTRIBUTE_JOB_LIST` is possible later hardening. |
| I3 | Info | No test for the unconfirmed-termination Unknown branch | New fault `AssignFailsTerminationUnconfirmed`, which still calls `TerminateProcess`; Windows test asserts `Unknown` plus `TerminationFailed` |

Linux results after remediation:
- Architecture 52/52;
- the N0 portable tests: 41/41;
- 14 Windows-only launcher tests, pending Windows CI.

## Slice N1: native requester-token, access and walker primitives (`c840ef1..ce4839d`) and remediation

Contract: [design](TASK_50_HELPER_ENDPOINT_AND_NATIVE_OBSERVER.md) rev 6 §2–§2.4 and §8 N1. Requirements: `FR-UPD-15`, `TM-03/04/07/13`.

Implemented in `PCSteward.Provider.Chocolatey.Native`, all internal and with no `src` caller:
- `NativeFileIdentity`/`NativeObservedFile`;
- `PeerProcess`;
- `TokenFacts`/`RequesterTokenPolicy` (pure, specific verdicts) and `RequesterToken`;
- `RequesterAccess`: AccessCheck in Node and Ancestor modes, `EvaluateAncestry`, `IsRequesterOwned`;
- `ProtectedNodeReader`, `ProtectedTreeWalker`, `ChocolateyNativePathResolver`.

Implementer deviations accepted by the reviewer:
- the test-only derived token also makes the logon SID deny-only, because the runner is a standard user;
- SYSTEM-owner and High-label cases run on in-memory descriptors through the test-only `EvaluateDescriptorForTests`;
- verdict-returning signatures;
- two stricter rules: the evaluation token must be Limited, and Administrators must not be usable for allow ACEs;
- parent identity uses the enumeration's 64-bit file ID, and a wider ID refuses;
- `IsRequesterOwned` is true when the owner is unreadable.

Windows CI on `ce4839d` failed 7 tests, all in test setup:
- six walker tests: `LockTree` locked parents first, stripping the children's inherited WRITE_OWNER. Fixed in `e924d7e` with a children-first lock order, after which all walker tests pass on Windows;
- the launch-path resolver test: diagnostics in `e924d7e` showed `GetUserProfileDirectory` succeeds on the identification-level token, so the §2.4 L1 design is feasible. The remaining cause was `SHGetKnownFolderPath` with flags 0, which verifies existence on a fresh CI profile. It now uses `KF_FLAG_DONT_VERIFY`, as the .NET folder API does; these paths only feed the compiled launch environment.

An independent code review (Opus, `ce4839d`) returned **CONDITIONAL**. It verified:
- AccessCheck flags, mapping and identification token;
- the Node, Ancestor and parent-delete-child masks;
- the token policy against §2.1;
- the single held `PeerProcess` handle;
- the node reader;
- the `FILE_ID_BOTH_DIR_INFO` parsing, absence rule, parent identity, bounds and cancellation;
- the resolver inputs;
- the IL bans;
- the CI assumptions for the runner's ACLs, the share-read open of `C:\`, and junctions.

| # | Severity | Finding | Remediation |
|---|---|---|---|
| H1 | High | Conditional ACEs depending on resource attributes were evaluated without those attributes. A requester-controlled node (OWNER RIGHTS plus `XA … (Exists @Resource.x)` plus a resource attribute settable with WRITE_DAC) would be judged Denied while Windows grants write. | `ATTRIBUTE_SECURITY_INFORMATION` is requested, and `TryAccessCheck` refuses (Unreadable) any DACL containing an ACE type other than ACCESS_ALLOWED (0) or ACCESS_DENIED (1), or an unparsable ACL. This applies to both the handle path and the test descriptor path. Tests cover conditional allow and deny ACEs on resource and user claims, plus an object ACE, all giving Unreadable. |
| M1 | Medium | Windows N1 evidence not yet green | Fixture and resolver fixes above. The H1 test builds callback and object ACEs in binary (`a6dce2e`). **Windows CI on `a6dce2e` is green**: Security 665/665, Application 883, Architecture 59, Domain 167, nothing skipped. |
| L1 | Low | Ancestor opens are share-read only, which briefly conflicts with renames in `C:\`, ProgramData or Windows | Recorded as availability only: the handles are held for milliseconds and the result fails closed. Not changed, so the reader's single read-only open path stays as is. |
| L2 | Low | `ChocolateyNativeRoots` could be constructed freely | `TryResolveLaunchPaths` requires `Root`/`HttpCache` to equal ProgramData plus `\chocolatey` / `\ChocolateyHttpCache` |
| L3 | Low | The environment/registry ban covered only the N1 files | The ban now covers the whole `Native/` folder |
| Info | — | ReFS wide IDs and WOF, dedup or OneDrive reparse tags refuse, so GAC roots may be unavailable on CompactOS machines | Recorded (fail closed) |

## Slice N2: static Chocolatey profile observer (`4c1ec2f..70966c0`)

Contract: [design](TASK_50_HELPER_ENDPOINT_AND_NATIVE_OBSERVER.md) rev 6 §3, §3.1, §2.3 and §8 N2. Requirements: `FR-UPD-15`, `TM-03/04/07/13`.

Implemented in `PCSteward.Provider.Chocolatey.Native`, all internal and with no `src` caller:
- `ChocolateyReleaseManifest2_7_5`: a generated table, equal to the JSON;
- `ChocolateyProfileObservation`: private construction, with a held-file holder;
- the closed `ChocolateyProfileResult`: Observed, Refused, TimedOut or Cancelled;
- `ChocolateyProfileObserver`: steps 1–9 and 8a in rev 6 order, with a test-only seam banned from `src` in compiled IL;
- `ChocolateyProfileRules`: pure rules;
- `ChocolateyEngineTrust`: manifest hash, WinVerifyTrust on the held handle, the signer from that chain, and version 2.7.5.0.

The implementer's deviations are all stricter or test-only:
- the whole root is walked once;
- extra helpers/tools directories, `chocolatey.licensed` anywhere under the root, and pending markers anywhere under `lib` all refuse;
- a negative source priority refuses at step 6;
- an absent GAC root is accepted only under the M3 parent-deny rule;
- the `lib-bkp` rule also matches 8.3 short names (hence the walker's `ShortName`/`TryListChildren`);
- a new registration-digest encoding, `pc-steward-choco-client-registration:1`.

Windows CI on `70966c0` passed 737 of 738 Security tests. The one failure was fixture setup: the log-file ACL was applied after `logs` had been deleted. It is fixed in `8cc8bf8`.

An independent code review (Opus, pinned `4c1ec2f..70966c0`) returned **PASS**, with no Blocker, High or Medium finding and no false-Observed path. Every refusal re-checks the interrupt state, and Observed is returned only after a final check. The reviewer verified:
- every step against Profile V2 and Task 90, including the source resolution and S6–S9 values;
- TOCTOU: hashing and holding go through the same share-read handles;
- the L3 fingerprint, with the golden reproduced through substitution;
- the IL seam ban;
- the manifest, compared independently with Python.

**Logs directory question resolved.** The reviewer fetched the pinned `setup.psm1` (blob `c66c1282` verified). Lines 349–353 add Users `Write` with `ObjectInherit | InheritOnly`, so the grant applies only to files created inside `logs`, not to the directory. Default installs therefore pass. Non-default install folders, and `ChocolateyInstallAllowCurrentUser`, correctly refuse.

Recorded items, none blocking:
- **L1 (availability).** A single 32,768-entry/depth-32 walk covers the whole root, including `lib` package contents and `logs`, so a large but healthy install could exceed it and be refused. A running choco holding a log file open for writing also makes the share-read open fail, so the result is Unreadable and the route is refused while choco runs. This is accepted as a stricter, fail-closed choice. Per-tree budgets are a later option if owner-host evidence shows real installs exceed the bound.
- **L2 (document reconciliation, integrating note).** Fact extension rev 3 §8 lists "stage-D registration vanished → InstalledMismatch with installed null". Design rev 6 §3 step 7 (L5) maps a missing target registration to `MachineInputsUnsupported`, and N2 follows rev 6. Settled by rev 3's own S6–S9, it stays `Refused` with installed and candidate null, so the outcome is the same settled Refused. This note annotates rev 3 §8 for the N4 stage-machine tests without rewriting the pinned artifact.
- **Info.** A `Compute` exception after steps 8/8a is reachable only with a non-canonical SID, which a real token cannot produce. A SID pre-check is optional.
- **Info, an N5 obligation.** The registration-digest encoding is sound, but it needs an independent golden vector before N5 persists or compares digests across builds. Drift would read as ClientChanged, which fails safe. The sort key uses `\` while the hashed paths use `/`; this is deterministic.
- **Info, N5.** The M2 postcheck passes the expected new version as `frozenInstalled`. Leftover `lib-bkp` or pending state then refuses, giving Unverified.

## Slice N3: retained root-engine binding and fixed client queries (`26e908a..9c3f1a1`) and remediation

Contract: [design](TASK_50_HELPER_ENDPOINT_AND_NATIVE_OBSERVER.md) rev 6 §4, §4.1, §5.4 and §8 N3. Requirements: `FR-UPD-15`, `TM-03/04/07/13`.

Implemented, all internal and with no `src` caller outside `Native/`:
- `ChocolateyHeldClientBinding`: built by the Helper-role observer. It holds the engine and release tree through exit, and holds code only until resume after an upgrade.
- `ChocolateyExactListArguments`: a golden test pins it to the protocol-1 literal.
- The sealed `ChocolateyCompiledCommand`, with factories on the nested records.
- `NativeLaunchSpec.FromBinding` and the typed `LaunchJob.RunAsync`. Call sites are pinned in compiled IL, and the scanner now maps compiler-generated callers back to their source methods.
- `ChocolateyClientQueries`, with a strict parser and the pinned-aware `ChocolateyNativeOutdatedRecord`.
- `ChocolateyStageCMapping`.

Windows CI on `3cbd47d` passed 829 of 830 Security tests, **including all six held-binding launch tests**: a stand-in engine starts while held read-share-only, through the real typed launch path. The one failure was a test that leaked a refused handle, fixed in `8e878be`.

An independent code review (Opus, pinned `26e908a..9c3f1a1`) returned **PASS**, with no Blocker, High or Medium. It verified:
- no free-form strings reach the launch path;
- the compiled-symbol bans and the closed command hierarchy;
- a query can never upgrade;
- the golden arguments;
- binding reuse refusal;
- the M4/L2 hold scope;
- the deadline caps;
- parser strictness;
- each stage-C row against S6–S10 and P1–P6.

| # | Severity | Finding | Remediation |
|---|---|---|---|
| L1 | Low | A well-formed list with zero records reported `PreflightQueryFailed` with the stage-B value, which stage C had not observed | Now `InstalledMismatch` with installed null (N3; P2 allows null). The `Refused` record permits a null installed value for this reason. |
| L2 | Low | Two overlapping upgrades on one binding could both pass `CanLaunch` before the code release | `TryClaimUpgradeLaunch()` is claimed atomically in `FromBinding` for upgrade specs; a second upgrade spec is refused (Windows test) |
| L3 | Low | The `CandidateMismatch` field 18 (the stage-B fingerprint) was not checked against F | `ChocolateyStageCBaseline` takes `expectedFingerprint` and requires fixed-time equality with the stage-B fingerprint |
| L4 | Low | The rows were not asserted against the desktop predicates | A new portable test passes every settled stage-C refusal through `ReviewedUpdateWireCodec.StructuralViolations` and `ReviewedUpdateProtocol2Interpreter.OperationBoundViolations`, with zero violations |
| Info | — | The strict outdated parser refuses the whole query on any odd record, even for an unrelated package | Recorded as an availability cost; required by Profile V2 ("strict unique records") |

## Slice N4a: protocol-2 helper stage machine over the qualification port (`81608fe..1f0e13e`)

Contract: [design](TASK_50_HELPER_ENDPOINT_AND_NATIVE_OBSERVER.md) rev 6 §4, §5.1–§5.4, §6 and §8 N4a, with fact extension rev 3 §2, §3, §6 and §10. Requirements: `FR-UPD-15`, `FR-UPD-01`, `TM-03/04/07/13`.

What it adds:
- The port interfaces and closed value records (`ChocolateyStageValues`, `ChocolateyUpgradeResult`, `ChocolateyItemRefusal`, `ChocolateyStageRefusal`), in Provider.Chocolatey. There is no native implementation yet; that is N4b.
- `ReviewedHelperEndpoint`, in Execution: the A0–A3, B, C, D and Launch stage machine, plus the `IReviewedHelperCapabilityCheck` seam for G1.
- No entry-point wiring: `--protocol 2` still exits with code 20.

Tests use fakes over a real pipe pair (16 KiB buffers) and run against the real desktop `ReviewedHelperExchangeSession` and `ReviewedUpdateProtocol2Interpreter`. All 103 endpoint tests pass; a mutation check showed they detect broken S7 nulling and a broken settlement guard. Architecture tests pass, 77/77. **Windows CI on `7066d76` is green.**

The implementer's deviations are all stricter or fail-safe:
- A1 also checks the route shape and refuses a future `ReviewedAt`.
- An `Observed` or `Matched` result whose values are not the frozen ones is treated as an internal fault.
- A refusal is sent only when `AggregateSettled`; otherwise Undetermined with ¬settled.
- `ApprovalExpired` at stage D carries null values.
- An unsettled, termination-failed or overflowed NotStarted at Launch is reported as Unknown.
- Async item calls have hard caps.

An independent code review (Opus, pinned `81608fe..1f0e13e`) returned **PASS**, with no Blocker, High or Medium and **no reachable path to any critical failure**: no false Matched, no launch without a stage-C Matched and a stage-D Observed, no settled refusal over unsettled work, no response to an uncorrelatable request, no second upgrade, and no response that the desktop would reject where the spec requires settlement. It verified:
- A0/A1 correlation, fixed-time checks, and the freshness and ceiling rules;
- G1 through `item.RequesterCannotModify`;
- the deadline arithmetic and the 60 s floor in the Launch row only;
- the disconnect monitor;
- a single `CreateResponse` call site and a single write;
- the field 16–18 values for every row against S6–S10 and P1–P6.

Its Lows (no real-time cap on synchronous stages B and D, a Launch cap without teardown slack, no hung-Launch test) and Info items (TimeProvider in waits, an item OCE that is not the endpoint's own, post-cap monitor and write waits) are fixed in `1866662`, with endpoint tests now 112/112:
- B and D run under `Task.Run(...).WaitAsync(remaining + ExitMargin, clock)`; on expiry the result is Undetermined + timedOut + ¬settled;
- `UpgradeAsync` gets `remaining − 1 s`, never below the floor;
- new tests for hung B, D and Launch;
- every wait uses the injected TimeProvider;
- an item OCE that is not the endpoint's own is a fault (Undetermined + ¬settled before launch, Unknown after);
- the monitor stop and the response write share one bounded window within `ResponseMargin`. That test is timing-sensitive.

N4b obligations recorded:
- the native item tolerates disposal while a call is in flight;
- the observer honours `remaining` and the token.


## Slice N4b: native port, item and peer, Helper-role G1 reader and protocol-2 bootstrap (`f0f12d5..c91a0e0`)

Contract: [design](TASK_50_HELPER_ENDPOINT_AND_NATIVE_OBSERVER.md) rev 6 §4, §7, §8, §10 and §11–§15, with the N4a obligations above. Requirements: `FR-UPD-15`, `FR-UPD-01`, `TM-03/04/07/13`.

What it adds:
- `NativeChocolateyHelperQualification` (the port) and `NativeChocolateyPeer`, plus `NativeChocolateyHelperItem`, in Provider.Chocolatey. The item runs stage B (observe and hold), stage C (the fixed list/outdated queries), stage D (the identity re-check) and Launch over the held binding.
- `HelperDeploymentCapabilityCheck`, plus the Helper role (with requester-access protection) of `OwnerOnlyDeploymentCapabilityReader` and `ResolveHelperLayout`: G1 for the installed helper.
- `ReviewedHelperBootstrap`, in Execution: the protocol-2 grammar, connect, held-handle peer verification and the protocol-2 composition root. `--protocol 2` now reaches the endpoint. Protocol 1 is unchanged apart from the dispatch, and an IL architecture test confirms its path names no protocol-2 type.

Host facts (`ReviewedHelperHost`) come only from `Environment.ProcessPath`, `AppContext.BaseDirectory`, `WindowsIdentity.GetCurrent()` and `Process.SessionId`, the same as protocol 1. Values from argv are used only for PID equality, the regex-checked pipe name and the fixed-time nonce comparison. The package ID and version reach execution only through `ChocolateyCompiledCommand.TryListExact`/`TryUpgrade` after the stage-B observation, and the engine path comes only from the held binding.

Exit codes: 20 not elevated (as protocol 1), 21 helper paths unavailable, 22 connect failed, 23 peer not verified, 24 no response, 26 write failed.

Implementer deviations, all fail-closed and accepted by review:
- The end-to-end tests replace only the two stage-C queries (architecture-banned test seam), because the stand-in engine prints no records. The real native stage C is tested separately to a settled refusal.
- The positive Helper-role fixture test starts ancestry checks at the fixture root through an architecture-banned test factory; production starts at the drive root.
- The peer image path read from the held handle must equal the installed `PCSteward.Desktop.exe`, which is then hashed through a read-share handle and compared with the ledger. G1 has just proven the requester cannot modify that root, and read-share blocks writers while held.
- Stage-D identity drift is `MachineInputsUnsupported` with null values; unsettled launch jobs keep the stage-B holds until helper exit; G1 is answered only before stage B.

**Windows CI on `c91a0e0` is green on the first attempt**, including the 23 Windows-only N4b tests: Security fixtures 1023/1023, integration 883, domain 167, architecture 79.

An independent fresh-context review of `011a3df..c91a0e0` returned **PASS with fixes**: no Blocker or High, every traced refusal path fails closed, and no argv, pipe, database or remote value reaches execution unvalidated. Both N4a obligations hold: the in-flight count defers release on Dispose mid-call, and the observer uses `CancelAfter(remaining)`. Findings:
- **M1:** the real native positive path (parsed `Matched`, then stage D after real child processes under the stage-B holds, then Launch) has no test.
- **M2:** A3 (G1 hashing and ACL walks) and WinVerifyTrust in peer verification have no real-time cap.
- **L3:** G1 is not exclusive with stage B inside the item; the ordering relies on the sequential endpoint.
- **L4:** the protocol-2 pipe-name regex accepts a trailing newline (`$`).
- **L5:** the architecture pins match constructors by name only, and some construction sites are not pinned.
- **L6:** exceptions before the endpoint (for example from `OpenPeer`) exit with 26 instead of 23.
- **L7 (accepted residual):** a PID can be reused between `GetNamedPipeServerProcessId` and `OpenProcess`; reuse is closed only after the open. Exploiting it needs a same-user attacker with UAC consent who engineers PID reuse onto a genuine desktop, and the impact is still bounded by stages B–D.
- **L8:** the item's lifetime CTS is never disposed.
- **Info (accepted residual):** G1 hashes the installed desktop file, not the mapped image of the running peer, so an old desktop still running after an administrator redeploy would pass. Administrator-only.

### CI intermittent on `011a3df` (environmental)

The first Windows CI attempt on `011a3df` (docs plus the `1866662` endpoint fixes, no fixture or ACL changes) failed 63 Security fixture tests, all with `UnauthorizedAccessException` from `SetSecurityInfo` in `N1NativeFixture.Lock` during `LockTree`. The same image (`win25-vs2026/20261021.278`) was green on `06c83d5`, and the unchanged re-run (attempt 2) was green. Suspected cause: `Test-CI.ps1` places the CI user's TEMP under the checkout, where that user is granted only `(OI)(CI)M`, so setting the owner depends on what the runner's inherited ACL provides. If this recurs, give the fixture root an explicit owner-grant, or move CI TEMP under the CI user's profile.

### N4b review remediation (`576063f..10daa47`): Windows CI green

- **M1:** `StandInChocolateyEngine` (test-only) writes a minimal x64 console image into the fixture temp tree. It imports four KERNEL32 functions, reads no file, environment or registry value, and prints fixed `--limit-output` records for `list` and `outdated`. `TheRealNativeChainParsesRealRecordsMatchesRevalidatesAndLaunchesThroughTheHeldBinding` now runs native stage C (real parsing to `Matched`), stage D after real child processes under the stage-B holds, and Launch, with no stage replaced. **It passes on Windows CI.** It does not replace host evidence with a real Chocolatey release.
- **M2:** A3 runs off the endpoint thread, capped at `Remaining()` on the endpoint clock. Hitting the cap gives RequestNotAdmitted (`a3-unresponsive`); an abandoned check's result is ignored, and later protection calls refuse. A spent budget before A3 gives Undetermined + timedOut (`a3-timed-out`). Peer trust already used `WTD_REVOKE_NONE` with `WTD_CACHE_ONLY_URL_RETRIEVAL` (shared with protocol 1); an architecture test now pins it.
- **L3:** stage calls require no call in flight, so stage B cannot start while G1 is running.
- **L4:** the protocol-2 locator is `\Apcsteward-reviewed-[a-f0-9]{32}\z`; LF, CRLF, CR and a leading LF are refused. The protocol-1 pattern is unchanged.
- **L5:** `CompiledIlScanner` records parameter types; G1 check, Helper-role reader, `ResolveHelperLayout` and `ReviewedHelperHost` construction are pinned by signature, and every pinned edge must exist.
- **L6:** a fault during the PID check, `OpenPeer` or `VerifyPeer` exits with 23 and closes the unverified peer.
- **L8:** the item's lifetime source is disposed once, after Dispose and the last in-flight call.
- **Info:** the bootstrap test comments describe CI accurately (a disposable standard user started from the elevated runner administrator, no UAC split token).
- L7 and the running-image Info item remain the accepted residuals above.

Linux: changed projects build with 0 warnings, architecture 81/81. **Windows CI on `10daa47` is green**: Security fixtures 1031/1031 (0 skipped), integration 883, domain 167, architecture 81.

## Slice N5: desktop producers (`80c9aa1..a749dba`)

Contract: [design](TASK_50_HELPER_ENDPOINT_AND_NATIVE_OBSERVER.md) rev 6 §7 and §8 N5; [adapter gates](TASK_50_ADAPTER_GATES.md) F′ and A2-L6; [Profile V2](TASK_50_CHOCOLATEY_PROFILE_V2.md) §4.3. Requirements: `FR-UPD-15`, `FR-UPD-01`, `TM-03/04/07/13`.

What it adds (all internal, in `Provider.Chocolatey/Native/`, constructed by no `src` code, so the route stays non-admitted):
- **F′** `ChocolateyProfileEvidenceSource.ObserveAsync`. It refuses unless the route shape is accepted and the ownership check passes. It then runs a Desktop-role observation over `RequesterToken.TryFromCurrentProcess` with `expectedFingerprint: null`, capped at 60 s plus a 5 s real-time cap. `EvidenceId` is `"choco-profile-v2:"` + the fresh fingerprint, and the SID is the fresh token's. Every refusal, timeout, cancellation or fault returns null. On success it records the observation's `ClientState` as the verifier baseline: in memory, one-use, keyed by operation digest, at most 8 entries with oldest-first eviction.
- **Review intent** `ObserveReviewIntentAsync`: a `ChocolateyReviewIntent` marked `UserCacheIntent`, with the Profile V2 disclosure text verbatim and the lib-bad residue flag.
- **Verifier** `ChocolateyReviewedUpdateVerifier`. The baseline is taken first, then `ChocolateyPostUpdateClientCheck.RunAsync` does the file capture (a Desktop observation at the expected version). Only `Unchanged` leads to an independent re-observation. Success needs the unique exact target at the expected version, the same SID and an unchanged client state. It never reports `RestartPending`.
- **Application:** `PostUpdateInventoryResult` gained an appended `Detail` (default None). `RejectVerification` records a non-None detail, or a restart-pending result on `ChocolateyMachineRuntimeLimitsV3`, as Unverified. The result type is not persisted, and WinGet behaviour and history mapping are unchanged.
- **Golden vector** for `pc-steward-choco-client-registration:1` in `ChocolateyProfileRulesTests`: literal digests plus a test-local field encoder. The reviewer recomputed both digests independently. This closes the N2/N5 golden-vector obligation.

**Windows CI on `a749dba` is green on the first attempt**, including the 5 Windows-only producer tests: Security 1098/1098, integration 888, domain 167, architecture 86.

An independent fresh-context review returned **PASS with fixes**: no false-Success path, no evidence on a refusal, nothing that enables live dispatch, and the test seams are IL-pinned or banned from `src`. Findings:
- **M1, a scope deferral, recorded here:** design §7 and Profile V2 say the producer itself captures the non-elevated client's `outdated` intent and the aggregate root. N5 does not run `outdated`. The Desktop role has no held binding, and the N3 commands are pinned to the helper item. Instead it validates a supplied `StructuredUpdateCandidateEvidence`: Community source, supported non-client ID, normalized versions, stable, strictly newer. This is not a safety regression: the intent is provisional (UserCacheIntent), the helper confirms the exact candidate at stage C, and the observer refuses `.pin` markers at step 7. **N6-blocking obligation:** composition must supply an intent resolved immediately before the observation, and the review must show the intent's own time.
- **L1:** an exit-0 update that changed nothing ends as `ClientUnverifiable`, with a misleading message (still Unverified).
- **L2:** the real-time cap does not cancel the observer.
- **L3:** cancellation during the re-observation reads as "target version was not detected".
- **L4:** `ObservedAt` is stamped after the observation finishes.
- **L5:** a possible sharing-violation flake in the native test's directory move.

**N6 obligation:** the evidence source and the verifier must share one baseline store instance; separate instances make every result Unverifiable (safe, but the route would be unusable).

### N5 review remediation (`edb1d19`): Windows CI green

- **M1 (code part):** the doc comments say the review intent is a supplied, validated, provisional candidate, never captured. `ObserveReviewIntentAsync` takes `intentResolvedAt`, kept as `ChocolateyReviewIntent.IntentResolvedAt` (UTC), separate from `ObservedAt`. It refuses (null), before observing anything, when that time is default, later than the observation start, or more than `IntentMaxAge` (2 minutes) before it. The 2 minutes match the literal dispatch freshness window in `FreshUpdateDispatchEvidence.Matches`. The N6 composition obligation remains.
- **L1:** an InstalledMismatch refusal on the file capture gives Unverified with detail None ("target version was not detected"). Every other refusal stays ClientUnverifiable, and none is ever Success.
- **L2:** the observer runs under a linked token with `CancelAfter(budget + CapMargin)`, so the cap stops the reads and releases their handles.
- **L3:** cancellation during the re-observation throws `OperationCanceledException`, recorded as "verification did not finish".
- **L4:** evidence and intent are timestamped before the observation starts (stricter freshness). The architecture test checks the ordering.
- **L5:** the Windows test retries the package rewrite and the folder rename on sharing violations (up to 5 times, with a growing back-off).

**Windows CI on `edb1d19` is green**: Security 1108/1108, integration 888, domain 167, architecture 86.
