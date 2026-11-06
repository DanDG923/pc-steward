# Task 50 — protocol-2 adapter gates: manifest capability seam (L-4) and helper-check history (§4.2), revision 2

Requirements: `FR-UPD-15`, `FR-UPD-01`, `FR-HIS-01`, `AC-UPD-01–11`, `TM-03/04/07/13`; retained `FR-UPD-08/09/14`, `TM-06`.

Status: **independently reviewed specification — PASS at revision 2** (`e801692`, SHA-256 `cca2d3eb…7b7f`; revision 1 at `c95fc34` was CONDITIONAL). Low findings are binding implementation obligations (§R). This is a design review result inside owner-accepted ADR-025. Still owner-gated: acceptance of the G2 ADR-022 alignment-note wording when the implementation lands, separate exact approval for any v2 redeploy, and the O5 contingency. It adds no code and grants no deployment, host-read, UAC or update authority.

Sources read: AGENTS.md; fact extension rev 3 (all sections); dispatch composition and its Task 90 remediation; ADR-025 (protocol boundary and the ADR-022 row of the amendment map); ADR-022 including the Task 48 pinned-payload amendment; handover §3–§4; Profile V2; V2 task contract (validation and migration rule). Code read at `aad760e`: `ReviewedUpdateDispatchWorkflow.cs`, `UpdateDispatchAdmission.cs`, `ReviewedUpdateHistory.cs`, `SqliteReviewedUpdateHistory.cs`, `SqliteUpdateDispatches.cs`, `SqliteReviewedOperations.cs`, `ReviewedUpdateOperation.cs`, `ReviewedUpdateProtocol2Interpreter.cs`, `ReviewedUpdateWireCodec.cs`, `WindowsElevatedHelperTransport.cs` (`InstalledHelperLaunchTrust`, `ProtectedLaunchLayout`), `ChocolateyVersionGrammar.cs`, `ChocolateyNativeVersion.cs`, `scripts/Install-`/`Test-OwnerOnlyProtectedTestDeployment.ps1`, `scripts/OwnerOnlyPayloadIntegrity.ps1`, and the architecture tests.

## 0. Changes from revision 1

