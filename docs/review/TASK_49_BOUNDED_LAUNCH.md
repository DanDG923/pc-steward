# Task 49 — Bounded owner-observed launch

Date: 2026-10-30. **Process/normal close/data preservation PASS; normal Overview OWNER-ATTESTED. Step 3 automated capture UNAVAILABLE / NOT MET. No unqualified launch Pass.** This is the one approved bounded startup check, not full UI/accessibility, real update/helper-UAC/progress/cancellation, Task 53/54 or distribution acceptance. Requirements addressed: `FR-UPD-14`, retained `FR-UPD-01–11`, `FR-UI-01–07`, `NFR-OBS-01–05`, preservation `TM-04/TM-06`, only to the explicitly measured scope below. Task 50 remains unstarted.

## Authority, exact artifact and one invocation

Owner replied **Approved** to [the one non-elevated 60-second Overview/normal-close plan](TASK_49_RECOVERY_EXECUTION.md). The protected replacement remained at source `2a13fe5a2a67770485d5751f54fb30a417707ad7`, manifest `6873DFA36B6CBDBD04CDE66306B757E1A231DB2B8F5AD0FD689FD058266D5E46`, completed ledger `AC1BF9755A03C42705ED69777B035445ECBCAD04E172A3715F2212E3408080CB`, same-signer apphosts `14F34EFEFA84C367F25917B4B28803DCECE85623`. Desktop EXE `18C0C7385447870B8C4EF087EAD2B8C299C75C9646FD9FD4CFB22EB522A1E44A`; DLL `DFEE7A9D552CBD78F96D48DB5BA5DF23B7BFC377AD973CC9F26FA359BB630E5A`.

The prepared observer's captured script block initially stopped at `Join-Path $PSScriptRoot` because script-block invocation provides no script-directory value. This happened **before creation of any launch claim/evidence directory and before Start-Process**; no app started. The fixed literal evidence path resolved it within preparation. Final local observer `artifacts/task-49-host-preparation/Invoke-Task49Launch.ps1` SHA-256 `96AC3AF2A6A85BD200B7476F5325B7C8DD73AE8693A8EA47F93F9B04C1E55641` was hash-checked as captured bytes and invoked. Source/data/backup/installed validator/executable/ledger and non-elevated owner/no-process checks passed.

Exactly **one** `Start-Process -FilePath 'C:\Program Files\PC Steward\PCSteward.Desktop.exe' -WorkingDirectory 'C:\Program Files\PC Steward' -PassThru`, no arguments/RunAs, created **PID 5652** at **2026-10-30T20:24:07.8616081Z**. The observer recorded a nonzero main window titled `PC Steward` from **1,097 ms**. All **74 sampled process Responding values were true**, including two pre-window samples; 72 samples had the named window. Process metrics do not prove visible content or interaction latency.

## Observation-tool failure and normal close reconciliation

Main read the current computer-use SKILL, guidance, API and confirmation policy, initialized `@oai/sky` through node_repl, then `sky.list_windows()` failed:

```text
Computer Use native pipe is unavailable: failed to connect native pipe: The system cannot find the file specified. (os error 2)
```

The documented two-second same-call retry failed identically; kernel reset/import/retry also failed. No helper executable was searched for/spawned, undocumented protocol used, ad-hoc UIA/capture substituted, or system/security setting changed. No screenshot/accessibility/agent-observed Overview is claimed. The missing native host remains an environment capability limit, not an established app defect.

To honor the already approved bounded normal close, main verified PID 5652's exact executable path and called **CloseMainWindow once** from a separate process-lifecycle command. It returned **true**, recorded at `2026-10-30T20:24:45.5866748Z`. The observer then captured natural **native exit 0**, no residual PCSteward process, end `20:24:45.8695243Z`, elapsed **38,007 ms**, within 60 seconds. No forced termination, second launch or automatic retry occurred.

The original observer's `closeMainWindowAccepted=false` does **not** record a failed close call. Its local variable stayed false because it calls close only after its missing visual-observation signal; that branch never ran. The actual close happened in the separate approved command, whose `normalCloseAccepted=true` record is retained. Both immutable records and `close-reconciliation.json` identify the two writers and timeline. There was one actual close request. The original observer deliberately remains `FailedOrUnknown/overviewObserved=false`; do not rewrite that automated result into a visual pass.

## Owner's direct visual report

The asynchronous question was:

> During the brief launch that just closed, did you see the normal PC Steward Overview with its main navigation and no error or setup dialog?

