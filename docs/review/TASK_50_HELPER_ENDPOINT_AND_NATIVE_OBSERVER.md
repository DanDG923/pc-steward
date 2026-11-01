# Task 50 — protocol-2 helper endpoint and native Profile V2 observer (design, revision 1)

Requirements: `FR-UPD-15`, `FR-UPD-01/08/09/14`, `AC-UPD-01–11`, `TM-03/04/07/13`.

Status: **proposed, for independent review before any implementation.** This record authorizes no host action, UAC, deployment, provider query or route admission.

Contracts implemented by this design:
- [Profile V2](TASK_50_CHOCOLATEY_PROFILE_V2.md) and its [Task 90 corrections](TASK_90_TASK_50_CHOCOLATEY_PROFILE_V2_REMEDIATION.md), including M1–M3, L1–L4 and the accepted fixed-feed runtime-limit contract;
- [protocol-2 fact extension rev 3](TASK_50_PROTOCOL2_REVIEWED_FACT_EXTENSION.md) §3.1 stages A–D, §3.2, the internal-fault table, and §10 N1–N4;
- [adapter gates](TASK_50_ADAPTER_GATES.md) A.10 G1 (helper-side capability check);
- the desktop side already built: the [composition record](TASK_50_REVIEWED_DISPATCH_COMPOSITION.md), the protocol-2 transport and the dispatcher.

Existing passive components reused without change:
- `ChocolateyConfigurationParser`, `ChocolateySourceResolution`, `ChocolateyCommonProfileFingerprint`;
- `ChocolateyTreeCoveragePolicy`, `ChocolateyPackageArchiveIdentityReader`/`ChocolateyNuspecIdentityReader`;
- `ChocolateyNativeVersion`, `ChocolateyCompiledEnvironment`, `ChocolateyClientReleasePolicy`, `ChocolateyReviewedUpdateArguments`, `ChocolateyPostUpdateClientCheck`;
- `ReviewedUpdateWireCodec`, `DeploymentCapabilityPolicy`, `DeploymentFileAccess`.

## 0. Why a design pass first

Profile V2 requires that "exact native file-handle/access-check port signatures must be fixed in code review before admitting callers". It also requires that "a public record, parsed request, SQLite flag or a pure-policy positive cannot construct" the helper's native binding. This record fixes those ports, the observer's evaluation order, and the helper stage machine. Sections 9 and 10 list what stays open: O5, A-Q1 and A2-L3 host evidence, and owner-gated redeploy.

## 1. Slices and order

| Slice | Content | Gate before the next slice |
|---|---|---|
| **N1** Native primitives | Requester-token access evaluator, protected node reader, bounded tree walker, known-folder and token-profile resolution (§2) | Windows CI with fixture ACLs (standard-user runner) |
| **N2** Profile observer | Static profile observation (§3), all reads through N1, producing `ChocolateyProfileObservation` (private construction) and the common fingerprint | Synthetic fixture trees: positives, plus one negative per rule |
| **N3** Client queries | Retained root-engine binding (§4), fixed `list`/`outdated` under the compiled environment, strict parsing | Fake-process golden/mutation tests; no real client in CI |
| **N4** Helper endpoint | `--protocol 2` bootstrap, stages A–D, launch, response (§5), G1 (§6) | Fixture endpoint against the desktop transport (§8) |
| **N5** Desktop producers | F′ evidence source, review-intent producer, file-first postcheck verifier (§7) | O5 evidence before admission |
| **N6** UI/history | Review disclosure, helper-check and history rendering | Fresh integrated review |
| Admission | `HasSupportedMode` admits the route; composition roots construct the dispatcher | Owner: host evidence (O5/A-Q1/A2-L3) and the separately approved v2 redeploy |

Each slice gets a fresh-context code review and Windows CI before the next starts.

## 2. N1 native primitives (Provider.Chocolatey, `internal`, Windows-only)

These live in `PCSteward.Provider.Chocolatey/Native/`. Provider.Chocolatey cannot reference Execution, and these are provider-observation primitives. They are already in the deployment binding set (`PCSteward.Provider.Chocolatey.dll`), so the binding set does not change.

