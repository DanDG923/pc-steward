# Task 48 — Owner-only deployment preparation

**Current outcome: Task 48 complete.** The owner-approved pinned redeployment and later separately approved responsive launch/normal close pass, with copied fresh-database integrity/hash/schema and final protected validation. See [completion](TASK_48_COMPLETION.md) and [deployment evidence](TASK_48_PINNED_REDEPLOY_EXECUTION.md). Earlier mismatch/preparation/proposed-route/pending-launch statements below are historical. The original unsigned candidate is installed exactly apart from validated signatures; no further host action or Task 49 follows.

Date: 2026-10-29. Status: **Approved installation performed; independent protection validator passes; exact candidate desktop DLL/PDB comparison fails. STOP before launch/retry.**

Requirements: `FR-DAT-12–15`, `AC-DAT-18`, `FRA-14–16`. This task preserves the retained data/deletion boundaries; it does not close distribution gates or demonstrate new 2.0 capabilities.

## Approved execution and stopped result

The owner replied **“Approved”** to the concrete fresh-install/UAC/14-day temporary-signer plan, with launch explicitly separate. Action-time preflight revalidated HEAD, all 188 input hashes, all 102 unsigned candidate files/path set, pinned scripts, SDK 10.0.403, ScheduledHost RID prerequisite, all three absent roots and no PCSteward processes. One hidden elevated Windows PowerShell wrapper then invoked the unchanged ADR-022 installer once under the same Windows SID. The wrapper only rechecked the approved prerequisites and captured installer stdout/stderr/result; it did not add deployment/removal authority. Its interface/evidence is local ignored `artifacts/task-48-deployment/Invoke-ApprovedDeployment.ps1` consuming the existing installer and producing the logs below.

The installer ran from `2026-10-29T17:36:45Z` to approximately `17:37:15Z`, reported `validation: Pass`, and left a completed v6 ledger. Installer stderr is empty. The wrapper's process object yielded a **null exitCode**, so installer exit 0 is **not** claimed; this capture limitation was not used to retry. The independent validator ran afterward with a directly captured **exit 0 / Pass**. It verifies the exact protected root/ledger identity, complete installed hash manifest, ACLs/ownership/inheritance, reparse absence, required resources and valid same-signer desktop/helper executables.

**The stricter approved candidate comparison failed and stopped the run.** The complete path set matches all 102 candidate files. Of 100 files that are not the two signed executables, 98 hashes match, including the helper assembly. Two differ:

| File | Approved candidate SHA-256 | Installed SHA-256 |
|---|---|---|
| `PCSteward.Desktop.dll` | `12C1B111B9DD4832C5D0E49BB554A8766CC2B8795071A547102CEDB51450185D` | `DD6F7D245EC176D6E67073958C95FC87BBAC9F77A2EA73F52A819BBF2C9F12B7` |
| `PCSteward.Desktop.pdb` | `96B6B8C862FF42BDD626ADB7C26E59C7B415294123A6C4A04FB93C1659C4F2CC` | `7D3EFD6E11171073C0C048A0D141A7A05467AF64D00C385D9B8173D02DBB91BD` |

No input/source hash changed. Building the WinUI desktop in a different protected staging/output directory is a possible cause, **not a demonstrated explanation or a waiver**. A limited read-only PDB marker check did not establish that cause. There was no launch, installer retry, removal, rollback, source refresh, app update or further certificate mutation after detection. See [failure record](../failures/task-48.md).

| Final observed evidence | Result |
|---|---|
| Installed root / state | `C:\Program Files\PC Steward` / `C:\Program Files\PC Steward OwnerOnlyTest State`; retained, completed ledger; staging removed by installer |
| Independent protected validator | **Pass, exit 0** |
| Exact approved-candidate gate | **FAIL — desktop DLL/PDB hashes above** |
| Installed manifest SHA-256 | `39A08C3396E23868DD894A581072E6FC2847F1488DFF09E7B912495D92D16F70` |
| Protected ledger SHA-256 | `05884590A90B1242B8963F01F3A9044963029C68F27F82757CD7E2FD43394585` |
| Installed helper DLL SHA-256 | `9FC90C8A4FCF9D695BAB3ADD1B5FE6B0AF8028D97EB4F409105C011139954D7D` — matches candidate |
| Both executable signatures | **Valid**, same signer `61FC6E8B6F34488247EB799B54456BD1EC671B1C` |
| Temporary signer expiry | `2026-11-12T17:36:46Z`; installer-owned CurrentUser My/Root/TrustedPublisher entries retained |
| Per-user data/database | **Preserved absence** of `%LOCALAPPDATA%\PCSteward`; no database created or copied |
| Installing identity / source drift | Same SID; all 188 input hashes unchanged |
| PCSteward processes / launch | Zero; not launched |

