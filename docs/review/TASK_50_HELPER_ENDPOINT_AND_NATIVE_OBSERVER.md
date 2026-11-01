# Task 50 — protocol-2 helper endpoint and native Profile V2 observer (design, revision 4)

Requirements: `FR-UPD-15`, `FR-UPD-01/08/09/14`, `AC-UPD-01–11`, `TM-03/04/07/13`.

Status: **revision 4, proposed for a fresh independent re-review before any implementation.**
- Revision 1 (`1024708`, SHA-256 `66cc6398…a46db239`) was reviewed CONDITIONAL: no Blocker, 4 High, 7 Medium, 6 Low.
- Revision 2 (`0f03ae7`, SHA-256 `0037f8bd…4780`) was reviewed CONDITIONAL: no Blocker or High, 5 Medium, 6 Low.
- Revision 3 (`38959d6`, SHA-256 `8c813d4e…e293`) was reviewed CONDITIONAL: no Blocker or High, 3 Medium, 4 Low.

This revision addresses every finding (§11–§13). This record authorizes no host action, UAC, deployment, provider query or route admission.

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
- Execution calls the observer **only** through the qualification port (§5.4), which is declared **in Provider.Chocolatey**. It calls `RequesterToken`/`RequesterAccess` from the helper G1 reader (§6).
- An architecture test lists every Execution file that references `PCSteward.Provider.Chocolatey.Native`. Only the port interfaces of §5.4 may appear there: no Execution file may name `LaunchJob`, `ProtectedNodeReader`, `ProtectedTreeWalker`, `RequesterToken`, `PeerProcess`, `ChocolateyHeldClientBinding`, `ChocolateyProfileObservation` or `ChocolateyCompiledCommand` (rev-3 M-A).

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

**Evaluation token (H2).** The token passed to `AccessCheck` is the helper's own **linked token** (`GetTokenInformation(TokenLinkedToken)`). Without SeTcb, Windows returns it as an identification-level **impersonation** token, which `AccessCheck` uses directly. It is accepted only when all of the following hold:
- the peer desktop token (from the held handle, `TOKEN_QUERY`) has `TokenElevationType == Limited`;
- its `AuthenticationId` equals the linked token's `AuthenticationId`;
- its user SID equals `expectedSid` and the linked token's SID;
- the evaluation token's integrity level is **exactly Medium**;
- `IsTokenRestricted` is false;
- `TokenIsAppContainer` is false;
- **(L4)** every privilege present (enabled or not) is in the allow-list {`SeChangeNotifyPrivilege`, `SeShutdownPrivilege`, `SeUndockPrivilege`, `SeIncreaseWorkingSetPrivilege`, `SeTimeZonePrivilege`}. Any other privilege kept by UAC filtering could exceed what `AccessCheck` models, so it refuses.

A Low-integrity, restricted or AppContainer desktop therefore cannot understate the user's rights: the evaluation always uses the full Medium filtered token of that logon. If UAC is off (no linked token), or the elevation type is Default/Full, the request is refused (`RequestNotAdmitted`).

**(L6)** This is a **new availability constraint of this route**, not an existing ADR-018/022 rule. The route is available only to an administrator account using a UAC split token. It is unavailable when UAC is off, and for standard-user desktops (elevation type Default, where the helper runs under a different account and the same-SID peer model already refuses). The review UI states this as an unavailability reason (N6).

The desktop role applies the same rules to its own token (exactly Medium, not restricted, not AppContainer, privilege allow-list, elevation type Limited). **(L3)** The current process token is primary, so it is first duplicated with `DuplicateTokenEx(TOKEN_QUERY, SecurityIdentification, TokenImpersonation)`.

