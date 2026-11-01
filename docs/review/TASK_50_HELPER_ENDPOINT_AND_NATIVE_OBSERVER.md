# Task 50 — protocol-2 helper endpoint and native Profile V2 observer (design, revision 2)

Requirements: `FR-UPD-15`, `FR-UPD-01/08/09/14`, `AC-UPD-01–11`, `TM-03/04/07/13`.

Status: **revision 2, proposed for a fresh independent re-review before any implementation.** Revision 1 (`1024708`, SHA-256 `66cc6398…a46db239`) was reviewed: CONDITIONAL, no Blocker, 4 High, 7 Medium, 6 Low. This revision addresses every finding (§11). This record authorizes no host action, UAC, deployment, provider query or route admission.

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
| **N0** Native launcher | Job-object launcher with suspended creation, explicit handle list, aggregate settlement and the full wire-fact result (§4.1) | Windows CI with fixture child trees |
| **N1** Native primitives | Requester token (linked-token rule, §2.1), access evaluator with node and ancestor modes, protected node reader, bounded tree walker, path resolution (§2) | Windows CI: fixture ACLs and token negatives (§8) |
| **N2** Profile observer | Static profile observation (§3), all reads through N1, producing `ChocolateyProfileObservation` (private construction) and the common fingerprint | Synthetic fixture trees: positives, plus one negative per rule |
| **N3** Client queries | Retained root-engine binding (§4), fixed `list`/`outdated` under the compiled environment, strict parsing | Fake-process golden/mutation tests; no real client in CI |
| **N4a** Stage machine | Pure stage machine over the qualification port (§5.4) with fakes; no entry-point wiring | Fixture endpoint against the desktop transport (§8) |
| **N4b** Endpoint wiring | `--protocol 2` bootstrap and the single native port implementation; until N4b, protocol 2 still exits with code 20 | N0–N3 and N4a complete and reviewed |
| **N5** Desktop producers | F′ evidence source, review-intent producer, file-first postcheck verifier (§7) | O5 evidence before admission |
| **N6** UI/history | Review disclosure, helper-check and history rendering | Fresh integrated review |
| Admission | `HasSupportedMode` admits the route; composition roots construct the dispatcher | Owner: host evidence (O5/A-Q1/A2-L3) and the separately approved v2 redeploy |

Each slice gets a fresh-context code review and Windows CI before the next starts.

## 2. N1 native primitives (Provider.Chocolatey, `internal`, Windows-only)

These live in `PCSteward.Provider.Chocolatey/Native/` (namespace `PCSteward.Provider.Chocolatey.Native`). They are already in the deployment binding set (`PCSteward.Provider.Chocolatey.dll`), so the binding set does not change.

**Layering (M1).** Provider.Chocolatey cannot reference Execution:
- `Native/` defines its own `NativeFileIdentity(ulong VolumeSerial, UInt128 FileId)` and `NativeObservedFile(long Length, string Sha256)`. It does not use Execution's `WindowsFileIdentity`/`DeploymentObservedFile`.
- An architecture test requires the two `FILE_ID_INFO` P/Invoke layouts to be identical.
- Provider.Chocolatey adds `InternalsVisibleTo("PCSteward.Execution")`.
- Execution calls the observer **only** through the qualification port (§5.4) and calls `RequesterToken`/`RequesterAccess` from the helper G1 reader (§6). An architecture test lists every Execution file that references `PCSteward.Provider.Chocolatey.Native`.

### 2.1 Requester token and peer process handle (H2, H3)

