# Task 50 — cloud handover to Claude Opus 5.5

Prepared 2026-10-31. This is a continuation handover, not a completion report or a fresh review. The owner paused Task 50 before requesting this document. Creating/transferring this packet does not resume implementation; the receiving agent starts only when the owner tells it to resume. The owner's choice of Opus 5.5 supersedes the original task prompt's model preference for that continuation.

## 1. Establish the correct starting state

- Repository: `https://github.com/DanDG923/pc-steward`; local branch `phase/5-v2-planning`.
- Last committed HEAD: **`44ff0cd45e33e1ba0376bc2275ea591a1071318b`**, `feat: pin Chocolatey mapping data and postcheck ordering`.
- At handover, this is **22 commits ahead of the locally recorded** `origin/phase/5-v2-planning` (`478016d907d31d12d82fd9dc3987599ad091cceb`). No fetch was performed: that remote-tracking observation may be stale. A GitHub checkout alone is not evidence that you have the handover state.
- The portable packet includes a self-contained Git bundle of this branch and an overlay of the pending files. Clone the bundle, then apply the overlay; do not discard the overlay to obtain a green tree.
- **Committed HEAD passed; the overlaid working tree is deliberately RED.** The owner decision documentation and tests are ahead of production implementation. No implementation/build/test/consultation/commit was resumed to prepare this handover.

Read `AGENTS.md`, this document, `docs/review/TASK_50_PAUSED_HANDOFF.md`, `docs/STATUS.md`, `prompts/50_REVIEWED_INSTALLER_MODE.md`, `docs/requirements/V2_PHASE_C.md`, `docs/requirements/V2_TASK_CONTRACT.md`, and the relevant ADR-025 sections. Read the full specification only for a cited requirement or conflict. No root CONTEXT files were found in the tracked file inventory.

**Task:** enforce immutable reviewed installer mode and a closed supported provider matrix through review, digest, live approval, claim, runner/helper and history. Preserve constrained Chocolatey Update scope. Requirement references: `FR-UPD-15; FR-UPD-01; AC-UPD-01–11; TM-03–04/07/13`; existing remediation also covers retained `FR-UPD-08/09/14` and `TM-06`. Stop at Task 50; Tasks 51+, 53A and 54A/54B retain their own instruction and gates.

## 2. Pending files and immediate continuation

Pending before this handover was written:

| File | State |
|---|---|
| `docs/adr/ADR-025-TYPED-OPERATION-JOBS-AND-HELPER.md` | Modified: accepted route-specific runtime exception |
| `docs/requirements/V2_PLANNING_DECISIONS.md` | Modified: same owner decision |
| `docs/requirements/V2_TASK_CONTRACT.md` | Modified: same owner decision |
| `docs/review/TASK_90_TASK_50_CHOCOLATEY_PROFILE_V2_REMEDIATION.md` | Modified: decision, proposed producer/consumer interfaces |
| `tests/PCSteward.Application.IntegrationTests/ChocolateyReviewedUpdateArgumentsTests.cs` | Modified: new route/disclosure expectations |
| `tests/PCSteward.Domain.UnitTests/ReviewedProviderGuardContractTests.cs` | New: canonical identity and mutation negatives |
| `docs/review/TASK_50_PAUSED_HANDOFF.md` | New: pause checkpoint |

This handover is an additional new file. The packet's `state.json` records exact pending file hashes. Current compile failures are expected missing production symbols, not a diagnosed SDK problem:

- `UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3`;
- `ReviewedProviderGuardContract` and `ReviewedUpdateOperation.ProviderGuardContract`;
- `ReviewedUpdateProviderDisclosure.GetRiskText`.

After an explicit resume instruction, the next bounded slice is:

1. Implement the already documented new route and derived closed guard-contract enum: `LegacyUnqualified`, `RequiredEffectiveControls`, `CommunityScriptRuntimeLimits`. Append serialized enum values rather than reinterpreting old ones. The new route gets the runtime-limit label; the older provisional route retains its stronger effective-control meaning.
2. Both provisional/new runtime routes carry `UserCacheIntent`, restricted at construction/restore to current schema 3 and exact Chocolatey/Community/Machine shape. Schema 2 cannot acquire the new meaning. Changed canonical route changes the digest; digest integrity never grants approval. Historical unresolved claims still block.
3. Switch the **passive** Chocolatey argument formatter to the new runtime-limit route only. Reject the older provisional route; retain the fixed official source and safety arguments.
4. Add the application disclosure seam with the risks and feed limits below. Before relying on the new test, freeze its `TimeProvider` at the operation's `ReviewedAt`; otherwise its `Confirm()` refusal may come from age/future time rather than unavailable admission. Add an explicit older-provisional disclosure-negative fixture.
5. Keep confirmation/native dispatch closed. Run focused tests, then canonical Release/full checks; reconcile stale pending-decision wording in STATUS, TRACEABILITY and `docs/failures/task-50-chocolatey-refresh-guard.md`; review scope; commit only green.

The full Task 50 integration below remains necessary after this slice. Do not present the new label/disclosure as native eligibility.

## 3. Owner decisions — settled, do not ask again

### Population and common gate

The owner chose **“Single protected deployed build; legacy standalone coexistence unsupported”**. Supported mutating callers need the common current-user gate across logon sessions. This limits the supported population; it does not technically exclude an old standalone binary. It grants no deployment permission.

### Provisional pre-UAC Chocolatey candidate

The owner explicitly allowed provisional review:

> Allow provisional review (Recommended) — the helper must qualify the exact reviewed candidate or refuse without updating.

For the new Chocolatey route, the desktop's signed-client `outdated` candidate may be **UserCacheIntent**, because the non-elevated client's HTTP cache is user-writable. That is an explicit amendment to ADR-025's former trusted-before-UAC candidate duty, already recorded in committed `ba3cd83`.

Trusted pre-UAC client/profile, installed registration and live explicit Community mapping remain required. Stored mappings are untrusted SQLite data: trust comes from displaying/binding the exact Community ID in the live review. Local registration evidence must use direct protected reads or the pinned root engine with the compiled environment/root, never inherited `ChocolateyInstall`.

The helper independently resolves the exact installed instance/candidate and validates its protected machine inputs. It must match the frozen reviewed tuple or refuse without updating, with no retargeting. Commit the claim before UAC. A preflight refusal is settled only when all preflight work settles.

### Community runtime limitations

After being told explicitly about refresh-triggered checksum bypass, installer-argument overrides and the TEMP replacement window, the owner answered:

> I think Chocolatey is fine as long it uses the official community repo

Main restated the interpretation and Opus/Fable/Astra acknowledged it: **accept the disclosed runtime limitations for the new Chocolatey route only, conditional on the fixed official Community feed**. This is a security-contract exception, not a claim the risks are prevented. The uncommitted ADR/planning/shared-contract edits preserve it; older STATUS/failure wording is stale.

Required meaning and remaining controls:

- All **remote target/dependency package resolution/downloads** use compiled `https://community.chocolatey.org/api/v2/`. Existing locally registered dependencies may be used; their historical origin can be unknown. Package scripts can fetch vendor installers/contact other endpoints. There is no network or script sandbox.
- Observe remembered arguments effectively OFF and pass fixed `--ignore-remembered-arguments`, so a remembered source cannot violate that condition. Keep `checksumFiles=true`, fixed `--require-checksums`, `--fail-on-not-installed`, the genuine protected initial client/verifier, and all remaining profile/cache/source checks.
- Disclose three distinct mechanisms: ordinary script-triggered command/module name binding; refresh restoring user-writable TEMP and a checksum-to-execution replacement race; refresh importing same-user environment values that disable checksums or override installer arguments. Initial environment pinning does not persist through an in-script refresh.
- Keep arbitrary flags, environment, source, URLs and paths out of serialized authority. The exception applies only through the new canonical route/guard contract; legacy or older provisional reviews cannot inherit it.
- Common gate, immutable live consent, durable claim, independent helper qualification, settlement and file-first postchecking remain required. No deployment, host read, UAC or real update is authorized.