**Testable validation (M5).** The rules are a pure function `RequesterTokenPolicy.Validate(TokenFacts peer, TokenFacts evaluation) → TokenVerdict`. `TokenFacts` holds the user SID, AuthenticationId, elevation type, integrity RID, restricted, AppContainer and the privilege names. The verdict is closed, with one value per specific refusal reason. The native factories only read `TokenFacts` from real tokens and call the policy.

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
    internal static RequesterModification EvaluateAncestry(RequesterToken token, string canonicalPath); // lexical drive root → parent
    internal static bool IsRequesterOwned(RequesterToken token, SafeFileHandle node);                // L4
}
```

**Two modes (M2).**
- **Node** uses the full mask below. It applies to every protected node.
- **Ancestor (M3)** denies `FILE_DELETE_CHILD | DELETE | WRITE_DAC | WRITE_OWNER` and requires no reparse attribute, on **every** directory from the lexical drive root of the final path down to the parent of the protected root. This is the same set as the existing `ProtectedLaunchLayout.AncestorReplacementRights`. Because every ancestor is checked for `FILE_DELETE_CHILD`, every ancestor's own rename/replace right is covered by its parent's check. The drive root itself has no parent check.
- The walk starts at the lexical root of the final path, **not** `GetVolumePathName`, so a mounted-folder ancestor is not skipped.
- Standard users may create subfolders in `C:\ProgramData` (`FILE_ADD_SUBDIRECTORY`, which Ancestor mode tolerates), but must not be able to delete, rename, replace or re-ACL an ancestor or one of its children.
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
internal readonly record struct NativeNode(string FullPath, string RelativePath, bool Directory, NativeFileIdentity Identity, long Length);
internal static class ProtectedNodeReader
{
    // Opens by path with FILE_FLAG_OPEN_REPARSE_POINT (and BACKUP_SEMANTICS for directories), share-read only; refuses a
    // reparse attribute, a final path different from the requested canonical path, or a zero file ID.
    internal static SafeFileHandle? TryOpen(string canonicalPath, bool directory, out NativeNode node);
    internal static bool TryReadBounded(SafeFileHandle file, int maximum, out byte[] bytes);        // EOF at observed length
    internal static bool TryHashBounded(SafeFileHandle file, long maximum, out NativeObservedFile observed);
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
// Native-internal; reached only through IChocolateyHelperItem (§5.4). Each call creates its own LaunchJob (§4.1).
internal static class ChocolateyClientQueries
{
    internal static Task<ChocolateyQueryOutcome<ChocolateyInstalledRecord>> ListExactAsync(ChocolateyHeldClientBinding client,
        ChocolateyCompiledCommand.ListExact command, TimeSpan deadline, CancellationToken cancellationToken);
    internal static Task<ChocolateyQueryOutcome<ChocolateyOutdatedRecord>> OutdatedAsync(ChocolateyHeldClientBinding client,
        ChocolateyCompiledCommand.Outdated command, TimeSpan deadline, CancellationToken cancellationToken);
    internal static Task<NativeProcessObservation> UpgradeAsync(ChocolateyHeldClientBinding client,
        ChocolateyCompiledCommand.Upgrade command, TimeSpan deadline, CancellationToken cancellationToken);
}
// Sealed hierarchy with private constructors and validating factories, all inside Native/ (rev-3 M-B).
internal abstract record ChocolateyCompiledCommand
{
    private ChocolateyCompiledCommand() { }
    internal IReadOnlyList<string> Arguments { get; private init; } = [];
    internal sealed record ListExact : ChocolateyCompiledCommand;   // ChocolateyExactListArguments.Build(operation)
    internal sealed record Outdated : ChocolateyCompiledCommand;    // ChocolateyUpdateSourceConnector.BuildOutdatedArguments()
    internal sealed record Upgrade : ChocolateyCompiledCommand;     // ChocolateyReviewedUpdateArguments.Build(operation)
    internal static ListExact? TryListExact(ReviewedUpdateOperation operation);
    internal static Outdated CreateOutdated();
    internal static Upgrade? TryUpgrade(ReviewedUpdateOperation operation);
}
```

The commands (rev-3 M-B):
- **`ChocolateyExactListArguments.Build(operation)`** is a new pinned builder. It returns `["list", id, "--exact", "--limit-output", "--no-color"]` only when the operation has the exact route shape (`ChocolateyMachineRuntimeLimitsV3`, the same predicates as `ChocolateyReviewedUpdateArguments.Build`), `ChocolateyNativePackageId.IsSupported(id)` holds, and the ID is not a client target (`ChocolateyClientReleasePolicy.IsClientTarget`). A golden test pins it against the protocol-1 literal at `ChocolateyProvider.cs:189`.
- **`Outdated`** uses the existing `BuildOutdatedArguments()`.
- **`Upgrade`** uses `ChocolateyReviewedUpdateArguments.Build(operation)`.
- Each factory returns null if its builder refuses. The only factory call sites are inside the item implementation (architecture test).

