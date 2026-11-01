# Task 50 — protocol-2 adapter gates: manifest capability seam (L-4) and helper-check history (§4.2)

Requirements: `FR-UPD-15`, `FR-UPD-01`, `FR-HIS-01`, `AC-UPD-01–11`, `TM-03/04/07/13`; retained `FR-UPD-08/09/14`, `TM-06`.

Status: **draft for independent review.** It discharges two of the four N5 items in [protocol-2 fact extension rev 3](../review/TASK_50_PROTOCOL2_REVIEWED_FACT_EXTENSION.md) §10. It also covers O2 / review-9 L3 (the `EvidenceId` format). It adds no code and grants no route admission, deployment, redeploy, host read, UAC or update authority. `ReviewedUpdateReview.HasSupportedMode` still admits only `WinGetUserExactV3`.

Sources read: AGENTS.md; fact extension rev 3 (all sections); dispatch composition and its Task 90 remediation; ADR-025 (protocol boundary and the ADR-022 row of the amendment map); ADR-022 including the Task 48 pinned-payload amendment; handover §3–§4; Profile V2; V2 task contract (validation and migration rule). Code read at `aad760e`: `ReviewedUpdateDispatchWorkflow.cs`, `UpdateDispatchAdmission.cs`, `ReviewedUpdateHistory.cs`, `SqliteReviewedUpdateHistory.cs`, `SqliteUpdateDispatches.cs`, `SqliteReviewedOperations.cs`, `ReviewedUpdateOperation.cs`, `ReviewedUpdateProtocol2Interpreter.cs`, `ReviewedUpdateWireCodec.cs`, `WindowsElevatedHelperTransport.cs` (`InstalledHelperLaunchTrust`, `ProtectedLaunchLayout`), `ChocolateyVersionGrammar.cs`, `ChocolateyNativeVersion.cs`, `scripts/Install-`/`Test-OwnerOnlyProtectedTestDeployment.ps1`, `scripts/OwnerOnlyPayloadIntegrity.ps1`, and the architecture tests.

---

# Part A — manifest capability seam (L-4) and `EvidenceId` enforcement (O2)

## A.1 Scope and baseline facts

| Fact (observed at `aad760e`) | Consequence |
|---|---|
| The ADR-022 ledger is `%ProgramFiles%\PC Steward OwnerOnlyTest State\deployment.json`, schema `pc-steward-owner-only-protected-test-v6`. Its fields are `installRoot`, `installerSid`, `certificateThumbprint`, `completed`, `files[{path,sha256}]` (signed install tree), `payloadOrigin` (`pinned-unsigned-v1`, or absent meaning `legacy-v6`), `candidateManifestSha256` and `executableImages`. `candidate-manifest.json` sits beside it as an array of `{bytes,path,sha256}`, pinned by the approved hash. | **No protocol or capability fact exists today.** ADR-025's ADR-022 row requires the future manifest to "identify supported operations/protocol". This part defines that fact. |
| The state root and install root are owned by Administrators with a protected DACL: SYSTEM and Administrators have Full control, Users have ReadAndExecute. There are no reparse points. | The non-elevated desktop can read the ledger. It cannot write it. |
| The helper is framework-dependent and multi-file. `PCSteward.ElevatedHelper.exe` is an apphost. The protocol code is in `PCSteward.ElevatedHelper.dll`, `Execution.dll`, `Application.dll`, `Domain.dll`, `Provider.Chocolatey.dll` and `Provider.WinGet.dll`. | Hashing the `.exe` alone does not bind the protocol implementation. The binding set (A.3) must cover the helper's code closure. |
| The launch is a path-based `ShellExecute runas` (`LaunchHelper`). No handle can be passed to it. Trust today comes from the fixed path, the ACL and reparse checks, WinVerifyTrust, the same-signer check and the post-connect `VerifyPeer` (image path and signer). | Hash binding has to be re-established immediately before `Process.Start` and cannot be inherited by the child process (A.4 step 3). |
| `RunAsync` calls `dispatcher.Supports` **outside** any try/catch, before the gate. `StillSupported` already turns exceptions into `false`. | A throwing seam would currently escape `RunAsync` before the claim. A.2 fixes this. |
| The protocol-2 codec writes and reads the literal `"2"`. | Introduce `ReviewedUpdateWireCodec.ProtocolVersion = "2"`. It is the only value the seam compares against. |

This is a **compatibility and consistency gate**, not a new security boundary against administrators. ADR-018 launch trust remains the security boundary. The gate's job is to keep any partial or mismatched deployment (old helper with new desktop, new desktop with an old ledger, a ledger from another deployment) from reaching a claim, UAC, or a no-response Uncertain barrier. ADR-025 phrases the rule as "mismatched/partial deployments reject without provider launch".

## A.2 Interfaces and placement