“Helper qualified” in UI/history must be derived from authenticated correlated observations and desktop verification, never trusted because a serialized label or helper success boolean says so. Keep what the user saw as intent, including refused items.

## 4. Implemented prerequisites versus open composition

| Area | Implemented at committed HEAD | Still open |
|---|---|---|
| Domain/review | Closed mode union; canonical schema 3; architecture union; live single-use review/approval; 30-minute original review lifetime | New runtime route/guard label is RED; truthful UI composition and actual native capability admission |
| Persistence | Draft, exact canonical claim CAS, terminal capacity reservation, migrations 9–12, orphan-claim reconciliation | Compose into all runners and UI/lifecycle paths |
| Gate | Global SID-qualified gate, same-thread ownership, async owner-thread lease, abandoned recovery, legacy dual-lock session | Desktop bootstrap/MainWindow/FactoryReset and other mutating callers still need conversion |
| ScheduledHost | Common gate acquired before marker/state/SQLite; reconciliation; hold through scan; barrier exit 78 | Runtime host behavior not proven; full cross-session caller population integration |
| Protocol | Passive strict protocol-2 codec and facts validator; schema 2 passive readability | Active helper/transport still protocol 1; reviewed response-fact extension and endpoint integration missing |
| WinGet | Passive fixed mode/MSIX formatter, bounded structured source guard/query producer | Native implementation/CLI provenance and guarded execution; no route admission |
| Chocolatey | Parsers, version/store model, archive reader, compiled environment, release policy, fixed arguments, enabled-source resolution, fingerprint, tree coverage and file-first postcheck policy | Native observers/binding, independent qualification, helper/runners, UI/history and end-to-end synthetic tests |

Most new mapping/policy seams have **no production caller**. ScheduledHost is a concrete gate conversion, not proof that all callers share it. The active protocol-1 endpoint has not gained the new mode or exception semantics.

### Gate, claims and lifecycle requirements

Use `src/PCSteward.Storage/CurrentUserOperationGate.cs`, `CurrentUserDataOperationLease.cs`, application `OperationOwnership`, `ReviewedUpdateExecutionWorkflow` and `UpdateDispatchReconciliation` as the existing seams. Inspect `src/PCSteward.Storage/FactoryReset.cs` (legacy `DataOperationLease`), `src/PCSteward.Desktop/App.xaml.cs` if present, and **`src/PCSteward.Desktop/MainWindow.cs`**; do not assume a MainWindow.xaml.cs. Numerous desktop leases and reset paths remain old.

Preserve ordering: common gate → marker/preflight and reconciliation as appropriate → draft/live canonical comparison and fresh dispatch observation → one-use consume/claim CAS → durable commit → provider/helper/UAC. Never hold the gate across the UI review wait. Recheck original approval/deadline immediately before launch. Native ownership context must become inactive before the owning thread releases the kernel mutex.

A committed valid reset marker is authority to finish fixed-manifest reset before SQLite, even if the database is missing/corrupt. Without that marker, unreadable state cannot prove no unresolved claims. Reset Arm and mutating scan/clear/execute paths fail closed on the barrier; read-only UI stays useful. Unclaimed drafts belong to scan data; claimed/terminal rows are history; factory reset removes all under its separate authority.

`DispatchClaimed`/`Uncertain` remain barriers, including legacy schema-2 rows. Persisted bytes never recreate approval. Reserve terminal capacity inside the claim transaction. No retry/replay after lost responses, crashes or a spent live claim attempt.

### Wire and aggregate settlement

`src/PCSteward.Execution/ReviewedUpdateWireCodec.cs` is passive: 16 KiB frames, 4 KiB domain payload, 2 KiB printable-ASCII diagnostic; exact field order/encoding, UTC-Z and canonical Base64; whole-frame re-encoding comparison. Hash the **received canonical payload bytes** for helper digest checks. Do not turn parsed rows into authority.

Wire facts distinguish invocation `Unknown`/`NotStarted`/`Started`, exit observations, owned I/O/EOF, termination, cancellation/time/output flags and aggregate settlement. No wire `Success`, `Final` or canonical `HelperQualified` assertion. Unknown invocation always remains uncertain. Provider zero exit is not verified update success.