The binding:
- **(M4)** holds `FileShare.Read`-only handles on the root engine and every release-tree file (`helpers`/`tools`) from stage B through invocation and exit.
- **(L2)** holds `hooks`/`extensions` code files from stage B until `CreateProcessW`, `AssignProcessToJobObject` **and** `ResumeThread` have all succeeded, then releases them.
  - "Through invocation" in Profile V2 is read as through process creation (rev 3 L-5).
  - Later loads (extensions at client startup, hooks at script time) rely on the requester-unmodifiable ACLs checked in stages B and D, plus the Task 90 M2 file-first postcheck. That check reports `ClientChanged`, never Success.
  - Reviewed Community dependency updates may change extension packages. They are disclosed, not forbidden (Task 90 M2).
- **(L6)** The binding captured in stage B is the one that launches. Stage D compares freshly observed identities against the held ones; any difference is drift.
- like `HeldHelperBinding`, it depends on **A-Q1** evidence that the loader can map held images;
- never holds `lib` or registration files (Profile V2: "do not hold writable package-registration files open").

The queries:
- Arguments come only from `ChocolateyCompiledCommand` (above).
- `ProcessStartInfo` is built from `EnginePath` plus `ChocolateyCompiledEnvironment.ApplyTo(start, LaunchPaths)`, with `UseShellExecute=false`, the working directory set to the root, and no inherited environment.
- Output capture is bounded at 1,000,000 characters and 1,024 records, as in Profile V2.
- Every query and the upgrade run through the **N0 launcher** (§4.1). Each launch has its own job. The item's aggregate root is the set of all its launch jobs, and it is settled only if every launch job settled. An unsettled launch yields `Undetermined`/`¬aggregateSettlementConfirmed`.
- **Deadlines (M5, revised for rev-2 M1).** The helper starts its clock `t0` when the pipe connects, before the frame read. `remaining = 11 min − (now − t0) − 5 s exit margin − 5 s response margin`.
  - Each process uses its **existing** item budget (Profile V2: "existing item budgets… no new deadline"), capped by `remaining`. The budgets are `list --exact` 30 s (protocol-1 executor recheck), `outdated` 45 s (`ChocolateyUpdateSourceConnector.Timeout`) and upgrade 10 min (`ChocolateyMachineUpdateExecutor.UpdateTimeout`).
  - `--execution-timeout 300` is the client's own per-script limit and is not the whole-run cap.
  - **Budget checks (rev-3 L-A).**
    - A `remaining` lapse during stage B, C or the stage-D re-walk, or a failed check at the start of D, gives `Undetermined` + `timedOut` (N4).
    - The observer receives `remaining` as its bound, and refuses with a timed-out result when exceeded.
  - **Launch floor (Launch row only).** After stage D has completed, immediately before `CreateProcessW`, `remaining` must be at least 60 s. Otherwise → `Matched ∧ NotStarted` + `timedOut`. This is settled: nothing was launched and all query jobs settled.
  - A query reaching its deadline kills its job and gives `Undetermined` + `timedOut` (N4). An upgrade reaching its deadline kills the job and gives `Started` + `timedOut`.
- **(L6)** The `outdated` record's installed field must equal the stage-C `list --exact` value; otherwise `Refused/InstalledMismatch` with the list value.

### 4.1 N0 native launcher (H4)

```csharp
internal sealed class LaunchJob : IDisposable  // one per process; JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE; no BREAKAWAY_OK; own IOCP
{
    // M4: no strings. Called only from the three ChocolateyClientQueries methods (architecture test).
    internal static Task<NativeProcessObservation> RunAsync(ChocolateyHeldClientBinding client, ChocolateyCompiledCommand command,
        TimeSpan deadline, CancellationToken cancellationToken);
}
internal sealed record NativeProcessObservation(NativeInvocation Invocation, int? ExitCode, bool DirectExitObserved,
    bool OwnedIoCompleted, bool PipeEofObserved, bool TerminationFailed, bool AggregateSettled, bool Cancelled,
    bool TimedOut, bool OutputLimitExceeded, bool LingeringTerminated, string StandardOutput, string StandardError);
internal enum NativeInvocation { NotStarted, Started, Unknown }
```