| Type / member | Project | Visibility | Role |
|---|---|---|---|
| `ReviewedUpdateWireCodec.ProtocolVersion` (`"2"`) | Execution | internal const | The single compiled protocol value. The codec, the seam and helper bootstrap all use it. |
| `HelperProtocolCapabilityStatus { Supported, Absent, Incomplete, Unreadable, Mismatch }` | Execution | internal | Closed result. Every value except `Supported` means unsupported. |
| `HelperProtocolCapabilityResult(Status, string Diagnostic)` | Execution | internal sealed record | The diagnostic is one fixed token per status (`helper-capability-absent` and so on). It never contains path, hash or JSON text. |
| `DeploymentCapabilityPolicy.Evaluate(DeploymentCapabilityInputs inputs, UpdateRouteContract route)` | Execution | internal static, **pure** | All parsing and comparison over bytes and hashes already read. Testable on Linux. The helper reuses it (A.4 step 4). |
| `DeploymentCapabilityInputs` | Execution | internal sealed record | Fields: `LedgerBytes`, `CandidateManifestBytes`, `CapabilityBytes`, `CurrentUserSid`, `ExpectedInstallRoot`, and `ObservedHashes` (read-only map: fixed leaf → `(bytes, SHA-256)`). |
| `IHelperProtocolCapability` | Execution | internal interface | `HelperProtocolCapabilityResult Evaluate(UpdateRouteContract route);` and `HelperProtocolCapabilityResult TryHold(UpdateRouteContract route, out HeldHelperBinding? binding);`. The protocol version is **not** a parameter, so a caller cannot choose it. |
| `HeldHelperBinding : IDisposable` | Execution | internal sealed | Share-read handles on the binding set, opened with `FileShare.Read` only (no write and no delete share), plus the verified hashes. Used only by the protocol-2 transport (A.4 step 3). |
| `OwnerOnlyDeploymentCapabilityReader : IHelperProtocolCapability` | Execution | internal sealed | Windows producer. Uses fixed paths derived only from `SpecialFolder.ProgramFiles` and `Environment.ProcessPath`. Reuses `InstalledHelperLaunchTrust.ResolveFixedPaths` and the `ProtectedLaunchLayout` no-reparse and caller-cannot-modify primitives. |
| `ChocolateyRuntimeLimitsRouteDispatcher : IReviewedUpdateRouteDispatcher` (future adapter) | Execution | internal | `Supports(op) = TryGetProfileDigest(op, out _) && capability.Evaluate(op.RouteContract).Status == Supported`, wrapped so that **any** exception returns `false`. |
| `IReviewedUpdateRouteDispatcher.Supports` doc | Application | — | Doc text becomes: "compiled route check plus, for helper routes, the adapter's protected deployment-capability seam; never eligibility evidence; must not throw". Application never sees a capability type. |
| `ReviewedUpdateDispatchWorkflow.RunAsync` | Application | — | Small robustness change: the pre-claim `Supports` goes through the same `StillSupported`-style catch. An exception means NotStarted with no claim. |
| `ReviewedUpdateOperation` (O2) | Domain | — | See A.6. |

Architecture tests (`tests/PCSteward.ArchitectureTests`):
- `IHelperProtocolCapability` implementations exist only in Execution.
- The reader's and policy's source files contain no `Sqlite`, `Microsoft.Data.Sqlite`, `Registry`, `Environment.GetEnvironmentVariable` or `HttpClient`.
- No Application, Persistence or ScheduledHost file references any capability type. ScheduledHost already cannot reference Execution.

## A.3 Producer: the pinned capability statement

**Origin.** A checked-in file, `PCSteward.DeploymentCapability.txt`, published at the payload root. It is a build artifact like any other: its SHA-256 appears in the approved candidate manifest, so the owner-approved manifest hash pins its bytes. A unit test asserts that its bytes equal the bytes generated from `ProtocolVersion` and the compiled route allowlist. The capability therefore cannot drift from the code.

**Exact bytes.** Printable ASCII, LF line endings, a final LF, no BOM, no other whitespace, at most 1,024 B. The minimal valid file is 103 B:

```
pc-steward-deployment-capability:1\n          (35 B)
helper-protocol:2\n                           (18 B)
operation:Update/ChocolateyMachineRuntimeLimitsV3\n   (50 B)
```

Rules:
- Exactly one `helper-protocol:` line.
- 1–8 `operation:` lines, unique, in ordinal order, each from the compiled closed allowlist. Today the allowlist has one entry.
- Any other line, duplicate, unknown token, case variant or encoding means `Mismatch` for the whole file.

**Script changes (producer; reviewed implementation only, no host action):**

