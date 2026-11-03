# Task 50 — Chocolatey helper qualification probe: protocol-2 addendum (slice S1 specification)

Requirements: `FR-UPD-15`, `TM-03`, `TM-04`, `TM-07`, `TM-13`; ADR-018, ADR-022, ADR-025 (durable dispatch; follow-up controls), ADR-031 (decision 7).

Status: **draft for independent review (slice S1).** It adds no code, endpoint, deployment, UAC prompt or host action. The probe stays undeployable until the owner accepts the [ADR-025 amendment](../adr/ADR-025-TYPED-OPERATION-JOBS-AND-HELPER.md#proposed-amendment-2026-11-03--owner-started-qualification-probe) separately, and each run needs its own approval.

Authority: [exact-artifact test design rev 2](TASK_50_EXACT_ARTIFACT_TEST_DESIGN.md) §4 (A), and the owner decisions recorded there on 2026-11-03: **Decision 1 Yes, Decision 2 Yes, Decision 3 (c)**. This record is an addendum to the [protocol-2 fact extension rev 3](TASK_50_PROTOCOL2_REVIEWED_FACT_EXTENSION.md). It builds on the [helper endpoint and native observer design rev 6](TASK_50_HELPER_ENDPOINT_AND_NATIVE_OBSERVER.md) (§2.1, §3 steps 1–9, §5, §6 G1) and the [ADR-031 design](TASK_50_ADR031_CLIENT_SELF_UPDATE_DESIGN.md) (§3.6 client-mode observation, R5, §3a). Source state: `0835801`.

## 0. Scope

In scope: the helper command-line grammar for the probe kind; the helper probe entry and stage order; the closed probe response, its rules Q1–Q14 and bounds; the desktop entry, transport and result file; compatibility; the architecture-test obligations for S3/S4; how results are interpreted.

Unchanged and out of scope: the update request and both update response shapes (package 20 properties, client 21; S1–S14); protocol 1; `HasSupportedMode`, `CapabilityOperationAllowlist`, the dispatcher's `Supports`; the capability file grammar; ADR-025's real-update and installed-state tests (open under G9); E4 (block H4).

## 1. Versioning decision: additive kind under protocol "2"

**Chosen: an additive request kind under protocol `"2"`, selected only by the command-line literal `--kind qualify-chocolatey-helper`.** No capability token is added, and the capability file grammar is unchanged.

Why not a capability token in `PCSteward.DeploymentCapability.txt`:
1. **It would touch an admission pin or the admission parser.** `TryParseCapability` accepts only `operation:` lines whose token is in `CapabilityOperationAllowlist`. A probe token as an `operation:` line changes the allow-list, which Decision 1 forbids. A new line type changes the parser that `Supports`, `TryHold` and G1 all share, so the probe would widen the update path's gate.
2. **It adds no compatibility protection.** The desktop, the helper and the capability file are all in `DeploymentCapabilityPolicy.BindingSet`. Their hashes are pinned by the ledger and checked by the desktop's `TryHold` before UAC and by the helper's G1 after it (G1 also checks the peer desktop's image hash). A mismatched desktop/helper pair is therefore already refused in any valid deployment. Admission changes the capability file anyway (the client token), so a token would only travel along with it.
3. **It is not what the user consents to.** The UAC details show the command line, not the capability file. The kind must bind the consent (design §10 finding 2), and only the command line does that.

Discrimination between the two kinds:
- **On the command line:** 8 arguments mean update; exactly 10, with the fixed literal at positions 8–9, mean probe (§2).
- **On the wire:** the probe response starts with `"protocolVersion":"2","kind":"qualify-chocolatey-helper"` and has a different exact property set. Each reader rejects the other frame on the exact-set check.
- **In the protocol value:** `ReviewedUpdateWireCodec.ProtocolVersion` stays `"2"`. The probe adds no new protocol value that an older reader could accept.

## 2. Command-line grammar and dispatch

**Probe line (exactly 10 arguments, fixed order, case-sensitive ordinal):**

```
--protocol 2 --desktop-pid <n> --pipe <pcsteward-reviewed-[a-f0-9]{32}> --nonce <64 uppercase hex> --kind qualify-chocolatey-helper
```

| Index | Value | Rule |
|---|---|---|
| 0, 1 | `--protocol`, `2` | literal; `2` = `ReviewedUpdateWireCodec.ProtocolVersion` |
| 2, 3 | `--desktop-pid`, `<n>` | `NumberStyles.None`, invariant, > 0 (as `TryParse`) |
| 4, 5 | `--pipe`, `<name>` | `WindowsReviewedHelperTransport.PipeNamePattern()` (`\A…\z`) |
| 6, 7 | `--nonce`, `<hex>` | exactly 64 of `0-9A-F` |
| 8, 9 | `--kind`, `qualify-chocolatey-helper` | both literal; the only kind value that exists |

The probe parser (`ReviewedHelperProbeBootstrap.TryParse`, a separate type) refuses anything else: other lengths, reordering, a repeated or unknown key, a different kind value, any of these with different case, or extra whitespace. Its result carries only PID, pipe and nonce, as today.

**Dispatch in `ElevatedHelperEntryPoint.RunAsync` (order is normative):**
1. `SelectsProbe(args)`: `args.Length == 10 && args[8] == "--kind"`. If true, take the probe path: the full probe parse, a non-Windows host or a non-elevated process → exit 20 before any connection.
2. Otherwise `SelectsProtocol2`, then the reviewed update path (unchanged).
3. Otherwise the protocol-1 path (unchanged).

**Mutual refusal (each is a required test):**
- `ReviewedHelperBootstrap.TryParse`, `SelectsProtocol2` and protocol-1 `HelperBootstrap.TryParse` each return false for every probe line. All three require exactly 8 arguments.
- Every 10-argument line that `SelectsProbe` does not select falls to protocol 1, which refuses it (exit 20).
- The probe parser refuses every 8-argument update line and every permutation of the probe line.
- A helper started in update mode never reaches the probe entry. A helper started in probe mode never reads a request frame (§3), so an update frame sent to it is never parsed.

Binding is as today: the per-request pipe is restricted to the user and SYSTEM; the helper connects with `TokenImpersonationLevel.Identification` within 30 s; `TryGetServerProcessId` must equal `--desktop-pid`; the helper opens the held peer handle (H3) and runs the handle-based `VerifyPeer` (fixed desktop path beside the helper, same session, same SID, trusted same signer); the nonce is one-shot, is echoed in the response, and the desktop compares it in fixed time. The kind is a fixed compiled literal. It carries no provider operation data (ADR-018).

## 3. Helper probe entry

`t0` is taken at pipe connection. The probe budget is **10 minutes from `t0`**, below the desktop's 11-minute response budget (as in N4), so the helper never writes after the desktop has given up.

| Stage | Work | Result |
|---|---|---|
| P0 | Parse (§2), elevation check, `ReviewedHelperHost.TryResolve`, connect, server-PID check, held peer handle, handle-based `VerifyPeer` | Failure → exit 21/22/23 with **no response**, as today |
| P1 | Start the **inbound monitor**: one pending 1-byte read on the pipe. The byte is never parsed. | A byte arrives → stop, **no response**, exit 27. EOF (desktop closed) → cancel any running stage, no response, exit 24 |
| A2 | `probePort.BeginProbe(peer, host.UserSid, out TokenVerdict verdict)`: the §2.1 linked-token rules, exactly as `BeginItem`, plus the peer-not-exited check (`PeerExited` is a `TokenVerdict` value). Returns an `IChocolateyProbeItem` only for `Accepted` | `tokenVerdict` = the closed verdict. Not `Accepted` → every `g1Status` = `NotEvaluated`, `observation = NotRun`; go to Stop |
| A3 | For **every** token of the compiled `CapabilityOperationAllowlist`, in ordinal order: `HelperDeploymentCapabilityCheck.Evaluate(route, item.RequesterCannotModify, peerImagePath)`, with the peer image path read from the held handle. Every token is evaluated, each independently. | `g1Status[token]` = the closed `HelperProtocolCapabilityStatus`. Any token not `Supported` → `observation = NotRun`; go to Stop |
| B′ | Exactly one `item.ObserveClient(remaining, ct)`, which calls `ChocolateyProfileObserver.ObserveClient(token, Helper, frozenInstalled, expectedFingerprint: null, remaining, ct)`. `frozenInstalled` = `ChocolateyReleaseList.Listed.Releases[^1].NativeVersion` (the newest compiled listed release; today 2.7.5). `remaining` = the probe budget minus the time elapsed; if it is ≤ 0, the result is `TimedOut` without any read | §4 fields 5–13 |
| Stop | Inside the native item: if B′ returned `Observed`, dispose its `ChocolateyHeldClientBinding` and require `IsDisposed`; refused and interrupted observations have already closed their held files. Then dispose the item (token), build the response through the probe codec, check that the inbound monitor saw no byte, and write the frame **once**. | A binding that is not disposed, a codec throw, any fault, or cancellation → **no response**, exit 24. Write failure → exit 26. Written → exit 0 |

The probe item has no `ObserveProfile`, `QueryCandidateAsync`, `RevalidateBeforeLaunch` or `UpgradeAsync` member, and no launch permission. The plain values that cross into Execution (`ChocolateyProbeValues`) carry the fingerprint's **length only**. The fingerprint string never leaves `Native/`. The peer handle is disposed after the write, as today.

**S3 observer additions** (values only; no behaviour change for existing callers):
- `failedStep`: the step whose check refused. Steps 1–7 are as numbered. Step 8 (source) and step 8a (installed value) both report `8`, and `refusalReason` tells them apart. Step 9 reports `9` (it can refuse only when `Compute` throws). The two disclosed refusals that run after step 7 (R5 `SetupAccessOverride`, §3a `UnlistedReleaseLeftover`) report `7` with the detail set.
- `nodesEvaluated` counts the `RequesterAccess.Evaluate` calls in Node mode during B′.
- `ancestorsEvaluated` counts the directories evaluated in Ancestor mode by `EvaluateAncestry` during B′.

## 4. The probe response (helper → desktop)

### 4.1 Fields (exact order; 15 properties; compact JSON; every property always present)

| # | Name | JSON type | Closed domain | Null |
|---|---|---|---|---|
| 0 | `protocolVersion` | string | `"2"` | never |
| 1 | `kind` | string | `"qualify-chocolatey-helper"` | never |
| 2 | `nonce` | string | 64 of `0-9A-F`; equals the bootstrap nonce (fixed-time compare on the desktop) | never |
| 3 | `tokenVerdict` | string | the `TokenVerdict` names exactly: `Accepted`, `FactsMalformed`, `PeerNotLimited`, `PeerSidMismatch`, `EvaluationSidMismatch`, `AuthenticationIdMismatch`, `EvaluationNotLimited`, `EvaluationNotMediumIntegrity`, `EvaluationRestricted`, `EvaluationAppContainer`, `EvaluationAdministratorsEnabled`, `EvaluationPrivilegeNotAllowed`, `PeerProcessIdMismatch`, `PeerExited`, `PeerTokenUnreadable`, `NoLinkedToken`, `EvaluationTokenUnreadable`, `EvaluationNotIdentificationLevel` | never |
| 4 | `g1Status` | object | one property per compiled allow-list token (`"Update/<route>"`), ordinal order; each value `Supported` \| `Absent` \| `Incomplete` \| `Unreadable` \| `Mismatch` \| `NotEvaluated` | never |
| 5 | `observation` | string | `NotRun` \| `Observed` \| `Refused` \| `TimedOut` \| `Cancelled` | never |
| 6 | `failedStep` | number \| null | integer 1–9 (§3) | null unless `Refused` |
| 7 | `refusalReason` | string | `None` \| `MachineInputsUnsupported` \| `SourceNotCommunity` \| `InstalledMismatch` (no `ProfileMismatch`: there is no expected fingerprint) | never |
| 8 | `refusalDetail` | string | `None` \| `UnlistedReleaseLeftover` \| `SetupAccessOverride` | never |
| 9 | `nodesEvaluated` | number | integer 0–1,048,576 | never |
| 10 | `ancestorsEvaluated` | number | integer 0–65,536 | never |
| 11 | `observedClientVersion` | string \| null | `ChocolateyNativeVersion.Normalized` form, ≤ 128 B (the S12 grammar) | null unless Q10 |
| 12 | `fingerprintPresent` | boolean | — | never |
| 13 | `fingerprintLength` | number | `0` or `64` | never |
| 14 | `diagnostic` | string | one fixed token per row (Q13) | never |

No field carries a path, user name, SID, fingerprint value, pipe name, PID or free text. All fields have closed domains, and the `diagnostic` field is closed too.

### 4.2 Response rules (codec; `CreateProbeResponse` throws and `TryReadProbeResponse` returns false unless every rule holds)

| ID | Rule |
|---|---|
| Q1 | Exactly the 15 properties of §4.1 in order, and the compiled `g1Status` keys, with no other property. Strict UTF-8, no BOM, `MaxDepth = 2`, no comments or trailing commas. **Whole-frame re-encode equality**, which rejects escapes, whitespace, reordering, numbers sent as strings and booleans sent as strings. Integers are canonical (no sign, no exponent, no fraction, no leading zero). |
| Q2 | Fields 0 and 1 are the exact literals. Field 2 is 64 uppercase hex. |
| Q3 | Every enum value is defined and spelled exactly (`ToString` round-trip). Numeric and alias forms are rejected. |
| Q4 | `g1Status` keys are exactly `CapabilityOperationAllowlist.Select(OperationToken)` in ordinal order, each once. |
| Q5 | `tokenVerdict ≠ Accepted` ⇒ every `g1Status` is `NotEvaluated` ∧ `observation = NotRun`. `tokenVerdict = Accepted` ⇒ no `g1Status` is `NotEvaluated`. |
| Q6 | `observation ≠ NotRun` ⇔ `tokenVerdict = Accepted` ∧ every `g1Status = Supported`. |
| Q7 | `observation = Refused` ⇔ `refusalReason ≠ None` ⇔ `failedStep ≠ null`. |
| Q8 | `SourceNotCommunity` ⇒ `failedStep = 8`. `InstalledMismatch` ⇒ `failedStep = 8`. `MachineInputsUnsupported` ⇒ `failedStep ∈ {1,…,7, 9}`. |
| Q9 | `refusalDetail ≠ None` ⇒ `refusalReason = MachineInputsUnsupported` ∧ `failedStep = 7`. |
| Q10 | `Observed` ⇒ `observedClientVersion` is a compiled listed release version (today `2.7.5`). `InstalledMismatch` ⇒ it is non-null and differs from the newest listed release. Otherwise it is null. |
| Q11 | `fingerprintPresent` ⇔ `observation = Observed`. `fingerprintLength = 64` if present, else `0`. |
| Q12 | `NotRun` ⇒ both counts are 0. `Observed` ⇒ both counts are ≥ 1. Counts outside §4.1 ranges are rejected. |
| Q13 | `diagnostic` is the row's fixed token: `NotRun` from A2 → `probe-token-refused`; `NotRun` from A3 → `probe-capability-refused`; `Observed` → `probe-observed`; `Refused` → `probe-observation-refused`; `TimedOut` or `Cancelled` → `probe-observation-interrupted`. |
| Q14 | Frame size ≤ **2,048 B** (codec bound, inside the unchanged 16 KiB `PipeMessageFraming` limit). One frame, followed by pipe EOF. |

### 4.3 Bounds

The upper bound combines maxima that no valid frame can hold together: 8 allow-list keys of 64 B each with `NotEvaluated`, the longest verdict (32 B), the longest reason and detail, a 128-B version, 7- and 5-digit counts, and the longest diagnostic (29 B). It is **1,283 B ≤ 2,048 B**. A passing frame on today's one-token allow-list is 468 B; with the client token added it is 518 B. The 4-byte length prefix is outside the bound.

### 4.4 Codec and goldens (S2)

- S2 adds the probe response codec only: `CreateProbeResponse` and `TryReadProbeResponse(frame, expectedNonce)`, a separate type or a separate region in Execution. There is no probe request codec.
- Goldens: one minimal frame per Q13 row; a passing frame on the one-token and the two-token allow-list; the largest valid frame.
- Mutation tests: each property missing, duplicated or swapped; each Q rule violated; wrong-case enums; `"1"`/`"3"` protocol; a frame of the update response fed to the probe reader and vice versa (both rejected).
- The package and client request and response goldens stay **byte-unchanged**.

## 5. Desktop side

### 5.1 Entry in `App.OnLaunched`

`QualificationProbeEntry.Classify(Environment.GetCommandLineArgs())` is pure and returns `Normal`, `Probe` or `Malformed`.
- `Probe` means exactly 2 elements, with element 1 ordinal-equal to `--qualify-chocolatey-helper`. Element 0 is not compared: the runtime may report the main assembly path rather than the apphost. Desktop identity is instead enforced by `TryHold` (both desktop files are in the binding set) and by the helper's peer verification and G1 peer-image hash.
- `Malformed` means any other line that contains the literal flag. It exits at once with no window, file, database or UAC.

The branch sits **directly after** the existing `CurrentUserDataOperationSession.TryAcquire` call, before `DataOperationLease.EnterBootstrap`, reset handling, the database and any window. It holds the session for the whole probe, including the helper exit wait and the result write, and disposes it before the process exits. The probe runs synchronously on a worker (`Task.Run(...).GetAwaiter().GetResult()`). No window is created.

### 5.2 Order of the desktop probe (`ReviewedHelperProbeTransport.ProbeAsync`; any failure ends the run with the stated outcome)

1. Create the fresh result folder (§5.3). If this fails, exit with no file.
2. Session `null` → `GateBusy`. `TryAcquire` threw → `GateUnavailable`.
3. The process token's elevation type is not `Limited` → `DesktopNotLimited`. This refuses an elevated desktop, UAC off and standard users before any prompt.
4. Fixed paths and initial launch trust (`InstalledHelperLaunchTrust`) → `LaunchTrustFailed`. Then the restricted per-request pipe and a fresh 32-byte nonce.
5. `TryHold` for **every** compiled allow-list token. All must be `Supported`, holding, and name the same helper path and the same helper `FileIdInfo`, or the outcome is `CapabilityNotHeld` and every binding is released. Only an ADR-022 owner-only deployment (v6 ledger, `pinned-unsigned-v2`, matching binding-set hashes) can pass, so **no prompt can appear elsewhere**.
6. Final launch trust → `LaunchTrustFailed`. Then one `runas` launch with the §2 line. `ERROR_CANCELLED` → `ElevationDeclined`. No process → `HelperNotStarted`. Both release the bindings.
7. Wait for connection (30 s, or helper exit first) → `HelperNotConnected`. Then the post-connect `VerifyPeer`: client PID equals the helper PID, path, session, SID, signer, and `FileIdInfo` equal to every held binding → `PeerNotVerified`. **Nothing is ever written to the pipe.**
8. Read one frame within 11 minutes of connection and require EOF → `NoValidResponse`. Then `TryReadProbeResponse` with the nonce → `ResponseRejected`, which discards all response fields. Otherwise → `ResponseAccepted`.
9. Observe helper exit (5 s). Terminate on any failure path. The bindings are released only after exit is observed and the transport is sealed (A-M2), as in the update transport. Then write the result file and exit.

The probe reuses the update transport's fixed paths, launch trust, pipe, hold, launcher shape and peer check. It omits the Chocolatey caller-trust check and request construction, because there is no operation. The update transport's `LaunchHelper` is not changed.

### 5.3 Result file

- **Location:** `<Path.GetTempPath()>\pcsteward-exact-artifact\qualification-result.json`. The temp path must be fully qualified and local (no UNC or device prefix). It is canonicalised to its long form (8.3 names expanded), and every existing component from the drive root is opened with `FILE_FLAG_OPEN_REPARSE_POINT`. A reparse attribute, or a final path different from the canonical path, refuses.
- **Folder:** created with `CreateDirectoryW`. An existing folder refuses: there is no reuse or merge. The folder handle is held and must not be a reparse point.
- **File:** created with `CREATE_NEW`, written once in full, flushed and closed. The desktop never deletes or overwrites anything; cleanup is the owner's C7 step. Integrity assumes no same-user malware (design §4 Safety).

Closed JSON, schema `pc-steward-qualification-probe-result:1`, exact property order:

| Property | Type / domain |
|---|---|
| `schema` | the literal above |
| `desktopOutcome` | `ResponseAccepted` \| `GateBusy` \| `GateUnavailable` \| `DesktopNotLimited` \| `LaunchTrustFailed` \| `CapabilityNotHeld` \| `ElevationDeclined` \| `HelperNotStarted` \| `HelperNotConnected` \| `PeerNotVerified` \| `NoValidResponse` \| `ResponseRejected` |
| `capabilityStatus` | object: compiled allow-list token → `HelperProtocolCapabilityStatus` name or `NotEvaluated` (the desktop's `TryHold` results) |
| `initialLaunchTrust`, `finalLaunchTrust`, `helperProcessCreated`, `pipeConnected`, `desktopPeerVerified`, `helperFileIdEqual`, `responseFrameReceived`, `pipeEofObserved`, `helperExitObserved`, `bindingHeldUntilExit`, `terminationFailed` | booleans (`helperProcessCreated` = UAC accepted; `bindingHeldUntilExit` = every binding `IsHolding` when exit was observed) |
| `helperExitCode` | integer or null |
| `pins` | null unless step 5 held; otherwise `ledgerSha256`, `capabilityFileSha256`, `desktopImageSha256`, `helperImageSha256` (64 uppercase hex, from the held binding and the ledger bytes it verified), `signerThumbprint` (40 uppercase hex) and `signerNotAfterUtc` (ISO 8601 `Z`), from the held helper image |
| `probe` | null unless `ResponseAccepted`; otherwise fields 3–14 of the accepted response, in order (no nonce) |
| `startedAtUtc`, `finishedAtUtc` | ISO 8601 `Z` |

The helper's verification of the desktop is not a separate fact: a helper writes only after its own handle-based `VerifyPeer` and the G1 peer-image hash, so `ResponseAccepted` implies it. The file holds no path, user name, SID, fingerprint value, nonce or pipe name.

### 5.4 Exit behaviour

After the file is closed and the session disposed, the desktop calls `Environment.Exit(code)`, with `0` only for `ResponseAccepted` and a fixed non-zero code per other outcome (S4 fixes the table). WinUI may not pass the code through, so **the result file is authoritative**. A missing file means no evidence.

### 5.5 What the probe path never does

It never opens or creates the database, runs a migration or reconciliation, takes a `DispatchClaimed` or any claim, writes history, scans, registers or touches the scheduled task, completes or verifies a factory reset, creates a window or view model, reads SQLite or remote data, uses the network, or sends a request frame. `ScheduledHost`, scan workflows and every UI path cannot reach it.

## 6. Compatibility (all fail closed with no result claim)

| Pair | Behaviour |
|---|---|
| New desktop, old helper | Within one deployment this cannot pass `TryHold`, because the ledger hashes bind both binaries. If it is forced (for example in a fixture), the 10-argument line goes to protocol 1, which exits 20 before connecting. The desktop records `HelperNotConnected` with `helperExitCode = 20`. |
| Old desktop, new helper | The old desktop never emits `--kind`, so the new helper serves updates exactly as today. The old desktop ignores `--qualify-chocolatey-helper` and **starts its normal UI** (database included), so there is **no result file**. The host procedure (S7) must therefore run C1 pins against a build known to contain the probe **before** C3. Under Decision 3 (c), the Part B deployment is such an old desktop. |
| Probe frame to an update reader, or update frame to the probe reader | Rejected on the exact property set (Q1; S14a/field-set checks). In normal operation neither happens, because each mode reads only its own codec. |
| Update frame sent to a probe-mode helper | Never parsed. The inbound monitor ends the run with no response (exit 27). |

## 7. Architecture-test obligations

S3 (helper):
- H1. The probe entry, probe bootstrap, probe item interface and their Execution callers reference none of: `LaunchJob`, `LaunchJobNativeMethods`, `NativeLaunchSpec`, `NativeCommandLine`, `ChocolateyCompiledCommand`, `ChocolateyClientQueries`, `ChocolateyClientUpdateArguments`, `ChocolateyExactListArguments`, `ChocolateyClientUpdateWatchdog`, `IChocolateyHelperItem` and its stage C/D/Launch members, `ReviewedUpdateWireCodec.TryReadRequest`, `CreateResponse`, `CreateClientResponse`, or `PipeMessageFraming.ReadAsync`.
- H2. `IChocolateyProbeItem` declares no launch, query or revalidation member. Its single implementation is in `Native/`. No field, property or return value of the probe path holds a `ChocolateyHeldClientBinding`, and the binding obtained from `ObserveClient` is disposed in the same method.
- H3. A behavioural test with an ordered fake writer: the binding is `IsDisposed` before the response bytes are created, and a binding that is not disposed yields no response.
- H4. The Execution allow-list of `Native` names grows only by `IChocolateyProbePort`, `IChocolateyProbeItem`, `ChocolateyProbeValues` and `TokenVerdict`. `ChocolateyProbeValues` has no fingerprint string member.
- H5. Only the native port constructs the probe item. `ElevatedHelperEntryPoint` constructs only the native ports.
- H6. The dispatch and mutual-refusal tests of §2, including a probe line against all three update parsers.
- H7. The inbound-byte test (exit 27, no response) and the EOF test (no response).

S4 (desktop):
- D1. `ProbeAsync` has exactly one `src` caller: the `OnLaunched` probe branch, through `QualificationProbeEntry`.
- D2. Nothing in `ScheduledHost`, `Application`, scan workflows, view models or windows references the probe types.
- D3. The update transport's launcher never emits `--kind` (argument-list test). The probe launcher is the only `src` emitter of the literal.
- D4. The existing pins on `HasSupportedMode`, `CapabilityOperationAllowlist` and `Supports` are unchanged, and their tests are unmodified.
- D5. The probe branch type references no `SqliteDatabase`, `FactoryReset`, `WindowsCurrentUserScheduledTaskRegistrar`, `DataOperationLease`, `Window` or view-model type.
- D6. Result-writer tests: an existing folder, a reparse component, a short-name temp path, a UNC temp path, and the closed schema (no path, SID or fingerprint value).

## 8. Interpretation (design §2 evidence; each run is one approved C3)

| Evidence | PASS | FAIL (a finding; no retry) | Inconclusive (no evidence) |
|---|---|---|---|
| E1 token match | `ResponseAccepted` ∧ `tokenVerdict = Accepted` | `ResponseAccepted` ∧ any other verdict (the verdict is the finding; for example `NoLinkedToken` = UAC off) | No accepted response |
| A3 / G1 | Every `g1Status = Supported` | Any other status after `Accepted` | Verdict not `Accepted`, or no response |
| E2 `AccessCheck` | `observation = Observed` ∧ `observedClientVersion = 2.7.5` ∧ `fingerprintPresent` ∧ `fingerprintLength = 64` ∧ both counts ≥ 1 ∧ `failedStep = null` | `Refused` (step, reason and detail are the finding; at steps 1, 4, 5 and 6 it may be a grant or an unreadable node, and both keep the route unusable on this host) | `NotRun`, `TimedOut`, `Cancelled`, or no response |
| E3 real transport | All result booleans true except `terminationFailed = false`; `helperExitCode = 0`; `pins` equal C1; the owner saw exactly one prompt for the signed helper with `--kind qualify-chocolatey-helper` in its details | Any false fact after `helperProcessCreated`; `CapabilityNotHeld` or `LaunchTrustFailed` on a C1-pinned deployment; `PeerNotVerified`, `NoValidResponse` or `ResponseRejected`; a prompt that differs from the expected one | `ElevationDeclined` (not repeated without new approval), `GateBusy`, `GateUnavailable`, `DesktopNotLimited`, `HelperNotStarted`, or no result file |
| E5 nothing changed | C4 = C2 (database and Chocolatey hashes), no `choco` process, only the result folder is new | Any difference | Baseline missing |

Overall **PASS** only if all five rows pass. Any FAIL stops use (Decision 2). An inconclusive run proves nothing and needs a new approval to repeat. A PASS proves **UAC and pre-launch enforcement only** for the deployment and pins it ran on.

## 9. Open questions for the reviewer and owner

1. Should a failed step also carry a closed access-refusal token (`RequesterModifiable` / `RequesterOwned` / `Unreadable` / `Reparse` / `OverBound`), so an E2 FAIL can tell an unprotected tree from an unreadable one? It is not included, to keep S3 small.
2. ADR-018 lists the bootstrap parameters as protocol, PID, pipe and nonce. The amendment states that `--kind` is a fixed literal that carries no operation data. Is that explicit text enough, or does ADR-018 itself need a note?
3. `nodesEvaluated` and `ancestorsEvaluated` count B′ only. Should the A3 (G1) evaluations be counted too?
4. Design open questions 2 and 3 (a package-mode observation; AQ1UAC versus probe E3) are not decided here. This spec assumes client mode only.

## 10. Review record

None yet. This spec needs a fresh-context independent review before S2 starts.