Local ignored evidence: `artifacts/task-48-deployment/authority-preflight.json`, `installer-started.json`, `installer-process.json`, `elevation-process.json`, `installer-stdout.log`, `installer-stderr.log`, `installer-result.json`, `independent-validator-stdout.log`, `independent-validator-stderr.log`, `independent-validator-result.json`, `installed-manifest.json`, `candidate-drift.json`, `deployment-ledger.json`, `post-install-verification.json`. Raw identity/ledger information stays local; the tables above are the sanitized report.

**Task 48 remains incomplete.** The installation exists and meets its protected-deployment validator, but cannot be accepted as the approved exact candidate. Resolve the reproducibility/artifact mismatch before any new deployment/launch decision. Retaining the installed roots/signer avoids an unapproved recovery action; deliberate removal requires its separate exact approval. No Task 49 start or distribution gate closure follows. The remaining preparation sections below are the historical pre-approval record; this execution result supersedes their pending-deployment and absent-install statements.

## Opus 5.5 consultation and proposed solution

The owner's subsequent instruction **“if you run to issues, consult with Opus 5.5 to find a solution”** authorized a fresh independent consultation. The completed consultation is [linked here](<t3-thread://v2/3aa39081-bc20-4540-91c1-09a75cb81a1a/thread:delegated-task:command%3Amcp%3A44413dc4-6ec3-4436-8ed8-ca3a80680c53%3Adelegate-task%3Atask48-opus55-artifact-diagnosis-20261029>). It performed source/metadata/PDB inspection and created two isolated offline metadata tools under ignored `artifacts/task-48-opus-consultation/`; no existing source/installed file, certificate, launch, build/reinstall or cleanup was performed by the consultant.

**Proven:** both DLLs embed the same PDB path; staging-path metadata is not the cause. The installed DLL equals shared repository `obj` output. The PDB has 23 source documents per binary; 20 match, and all three differing hashes belong to CsWinRT's AOT generator. Metadata differences are confined to its generated vtable classes/name/reference ordering, with the same assembly references and type/method/field counts. The parent's read-only rerun of both tools returned exit 0 and independently confirmed these points and unchanged retained installation/data absence. The underlying cross-compiler-process generator nondeterminism mechanism remains unproven, not a reason to waive byte identity.

Consultation pins: `FINDINGS.md` SHA-256 `5D88B6143F08A05E8CA1A22E3288DF46F163D5F320B3CDB2AB46EED3CDE68FF6`; `dll-diff.txt` `D3F52D3A276015C5C50FD1E7B5BE06D9E63FC4730165B499DBDE0F13D91A808E`; `pdb-diff.txt` `1E69DE12ACE914710F03160BE33B698A7597F78C99E4F213658B7677E84DC9FD`. Parent evidence is `artifacts/task-48-deployment/parent-metadata-diff.txt` and `parent-pdb-diff.txt`.

**Recommended solution:** remove compiler/build/publish execution from the privileged installer; consume the pinned reviewed unsigned candidate, verify it in protected staging, then sign/install with full provenance and signed-image identity checks. The [concrete fix proposal](TASK_48_PINNED_PAYLOAD_FIX_PROPOSAL.md) defines interfaces, transaction order, negative fixtures, review, exit capture and later host boundaries. ADR-022 has a clearly **Proposed** amendment; its Accepted original route is unchanged. Task 48's prompt requires accepted decisions and the existing guarded route, so owner acceptance is required before changing that route. No production script/code has been changed; no new removal/deployment/launch authority is inferred.

## Authority and current-host discrepancy (historical preparation)

The owner instructed **“Start task 48”**. This authorizes inspection, ordinary local build/test/payload preparation and this reviewable plan. The task prompt, Phase B and exact-host contract explicitly retain separate deployment and launch authority. No elevated installer/remover, certificate mutation, app/provider launch, live source refresh, application update, data deletion, schedule mutation, push or release has been performed.