| Script | Change |
|---|---|
| `OwnerOnlyPayloadIntegrity.ps1` `Read-OwnerOnlyCandidateManifest` | A manifest that contains `PCSteward.DeploymentCapability.txt` is a v2 payload. Validate its exact bytes. |
| `Install-OwnerOnlyProtectedTestDeployment.ps1` | Write `payloadOrigin = 'pinned-unsigned-v2'` iff the capability file is in the pinned manifest. Otherwise keep `pinned-unsigned-v1`. Ledger schema stays v6, so the removal script is unchanged. |
| `Assert-OwnerOnlyPayloadProvenance` / `Test-OwnerOnlyProtectedTestDeployment.ps1` | Recognise `pinned-unsigned-v2`, which requires a valid capability file. Report `helperProtocol` in the output. |

Existing v6, `legacy-v6` and `pinned-unsigned-v1` deployments gain nothing: no capability means `Absent`.

**Binding set.** A compiled closed list of fixed leaves, each of which must appear exactly once in `ledger.files` with a matching hash:
- `PCSteward.Desktop.exe` (it must equal `Environment.ProcessPath`);
- `PCSteward.ElevatedHelper.exe`, `.dll`, `.deps.json` and `.runtimeconfig.json`;
- `PCSteward.Execution.dll`, `PCSteward.Application.dll`, `PCSteward.Domain.dll`, `PCSteward.Provider.Chocolatey.dll` and `PCSteward.Provider.WinGet.dll`;
- `PCSteward.DeploymentCapability.txt`.

A build test asserts that the list equals the project-assembly closure in the published helper `deps.json`. Size bounds: each file ≤ 64 MiB and the whole set ≤ 128 MiB, otherwise `Unreadable`.

## A.4 What the seam reads, and evaluation order

**Step 1 — `Supports(op)`, before the claim (outside the gate).** Each check runs in order and the first failure ends evaluation.

| # | Check | Failure status |
|---|---|---|
| 1 | `TryGetProfileDigest(op)`: route shape, plus O2 with `EvidenceId = "choco-profile-v2:" + 64 uppercase hex`. **No file I/O happens before this passes.** | `false` (route) |
| 2 | Fixed layout: `ProcessPath == <PF>\PC Steward\PCSteward.Desktop.exe`. The state root, `deployment.json`, `candidate-manifest.json` and the binding set all have no reparse component and cannot be modified by the caller. | `Absent` if the state root or ledger is missing; `Unreadable` otherwise |
| 3 | Ledger: ≤ 1 MiB, strict UTF-8, no BOM; JSON with no duplicate or unknown keys and exact types; `schema` v6; `completed == true`; `rollbackRequired` absent or false; `installRoot` equals the fixed root; `installerSid ==` current user SID (owner-only, matching the ADR-022 validator); `payloadOrigin == "pinned-unsigned-v2"`; `files` ≤ 512 entries with unique ordinal paths and 64-hex hashes. | `Absent` for a `v1` or legacy origin; `Incomplete` for an incomplete or rollback ledger; `Mismatch` for SID or root; `Unreadable` otherwise |
| 4 | `SHA256(candidate-manifest.json) == ledger.candidateManifestSha256`. The manifest contains exactly one capability entry, and its hash equals the corresponding `ledger.files` entry (the file is unsigned, so the hash is identical). | `Mismatch` |
| 5 | Read the capability file through a share-read handle. Its hash equals the ledger entry. It parses exactly (A.3). `helper-protocol == ProtocolVersion`. The `operation:` set contains `op.RouteContract`. | `Mismatch` |
| 6 | Hash every binding-set leaf through a share-read handle. Each hash equals the corresponding `ledger.files` entry. | `Mismatch` (an old helper, a foreign desktop, or a partial copy) |
| 7 | Any exception (I/O, ACL, JSON, crypto, unexpected) | `Unreadable`, so `Supports = false` |

**Step 2 — `StillSupported(op)`, after `TryConsume` and before `DispatchAsync`.** A full re-evaluation of steps 1–7. There is no cache: neither a process cache nor SQLite holds a capability. A failure, or an exception, finalizes the claim `Cancelled` with a NotInvoked settlement. This is the existing workflow path.

**Step 3 — inside `DispatchAsync`, in the protocol-2 transport, at final pre-launch trust.**
1. Call `TryHold(route, out binding)`. This repeats steps 2–6 and keeps the share-read handles open.
2. Run the existing `validatePreLaunchTrust`.
3. Call `Process.Start(runas)`.
4. After connecting, run `VerifyPeer`. It now also requires the peer image path to be the held helper path.
5. Dispose the binding only after peer verification succeeds, or after the helper exits.

A `TryHold` failure is `ReviewedHelperTransportOutcome.LaunchRefusedBeforeElevation`. The interpreter turns it into `NotStarted(Failed)`, a NotInvoked settlement, and the history text "Not updated: nothing was started." There is no UAC prompt.