| Finding | Edit |
|---|---|
| A-M1 — peer path check adds nothing | §A.4 step 3.4. `VerifyPeer` already compares the peer image path string (`WindowsElevatedHelperTransport.cs:851`). The new check compares **file identity** instead: the volume serial number plus the 128-bit file ID, read with `GetFileInformationByHandleEx(FileIdInfo)`, for the peer's main-module image against the held `HeldHelperBinding` helper handle. Windows behaviour of a `FileShare.Read` handle during image mapping is marked **Windows evidence required**. The ACL plus re-hash fallback is kept (A-Q1). |
| A-M2 — hold lifetime | §A.4 step 3.5. The binding, including lazily loaded closure files such as `Provider.Chocolatey.dll`, is held through helper exit **and** transport seal, not only until peer verification. |
| A-L1 — redeploy script and v1 host | §A.3 adds `Invoke-OwnerOnlyApprovedRedeployment.ps1` to the script table and the hard-coded `pinned-unsigned-v1` pin in `Install-…ps1`. New §A.3.1 states the consequence: the current host is `pinned-unsigned-v1`, and the existing exact plan cannot deploy v2. Protocol-2 therefore stays `Absent` until a separately approved new exact redeploy plan exists. |
| A-L2 — rollback fixtures | §A.7 adds two fixtures. The old `Test-` script on a v2 ledger throws "Unrecognised pinned-payload provenance" (fail closed). An old desktop, which has no seam, is unaffected by a v2 ledger. |
| A-L3 — helper-side check | §A.4 step 4 is now recorded in §A.10 as integrating obligation **G1**, which extends rev 3 §3.1 stage A. It is not a silent change to rev 3. |
| A-Q2 / A-Q3 / A-Q5 resolved | Runtime files stay out of the binding set: the ACL plus the ADR-022 validator suffice. `Supports` stays synchronous; the Desktop must call `RunAsync` off the UI thread (G3). Bounds are tightened to 32 MiB per file and 64 MiB per set. An ADR-022 alignment note is required, and the owner accepts its wording (G2). |
| B-M1 — grammar injection | §B.2 and §B.7. Production must inject `IReviewedHelperVersionGrammar`; otherwise every v3 row reads as an integrity failure. A composition test requires Desktop's `SqliteDatabase` to be built with the grammar. The constructor without a grammar is test-only. B-Q1 is resolved: one injected grammar. |
| B-L1 — no operation | §B.5. `HelperCheck != NotObserved` with a null or unrestored operation makes the row unreadable. |
| B-L2 — store rollback | §B.7 adds a fixture: an older build's `GetDispatchStateAsync` and `RecoverUnfinishedDispatchesAsync` check only the digest, so they still succeed on a database with v3 rows. Barriers and reconciliation survive rollback. |
| B-L3 — truncation, `TryRestore`, wording tests | §B.2: "helper-check-not-recorded" is appended **before** the 4,000-character truncation. §B.7: an architecture assertion that `RecordedHelperCandidateCheck.TryRestore(` is referenced only in `ReviewedUpdateHistory.cs`, and wording tests match whole words. B-Q2 is resolved (Unconfirmed stays `null`) and B-Q3 is resolved (texts accepted). |
| Reviewer recommendation — fresh `EvidenceId` | New §A.6.1: a route-specific rule. For `ChocolateyMachineRuntimeLimitsV3`, the fresh dispatch evidence must carry the same profile digest as the frozen `EvidenceId`, checked before the claim and failing closed. Other routes keep their own fresh `EvidenceId` (for example `fresh:1`), and equality is **not** required globally. |
| Authority | ADR-025's accepted ADR-022 row covers the design, so no new owner decision is needed. The implementation task must present the ADR-022 alignment-note wording for owner acceptance (G2). Any v2 redeploy needs separate exact approval. |

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
| `HeldHelperBinding : IDisposable` | Execution | internal sealed | Share-read handles on the binding set, opened with `FileShare.Read` only (no write and no delete share), plus the verified hashes and the helper `.exe`'s `FileIdInfo` (volume serial number and 128-bit file ID). Used only by the protocol-2 transport (A.4 step 3). Disposed only after helper exit and transport seal. |
| `OwnerOnlyDeploymentCapabilityReader : IHelperProtocolCapability` | Execution | internal sealed | Windows producer. Uses fixed paths derived only from `SpecialFolder.ProgramFiles` and `Environment.ProcessPath`. Reuses `InstalledHelperLaunchTrust.ResolveFixedPaths` and the `ProtectedLaunchLayout` no-reparse and caller-cannot-modify primitives. |
| `ChocolateyRuntimeLimitsRouteDispatcher : IReviewedUpdateRouteDispatcher` (future adapter) | Execution | internal | `Supports(op) = TryGetProfileDigest(op, out _) && capability.Evaluate(op.RouteContract).Status == Supported`, wrapped so that **any** exception returns `false`. |
| `IReviewedUpdateRouteDispatcher.Supports` doc | Application | — | Doc text becomes: "compiled route check plus, for helper routes, the adapter's protected deployment-capability seam; never eligibility evidence; must not throw". Application never sees a capability type. |
| `ReviewedUpdateDispatchWorkflow.RunAsync` | Application | — | Small robustness change: the pre-claim `Supports` goes through the same `StillSupported`-style catch. An exception means NotStarted with no claim. The doc comment states that callers must invoke `RunAsync` off the UI thread, because `Supports` performs bounded synchronous I/O (G3). |
| `FreshUpdateDispatchEvidence.Matches` | Application | — | Adds the route-specific profile-digest equality (A.6.1). |
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
| `Install-OwnerOnlyProtectedTestDeployment.ps1` | Write `payloadOrigin = 'pinned-unsigned-v2'` iff the capability file is in the pinned manifest. Otherwise keep `pinned-unsigned-v1`. The post-install check at line 179, which hard-pins `pinned-unsigned-v1`, becomes "the origin implied by the pinned manifest". Ledger schema stays v6, so the removal script is unchanged. |
| `Invoke-OwnerOnlyApprovedRedeployment.ps1` | The postcondition at line 74 hard-pins `payloadOrigin -cne 'pinned-unsigned-v1'`. A v2 redeploy needs a **new embedded exact plan** that names the expected origin. The existing plan is left unchanged, so it can still deploy only v1. |
| `Assert-OwnerOnlyPayloadProvenance` / `Test-OwnerOnlyProtectedTestDeployment.ps1` | Recognise `pinned-unsigned-v2`, which requires a valid capability file. Report `helperProtocol` in the output. |