**Launcher surface (M4, rev-3 M-A/M-B).**
- `LaunchJob.RunAsync` takes no path or argument strings: the engine path comes from the held binding, the arguments from the sealed `ChocolateyCompiledCommand`.
- An architecture test pins its call sites to the three methods of `ChocolateyClientQueries`, and forbids any Execution reference to `LaunchJob` or `ChocolateyCompiledCommand`.
- Each launch has its own job and completion port, so no message from an earlier launch can reach a later one (rev-3 M-C stale message).

How `RunAsync` launches:
- It calls `CreateProcessW` with `CREATE_SUSPENDED | CREATE_NO_WINDOW | EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT`.
- The `PROC_THREAD_ATTRIBUTE_HANDLE_LIST` contains only the stdin (NUL), stdout and stderr pipe write ends.
- The environment block comes from `ChocolateyCompiledEnvironment` only, and the command line is built from the compiled argument list with the standard quoting rules.
- It then calls `AssignProcessToJobObject` and `ResumeThread`.

Invocation classification:
- `CreateProcessW` returns FALSE → `NotStarted`. This is the only definitive pre-creation failure.
- **(L1)** After `CreateProcessW` succeeds, a failure of `AssignProcessToJobObject` **or** `ResumeThread` means the suspended process never ran user code. The launcher calls `TerminateProcess` and waits, within the exit margin, for the process handle:
  - confirmed exit → `NotStarted` (definitive launch failure: rev 3 Launch row, settled, S13 satisfied);
  - otherwise `Unknown`.
- After `ResumeThread` succeeds, invocation is `Started`.
- An exception after the call returns, before its result is known → `Unknown`.
- **Integrating note (rev-3 L-D).** Reporting `NotStarted` after `CreateProcessW` succeeded, for a suspended process that was never resumed and whose termination is confirmed, is an **interpretation** of rev 3's Launch row ("a definitive failure before process creation"). It treats "creation" as "the process could run user code". The integrating record annotates rev 3 §3.1 with it, as was done for G1, without rewriting the pinned artifact.

Settlement:
- `AggregateSettled` means the job's active process count reached 0 after the primary process exit, observed **while the job handle is open**.
  - **Authority:** `QueryInformationJobObject(JobObjectBasicAccountingInformation).ActiveProcesses == 0`.
  - `JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO` on the launch's own completion port is only a wake-up, re-confirmed by the query (rev-3 M-C).
- **Settle window after a normal exit (rev-3 M-C).** After the primary process exits, the launcher waits at most **10 s** (inside `remaining`; the exit and response margins are reserved separately) for `ActiveProcesses == 0` and both pipe readers at EOF.
  - Both reached → settled; `LingeringTerminated` = false.
  - Otherwise, the M2 teardown runs. Lingering descendants, for example an application relaunched by an installer, are killed.
    - Zero count confirmed → `AggregateSettled` = true and `LingeringTerminated` = true.
    - Otherwise → `AggregateSettled` = false and `TerminationFailed` = true.
  - `LingeringTerminated` is reported in the helper diagnostic as the fixed token `lingering-descendants-terminated`. The wire has no such field, and it never changes the outcome. The review disclosure (N6) states that processes started by the update and still running afterwards may be closed.
- **Teardown (M2).** On cancellation or a deadline, the launcher calls `TerminateJobObject` and waits, within the exit margin, for the active-process-zero message (or `ActiveProcesses == 0`). Only then does it close the job handle.
  - The zero count was observed → `AggregateSettled` = true.
  - Otherwise → `AggregateSettled` = false and `TerminationFailed` = true.
  - Kill-on-close on the final handle close remains a backstop for helper crashes, never the settlement step.
- Settlement is job-scoped. Work hosted outside the job (for example by `msiserver`) is outside it; the desktop postcheck stays independent of settlement (Info).
- Nested jobs: an empty job nests under an existing one (Windows 8+), and kill-on-close works when nested.
- `DirectExitObserved` means the primary process exit was observed.
- `OwnedIoCompleted` and `PipeEofObserved` mean both pipe readers finished at EOF.