### 2.1 Requester token

```csharp
internal sealed class RequesterToken : IDisposable
{
    // Helper: the verified desktop peer's primary token, duplicated for identification-level access checks.
    internal static RequesterToken? TryFromVerifiedPeer(int desktopProcessId, string expectedSid);
    // Desktop: the current process token (the desktop is the requester).
    internal static RequesterToken? TryFromCurrentProcess();
    internal string Sid { get; }
}
```

How `TryFromVerifiedPeer` works:
- It opens the desktop process with `PROCESS_QUERY_LIMITED_INFORMATION` only, then `OpenProcessToken(TOKEN_QUERY | TOKEN_DUPLICATE)`, then `DuplicateTokenEx(TOKEN_QUERY, SecurityIdentification, TokenImpersonation)`.
- It requires the token's user SID to equal `expectedSid`, which is the peer SID already verified by `WindowsPeerIdentityVerifier`.
- It requires the token to be **not elevated** (`TokenElevationType != Full`) and its integrity level to be **at most Medium**. A failure returns null, which refuses as `RequestNotAdmitted`.

Invariants:
- The token is never used to impersonate for I/O, never used to start a process, and never persisted.
- It is used only as the client token for `AccessCheck`.
- The desktop process ID comes from the protocol-1-style bootstrap plus `GetNamedPipeServerProcessId`, after `VerifyPeer`. It never comes from the payload.

### 2.2 Access evaluation (no probing opens)

```csharp
internal enum RequesterModification { Denied, Granted, Unreadable }
internal static class RequesterAccess
{
    internal static RequesterModification Evaluate(RequesterToken token, SafeFileHandle node, bool directory,
        SafeFileHandle? parentDirectory);
}
```

How `Evaluate` works:
- It reads `OWNER | GROUP | DACL | LABEL` security information from the **held handle** (`GetSecurityInfo`; READ_CONTROL), not by path.
- It calls `AccessCheck(sd, token, MAXIMUM_ALLOWED, FILE_GENERIC_MAPPING)`, which accounts for owner-implicit rights, OWNER RIGHTS ACEs and the mandatory integrity label.
- Granted means the mask intersects:
  - for files: `FILE_WRITE_DATA | FILE_APPEND_DATA | FILE_WRITE_EA | FILE_WRITE_ATTRIBUTES | DELETE | WRITE_DAC | WRITE_OWNER`;
  - for directories, additionally: `FILE_ADD_FILE | FILE_ADD_SUBDIRECTORY | FILE_DELETE_CHILD`;
  - or the **parent's** check grants `FILE_DELETE_CHILD`, which allows deletion or replacement of the node.
- Any API failure gives Unreadable, which refuses.

Rationale:
- The existing protocol-1 `TryProbeRights` probes with the **calling** process token. In the elevated helper that is the wrong principal (Profile V2: "checking the elevated helper's own access or merely the supplied SID is insufficient").
- `AccessCheck` with the requester's identification token is side-effect free. It needs no write-capable open, and it evaluates exactly the token the requester holds.
- Ownership:
  - Ownership is not a separate rule, except where Profile V2/L1 requires "not caller-owned" (the helper `ChocolateyHttpCache`). There, the owner SID must differ from the requester SID and from every enabled group SID in the token with the `SE_GROUP_OWNER` attribute.
  - Owner-implied `WRITE_DAC`/`READ_CONTROL` is already covered by `AccessCheck`.

### 2.3 Protected node reader and bounded walk