Aggregate ownership includes every preflight query, scan, provider process, callback/I/O and teardown. An unresolved preflight prevents upgrade. Roots can remain pending forever: bounded teardown may produce unsettled facts, not an unbounded await or an early settled response. The desktop needs valid authentication/correlation, positively settled facts, its own helper-exit observation within existing budgets and applicable post-verification before final classification. Killing is never settlement proof. Negative settled refusals/failures and verified updates are different outcomes.

Before endpoint composition, document and independently review the exact bounded fact extension for the helper's installed/candidate/profile/guard observations. Existing Update deadlines remain unchanged. Historical machine-helper 15-minute versus desktop 11-minute timeout inconsistency is recorded, not permission to redesign Task 51 here.

## 5. Chocolatey profile work to finish

Start with `docs/review/TASK_90_TASK_50_CHOCOLATEY_PROFILE_V2_REMEDIATION.md`, then the historical V2 profile and referenced source ledger as needed. Preserve pinned historical V1/V2 reports; put corrected mapping in a new revision/remediation record rather than rewriting what a reviewer saw.

Native composition must independently establish the protected root engine/artifacts, token/ACL/file identity and ancestry, complete library/store/config/licence/alias/residue/environment/cache coverage under bounds. Pure parsed facts, hashes and paths do not themselves form native trust. Define the producing/consuming binding interfaces before callers.

Key constraints already established:

- Launch protected canonical **root `choco.exe`**, not `bin/choco.exe`. The bin shim is untraced prebuilt forwarding code. Re-point signer/hash/path checks; do not inherit trust from the old shim route.
- The release manifest pins loaded root/helpers/tools files and required native tools. Refuse missing/changed/extra loading code rather than silently using PATH fallbacks. Config and administrator hooks/extensions have their own observed rules.
- Effective feature value is absent/default or `setExplicitly ? enabled : default`. Keep raw presence/Enabled/SetExplicitly separately. The host feature key is **`powershellHost`**. Strict parser rejects duplicate/unknown relied-on schema content tied to the admitted client version.
- Require configured `cacheLocation` non-empty, absolute, no `%` expansion, protected and reparse-free. Default user TEMP refuses. Create/harden no third-party cache directory.
- Elevated HTTP cache is **`%ProgramData%/ChocolateyHttpCache`**, outside the chocolatey root. Require it already exists, is readable, not caller-owned/modifiable across its entire subtree and has no reparse points **before any elevated client command**. Native cache-lockdown would otherwise create/purge/harden; the route must refuse first. `--ignore-http-cache` does not prevent future-dated planted cache entries being read.
- Desktop candidate HTTP cache is user-writable: intent only. Helper cache/profile qualification is independent. Compare desktop/helper common profile only over elevation-independent protected facts; validate helper-specific HTTP cache/token/environment separately.
- Library enumeration: root plus one directory level, top-level `*.nupkg`, bounded embedded nuspec identity. Fail on any access/archive/bound error, never skip. Require V2 discovery and exactly one target identity/file; native list/outdated output alone hides duplicate registrations. Normalized versions must equal review/list/outdated/native library identity as applicable.
- Preserve native store alternatives and ambiguity refusal, pins/arguments/version/sxs checks, nested/top-level pending markers, and `lib-bkp` target-prefix residue hazard. Optional hooks/extensions/license/lib-bkp/lib-bad may be absent only with protected parent/observed absence. Writable regular log contents are excluded from loading-code immutability; log directory identity remains protected.
- Enabled source aliases follow native key-before-name resolution. A disabled URL key cannot hide an enabled malicious name; a name-only redirection refuses. Matching official-source proxy/certificate options are disclosed transport settings.
- Compiled environment replaces inherited environment and pins root/cache/native tools at launch. Script-triggered refresh limitations are the accepted exception, not an initial bootstrap exception. No static script scan is a sound absence proof.
- Licensed-assembly absence must cover the evidenced CLR/GAC/config/appbase/recursive extension loader inputs. Unsupported loader settings refuse. The unsupported generic CLR-registry hypothesis was withdrawn; do not recreate hypothetical blockers without reachability evidence.
- Dependencies can update the client/extensions/hooks. **First postcheck is direct file-only** release/code/self-client registration comparison, before executing any client for target verification. Changed/unverifiable means ClientChanged/Unknown, never Success; no next item with the old binding. Install's dependency prohibition is not imported into Update.
- Licensed extension dependency/feed reachability remains unresolved. A claim it is on Community lacked an adopted primary citation. Admission-time absence is not whole-run absence: later PowerShell runspaces may load newly arrived licensed code. Postcheck detects change, not retroactive prevention.