Output capture is bounded (1,000,000 characters, 1,024 records). These are exactly wire facts 4–13.

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

Protocol 2 then calls `port.OpenPeer(pipeServerPid)` (§5.4), which opens the **held peer handle** (H3). It runs the handle-based `VerifyPeer` overload on the identity read through that handle: fixed desktop path, same session, same SID, trusted same signer. All later peer facts come from that handle.

### 5.2 Stage machine (rev 3 §3.1; G1; N1–N4)

| Stage | Work | Report |
|---|---|---|
| A0 | Read one frame (30 s); `ReviewedUpdateWireCodec.TryReadRequest`; hash of the received payload == digest; `ReviewedUpdateOperation.TryRestore`; nonce == bootstrap nonce (fixed time) | Any failure: **no response** (§6 rev 3) |
| A1 | Freshness at most 2 min, **`issuedAt` not later than now** (L6; same machine clock, no tolerance), within the 30-min review ceiling; route == `ChocolateyMachineRuntimeLimitsV3` with exact shape; payload SID == verified peer SID; `TryGetProfileDigest` | `Refused/RequestNotAdmitted`, fields 16–18 null |
| A2 | `item = port.BeginItem(peer, sid)` (§5.4): token acquisition with the §2.1 linked-token rules; peer not exited | `Refused/RequestNotAdmitted` |
| A3 (G1) | Helper capability reader (§6) with `item.RequesterCannotModify` as its protection check; image hashes of the helper itself and of the peer (`peer.ImagePath`) == ledger entries | `Refused/RequestNotAdmitted` |
| B | `item.ObserveProfile(operation, F, remaining)`, where F = the frozen operation's profile digest. Returns **values only**. | Its refusal reason and values (S6–S9) |
| C | `item.QueryCandidateAsync(remaining)`: `list --exact`, then `outdated` (§4 table) | §4 table |
| D | Recheck cancellation, the 2-min and 30-min limits, and `remaining` (§4: from `t0` at connection). Then `item.RevalidateBeforeLaunch(remaining)`: stage B in full with the same F, comparing identities with the held stage-B binding. | Approval lapse → `Refused/ApprovalExpired`. Failed budget check at the start of D, or a lapse during the re-walk → `Undetermined`+`timedOut` (N4). Drift → the stage-B reason with **stage-D** values; the candidate is kept unless installed is null (S7). |
| Launch | Launch floor (§4); then `item.UpgradeAsync(remaining)` exactly once, with the stage-B binding (L6), the sealed `Upgrade` command and the N0 launcher (§4.1). The item allows this **only** with its internal one-use launch permission (rev-3 L-C), minted only after a stage-C match **and** a stage-D `Observed`. | Launch floor failed → `Matched ∧ NotStarted`+`timedOut`. `CreateProcessW` FALSE, or confirmed termination of a never-resumed process (§4.1 note) → `Matched ∧ NotStarted`. Cancellation after D and before `CreateProcessW` → `Matched ∧ NotStarted` with the flag. `NativeInvocation.Unknown` → `Unknown`. Otherwise `Started` with the observation's exit code and flags (facts 4–13). No permission → the item refuses without launching, which the stage machine treats as an internal fault (N2). |

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

### 5.4 The qualification port (M6, rev-3 M-A)

The port owns the item. The stage machine handles only **values** and opaque interfaces. No Native type with a private constructor crosses into Execution.

```csharp
// Declared in Provider.Chocolatey. One production implementation of each interface.
internal interface IChocolateyHelperQualificationPort
{
    IChocolateyPeer? OpenPeer(int pipeServerProcessId);              // held handle (H3); null on failure
    IChocolateyHelperItem? BeginItem(IChocolateyPeer peer, string expectedSid, out ChocolateyItemRefusal refusal); // A2
}
internal interface IChocolateyPeer : IDisposable
{
    int ProcessId { get; }
    bool HasExited { get; }
    bool TryReadIdentity(out string imagePath, out int session, out string userSid);
}
internal interface IChocolateyHelperItem : IDisposable              // owns token, held binding and launch jobs
{
    bool RequesterCannotModify(string root, IReadOnlyList<string> files);          // G1 protection check (§6)
    ChocolateyStageValues ObserveProfile(ReviewedUpdateOperation operation, string expectedFingerprint, TimeSpan remaining, CancellationToken cancellationToken);
    Task<ChocolateyCandidateValues> QueryCandidateAsync(TimeSpan remaining, CancellationToken cancellationToken);
    ChocolateyStageValues RevalidateBeforeLaunch(TimeSpan remaining, CancellationToken cancellationToken);
    Task<NativeProcessObservation> UpgradeAsync(TimeSpan remaining, CancellationToken cancellationToken);
    bool AggregateSettled { get; }                                   // every launch job of the item settled
}
```