```csharp
internal sealed class PeerProcess : IDisposable          // one handle for the whole request (H3)
{
    internal static PeerProcess? TryOpen(int processId);  // PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE
    internal int ProcessId { get; }                       // GetProcessId(handle)
    internal bool HasExited { get; }                      // WaitForSingleObject(handle, 0)
    internal bool TryReadIdentity(out string imagePath, out int session, out string userSid); // all from the handle
}
internal sealed class RequesterToken : IDisposable
{
    // Helper: the helper's own TokenLinkedToken (the filtered Medium token of the same logon), accepted only if it
    // matches the verified desktop peer (below).
    internal static RequesterToken? TryForVerifiedPeer(PeerProcess peer, string expectedSid);
    // Desktop: the current process token, which must satisfy the same token rules.
    internal static RequesterToken? TryFromCurrentProcess();
    internal string Sid { get; }
}
```

**Peer handle (H3).**
- The helper opens the desktop process **once**, immediately after the pipe connects, using `GetNamedPipeServerProcessId`.
- It keeps that handle for the whole request.
- Every identity fact comes from that handle: image path, session, SID, signer, image hash (G1) and token. The handle prevents PID reuse while it is open.
- `GetProcessId(handle)` must equal the pipe server PID.
- After duplicating the token, `HasExited` must be false. Exit at any point before A3 completes gives `RequestNotAdmitted`.
- Protocol 1 keeps its PID-based `VerifyPeer`. The shared verifier gains a handle-based overload that protocol 2 uses, while protocol 1 behaviour is unchanged.

**Evaluation token (H2).** The token passed to `AccessCheck` is the helper's own **linked token** (`GetTokenInformation(TokenLinkedToken)`). That is the filtered token of the same split logon, duplicated to `SecurityIdentification`. It is accepted only when all of the following hold:
- the peer desktop token (from the held handle, `TOKEN_QUERY`) has `TokenElevationType == Limited`;
- its `AuthenticationId` equals the linked token's `AuthenticationId`;
- its user SID equals `expectedSid` and the linked token's SID;
- the evaluation token's integrity level is **exactly Medium**;
- `IsTokenRestricted` is false;
- `TokenIsAppContainer` is false;
- none of `SeBackupPrivilege`, `SeRestorePrivilege`, `SeTakeOwnershipPrivilege`, `SeRelabelPrivilege` or `SeSecurityPrivilege` is present, enabled or not.

A Low-integrity, restricted or AppContainer desktop therefore cannot understate the user's rights: the evaluation always uses the full Medium filtered token of that logon. If UAC is off (no linked token), or the elevation type is Default/Full, the request is refused (`RequestNotAdmitted`). Such a host has no unelevated requester token, and the owner-only deployment already requires UAC.

The desktop role applies the same rules to its own token: exactly Medium, not restricted, not AppContainer, none of those privileges, elevation type Limited.

Invariants:
- Tokens are never used to impersonate for I/O, never used to start a process, and never persisted.
- They are used only as `AccessCheck` client tokens and for SID/IL/LUID comparison.
- No input comes from the payload.

### 2.2 Access evaluation (no probing opens)

```csharp
internal enum RequesterModification { Denied, Granted, Unreadable }
internal enum AccessMode { Node, Ancestor }
internal static class RequesterAccess
{
    internal static RequesterModification Evaluate(RequesterToken token, SafeFileHandle node, bool directory,
        SafeFileHandle? parentDirectory, AccessMode mode);
    internal static RequesterModification EvaluateAncestry(RequesterToken token, string canonicalPath); // volume root → parent
    internal static bool IsRequesterOwned(RequesterToken token, SafeFileHandle node);                // L4
}
```