Current checks found all three canonical roots absent:

- `C:\Program Files\PC Steward`
- `C:\Program Files\PC Steward OwnerOnlyTest State` (including `deployment.json`)
- `%USERPROFILE%\AppData\Local\PCSteward` (including `pcsteward.db` and sidecars)

No `PCSteward*` process was running. The state path is confirmed by `FactoryReset.GetStateRoot()` and desktop composition. The historical Task 39 installation/database evidence is not current-host evidence. No alternative installation, profile, machine or backup has been searched for or presumed. This plan is therefore a **fresh protected installation on this machine**, not a demonstrated replacement of the historical installation. If the owner intended another existing installation, resolve that target before action.

The existing read-only validator was invoked through Windows PowerShell. It stopped with **“No owner-only protected PC Steward test deployment is present.”** The enclosing preparation command exited 1. This is an open deployment gate, not a passing signer/ACL/hash check. No removal is needed or authorized by this plan.

## Exact implementation and snapshot

| Identity | Value |
|---|---|
| Task 42 implementation | `52b43130130b25a850f43f1fe478d112c3ce9cb7` |
| Current HEAD / branch | `cd6d4530ac4e8c1e31a07b1c9096ffd358e75342` / `phase/5-v2-planning` |
| Runtime-source changes since Task 42 | No C# changes; the SQLite project restoration is the only `src/` difference |
| Build recovery | Committed SDK 10.0.403 selection, restored SQLite project and narrow ignore exception from Task 45 |
| Planning successor | Existing Task 46/47 Markdown working-tree changes and owner acceptance retained; no runtime feature inferred from them |
| Pre-task repository snapshot | 700 entries, `artifacts/task-48-preparation/pre-task-source-snapshot.json` |
| Snapshot SHA-256 | `128335309F99485E37165AAFB56CF2F463A8644FFABB4E28F1CB34A206F40E3D` |
| Build/input inspection manifest | 188 entries, `artifacts/task-48-preparation/build-input-manifest.json` |
| Input manifest SHA-256 | `632B9052C4586752E9555E1D68A0EB8C9E1940C1BF5AEB72C08130C04D0EE52E` |

The snapshot pins pre-existing uncommitted planning changes before this task's report/STATUS/TRACEABILITY edits. It is not an independent review. Task 47's report retains the two earlier independent review snapshots and the disclosed owner-accepted author follow-ups; no new independent reviewer was authorized. Recomputed input hashes after preparation show zero drift. No production source, project, script, dependency, installer, validator or interface/schema change was made in Task 48.

## Local verification and packaging prerequisite

Environment: native Windows x64, OS build `10.0.26300.0`; Windows PowerShell 5.1 fallback because `pwsh` is absent. The approved `%LOCALAPPDATA%\Microsoft\dotnet` directory was prepended to the command-only PATH; `dotnet --version` returned `10.0.403`. No SDK/package installation or PATH/Git configuration change was performed. Normal project restore during RID builds/publish reused the existing declared dependency graph; no PC Steward/provider source refresh was run.

| Command / scenario | Result |
|---|---|
| `dotnet test tests/PCSteward.Application.IntegrationTests/PCSteward.Application.IntegrationTests.csproj --configuration Release --no-build --filter 'FullyQualifiedName~AsymmetricTrustedSourceConsentLoadsFailClosed\|FullyQualifiedName~ShortMalformedUpdateOutputCannotProduceALiveSnapshot'` | Exit 0; 2/2, zero skipped |
| `powershell.exe -NoProfile -File .\scripts\Build.ps1` | Exit 0; Release, 0 warnings/errors |
| `powershell.exe -NoProfile -File .\scripts\Test.ps1` | Exit 0; 290/290, zero skipped: Domain 59, Application 181, Architecture 28, Security 22 |
| Initial desktop RID/resource build with `--output` | Exit 1: `PCSteward.Desktop.csproj(40,5): error : Required RID-specific PCSteward.ScheduledHost.dll is missing.` |
| `dotnet build src/PCSteward.ScheduledHost/PCSteward.ScheduledHost.csproj --configuration Release --runtime win-x64 --no-restore` | Exit 0; prerequisite output created, 0 warnings/errors |
| Same desktop RID/resource build after that prerequisite | Exit 0; 0 warnings/errors |
| Desktop/helper `dotnet publish`, Release/win-x64/framework-dependent | Both exit 0 |