The value types are plain records with public construction. Fakes can therefore build them, but they carry no authority:
- `ChocolateyStageValues` is a closed kind {Observed, Refused(reason), TimedOut}, plus the observed installed version and fingerprint "as far as observed";
- `ChocolateyCandidateValues` is a closed kind {Matched(candidate), Refused(reason, values), Undetermined(flags)};
- `NativeProcessObservation` is the wire facts.

Rules enforced inside the native item, independent of the stage machine:
- `QueryCandidateAsync` requires an earlier `Observed` from `ObserveProfile`;
- `RevalidateBeforeLaunch` requires a stage-C `Matched`;
- `UpgradeAsync` requires the one-use launch permission minted by a stage-D `Observed` that follows a stage-C `Matched` (rev-3 L-C);
- any out-of-order call refuses without I/O or launch.

Architecture tests:
- `NativeChocolateyHelperQualification` (port), its item and its peer are the **only** implementations in `src`;
- the ElevatedHelper composition root (`ElevatedHelperEntryPoint`) constructs only the native port;
- `ChocolateyProfileObservation`, `ChocolateyHeldClientBinding`, `RequesterToken` and `ChocolateyCompiledCommand` keep private constructors, with factory call sites only inside `Native/`;
- the Execution reference rule of §2.

A fake port or item in tests can drive every §5.2 row with values. A fake cannot reach production, and a pure-policy positive cannot construct a binding (Profile V2).

## 6. G1 detail

The helper evaluates the **same pure** `DeploymentCapabilityPolicy` as the desktop reader, over inputs it reads itself:
- ledger, manifest and capability file;
- binding-set hashes from its install directory under the same protection checks.

**(H1)** The existing reader's protection check (`ProtectedLaunchLayout.CallerCannotModify`) probes with the **current** token. In the elevated helper that is the admin token, which can write `Program Files`. So:
- `OwnerOnlyDeploymentCapabilityReader` gains a closed role `{Desktop, Helper}` as a constructor parameter, keeping one class. The role sets two things:
  - the expected process leaf (`PCSteward.Desktop.exe` or `PCSteward.ElevatedHelper.exe`);
  - the protection principal (in Helper role, the `IChocolateyHelperItem.RequesterCannotModify` delegate).