**Two modes (M2).**
- **Node** uses the full mask below. It applies to every protected node.
- **Ancestor** uses `DELETE | WRITE_DAC | WRITE_OWNER`, plus the grandparent's `FILE_DELETE_CHILD`, plus no reparse attribute. It applies to every directory from the volume root down to the parent of a protected root. Standard users may create subfolders in `C:\ProgramData` (which Ancestor mode tolerates), but must not be able to delete, rename, replace or re-ACL an ancestor.
- `EvaluateAncestry` runs top-down **before** any node of that root is opened. The roots it covers:
  - the Chocolatey root;
  - `ChocolateyHttpCache`;
  - `cacheLocation`;
  - both `machine.config` files and their `Config` directories;
  - every GAC root;
  - the PC Steward install root (G1).

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
- **Ownership (L4).**
  - `IsRequesterOwned` is true when the owner SID equals the token's user SID, its logon SID, or any **enabled** group SID of the token.
  - It applies to **every node** of `ChocolateyHttpCache` (Profile V2/L1: "not caller-owned"). Elsewhere, ownership is covered by `AccessCheck`'s owner-implied rights.

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
- `MayBeAbsent` applies only to the five optional trees and `logs`, and only when the parent directory evaluates Denied for `FILE_ADD_SUBDIRECTORY` (M3).
- **Absence (L2).** A child is absent only if it is missing from the **held parent's enumeration**. A failed open of a listed child is `Refused(unreadable)`, never absence.
- Each opened child's parent identity must equal the held enumerated directory's identity (§10 answer 2).

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
- **(L1)** The requester profile path comes from `GetUserProfileDirectory` on the evaluation token (`TOKEN_QUERY` only). RoamingAppData and LocalAppData come from `SHGetKnownFolderPath(..., hToken: null)` in the helper's own same-SID context. The identification token is never passed to an API that impersonates. Nothing comes from the environment.
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
2. **Release tree.** Every entry of the **compiled** 56-file manifest (§3.1) is present with matching length and SHA-256. No extra file appears under `helpers`/`tools`.
   - **(L3)** The root engine `<root>\choco.exe` is trusted by its manifest hash, plus `WinVerifyTrust` on the **held handle** (`WTD_CHOICE_FILE` with `hFile`), plus the expected signer and file version `2.7.5.0`.
   - `MachineTrustedChocolatey.ResolveExecutable`, which resolves the `bin` shim, is never called.
   - → `MachineInputsUnsupported`.
3. **Code identities.** Ordered `ChocolateyCodeFileData` for **every regular file** under `hooks` and `extensions` (L5). The existing bounds apply (512 MiB, 64 MiB per file, 1 MiB of path bytes). → `MachineInputsUnsupported`.
4. **Helper HTTP cache (Helper only; L1).** Ancestry evaluation first. Then `ChocolateyHttpCache` must exist, its walk must be complete, and every node must be requester-unmodifiable and **not requester-owned** (L4), reparse-free and readable. This happens **before** any elevated client command. Absent → `MachineInputsUnsupported`. PC Steward never creates or repairs it.
5. **Loader and licensing.**
   - Both Framework and Framework64 `machine.config` files are parsed with bounds. Refuse app-domain managers, `codeBase`/`privatePath`, `developmentMode`, and any `dependentAssembly`/redirect naming `chocolatey.licensed`.
   - `chocolatey.licensed` and `policy.*.chocolatey.licensed` must be absent from `Microsoft.NET\assembly\GAC_MSIL|GAC_32|GAC_64` and the legacy `assembly\GAC*`.
   - `chocolatey.licensed.dll` must be absent from the root, culture subfolders and anywhere under `extensions`.
   - `choco.exe.config` must not exist.
   - **(M3)** Both `machine.config` files, their `Config` directories and every GAC root must pass ancestry evaluation and be requester-unmodifiable and reparse-free.
   - → `MachineInputsUnsupported`.
6. **Configuration.**
   - `config\chocolatey.config` is read through the held handle at 64 KiB maximum and passed to `ChocolateyConfigurationParser.TryRead`.
   - The required effective features from the Profile V2 table must hold.
   - `cacheLocation` must be nonempty, absolute, existing, requester-unmodifiable and reparse-free, and its directory identity is captured.
   - → `MachineInputsUnsupported`.