The failure is an output-location prerequisite: the desktop's `CopyScheduledHostRuntimeAfterBuild` target reads the ScheduledHost project's normal RID output directory, while the installer-style `--output` build directs referenced output to staging. This host initially had only non-RID ScheduledHost output. A separate normal RID build supplied the expected output; the same resource build then passed. The existing guarded installer remains unchanged. Revalidate that prerequisite immediately before any approved installation; a clean checkout must not be assumed to have it.

Logs are in ignored `artifacts/task-48-preparation/`: `focused-tests.log`, `build.log`, `full-tests.log`, `winui-build.log`, `scheduled-host-prebuild.log`, `winui-build-prepared.log`, `desktop-publish.log`, `helper-publish.log`. The validator's stderr was surfaced by the enclosing shell; its redirected log is empty and is not claimed as a captured validator transcript. Two local summary attempts hit PowerShell collection/`Measure-Object` errors after payload creation; calculating totals directly from files resolved that bookkeeping issue without changing payloads.

## Reviewable unsigned candidate

Local directory: `artifacts/task-48-preparation/unsigned-candidate/`. It was assembled using the installer's order: desktop publish, resource-build `App.xbf`/PRI/Resources, then helper publish. Required desktop/helper executables, ScheduledHost assembly, PRI/XBF resources and reparse-point absence were checked.

| Evidence | Value |
|---|---|
| Files / bytes | 102 / 50,051,220 |
| Compiled XBF files | 6 |
| Product version | `0.1.0-private-beta+cd6d4530ac4e8c1e31a07b1c9096ffd358e75342` |
| Complete manifest | `artifacts/task-48-preparation/unsigned-candidate-manifest.json` |
| Manifest SHA-256 | `43F209AD1E79E5B2A251F29D723059F419789E44FF72671D68D5C7C7A0B3B7AE` |
| Desktop assembly SHA-256 | `12C1B111B9DD4832C5D0E49BB554A8766CC2B8795071A547102CEDB51450185D` |
| Helper assembly SHA-256 | `9FC90C8A4FCF9D695BAB3ADD1B5FE6B0AF8028D97EB4F409105C011139954D7D` |
| Desktop/helper Authenticode | Both `NotSigned`; expected for preparation, not deployable trust evidence |

ADR-022 rebuilds into protected staging and signs the two executables during installation; it cannot directly consume this unsigned directory. An approved run must bind the unchanged inputs/SDK to this candidate, compare the entire resulting path set and every non-signed file hash, separately validate both signed executables/same signer, and capture the final complete signed manifest. Signing changes executable hashes, so their final hashes/thumbprint do not yet exist and cannot honestly be pinned now. Any other payload drift stops the run. Existing protocol/update capabilities remain unchanged; no install/uninstall/diagnostic/AI capability is added by deployment.

## Preservation and rollback plan

There is currently **no live database to copy, hash or integrity-check**. No empty replacement database or fabricated preservation evidence was created. The precondition is continued absence of the exact per-user state root immediately before deployment, and the postcondition is continued absence after deployment (before launch). The installer does not create that data root. If a database/root appears, stop for a new reviewed plan: close through normal user action, snapshot database plus sidecars consistently into a unique ignored local backup, hash originals/copies, and run SQLite integrity checks on the copy only. Do not proceed on mismatch or partial/locked storage.

Because the protected roots are absent, there are no old installed binaries or signer to preserve/restore. No previous deployment rollback is claimed. ADR-022's installer performs its own bounded failure cleanup of transaction-owned roots/certificate entries and retains an incomplete protected ledger if cleanup fails. That documented installer behavior must be included in deployment approval. A later deliberate return to the initial absent-install state would require separate explicit elevated approval for `Remove-OwnerOnlyProtectedTestDeployment.ps1 -Remove`; it preserves per-user data. No manual recursive deletion, old-data restoration, alternative root or automatic retry is authorized.

## Concrete proposed action-time plan — not yet approved