Useful source files: `ChocolateyCompiledEnvironment`, `ChocolateyConfigurationParser`, `ChocolateyNativeVersion`, `ChocolateyNativePackageId`, `ChocolateyNuspecIdentityReader`, `ChocolateyPackageArchiveIdentityReader`, `ChocolateyClientReleasePolicy`, `ChocolateyReviewedUpdateArguments`, `ChocolateySourceResolution`, `ChocolateyCommonProfileFingerprint`, `ChocolateyTreeCoveragePolicy`, `ChocolateyPostUpdateClientCheck` under `src/PCSteward.Provider.Chocolatey/`.

## 6. WinGet disposition and scope boundaries

Keep WinGet/winget/User Selected(Silent) **unavailable pending a guarded existing-only execution path**. Pinned MSIX CLI upgrade uses AddPackage* add-or-update semantics; composite installed correlation has a weak fallback. A removed reviewed PFN can lead to a fresh install despite an upgrade request. Prechecks, no-sibling checks and postchecks are defence in depth, not the missing execution guard. Fable initially accepted a same-user residual; main adopted the stricter ADR-025 interpretation. The Chocolatey owner exceptions do not apply to WinGet.

Native COM evidence versus CLI execution identity/provenance also remains open. Projection version is not the active engine identity. Research proposed an owned class-method/module self-check against the protected bundled manifest and exact trusted engine-byte equality with the registered App Installer engine, not equal version numbers. This is deployment-integrity/installed-client verification, not a new security boundary. No host equality or COM implementation was observed.

Suspended native creation with exact registered package full-name/image inspection before one resume is an agreed **unimplemented** seam. It must reject unpackaged/unknown/mismatched context, establish ownership before inspection, and terminate/observe refusals within existing budgets. Do not assume direct versioned-path launch receives package identity. It remains lower priority while Update-only is blocked.

No COM executor adoption here: Tasks 54A/54B own that investigation/adoption and may have separate dependency approval. `UpdatePackageAsync` with a URI is a separate deployment/provider path, not permission to import installer URLs. WinGet machine and Store remain unavailable. A passive positive fake-adapter case never establishes host eligibility.

## 7. Validation, evidence and cloud constraints

At committed `44ff0cd`, the prior Windows run reported:

- Focused **63/63** over formatter, source resolution, fingerprint and postcheck/coverage classes.
- Canonical Release build: exit 0, zero warnings/errors.
- Full **900/900**, zero skipped: Domain 139 / Application 633 / Architecture 31 / Security 97.
- Evidence recorded in the V2 remediation report; local TEMP logs are not portable packet evidence. These checks were **not rerun for this documentation handover**, and do not describe the overlaid RED tree.

`global.json` pins SDK **10.0.403**. The previous machine used its existing approved SDK at `%LOCALAPPDATA%/Microsoft/dotnet` on the process-only PATH. PowerShell 7 was absent, so existing Windows PowerShell executed the unchanged `scripts/Build.ps1` and `scripts/Test.ps1` equivalents. Canonical documented commands remain `pwsh -NoProfile -File ./scripts/Build.ps1` and `.../Test.ps1`; inspect the scripts and installed environment before choosing an equivalent.

Cloud may be Linux or lack Windows/SDK/restore assets. Establish that honestly first. Do not install SDKs/dependencies merely to force the design to work, change platform targets, skip mandatory tests, or replace Windows-native trust checks with permissive stubs to obtain a pass. Cloud-compatible pure fixtures may be partial evidence; Windows build/full/native gates remain open until run on an authorized suitable environment. New package/spike dependencies retain their separate approval rules. Never imply this handover approved owner-host probes or provider execution.