7. **Library and store (structure).**
   - The pinned V2 discovery set (lib-root `.nupkg` files and the top-level `.nupkg` of each immediate directory) is enumerated with Profile V2 bounds.
   - Exactly one target identity is read through `ChocolateyPackageArchiveIdentityReader`.
   - The four store-location alternatives are computed, and at most one may exist.
   - `.version`/`.sxs`/`.pin`, pending markers and `lib-bkp` prefix entries are refused.
   - `lib-bad` residue is recorded for disclosure.
   - The target must not be a client target.
   - Any failure in this step → `MachineInputsUnsupported` (L5).
8. **Source.** `ChocolateySourceResolution.TryResolve` must keep the compiled Community URL byte-equal. Otherwise → `SourceNotCommunity`, with installed and fingerprint null (S8). Rev 3 precedence is MachineInputsUnsupported, then SourceNotCommunity, then InstalledMismatch, then ProfileMismatch.
8a. **Installed value.** The library's installed version must equal the frozen value; otherwise → `InstalledMismatch` with the observed value.
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
- **(M4)** holds `FileShare.Read`-only handles on the root engine and every release-tree file (`helpers`/`tools`) from stage B through invocation and exit.
- holds `hooks`/`extensions` code files only from stage B until process creation, then releases them. Reviewed Community dependency updates may change extension packages; they are disclosed, not forbidden (Task 90 M2). Any change is caught by the file-first postcheck (`ClientChanged`, never Success).
- **(L6)** The binding captured in stage B is the one that launches. Stage D compares freshly observed identities against the held ones; any difference is drift.
- like `HeldHelperBinding`, it depends on **A-Q1** evidence that the loader can map held images;
- never holds `lib` or registration files (Profile V2: "do not hold writable package-registration files open").

The queries:
- Arguments are compiled. `list <ID> --exact --limit-output --no-color` and `outdated --source <compiled URL> --ignore-unfound --limit-output --no-color --execution-timeout 60` are the existing builders. Upgrade uses only `ChocolateyReviewedUpdateArguments.Build(operation)`.
- `ProcessStartInfo` is built from `EnginePath` plus `ChocolateyCompiledEnvironment.ApplyTo(start, LaunchPaths)`, with `UseShellExecute=false`, the working directory set to the root, and no inherited environment.
- Output capture is bounded at 1,000,000 characters and 1,024 records, as in Profile V2.
- Every query and the upgrade run through the **N0 launcher** (§4.1), inside one item job: the item's aggregate root. An unsettled job yields `Undetermined`/`¬aggregateSettlementConfirmed`.
- **Deadlines (M5).** The helper starts its clock when the pipe connects, **before** the frame read.
  - Each process deadline is min(its own budget, `11 min − elapsed − 5 s exit margin − 5 s response margin`).
  - The own budgets are 60 s for each query and 300 s for the upgrade, matching its `--execution-timeout`.
  - Reaching the deadline kills the job. A timeout before launch reports `Undetermined`+`timedOut` (N4). A timeout after launch reports `Started` + `timedOut`.
- **(L6)** The `outdated` record's installed field must equal the stage-C `list --exact` value; otherwise `Refused/InstalledMismatch` with the list value.

### 4.1 N0 native launcher (H4)

```csharp
internal sealed class ItemJob : IDisposable                   // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE, no BREAKAWAY_OK
{
    internal static ItemJob? TryCreate();
    internal Task<NativeProcessObservation> RunAsync(string engine, IReadOnlyList<string> arguments, ChocolateyLaunchPaths paths,
        TimeSpan deadline, int maximumCapturedCharacters, CancellationToken cancellationToken);
    internal bool IsSettled { get; }                           // QueryInformationJobObject: ActiveProcesses == 0
}
internal sealed record NativeProcessObservation(NativeInvocation Invocation, int? ExitCode, bool DirectExitObserved,
    bool OwnedIoCompleted, bool PipeEofObserved, bool TerminationFailed, bool AggregateSettled, bool Cancelled,
    bool TimedOut, bool OutputLimitExceeded, string StandardOutput, string StandardError);
internal enum NativeInvocation { NotStarted, Started, Unknown }
```

