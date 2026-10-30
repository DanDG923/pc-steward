# Task 49 — Stopped owner-approved replacement

**Resolution: separately approved recovery PASS at 2026-10-30T20:18:14Z.** [Execution evidence](../review/TASK_49_RECOVERY_EXECUTION.md) records one native/protected exit 0, independent complete deployment validation, all 102 files including missing resources restored, signer replacement and unchanged database/backup. The app remains closed. The partial/current-stop statements below are retained historical evidence; the next action is a separately approved bounded launch, not another recovery.

**Current state, 2026-10-30 20:08 UTC: second separately approved attempt stopped during removal; installation is PARTIAL, not launchable.** Eight resource files were removed before deletion of `e_sqlite3.dll` failed. The remaining 94 files match the original ledger; database/backup and old ledger hashes are unchanged; old signer remains. No new candidate installed, no app launched, no retry. New Opus dialogue is in progress for this distinct failure. Earlier unchanged-installation statements below describe attempt 1 only.

Date: 2026-10-30. Requirements: `FR-UPD-14`, retained `FR-UPD-01–11`, `TM-04/TM-06`. **One approved RunAs attempt stopped, native exit 1; no retry.** Opus 5.5 consultation is in progress. This record does not declare the task blocked before consultation.

## Authority and exact attempt

Owner replied **Approved** to the guarded replacement in [host-check plan](../review/TASK_49_HOST_CHECK_PLAN.md). App launch/scan/source refresh/update were excluded. Source HEAD `2a13fe5a2a67770485d5751f54fb30a417707ad7`. Prepared caller SHA-256 `BCD152E3F11AE0A57EB80928B08F8E4DEA5C7492E1B1920C4D25297D5632E8AF`; encoded wrapper SHA-256 `C3C54F184021ECE9CF92F609D1DE6CD7EF5661B62855E5610A238407E64C4402`, 25,652 encoded characters. Preflight revalidated candidate, six code pins/ACLs, old deployment, backup integrity and live database identity before dispatch. Caller bytes were verified and executed from the captured buffer with `-Execute`.

Caller: `artifacts/task-49-host-preparation/Invoke-ApprovedReplacement.ps1`. Its single hidden `Start-Process ... -Verb RunAs` launched the Windows PowerShell encoded wrapper, then `Wait-OwnerOnlyDeploymentProcess -TimeoutMilliseconds 300000` returned native **1**. Result-record completion time: `2026-10-30T14:14:00.8626068Z`. No protected result was produced. Parent error:

```text
Get-Acl : Cannot find path 'C:\Program Files\PC Steward OwnerOnlyTest Execution 2a3df1cb2e13455285979c597fe609c8\task49-replacement-result-20261030.json' because it does not exist.
```

The elevated stderr was not captured; no exact child exception or UAC visual/prompt-count claim is made. The caller reports `StoppedOrUnknown`, `attempt:1`, `retry:false`. Never delete/reuse that claim to run again.

## Observed preservation after stop

Independent `scripts/Test-OwnerOnlyProtectedTestDeployment.ps1 -RequirePinnedPayload`: **Pass**, original Task 48 manifest `43F209AD1E79E5B2A251F29D723059F419789E44FF72671D68D5C7C7A0B3B7AE`. Old protected ledger remains exactly `E29ACFECE08CF8E53658C257939FF40BE5D974076C9EF987A7DA94644C135A20`. Old signing certificate `49D695BA9CA2DEA922659699E5F40710F2B5B264` remains present. Database remains exactly `CEB917F40ED7B5101E2219501B00B852F9B424683EFA33E142FECA94694F6B99`. Zero PCSteward processes. No removal or candidate installation is observed. Existing evidence, signer, candidate and backup retained.

## Diagnosis and attempted approaches