**Step 4 — helper side (proposed for confirmation; ADR-025 requires "both endpoints prove matching capability").** In stage A, after correlation, the helper runs `DeploymentCapabilityPolicy.Evaluate` over the same protected files using its own reads. It also checks its own image and the peer desktop image hashes against the ledger. A failure is `Refused/RequestNotAdmitted`, with fields 16–18 null. That is settled, NotInvoked, and recorded as `Refused`. The helper bootstrap accepts only `--protocol ProtocolVersion`, so there is no protocol-1 fallback.

## A.5 Trust boundaries and failure semantics

| Input | Trusted? |
|---|---|
| Protected ledger, candidate manifest and capability file under the admin-owned, non-reparse, caller-unwritable roots, and bound by hash to the actual binaries | Yes, as a statement of deployment consistency only |
| SQLite (any table), stored drafts and claims, `FreshUpdateDispatchEvidence`, any `EvidenceId` beyond its format | **Never** |
| Remote or catalogue metadata, the helper's wire `protocolVersion`, helper diagnostics | **Never** a source of capability |
| Environment variables, the registry, the current directory, assembly version strings, output of the `Test-` script, user-profile paths | **Never** |

| Phase | Failure | Result |
|---|---|---|
| Before the claim | Any non-`Supported` status, or any exception | `NotStarted` with the message "This reviewed provider, mode or route is unavailable. No update was started." No claim, gate or UAC. |
| After the permit | Same | `Cancelled`, NotInvoked; the claim is finalized and the dispatcher is never called |
| Pre-launch (transport) | Same | `LaunchRefusedBeforeElevation`, which becomes `Failed`/NotInvoked; no UAC |
| After the request is written | Helper-side mismatch | `Refused/RequestNotAdmitted`, which settles as `Refused` |
| After the request is written | Bootstrap rejected (old helper), which gives no response | Uncertain plus a barrier with no retry, per rev 3 §7. Steps 1–3 exist to make this unreachable for a consistent ledger. |

## A.6 `EvidenceId` enforcement (O2, review-9 L3)

`ReviewedUpdateOperation`'s private constructor is shared by construction and `TryRestore`. Add: if `routeContract == ChocolateyMachineRuntimeLimitsV3`, the `EvidenceId` must match `\Achoco-profile-v2:[0-9A-F]{64}\z` (exactly 81 characters); otherwise throw `ArgumentException`. `TryRestore` already maps `ArgumentException` to `false`. Expose `ReviewedUpdateOperation.ProfileEvidencePrefix` and make the interpreter use it. `TryGetProfileDigest` keeps its own check as defence in depth. Other routes, including `ChocolateyMachineProvisionalV3`, are unchanged.

There is no format or digest change: `EvidenceId` is already a canonical field. There is no migration and no schema bump.

Back-compatibility for stored rows. Production cannot hold a runtime-limit schema-3 row, because the route is not admitted and review confirmation refuses Chocolatey. A non-conforming row could exist only through forgery or a test fixture:

| Stored location | Behaviour after the change (existing code paths) |
|---|---|
| `reviewed_update_operations` draft | `ReadReviewedOperationAsync` throws "corrupt". The draft is unreadable and is never admitted. Claim CAS is impossible because no operation can be constructed. |
| `update_dispatch_claims` in `DispatchClaimed` | `GetDispatchStateAsync` throws, so the workflow returns `NotStartedClaimRetained`. `HasUnresolvedDispatchesAsync` (SQL presence only) stays true. `RecoverUnfinishedDispatchesAsync` throws, so the gate fails closed. The barrier remains until explicit reconciliation (an existing owner note, G6). |
| `update_dispatch_claims` in `Uncertain` | History shows `IntegrityVerified=false` and the row blocks (non-Final). |
| `update_dispatch_claims` in `Final` | History shows `IntegrityVerified=false` with "outcome cannot be trusted". **No new barrier is created**, consistent with Task 90 L1, where blocking mirrors the store state. |

## A.7 Negative test matrix (Part A)