Existing v6, `legacy-v6` and `pinned-unsigned-v1` deployments gain nothing: no capability means `Absent`.

### A.3.1 Consequence for the current host

The owner's host is `pinned-unsigned-v1`, and the existing exact plan cannot produce v2. **Protocol-2 is therefore `Absent` on that host until a separately approved new exact redeploy plan has run.** This design grants no redeploy, signing or certificate authority.

**Binding set.** A compiled closed list of fixed leaves, each of which must appear exactly once in `ledger.files` with a matching hash:
- `PCSteward.Desktop.exe` (it must equal `Environment.ProcessPath`);
- `PCSteward.ElevatedHelper.exe`, `.dll`, `.deps.json` and `.runtimeconfig.json`;
- `PCSteward.Execution.dll`, `PCSteward.Application.dll`, `PCSteward.Domain.dll`, `PCSteward.Provider.Chocolatey.dll` and `PCSteward.Provider.WinGet.dll`;
- `PCSteward.DeploymentCapability.txt`.

A build test asserts that the list equals the project-assembly closure in the published helper `deps.json`. Runtime files (`hostfxr`, `coreclr` and so on, if the payload is ever self-contained) are **not** in the binding set; the protected ACL and the ADR-022 validator cover them. Size bounds: each file ≤ 32 MiB and the whole set ≤ 64 MiB, otherwise `Unreadable`.

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
4. After connecting, run `VerifyPeer` (path, session, SID and signer, as today), then the new **file-identity check**:
   - open the peer's main-module image from its process path with no write or delete share;
   - read `FileIdInfo` (`VolumeSerialNumber` plus the 128-bit `FileId`) with `GetFileInformationByHandleEx`;
   - require it to equal the `FileIdInfo` of the held helper handle.

   A mismatch, or a failure to read either identity, is treated like a `VerifyPeer` failure: the request is not written, giving `HelperStartedRequestNotWritten` (NotStarted only with H, otherwise Uncertain). The scripts already hold-then-check a path (`AssertHeldPath`, which compares the final path); this check compares the stronger file ID.
5. Keep the binding held, including closure files the helper loads lazily such as `Provider.Chocolatey.dll`, **until the helper exit has been observed and the transport is sealed**. On the no-exit or termination-failure path, keep it until teardown completes. Disposing it earlier would reopen a replacement window for lazily loaded files.

**Windows evidence required (A-Q1):** whether the image loader can map a file while another process holds a `FileShare.Read`-only handle to it (`FILE_EXECUTE` is expected to fall under read sharing), and whether the file ID of the peer's main module is stable as read. If either fails, the fallback is ACL plus `VerifyPeer`, plus re-hashing the binding set and running the file-identity check after peer verification and before writing the request.

A `TryHold` failure is `ReviewedHelperTransportOutcome.LaunchRefusedBeforeElevation`. The interpreter turns it into `NotStarted(Failed)`, a NotInvoked settlement, and the history text "Not updated: nothing was started." There is no UAC prompt.

**Step 4 — helper side (integrating obligation G1, A.10; ADR-025 requires "both endpoints prove matching capability").** In stage A, after correlation, the helper runs `DeploymentCapabilityPolicy.Evaluate` over the same protected files using its own reads. It also checks its own image and the peer desktop image hashes against the ledger. A failure is `Refused/RequestNotAdmitted`, with fields 16–18 null. That is settled, NotInvoked, and recorded as `Refused`. The helper bootstrap accepts only `--protocol ProtocolVersion`, so there is no protocol-1 fallback.

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

### A.6.1 Fresh dispatch evidence for this route (reviewer recommendation)

`FreshUpdateDispatchEvidence.Matches` (`UpdateDispatchAdmission.cs:38`) does not compare `EvidenceId`. That is intended for the other routes, whose fresh evidence carries its own new identity (for example `fresh:1`), and it stays unchanged for them.

For `op.RouteContract == ChocolateyMachineRuntimeLimitsV3`, `Matches` additionally requires:
- `fresh.EvidenceId` has the exact profile form `choco-profile-v2:` + 64 uppercase hex. This uses the Domain helper from A.6.
- Its digest equals `F` from `op.EvidenceId`, compared in fixed time.