1. Check-only initially rejected the oversized encoded wrapper; no elevation occurred. Compact JSON and embedding only the unchanged ACL/path/environment assertion functions reduced it below the fixed command bound. Check-only then passed and the above single attempt ran.
2. Inspection found PowerShell 5.1 serialized the decorated `ConvertFrom-Json` array as `pins:{value:[...],Count:6}`. The emitted wrapper assumes `pins` is an array; its first `foreach` entry has no `path` or `sha256`. This is before the elevated try/result block. An initial offline Join-Path-only check surprisingly succeeds with null ChildPath, resolving to the scripts directory. Running the exact next read-only path/ACL assertions reproduces **`Execution infrastructure ACL has unexpected authority.`**: a directory with inheritable rules is being checked as a leaf file. This explains a deterministic early rejection, without executing privileged code. The absent error result is explained by initialization outside the try block; actual child stderr was not captured, so no observation of its exact first exception is claimed.

Proposed correction: reconstruct plain per-pin records into a fresh array, enforce a full JSON roundtrip/schema check before elevation, and cover initialization with bounded protected failure evidence once root trust is established. Exercise positive and adversarial plan fixtures offline. Do not treat a parser-only check as semantic validation. Do not execute a corrected elevated attempt under the exhausted one-shot approval.

Changed files: only local ignored caller/wrapper/preflight/attempt/diagnosis artifacts and task documentation. No production source, canonical scripts or installation/data files were edited by preparation. Existing Release zero-warning/error and 415/415 test evidence applies to unchanged product source; it did not test this generated orchestration wrapper.

## Consultation and next decision

Opus 5.5 received the scoped objective, requirement IDs, exact artifact/script/source pins, failing wrapper/caller, observed preservation and proposed correction for an open dialogue. Its first response independently reproduced the exact read-only path/ACL rejection and serialization behavior. It challenged the original correction in these ways:

- Require the schema guard inside the elevated wrapper as well as whole-plan roundtrip comparison in the caller.
- An early initialization failure cannot use the protected writer before the integrity module loads. Use distinct native phase codes instead of introducing a second privileged writer.
- Record native exit and missing-result presence before attempting result ACL reads.
- Preserve the original one-shot claim byte-for-byte; prepare a new claim and present exact corrected code pins for new approval.
- Parse and exercise the actual emitted bytes and extracted guards/loop in a fresh PowerShell 5.1 process. Keep compiler ACL/elevated compilation unproven until a separately approved host run.

Main accepted these objections. No protected or canonical script was changed. Corrected preparation is entirely under ignored `artifacts/task-49-host-preparation/`:

| Artifact | SHA-256 / evidence |
|---|---|
| `Invoke-CorrectedReplacement.ps1`, final result-ACL correction | `15EC26098D0E8D0F66CE7F515AF5E64348A9479B70A2DB3A46F611986AD57A72` |
| `replacement-v2-wrapper.ps1` | `A28C2B57590F1BAADEC631C29777FB34DDF2B2F437422D993A1B245B66EE9831` |
| Original `approved-replacement-attempt.json`, preserved | `B8B16DD68C56FC7C6528251E1090D89DF1BECDBA630805FFF4FF94D2644C1CE5`; corrected caller verifies this hash |
| Corrected check-only preparation | Pass; same 102-file manifest, old ledger/signer, database/backup and six unchanged protected scripts; encoded command 29,212 characters |
| Original serializer regression harness | 9/9 offline scenarios |
| `powershell.exe -NoProfile -File artifacts/task-49-host-preparation/Test-CorrectedWrapper.ps1` | Exit 0; **19/19**, fresh Windows PowerShell 5.1 |

The 19 scenarios parse actual emitted bytes; verify the six roundtripped entries against protected files; run the actual AST-extracted closed guard and hash/ACL loop; require only plan/function definitions/outer try/exit at top level; invoke the exact generated wrapper **non-elevated**, observing **exit 21**, no result/second-attempt claim and unchanged database; reject old wrapped shape, five/seven pins, duplicate/traversing/separated paths, short/lowercase hash, extra fields and a valid-length hash mutation. A one-byte mutation changes the approved wrapper digest. Synthetic database/ledger/signer comparisons reject drift; these are comparator checks, not actual host-drift execution. All executable wrapper initialization is under an outer try; failure phases 21–29 preserve native diagnosis even before the existing writer is loaded. No second privileged writer was added.