```csharp
internal readonly record struct NativeNode(string FullPath, string RelativePath, bool Directory, WindowsFileIdentity Identity, long Length);
internal static class ProtectedNodeReader
{
    // Opens by path with FILE_FLAG_OPEN_REPARSE_POINT (and BACKUP_SEMANTICS for directories), share-read only; refuses a
    // reparse attribute, a final path different from the requested canonical path, or a zero file ID.
    internal static SafeFileHandle? TryOpen(string canonicalPath, bool directory, out NativeNode node);
    internal static bool TryReadBounded(SafeFileHandle file, int maximum, out byte[] bytes);        // EOF at observed length
    internal static bool TryHashBounded(SafeFileHandle file, long maximum, out DeploymentObservedFile observed);
}
internal static class ProtectedTreeWalker
{
    // Fixed-depth, non-recursive-by-API enumeration (FileIdBothDirectoryInfo on the held directory handle); each entry is
    // opened through ProtectedNodeReader and evaluated with RequesterAccess before descending. Bounds per Profile V2.
    internal static TreeWalkResult Walk(RequesterToken token, string root, TreeWalkBounds bounds,
        Func<string, bool, ChocolateyTreeContentPolicy> classify, CancellationToken cancellationToken);
}
```

`TreeWalkResult` is closed:
- `Complete(IReadOnlyList<NativeNode>)`;
- `Refused(reason token)` for reparse, requester-modifiable, unreadable, over-bound, case-alias duplicate, control character or cancelled.

Classification:
- Uses `ChocolateyTreeCoveragePolicy`.
- `NonLoadingLogContents` is exempt **only** from the requester-modification rule on regular files under `logs`. Their directories stay Protected.
- `MayBeAbsent` applies only to the five optional trees, and only when the parent directory evaluates Denied for `FILE_ADD_SUBDIRECTORY` (M3).

TOCTOU argument:
- Path-based opens with final-path verification are sound here **because** every ancestor and node is first proven requester-unmodifiable. A same-user attacker therefore cannot swap a component between check and use.
- Administrator changes are outside the same-user threat model (Profile V2).
- Stage D repeats the walk immediately before launch.

### 2.4 Native path resolution

```csharp
internal sealed record ChocolateyNativeRoots(string ProgramData, string Root, string HttpCache, string WindowsRoot,
    string ProgramFiles, string ProgramFilesX86);
internal static class ChocolateyNativePathResolver
{
    internal static ChocolateyNativeRoots? TryResolveMachine();                     // SHGetKnownFolderPath, no environment
    internal static ChocolateyLaunchPaths? TryResolveLaunchPaths(RequesterToken token, ChocolateyNativeRoots roots, string protectedCache);
}
```

- `Root = <ProgramData>\chocolatey` and `HttpCache = <ProgramData>\ChocolateyHttpCache`. These are fixed concatenations, as in the native client.
- Requester profile, RoamingAppData and LocalAppData come from `SHGetKnownFolderPath(..., requesterToken)`, never from the environment.
- An architecture test forbids `Environment.GetEnvironmentVariable`, `ExpandEnvironmentVariables`, `Registry` and SQLite in `Native/`.

## 3. N2 static profile observation

```csharp
internal sealed class ChocolateyProfileObservation   // private constructor; produced only by ChocolateyProfileObserver
{
    internal ChocolateyCommonProfileData Common { get; }            // feeds ChocolateyCommonProfileFingerprint.Compute
    internal string Fingerprint { get; }
    internal ChocolateyClientStateData ClientState { get; }         // M2 file-first baseline
    internal ChocolateyHeldClientBinding? HeldClient { get; }       // helper only (§4)
    internal ChocolateyNativeVersion InstalledVersion { get; }
}
internal enum ChocolateyProfileRefusal { MachineInputsUnsupported, SourceNotCommunity, InstalledMismatch, ProfileMismatch }
internal static class ChocolateyProfileObserver
{
    internal static ChocolateyProfileResult Observe(RequesterToken token, ChocolateyObserverRole role, string targetId,
        ChocolateyNativeVersion frozenInstalled, string? expectedFingerprint, CancellationToken cancellationToken);
}
```

`ChocolateyProfileResult` is closed:
- `Observed(observation)`;
- `Refused(ChocolateyProfileRefusal, string? observedInstalled, string? observedFingerprint)`, carrying the values "as far as observed" that rev 3 fields 16 and 18 need.

`role ∈ {Desktop, Helper}`. Only `Helper` performs the §3 step 4 HTTP-cache check and returns `HeldClient`.