How `RunAsync` launches:
- It calls `CreateProcessW` with `CREATE_SUSPENDED | CREATE_NO_WINDOW | EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT`.
- The `PROC_THREAD_ATTRIBUTE_HANDLE_LIST` contains only the stdin (NUL), stdout and stderr pipe write ends.
- The environment block comes from `ChocolateyCompiledEnvironment` only, and the command line is built from the compiled argument list with the standard quoting rules.
- It then calls `AssignProcessToJobObject` and `ResumeThread`.

Invocation classification:
- `CreateProcessW` returns FALSE → `NotStarted`. This is the only definitive pre-creation failure.
- After success, invocation is `Started`. A failed job assignment terminates the suspended process; the observation is `Started` with the observed exit if termination is confirmed, otherwise `Unknown`.
- An exception after the call returns, before its result is known → `Unknown`.

Settlement:
- `AggregateSettled` means the job's active process count is 0 after the primary process exit. The kill-on-close job ensures no descendant outlives the helper.
- `DirectExitObserved` means the primary process exit was observed.
- `OwnedIoCompleted` and `PipeEofObserved` mean both pipe readers finished at EOF.

Output capture is bounded (1,000,000 characters, 1,024 records). Cancellation or the deadline closes the job, which kills every process. These are exactly wire facts 4–13.

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

Both bootstraps check that the process is elevated, connect with `TokenImpersonationLevel.Identification`, and require `TryGetServerProcessId == desktop-pid`.

Protocol 2 then opens the **`PeerProcess` handle** (H3) and runs the handle-based `VerifyPeer` overload: fixed desktop path, same session, same SID, trusted same signer. All later peer facts come from that handle.

### 5.2 Stage machine (rev 3 §3.1; G1; N1–N4)

| Stage | Work | Report |
|---|---|---|
| A0 | Read one frame (30 s); `ReviewedUpdateWireCodec.TryReadRequest`; hash of the received payload == digest; `ReviewedUpdateOperation.TryRestore`; nonce == bootstrap nonce (fixed time) | Any failure: **no response** (§6 rev 3) |
| A1 | Freshness at most 2 min, **`issuedAt` not later than now** (L6; same machine clock, no tolerance), within the 30-min review ceiling; route == `ChocolateyMachineRuntimeLimitsV3` with exact shape; payload SID == verified peer SID; `TryGetProfileDigest` | `Refused/RequestNotAdmitted`, fields 16–18 null |
| A2 | `RequesterToken.TryForVerifiedPeer(peerHandle, sid)` (§2.1 linked-token rules); peer not exited | `Refused/RequestNotAdmitted` |
| A3 (G1) | Helper capability reader (§6) with the A2 token as the protection principal; image hashes of the helper itself and of the peer (from the held handle) == ledger entries | `Refused/RequestNotAdmitted` |
| B | `ChocolateyProfileObserver.Observe(token, Helper, id, frozenInstalled, F)`, where F = the frozen operation's profile digest | Its refusal reason and values (S6–S9) |
| C | `ListExactAsync`, then `OutdatedAsync` (§4 table) | §4 table |
| D | Recheck cancellation, the 2-min and 30-min limits, and the **desktop's remaining budget** (N4: 11 min from request read, minus elapsed). Then repeat stage B in full with the same F. | Lapse → `Refused/ApprovalExpired`. Budget lapse → `Undetermined`+`timedOut` (N4). Drift → the stage-B reason with **stage-D** values; the candidate is kept unless installed is null (S7). |
| Launch | `UpgradeAsync` with the stage-B binding (L6) and `ChocolateyReviewedUpdateArguments.Build(operation)`, exactly once, through the N0 launcher (§4.1) | `CreateProcessW` FALSE → `Matched ∧ NotStarted`. Cancellation or timeout after D and before `CreateProcessW` → `Matched ∧ NotStarted` with the flag. `NativeInvocation.Unknown` → `Unknown`. Otherwise `Started` with the observation's exit code and flags (facts 4–13 from `NativeProcessObservation`). |

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