Main returned these implementation pins, exact validation and objections to overclaiming the original child exception/compiler ACL/host-drift coverage to Opus in a second consultation round. Opus accepted those limits but identified a second concrete caller defect: `Assert-OwnerOnlyExecutionAcl` requires four ACEs including OWNER RIGHTS, while `Write-OwnerOnlyProtectedJson` creates three ACEs (SYSTEM/Administrators full, Users RX + Synchronize). Thus the caller would reject a successful protected result. Opus reproduced this against the existing protected result files; its objection prevented an avoidable second host failure. The earlier corrected caller `7B8EC2CA6ABAB47F73B876A6C7CC0F8F6834FE18BB60D22BB262594CDAB5C399` is superseded and was never elevated.

Main implemented an exact three-entry result ACL check in the caller only; privileged code still uses its unchanged four-entry execution ACL. The new result check requires normal resolved path, protected Administrators ownership, exactly the three explicit Allow leaf rules with exact rights, and no inherited/extra/Deny authority. The result is then read through a held stable handle. Failed classification retains available trusted protected exit code/phase/failure alongside native exit and caller error. The wrapper bytes and six protected scripts remain unchanged. Latest check-only preflight passes.

`powershell.exe -NoProfile -File artifacts/task-49-host-preparation/Test-ResultAcl.ps1`: **exit 0, 15/15**. Tests extract actual final caller functions; four existing protected writer results pass; old bootstrap four-ACE result, extra ACE, missing Users, Users Write, unprotected, wrong owner, Deny and inherited rules reject. The original incorrect checker rejects existing writer output (regression); AST inspection verifies the actual success path calls the new result checker. The exact wrapper bytes were also run through the real **EncodedCommand**, non-elevated: **exit 21**, unchanged database, no second-attempt claim. Only local test evidence files were written. This closes the earlier File-versus-EncodedCommand validation gap. No additional product build/test was needed after caller-only changes.

Main returned the final caller pin, unchanged wrapper pin and all 15 results to Opus in a third dialogue round. **Consultation resolved for corrected preparation:** Opus independently reran the result-ACL positive/negative cases in memory, confirmed final caller/wrapper hashes and the actual success-path checker, and found no concrete contradiction preventing a new one-attempt approval request. It agreed that timeout leaves the result unread and no retry, and that protected failure details are retained only after trustworthy parsing. It did not independently rerun the EncodedCommand step; that remains main-agent evidence. This dialogue is not independent phase-review acceptance or host authorization. Fable/Astra escalation is not needed because main/Opus reached a tested correction.

Remaining limits: no observed original child stderr; compiler ACL/elevated compilation remain host-only; baseline-drift tests are synthetic comparisons. Result ACL verification precedes stable-handle acquisition under the existing protected root/owner-session trust assumptions. `WriteProtectedFile` uses OpenOrCreate, while both caller/wrapper require new result/claim paths; no result reuse is authorized.

## Prepared new approval boundary

Subject to completed consultation, a new owner approval may authorize **one new guarded replacement attempt only**, using the same exact candidate and effects in the host-check plan. Pin source `2a13fe5a2a67770485d5751f54fb30a417707ad7`, caller and wrapper hashes above, and candidate manifest `6873DFA36B6CBDBD04CDE66306B757E1A231DB2B8F5AD0FD689FD058266D5E46`. The protected six-script bundle remains unchanged. The caller uses a fresh `replacement-v2-attempt.json` created with CreateNew and protected `task49-replacement-result-v2-20261030.json`; it refuses existing claims/results. Preflight drift stops before elevation. Old data and execution/compiler/evidence roots remain preserved; the old deployment and installer-owned certificate are removed, then the same pinned candidate is installed with a fresh fourteen-day signer, independently validated and compared with the backup. The five-minute observation/no retry/no force-kill/manual recovery boundary remains unchanged. No launch, scan, refresh or application update is included.

At that preparation checkpoint, no corrected elevated invocation had occurred; the separately approved attempt 2 is recorded next. Host checks remained incomplete and launch/update authority remained separate.

## Second separately approved execution — removal stopped