Evaluation order. The first failure returns, and reasons follow rev 3 §3.1 precedence:

1. **Roots.** `TryResolveMachine`. Then the root and its required coverage (M3) through `ProtectedTreeWalker`: root and direct files, `bin`, `lib`, `.chocolatey`, `config`, `helpers`, `tools`; optional `hooks`, `extensions`, `license`, `lib-bkp` and `lib-bad`; and `logs` with only its regular files exempt. → `MachineInputsUnsupported`.
2. **Release tree.** Every entry of the **compiled** 56-file manifest (§3.1) is present with matching length and SHA-256. No extra file appears under `helpers`/`tools`. The root engine `choco.exe` passes `MachineTrustedChocolatey`'s signer and version checks. → `MachineInputsUnsupported`.
3. **Code identities.** Ordered `ChocolateyCodeFileData` for `hooks`/`extensions` code. The existing bounds apply (512 MiB, 64 MiB per file, 1 MiB of path bytes). → `MachineInputsUnsupported`.
4. **Helper HTTP cache (Helper only; L1).** `ChocolateyHttpCache` exists, its walk is complete, it is requester-unmodifiable and not requester-owned, and it is reparse-free and readable. This happens **before** any elevated client command. Absent → `MachineInputsUnsupported`. PC Steward never creates or repairs it.
5. **Loader and licensing.**
   - Both Framework and Framework64 `machine.config` files are parsed with bounds. Refuse app-domain managers, `codeBase`/`privatePath`, `developmentMode`, and any `dependentAssembly`/redirect naming `chocolatey.licensed`.
   - `chocolatey.licensed` and `policy.*.chocolatey.licensed` must be absent from `Microsoft.NET\assembly\GAC_MSIL|GAC_32|GAC_64` and the legacy `assembly\GAC*`.
   - `chocolatey.licensed.dll` must be absent from the root, culture subfolders and anywhere under `extensions`.
   - `choco.exe.config` must not exist.
   - → `MachineInputsUnsupported`.
6. **Configuration.**
   - `config\chocolatey.config` is read through the held handle at 64 KiB maximum and passed to `ChocolateyConfigurationParser.TryRead`.
   - The required effective features from the Profile V2 table must hold.
   - `cacheLocation` must be nonempty, absolute, existing, requester-unmodifiable and reparse-free, and its directory identity is captured.
   - → `MachineInputsUnsupported`.
7. **Source.** `ChocolateySourceResolution.TryResolve` must keep the compiled Community URL byte-equal. → `SourceNotCommunity`. Values: installed is null, and the fingerprint is null (S8).
8. **Library and store.**
   - The pinned V2 discovery set (lib-root `.nupkg` files and the top-level `.nupkg` of each immediate directory) is enumerated with Profile V2 bounds.
   - Exactly one target identity is read through `ChocolateyPackageArchiveIdentityReader`.
   - The four store-location alternatives are computed, and at most one may exist.
   - `.version`/`.sxs`/`.pin`, pending markers and `lib-bkp` prefix entries are refused.
   - `lib-bad` residue is recorded for disclosure.
   - The target must not be a client target (`ChocolateyClientReleasePolicy.IsClientTarget`).
   - Installed version ≠ frozen → `InstalledMismatch` with the observed value.
9. **Fingerprint.** `Compute(Common)`. If `expectedFingerprint` is supplied (helper stages B and D), it is compared in fixed time; a difference → `ProfileMismatch` with the observed fingerprint.

`ClientState` is computed from the same reads:
- the engine SHA-256;
- the code-list digest;
- the `chocolatey` and `chocolatey.extension` registration digests, or their known absence.

### 3.1 Compiled release manifest

`ChocolateyReleaseManifest2_7_5` is a generated C# table of the 56 `(path, length, sha256)` entries plus the asset digest. A portable test parses `docs/review/TASK_50_CHOCOLATEY_RELEASE_2_7_5.json` and requires exact equality with the table, so an edit to either is caught. At runtime the JSON is never read: Profile V2 says to "compile/embed … not accept a manifest supplied by SQLite/payload".

## 4. N3 retained root-engine binding and client queries

