# Task 50 — Chocolatey helper qualification probe: protocol-2 addendum (slice S1 specification)

Requirements: `FR-UPD-15`, `TM-03`, `TM-04`, `TM-07`, `TM-13`; ADR-018, ADR-022, ADR-025 (durable dispatch; follow-up controls), ADR-031 (decision 7).

Status: **revision 2, after the independent review (verdict "Sound with changes"); see §11.** It adds no code, endpoint, deployment, UAC prompt or host action. The probe stays undeployable until the owner accepts the [ADR-025 amendment](../adr/ADR-025-TYPED-OPERATION-JOBS-AND-HELPER.md#proposed-amendment-2026-11-03--owner-started-qualification-probe) and the [ADR-018 cross-note](../adr/ADR-018-HELPER-IPC-AND-ELEVATION.md#proposed-cross-note-2026-11-03--qualification-probe-bootstrap) separately, and each run needs its own approval.

Authority: [exact-artifact test design rev 2](TASK_50_EXACT_ARTIFACT_TEST_DESIGN.md) §4 (A), and the owner decisions recorded there on 2026-11-03: **Decision 1 Yes, Decision 2 Yes, Decision 3 (c)**. This record is an addendum to the [protocol-2 fact extension rev 3](TASK_50_PROTOCOL2_REVIEWED_FACT_EXTENSION.md). It builds on the [helper endpoint and native observer design rev 6](TASK_50_HELPER_ENDPOINT_AND_NATIVE_OBSERVER.md) (§2.1, §3 steps 1–9, §5, §6 G1) and the [ADR-031 design](TASK_50_ADR031_CLIENT_SELF_UPDATE_DESIGN.md) (§3.6 client-mode observation, R5, §3a). Source state: `src` at `0835801` (unchanged at `febf1ac`).

## 0. Scope

In scope: the helper command-line grammar for the probe kind; the helper probe entry and stage order; the closed probe response, its rules Q1–Q16 and bounds; the desktop entry, transport and result file; compatibility; the architecture-test obligations for S2–S4; how results are interpreted; the deployment-order obligations for S7 (§9).

Unchanged and out of scope: the update request and both update response shapes (package 20 properties, client 21; S1–S14); protocol 1; `HasSupportedMode`, `CapabilityOperationAllowlist`, the dispatcher's `Supports`; the capability file grammar; ADR-025's real-update and installed-state tests (open under G9); E4 (block H4); a package-mode probe (§10 answer 4).

## 1. Versioning decision: additive kind under protocol "2"

**Chosen: an additive request kind under protocol `"2"`, selected only by the command-line literal `--kind qualify-chocolatey-helper`.** No capability token is added, and the capability file grammar is unchanged.

Why not a capability token in `PCSteward.DeploymentCapability.txt`:
1. **It would touch an admission pin or the admission parser.** `TryParseCapability` accepts only `operation:` lines whose token is in `CapabilityOperationAllowlist`. A probe token as an `operation:` line changes the allow-list, which Decision 1 forbids. A new line type changes the parser that `Supports`, `TryHold` and G1 all share, so the probe would widen the update path's gate.
2. **It adds no compatibility protection.** The desktop, the helper and the capability file are all in `DeploymentCapabilityPolicy.BindingSet`. Their hashes are pinned by the ledger and checked by the desktop's `TryHold` before UAC and by the helper's G1 after it (G1 also checks the peer desktop's image hash). A mismatched desktop/helper pair is therefore already refused in any valid deployment. Admission changes the capability file anyway (the client token), so a token would only travel along with it.
3. **It is not what the user consents to.** The UAC details show the command line, not the capability file. The kind must bind the consent (design §10 finding 2), and only the command line does that.

Discrimination between the two kinds:
- **On the command line:** 8 arguments mean update; exactly 10, with the fixed literal pair at positions 0–1, mean probe (§2).
- **On the wire:** the probe response starts with `"protocolVersion":"2","kind":"qualify-chocolatey-helper"` and has a different exact property set. Each reader rejects the other frame on the exact-set check.
- **In the protocol value:** `ReviewedUpdateWireCodec.ProtocolVersion` stays `"2"`. The probe adds no new protocol value that an older reader could accept.

## 2. Command-line grammar and dispatch

**Probe line (exactly 10 arguments, fixed order, case-sensitive ordinal):**

```
--kind qualify-chocolatey-helper --protocol 2 --desktop-pid <n> --pipe <pcsteward-reviewed-[a-f0-9]{32}> --nonce <64 uppercase hex>
```

| Index | Value | Rule |
|---|---|---|
| 0, 1 | `--kind`, `qualify-chocolatey-helper` | both literal; the only kind value that exists |
| 2, 3 | `--protocol`, `2` | literal; `2` = `ReviewedUpdateWireCodec.ProtocolVersion` |
| 4, 5 | `--desktop-pid`, `<n>` | `NumberStyles.None`, invariant, > 0 (as `TryParse`) |
| 6, 7 | `--pipe`, `<name>` | `WindowsReviewedHelperTransport.PipeNamePattern()` (`\A…\z`) |
| 8, 9 | `--nonce`, `<hex>` | exactly 64 of `0-9A-F` |

**Why the kind comes first (review L10).** The UAC details view shows the program path followed by the command line, and a long line can be cut off at the end. With the kind at indices 0–1 it follows the program path directly, so it is visible even if the pipe name and nonce are truncated.

The probe parser (`ReviewedHelperProbeBootstrap.TryParse`, a separate type) refuses anything else: other lengths, reordering (including the revision-1 order with the kind at 8–9), a repeated or unknown key, a different kind value, any of these with different case, or extra whitespace. Its result carries only PID, pipe and nonce, as today. Unlike the update parser, which accepts its four keys in any order, the probe parser accepts only this one order.

**Dispatch in `ElevatedHelperEntryPoint.RunAsync` (order is normative):**
1. `SelectsProbe(args)`: `args.Length == 10 && args[0] == "--kind"`. If true, take the probe path (§3).
2. Otherwise `SelectsProtocol2`, then the reviewed update path (unchanged).
3. Otherwise the protocol-1 path (unchanged).

**Mutual refusal (each is a required test):**
- `ReviewedHelperBootstrap.TryParse`, `SelectsProtocol2` and protocol-1 `HelperBootstrap.TryParse` each return false for every probe line, because all three require exactly 8 arguments (`SelectsProtocol2` and both `TryParse` methods check `Length != 8` first).
- Elements 2–9 of a probe line form a valid 8-argument update line. The probe parser refuses that line, and both update parsers refuse the full 10-argument line. Both directions are tests.
- Every 10-argument line that `SelectsProbe` does not select falls to protocol 1, which refuses it (exit 20). This includes the revision-1 order.
- The probe parser refuses every 8-argument update line and every permutation of the probe line.
- A helper started in update mode never reaches the probe entry. A helper started in probe mode never reads a request frame (§3), so an update frame sent to it is never parsed.

Binding is as today: the per-request pipe is restricted to the user and SYSTEM; the helper connects with `TokenImpersonationLevel.Identification` within 30 s; `TryGetServerProcessId` must equal `--desktop-pid`; the helper opens the held peer handle (H3) and runs the handle-based `VerifyPeer` (fixed desktop path beside the helper, same session, same SID, trusted same signer); the nonce is one-shot, is echoed in the response, and the desktop compares it in fixed time. The kind is a fixed compiled literal. It carries no provider operation data. ADR-018 enumerates four launch parameters and one accepted request, so this fifth literal and the absence of a request frame are an explicit exception (ADR-025 amendment item 2 (d); ADR-018 cross-note).

## 3. Helper probe entry

`t0` is taken at pipe connection. The probe budget is **10 minutes from `t0`**, below the desktop's 11-minute response budget (as in N4). Every stage that does real work runs under a real-time cap inside that budget (A3, B′ below), so the helper never writes after the desktop has given up.

**Port (review M2).** The probe uses its own port, `IChocolateyProbePort`, with its own `OpenPeer(int pipeServerProcessId)` and `BeginProbe(peer, expectedSid, out TokenVerdict verdict)` that returns an `IChocolateyProbeItem`. The probe bootstrap has its own `OpenVerifiedProbePeer`, which makes the same calls as today's private `ReviewedHelperBootstrap.OpenVerifiedPeer` (`RestrictedNamedPipe.TryGetServerProcessId`, `OpenPeer`, `WindowsPeerIdentityVerifier.VerifyPeer`) but is typed on `IChocolateyProbePort`. It does not reuse `OpenVerifiedPeer`, because that method takes `IChocolateyHelperQualificationPort`, whose `BeginItem` returns an `IChocolateyHelperItem` with `UpgradeAsync`. `IChocolateyPeer`, `ReviewedHelperHost.TryResolve` and the exit-code constants are shared.

| Stage | Work | Result |
|---|---|---|
| P0 | Parse (§2), Windows check and elevation check | Failure → exit **20** before any connection (as both existing paths) |
| P0 (cont.) | `ReviewedHelperHost.TryResolve` | Null → exit **21** (`LayoutUnavailable`) |
| P0 (cont.) | Connect (30 s) | Failure → exit **22** (`ConnectionFailed`) |
| P0 (cont.) | Server-PID check, `probePort.OpenPeer`, handle-based `VerifyPeer` | Failure → exit **23** (`PeerNotVerified`). P0 never writes a response. |
| P1 | Start the **inbound monitor**: one pending 1-byte read on the pipe. The byte is never parsed. | A byte arrives → stop, **no response**, exit 27. EOF (desktop closed) → cancel or abandon any running stage, no response, exit 24 |
| A2 | `probePort.BeginProbe(peer, host.UserSid, out TokenVerdict verdict)`: the peer-not-exited check, then `NativeChocolateyPeer.TryAcquireRequesterToken` under the §2.1 linked-token rules (as `BeginItemWithVerdict` does, without calling it). Returns an `IChocolateyProbeItem` only for `Accepted` | `tokenVerdict` = the closed verdict. Not `Accepted` → every `g1Status` = `NotEvaluated`, `observation = NotRun`; go to Stop |
| A3 | For **every** token of the compiled `CapabilityOperationAllowlist`, in ordinal order: `HelperDeploymentCapabilityCheck.Evaluate(route, Protection, peerImagePath)`, with the peer image path read from the held handle and `Protection` wrapping `item.RequesterCannotModify`. Each token is evaluated independently. **Cap (review M1), as `ReviewedHelperEndpoint.CapabilityAsync`:** each evaluation runs in `Task.Run(...).WaitAsync(remaining)`; on the cap an abandonment flag is set, after which `Protection` returns false without reaching the item. A budget already spent before a token starts counts as the cap | `g1Status[token]` = the closed `HelperProtocolCapabilityStatus`. Any token not `Supported` → `observation = NotRun`; go to Stop. **Cap reached, or EOF during A3 → no response, exit 24** (the abandoned evaluation is not awaited) |
| B′ | Exactly one `item.ObserveClient(remaining, ct)`, which calls `ChocolateyProfileObserver.ObserveClient(token, Helper, frozenInstalled, expectedFingerprint: null, remaining, ct)`. `frozenInstalled` = `ChocolateyReleaseList.Listed.Releases[^1].NativeVersion` (the newest compiled listed release; today 2.7.5). `remaining` = the probe budget minus the time elapsed; if it is ≤ 0, the result is `TimedOut` without any read. The observer enforces `remaining` itself; the entry also caps the call at `remaining` + the endpoint's 5-s exit margin | §4 fields 5–15. EOF cancels `ct`; the resulting `Cancelled` is never written: **no response, exit 24**. The outer cap reached → no response, exit 24 |
| Stop | Inside the native item: if B′ returned `Observed`, dispose its `ChocolateyHeldClientBinding` and require `IsDisposed`; refused and interrupted observations have already closed their held files. Then dispose the item (token), build the response through the probe codec, check that the inbound monitor saw no byte, and write the frame **once** | A binding that is not disposed, a codec throw, any fault, or cancellation → **no response**, exit 24. Write failure → exit 26. Written → exit 0 |

The probe item has no `ObserveProfile`, `QueryCandidateAsync`, `RevalidateBeforeLaunch` or `UpgradeAsync` member, and no launch permission. Its `RequesterCannotModify` has the same rule as `NativeChocolateyHelperItem.RequesterCannotModify` (ancestry on the root, then Node mode on the root and every file directly inside it). S3 may move that body into one shared internal static that both items call; the probe item never constructs or references `NativeChocolateyHelperItem`. The plain values that cross into Execution (`ChocolateyProbeValues`) carry the fingerprint's **length only**. The fingerprint string never leaves `Native/`. The peer handle is disposed after the write, as today.

**S3 observer additions** (values only; no behaviour change for existing callers):
- `failedStep`: the step whose check refused. Steps 1–7 are as numbered. Step 8 (source) and step 8a (installed value) both report `8`. Step 9 reports `9` (it can refuse only when `Compute` throws). The two disclosed refusals that run after step 7 passed (R5 `SetupAccessOverride`, §3a `UnlistedReleaseLeftover`) report `7`.
- `failedCheck` (review L5): which check at that step refused, so the response no longer relies on `refusalReason` and `refusalDetail` to tell them apart. Closed: `None` (not refused), `StepRule` (the numbered step's own rule), `InstalledVersion` (step 8a), `SetupAccessGuard` (the R5 guard after step 7), `ReleaseLeftover` (the §3a rule after step 7, whose input step 2 recorded).
- `failedAccess` (review L5; §10 answer 1): the access verdict behind the refusal. Closed: `None` (not refused), `Granted` (a `RequesterModification.Granted` verdict in Node or Ancestor mode, or the step-4 requester-owned check, which the observer treats as modification authority), `Unreadable` (a `RequesterModification.Unreadable` verdict), `Other` (the refusal was not an access verdict: a missing or extra node, a reparse point, a bound, a hash, an identity change, a version, the source, or a guard). The observer records the first non-`Denied` verdict that ends a step; S3 adds this as a value next to the existing `bool` returns. `RequesterModification` (`RequesterAccess.cs`) already has exactly `Denied`, `Granted` and `Unreadable`.
- `nodesEvaluated` counts the `RequesterAccess.Evaluate` calls in Node mode during B′; `ancestorsEvaluated` counts the directories evaluated in Ancestor mode by `EvaluateAncestry` during B′ (review L7). Both are fields of the **one B′ observation instance** (not static or shared counters), so A3, earlier runs and other threads cannot add to them. Each counter **saturates** at its §4.1 maximum instead of wrapping; a saturated value means "at least this many". A3 evaluations are not counted (§10 answer 3).

## 4. The probe response (helper → desktop)

### 4.1 Fields (exact order; 17 properties; compact JSON; every property always present)

| # | Name | JSON type | Closed domain | Null |
|---|---|---|---|---|
| 0 | `protocolVersion` | string | `"2"` | never |
| 1 | `kind` | string | `"qualify-chocolatey-helper"` | never |
| 2 | `nonce` | string | 64 of `0-9A-F`; equals the bootstrap nonce (fixed-time compare on the desktop) | never |
| 3 | `tokenVerdict` | string | the `TokenVerdict` names exactly (18): `Accepted`, `FactsMalformed`, `PeerNotLimited`, `PeerSidMismatch`, `EvaluationSidMismatch`, `AuthenticationIdMismatch`, `EvaluationNotLimited`, `EvaluationNotMediumIntegrity`, `EvaluationRestricted`, `EvaluationAppContainer`, `EvaluationAdministratorsEnabled`, `EvaluationPrivilegeNotAllowed`, `PeerProcessIdMismatch`, `PeerExited`, `PeerTokenUnreadable`, `NoLinkedToken`, `EvaluationTokenUnreadable`, `EvaluationNotIdentificationLevel` | never |
| 4 | `g1Status` | object | one property per compiled allow-list token (`"Update/<route>"`), ordinal order; each value `Supported` \| `Absent` \| `Incomplete` \| `Unreadable` \| `Mismatch` \| `NotEvaluated` | never |
| 5 | `observation` | string | `NotRun` \| `Observed` \| `Refused` \| `TimedOut` (no `Cancelled`: §3 B′, review L4) | never |
| 6 | `failedStep` | number \| null | integer 1–9 (§3) | null unless `Refused` |
| 7 | `failedCheck` | string | `None` \| `StepRule` \| `InstalledVersion` \| `SetupAccessGuard` \| `ReleaseLeftover` | never |
| 8 | `failedAccess` | string | `None` \| `Granted` \| `Unreadable` \| `Other` | never |
| 9 | `refusalReason` | string | `None` \| `MachineInputsUnsupported` \| `SourceNotCommunity` \| `InstalledMismatch` (no `ProfileMismatch`: there is no expected fingerprint) | never |
| 10 | `refusalDetail` | string | `None` \| `UnlistedReleaseLeftover` \| `SetupAccessOverride` | never |
| 11 | `nodesEvaluated` | number | integer 0–1,048,576 (saturating) | never |
| 12 | `ancestorsEvaluated` | number | integer 0–65,536 (saturating) | never |
| 13 | `observedClientVersion` | string \| null | `ChocolateyNativeVersion.Normalized` form, ≤ 128 B (the S12 grammar) | null unless Q10 |
| 14 | `fingerprintPresent` | boolean | — | never |
| 15 | `fingerprintLength` | number | `0` or `64` | never |
| 16 | `diagnostic` | string | one fixed token per row (Q13) | never |

No field carries a path, user name, SID, fingerprint value, pipe name, PID or free text. All fields have closed domains, and the `diagnostic` field is closed too.

### 4.2 Response rules (codec; `CreateProbeResponse` throws and `TryReadProbeResponse` returns false unless every rule holds)

| ID | Rule |
|---|---|
| Q1 | Exactly the 17 properties of §4.1 in order, and the compiled `g1Status` keys, with no other property. Strict UTF-8, no BOM, `MaxDepth = 2`, no comments or trailing commas. **Whole-frame re-encode equality**, which rejects escapes, whitespace, reordering, numbers sent as strings and booleans sent as strings. Integers are canonical (no sign, no exponent, no fraction, no leading zero). |
| Q2 | Fields 0 and 1 are the exact literals. Field 2 is 64 uppercase hex. |
| Q3 | Every enum value is defined and spelled exactly (`ToString` round-trip). Numeric and alias forms are rejected. `Cancelled` is not in the `observation` domain and is rejected. |
| Q4 | `g1Status` keys are exactly `CapabilityOperationAllowlist.Select(OperationToken)` in ordinal order, each once. |
| Q5 | `tokenVerdict ≠ Accepted` ⇒ every `g1Status` is `NotEvaluated` ∧ `observation = NotRun`. `tokenVerdict = Accepted` ⇒ no `g1Status` is `NotEvaluated`. |
| Q6 | `observation ≠ NotRun` ⇔ `tokenVerdict = Accepted` ∧ every `g1Status = Supported`. |
| Q7 | `observation = Refused` ⇔ `refusalReason ≠ None` ⇔ `failedStep ≠ null` ⇔ `failedCheck ≠ None` ⇔ `failedAccess ≠ None`. |
| Q8 | `SourceNotCommunity` ⇒ `failedStep = 8` ∧ `failedCheck = StepRule`. `InstalledMismatch` ⇔ `failedCheck = InstalledVersion`, and then `failedStep = 8`. `MachineInputsUnsupported` ⇒ `failedStep ∈ {1,…,7, 9}`. |
| Q9 | `refusalDetail = SetupAccessOverride` ⇔ `failedCheck = SetupAccessGuard`. `refusalDetail = UnlistedReleaseLeftover` ⇔ `failedCheck = ReleaseLeftover`. Either ⇒ `refusalReason = MachineInputsUnsupported` ∧ `failedStep = 7`. |
| Q10 | (review L3) `Observed` ⇒ `observedClientVersion` equals the newest compiled listed release (`frozenInstalled`; today `2.7.5`). `InstalledMismatch` ⇒ it is a compiled listed release strictly older than the newest one (step 7 in client mode requires the registration to equal the release step 2 matched, so no other value can reach step 8a). Otherwise it is null. With today's one-entry list, `InstalledMismatch` cannot occur. |
| Q11 | `fingerprintPresent` ⇔ `observation = Observed`. `fingerprintLength = 64` if present, else `0`. |
| Q12 | `NotRun` ⇒ both counts are 0. `Observed` ⇒ both counts are ≥ 1. Counts outside §4.1 ranges are rejected. |
| Q13 | `diagnostic` is the row's fixed token: `NotRun` from A2 → `probe-token-refused`; `NotRun` from A3 → `probe-capability-refused`; `Observed` → `probe-observed`; `Refused` → `probe-observation-refused`; `TimedOut` → `probe-observation-timed-out`. |
| Q14 | Frame size ≤ **2,048 B** (codec bound, inside the unchanged 16 KiB `PipeMessageFraming` limit). One frame, followed by pipe EOF. |
| Q15 | `failedAccess ∈ {Granted, Unreadable}` ⇒ `refusalReason = MachineInputsUnsupported` ∧ `failedCheck = StepRule` ∧ `failedStep ∈ {1, 4, 5, 6, 7}` (the steps that evaluate requester access). Every other refusal has `failedAccess = Other`. |
| Q16 | `TimedOut` ⇒ `failedStep = null`, every refusal field `None`, `observedClientVersion = null`; counts are what the instance reached (saturating). |

### 4.3 Bounds

The upper bound combines maxima that no valid frame can hold together: 8 allow-list keys of 64 B each with `NotEvaluated`, the longest verdict (32 B), `failedStep` 9, the longest check (`SetupAccessGuard`, 16 B), access (`Unreadable`, 10 B), reason (24 B) and detail (23 B), 7- and 5-digit counts, a 128-B version and the longest diagnostic (`probe-observation-timed-out`, 27 B). It is **1,338 B ≤ 2,048 B**. A passing frame with 6- and 5-digit counts is 520 B on today's one-token allow-list and 570 B with the client token added. The 4-byte length prefix is outside the bound.

The 8-key, 64-B assumption is pinned by a static test (review L8): the compiled `CapabilityOperationAllowlist` has at most 8 entries, and each `OperationToken` is at most 64 UTF-8 bytes (today 1 entry, `Update/ChocolateyMachineRuntimeLimitsV3`, 39 B). A larger allow-list fails that test and needs a spec revision of this bound.

### 4.4 Codec and goldens (S2)

- S2 adds the probe response codec only: `CreateProbeResponse` and `TryReadProbeResponse(frame, expectedNonce)`, a separate type or a separate region in Execution. There is no probe request codec.
- Goldens: one minimal frame per Q13 row; a passing frame on the one-token and the two-token allow-list; the largest valid frame; a `Refused` frame per `failedCheck` value and per `failedAccess` value.
- **Wire-name golden (review L6):** a test pins the 18 `TokenVerdict` names of §4.1 as an ordered literal list and compares it with `Enum.GetNames<TokenVerdict>()`. A rename, an addition or a removal of a verdict fails the test and needs a spec revision, because the enum name is the wire value. The same pin covers the `failedCheck` and `failedAccess` enums.
- Mutation tests: each property missing, duplicated or swapped; each Q rule violated; wrong-case enums; `Cancelled` as `observation`; `"1"`/`"3"` protocol; a frame of the update response fed to the probe reader and vice versa (both rejected).
- The package and client request and response goldens stay **byte-unchanged**.

## 5. Desktop side

### 5.1 Entry in `App.OnLaunched`

`QualificationProbeEntry.Classify(Environment.GetCommandLineArgs())` is pure and returns `Normal`, `Probe` or `Malformed`.
- `Probe` means exactly 2 elements, with element 1 ordinal-equal to `--qualify-chocolatey-helper`. Element 0 is not compared: the runtime may report the main assembly path rather than the apphost. Desktop identity is instead enforced by `TryHold` (both desktop files are in the binding set) and by the helper's peer verification and G1 peer-image hash.
- `Malformed` means any other line that contains the literal flag.

**Order (review M3).** `Classify` is the **first** statement of `OnLaunched`, **before** the existing `CurrentUserDataOperationSession.TryAcquire` call. Today a `TryAcquire` that throws opens `StartupFailureWindow`, and a null session opens `DataOperationBusyWindow` (App.xaml.cs, before `EnterBootstrap`). A probe branch placed after that call would show a window and write no result file.
- `Normal`: the existing code runs unchanged, including its `TryAcquire`, both windows and `EnterBootstrap`.
- `Malformed`: exits at once. It acquires nothing and creates no window, file, database or UAC.
- `Probe`: the branch acquires the gate itself with `CurrentUserDataOperationSession.TryAcquire(TimeSpan.Zero, …)` and maps a null session, a throw and `RecoveryRequired` to result outcomes (§5.2 step 2). It holds the session for the whole probe, including the helper exit wait and the result write. It never calls `DataOperationLease.EnterBootstrap`, reset handling, the database or any window.

**Thread.** `CurrentUserDataOperationSession.Dispose` throws unless it runs on the acquiring thread (`CurrentUserDataOperationLease.cs`, `Dispose`), and the legacy mutex is thread-affine. The branch therefore acquires and disposes the session on the `OnLaunched` thread, and runs `ProbeAsync` on a worker that thread waits for (`Task.Run(...).GetAwaiter().GetResult()`). No window is created.

### 5.2 Order of the desktop probe (`ReviewedHelperProbeTransport.ProbeAsync`; any failure ends the run with the stated outcome)

1. Create the fresh result folder (§5.3). If this fails, exit with no file.
2. Gate. Session `null` → `GateBusy`. `TryAcquire` threw → `GateUnavailable`. **`session.Context.RecoveryRequired` → `GateRecoveryRequired`** (review H1 b): the gate or the legacy mutex was abandoned by a process that ended while holding it (`AbandonedMutexException` → `RequireRecovery`), so a crashed operation may have left work behind. The **durable unresolved-operation barrier in the database is not consulted**: the probe opens no database, so it cannot see a barrier that a normal workflow reconciles after taking the gate (`CurrentUserDataOperationLease.RunCoreAsync` → `session.Reconcile`; ScheduledHost likewise). Steps 2 and 4 and the S7 host preconditions (§9) compensate.
3. The process token's elevation type is not `Limited` → `DesktopNotLimited`. This refuses an elevated desktop, UAC off (elevation type `Default`) and standard users before any prompt.
4. **Process check** (review H1 b). A read-only enumeration of process names (each `Process` object disposed; no process is opened for access or signalled). Any process named `choco`, or any process other than the current one whose name starts with `PCSteward` (ordinal, case-insensitive), or an enumeration failure → `ConflictingProcess`. This narrows, but cannot close, the window in which such a process starts later; E5 re-checks at C4.
5. Fixed paths and initial launch trust (`InstalledHelperLaunchTrust`) → `LaunchTrustFailed`. Then the restricted per-request pipe and a fresh 32-byte nonce.
6. `TryHold` for **every** compiled allow-list token. All must be `Supported`, holding, and name the same helper path and the same helper `FileIdInfo`, or the outcome is `CapabilityNotHeld` and every binding is released. Only an ADR-022 owner-only deployment (v6 ledger, `pinned-unsigned-v2`, matching binding-set hashes) can pass, so **the desktop raises no prompt outside an ADR-022 owner-only deployment**. (Any same-user process can still `runas` the helper directly with a probe line; that helper fails to connect (exit 22) or fails peer verification (exit 23), and writes no response.)
7. Final launch trust → `LaunchTrustFailed`. Then one `runas` launch with the §2 line. `ERROR_CANCELLED` → `ElevationDeclined`. No process → `HelperNotStarted`. Both release the bindings.
8. Wait for connection (30 s, or helper exit first) → `HelperNotConnected`. Then the post-connect `VerifyPeer`: client PID equals the helper PID, path, session, SID, signer, and `FileIdInfo` equal to every held binding → `PeerNotVerified`. **Nothing is ever written to the pipe.**
9. Read one frame within 11 minutes of connection and require EOF → `NoValidResponse`. Then `TryReadProbeResponse` with the nonce → `ResponseRejected`, which discards all response fields. Otherwise → `ResponseAccepted`.
10. Observe helper exit (5 s). Terminate on any failure path. The bindings, and with them the desktop's share-read holds on the PC Steward install files, are released only after exit is observed and the transport is sealed (A-M2), as in the update transport. Then write the result file and exit.

The probe reuses the update transport's fixed paths, launch trust, pipe, hold, launcher shape and peer check. It omits request construction, because there is no operation, and it omits the desktop's Chocolatey caller-trust check (`ChocolateyCallerTrusted` → `ChocolateyMachineUpdateExecutor.ValidateDesktopCallerClientTrust`). That check is a pre-launch gate of a real Chocolatey update, so **E3 does not cover it** (review L1; §8). The update transport's `LaunchHelper` is not changed.

### 5.3 Result file

- **Location:** `<Path.GetTempPath()>\pcsteward-exact-artifact\qualification-result.json`. The temp path must be fully qualified and local (no UNC or device prefix). It is canonicalised to its long form (8.3 names expanded), and every existing component from the drive root is opened with `FILE_FLAG_OPEN_REPARSE_POINT`. A reparse attribute, or a final path different from the canonical path, refuses.
- **Folder:** created with `CreateDirectoryW`. An existing folder refuses: there is no reuse or merge. The folder handle is held and must not be a reparse point.
- **File:** created with `CREATE_NEW`, written once in full, flushed and closed. The desktop never deletes or overwrites anything; cleanup is the owner's C7 step. Integrity assumes no same-user malware (design §4 Safety).

Closed JSON, schema `pc-steward-qualification-probe-result:2`, exact property order:

| Property | Type / domain |
|---|---|
| `schema` | the literal above |
| `desktopOutcome` | `ResponseAccepted` \| `GateBusy` \| `GateUnavailable` \| `GateRecoveryRequired` \| `DesktopNotLimited` \| `ConflictingProcess` \| `LaunchTrustFailed` \| `CapabilityNotHeld` \| `ElevationDeclined` \| `HelperNotStarted` \| `HelperNotConnected` \| `PeerNotVerified` \| `NoValidResponse` \| `ResponseRejected` |
| `capabilityStatus` | object: compiled allow-list token → `HelperProtocolCapabilityStatus` name or `NotEvaluated` (the desktop's `TryHold` results) |
| `initialLaunchTrust`, `finalLaunchTrust`, `helperProcessCreated`, `pipeConnected`, `desktopPeerVerified`, `helperFileIdEqual`, `responseFrameReceived`, `pipeEofObserved`, `helperExitObserved`, `bindingHeldUntilExit`, `terminationFailed` | booleans (`helperProcessCreated` = UAC accepted; `bindingHeldUntilExit` = every binding `IsHolding` when exit was observed) |
| `helperExitCode` | integer or null |
| `pins` | null unless step 6 held; otherwise `ledgerSha256`, `capabilityFileSha256`, `desktopImageSha256`, `helperImageSha256` (64 uppercase hex, from the held binding and the ledger bytes it verified), `signerThumbprint` (40 uppercase hex) and `signerNotAfterUtc` (ISO 8601 `Z`), from the held helper image |
| `probe` | null unless `ResponseAccepted`; otherwise fields 3–16 of the accepted response, in order (no nonce) |
| `startedAtUtc`, `finishedAtUtc` | ISO 8601 `Z` |

The schema version moves to `:2` because the outcome domain and the `probe` fields changed; no `:1` file was ever produced. The helper's verification of the desktop is not a separate fact: a helper writes only after its own handle-based `VerifyPeer` and the G1 peer-image hash, so `ResponseAccepted` implies it. The file holds no path, user name, SID, fingerprint value, nonce or pipe name.

### 5.4 Exit behaviour

After the file is closed and the session disposed (on the acquiring thread), the desktop calls `Environment.Exit(code)`, with `0` only for `ResponseAccepted` and a fixed non-zero code per other outcome (S4 fixes the table). WinUI may not pass the code through, so **the result file is authoritative**. A missing file means no evidence.

### 5.5 What the probe path never does

It never opens or creates the database, runs a migration or reconciliation, takes a `DispatchClaimed` or any claim, writes history, scans, registers or touches the scheduled task, completes or verifies a factory reset, creates a window or view model, reads SQLite or remote data, uses the network, or sends a request frame. `ScheduledHost`, scan workflows and every UI path cannot reach it.

### 5.6 Effects (for the amendment's limits)

Reads; the desktop's share-read holds on the PC Steward install files (`TryHold` bindings) from step 6 until the helper's exit is observed; the helper's share-read holds on Chocolatey files during B′ (released at Stop). Both are bounded by the 10-minute budget (plus the 11-minute desktop read and 5-s exit wait) and are expected to last seconds. The observer may read and hash up to 512 MiB of code and 512 MiB of archives (`ChocolateyProfileObserver` bounds), so "seconds" is an expectation, not the bound. One result file in a fresh `%TEMP%` folder. Same-user processes can start the probe entry (§9).

## 6. Compatibility (all fail closed with no result claim)

| Pair | Behaviour |
|---|---|
| New desktop, old helper | Within one deployment this cannot pass `TryHold`, because the ledger hashes bind both binaries. If it is forced (for example in a fixture), the 10-argument line goes to protocol 1, which exits 20 before connecting. The desktop records `HelperNotConnected` with `helperExitCode = 20`. |
| Old desktop, new helper | The old desktop never emits `--kind`, so the new helper serves updates exactly as today. The old desktop ignores `--qualify-chocolatey-helper` and **starts its normal UI** (database included), so there is **no result file**. Under Decision 3 (c), the Part B deployment is such an old desktop. §9 makes S7 refuse C3 there. |
| Probe frame to an update reader, or update frame to the probe reader | Rejected on the exact property set (Q1; S14a/field-set checks). In normal operation neither happens, because each mode reads only its own codec. |
| Update frame sent to a probe-mode helper | Never parsed. The inbound monitor ends the run with no response (exit 27). |

## 7. Architecture-test obligations

S3 (helper):
- H1. The probe entry, probe bootstrap, `IChocolateyProbePort`, `IChocolateyProbeItem`, **their native implementations** and their Execution callers reference none of: `LaunchJob`, `LaunchJobNativeMethods`, `NativeLaunchSpec`, `NativeCommandLine`, `ChocolateyCompiledCommand`, `ChocolateyClientQueries`, `ChocolateyClientUpdateArguments`, `ChocolateyExactListArguments`, `ChocolateyClientUpdateWatchdog`, `IChocolateyHelperItem` and its stage C/D/Launch members, `NativeChocolateyHelperItem`, `IChocolateyHelperQualificationPort`, `NativeChocolateyHelperQualification` (including `BeginItemWithVerdict`), `ReviewedHelperEndpoint`, `ReviewedHelperBootstrap.RunAsync` and `OpenVerifiedPeer`, `ReviewedUpdateWireCodec.TryReadRequest`, `CreateResponse`, `CreateClientResponse`, or `PipeMessageFraming.ReadAsync`. The native probe implementation may touch `ChocolateyHeldClientBinding` only through `Dispose` and `IsDisposed` (review M2).
- H2. `IChocolateyProbeItem` declares no launch, query or revalidation member. Its single implementation is in `Native/`. No field, property or return value of the probe path holds a `ChocolateyHeldClientBinding`, and the binding obtained from `ObserveClient` is disposed in the same method.
- H3. A behavioural test with an ordered fake writer: the binding is `IsDisposed` before the response bytes are created, and a binding that is not disposed yields no response.
- H4. The Execution allow-list of `Native` names grows only by `IChocolateyProbePort`, `IChocolateyProbeItem`, `ChocolateyProbeValues`, `TokenVerdict` and the two closed enums behind `failedCheck` and `failedAccess`. `ChocolateyProbeValues` has no fingerprint string member.
- H5. Only the native port constructs the probe item. `ElevatedHelperEntryPoint` constructs only the native ports.
- H6. The dispatch and mutual-refusal tests of §2: a probe line against all three update parsers; elements 2–9 of a probe line against the probe parser; the revision-1 order (kind at 8–9) refused with exit 20.
- H7. The inbound-byte test (exit 27, no response), the EOF test during A3 and during B′ (no response, exit 24) and the A3 cap test (a `Protection` that blocks past the budget → no response, exit 24, and a later `Protection` call does not reach the item).
- H8. The P0 exit codes: parse, Windows or elevation failure 20; `TryResolve` 21; connect 22; server PID or peer 23 (review L9).
- H9. The node and ancestor counts are per observation instance and saturate (review L7): two sequential observations report independent counts; a fake over the cap reports the cap.

S2 (wire): the `TokenVerdict`, `failedCheck` and `failedAccess` wire-name goldens (§4.4) and the allow-list size test (§4.3).

S4 (desktop):
- D1. `ProbeAsync` has exactly one `src` caller: the `OnLaunched` probe branch, through `QualificationProbeEntry`.
- D2. Nothing in `ScheduledHost`, `Application`, scan workflows, view models or windows references the probe types.
- D3. The update transport's launcher never emits `--kind` (argument-list test). The probe launcher is the only `src` emitter of the literal, and emits it at indices 0–1.
- D4. The existing pins on `HasSupportedMode`, `CapabilityOperationAllowlist` and `Supports` are unchanged, and their tests are unmodified.
- D5. The probe branch type references no `SqliteDatabase`, `FactoryReset`, `WindowsCurrentUserScheduledTaskRegistrar`, `DataOperationLease`, `Window` or view-model type.
- D6. Result-writer tests: an existing folder, a reparse component, a short-name temp path, a UNC temp path, and the closed schema (no path, SID or fingerprint value).
- D7. Entry order (review M3): `Classify` precedes the only `TryAcquire` on the normal path; with a test gate held, a probe start yields `GateBusy` in the result file and opens no window; with an abandoned test gate, `GateRecoveryRequired`; a throwing acquire yields `GateUnavailable`; the session is disposed on the acquiring thread.
- D8. The process check: a fake enumeration with a `choco` process, with another `PCSteward*` process, and a throwing enumeration each yield `ConflictingProcess` before any launch-trust call.

## 8. Interpretation (design §2 evidence; each run is one approved C3)

| Evidence | PASS | FAIL (a finding; no retry) | Inconclusive (no evidence) |
|---|---|---|---|
| E1 token match | `ResponseAccepted` ∧ `tokenVerdict = Accepted` | `ResponseAccepted` ∧ any other verdict (the verdict is the finding; for example `EvaluationPrivilegeNotAllowed`: the filtered token holds a privilege outside the allow-list). UAC off does not reach the helper: its elevation type is `Default`, so the desktop stops at `DesktopNotLimited` (review L2) | No accepted response |
| A3 / G1 | Every `g1Status = Supported` | Any other status after `Accepted` | Verdict not `Accepted`, or no response |
| E2 `AccessCheck` | `observation = Observed` ∧ `observedClientVersion = 2.7.5` (the newest listed release) ∧ `fingerprintPresent` ∧ `fingerprintLength = 64` ∧ both counts ≥ 1 ∧ `failedStep = null` | `Refused` with any reason other than `InstalledMismatch`. Step, check, access, reason and detail are the finding; `failedAccess` tells an unprotected tree (`Granted`) from an unreadable one (`Unreadable`), and both keep the route unusable on this host | `NotRun`, `TimedOut`, `InstalledMismatch` (the host runs an older listed release; a precondition, not an enforcement failure; review L3), or no response |
| E3 real transport | All result booleans true except `terminationFailed = false`; `helperExitCode = 0`; `pins` equal C1; the owner saw exactly one prompt for the signed helper whose details show `--kind qualify-chocolatey-helper` right after the program path. **Not covered:** the desktop's Chocolatey caller-trust check (§5.2) | Any false fact after `helperProcessCreated`; `CapabilityNotHeld` or `LaunchTrustFailed` on a C1-pinned deployment; `PeerNotVerified`, `NoValidResponse` or `ResponseRejected`; a prompt that differs from the expected one | `ElevationDeclined` (not repeated without new approval), `GateBusy`, `GateUnavailable`, `GateRecoveryRequired`, `ConflictingProcess`, `DesktopNotLimited`, `HelperNotStarted`, or no result file |
| E5 nothing changed | C4 = C2 (database and Chocolatey hashes), no `choco` process, only the result folder is new | Any difference | Baseline missing |

Overall **PASS** only if all five rows pass. Any FAIL stops use (Decision 2). An inconclusive run proves nothing and needs a new approval to repeat. A PASS proves **UAC and pre-launch enforcement only** for the deployment and pins it ran on, excluding the caller-trust check.

## 9. Deployment-order obligations under Decision 3 (c) (for S7 and the activation change)

**S7 obligations (Part C of the activation plan):**
1. **C1 stops unless the installed desktop's SHA-256 equals a build record marked "probe included".** The G3 build record for the activated build must carry that mark, set only when the build commit contains S2–S6. Any other hash → `STOP: not a probe build`, with no C2 or C3.
2. **C3 is never offered on the Part B deployment.** Part B deploys a pre-probe build (§6: an old desktop starts its normal UI and writes no result file), so C1 always stops there.
3. **Host preconditions before C1:** no `choco` or `PCSteward*` process (read-only `Get-Process`); no Chocolatey operation in progress; and, because the probe does not consult the durable unresolved-operation barrier (§5.2 step 2), the owner opens PC Steward normally, confirms that no unresolved-operation notice is shown, and closes it before C2 takes the baseline.

**Residual gap.** Under (c), the activation change admits the Chocolatey routes in code (`HasSupportedMode`, the client capability token) before any exact-artifact evidence exists. Decision 2 ("confirm no Chocolatey review until the probe passes on this deployment") then holds **as procedure, not code**: between the activation deploy and a recorded PASS, the desktop would enable Confirm.

**Code-level guard: feasibility and the smallest option (owner decision D4, not decided here).**
- *A compiled requirement is not feasible.* The ledger, capability file hash and signer are created at deploy time, after the build, so the build cannot name the deployment it must have evidence for.
- *A marker written by the probe is not proposed.* It would add a state write to the probe (breaking §5.5 and E5 "only the result folder is new"), and the probe cannot judge E3 (the prompt the owner saw) or E5 (C4 = C2).
- **Proposed smallest option (G-a): an owner-placed pass record checked before UAC.** After a recorded overall PASS, the owner copies the result file, as an explicit S7 step, to a fixed name in the PC Steward state root. The activation change then (i) adds a review unavailability reason that keeps Confirm disabled for every Chocolatey route unless that file exists, and (ii) has the transport, after `TryHold` and before UAC for any Chocolatey route, read it with the §5.3 closed reader (bounded, reparse-refusing) and refuse before elevation unless `desktopOutcome = ResponseAccepted`, the E1, A3 and E2 rows pass, and `pins` equal the held binding's ledger, capability file, desktop and helper hashes. A redeploy changes the pins, so the guard closes again until a new PASS is recorded. Limits: same-user code can forge the file, so it guards against owner or procedure error, not malware (the same limit as the result file); E3 and E5 stay owner judgments; factory reset removes it with the state root (fail closed), and the Clear scan data and Clear downloaded files semantics would need to keep it, which ADR text must state.
- *Alternative (G-0): procedure only*, as Decision 2 stands today. No code cost; one owner mistake allows a real Chocolatey update on a deployment with no exact-artifact evidence.

G-a belongs to the activation change (G6, §8.6 admission gates), not to S2–S6. Choosing it, G-0 or another option is the owner's decision.

## 10. Answers to the revision-1 open questions (recorded)

1. **Access verdict for the failed step: yes.** Added as `failedAccess` (`None` / `Granted` / `Unreadable` / `Other`), with Q15. The revision-1 list (`RequesterModifiable` / `RequesterOwned` / `Unreadable` / `Reparse` / `OverBound`) is replaced: `RequesterModification` already has `Granted` and `Unreadable`, the requester-owned check maps to `Granted`, and reparse and bound refusals are `Other`.
2. **ADR-018 note: yes.** A **Proposed** cross-note is added at the end of ADR-018. It takes effect only with the owner's acceptance of the ADR-025 amendment.
3. **A3 counts: not merged into the B′ counts.** Separate G1 counts are optional; they would be a separate pair of fields with their own bounds, and they are not added in this revision.
4. **No package-mode probe.** AQ1UAC stays as cheap supporting evidence (Part A passed it on 2026-11-03). Probe E3 is the evidence for the signed apphost.

## 11. Review disposition (revision 2)

Independent review of revision 1, verdict **"Sound with changes"**. Each point was checked against `src` at `0835801` (unchanged at `febf1ac`) before it was applied.

| # | Sev. | Finding | Code check | Disposition |
|---|---|---|---|---|
| H1 a | High | Amendment "No other rule is relaxed" is false: ADR-018 lists four launch parameters and one accepted canonical request | ADR-018 Decision ¶2–3 | Applied: exception (d) in the ADR-025 amendment; Proposed cross-note in ADR-018; §2 names the exception. |
| H1 b | High | Mutating workflows reconcile the DB barrier after taking the gate; the probe has no DB and ignores `RecoveryRequired` | Confirmed: `RunCoreAsync` calls `session.Reconcile` after acquire; ScheduledHost reconciles and also returns on `RecoveryRequired`; `RequireRecovery` on `AbandonedMutexException` (lease and `CurrentUserOperationGate`) | Applied: `GateRecoveryRequired`; barrier not consulted is stated; read-only process check (`ConflictingProcess`); host preconditions (§9). |
| M1 | Med | "A few seconds of holds" is not a bound; A3 is uncapped | Confirmed: 512 MiB code and 512 MiB archive aggregates; `CapabilityAsync` uses `WaitAsync` and `capabilityAbandoned` | Applied: §5.6 wording; A3 cap with abandonment; B′ outer cap; cap or EOF → no response, exit 24. |
| M2 | Med | P0 reuses `OpenVerifiedPeer` on `IChocolateyHelperQualificationPort`, whose `BeginItem` returns an item with `UpgradeAsync` | Confirmed: `OpenVerifiedPeer` is private and typed on that port | Applied: `IChocolateyProbePort` with its own `OpenPeer`; H1 bans and applies to native implementations; binding only via `Dispose`/`IsDisposed`. |
| M3 | Med | `OnLaunched` opens `StartupFailureWindow` / `DataOperationBusyWindow` before `EnterBootstrap` | Confirmed (App.xaml.cs); `Dispose` throws off the acquiring thread | Applied: `Classify` first; the probe branch takes the gate; normal path unchanged; same-thread dispose; D7. One difference: `Malformed` acquires nothing, because it has no effect to protect. |
| M4 | Med | Same-user start and the "no prompt elsewhere" claim | Any process can `runas` the helper; with no desktop pipe it fails to connect (exit 22), and against a non-desktop pipe server it fails `VerifyPeer` (exit 23) | Applied in §5.2 step 6, §5.6 and amendment item 3. |
| L1 | Low | Caller-trust check skipped | Confirmed: `ChocolateyCallerTrusted` in `ExchangeAsync` | Applied: excluded from E3 (§5.2, §8). |
| L2 | Low | §8 E1 example wrong | UAC off → elevation type `Default` → step 3 | Applied. |
| L3 | Low | Q10 | Confirmed: client step 7 requires the registration to equal the matched listed release; step 8a compares with `frozenInstalled` | Applied: Q10; `InstalledMismatch` is Inconclusive. |
| L4 | Low | `Cancelled` cannot be written | EOF → no response | Applied: removed; codec rejects it; diagnostic renamed `probe-observation-timed-out`. |
| L5 | Low | Closed `failedCheck`; access verdict | `RequesterModification` = `Denied`/`Granted`/`Unreadable` | Applied: fields 7–8, Q7–Q9, Q15; bounds recomputed. |
| L6 | Low | Pin the 18 `TokenVerdict` wire names | 18 names confirmed in `RequesterTokenPolicy.cs` | Applied: §4.4 golden. |
| L7 | Low | Count scope and caps | — | Applied: per-instance, saturating; H9. |
| L8 | Low | Allow-list size assumption | Today 1 token, 39 B | Applied: static test (§4.3). |
| L9 | Low | P0 exit codes | Confirmed: 20 before connection; 21/22/23 constants in `ReviewedHelperBootstrap` | Applied: §3 P0 rows; H8. |
| L10 | Low | Kind position in the UAC details | `SelectsProtocol2` and both parsers check length first | Applied: kind at indices 0–1; grammar, dispatch, mutual refusal, H6, D3. |
| L11 | Low | Desktop holds missing from effects | `TryHold` bindings released after exit | Applied: §5.6; amendment item 3. |
| 10 | — | Decision 3 (c) ordering risk | `HasSupportedMode` and the review unavailability reasons are the only admission points | Applied: §9 S7 obligations; guard options; G-a proposed as owner decision D4, not decided. |

Result-file schema moved to `:2`; response grew from 15 to 17 properties. A fresh-context review of revision 2 is needed before S2 starts.