The owner again replied **Approved** to the corrected one-attempt replacement. At source `2a13fe5a2a67770485d5751f54fb30a417707ad7`, main rechecked the final caller/wrapper/original-claim hashes above, read the caller as bytes, verified its SHA-256 and invoked the captured script block with `-Execute`. All caller preflight checks ran before its sole hidden `Start-Process ... -Verb RunAs`. This was the one new approved attempt, not an automatic retry of attempt 1.

At `2026-10-30T20:07:54.3391417Z`, `replacement-v2-attempt.json` recorded **native exit 27**, protected exit **27**, phase **approved-removal**, failure **`Access to the path 'e_sqlite3.dll' is denied.`**, retry false. The protected `task49-replacement-result-v2-20261030.json` exists and was read successfully through the corrected ACL/held-handle path. Installation was never reached. No retry, rollback, restoration or cleanup was attempted.

Read-only state snapshot `artifacts/task-49-host-preparation/stopped-removal-state.json` records:

- 94 of the original 102 files remain, each matching its original ledger hash. Complete retained manifest `remaining-old-payload.json` SHA-256 **`94336C03FBE30464FF3AF74035471F470BB38150593F6F9934C33750A0CA856A`**.
- Missing: `App.xbf`, `Assets/logo-mark-64.png`, `Assets/pc-steward.ico`, and the five `Resources/*.xbf` files (ColorTokens, ControlStyles, DataTemplates, ThemeResources, Typography).
- Ledger unchanged: `E29ACFECE08CF8E53658C257939FF40BE5D974076C9EF987A7DA94644C135A20`; old signer still present; desktop signature reports Valid. **A valid executable signature is not a valid complete installation.**
- Database and backup remain `CEB917F40ED7B5101E2219501B00B852F9B424683EFA33E142FECA94694F6B99`; zero PCSteward processes. No full-deployment validator pass is claimed for this partial tree.

Main's evidence-backed suspected cause: caller lines 61–63 load `Task48ReadOnlySqlite` and perform backup integrity queries through an absolute P/Invoke binding to the **installed** `C:\Program Files\PC Steward\e_sqlite3.dll`. The caller remains alive awaiting the elevated child while removal tries to delete that image. SQLite connections close, but the native DLL module remains loaded until the caller process exits. The enclosing tool PowerShell process has now exited; do not retry on that fact alone. The backup check unintentionally created a removal-time file lock. Actual module ownership at the instant of failure was not captured.

The unchanged remover validates the protected ledger/SID/allowlisted roots and every retained descendant's ACL/reparse state; it does not require the complete old file manifest before removal. This is the existing retry-safe partial-deployment recovery route, not authority to run it now. Main is consulting Opus about isolating backup SQLite checks in a short-lived child which exits before elevation, pinning the exact partial tree, and a separately approved recovery/removal plus the same pinned installation. No canonical script/product change or waiver is proposed. Return implementation/fixture results to Opus before any new final approval request.

## Recovery dialogue and returned evidence

Opus confirmed the caller-loaded native image mechanism from source and deletion pattern. It also confirmed that a plain retry of the old wrapper would reject the missing resource files, while the canonical remover already supports a partial protected tree. Main accepted replacing the old complete-deployment preflight with an exact pinned partial-tree check, preserving both original attempt records, and avoiding SQLite loading in the waiting caller.

Main returned a synthetic copied-DLL test that reproduces the exact access-denied failure and eight missing resource files while a child holds the image; its non-writing write-open probe rejects then succeeds after normal child exit, and removal likewise succeeds only after exit. A separate isolated integrity child proves no installed module remains loaded in the waiting parent. Recovery itself uses hash equivalence to the same previously integrity-checked backup/live bytes, with no SQLite load.

Main challenged Opus's proposed all-process module enumeration: protected OS processes cannot be exhaustively inspected, and broad OS process/module reads would add unnecessary scope. The proposed alternative checks only the caller's own module list and uses the actual fixture-proven non-writing write-open probe on each exact remaining DLL/EXE in the elevated pre-removal phase. This does not guarantee against a new external lock after probing. No process is forced closed or killed.