| Group | Case → expected |
|---|---|
| Order | A malformed `EvidenceId` (63 or 65 digits, lowercase, `choco-profile-v1:`, prefix only, a trailing character) is rejected by the constructor and by `TryRestore` with a recomputed digest. `Supports` returns false with **zero** capability calls, and no claim row exists. A WinGet op returns false. A valid op with a fake `Absent` capability returns false, no claim, and the dispatcher is never called. |
| Ledger | Missing state root or ledger → `Absent`. `legacy-v6` or `pinned-unsigned-v1` → `Absent`. `completed=false` or `rollbackRequired=true` → `Incomplete`. Unknown origin, a duplicate key, an unknown key, a wrong type, more than 1 MiB, a BOM, or invalid UTF-8 → `Unreadable`. A different `installerSid` or `installRoot` → `Mismatch`. A duplicate `files` path → `Unreadable`. |
| Manifest / capability | Candidate-manifest hash mismatch. Capability absent from the manifest or the root. Capability bytes with CRLF, a trailing space, a BOM, no final LF, an uppercase header, protocol `1`, `3` or `2 `, two protocol lines, the route missing, a duplicate or unsorted operation, an unknown token, or more than 1,024 B. Each → `Mismatch` or `Unreadable`. |
| Binding | The hash of the helper `.exe`, any helper-closure DLL, or the desktop `.exe` differs from the ledger → `Mismatch`. Any reparse point or caller-writable path in the roots → `Unreadable`. |
| Phases | Capability valid before the claim, then removed → `Cancelled`/NotInvoked, dispatcher not called. Valid through `StillSupported`, then the helper is replaced → `LaunchRefusedBeforeElevation` → `Failed`/NotInvoked, and `launchHelper` is never invoked. A seam that throws before the claim → `NotStarted` with no claim (new workflow catch). Two `Supports` calls → two full reads (no cache). |
| Helper side | A ledger mismatch observed by the helper → `Refused/RequestNotAdmitted` with null fields → settled `Refused`. Bootstrap `--protocol 1` → no response. |
| Stored O2 rows | Each A.6 row has its stated behaviour, and `HasUnresolvedDispatchesAsync` agrees with history `BlocksOtherUpdates`. |
| Regression | Protocol-1 transport tests are unchanged. The capability file's bytes equal the bytes generated from the compiled constants. The binding set equals the helper `deps.json` closure. Script fixtures: v1 manifest → `pinned-unsigned-v1`; v2 manifest → `pinned-unsigned-v2`; a malformed v2 capability makes the install fail before signing. |

## A.8 Non-goals

Route admission (`HasSupportedMode`); the protocol-2 transport itself; any redeploy, signing or certificate action; changing ADR-018 launch trust; WinGet; protocol 1; caching or persisting capability; full-tree re-hashing at dispatch (the ADR-022 validator still owns that).

## A.9 Open questions

| # | Question | Who |
|---|---|---|
| A-Q1 | Can a `FileShare.Read`-only handle on the helper code files be held across `ShellExecute runas` without breaking the image loader? This needs native evidence. If the answer is no, the fallback is ACL plus `VerifyPeer`, plus re-hashing the binding set immediately after peer verification and before writing the request. | Reviewer and native evidence |
| A-Q2 | If the payload becomes self-contained, must the runtime files (`hostfxr`, `coreclr` and so on) join the binding set, or do the ACL and the ADR-022 validator suffice? | Reviewer |
| A-Q3 | `Supports` stays a synchronous `bool` with bounded I/O of about 10 files and at most 128 MiB. The alternatives are an async `SupportsAsync` or requiring desktop callers to invoke `RunAsync` off the UI thread. | Reviewer |
| A-Q4 | Confirm helper-side step 4 (stage-A `RequestNotAdmitted`) as consistent with rev 3 §3.1, or defer it as a separate obligation. | Reviewer |
| A-Q5 | Is a short ADR-022 note needed for `payloadOrigin = pinned-unsigned-v2`? It sits inside ADR-025's accepted ADR-022 row, and no host action follows. | Reviewer, then owner only if the reviewer says an ADR change is needed |

---

# Part B — history persistence of the desktop-derived candidate check (§4.2, M2, O3/O6)

## B.1 Scope

The interpreter returns an Execution-internal `ReviewedHelperCandidateCheck` beside the observation. Today the workflow records only the version-2 terminal. Part B covers four things:
- an Application-level closed type;
- the minimal path that carries it to finalization;
- a version-3 terminal slot written in the same UPDATE;
- strict read-back for history display only.

The value never changes `Status`, settlement or outcome, and it is never authority.

## B.2 Interfaces