### 5.4 The qualification port (M6)

```csharp
internal interface IChocolateyHelperQualificationPort          // Execution-facing; one production implementation
{
    ChocolateyProfileResult ObserveProfile(RequesterToken token, string targetId, ChocolateyNativeVersion frozenInstalled,
        string expectedFingerprint, CancellationToken cancellationToken);
    Task<ChocolateyStageCResult> QueryAsync(ChocolateyHeldClientBinding client, ItemJob job, string targetId, TimeSpan deadline, CancellationToken cancellationToken);
    Task<NativeProcessObservation> UpgradeAsync(ChocolateyHeldClientBinding client, ItemJob job, ReviewedUpdateOperation operation, TimeSpan deadline, CancellationToken cancellationToken);
}
internal sealed class NativeChocolateyHelperQualification : IChocolateyHelperQualificationPort { /* N1–N3 only */ }
```

- The stage machine (N4a) depends only on this port. Its tests use fakes.
- Architecture tests:
  - `NativeChocolateyHelperQualification` is the **only** implementation in `src`;
  - the ElevatedHelper composition root (`ElevatedHelperEntryPoint`) constructs only it;
  - `ChocolateyProfileObservation` and `ChocolateyHeldClientBinding` keep private constructors, with exactly one factory call site each, inside `Native/`.
- A fake therefore cannot reach production. A pure-policy positive cannot construct a binding (Profile V2).

## 6. G1 detail

The helper evaluates the **same pure** `DeploymentCapabilityPolicy` as the desktop reader, over inputs it reads itself:
- ledger, manifest and capability file;
- binding-set hashes from its install directory under the same protection checks.

**(H1)** The existing reader's protection check (`ProtectedLaunchLayout.CallerCannotModify`) probes with the **current** token. In the elevated helper that is the admin token, which can write `Program Files`. So:
- `OwnerOnlyDeploymentCapabilityReader` gains a closed role `{Desktop, Helper}` as a constructor parameter, keeping one class. The role sets two things:
  - the expected process leaf (`PCSteward.Desktop.exe` or `PCSteward.ElevatedHelper.exe`);
  - the protection principal.