The [concrete recovery plan](../review/TASK_49_PARTIAL_RECOVERY_PLAN.md) pins caller `F81AA70AEA1BAFE6EFEE44BC91AC90105B9FC551B0635B8BCE8CDAA98E12105B`, wrapper `EC1D81B3282933B7E9C2D6A1473382597750A5744116A4D4A6FFDBE9DA715001`, launcher `ACB9A56F0F70C53C495C64FB2371F25A8542A3E332FDBCEADEB71F57B34EF6D8`, partial manifest, original claims and the unchanged candidate/database/source. The fixed encoded launcher losslessly expands a local gzip literal to the pinned wrapper, proven byte-for-byte; this stays within the command-line bound without executing mutable privileged file contents. The encoded command is 17,312 characters.

Returned validation: check-only recovery Pass with caller module absence; synthetic lock/removal reproduction Pass; actual recovery/partial functions **11/11**; actual schema/wrapper **19/19**; exact compressed launcher non-elevated **exit 21**, database unchanged and no recovery attempt claim. No protected/canonical/product source changes, no recovery elevation, and no full old-install validator pass is claimed.

Opus accepted main's objection to global process enumeration and withdrew that proposal. It asked for two small probe corrections: cover all 94 files (not only DLL/EXE) and attempt combined WriteData/Delete rights to catch deny-delete handles as well as loaded images. Main implemented both locally and reran the fixtures: loaded-image and deny-delete handles reject; after normal holder exit the probe passes without changing hashes or LastWriteTimeUtc. Final 11/11 recovery, 19/19 wrapper, exact launcher byte expansion and encoded non-elevated exit 21 all pass with the final pins above. No new elevated action occurred.

Main returned final validation to the same Opus dialogue. Opus acknowledged that its stated conditions are met on main's report and no unresolved contradiction prevents a new owner recovery request. It did not independently inspect/hash the final small edits; no independent review or host/phase acceptance is claimed. It reminded main to pin the changed launcher: final launcher `ACB9A56F0F70C53C495C64FB2371F25A8542A3E332FDBCEADEB71F57B34EF6D8` is included in the recovery approval record and machine-readable byte-check JSON alongside caller/wrapper hashes. Fable/Astra escalation is not required for this resolved dialogue. A new external lock after probing can still produce a new partial tree; that would require fresh evidence and owner approval, never an automatic retry.

## Later bounded launch — observation helper unavailable

After successful separately approved recovery, the owner separately approved one non-elevated 60-second Overview/normal-close check. [Bounded launch evidence](../review/TASK_49_BOUNDED_LAUNCH.md) records the exact one launch/38,007 ms/native exit 0/data and deployment preservation plus the owner's verbatim normal-Overview report. The final observer is `Invoke-Task49Launch.ps1` SHA-256 `96AC3AF2A6A85BD200B7476F5325B7C8DD73AE8693A8EA47F93F9B04C1E55641`; its initial script-directory bookkeeping failure occurred before any launch/claim and was fixed with a literal evidence path.

Supported `@oai/sky` initialization succeeded, but node_repl `sky.list_windows()` failed with **Computer Use native pipe is unavailable: failed to connect native pipe: The system cannot find the file specified. (os error 2)**. A two-second retry and the documented reset/import/retry returned the same failure. No helper was searched for/spawned or alternative capture protocol improvised. Main used the already approved normal process-close method once on exact PID/path; the observer later saw exit 0. Separate close and observer records were reconciled without altering the originals: the observer never took its visual-marker close branch, whereas the separate actual CloseMainWindow returned true.

An Opus dialogue examined the actual artifacts and source branches, received copied-database integrity results and the owner's actual report, and accepted a scoped reporting solution: process/close/data Pass, normal Overview owner-attested, automated visual capture **Unavailable/not met**, original FailedOrUnknown retained. Opus agreed no Fable/Astra escalation is triggered for this current reporting objective because main/Opus found an evidence-backed solution; no standing rule is waived. Missing native pipe is an unfixed environment capability limit. If a future explicitly approved test depends on agent-visible evidence while the gap persists, restart the full consultation/escalation protocol. Owner attestation must not be silently substituted for future progress/helper-UAC evidence. No new launch or host action is authorized by this record.