The owner replied verbatim:

> Yes, normal Overview appeared

`owner-overview-attestation.json` records this as **owner-attested** evidence for this run, separately from unavailable automated capture. This confirms what the owner saw; it does not create an agent screenshot, test every navigation destination, prove update progress/helper UAC, or close broad FR-UI/accessibility/phase criteria. No further launch is needed merely to repeat this bounded owner observation. No retrospective assumption is made from the window title alone.

## Post-close preservation and installed validation

After normal exit and zero processes, the live data root contained only `pcsteward.db` plus its existing empty downloads directory; no WAL/SHM. A unique `post-close-database-copy/pcsteward.db` was copied from stable read handles, comparing source/copy hashes, held path and source LastWriteTimeUtc. A short-lived isolated PowerShell reader used the hash-checked native SQLite library and fixed read-only helper on the **copy only**. No live database was opened by SQLite verification and no installed DLL was loaded into a waiting long-lived caller.

| Check | Result |
|---|---|
| Live and copied DB SHA-256 | `CEB917F40ED7B5101E2219501B00B852F9B424683EFA33E142FECA94694F6B99`, unchanged from pre-launch/backup |
| Integrity / foreign keys | `ok` / zero violations |
| Schema | 1,2,3,4,5,6,7,8, unchanged |
| Non-migration tables | All empty; no new scan/source/update/approval/history/settings content |
| Original and copied inputs after read-only queries | Unchanged hashes |
| Independent pinned-deployment validator | Pass; expected new manifest/signer/complete resources/ACLs/reparse/provenance |
| Protected ledger | Unchanged `AC1BF9755A03C42705ED69777B035445ECBCAD04E172A3715F2212E3408080CB` |
| Residual PCSteward process | Zero |

No scan, source refresh, connector consent/settings change, schedule registration, clear/reset, app update/install/uninstall, helper/provider execution, package/runtime acquisition, security-policy action, publication/push/merge or Task 50 occurred. No new production source or canonical script changed; preceding Release zero-warning/error/415-test and wrapper fixture evidence remains historical applicable evidence, not rerun product tests for this host-only check.

Evidence under ignored `artifacts/task-49-launch-result/`: attempt/running/process/samples, observation-error, close result/reconciliation, owner attestation, compact summary, post-close preservation and retained copied database. Process record SHA-256 `C21254C31E522AECC8D46A3FC002CF4E1C667AA3A23787AB89EC0D2A438221F7`; close record `EAF079917BA1F2C1AC71B11309BCECD79234E913A05096D1901472E7EC1E40AE`; post-close preservation record `7AC63C02B8E2474CDEEAA80D3B393E4B31DE0CE5E40A1B6168F5DA3A0FC3D824`. `git diff --check` is part of documentation closure.

## Consultation and handoff

Opus reviewed the failed automated observation and process evidence, agreed process and visual claims must be separate, and requested reconciliation of the close records and returned preservation/owner evidence. Main returned the explicit two-writer close branch/timestamps, isolated copy integrity result and the owner's actual reply. Main challenged scope expansion into fixing an unavailable helper for a future unapproved launch: the current one-shot process test is done and owner observation supplies a separately labelled bounded visual claim.

Opus then read the compact reconciliation/attestation artifacts and observer branch, confirmed there is no close-record discrepancy and accepted the scope distinction. Its objections are preserved: automated capture in approved step 3 remains **unavailable/not met**; the owner report reconciles the missing observation but is not reproducible screenshot/accessibility evidence, navigation testing or an assertion that input actions worked. `Responding=true` is a non-hung Windows process sample only. Original UTF-16/BOM close/reconciliation records and UTF-8 attestation are retained without rewriting.

**Consultation outcome:** an evidence-backed reporting solution exists for this one launch. Opus agreed the Fable/Astra escalation trigger (main and Opus still cannot find a solution to the scoped objective) is not met. This is application of the standing condition, not a waiver. The unavailable native pipe remains a declared environment limit with no repair claimed.

**Re-trigger:** if a later owner-approved test requires agent-visible observation and the pipe remains unavailable, restart the full consultation protocol and applicable escalation. Do not assume owner attestation replaces automated evidence for real update-progress/helper-UAC or any future test without that test's explicit scope/approval. No new host action follows from consultation.

The safe next action is an explicit owner-scoped decision about remaining real update/helper-UAC/progress checks or Task 50. Existing approval authorizes neither a fresh scan/source refresh nor another launch/update. No new approval is requested solely to recreate the already owner-observed Overview.