1. Confirm that **this current machine**, with absent protected installation/state/database, is the intended target. Revalidate source/input/candidate/script hashes, SDK 10.0.403, no PCSteward processes and all three absent roots. Drift stops before elevation. Preserve the ignored local snapshot/candidate/evidence. No launch is included.
2. Verify the normal RID ScheduledHost prerequisite above. Use the existing ADR-022 installer once, from the pinned checkout, in a hidden elevated Windows PowerShell process as the same installing Windows user, with command-only SDK PATH and log capture: `powershell.exe -NoProfile -File .\scripts\Install-OwnerOnlyProtectedTestDeployment.ps1 -Install -CreateOwnerOnlyCertificate`. Approval must explicitly cover UAC, protected staging under `C:\Program Files\PC Steward OwnerOnlyTest State`, installation at `C:\Program Files\PC Steward`, and the script's transaction-owned failure cleanup.
3. The script creates a 14-day non-exportable CurrentUser code-signing certificate and adds its public certificate to CurrentUser Root/TrustedPublisher. The generated unique subject/thumbprint must be recorded from its protected ledger; only script-owned entries are eligible for failure cleanup. No existing certificate or trust store is to be broadly modified.
4. On installer completion, run the independent read-only `powershell.exe -NoProfile -File .\scripts\Test-OwnerOnlyProtectedTestDeployment.ps1`. Capture actual exit/output. Require completed v6 ledger for this SID, exact roots, complete manifest, administrator/SYSTEM write ownership, Users read/execute, no reparse points, trusted same-signer executable signatures and required resources. Independently compare full candidate file set/non-signed hashes, desktop/helper assembly hashes, and continued per-user-root absence. Signer/ACL/hash/preservation failure stops; do not launch or retry.
5. Allow at most 15 minutes for the single deployment attempt; if the observation deadline expires, inspect the existing process/ledger once and report pending/unknown. Do not kill the process tree, launch a second installer or remove roots automatically. A normal installer failure follows only its own declared cleanup/ledger behavior; further recovery requires a separately reviewed approval.
6. After successful installed evidence, request separate approval to launch only `C:\Program Files\PC Steward\PCSteward.Desktop.exe`, observe window responsiveness for at most 60 seconds and close normally. Review launch-time first-run/migration/data/connector effects first; do not assume startup is side-effect-free. No source refresh, scan, update, reset or other UI action is included in the future responsiveness test. Failed launch/normal-close leaves the result failed/unknown; no forced termination or retry is implied.

Pinned script SHA-256 values:

- Installer: `E45D3309175FCDA60E9CCB91F2E6C05B5B132ED33897C3F378A0EEEFFFE7A253`
- Independent validator: `45365930AC383AF32A6156EBCD78C7DEA39480C63A047CD579289798D7FF2B53`
- Separately authorized removal/recovery: `3703F0CA5CB837C4E784E1BB42D18ABAA013CFAA75F9A3A1B5FFF5C6E81F0B67`

## Open gates and handoff

Final preparation checks passed: complete candidate path set plus all 102 file hashes; zero unexpected changes across the pre-task snapshot (only STATUS/TRACEABILITY deliberately updated); protected/per-user roots still absent and no PCSteward process; unchanged installer/validator/remover parse successfully; touched documentation is strict UTF-8; `git diff --check` passes. Runtime-source/project/script/dependency diff against pre-task HEAD is empty. Windows PowerShell version was verified as `5.1.26100.9711`. These checks validate preparation only and do not substitute for installed signer/ACL validation or launch evidence.

- Owner confirmation/approval of this exact fresh-install target, candidate and protected signer/deployment plan is pending. If the absent installation/database is unexpected, stop and clarify the intended machine/profile instead.
- Existing installed validation cannot pass until an approved installation exists. Final signed manifest, signer/ACL checks and installed assembly comparison remain uncollected.
- Database backup/integrity evidence is inapplicable to the currently absent database; preservation must instead prove continued absence, or stop for a revised plan if the baseline changes.
- Launch authority, launch effects review and responsive normal-close evidence remain pending separately.
- No independent new source review, public distribution gate, production signing/channel gate or live provider update evidence is supplied by preparation. Do not start Task 49.
- No commit: Git user name/email remain unconfigured; prior task-specific identity authorization was not assumed for this task, and no Git identity/configuration was changed. All pre-existing Task 46/47 changes remain intact.