```csharp
internal sealed class ChocolateyHeldClientBinding : IDisposable  // private constructor; produced by the observer (Helper role)
{
    internal string EnginePath { get; }
    internal ChocolateyLaunchPaths LaunchPaths { get; }
}
internal static class ChocolateyClientQueries
{
    internal static Task<ChocolateyQueryResult<ChocolateyInstalledRecord>> ListExactAsync(ChocolateyHeldClientBinding client, string id, ...);
    internal static Task<ChocolateyQueryResult<ChocolateyOutdatedRecord>> OutdatedAsync(ChocolateyHeldClientBinding client, ...);
    internal static Task<ProviderProcessResult> UpgradeAsync(ChocolateyHeldClientBinding client, ChocolateyReviewedUpdateArguments arguments, ...);
}
```

The binding:
- holds `FileShare.Read`-only handles on the root engine, every release-tree file and every code file, from stage B through invocation and exit;
- like `HeldHelperBinding`, it depends on **A-Q1** evidence that the loader can map held images;
- never holds `lib` or registration files (Profile V2: "do not hold writable package-registration files open").

The queries:
- Arguments are compiled. `list <ID> --exact --limit-output --no-color` and `outdated --source <compiled URL> --ignore-unfound --limit-output --no-color --execution-timeout 60` are the existing builders. Upgrade uses only `ChocolateyReviewedUpdateArguments.Build(operation)`.
- `ProcessStartInfo` is built from `EnginePath` plus `ChocolateyCompiledEnvironment.ApplyTo(start, LaunchPaths)`, with `UseShellExecute=false`, the working directory set to the root, and no inherited environment.
- Output capture is bounded at 1,000,000 characters and 1,024 records, as in Profile V2.
- Each query runs under the existing item budgets. A query process belongs to the item's aggregate root and is tracked to settlement. An unsettled query yields `Undetermined`/`¬aggregateSettlementConfirmed`.

Result mapping (rev 3 §3.1 stage C with N1 and N3):

| Query observation | Reported state |
|---|---|
| A settled non-zero exit, a malformed, duplicate or incomplete record, or an output overflow | `Refused/PreflightQueryFailed`, candidate null; `outputLimitExceeded` when overflowed |
| A well-formed `list --exact` installed value ≠ the stage-B value | `Refused/InstalledMismatch` with the stage-C value (N3) |
| No unique unpinned stable strictly-newer candidate, or a candidate ≠ frozen | `Refused/CandidateMismatch`. Field 16 is the stage-C list value and field 18 the stage-B fingerprint (N1). |
| A timeout, cancellation or unsettled root | `Undetermined` with the matching flag, or `¬aggregateSettlementConfirmed` |

## 5. N4 helper endpoint (Execution)

### 5.1 Bootstrap and authentication

`ElevatedHelperEntryPoint.RunAsync` dispatches on `--protocol`:
- `1` takes the unchanged protocol-1 path;
- `2` takes the new `ReviewedHelperEndpoint.RunAsync`;
- anything else returns exit code 20.

`ReviewedHelperBootstrap.TryParse` accepts exactly:
- `--protocol 2`;
- `--desktop-pid <n>`;
- `--pipe <pcsteward-reviewed-[a-f0-9]{32}>`;
- `--nonce <64 hex>`.

Both bootstraps check that the process is elevated, connect with `TokenImpersonationLevel.Identification`, run `TryGetServerProcessId == desktop-pid`, then `VerifyPeer` against the fixed desktop path, same session and same SID. These are shared with protocol 1, not duplicated.

### 5.2 Stage machine (rev 3 §3.1; G1; N1–N4)