**Representation.** No new field: the fresh evidence's existing `EvidenceId` carries `"choco-profile-v2:" + F′`.
- `F′` is `ChocolateyCommonProfileFingerprint.Compute` over the desktop's **fresh** non-elevated observation at dispatch preparation, using the same L3 normal form and the authenticated requester SID.
- `EvidenceId` is already in the evidence's canonical bytes (`pc-steward-dispatch-evidence:2`) and in the `fresh_evidence` blob, so there is no format or schema change.
- The 81-character value satisfies `FreshUpdateDispatchEvidence.Identity` (≤ 256 characters, `:` and `-` allowed).

**Producer rule.** The route's `IReviewedUpdateDispatchEvidenceSource` computes `F′` from fresh observation. It must never copy `op.EvidenceId`. An architecture test checks that the evidence-source file does not read the operation's `EvidenceId`. If `F′` cannot be computed, the source returns `null`.

**Effect.** A mismatch or missing digest makes `Matches` false:
- at `ClaimAsync`, before the claim → `NotStarted` ("Fresh update evidence … no longer matches"), with no claim;
- at the store `TryClaimAsync` → no row;
- at `TryConsume` → `Cancelled`/NotInvoked.

It never qualifies anything: the helper still compares its own fingerprint with `F` (P1/P3/P4).

### A.6.2 Back-compatibility for stored rows

Production cannot hold a runtime-limit schema-3 row, because the route is not admitted and review confirmation refuses Chocolatey. A non-conforming row could exist only through forgery or a test fixture:

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
| Peer identity | The peer image path is equal but the `FileIdInfo` differs (a replaced or hard-linked file) → request not written. `FileIdInfo` cannot be read → request not written. The binding is still held after peer verification and is released only after helper exit and seal. A lazily loaded closure file cannot be replaced while the binding is held (Windows evidence). |
| Fresh evidence (A.6.1) | Runtime-limit route with fresh `EvidenceId` `fresh:1`, a lowercase digest, 63 or 65 digits, or a different `F′` → `Matches` false → NotStarted with no claim. A mismatch introduced between claim and `TryConsume` → `Cancelled`/NotInvoked. A WinGet route with `fresh:1` is still accepted (no global equality). The evidence source never copies `op.EvidenceId` (architecture test). |
| Rollback | The old `Test-OwnerOnlyProtectedTestDeployment.ps1` (v1-only `Assert-OwnerOnlyPayloadProvenance`) on a v2 ledger throws "Unrecognised pinned-payload provenance", so it fails closed. The old `Invoke-…Redeployment.ps1` postcondition rejects a v2 result. An old desktop build (no seam) with a v2 ledger is unaffected, because it never reads the ledger. |
| Regression | Protocol-1 transport tests are unchanged. The capability file's bytes equal the bytes generated from the compiled constants. The binding set equals the helper `deps.json` closure. Script fixtures: v1 manifest → `pinned-unsigned-v1`; v2 manifest → `pinned-unsigned-v2`; a malformed v2 capability makes the install fail before signing. |

## A.8 Non-goals

Route admission (`HasSupportedMode`); the protocol-2 transport itself; any redeploy, signing or certificate action; changing ADR-018 launch trust; WinGet; protocol 1; caching or persisting capability; full-tree re-hashing at dispatch (the ADR-022 validator still owns that).

## A.9 Open questions

| # | Question | Who |
|---|---|---|
| A-Q1 | Whether a `FileShare.Read`-only hold on the binding set survives `ShellExecute runas` and the image loader, and whether the peer main-module file ID is stable. The fallback is stated in A.4 step 3. | **Windows evidence** |
| A-Q2, A-Q3, A-Q4, A-Q5 | Resolved in revision 2; see §0 and G1–G3. | — |

## A.10 Integrating obligations (binding on the adapter slice, like rev 3 N5)