| Type / member | Project | Notes |
|---|---|---|
| `enum RecordedHelperCheckKind { NotObserved, Confirmed, Refused, Unconfirmed }` | Application | Mirrors the §4.1 kinds by name. |
| `enum RecordedHelperRefusalReason { None, RequestNotAdmitted, MachineInputsUnsupported, SourceNotCommunity, InstalledMismatch, ProfileMismatch, PreflightQueryFailed, CandidateMismatch, ApprovalExpired }` | Application | Same names as the wire enum. |
| `sealed class RecordedHelperCandidateCheck` | Application | Private constructor. `static NotObserved`. Members: `Kind`, `RefusalReason`, `ReportedInstalledVersion?`, `ReportedCandidateVersion?`. XML doc: "desktop-derived, recorded at the time; reported by PC Steward's administrator helper; never authority". |
| `public static RecordedHelperCandidateCheck FromDesktopDerived(kind, reason, installed, candidate)` | Application | Throws on a structural violation (B.5 rules R1–R4 and the structural version form). An architecture test requires **exactly one** call site in `src`: the Execution adapter below. |
| `internal static bool TryRestore(...)` | Application | Used only by `ReviewedUpdateTerminalRecord.TryParse`. |
| `public bool IsConsistentWith(ReviewedUpdateOperation op, UpdateDispatchOutcome outcome, UpdateDispatchSettlementState settlement)` | Application | One pure function holding all the B.5 invariants. Shared by the workflow (pre-check), the store (write) and the reader (read). |
| `public interface IReviewedHelperVersionGrammar { bool IsNormalized(string value); }` | Application | Grammar port. Persistence cannot reference Provider.Chocolatey. |
| `ChocolateyHelperVersionGrammar : IReviewedHelperVersionGrammar` | Provider.Chocolatey | Delegates to `ChocolateyVersionGrammar.IsNormalized(value, 128)`. This is the same facade the codec uses, so it is not a second grammar. |
| `ReviewedHelperCandidateCheck.ToRecorded()` | Execution (interpreter file) | **The single adapter function.** Maps the enums with an explicit exhaustive `switch` (no numeric cast) and copies fields 16 and 17 verbatim. |
| `ReviewedUpdateDispatchObservation(..., bool RestartReported = false, RecordedHelperCandidateCheck? HelperCheck = null)` | Application | **The minimal interface change**: one appended optional member, as rev 3 §4.1 itself specifies. `null` means `NotObserved`. The `NotStarted` factory, existing fakes and dispatchers are unchanged. |
| `UpdateDispatchTerminal(outcome, occurredAt, exitCode, diagnostic, RecordedHelperCandidateCheck? helperCheck = null)` | Application | Gains a `HelperCheck` property. `GetReservedPayload` emits version 3 iff `Kind != NotObserved`. |
| `ReviewedUpdateTerminalRecord` | Application | Gains `RecordedHelperCandidateCheck HelperCheck`. `TryParse(reserved, IReviewedHelperVersionGrammar? grammar, out record)`. |
| `ReviewedUpdateDispatchResult(..., RecordedHelperCandidateCheck? HelperCheck = null)` | Application | Set only to the value actually persisted, so live text equals history. |
| `ReviewedUpdateHistoryView(..., string? HelperReport)` | Application | A separate field, never concatenated into `Status`. |
| `SqliteDatabase(string path, IReviewedHelperVersionGrammar? helperVersionGrammar)` | Persistence.Sqlite | New overload. Without a grammar, **every v3 terminal is unreadable** (fail closed). The existing constructor is unchanged. |

Workflow flow: `FinalizeAsync` gains a `RecordedHelperCandidateCheck` argument.
- The Uncertain, NotInvoked and settled-classification paths pass `observation.HelperCheck ?? NotObserved`.
- The orphan-claim path, the `TryConsume`/`StillSupported` paths and the dispatcher-exception path pass `NotObserved`.
- Before building the terminal, the workflow calls `IsConsistentWith`. If the check is inconsistent, it records `NotObserved` and appends the fixed diagnostic "helper-check-not-recorded". It **never** fails the write over the check, because a failed write would turn the outcome into Uncertain and so change the outcome.
- `IUpdateDispatchStore.RecordFinalAsync` keeps its signature. The store re-checks `terminal.HelperCheck.IsConsistentWith(claim.Draft.Operation, …)` and throws on a violation, as defence in depth.

## B.3 Storage choice: terminal header version 3 in the existing reserved slot

| Criterion | v3 slot (**chosen**) | New columns plus migration 13 |
|---|---|---|
| Same transaction | Yes: the same single `UPDATE … terminal_payload, terminal_digest` | Requires extending the UPDATE and its exact-match readback |
| Integrity | Covered by the existing `terminal_digest` with no extra work | Needs new digest coverage |
| Precedent | The entry assessment says "no additional owned-work column or persistent leaf; claim lifecycle state carries exclusion". The terminal was already versioned in place (v1 → v2). | Contradicts that precedent |
| Rollback | An older build reads v3 as an unreadable terminal: Final rows show "cannot be trusted", non-Final rows still block by state. The database stays openable. | The schema bump makes an older build refuse the whole database (`RefuseFutureSchemaAsync`) |
| Capacity | 509 B header + 12,288 B diagnostic ≤ 16,384 (B.4) | Not applicable |

There is no SQL, CHECK or `schema_migrations` change, and no external file or barrier leaf. The V2 contract still requires old, new, missing, corrupt, partial and rollback fixtures (B.7).

**Writer rule:** emit v2 (byte-identical to today) when the check is `NotObserved`; emit v3 only for Confirmed, Refused or Unconfirmed. Recovery keeps writing v2 Uncertain. The reader therefore treats **v3 with `NotObserved` as corruption**.

## B.4 Byte layout (v3)

ASCII header lines, each ending in LF, each ≤ 256 B, bytes 0x20–0x7E only. The diagnostic follows as strict UTF-8, then zero padding to exactly 16,384 B.