| Stage | Work | Report |
|---|---|---|
| A0 | Read one frame (30 s); `ReviewedUpdateWireCodec.TryReadRequest`; hash of the received payload == digest; `ReviewedUpdateOperation.TryRestore`; nonce == bootstrap nonce (fixed time) | Any failure: **no response** (§6 rev 3) |
| A1 | Freshness at most 2 min and within the 30-min review ceiling; route == `ChocolateyMachineRuntimeLimitsV3` with exact shape; payload SID == verified peer SID; `TryGetProfileDigest` | `Refused/RequestNotAdmitted`, fields 16–18 null |
| A2 (G1) | `DeploymentCapabilityPolicy.Evaluate` over the helper's own protected reads (the same fixed layout as the desktop reader, from `AppContext.BaseDirectory`, which is the protected install root); SHA-256 of the helper's own image and of the peer desktop image (path from the verified peer) == the ledger entries | `Refused/RequestNotAdmitted` |
| A3 | `RequesterToken.TryFromVerifiedPeer` (non-elevated, ≤ Medium, SID equal) | `Refused/RequestNotAdmitted` |
| B | `ChocolateyProfileObserver.Observe(token, Helper, id, frozenInstalled, F)`, where F = the frozen operation's profile digest | Its refusal reason and values (S6–S9) |
| C | `ListExactAsync`, then `OutdatedAsync` (§4 table) | §4 table |
| D | Recheck cancellation, the 2-min and 30-min limits, and the **desktop's remaining budget** (N4: 11 min from request read, minus elapsed). Then repeat stage B in full with the same F. | Lapse → `Refused/ApprovalExpired`. Budget lapse → `Undetermined`+`timedOut` (N4). Drift → the stage-B reason with **stage-D** values; the candidate is kept unless installed is null (S7). |
| Launch | `UpgradeAsync` with the retained binding and `ChocolateyReviewedUpdateArguments.Build(operation)`, exactly once | A definitive pre-creation failure → `Matched ∧ NotStarted`. Cancellation or timeout after D and before launch → `Matched ∧ NotStarted` with the flag. Ambiguous creation → `Unknown`. Otherwise `Started` with the exit code and flags. |

Internal faults (N2):
- A fault before process creation, with all owned query work settled, gives `Refused/MachineInputsUnsupported`.
- If settlement cannot be proven, it gives `Undetermined` + `¬aggregateSettlementConfirmed`.
- Any interrupt flag forces `Undetermined`.
- A fault at or after entering process creation follows the Launch row (Unknown).

The response:
- is built **only** through `ReviewedUpdateWireCodec.CreateResponse`, which throws on any S1–S13 violation. The endpoint catches that throw and sends **no response**, so the desktop records Uncertain;
- has the candidate nulled before construction whenever installed is null (S7);
- is written exactly once, after which the endpoint exits.

The disconnect monitor (as in protocol 1) cancels provider work when the desktop closes the pipe.

### 5.3 What the endpoint never does

- It never reads SQLite, the registry or environment variables as inputs.
- It never retries, never runs a second upgrade, and never touches protocol 1 for a protocol-2 request.
- It never creates, hardens or repairs Chocolatey directories, caches or ACLs.
- It never runs the client for verification after an upgrade.
- It never reports `Success`/`Final`/`HelperQualified`.

## 6. G1 detail

The helper evaluates the **same pure** `DeploymentCapabilityPolicy` as the desktop reader, over inputs it reads itself:
- ledger, manifest and capability file;
- binding-set hashes from its install directory under the same protection checks.

This needs a helper-side variant of `OwnerOnlyDeploymentCapabilityReader` whose `ProcessPath` rule expects the helper apphost rather than the desktop. That is a constructor parameter (`ExpectedProcessLeaf`) with two compiled values only.

The peer desktop image hash is computed from the path returned for the verified peer PID. Both image hashes must equal their ledger entries. A failure gives `Refused/RequestNotAdmitted`. This is settled and NotInvoked, and the desktop records it as `Refused`.

## 7. N5 desktop producers (for completeness; admission waits for O5)