| # | Obligation |
|---|---|
| G1 | **Extension of rev 3 §3.1 stage A.** After correlation, the helper evaluates `DeploymentCapabilityPolicy` over its own protected reads and checks its own image hash and the peer desktop image hash against the ledger. A failure gives `Refused/RequestNotAdmitted` with fields 16–18 null (S6 holds). The integrating record must annotate rev 3 §3.1, without rewriting the pinned artifact, and the §8 tests gain this case. |
| G2 | **ADR-022 alignment note.** The implementation task adds a short note recording `payloadOrigin = pinned-unsigned-v2` and the pinned capability file as the ADR-025 ADR-022-row manifest fact. Its **wording is presented for owner acceptance.** It grants no redeploy authority. A v2 redeploy requires a separately approved new exact plan (A.3.1). |
| G3 | **Desktop threading.** Desktop callers invoke `ReviewedUpdateDispatchWorkflow.RunAsync` off the UI thread, because `Supports` and `StillSupported` perform bounded synchronous I/O (at most 64 MiB). Covered by a Desktop architecture or UI-contract test. |

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
| `SqliteDatabase(string path, IReviewedHelperVersionGrammar helperVersionGrammar)` | Persistence.Sqlite | New overload; the grammar must be non-null. **Production must use this overload.** Without a grammar, every v3 terminal reads as an integrity failure (fail closed). The existing constructor without a grammar is kept **for tests and non-history callers only**. A composition test requires that the Desktop composition root building the history `SqliteDatabase` passes `ChocolateyHelperVersionGrammar`. |

Workflow flow: `FinalizeAsync` gains a `RecordedHelperCandidateCheck` argument.
- The Uncertain, NotInvoked and settled-classification paths pass `observation.HelperCheck ?? NotObserved`.
- The orphan-claim path, the `TryConsume`/`StillSupported` paths and the dispatcher-exception path pass `NotObserved`.
- Before building the terminal, the workflow calls `IsConsistentWith`. If the check is inconsistent, it records `NotObserved` and adds the fixed diagnostic "helper-check-not-recorded" to the message **before** the 4,000-character `Truncate`, as a prefix, so truncation can never drop it. It **never** fails the write over the check, because a failed write would turn the outcome into Uncertain and so change the outcome.
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
- O-none: `Kind != NotObserved` with a null, unrestored or integrity-failed operation ⇒ unreadable.
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
| Fixtures (V2 contract) | **Old:** a v2 row read by the new reader gives NotObserved and identical text. **New:** every allowed (kind, outcome, settlement) round-trips byte-exactly, and the maximal frame (509 B header + 12,288 B diagnostic) round-trips. **Missing:** a v3 header with 7 lines, or a v2 header with 11 lines, is unreadable. **Corrupt:** single-byte tampering, with and without a recomputed digest, is unreadable. **Partial:** a missing length line, a header without its final LF, or non-zero padding is unreadable. **Rollback:** a frozen copy of the v2 parser returns `false` on v3 without throwing; on an older build a Final v3 row is non-blocking and an Uncertain v3 row blocks. **Store rollback:** on a database containing v3 Final, v3 Uncertain and DispatchClaimed rows, the older build's `GetDispatchStateAsync` and `RecoverUnfinishedDispatchesAsync`, which check only the terminal digest, still succeed. Barriers, `HasUnresolvedDispatchesAsync` and reconciliation behave as before. |
| Enumerations | Wrong case, numeric `1`, an unknown kind or reason, a reason ≠ None with Confirmed or Unconfirmed, `None` with Refused, `NotObserved` in v3 → unreadable. |
| Versions | Empty, `None `, `none`, 129 characters, `1.0+meta`, `01.0`, `1.0`, `1.0.0.0`, `1.0.0-`, non-ASCII → unreadable. A differential test runs the codec and the history reader over the shared O9 corpus. No grammar injected → all v3 terminals are unreadable. **Composition:** Desktop's history `SqliteDatabase` is constructed with `ChocolateyHelperVersionGrammar`; a test fails if the no-grammar constructor is used outside tests. |
| Operation | A v3 Confirmed or Refused row whose canonical operation fails restore or digest checks → terminal unreadable (O-none). |
| R and P rules | Confirmed with a null version or a version ≠ frozen. `RequestNotAdmitted` with values. A candidate without an installed version. A candidate with `PreflightQueryFailed`. `ProfileMismatch` with installed null or ≠ frozen. `CandidateMismatch` with candidate = frozen. Another reason with candidate ≠ frozen. `InstalledMismatch` with installed = frozen. Each → the store throws on write and the reader marks the row unreadable. |
| Outcome matrix | Confirmed with Refused, ElevationDenied or RestartRequired. Refused with Success, Failed, Cancelled or OwnedWorkStopped. Unconfirmed with Success, Unverified or Refused. A non-NotObserved check on a WinGet or schema-2 row. Each → the store throws and the reader marks the row unreadable. |
| Workflow | An inconsistent check (test-only factory call) gives NotObserved plus "helper-check-not-recorded", and the outcome and settlement are unchanged. With a 20,000-character diagnostic, the marker survives the 4,000-character truncation. Parameterising the same observation over every kind yields identical outcome and settlement. A diagnostic containing "matched its protected machine checks" yields NotObserved. A dispatcher exception gives NotObserved. |
| Presentation | Exact texts. Forbidden-word assertions match **whole words**, case-insensitive (`\b(updated|success|verified|completed)\b`), so "refused before updating" passes. A Success row with Confirmed has a separate `HelperReport` and Status text unchanged. An integrity failure gives null. Candidate text for each provenance is independent of the helper kind (behavioural test). |
| Architecture | `FromDesktopDerived(` has exactly one call site in `src` (the interpreter file). `RecordedHelperCandidateCheck.TryRestore(` is referenced only in `ReviewedUpdateHistory.cs`. `ReviewedUpdateTerminalRecord` is referenced only by history files. `RecordedHelperCandidateCheck` is not referenced by `ReviewedUpdateReview*`, `UpdateDispatchAdmission` (admission and permit types), dispatch-evidence or reconciliation sources. Reflection finds no method that takes either `RecordedHelperCandidateCheck` or `ReviewedCandidateProvenance` and returns the other. The existing interpreter boundary test is unchanged. |