Relevant existing review trail:

- `TASK_90_TASK_50_PREREQUISITE_REMEDIATION.md`: codec, schema/migration, bounded source guard repairs.
- `TASK_90_TASK_50_CHOCOLATEY_MAPPING_REMEDIATION.md`: pinned primary-source dialogue and mapping development.
- `TASK_90_TASK_50_CHOCOLATEY_PROFILE_V2_REMEDIATION.md`: latest CONDITIONAL profile review and implemented corrections.
- `docs/failures/task-50-pre-uac-candidate-evidence.md`: first accepted amendment.
- `docs/failures/task-50-chocolatey-refresh-guard.md`: exact refresh chain and older pending decision; reconcile using the accepted amendment, not a new question.

New integrated work requires a fresh independent review. Follow AGENTS' iterative consultation for a concrete stuck/capability conflict: Opus 5.5, then Fable 5.1 and Astra 6 together with Opus. Prior council messages are not cloud-accessible authority; durable repository evidence is the handoff. If required consultation models/tools are unavailable, record the exact unavailability and request the smallest necessary resolution; do not silently substitute models or treat isolated opinions as the completed dialogue. No new agents/worktrees are authorized merely by this handover.

## 8. Pinned research artifacts and unavailable local material

Primary source pins:

- choco 2.7.5: `194ac6262ac75571c44aa0a431c3ab6f36fa73be`.
- Chocolatey/NuGet.Client 3.5.0: `d08e5cc246b384fd424e99371168d7cf4534723e`.
- WinGet research: `v1.10.340`; use the exact blobs in the source ledgers.

Public release artifact: `https://github.com/chocolatey/choco/releases/download/2.7.5/chocolatey.2.7.5.nupkg`, 5,719,618 bytes, SHA-256 **`F86F99A18D236C2F99E6F67A20BE7ADC65C6C7756BF6CF3B43759E7EC0AA5839`**. Its root engine is 12,022,880 bytes, SHA-256 **`4AB5785C91D6CB3CC0E9890BCA29077F7D6BAAC0DFC935DBE2DA37047ED761C0`**. The manifest is `docs/review/TASK_50_CHOCOLATEY_RELEASE_2_7_5.json`: 56 mapped loaded files. Byte inspection of that public ZIP was research only, not extraction/install/execution. Release digest does not prove reproducible source-to-binary provenance.

The local downloaded nupkg, PowerShell golden-vector scratch script, test logs, machine SDK/cache, local Git configuration, application databases/settings and previous chat/tool stores are **not in this packet**. Do not claim to have inspected them. Referenced source/archive pins can be researched within the existing bounded read-only research authority; never execute the downloaded artifact.

## 9. Exit bar and authority

Continue toward full integrated positive/negative fake-adapter coverage: unsupported/mutated mode/provider/scope/architecture/approval/capability and recomputed-digest rejection; old/new protocol mismatch; exact candidate refusal; cancellation/crash/expired review; preflight/provider/helper unresolved work; barriers across lifecycle/reset; truthful review/history and verified outcomes. A closed matrix of unfinished routes is not a substitute for implementing the required constrained Chocolatey capability.

The native profile, remaining common-gate callers, claim/runner/helper protocol composition, UI/history and fresh integrated review are open. Task 50 is not nearly complete merely because 900 existing tests passed. Previous rough remaining estimate was 12–24 hours of focused agent work with substantial uncertainty, not a deadline or an instruction to cut scope.

No push, merge, tag, release, signing, installation, deployment, real provider/UAC/update, registry/cache/ACL modification, owner inventory read or next-task start is authorized. Preserve the current data/safety boundaries. Keep Git identity/global configuration unchanged. On a resumed implementation, follow focused → canonical build/full → STATUS/TRACEABILITY → scoped diff → green local commit, subject to the receiving environment's available identity/authority. Return the repository's concise completion format and explicitly identify any unrun gates.
