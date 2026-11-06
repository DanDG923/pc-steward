# Task 50 — Chocolatey helper qualification probe: protocol-2 addendum (slice S1 specification)

Requirements: `FR-UPD-15`, `TM-03`, `TM-04`, `TM-07`, `TM-13`; ADR-018, ADR-022, ADR-025 (durable dispatch; follow-up controls), ADR-031 (decision 7).

Status: **revision 4, precise wording fixes after a further review (no design change); see §11.3. Revision 3 followed the second independent review (verdict "Sound with changes"; no design change), and revision 2 the first.** It adds no code, endpoint, deployment, UAC prompt or host action. The probe stays undeployable until the owner accepts the [ADR-025 amendment](../adr/ADR-025-TYPED-OPERATION-JOBS-AND-HELPER.md#proposed-amendment-2026-11-03--owner-started-qualification-probe) and the [ADR-018 cross-note](../adr/ADR-018-HELPER-IPC-AND-ELEVATION.md#proposed-cross-note-2026-11-03--qualification-probe-bootstrap) separately, and each run needs its own approval.

Authority: [exact-artifact test design rev 2](TASK_50_EXACT_ARTIFACT_TEST_DESIGN.md) §4 (A), and the owner decisions recorded there on 2026-11-03: **Decision 1 Yes, Decision 2 Yes, Decision 3 (c)**. This record is an addendum to the [protocol-2 fact extension rev 3](TASK_50_PROTOCOL2_REVIEWED_FACT_EXTENSION.md). It builds on the [helper endpoint and native observer design rev 6](TASK_50_HELPER_ENDPOINT_AND_NATIVE_OBSERVER.md) (§2.1, §3 steps 1–9, §5, §6 G1) and the [ADR-031 design](TASK_50_ADR031_CLIENT_SELF_UPDATE_DESIGN.md) (§3.6 client-mode observation, R5, §3a). Source state: `src` at `0835801` (unchanged at `febf1ac` and at the revision-2 head `7044e82`).

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

`t0` is taken at pipe connection. The probe budget is **10 minutes from `t0`**, below the desktop's 11-minute response budget (as in N4). Every stage that does real work runs under a real-time cap (A3, B′ below). A3 ends inside the budget. B′ is capped at the remaining budget plus the endpoint's 5-s exit margin, and the Stop write has its own 5-s window, so the helper's last write can end up to 10 min 10 s after `t0`. That is still under the desktop's 11-minute read, so the helper never writes after the desktop has given up.

**Port (review M2).** The probe uses its own port, `IChocolateyProbePort`, with its own `OpenPeer(int pipeServerProcessId)` and `BeginProbe(peer, expectedSid, out TokenVerdict verdict)` that returns an `IChocolateyProbeItem`. The probe bootstrap has its own `OpenVerifiedProbePeer`, which makes the same calls as today's private `ReviewedHelperBootstrap.OpenVerifiedPeer` (`RestrictedNamedPipe.TryGetServerProcessId`, `OpenPeer`, `WindowsPeerIdentityVerifier.VerifyPeer`) but is typed on `IChocolateyProbePort`. It does not reuse `OpenVerifiedPeer`, because that method takes `IChocolateyHelperQualificationPort`, whose `BeginItem` returns an `IChocolateyHelperItem` with `UpgradeAsync`. `IChocolateyPeer`, `ReviewedHelperHost.TryResolve` and the exit-code constants are shared.

| Stage | Work | Result |
|---|---|---|
| P0 | Parse (§2), Windows check and elevation check | Failure → exit **20** before any connection (as both existing paths) |
| P0 (cont.) | `ReviewedHelperHost.TryResolve` | Null → exit **21** (`LayoutUnavailable`) |
| P0 (cont.) | Connect (30 s) | Failure → exit **22** (`ConnectionFailed`) |
| P0 (cont.) | Server-PID check, `probePort.OpenPeer`, handle-based `VerifyPeer` | Failure → exit **23** (`PeerNotVerified`). P0 never writes a response. |
| P1 | Start the **inbound monitor**: one pending 1-byte read on the pipe. The byte is never parsed. | A byte arrives → stop, **no response**, exit 27. EOF (desktop closed) → cancel or abandon any running stage, no response, exit 24 |
| A2 | `probePort.BeginProbe(peer, host.UserSid, out TokenVerdict verdict)`: the peer-not-exited check, then `NativeChocolateyPeer.TryAcquireRequesterToken` under the §2.1 linked-token rules (as `BeginItemWithVerdict` does, without calling it). Returns an `IChocolateyProbeItem` only for `Accepted` | `tokenVerdict` = the closed verdict. Not `Accepted` → every `g1Status` = `NotEvaluated`, `observation = NotRun`; go to Stop |
| A3 | For **every** token of the compiled `CapabilityOperationAllowlist`, in ordinal order: `HelperDeploymentCapabilityCheck.Evaluate(route, Protection, peerImagePath)`, with the peer image path read from the held handle and `Protection` wrapping `item.RequesterCannotModify`. Each token is evaluated independently. **Cap (review M1), as `ReviewedHelperEndpoint.CapabilityAsync`:** each evaluation runs in `Task.Run(...).WaitAsync(remaining)`; on the cap an abandonment flag is set, after which `Protection` returns false without reaching the item. A budget already spent before a token starts counts as the cap. **Abandonment ordering (S3 review):** before each `Task.Run` the entry checks the run's token and the remaining budget and starts nothing if either is spent; during the evaluation `Protection` also reads the run's token and the token's own cap directly, and a registration sets the flag, so abandonment holds from the moment of cancellation or the cap, before the entry's wait observes it; a status that completes at that moment is not used | `g1Status[token]` = the closed `HelperProtocolCapabilityStatus`. Any token not `Supported` → `observation = NotRun`; go to Stop. **Cap reached, or EOF during A3 → no response, exit 24** (the abandoned evaluation is not awaited) |
| B′ | Exactly one `item.ObserveClient(remaining, ct)`, which calls `ChocolateyProfileObserver.ObserveClient(token, Helper, frozenInstalled, expectedFingerprint: null, remaining, ct)`. `frozenInstalled` = `ChocolateyReleaseList.Listed.Releases[^1].NativeVersion` (the newest compiled listed release; today 2.7.5; valid only while the list has one entry, see the re-spec trigger below). `remaining` = the probe budget minus the time elapsed; if it is ≤ 0, the result is `TimedOut` without any read. The observer enforces `remaining` itself; the entry also caps the call at `remaining` + the endpoint's 5-s exit margin | §4 fields 5–15. EOF cancels `ct`; the resulting `Cancelled` is never written: **no response, exit 24**. The outer cap reached → no response, exit 24 |
| Stop | Inside the native item: if B′ returned `Observed`, dispose its `ChocolateyHeldClientBinding` and require `IsDisposed`; refused and interrupted observations have already closed their held files. Then dispose the item (token), build the response through the probe codec, check that the inbound monitor saw no byte, and write the frame **once**. The monitor stop and the write share one window of `min(ResponseWrite, ResponseMargin)` (5 s each in `ReviewedHelperEndpointBudgets.Production`), taken from the response margin as in `ReviewedHelperEndpoint`; the window also bounds a write that does not observe its token (review L-3) | A binding that is not disposed, a codec throw, a fault inside a stage (caught by the entry), or cancellation → **no response**, exit 24. Write failure or window spent → exit 26. Written → exit 0. An exception that escapes the entry after the connection (for example from the monitor setup or a disposal) → exit **26**, as the update bootstrap's catch-all: the bootstrap cannot then tell whether a frame was written, so it does not claim "no response" (24); neither code carries a result, only a written frame the desktop accepts does |

**In-flight disposal (N4b, review L-3).** After the A3 cap or the B′ outer cap, the abandoned `Protection` → `RequesterCannotModify` call or `ObserveClient` call may still be running when the entry disposes the item. `ReviewedHelperEndpoint` records the same situation as its N4b obligation for `ObserveProfile` and the later stages. The probe item must tolerate `Dispose` while either call is in flight: `Dispose` does not wait for the call and does not throw; the running call ends as a refusal or fault, never as a crash of the entry; and after `Dispose` the item starts nothing on the machine and opens no handle beyond those the running call had already started to open. A binding that an abandoned `ObserveClient` returns after `Dispose` is disposed by the item and never handed out (H10). **Residual (S3 review, accepted; the shared rule is unchanged):** "the running call" includes an abandoned `RequesterCannotModify` inside the shared `ChocolateyRequesterProtection.CannotModify`, which evaluates the ancestry, the root and then the listed files one by one; after `Dispose` it may still open read-only handles for the remaining nodes and listed files of that call, exactly as the update item's abandoned check does. The exposure is small (read-only opens of a bounded, compiled list; no new call is admitted, nothing is started and no write is made) and ends when that call returns or, at the latest, at helper process exit.

**Re-spec trigger for `frozenInstalled` (review L-1).** Step 8a compares the registration with `frozenInstalled`, and in client mode step 7 already requires the registration to equal the listed release step 2 matched (`ChocolateyProfileObserver`, `LibraryAndStore`). With two or more listed releases, a host on an older listed release would therefore always end at `InstalledMismatch` (Inconclusive, §8). It could never pass, so under Decision 2 it could never confirm a Chocolatey review. The observer has no option today to compare with the matched release instead: `frozenInstalled` is a required parameter of `ObserveClient`, and the matched release is private to the observation. This revision therefore keeps the newest listed release and records a trigger: **before any build lists a second release, this spec must be revised.** The expected direction is a probe-only S3 observer option in which step 8a compares with the release step 2 matched. S3 pins the trigger with a static test that `ChocolateyReleaseList.Listed.Releases.Count == 1`; adding a release fails it (H11).

The probe item has no `ObserveProfile`, `QueryCandidateAsync`, `RevalidateBeforeLaunch` or `UpgradeAsync` member, and no launch permission. Its `RequesterCannotModify` has the same rule as `NativeChocolateyHelperItem.RequesterCannotModify` (ancestry on the root, then Node mode on the root and every file directly inside it). S3 may move that body into one shared internal static that both items call; the probe item never constructs or references `NativeChocolateyHelperItem`. The plain values that cross into Execution (`ChocolateyProbeValues`) carry the fingerprint's **length only**. The fingerprint string never leaves `Native/`. The peer handle is disposed after the write, as today.

**S3 observer additions** (values only; no behaviour change for existing callers):
- `failedStep`: the step whose check refused. Steps 1–7 are as numbered. Step 8 (source) and step 8a (installed value) both report `8`. Step 9 reports `9` (it can refuse only when `Compute` throws). The two disclosed refusals that run after step 7 passed (R5 `SetupAccessOverride`, §3a `UnlistedReleaseLeftover`) report `7`.
- `failedCheck` (review L5): which check at that step refused, so the response no longer relies on `refusalReason` and `refusalDetail` to tell them apart. Closed: `None` (not refused), `StepRule` (the numbered step's own rule), `InstalledVersion` (step 8a), `SetupAccessGuard` (the R5 guard after step 7), `ReleaseLeftover` (the §3a rule after step 7, whose input step 2 recorded).
- `failedAccess` (review L5; §10 answer 1): the access verdict behind the refusal. Closed: `None` (not refused), `Granted` (a `RequesterModification.Granted` verdict in Node or Ancestor mode, or the step-4 requester-owned check, which the observer treats as modification authority), `Unreadable` (a `RequesterModification.Unreadable` verdict), `Other` (the refusal was not an access verdict: a missing or extra node, a reparse point, a bound, a hash, an identity change, a version, the source, or a guard). The observer records the first non-`Denied` verdict that ends a step; S3 adds this as a value next to the existing `bool` returns. `RequesterModification` (`RequesterAccess.cs`) already has exactly `Denied`, `Granted` and `Unreadable`.
- `nodesEvaluated` counts the `RequesterAccess.Evaluate` calls in Node mode during B′; `ancestorsEvaluated` counts the directories evaluated in Ancestor mode by `EvaluateAncestry` during B′ (review L7). Both are fields of the **one B′ observation instance** (not static or shared counters), so A3, earlier runs and other threads cannot add to them. Each counter **saturates** at its §4.1 maximum instead of wrapping; a saturated value means "at least this many". A3 evaluations are not counted (§10 answer 3).
- **Threading through shared static code (review L-4).** The verdicts and evaluations above happen inside static code that existing callers share, so the instance cannot see them today:
  - `ProtectedTreeWalker.Walk` evaluates the root and every child in Node mode (`WalkCore`, the root `Evaluate` and the per-child `Evaluate`) and folds the verdict into `TreeWalkRefusal.RequesterModifiable` or `TreeWalkRefusal.Unreadable`. `Unreadable` also comes from a failed open (`ProtectedNodeReader.TryOpen`, `OpenListedChild`), which is not an access verdict. S3 adds an optional per-call sink parameter that receives the Node-mode count and the verdict behind a refusal, so a failed open reports `failedAccess = Other` and only an `Evaluate` result reports `Granted` or `Unreadable`.
  - `RequesterAccess.EvaluateAncestry` evaluates each ancestor in Ancestor mode. It also returns `RequesterModification.Unreadable` for an ancestor open failure, a volume change or a drive-root path. S3 does not split these: any `Unreadable` result of `EvaluateAncestry` reports `failedAccess = Unreadable`. The same sink receives the Ancestor-mode count.
  - The sink belongs to the B′ observation instance and is passed down explicitly. No static, thread-static or ambient state carries it. With no sink (every existing caller), behaviour is unchanged.
  - Existing callers: the observer's two `Walk` calls (the step-1 root walk in `Roots` and the step-4 walk in `HttpCache`), its `EvaluateAncestry` call (through its private `Ancestry`), and `NativeChocolateyHelperItem`'s `EvaluateAncestry` call in `RequesterCannotModify`. H12 requires a test that their results are unchanged.

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
| Q10 | (review L3) `Observed` ⇒ `observedClientVersion` equals the newest compiled listed release (`frozenInstalled`; today `2.7.5`). `InstalledMismatch` ⇒ it is a compiled listed release strictly older than the newest one (step 7 in client mode requires the registration to equal the release step 2 matched, so no other value can reach step 8a). Otherwise it is null. With today's one-entry list, `InstalledMismatch` cannot occur. A second listed release requires a spec revision first (§3 re-spec trigger; H11). |
| Q11 | `fingerprintPresent` ⇔ `observation = Observed`. `fingerprintLength = 64` if present, else `0`. |
| Q12 | `NotRun` ⇒ both counts are 0. `Observed` ⇒ both counts are ≥ 1. Counts outside §4.1 ranges are rejected. |
| Q13 | `diagnostic` is the row's fixed token: `NotRun` from A2 → `probe-token-refused`; `NotRun` from A3 → `probe-capability-refused`; `Observed` → `probe-observed`; `Refused` → `probe-observation-refused`; `TimedOut` → `probe-observation-timed-out`. |
| Q14 | Frame size ≤ **2,048 B** (codec bound, inside the unchanged 16 KiB `PipeMessageFraming` limit). One frame, followed by pipe EOF. |
| Q15 | `failedAccess ∈ {Granted, Unreadable}` ⇒ `refusalReason = MachineInputsUnsupported` ∧ `failedCheck = StepRule` ∧ `failedStep ∈ {1, 4, 5, 6, 7}` (the steps that evaluate requester access). Every other refusal has `failedAccess = Other`. |
| Q16 | `TimedOut` ⇒ `failedStep = null`, every refusal field `None`, `observedClientVersion = null`; counts are what the instance reached (saturating). |

### 4.3 Bounds

The upper bound combines maxima that no valid frame can hold together: 8 allow-list keys of 64 B each with `NotEvaluated`, the longest verdict (32 B), `failedStep` null, the longest check (`SetupAccessGuard`, 16 B), access (`Unreadable`, 10 B), reason (24 B) and detail (23 B), 7- and 5-digit counts, a 128-B version and the longest diagnostic (`probe-observation-timed-out`, 27 B). It is **1,341 B ≤ 2,048 B**. A passing frame with 6- and 5-digit counts is 520 B on today's one-token allow-list and 570 B with the client token added. The 4-byte length prefix is outside the bound.

The 8-key, 64-B assumption is pinned by a static test (review L8): the compiled `CapabilityOperationAllowlist` has at most 8 entries, and each `OperationToken` is at most 64 UTF-8 bytes (today 1 entry, `Update/ChocolateyMachineRuntimeLimitsV3`, 39 B). A larger allow-list fails that test and needs a spec revision of this bound.

### 4.4 Codec and goldens (S2)

- S2 adds the probe response codec only: `CreateProbeResponse` and `TryReadProbeResponse(frame, expectedNonce)`, a separate type or a separate region in Execution. There is no probe request codec.
- Goldens: one minimal frame per Q13 row; a passing frame on the one-token and the two-token allow-list; the largest valid frame; a `Refused` frame per `failedCheck` value and per `failedAccess` value.
- **Wire-name golden (review L6):** a test pins the 18 `TokenVerdict` names of §4.1 as an ordered literal list and compares it with `Enum.GetNames<TokenVerdict>()`. A rename, an addition or a removal of a verdict fails the test and needs a spec revision, because the enum name is the wire value. The same pin covers the `failedCheck` and `failedAccess` enums. S2 pins those two as ordered literal lists against the Execution enums only, because their native counterparts do not exist until the S3 observer additions (§3); pinning the native `failedCheck` and `failedAccess` enums against the Execution enums by exact name and value is an **S3 obligation**, as the S2 test does for `TokenVerdict`.
- Mutation tests: each property missing, duplicated or swapped; each Q rule violated; wrong-case enums; `Cancelled` as `observation`; `"1"`/`"3"` protocol; a frame of the update response fed to the probe reader and vice versa (both rejected).
- The package and client request and response goldens stay **byte-unchanged**.

## 5. Desktop side

### 5.1 Entry in `App.OnLaunched`

`QualificationProbeEntry.Classify(Environment.GetCommandLineArgs())` is pure and returns `Normal`, `Probe` or `Malformed`.
- `Probe` means exactly 2 elements, with element 1 ordinal-equal to `--qualify-chocolatey-helper`. Element 0 is not compared: the runtime may report the main assembly path rather than the apphost. Desktop identity is instead enforced by `TryHold` (both desktop files are in the binding set) and by the helper's peer verification and G1 peer-image hash.
- `Malformed` means any other line in which at least one element is ordinal-equal to `--qualify-chocolatey-helper` (review L-2). An element that differs from the flag in any way (case, whitespace, a prefix or suffix) is not the flag, so a line whose only near-matches are such elements is `Normal` and starts the normal UI, as any unrecognised argument does today.

**Order (review M3).** `Classify` is the **first** statement of `OnLaunched`, **before** the existing `CurrentUserDataOperationSession.TryAcquire` call. Today a `TryAcquire` that throws opens `StartupFailureWindow`, and a null session opens `DataOperationBusyWindow` (App.xaml.cs, before `EnterBootstrap`). A probe branch placed after that call would show a window and write no result file.
- `Normal`: the existing code runs unchanged, including its `TryAcquire`, both windows and `EnterBootstrap`.
- `Malformed`: exits at once. It acquires nothing and creates no window, file, database or UAC.
- `Probe`: the branch first creates the result folder (§5.2 step 1), then acquires the gate itself with `CurrentUserDataOperationSession.TryAcquire(TimeSpan.Zero, …)` and maps a null session, a throw, `RecoveryRequired` and a reset marker to result outcomes (§5.2 step 2). It holds the session for the whole probe, including the helper exit wait and the result write. It never calls `DataOperationLease.EnterBootstrap`, reset handling, the database or any window.

**Thread.** `CurrentUserDataOperationSession.Dispose` throws unless it runs on the acquiring thread (`CurrentUserDataOperationLease.cs`, `Dispose`), and the legacy mutex is thread-affine. The branch therefore runs §5.2 steps 1–2 on the `OnLaunched` thread, runs `ProbeAsync` (steps 3–10) on a worker that thread waits for (`Task.Run(...).GetAwaiter().GetResult()`), then writes the result file and disposes the session on the `OnLaunched` thread. When step 2 refuses, the branch writes the file without calling `ProbeAsync`. No window is created.

### 5.2 Order of the desktop probe (steps 1–2 and the result write in the `OnLaunched` probe branch; steps 3–10 in `ReviewedHelperProbeTransport.ProbeAsync`; any failure ends the run with the stated outcome)

1. Create the fresh result folder (§5.3). If this fails, exit with no file. **Why the folder comes before the gate (review L-2):** the result file is the probe's only output. If the folder cannot be created, no outcome can be recorded, so the branch exits before taking the gate or doing anything else. The folder is in `%TEMP%`, outside every PC Steward root and every state the gate protects, so creating it needs no gate. An existing folder refuses, so a second probe started while one runs, or before the owner's C7 cleanup, exits here with no file and touches neither the first run's folder nor the gate.
2. Gate, then the reset marker.
   - Session `null` → `GateBusy`. `TryAcquire` threw → `GateUnavailable`.
   - **`session.Context.RecoveryRequired` → `GateRecoveryRequired`** (review H1 b; narrowed by review M-2 b). It is set only when **this** acquisition observed an abandoned gate or legacy mutex (`AbandonedMutexException` in `CurrentUserOperationGate.TryAcquire` and in `CurrentUserDataOperationSession.Acquire`, then `RequireRecovery`). Windows reports an abandonment to one acquirer only: the first wait that succeeds after the owner ended. If a normal desktop start, a ScheduledHost run or any other acquirer took the gate first, the probe sees no abandonment. This is therefore a **narrow check, not a compensating control** for the barrier. Consuming the abandonment hides nothing from later workflows, because every mutating workflow runs the database reconciliation on each acquisition anyway (`CurrentUserDataOperationSession.Reconcile` calls `RequireRecovery` before it reconciles).
   - **Reset marker (review M-2 c).** `FactoryReset.GetMarkerState()` other than `None` → `GateResetPending`. The read is read-only (handle-based reads of the `reset` control directory; it deletes, promotes and writes nothing). It is the check `CurrentUserDataOperationLease.RunCoreAsync` makes as `statePreflight` after acquiring the gate, and that ScheduledHost makes before any state access (`Program.cs`, `RunWithOwnedMutexes`). A pending, partial or transition marker means a factory reset has not finished, and ordinary work stays unavailable until a normal desktop start completes it. An onboarding marker means the reset finished but ordinary work stays unavailable until the owner gives the onboarding acknowledgement (`FactoryReset.AcknowledgeOnboarding`). An invalid marker makes a normal desktop start open `ResetRecoveryWindow` (`App.xaml.cs`). The probe reads no PC Steward state, but it holds the gate that reset completion needs, and its E5 baseline assumes settled state, so it refuses in the same way.
   - The **durable unresolved-operation barrier in the database is not consulted**: the probe opens no database, so it cannot see a barrier that a normal workflow reconciles after taking the gate (`CurrentUserDataOperationLease.RunCoreAsync` → `session.Reconcile`; ScheduledHost likewise). The only control that stands in for it is the S7 host precondition (§9 item 3). The step-2 checks and the step-4 process check only narrow the window.
3. The process token's elevation type is not `Limited` → `DesktopNotLimited`. This refuses an elevated desktop, UAC off (elevation type `Default`) and standard users before any prompt.
4. **Process check** (review H1 b). A read-only enumeration of process names (each `Process` object disposed; no process is opened for access or signalled). Any process named `choco`, or any process other than the current one whose name starts with `PCSteward` (ordinal, case-insensitive), or an enumeration failure → `ConflictingProcess`. This narrows, but cannot close, the window in which such a process starts later; E5 re-checks at C4.
5. Fixed paths and initial launch trust (`InstalledHelperLaunchTrust`) → `LaunchTrustFailed`. Then the restricted per-request pipe and a fresh 32-byte nonce.
6. `TryHold` for **every** compiled allow-list token. All must be `Supported`, holding, and name the same helper path and the same helper `FileIdInfo`, or the outcome is `CapabilityNotHeld` and every binding is released. Only an ADR-022 owner-only deployment (v6 ledger, `pinned-unsigned-v2`, matching binding-set hashes) can pass, so **the desktop raises no prompt outside an ADR-022 owner-only deployment**. (Any same-user process can still `runas` the helper directly with a probe line; that helper fails to connect (exit 22) or fails peer verification (exit 23), and writes no response.)
7. Final launch trust → `LaunchTrustFailed`. Then one `runas` launch with the §2 line. `ERROR_CANCELLED` → `ElevationDeclined`. No process → `HelperNotStarted`. Both release the bindings.
8. Wait for connection (30 s, or helper exit first) → `HelperNotConnected`. Then the post-connect `VerifyPeer`: client PID equals the helper PID, path, session, SID, signer, and `FileIdInfo` equal to every held binding → `PeerNotVerified`. **Nothing is ever written to the pipe.**
9. Read one frame within 11 minutes of connection and require EOF → `NoValidResponse`. Then `TryReadProbeResponse` with the nonce → `ResponseRejected`, which discards all response fields. Otherwise → `ResponseAccepted`.
10. Observe helper exit (5 s). Terminate on any failure path. The bindings, and with them the desktop's share-read holds on the PC Steward install files, are released only after exit is observed and the transport is sealed (A-M2), as in the update transport. `ProbeAsync` then returns its result to the branch, which writes the result file and exits (§5.4).

The probe reuses the update transport's fixed paths, launch trust, pipe, hold, launcher shape and peer check. It omits request construction, because there is no operation, and it omits the desktop's Chocolatey caller-trust check (`ChocolateyCallerTrusted` → `ChocolateyMachineUpdateExecutor.ValidateDesktopCallerClientTrust`). That check is a pre-launch gate of a real Chocolatey update, so **E3 does not cover it** (review L1; §8). The update transport's `LaunchHelper` is not changed.

### 5.3 Result file

- **Location:** `<Path.GetTempPath()>\pcsteward-exact-artifact\qualification-result.json`. The temp path must be fully qualified and local (no UNC or device prefix). It is canonicalised to its long form (8.3 names expanded), and every existing component from the drive root is opened with `FILE_FLAG_OPEN_REPARSE_POINT`. A reparse attribute, or a final path different from the canonical path, refuses.
- **Folder:** created with `CreateDirectoryW`. An existing folder refuses: there is no reuse or merge. The folder handle is held and must not be a reparse point.
- **File:** created with `CREATE_NEW`, written once in full, flushed and closed. The desktop never deletes or overwrites anything; cleanup is the owner's C7 step. Integrity assumes no same-user malware (design §4 Safety).

Closed JSON, schema `pc-steward-qualification-probe-result:2`, exact property order:

| Property | Type / domain |
|---|---|
| `schema` | the literal above |
| `desktopOutcome` | `ResponseAccepted` \| `GateBusy` \| `GateUnavailable` \| `GateRecoveryRequired` \| `GateResetPending` \| `DesktopNotLimited` \| `ConflictingProcess` \| `LaunchTrustFailed` \| `CapabilityNotHeld` \| `ElevationDeclined` \| `HelperNotStarted` \| `HelperNotConnected` \| `PeerNotVerified` \| `NoValidResponse` \| `ResponseRejected` |
| `capabilityStatus` | object: compiled allow-list token → `HelperProtocolCapabilityStatus` name or `NotEvaluated` (the desktop's `TryHold` results) |
| `initialLaunchTrust`, `finalLaunchTrust`, `helperProcessCreated`, `pipeConnected`, `desktopPeerVerified`, `helperFileIdEqual`, `responseFrameReceived`, `pipeEofObserved`, `helperExitObserved`, `bindingHeldUntilExit`, `terminationFailed` | booleans (`helperProcessCreated` = UAC accepted; `bindingHeldUntilExit` = every binding `IsHolding` when exit was observed) |
| `helperExitCode` | integer or null |
| `pins` | null unless step 6 held; otherwise `ledgerSha256`, `capabilityFileSha256`, `desktopImageSha256` (the desktop apphost), `helperImageSha256` (the helper apphost), each 64 uppercase hex; `signerThumbprint` (40 uppercase hex) and `signerNotAfterUtc` (ISO 8601 `Z`) of the helper apphost's signing certificate. Sources: see below the table |
| `probe` | null unless `ResponseAccepted`; otherwise fields 3–16 of the accepted response, in order (no nonce) |
| `startedAtUtc`, `finishedAtUtc` | ISO 8601 `Z` |

**Pin sources (review M-1).** `capabilityFileSha256`, `desktopImageSha256` and `helperImageSha256` come from `HeldHelperBinding.VerifiedHashes`. That dictionary covers the binding-set files only. The ledger is read once through `ReadWhole` and never held, and `TryHold` returns no hash of it. The signer check (`AuthenticodeIdentity.HaveSameSigner`, path-based) returns only equality. S4 must therefore **extend what `TryHold` returns**, values only:
- `LedgerSha256`: the SHA-256 of the exact bytes `ReadWhole` returned and the policy verified. The ledger is not held, so a later change to the file is not reflected; the value records what was verified.
- The helper apphost's signer thumbprint and `NotAfter`, read while the apphost is held share-read (so the path names the held file).

Neither value adds authority, changes a `TryHold` status or is cached; D9 tests both.

The schema version moves to `:2` because the outcome domain and the `probe` fields changed; no `:1` file was ever produced. The helper's verification of the desktop is not a separate fact: a helper writes only after its own handle-based `VerifyPeer` and the G1 peer-image hash, so `ResponseAccepted` implies it. The file holds no path, user name, SID, fingerprint value, nonce or pipe name.

### 5.3a Closed result reader (only if D4 selects G-a or G-a′)

§5.3 defines a writer only. G-0 (§9) needs no reader, and then none is built. G-a and G-a′ need one, built in the activation change (not in S2–S6):
- **Type:** `QualificationResultReader.TryRead`, values only, in Execution. It writes, deletes and caches nothing.
- **Open:** only the fixed path of the chosen option (§9). Every component from the drive root is opened with `FILE_FLAG_OPEN_REPARSE_POINT`; a reparse attribute, or a final path different from the canonical path, refuses. The file is opened share-read and read through that handle. For G-a′, the same protection check that guards the ledger (`protection(stateRoot, …)` in `OwnerOnlyDeploymentCapabilityReader`) runs first.
- **Bound:** at most 4,096 B, read one byte over and refused if larger. S4 adds a writer test that the largest valid result file is at most 4,096 B.
- **Grammar:** strict UTF-8, no BOM, the exact schema literal, the exact §5.3 property set and order, every closed domain, and whole-file re-encode equality (as Q1). The embedded `probe` object must satisfy the Q rules that apply to fields 3–16.
- **Decision:** passes only if `desktopOutcome = ResponseAccepted`, the E1, A3 and E2 PASS conditions of §8 hold on `probe`, `pins` is not null, and every pin equals, in fixed time, the value the current `TryHold` returns (the S4 extension above). Anything else, including a read failure, refuses. The reader does not decide E3 or E5; the owner's S7 copy step records those judgments.

### 5.4 Exit behaviour

After the file is closed and the session disposed (on the acquiring thread), the desktop calls `Environment.Exit(code)`, with `0` only for `ResponseAccepted` and a fixed non-zero code per other outcome (S4 fixes the table). WinUI may not pass the code through, so **the result file is authoritative**. A missing file means no evidence.

### 5.5 What the probe path never does

It never opens or creates the database, runs a migration or reconciliation, takes a `DispatchClaimed` or any claim, writes history, scans, registers or touches the scheduled task, completes, verifies or arms a factory reset (it only reads the marker state, §5.2 step 2), creates a window or view model, reads SQLite or remote data, uses the network, or sends a request frame. `ScheduledHost`, scan workflows and every UI path cannot reach it.

### 5.6 Effects (for the amendment's limits)

Reads; the common current-user data-operation gate, held from §5.2 step 2 until the result file is written. While it is held, a ScheduledHost run skips its run (a null session returns 0 before any state access), and any desktop start shows `DataOperationBusyWindow` (review L-3). The desktop's share-read holds on the PC Steward install files (`TryHold` bindings) from step 6 until the helper's exit is observed; the helper's share-read holds on Chocolatey files during B′ (released at Stop). The gate and both holds are bounded by the 10-minute budget plus the desktop's read and exit waits (11 min and 5 s), plus the 30-s connection wait and however long the UAC prompt stays open (not bounded by PC Steward), and are expected to last seconds. The observer may read and hash up to 512 MiB of code and 512 MiB of archives (`ChocolateyProfileObserver` bounds), so "seconds" is an expectation, not the bound. At most one result file in a fresh `%TEMP%` folder (a failed write can leave an empty folder or an empty or partial file). Same-user processes can start the probe entry (§9).

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
- H3. A behavioural test with an ordered fake writer: the binding is `IsDisposed` before the response bytes are created, and a binding that is not disposed yields no response. The writer sees only the write, so an architecture test also pins the Stop order in the compiled `RunAsync` state machine: the single `DisposeItem` call precedes the single `CreateProbeResponse` call in IL order (and in the LF-normalised source); the native work captures the binding first and disposes it in a `finally` (S3 review).
- H1a. A pinned transitive closure over the compiled IL of every built `src` assembly from `ElevatedHelperEntryPoint.RunProbeAsync` and `SelectsProbe` (calls by overload, interface and virtual dispatch to instantiated `src` types, runtime and BCL callbacks, lambdas, local functions and async state machines, static constructors) reaches none of `Process.Start`, `ProcessStartInfo`, `CreateProcess*`, any job-object API, `LaunchJob`, `LaunchJobNativeMethods`, `NativeLaunchSpec`, `NativeCommandLine`, `ChocolateyCompiledCommand`, `UpgradeAsync`, `QueryCandidate*`, `NativeChocolateyHelperItem`, `ReviewedHelperEndpoint`, `TryReadRequest` or `PipeMessageFraming.ReadAsync` (S3 review).
- H4. The Execution allow-list of `Native` names grows only by `IChocolateyProbePort`, `IChocolateyProbeItem`, `ChocolateyProbeValues`, `TokenVerdict` and the two closed enums behind `failedCheck` and `failedAccess`. `ChocolateyProbeValues` has no fingerprint string member.
- H5. Only the native port constructs the probe item. `ElevatedHelperEntryPoint` constructs only the native ports.
- H6. The dispatch and mutual-refusal tests of §2: a probe line against all three update parsers; elements 2–9 of a probe line against the probe parser; the revision-1 order (kind at 8–9) refused with exit 20.
- H7. The inbound-byte test (exit 27, no response), the EOF test during A3 and during B′ (no response, exit 24) and the A3 cap test (a `Protection` that blocks past the budget → no response, exit 24, and a later `Protection` call does not reach the item).
- H8. The P0 exit codes: parse, Windows or elevation failure 20; `TryResolve` 21; connect 22; server PID or peer 23 (review L9).
- H9. The node and ancestor counts are per observation instance and saturate (review L7): two sequential observations report independent counts; a fake over the cap reports the cap.
- H10. In-flight disposal (N4b; review L-3): with a `Protection` blocked past the A3 cap, and separately with an `ObserveClient` blocked past the B′ outer cap, the entry disposes the item while the call runs; `Dispose` returns without waiting or throwing; when the blocked call is released it starts nothing and opens no new handle; a binding it returns after `Dispose` is disposed and never reaches the codec. The write-window test: a writer that never completes ends at exit 26 within the `min(ResponseWrite, ResponseMargin)` window.
- H11. The `frozenInstalled` re-spec trigger (review L-1): a static test that `ChocolateyReleaseList.Listed.Releases.Count == 1`, whose failure message names this spec's §3 trigger.
- H12. Existing callers unchanged (review L-4): `ProtectedTreeWalker.Walk` and `RequesterAccess.EvaluateAncestry` return the same results, with and without the new sink, over the existing walker, ancestry, observer and `NativeChocolateyHelperItem.RequesterCannotModify` fixtures; existing tests are unmodified. The sink reports `failedAccess = Other` for a failed open and `Granted` / `Unreadable` only for an `Evaluate` verdict, and `Unreadable` for every `Unreadable` result of `EvaluateAncestry`. No static, thread-static or ambient field carries the sink or the counts.

S2 (wire): the `TokenVerdict`, `failedCheck` and `failedAccess` wire-name goldens (§4.4) and the allow-list size test (§4.3).

S4 (desktop):
- D1. `ProbeAsync` has exactly one `src` caller: the `OnLaunched` probe branch, through `QualificationProbeEntry`.
- D2. Nothing in `ScheduledHost`, `Application`, scan workflows, view models or windows references the probe types.
- D3. The update transport's launcher never emits `--kind` (argument-list test). The probe launcher is the only `src` emitter of the literal, and emits it at indices 0–1.
- D4. The existing pins on `HasSupportedMode`, `CapabilityOperationAllowlist` and `Supports` are unchanged, and their tests are unmodified. *(Held for S1–S6. The activation change (Task 50 step C-A, [record](TASK_50_ACTIVATION_CHANGE.md)) changes exactly these pins, deliberately, under ADR-031 decision 7. The probe still never calls an admission member.)*
- D5. The probe branch type references no `SqliteDatabase`, `WindowsCurrentUserScheduledTaskRegistrar`, `DataOperationLease`, `Window` or view-model type. Its only `FactoryReset` member is the read-only `GetMarkerState` (review M-2 c); `Arm`, `CompletePendingAsync`, `VerifyOnboardingBootstrap`, `AcknowledgeOnboarding` and every other member stay unreferenced.
- D6. Result-writer tests: an existing folder, a reparse component, a short-name temp path, a UNC temp path, and the closed schema (no path, SID or fingerprint value).
- D7. Entry order (review M3, L-2): `Classify` precedes the only `TryAcquire` on the normal path; on the probe path the folder is created before the gate, and a folder failure acquires no gate and writes no file; with a test gate held, a probe start yields `GateBusy` in the result file and opens no window; with an abandoned test gate, `GateRecoveryRequired`, and a second acquisition of the same gate afterwards reports no recovery (the one-acquirer behaviour, review M-2 b); a throwing acquire yields `GateUnavailable`; each non-`None` marker state, including `Invalid`, yields `GateResetPending` with no state write; the session is disposed on the acquiring thread. `Classify` tests: the exact 2-element line is `Probe`; a line with the flag at another index or with more elements is `Malformed`; a case or whitespace variant of the flag is `Normal`.
- D8. The process check: a fake enumeration with a `choco` process, with another `PCSteward*` process, and a throwing enumeration each yield `ConflictingProcess` before any launch-trust call.
- D9. The `TryHold` value extension (review M-1): `LedgerSha256` equals the SHA-256 of the bytes the policy verified (a fixture that changes the ledger after `TryHold` keeps the earlier value); the signer thumbprint and `NotAfter` equal the fixture certificate's; every existing `TryHold` status test is unmodified and passes.

## 8. Interpretation (design §2 evidence; each run is one approved C3)

| Evidence | PASS | FAIL (a finding; no retry) | Inconclusive (no evidence) |
|---|---|---|---|
| E1 token match | `ResponseAccepted` ∧ `tokenVerdict = Accepted` | `ResponseAccepted` ∧ any other verdict (the verdict is the finding; for example `EvaluationPrivilegeNotAllowed`: the filtered token holds a privilege outside the allow-list). UAC off does not reach the helper: its elevation type is `Default`, so the desktop stops at `DesktopNotLimited` (review L2) | No accepted response |
| A3 / G1 | Every `g1Status = Supported` | Any other status after `Accepted` | Verdict not `Accepted`, or no response |
| E2 `AccessCheck` | `observation = Observed` ∧ `observedClientVersion = 2.7.5` (the newest listed release) ∧ `fingerprintPresent` ∧ `fingerprintLength = 64` ∧ both counts ≥ 1 ∧ `failedStep = null` | `Refused` with any reason other than `InstalledMismatch`. Step, check, access, reason and detail are the finding; `failedAccess` tells an unprotected tree (`Granted`) from an unreadable one (`Unreadable`), and both keep the route unusable on this host | `NotRun`, `TimedOut`, `InstalledMismatch` (the host runs an older listed release; a precondition, not an enforcement failure; review L3), or no response |
| E3 real transport | `desktopOutcome = ResponseAccepted`; all result booleans true except `terminationFailed = false`; `helperExitCode = 0`; `pins` equal C1; the owner saw exactly one prompt for the signed helper whose details show `--kind qualify-chocolatey-helper` right after the program path. **Not covered:** the desktop's Chocolatey caller-trust check (§5.2) | Any false fact after `helperProcessCreated`; `CapabilityNotHeld` or `LaunchTrustFailed` on a C1-pinned deployment; `PeerNotVerified`, `NoValidResponse` or `ResponseRejected`; a prompt that differs from the expected one | `ElevationDeclined` (not repeated without new approval), `GateBusy`, `GateUnavailable`, `GateRecoveryRequired`, `GateResetPending`, `ConflictingProcess`, `DesktopNotLimited`, `HelperNotStarted`, or no result file |
| E5 nothing changed | C4 = C2 (database and Chocolatey hashes), no `choco` process, only the result folder is new | Any difference | Baseline missing |

Overall **PASS** only if all five rows pass. Any FAIL stops use (Decision 2). An inconclusive run proves nothing and needs a new approval to repeat. A PASS proves **UAC and pre-launch enforcement only** for the deployment and pins it ran on, excluding the caller-trust check.

## 9. Deployment-order obligations under Decision 3 (c) (for S7 and the activation change)

**S7 obligations (Part C of the activation plan):**
1. **C1 stops unless the installed desktop's SHA-256 equals a build record marked "probe included".** The G3 build record for the activated build must carry that mark, set only when the build commit contains S2–S6. Any other hash → `STOP: not a probe build`, with no C2 or C3.
2. **C3 is never offered on the Part B deployment.** Part B deploys a pre-probe build (§6: an old desktop starts its normal UI and writes no result file), so C1 always stops there.
3. **Host preconditions before C1:**
   - no Chocolatey operation in progress;
   - **the unresolved-operation check (review M-2 a).** The probe does not consult the durable barrier (§5.2 step 2), and PC Steward shows **no startup notice** for it: the desktop calls `DataOperationLease.ReconcileBootstrap` and discards its result (`App.xaml.cs`). The only visible sign is in the reviewed-update history. The owner opens PC Steward normally, opens **Activity**, waits until the text "Loading reviewed update outcomes…" (`ReviewedUpdateHistoryListViewModel.LoadingText`) is replaced, and then reads the **"Reviewed update outcomes"** section. The section lists unresolved records first. The precondition holds only if it shows "No reviewed update outcomes are recorded." or no record shows the field **"Other updates"** with the text **"Other updates stay blocked until this record is safely reconciled."** (`ReviewedUpdateHistoryItemViewModel.BlocksOtherUpdatesText`). The text "Reviewed update history is unavailable right now. Nothing was started or changed." means **stop**: the check could not be made. The owner then closes PC Steward;
   - **after PC Steward is closed:** no `choco` or `PCSteward*` process (read-only `Get-Process`).

**Residual gap.** Under (c), the activation change admits the Chocolatey routes in code (`HasSupportedMode`, the client capability token) before any exact-artifact evidence exists. Decision 2 ("confirm no Chocolatey review until the probe passes on this deployment") then holds **as procedure, not code**: between the activation deploy and a recorded PASS, the desktop would enable Confirm.

**Code-level guard: owner decision D4 (not decided here).**

Two things are ruled out:
- *A compiled requirement is not feasible.* The ledger, capability file hash and signer are created at deploy time, after the build, so the build cannot name the deployment it must have evidence for.
- *A marker written by the probe is not proposed.* It would add a state write to the probe (breaking §5.5 and E5 "only the result folder is new"), and the probe cannot judge E3 (the prompt the owner saw) or E5 (C4 = C2).

The options G-a and G-a′ share one mechanism and differ only in where the pass record lives. After a recorded overall PASS, the owner copies the result file, as an explicit S7 step, to a fixed name in a fixed root. The activation change then:
- (i) adds a review unavailability reason that keeps Confirm disabled for every Chocolatey route unless the record exists; and
- (ii) has the transport, after `TryHold` and before UAC for any Chocolatey route, read the record with the §5.3a closed reader and refuse before elevation unless it passes there. The reader needs the S4 `TryHold` extension (§5.3, pin sources), because `ledgerSha256` and the signer values are not available from `TryHold` today.

A redeploy changes the pins, so the guard closes again until a new PASS is recorded. E3 and E5 remain owner judgments in every option.

| Option | Where the record lives | What it protects against | What it costs |
|---|---|---|---|
| **G-0** (procedure only, as Decision 2 stands) | nowhere | Nothing in code. | No code. One owner mistake allows a real Chocolatey update on a deployment with no exact-artifact evidence. |
| **G-a** (user root) | `%LOCALAPPDATA%\PCSteward\` (`FactoryReset.GetStateRoot`), at a fixed leaf name | Owner or procedure error only. Any same-user process can write the record, so it does not resist same-user malware (the same limit as the result file). | **Factory reset must change.** Today `CompletePendingAsync` deletes only `downloads`, `logs` and the four database leaves, then `VerifyOnlyResetControl` and `VerifyOnlyExpectedRootChild` (`DownloadedArtifactCache.cs`, `NativeResetControl`) require the root to hold exactly the `reset` directory. Any other file fails as `UnexpectedOwnedObject`, the reset does not complete, and every later start shows `ResetRecoveryWindow`. The revision-2 claim that reset "removes it with the state root" was false. G-a must add the leaf to reset deletion and verification (delete, then verify absent, before the owned-root check), which needs ADR text: the ADR-013 fixed reset manifest gains the leaf, and ADR-019/ADR-027 state that Clear scan data and Clear downloaded files keep it (neither touches the root today). The S7 copy step is unelevated. **A factory reset then deletes the record**, so after any reset the guard stays closed until a new approved probe run and a new copy (the `%TEMP%` result file was already removed in C7). **The onboarding-root check rejects the record too:** while the marker is `OnboardingPending`, `FactoryReset.VerifyOnboardingBootstrap` (and `AcknowledgeOnboarding`) call `VerifyOnlyOnboardingRoot`, whose `EnsureOnlyOnboardingRoot` (`DownloadedArtifactCache.cs`) allows only `reset` and the four database leaves in the root. Any other child fails as `UnexpectedOwnedObject`, and the start opens `ResetRecoveryWindow`. The S7 copy must therefore refuse unless `FactoryReset.GetMarkerState()` is `None`, or that check must change as well. |
| **G-a′** (ADR-022 root) | `%ProgramFiles%\PC Steward OwnerOnlyTest State\`, beside `deployment.json`, at a fixed leaf name | Owner or procedure error, and a same-user process **after** the copy: the root is writable only by Administrators and SYSTEM (Users read), so a standard-user process cannot write the record. A same-user process can still alter the `%TEMP%` result file between the probe and the copy. The copy step therefore re-reads the file with the §5.3a reader, and the owner makes the copy right after reviewing the file. | **An elevated owner copy** per PASS: one more host action with its own approval and one more UAC prompt. `Test-OwnerOnlyProtectedTestDeployment.ps1` and `Remove-OwnerOnlyProtectedTestDeployment.ps1` require the exact ADR-022 owner, DACL and no-reparse state on every state-root path, so the copy must set them exactly; the validator should be extended to recognise and report the leaf, and the redeploy plan should name it. **ADR-022 alignment note 3 must be narrowed:** it says the deployment reading is "never cached and never persisted", and this record persists a pass derived from that reading. The record **survives factory reset**, because reset deletes only the user root. That is correct for evidence about the deployment. But ADR-025's Task 47 text adds no "hidden reset-surviving state", so the narrowed ADR-022 note must disclose the record. ADR-022 removal deletes it with the state root, and a reinstall refuses an existing state root, so a redeploy cannot keep it. |

G-a and G-a′ belong to the activation change (G6, §8.6 admission gates), not to S2–S6. Choosing G-0, G-a, G-a′ or another option is the owner's decision. If G-0 is chosen, no reader is built.

**Owner decision 2026-11-06: G-0.** The activation change built no reader and no guard. Note: in that build the Desktop composes no Chocolatey review, so Confirm is not enabled in practice either ([record](TASK_50_ACTIVATION_CHANGE.md) §2.3). S7 obligations 1–3 are built there: the G3 build-record mark (§3) and `scripts/evidence/Task50-QualificationProbe.ps1` (§4).

## 10. Answers to the revision-1 open questions (recorded)

1. **Access verdict for the failed step: yes.** Added as `failedAccess` (`None` / `Granted` / `Unreadable` / `Other`), with Q15. The revision-1 list (`RequesterModifiable` / `RequesterOwned` / `Unreadable` / `Reparse` / `OverBound`) is replaced: `RequesterModification` already has `Granted` and `Unreadable`, the requester-owned check maps to `Granted`, and reparse and bound refusals are `Other`.
2. **ADR-018 note: yes.** A **Proposed** cross-note is added at the end of ADR-018. It takes effect only with the owner's acceptance of the ADR-025 amendment.
3. **A3 counts: not merged into the B′ counts.** Separate G1 counts are optional; they would be a separate pair of fields with their own bounds, and they are not added in this revision.
4. **No package-mode probe.** AQ1UAC stays as cheap supporting evidence (Part A passed it on 2026-11-03). Probe E3 is the evidence for the signed apphost.

## 11. Review disposition

### 11.1 Revision 2 (first independent review)

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

Result-file schema moved to `:2`; response grew from 15 to 17 properties.
### 11.2 Revision 3 (second independent review)

Independent review of revision 2, verdict **"Sound with changes"**, with no design change needed. Each point was checked against `src` at `0835801` (unchanged at the revision-2 head `7044e82`) and the ADR-022 scripts at that head before it was applied.

| # | Sev. | Finding | Code check | Disposition |
|---|---|---|---|---|
| M-1 | Med | D4 G-a: "factory reset removes it" is false; the root is unnamed; no reader; `ledgerSha256` not available | Confirmed: `FactoryReset.CompletePendingAsync` deletes `downloads`, `logs` and four database leaves, then `VerifyOnlyResetControl` / `VerifyOnlyExpectedRootChild` require exactly the `reset` directory (`UnexpectedOwnedObject` → `ResetRecoveryWindow`). `HeldHelperBinding.VerifiedHashes` covers binding-set files only; the ledger is read once by `ReadWhole`. **Also found:** the signer thumbprint and `NotAfter` are not exposed either (`AuthenticodeIdentity.HaveSameSigner` returns equality only) | Applied: §9 D4 table with G-0, G-a (user root `%LOCALAPPDATA%\PCSteward`, reset deletion and verification plus ADR-013/019/027 text) and G-a′ (ADR-022 root, elevated copy, validator/remover/redeploy-plan changes, ADR-022 note 3 narrowed, reset-surviving disclosure); §5.3a closed reader; §5.3 pin sources and the S4 `TryHold` extension for the ledger hash and the signer values; D9. D4 stays an owner decision. |
| M-2 a | Med | No startup notice exists | Confirmed: `App.OnLaunched` discards the result of `DataOperationLease.ReconcileBootstrap`; the history row field "Other updates" carries `BlocksOtherUpdatesText`; unresolved rows sort first (`SqliteReviewedUpdateHistory`, non-`Final` first) | Applied: §9 item 3 names the page, section and exact text, and treats "unavailable" as stop; amendment item 2. |
| M-2 b | Med | `GateRecoveryRequired` fires only once | Confirmed: `AbandonedMutexException` reaches one acquirer (`CurrentUserOperationGate.TryAcquire`; legacy mutex in `CurrentUserDataOperationSession.Acquire`) | Applied: §5.2 step 2 and the amendment call it a narrow check, not a compensating control; consuming it hides nothing, because `Reconcile` runs on every acquisition; D7 test. |
| M-2 c | Med | Reset marker not checked | Confirmed: `RunCoreAsync` `statePreflight` and ScheduledHost `RunWithOwnedMutexes` both refuse a non-`None` marker after acquiring; `GetMarkerState` is read-only | Applied: `GateResetPending` (§5.2 step 2, §5.3, §8 E3 Inconclusive); D5 allows only `FactoryReset.GetMarkerState`; D7. |
| M-3 | Med | Exception (b) cites the wrong rule | The probe is not a scan, and no scan reaches it. Spec text checked: FR-UPD-04 "Request elevation only when the provider determines it is needed"; §13.3 "start only for a user-approved operation" and "receive immutable source/package identifiers rather than arbitrary command lines" | Applied: exception (b) rewritten in the ADR-025 amendment, with a sentence explaining the correction to Decision 1's wording; ADR-018 cross-note also narrows "After confirmation…", "Elevation is delayed until execution" and "one narrow provider capability". **Note:** the probe receives *no* identifiers, which is narrower than the §13.3 bullet, but the bullet assumes an operation exists, so it is listed. |
| L-1 | Low | Q10: `frozenInstalled` = newest listed release can never pass on an older listed release | Confirmed: client step 7 requires the registration to equal the matched release; step 8a compares with `frozenInstalled`. The observer has no option to compare with the matched release (required parameter; the match is private) | Re-spec trigger recorded (§3) and pinned by H11 (`Listed.Releases.Count == 1`); Q10 cross-reference. No observer change in this revision. |
| L-2 | Low | §5.1 and §5.2 order inconsistent; `Malformed` undefined | Revision 2 put the gate in `ProbeAsync` step 2 but required it on the `OnLaunched` thread | Applied: folder first, with the reason stated (§5.2 step 1); steps 1–2 and the result write run in the branch on the acquiring thread, steps 3–10 in `ProbeAsync`; `Malformed` = an element ordinal-equal to the flag; D7 `Classify` tests. |
| L-3 | Low | Effects lists; Stop write timeout; N4b | Confirmed: a null session makes ScheduledHost return 0 and the desktop open `DataOperationBusyWindow`; the update endpoint bounds the monitor stop and write by `min(ResponseWrite, ResponseMargin)` (5 s); N4b comment in `ReviewedHelperEndpoint.StageBAsync` | Applied: §5.6 and amendment item 3; §3 Stop row window and exit 26; §3 in-flight disposal paragraph; H10. |
| L-4 | Low | `failedAccess` and the counters need threading through shared static code | Confirmed: `ProtectedTreeWalker.WalkCore` folds verdicts into `TreeWalkRefusal` (and `Unreadable` also from failed opens); `RequesterAccess.EvaluateAncestry` is static and also returns `Unreadable` for open/volume failures | Applied: §3 threading bullet (explicit per-call sink, no ambient state; failed open → `Other`; any ancestry `Unreadable` → `Unreadable`); H12 unchanged-results test for existing callers. |

Response and result-file shapes are unchanged except the new `GateResetPending` outcome value (schema stays `:2`, because no `:2` file has been produced yet). A fresh-context check of revision 3 is needed before S2 starts.

### 11.3 Revision 4 (precise wording fixes)

Further review of revision 3 and the two Proposed ADR texts; wording only, with no design, shape or bound change. Each point was checked against `src` at `0835801`, the ADR-022 scripts and the ADR and test-design text at the revision-3 head `55cd683` before it was applied.

| # | Where | Finding | Check | Disposition |
|---|---|---|---|---|
| R4-1 | ADR-025 amendment, exception (d) | (d) named only ADR-018, but this ADR's own boundary text also limits the bootstrap arguments and the request lifetime | ADR-025 "Privilege and protocol boundary": "Bootstrap arguments carry only protocol/PID/pipe/nonce" and "single-request lifetime", both verbatim | Applied: (d) names that section and quotes both phrases. |
| R4-2 | ADR-025 amendment, item 2 lead-in | "accepted in Decision 1" overstated what Decision 1 accepted | Decision 1 (exact-artifact test design §6) accepts spec §2.3 principle 4, "Scanning never requests elevation" and the DispatchClaimed-before-UAC rule; it names no ADR-018 exception | Applied: Decision 1 accepted (a), (c) and a differently worded (b); the corrected (b) and (d) take effect only when the amendment is accepted. |
| R4-3 | ADR-025 amendment, correction paragraph | Decision 1's effects wording is narrower than item 3 | Decision 1: "its only effects are reads, a few seconds of share-read holds on Chocolatey files, and one result file in %TEMP%" (verbatim) | Applied: the paragraph states that item 3 replaces it. |
| R4-4 | ADR-025 amendment, (b) and item 4 | "receives no identifiers" ignored the bootstrap data; "It establishes" attributed the claim to the probe rather than to a passing result | §2: the line carries PID, pipe and nonce besides the kind literal; §8 grants the claim to an overall PASS only | Applied: "no source/package identifiers; besides the bootstrap data it receives only one fixed compiled literal"; "A PASS establishes". |
| R4-5 | ADR-025 amendment, item 5 | G-a′ omitted the disclosure that §9 requires | ADR-025 "Durable dispatch": "no resident/elevated service or hidden reset-surviving state is added" | Applied: G-a′ adds disclosure under that rule; the G-a and G-a′ summaries also mirror R4-9 and R4-10. |
| R4-6 | ADR-018 cross-note, bullets 1 and 3 | "the owner's start" implied that only the owner can start the entry | Amendment item 3 "Starting": any same-user process can start it and cause a genuine prompt | Applied: both bullets say the trigger is a start of the entry, intended only for the owner's separately approved run; any same-user process can start it, and the owner declines any prompt they did not start. |
| R4-7 | ADR-018 cross-note, bullet 3 | "The rest of the sentence and paragraph is retained" also retained the four-parameter sentence that the next bullet narrows | ADR-018 Decision ¶2: "Launch parameters contain only protocol/bootstrap data: …" | Applied: "…retained, except the launch-parameter sentence, narrowed below". |
| R4-8 | ADR-025 amendment item 3; ADR-018 cross-note, last paragraph | "A helper elevated (started) for one kind refuses the other" described a refusal that does not occur: neither mode parses the other kind | §2 mutual refusal, last bullet; §6 last row | Applied in both: "A probe-mode helper never reads an update request, and an update-mode helper never reaches the probe entry." |
| R4-9 | §9 D4 G-a | The cost omitted that reset then deletes the record, and the onboarding-root check | `App.OnLaunched` → `VerifyOnboardingBootstrap` → `VerifyOnlyOnboardingRoot` → `EnsureOnlyOnboardingRoot`, which allows only `reset` and the four database leaves (`UnexpectedOwnedObject` → `ResetRecoveryWindow`); `AcknowledgeOnboarding` makes the same check | Applied: after any reset the guard stays closed until a new approved run and copy; the S7 copy refuses unless `GetMarkerState()` is `None`, or that check changes. |
| R4-10 | §9 D4 G-a′ | "administrator-only" and "validator, remover … must change" were imprecise | Both scripts' `Assert-ExactDacl`: owner Administrators, protected DACL with SYSTEM and Administrators full control and Users read and execute; reparse points refused on every state-root path. Neither script rejects an unknown leaf; the remover deletes the state root recursively | Applied: the copy must set the exact owner, DACL and no-reparse state; the validator should recognise and report the leaf; the redeploy plan should name it; "writable only by Administrators and SYSTEM (Users read)". |
| R4-11 | §3 budget | "inside that budget" was false for B′ and the write | B′ outer cap = remaining + 5-s exit margin; Stop window `min(ResponseWrite, ResponseMargin)` = 5 s (`ReviewedHelperEndpointBudgets.Production`); desktop read 11 min | Applied: the last write can end up to 10 min 10 s after `t0`, still under the desktop's 11 minutes. |
| R4-12 | §9 item 3 | The process check came before the owner opens and closes PC Steward for the history check, so it did not cover the state C2 baselines; the history shows a loading text first | `ReviewedUpdateHistoryListViewModel.LoadingText` = "Loading reviewed update outcomes…", shown while the list loads (`MainWindow.cs`) | Applied: wait until that text is replaced; the process check moves to after PC Steward is closed. |
| R4-13 | §5.2 step 2 | "until a normal desktop start completes it" was wrong for onboarding and invalid markers | `App.OnLaunched`: only `ResetPending`, `Partial` and `TransitionPending` call `CompletePendingAsync`; `OnboardingPending` shows onboarding and needs `AcknowledgeOnboarding`; `Invalid` opens `ResetRecoveryWindow` | Applied: the three cases are stated separately. |

A fresh-context check of revision 4 is needed before S2 starts.

### 11.4 S3 implementation security review (text aligned with the implementation; no design change)

Verdict "Sound with changes". The code fixes are recorded in `docs/STATUS.md`; this record changed as follows.

| # | Sev | Finding | Disposition |
|---|---|---|---|
| S3-M | Med | The behavioural H3 test did not enforce the Stop order (item disposed, then response built) | H3 adds the IL-order (and source-order) test; moving `CreateProbeResponse` before `DisposeItem` now fails it. |
| S3-L1 | Low | A3 abandonment race: a cancelled or capped evaluation could still reach `RequesterCannotModify` before the flag was set | §3 A3 row: token and budget checked before each `Task.Run`; `Protection` reads the run's token and the cap; a registration sets the flag; a status completing at abandonment is not used. |
| S3-L2 | Low | The native work read the observation's values before disposing the binding | H3: binding captured first and disposed in a `finally`; a throwing getter still releases it and the item returns `Faulted`. |
| S3-L3 | Low | N4b: an abandoned `CannotModify` after `Dispose` may still open read-only handles | Recorded as an accepted residual in the in-flight disposal paragraph; the shared rule is unchanged. |
| S3-L4 | Low | H1 checked only direct references | H1a: pinned transitive closure from the probe entry points. |
| S3-N | Note | §3 Stop said exit 24 for "any fault", but an exception escaping the entry after connection exits 26 | §3 Stop row aligned with the implementation (26, as the update bootstrap), with the reason. |

### 11.5 S4 implementation record (desktop side; no design change)

Implemented on 2026-11-04. Types: `QualificationProbeEntry` (public `Classify` and `Run`), `QualificationProbeRunner`, `ReviewedHelperProbeTransport.ProbeAsync`, `ReviewedHelperProbeSession`, `QualificationProbeResultDocument`, `QualificationResultFile` and `WindowsQualificationResultFileSystem` (Execution); `QualificationProbeLaunchGate` (Desktop, the only Storage-facing part: `TryAcquire(TimeSpan.Zero, …)`, `Context.RecoveryRequired`, `FactoryReset.GetMarkerState`).

**§5.4 exit-code table:** `ResponseAccepted` 0; `GateBusy` 41, `GateUnavailable` 42, `GateRecoveryRequired` 43, `GateResetPending` 44, `DesktopNotLimited` 45, `ConflictingProcess` 46, `LaunchTrustFailed` 47, `CapabilityNotHeld` 48, `ElevationDeclined` 49, `HelperNotStarted` 50, `HelperNotConnected` 51, `PeerNotVerified` 52, `NoValidResponse` 53, `ResponseRejected` 54; no result file 60; malformed line 61. The file stays authoritative.

Conservative readings where the text left a choice:
- **Step 6 needs complete pins.** `TryHold` keeps its status; the probe additionally requires the D9 values (ledger SHA-256 and a readable helper signer) and equal pins across every token's hold, or `CapabilityNotHeld` before any prompt. Every token is held and its status recorded (no stop at the first failure).
- **Step 5:** a failed identity read or restricted-pipe creation after the initial trust is `LaunchTrustFailed` (no separate outcome exists).
- **D9 values are recorded only in the Desktop role;** the helper's G1 reads are unchanged.
- **Unowned helper process** (`HelperProcessWork` construction fails): `HelperNotConnected`, the helper is terminated and the bindings stay held (A-M2), as in the update transport. A pre-launch cancellation (no production caller passes one) is `HelperNotStarted`.
- **Connection race:** as in the update session, a helper whose exit is observed before its connection is `HelperNotConnected`.
- **Result file:** compact JSON; timestamps `yyyy-MM-ddTHH:mm:ss.fffZ`; `startedAtUtc` is taken before step 1; a document that is not closed and consistent (for example accepted without facts) is not written (exit 60).
- **Public surface:** Execution's internals are not visible to the Desktop, so the entry, the launch enum and the gate-session interface are public; every test seam stays internal and has no `src` caller outside the production composition (architecture test).

Accepted residuals and usability notes (S4 security review, verdict PASS; no design change):
- **Result-file creation through a swapped ancestor (Low).** The file is created by path (`CreateFileW`, `CREATE_NEW`), not relative to the held folder handle. A same-user process that replaces a `%TEMP%` ancestor with a junction during the run is caught by the final-path check on the new file (its final path must equal the canonical target), so nothing is written and the run exits 60. The `CREATE_NEW` may already have left an **empty file** at the redirected location, though. That location is one the same user can write anyway, and the case is inside the "no same-user malware" assumption (§5.3). Creating the file relative to the held handle (`NtCreateFile` with `RootDirectory`) would close it, but it needs a new native surface and `FILE_ADD_FILE` on the held handle, and it can be validated only on Windows. It is not done in S4.
- **UAC prompt with no owner window.** The probe start opens no window (§5.6), so the `runas` launch has no owner window. The consent prompt may appear behind other windows, with only a taskbar flash. The owner procedure should say so.
- **The main thread blocks during the probe by design.** `OnLaunched` waits for `Run` and then calls `Environment.Exit`. No window exists to stop responding (§5.6), and the work is bounded by the §5.4 budgets.

Existing pins changed explicitly for S4: the reader-construction and binding-file pins (`DeploymentCapabilityBoundaryTests`, plus the probe's own A-M2 release points), the G1 construction-signature pin (`HelperEndpointBoundaryTests`: the probe composition is the only caller of the Desktop-role parameterless reader), the codec consumers (`QualificationProbeWireBoundaryTests`) and the S3 "not wired" pin, now "wired only through the entry" (`QualificationProbeHelperBoundaryTests`). The D4 admission-pin tests are unmodified. D4 (G-0, G-a, G-a′) is not implemented.

### 11.6 S6 integrated review disposition (no design change)

Recorded on 2026-11-04. The update path is unchanged.

- **Desktop closure (test).** The transitive-closure IL test (`QualificationProbeReachabilityTests`) now also starts from `QualificationProbeEntry.Run` and `Classify`. It applies the existing ban list plus `LaunchJob*`, `ChocolateyCompiledCommand`, `ChocolateyMachineUpdateExecutor`, `TryReadRequest` and the update transport in `ReviewedHelperProtocol2Transport.cs` (everything except the pipe-name locator, `CreatePipeName` and `PipeNamePattern`, and its static initialiser). It also bans any pipe write (`PipeMessageFraming.WriteAsync`) and allows the one response read only in `ReviewedHelperProbeSession`. Exactly one `ProcessStartInfo`/`Process.Start` use is allowed, in `ReviewedHelperProbeTransport.LaunchProbeHelper`. A planted second `Process.Start` in `EnumerateProcessNames` failed the test, and the plant was then removed.
- **§8 E3 (spec).** The PASS column now also requires `desktopOutcome = ResponseAccepted`.
- **§5.6 and ADR-025 item 3 (wording).** The gate and the holds are also bounded by the 30-s connection wait (`ReviewedHelperBudgets.Production.Connection`) and by however long the UAC prompt stays open, which PC Steward does not bound. The helper writes at most one closed response (ADR-025 item 1, ADR-018 cross-note). The probe leaves at most one result file, and a failed write can leave an empty folder or an empty or partial file.
- **Unowned helper (code).** `ReviewedHelperProbeTransport.TryTerminate` now also catches the `AggregateException` that `Process.Kill(entireProcessTree: true)` can throw. Before this fix, that exception escaped `ProbeAsync` in the branch where `HelperProcessWork` cannot be constructed, so the runner wrote no result file (exit 60). Now the run records `HelperNotConnected` with `terminationFailed = true`. The branch's comment is corrected: the bindings are not released there, and the desktop's `Environment.Exit` releases the holds right after the result file is written and the gate is released. A portable test covers the kill wrapper.
- **Budget test.** `ReviewedHelperProbeTransport.Budgets.Execution` is asserted to exceed `Probe + ExitMargin + min(ResponseWrite, ResponseMargin)`, computed from `ReviewedHelperProbeBudgets.Production` rather than from literals.
- **Follow-up (not in this task).** The update transport's `WindowsReviewedHelperTransport.TryTerminate` has the same pattern: it does not catch `AggregateException`. It is unchanged here and needs its own reviewed change.
- **Info: `CapabilityNotHeld` with all-`Supported` statuses.** When every token's `TryHold` is `Supported` but the pins are incomplete (for example an unsigned helper) or differ between holds, the file shows `CapabilityNotHeld` with every `capabilityStatus` `Supported` and `pins` null. The statuses alone do not explain the refusal.
- **Info: helper exit 24.** Exit 24 covers several causes (EOF, the A3 cap, B′ cancellation or cap, a stage fault, a binding that is not disposed, a codec throw). The helper's diagnostic token is discarded, and the desktop records only `helperExitCode`, so the file gives only limited diagnosis.
- **S7 note.** C1 must record all six pins in the result file's format: uppercase hex for the four SHA-256 values and the thumbprint, and `signerNotAfterUtc` as `yyyy-MM-ddTHH:mm:ss.fffZ`. `ledgerSha256` covers the probe's own code, so any change to the probe changes it.