## B.8 Non-goals

Storing the fingerprint or the diagnostic as a fact; any change to `Status`, settlement or outcome driven by the check; desktop rendering; the §4.3 verifier and restart guard (a separate obligation, though B.5 already forbids Confirmed with RestartRequired); any SQL or schema migration.

## B.9 Open questions

| # | Question | Who |
|---|---|---|
| B-Q1 | Resolved: one injected grammar (B-M1). | — |
| B-Q2 | Resolved: Unconfirmed has `null` history text. | — |
| B-Q3 | Resolved: the B.6 texts are accepted. | — |
| B-Q4 | Write v2 for NotObserved (chosen: no byte change to existing paths), or always write v3? | Reviewer |

The fresh-`EvidenceId` observation from revision 1 is now the route-specific rule in A.6.1.

Owner actions: accept the ADR-022 alignment-note wording (G2) during the implementation task, and give separate exact approval for any v2 redeploy. Beyond that, no owner question arises other than the existing O5 contingency.

## §R. Review record and binding implementation obligations (revision 2 PASS)

| # | Obligation |
|---|---|
| A2-L1 | Specify a closed ledger key set **per writer variant**. The installer also writes `schema`, `certificateSubject`, `ownedCertificateStores` and `createdAtUtc`; pending and rollback ledgers add `pendingCertificate`, `rollbackFailures` and `originalFailure`, and may hold a null thumbprint. Add a fixture test against the actual scripts so that real ledgers are not all rejected and `Incomplete` stays reachable. |
| A2-L2 | A v2 redeploy from a v1 host needs a reviewed redeploy-template or plan-schema change: the template hard-codes the `legacy-v6` preflight (line 60) and the v1 postcondition (line 74). A new embedded plan alone is not enough. Owner approval remains separate. |
| A2-L3 | Windows evidence: the `TryProbeRights` write probes, run while read-share handles are held, must still return ACCESS_DENIED (a sharing violation already fails closed). |
| A2-L4 | Bind `PCSteward.Desktop.dll` as well as the apphost, or reword the "foreign desktop" claim. |
| A2-L5 | The fallback names its reference identity (the FileIdInfo captured before launch) and states that it detects replacement only. |
| A2-L6 | Restate the claim-to-TryConsume mismatch case as "the same result at all three `Matches` sites" (evidence and operation are immutable). F′ must use the Profile V2 observer including its protection checks, not just the same normal form. |
| B2-L1 | Require the version grammar in **every** production composition root (Desktop and ScheduledHost). The no-grammar constructor is test-only. |
| B2-L2 | Guarantee every written v3 row is readable: add a write-side grammar check or a test proving writer and reader agree. |
| Authority | Owner: (a) accept the G2 ADR-022 alignment-note wording, reconciled with ADR-022 Task 48's "no runtime updater capability changes"; (b) separate exact approval for any v2 redeploy; (c) the O5 contingency. A-Q1 and O5 evidence need separately authorized exact-artifact Windows host tests (ADR-025 follow-up controls). |
| Open | A-Q1 remains native evidence required before admission and does not block the spec. B-Q4 is closed: writing v2 for NotObserved is correct. Nit: the transport comparison is at lines 852–853. |