| # | Line | Max line + LF |
|---|---|---|
| 0 | `pc-steward-update-terminal:3` | 29 |
| 1 | Outcome (exact enum name) | 16 |
| 2 | `OccurredAt` in `O` format (33 characters) | 34 |
| 3 | Exit code, or `None` | 12 |
| 4 | Settlement state | 19 |
| 5 | Settlement facts, or `None` (6 × `False` + 5 `/` = 35) | 36 |
| 6 | `helper-check:` + `Confirmed`\|`Refused`\|`Unconfirmed` | 25 |
| 7 | `helper-refusal:` + reason (the longest is `MachineInputsUnsupported`) | 40 |
| 8 | `helper-installed:` + version (≤ 128) or `None` | 146 |
| 9 | `helper-candidate:` + version (≤ 128) or `None` | 146 |
| 10 | Diagnostic byte length in decimal, no leading zeros | 6 |
| | **Maximum header** | **509 B** (v2: 152 B) |
| | Diagnostic: ≤ 4,096 UTF-16 code units, at most 3 B each in UTF-8 | 12,288 B |
| | **Maximum total** | **12,797 ≤ 16,384** (3,587 B headroom) |

`None` cannot collide with a version, because a normalized version must start with a digit. The differential corpus includes `None`, `none` and `None ` as invalid versions.

## B.5 Validation (`IsConsistentWith`, plus the strict parser)

**Structural rules (factory and parser)**
- R1: `reason != None` ⇔ `Kind == Refused`.
- R2: `Confirmed` ⇒ both versions are non-null (S5).
- R3: `Unconfirmed` ⇒ both versions are null.
- R4, for `Refused`:
  - `RequestNotAdmitted` ⇒ both null (S6);
  - candidate non-null ⇒ installed non-null, and the reason is not `RequestNotAdmitted` or `PreflightQueryFailed` (S7);
  - `ProfileMismatch` ⇒ installed non-null (S9, version part).

**Version rule.** Each version has 1–128 characters from `[0-9A-Za-z.-]` and starts with a digit; this is the structural form, a superset of `ChocolateyNativeVersion.Normalized`. On read it **also** requires `grammar.IsNormalized(v)`.

**Operation-bound rules (against the restored frozen operation)**
- O-route: `Kind != NotObserved` ⇒ `op.HasCurrentSchema && op.RouteContract == ChocolateyMachineRuntimeLimitsV3`.
- P1: `Confirmed` ⇒ installed and candidate equal the frozen versions (ordinal comparison).
- P2: `InstalledMismatch` ⇒ installed is null or differs from frozen.
- P3: `ProfileMismatch` ⇒ installed equals frozen.
- P4: `CandidateMismatch` ⇒ installed equals frozen, and the candidate is null or differs from frozen.
- P5: a candidate present with any other reason equals frozen.

The fingerprint is not stored, so its part of P1/P3/P4 is not re-checked.

**Outcome × settlement invariants.** These mirror the §4.1 status table; the store's existing rules still apply.

| Kind | Allowed (outcome, settlement) |
|---|---|
| NotObserved | Any pair the store already allows |
| Confirmed | `Success`/`Unverified` with OwnedWorkStopped; `Failed` with NotInvoked (launch failure or timeout) or OwnedWorkStopped (provider failure); `Cancelled` with NotInvoked; `Uncertain` with any unsettled state. **Never** `Refused`, `ElevationDenied` or `RestartRequired` (§4.3/O4). |
| Refused | `Refused` with NotInvoked; `Uncertain` |
| Unconfirmed | `Failed` or `Cancelled` with NotInvoked; `Uncertain` |

**Read failure.** Any violation, any parse failure, a digest mismatch, a missing grammar, or a v3 terminal with `NotObserved` sets `IntegrityVerified=false` and `Terminal=null`. The row is then presented as an integrity failure. It blocks iff its state is non-Final, unchanged from L1. A v2 terminal parses with `HelperCheck = NotObserved`, and its rules and text are exactly as today.

## B.6 Presentation (`HelperReport`; Status text unchanged)

| Kind | `HelperReport` |
|---|---|
| Confirmed | "Recorded at the time: reported by PC Steward's administrator helper: the exact reviewed version matched its protected machine checks." |
| Refused | "Recorded at the time: reported by PC Steward's administrator helper: it refused before updating because {fixed reason text}. Versions it reported: installed {v \| not reported}; available {v \| not reported}." |
| Unconfirmed, NotObserved, or integrity failure | `null` |

The Confirmed sentence is the rev-3 §4.1 live text, prefixed with "Recorded at the time:".

Reason texts:

| Reason | Text |
|---|---|
| RequestNotAdmitted | "the request was not accepted (for example, it was no longer fresh or did not come from your account)" |
| MachineInputsUnsupported | "Chocolatey's protected machine files or settings could not be confirmed as supported" |
| SourceNotCommunity | "Chocolatey could not be confirmed to use only the official Community repository" |
| InstalledMismatch | "the installed version it found differed from the reviewed version" |
| ProfileMismatch | "Chocolatey's configuration changed after the review" |
| PreflightQueryFailed | "its check for the available version did not complete cleanly" |
| CandidateMismatch | "the available version it found differed from the reviewed version" |
| ApprovalExpired | "the review's time limit passed before the update could start" |