- **Desktop** keeps today's check.
- **Helper** evaluates with `RequesterAccess` and the A2 evaluation token: `Ancestor` mode from the volume root to `Program Files`; `Node` mode on the install root, the state root, the ledger, the manifest and every binding-set leaf.
- **(L6)** In Helper role:
  - `AppContext.BaseDirectory` must equal `<known-folder Program Files>\PC Steward\`;
  - `Environment.ProcessPath` must equal the helper apphost in that directory;
  - the state root is the same fixed path the desktop reader uses.

The peer desktop image hash is computed from the path returned for the verified peer PID. Both image hashes must equal their ledger entries. A failure gives `Refused/RequestNotAdmitted`. This is settled and NotInvoked, and the desktop records it as `Refused`.

## 7. N5 desktop producers (for completeness; admission waits for O5)

- **F′** `ChocolateyProfileEvidenceSource : IReviewedUpdateDispatchEvidenceSource`. It runs `Observe(TryFromCurrentProcess(), Desktop, id, frozenInstalled, expectedFingerprint: null)`. The current token must satisfy the §2.1 token rules, otherwise the result is null. and returns `FreshUpdateDispatchEvidence` with `EvidenceId = "choco-profile-v2:" + Fingerprint` from the **fresh** observation, never from the frozen operation (A2-L6; an architecture test already exists). A refusal returns null, so nothing is dispatched.
- **Review intent** `ObserveReviewIntentAsync`. The same observation, plus the non-elevated client's `outdated` intent, captured as `UserCacheIntent` with the required disclosure text.
- **Verifier** `ChocolateyReviewedUpdateVerifier : IReviewedUpdateVerifier`. `ChocolateyPostUpdateClientCheck.RunAsync` runs with a file-only capture first, through the Desktop-role reads, against `ClientState` from the F′ observation. Only `Unchanged` proceeds to the residue-aware registration re-observation, which requires the unique exact target. `ClientChanged`/`Unverifiable` gives `Unverified`, never Success. A restart-pending result is rejected (L5).
- **O5** is exactly whether the desktop role can complete §3 steps 1–3 and 5–9 without elevation. If it cannot, every update on this route ends `Unverifiable`, and the owner contingency in rev 3 §9 applies.

## 8. Tests per slice (summary)

- **N0 (Windows CI):**
  - a fixture child that spawns a grandchild → the job kills both on cancel and deadline;
  - `AggregateSettled` only at zero active processes;
  - `CreateProcessW` failure (missing image) → `NotStarted`;
  - the handle list excludes an inheritable test handle;
  - an output overflow;
  - the environment block equals the compiled one.
- **N1 (Windows CI, standard user):**
  - fixture DACLs: a requester-writable file, a parent with `FILE_DELETE_CHILD`, a requester-owned node, an OWNER RIGHTS restriction, a High integrity label → each gives the expected Node/Ancestor verdict;
  - an Ancestor-mode positive on a directory where the requester may only add subfolders;
  - **token negatives (M7)** on tokens derived from the test process: a `CreateRestrictedToken` token is refused; a duplicated token lowered to Low IL via `SetTokenInformation(TokenIntegrityLevel)` is refused; a child `PeerProcess` that exits before duplication is refused; a peer handle whose PID differs from the pipe PID is refused;
  - reparse (junction) refusal, final-path mismatch, a parent-identity mismatch, absence vs unreadable (L2), bounds, cancellation;
  - architecture: no environment variables, registry or SQLite; no write-capable opens; the `FILE_ID_INFO` layout equals Execution's.
- **Positive linked-token path (not CI).** GitHub Windows runners run the admin without UAC split tokens, so the linked-token match cannot be exercised there faithfully. It is **owner-host evidence** for the exact-artifact run (§9). CI proves the refusals and the evaluator.
- **N2:** synthetic `ProgramData` trees under a GUID temp root, injected through a resolver seam that is test-only (production uses `TryResolveMachine`). One positive, plus one negative for each step 1–9 rule. The compiled manifest equals the JSON. The fingerprint equals the existing goldens for the golden snapshot.
- **N3:** fake process with golden argument lists. Every stage-C table row, including N1/N3. Output-overflow and record-count bounds. Settlement tracking.
- **N4a/N4b:**
  - an in-process stage machine over a real pipe pair against `ReviewedHelperExchangeSession` (portable), with a fake port (§5.4);
  - deadline arithmetic (M5): connection-start clock, margins, a timeout before launch → `Undetermined`+`timedOut`;
  - every §5.2 row, including the A0 no-response cases, G1 refusal, N2 fault rows, S7 nulling and `CreateResponse` throwing → no response;
  - architecture: the only `CreateResponse` call site is the endpoint; the endpoint never references `ElevatedHelperProtocol.CreateResponse`.
- **N5:** fake observer outcomes → F′ null or evidence. Verifier ordering via the existing `ChocolateyPostUpdateClientCheck` tests plus composition.

No CI test runs the real Chocolatey client, reads the runner's ProgramData Chocolatey, or elevates.

## 9. Native evidence still required (owner host; not CI)

- **O5:** desktop-role readability (§7).
- **A-Q1:** loader compatibility of `FileShare.Read` holds, for both `HeldHelperBinding` and `ChocolateyHeldClientBinding`. The A2-L5 fallback applies only to the helper binding. The client binding has **no** fallback; without A-Q1 the route stays unavailable.
- **A2-L3:** write probes under holds return ACCESS_DENIED.
- An exact-artifact run of the signed helper (ADR-025 follow-up), including the §2.1 linked-token match against the real desktop and an `AccessCheck` result on the real Chocolatey tree.

The [host-evidence plan](TASK_50_HOST_EVIDENCE_PLAN.md) covers O5, A-Q1 and A2-L3 and is awaiting owner approval.

## 10. Questions from revision 1 (reviewer answers adopted)

1. **Requester token.** Yes, `AccessCheck` with an identification token. The evaluation token is the helper's **linked token**, matched to the peer by `AuthenticationId` (§2.1). The helper is the pipe client and cannot impersonate the desktop.
2. **Path opens.** Sufficient under the same-user threat model, given top-down ancestry evaluation (§2.2), final-path comparison, and each child's parent identity equal to the held enumerated directory (§2.3). `NtCreateFile(RootDirectory)` is optional hardening.
3. **G1 reader.** One class with a closed role `{Desktop, Helper}` that sets the process leaf and the protection principal (§6).
4. **Slice order.** N4 is split. N4a (stage machine with fakes) may land early. N4b (entry-point wiring and the native port) lands only after N0–N3; until then protocol 2 exits with code 20.

## 11. Changes from revision 1 (review of `1024708`)

| Finding | Severity | Change |
|---|---|---|
| H1 G1 used the elevated token and ran before the token existed | High | A2/A3 swapped; Helper-role reader evaluates with the requester token (Ancestor/Node modes) (§5.2, §6) |
| H2 a Low/restricted/AppContainer desktop could understate rights | High | Evaluate the helper's linked token; peer LUID/SID/elevation-type match; exactly Medium, not restricted, not AppContainer, no backup/restore/take-ownership/relabel/security privileges (§2.1) |
| H3 PID reuse and exit | High | One `PeerProcess` handle from connection; all peer facts from it; exit check after duplication (§2.1, §5.1) |
| H4 no aggregate root or wire facts | High | New slice N0: suspended `CreateProcessW`, handle list, kill-on-close job, settlement at zero active processes, `NativeProcessObservation` carrying facts 4–13 (§4.1) |
| M1 layering | Medium | `Native/` owns its identity types; `InternalsVisibleTo(Execution)`; port-only access (§2, §5.4) |
| M2 ancestors | Medium | `AccessMode.Ancestor` and top-down `EvaluateAncestry` for every protected root (§2.2) |
| M3 loader ACLs | Medium | `machine.config` files, `Config` directories and GAC roots must be protected (§3 step 5) |
| M4 hold scope | Medium | Extension and hook files held only until process creation; the release tree is held through exit (§4) |
| M5 deadlines | Medium | Clock starts at connection; per-process deadline from the desktop's remaining budget minus margins (§4) |
| M6 fakes | Medium | Single qualification port with one native implementation; composition and factory architecture tests (§5.4) |
| M7 principal tests | Medium | Token negatives in CI; the positive linked-token path declared host evidence (§8, §9) |
| L1 known folders | Low | `GetUserProfileDirectory` on the token; known folders in own context (§2.4) |
| L2 absence | Low | Absence only from the held parent enumeration (§2.3) |
| L3 shim | Low | Root engine trusted by manifest hash plus `WinVerifyTrust` on the held handle; no `ResolveExecutable` (§3 step 2) |
| L4 ownership | Low | User, logon and enabled group SIDs; every HTTP-cache node (§2.2, §3 step 4) |
| L5 order | Low | Library structure before source; installed comparison after source; all regular files in hooks/extensions; `logs` may be absent under a protected root (§3) |
| L6 details | Low | `outdated` installed field equals the list value; the stage-B binding launches; future `issuedAt` refused; G1 base-directory and process-path rule (§4, §5.2, §6) |