- **Desktop** keeps today's check.
- **Helper** evaluates with `RequesterAccess` and the A2 evaluation token: `Ancestor` mode from the lexical drive root to `Program Files`; `Node` mode on the install root, the state root, the ledger, the manifest and every binding-set leaf.
- **(L6)** In Helper role:
  - `AppContext.BaseDirectory` must equal `<known-folder Program Files>\PC Steward\`;
  - `Environment.ProcessPath` must equal the helper apphost in that directory;
  - the state root is the same fixed path the desktop reader uses.

The peer desktop image hash is computed from the image path read from the **held peer handle** (§2.1). Both image hashes must equal their ledger entries. A failure gives `Refused/RequestNotAdmitted`. This is settled and NotInvoked, and the desktop records it as `Refused`.

## 7. N5 desktop producers (for completeness; admission waits for O5)

- **F′** `ChocolateyProfileEvidenceSource : IReviewedUpdateDispatchEvidenceSource`. It runs `Observe(TryFromCurrentProcess(), Desktop, id, frozenInstalled, expectedFingerprint: null)`. The current token must satisfy the §2.1 token rules, otherwise the result is null. and returns `FreshUpdateDispatchEvidence` with `EvidenceId = "choco-profile-v2:" + Fingerprint` from the **fresh** observation, never from the frozen operation (A2-L6; an architecture test already exists). A refusal returns null, so nothing is dispatched.
- **Review intent** `ObserveReviewIntentAsync`. The same observation, plus the non-elevated client's `outdated` intent, captured as `UserCacheIntent` with the required disclosure text.
- **Verifier** `ChocolateyReviewedUpdateVerifier : IReviewedUpdateVerifier`. `ChocolateyPostUpdateClientCheck.RunAsync` runs with a file-only capture first, through the Desktop-role reads, against `ClientState` from the F′ observation. Only `Unchanged` proceeds to the residue-aware registration re-observation, which requires the unique exact target. `ClientChanged`/`Unverifiable` gives `Unverified`, never Success. A restart-pending result is rejected (L5).
- **O5** is exactly whether the desktop role can complete §3 steps 1–3 and 5–9 without elevation. If it cannot, every update on this route ends `Unverifiable`, and the owner contingency in rev 3 §9 applies.

## 8. Tests per slice (summary)

- **N0 (Windows CI):**
  - a fixture child that spawns a grandchild → the job kills both on cancel and deadline;
  - `AggregateSettled` only at zero active processes;
  - a lingering grandchild after a normal child exit → killed after the 10 s settle window, `LingeringTerminated` (rev-3 M-C);
  - per-launch jobs: a second launch is unaffected by the first launch's messages;
  - an internal **test-only** fault hook (architecture-banned from `src` callers) injects an assign/resume failure → `NotStarted` after confirmed termination (L1), and a teardown failure → `TerminationFailed`;
  - `CreateProcessW` failure (missing image) → `NotStarted`;
  - the handle list excludes an inheritable test handle;
  - an output overflow;
  - the environment block equals the compiled one.
- **N1 (Windows CI; the runner is an admin without split tokens):**
  - fixture DACLs: a requester-writable file, a parent with `FILE_DELETE_CHILD`, a requester-owned node, an OWNER RIGHTS restriction, a High integrity label → each gives the expected Node/Ancestor verdict;
  - an Ancestor-mode positive on a directory where the requester may only add subfolders;
  - **token policy (M5, portable):** `RequesterTokenPolicy.Validate` over every combination of AuthenticationId match, SID match, elevation type, integrity level, restricted, AppContainer and privileges inside/outside the allow-list. Each test asserts the **specific** verdict;
  - **evaluator principal (M5, Windows):** an internal **test-only** factory `RequesterToken.ForTestsFromDerivedToken()` builds a token from the runner's own token: `CreateRestrictedToken(LUA_TOKEN)`, set to Medium IL, privileges outside the allow-list removed, then duplicated to identification level.
    - The fixture-DACL tests run against it.
    - Each test first asserts the token's shape through `TokenFacts`: Medium, allow-listed privileges, Administrators deny-only. The factory bypasses `Validate`, so this is checked explicitly.
    - Fixture owners are set explicitly to the user SID or SYSTEM, never left at the runner default of Administrators, which is deny-only in this token.
    - An architecture test forbids any `src` caller of this factory;
  - **native factory refusals (Windows):** on the runner, `TryForVerifiedPeer` refuses with the specific reason "no linked token"; a child `PeerProcess` that exits before duplication is refused; a peer handle whose PID differs from the pipe PID is refused. These assert the reason, not just null;
  - reparse (junction) refusal, final-path mismatch, a parent-identity mismatch, absence vs unreadable (L2), bounds, cancellation;
  - architecture: no environment variables, registry or SQLite; no write-capable opens; the `FILE_ID_INFO` layout equals Execution's.
- **Positive linked-token path (not CI).** GitHub Windows runners run the admin without UAC split tokens, so the linked-token match cannot be exercised there faithfully. It is **owner-host evidence** for the exact-artifact run (§9). CI proves the refusals and the evaluator.
- **N2:** synthetic `ProgramData` trees under a GUID temp root, injected through a resolver seam that is test-only (production uses `TryResolveMachine`). One positive, plus one negative for each step 1–9 rule. The compiled manifest equals the JSON. The fingerprint equals the existing goldens for the golden snapshot.
- **N3:** fake process with golden argument lists. Every stage-C table row, including N1/N3. Output-overflow and record-count bounds. Settlement tracking.
- **N4a/N4b:**
  - an in-process stage machine over a real pipe pair against `ReviewedHelperExchangeSession` (portable), with a fake port, item and peer returning values (§5.4);
  - native item ordering (Windows): out-of-order `QueryCandidateAsync`/`RevalidateBeforeLaunch`/`UpgradeAsync` refuse without I/O or launch;
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

## 12. Changes from revision 2 (review of `0f03ae7`)

| Finding | Severity | Change |
|---|---|---|
| M1 new 60 s / 300 s deadlines; wording inconsistent | Medium | Existing item budgets (30 s, 45 s, 10 min) capped by `remaining` from `t0` at connection; 60 s launch floor → `Matched ∧ NotStarted`+`timedOut`; §4 and §5.2 D wording aligned (§4, §5.2) |
| M2 settlement after closing the job | Medium | `TerminateJobObject`, then wait for active-process-zero with the handle open, then close; otherwise `TerminationFailed` (§4.1) |
| M3 ancestor mask read as one directory | Medium | Deny `FILE_DELETE_CHILD`, `DELETE`, `WRITE_DAC`, `WRITE_OWNER` plus reparse on every ancestor from the lexical drive root; mounted folders not skipped (§2.2) |
| M4 general-purpose launcher reachable from Execution | Medium | `RunAsync` takes the held binding plus a closed compiled command, no strings; call sites pinned; no Execution reference to `ItemJob`; port declared in Provider.Chocolatey (§2, §4.1, §5.4) |
| M5 CI cannot exercise the factories | Medium | Pure `RequesterTokenPolicy` over `TokenFacts` with specific verdicts; test-only derived-token factory with an architecture ban on production callers; native refusals assert their reason (§2.1, §8) |
| L1 assignment/resume failure labelled Started | Low | Terminated suspended process → `NotStarted` if exit is confirmed, else `Unknown`; same for a `ResumeThread` failure (§4.1) |
| L2 hold-release point ambiguous | Low | Release after `CreateProcessW` + assignment + resume; later loads rely on ACLs plus the file-first postcheck (§4) |
| L3 desktop primary token | Low | Duplicated to an identification-level impersonation token; linked token used directly (§2.1) |
| L4 privilege deny-list | Low | Allow-list of the five standard filtered-token privileges (§2.1) |
| L5 Execution types in `NativeNode`/`TryHashBounded` | Low | `NativeFileIdentity`/`NativeObservedFile` (§2.3) |
| L6 UAC claim unsupported | Low | Recorded as a new availability constraint of this route, disclosed in review UI (§2.1) |
| Info | — | Peer image from the held handle (§6); stage B through the port (§5.2); N0 added to STATUS; job-scoped settlement and nested jobs noted (§4.1) |

## 13. Changes from revision 3 (review of `38959d6`)

| Finding | Severity | Change |
|---|---|---|
| M-A the port exposed native private-construction types and `ItemJob` to Execution; fakes could not reach stages C, D or Launch | Medium | The port owns the item. `OpenPeer`/`BeginItem` return opaque interfaces, and the stage machine sees values only. The Execution reference ban covers every native type (§2, §5.4) |
| M-B `ChocolateyCompiledCommand` did not fit the builders; unvalidated ID | Medium | Sealed hierarchy with private constructors and validating factories. New pinned `ChocolateyExactListArguments.Build(operation)` with ID, client-target and route checks, golden against the protocol-1 literal (§4) |
| M-C unbounded settle after a normal exit; stale messages | Medium | 10 s settle window, then teardown with `LingeringTerminated`; `ActiveProcesses` is the authority; per-launch jobs and ports; disclosure (§4.1) |
| L-A launch floor placement; lapses in B, C or at the start of D | Low | Floor moved to the Launch row; every pre-launch budget lapse → `Undetermined`+`timedOut`; the observer is bounded by `remaining` (§4, §5.2) |
| L-B "volume root" wording | Low | Lexical drive root throughout (§2.2, §6) |
| L-C binding could launch straight after B | Low | One-use launch permission minted only after a stage-C match and a stage-D `Observed`; out-of-order calls refuse (§5.2, §5.4) |
| L-D `NotStarted` after `CreateProcessW` success | Low | Recorded as an integrating interpretation of the rev 3 Launch row (§4.1) |
| Derived-token test requirements | — | Shape asserted through `TokenFacts`; explicit fixture owners (§8) |