- **F′** `ChocolateyProfileEvidenceSource : IReviewedUpdateDispatchEvidenceSource`. It runs `Observe(TryFromCurrentProcess(), Desktop, id, frozenInstalled, expectedFingerprint: null)` and returns `FreshUpdateDispatchEvidence` with `EvidenceId = "choco-profile-v2:" + Fingerprint` from the **fresh** observation, never from the frozen operation (A2-L6; an architecture test already exists). A refusal returns null, so nothing is dispatched.
- **Review intent** `ObserveReviewIntentAsync`. The same observation, plus the non-elevated client's `outdated` intent, captured as `UserCacheIntent` with the required disclosure text.
- **Verifier** `ChocolateyReviewedUpdateVerifier : IReviewedUpdateVerifier`. `ChocolateyPostUpdateClientCheck.RunAsync` runs with a file-only capture first, through the Desktop-role reads, against `ClientState` from the F′ observation. Only `Unchanged` proceeds to the residue-aware registration re-observation, which requires the unique exact target. `ClientChanged`/`Unverifiable` gives `Unverified`, never Success. A restart-pending result is rejected (L5).
- **O5** is exactly whether the desktop role can complete §3 steps 1–3 and 5–9 without elevation. If it cannot, every update on this route ends `Unverifiable`, and the owner contingency in rev 3 §9 applies.

## 8. Tests per slice (summary)

- **N1 (Windows CI, standard user):**
  - fixture directories with explicit DACLs: a requester-writable file, a parent with `FILE_DELETE_CHILD` for the requester, a requester-owned file, an OWNER RIGHTS restriction, and a Low/Medium integrity label → each gives the expected verdict;
  - `AccessCheck` on an elevated helper-like token is not exercised; CI tests the current token as requester;
  - reparse (junction) refusal, final-path mismatch refusal, bounds, cancellation;
  - architecture: no environment variables, registry or SQLite; no write-capable opens.
- **N2:** synthetic `ProgramData` trees under a GUID temp root, injected through a resolver seam that is test-only (production uses `TryResolveMachine`). One positive, plus one negative for each step 1–9 rule. The compiled manifest equals the JSON. The fingerprint equals the existing goldens for the golden snapshot.
- **N3:** fake process with golden argument lists. Every stage-C table row, including N1/N3. Output-overflow and record-count bounds. Settlement tracking.
- **N4:**
  - an in-process endpoint over a real pipe pair against `ReviewedHelperExchangeSession` (portable), plus a fake observer and fake queries;
  - every §5.2 row, including the A0 no-response cases, G1 refusal, N2 fault rows, S7 nulling and `CreateResponse` throwing → no response;
  - architecture: the only `CreateResponse` call site is the endpoint; the endpoint never references `ElevatedHelperProtocol.CreateResponse`.
- **N5:** fake observer outcomes → F′ null or evidence. Verifier ordering via the existing `ChocolateyPostUpdateClientCheck` tests plus composition.

No CI test runs the real Chocolatey client, reads the runner's ProgramData Chocolatey, or elevates.

## 9. Native evidence still required (owner host; not CI)

- **O5:** desktop-role readability (§7).
- **A-Q1:** loader compatibility of `FileShare.Read` holds, for both `HeldHelperBinding` and `ChocolateyHeldClientBinding`. The A2-L5 fallback applies only to the helper binding. The client binding has **no** fallback; without A-Q1 the route stays unavailable.
- **A2-L3:** write probes under holds return ACCESS_DENIED.
- An exact-artifact run of the signed helper (ADR-025 follow-up).

The [host-evidence plan](TASK_50_HOST_EVIDENCE_PLAN.md) covers O5, A-Q1 and A2-L3 and is awaiting owner approval.

## 10. Open questions for the reviewer

1. Is `AccessCheck` with a duplicated identification token from the verified peer an acceptable "authenticated unelevated requester token" source? The alternative, impersonating the pipe client, gives the helper's own elevated identity, not the requester's.
2. Is path-plus-final-path verification (§2.3) sufficient under the same-user threat model, or must children be opened relative to the parent handle (`NtCreateFile` with `RootDirectory`)?
3. G1 helper reader variant (§6): is a constructor parameter acceptable, or should the helper reuse the desktop reader's exact class with a second compiled layout?
4. Slice order: should N4 land with fake observers before N1–N3, to exercise the stage machine early? The risk is an endpoint that exists before its native producer. It would be unreachable, because the route is not admitted and the helper is not redeployed.