Presentation rules:
- `Status` never contains "administrator helper".
- `HelperReport` never contains "updated", "success", "verified" or "completed".
- The desktop renders `HelperReport` in the candidate-evidence area, never beside the status line, so it is never adjacent to Success.
- The diagnostic never produces any `HelperReport`.
- "Helper qualified" is still never displayed.

## B.7 Negative and fixture matrix (Part B)

| Group | Case → expected |
|---|---|
| Fixtures (V2 contract) | **Old:** a v2 row read by the new reader gives NotObserved and identical text. **New:** every allowed (kind, outcome, settlement) round-trips byte-exactly, and the maximal frame (509 B header + 12,288 B diagnostic) round-trips. **Missing:** a v3 header with 7 lines, or a v2 header with 11 lines, is unreadable. **Corrupt:** single-byte tampering, with and without a recomputed digest, is unreadable. **Partial:** a missing length line, a header without its final LF, or non-zero padding is unreadable. **Rollback:** a frozen copy of the v2 parser returns `false` on v3 without throwing; on an older build a Final v3 row is non-blocking and an Uncertain v3 row blocks. |
| Enumerations | Wrong case, numeric `1`, an unknown kind or reason, a reason ≠ None with Confirmed or Unconfirmed, `None` with Refused, `NotObserved` in v3 → unreadable. |
| Versions | Empty, `None `, `none`, 129 characters, `1.0+meta`, `01.0`, `1.0`, `1.0.0.0`, `1.0.0-`, non-ASCII → unreadable. A differential test runs the codec and the history reader over the shared O9 corpus. No grammar injected → all v3 terminals are unreadable. |
| R and P rules | Confirmed with a null version or a version ≠ frozen. `RequestNotAdmitted` with values. A candidate without an installed version. A candidate with `PreflightQueryFailed`. `ProfileMismatch` with installed null or ≠ frozen. `CandidateMismatch` with candidate = frozen. Another reason with candidate ≠ frozen. `InstalledMismatch` with installed = frozen. Each → the store throws on write and the reader marks the row unreadable. |
| Outcome matrix | Confirmed with Refused, ElevationDenied or RestartRequired. Refused with Success, Failed, Cancelled or OwnedWorkStopped. Unconfirmed with Success, Unverified or Refused. A non-NotObserved check on a WinGet or schema-2 row. Each → the store throws and the reader marks the row unreadable. |
| Workflow | An inconsistent check (test-only factory call) gives NotObserved plus "helper-check-not-recorded", and the outcome, settlement and message prefix are unchanged. Parameterising the same observation over every kind yields identical outcome and settlement. A diagnostic containing "matched its protected machine checks" yields NotObserved. A dispatcher exception gives NotObserved. |
| Presentation | Exact texts. A Success row with Confirmed has a separate `HelperReport` and Status text unchanged. An integrity failure gives null. Candidate text for each provenance is independent of the helper kind (behavioural test). |
| Architecture | `FromDesktopDerived(` has exactly one call site in `src` (the interpreter file). `ReviewedUpdateTerminalRecord` is referenced only by history files. `RecordedHelperCandidateCheck` is not referenced by `ReviewedUpdateReview*`, `UpdateDispatchAdmission` (admission and permit types), dispatch-evidence or reconciliation sources. Reflection finds no method that takes either `RecordedHelperCandidateCheck` or `ReviewedCandidateProvenance` and returns the other. The existing interpreter boundary test is unchanged. |

## B.8 Non-goals

Storing the fingerprint or the diagnostic as a fact; any change to `Status`, settlement or outcome driven by the check; desktop rendering; the §4.3 verifier and restart guard (a separate obligation, though B.5 already forbids Confirmed with RestartRequired); any SQL or schema migration.

## B.9 Open questions

| # | Question | Who |
|---|---|---|
| B-Q1 | Inject the grammar (chosen: one implementation, fail closed when absent), or keep an Application-local equivalent grammar checked by the differential test? O9 permits both. | Reviewer |
| B-Q2 | Should Unconfirmed have neutral history text, or none (chosen)? | Reviewer |
| B-Q3 | Wording of the per-reason refusal texts in B.6. | Reviewer (UX) |
| B-Q4 | Write v2 for NotObserved (chosen: no byte change to existing paths), or always write v3? | Reviewer |

Outside scope, for the reviewer's attention: `FreshUpdateDispatchEvidence.Matches` does not compare `EvidenceId` with the operation. It is noted here only so the adapter review can confirm that this is intended for the UserCacheIntent route, where the helper compares fingerprints.

No owner question arises in either part beyond the existing O5 contingency and the standing rule that any redeploy needs exact owner approval.
