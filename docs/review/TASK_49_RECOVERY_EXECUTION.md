# Task 49 — Approved recovery execution and separate launch plan

**Subsequent launch result:** The owner approved the bounded run below. [Current launch evidence](TASK_49_BOUNDED_LAUNCH.md) records one normal exit 0 in 38,007 ms and preserved data/deployment, with normal Overview owner-attested. Automated capture was unavailable/not met; no unqualified visual or UI-gate Pass is claimed. Earlier app-unlaunched/launch-approval-pending language below is the deployment-time record.

Date: 2026-10-30. **Recovery/deployment PASS. App/helper/update host checks remain open.** Requirements: `FR-UPD-14`, retained `FR-UPD-01–11`, `FR-UI-01–07`, `NFR-OBS-01–05`, preservation `TM-04/TM-06`. Task 50 remains unstarted. This record supersedes the partial-installation/current-recovery-pending handoff; earlier failures and their evidence remain preserved.

## Exact approved execution

The owner replied **Approved** to the [exact partial-recovery plan](TASK_49_PARTIAL_RECOVERY_PLAN.md). Source HEAD stayed `2a13fe5a2a67770485d5751f54fb30a417707ad7`. Main captured caller bytes and verified caller `F81AA70AEA1BAFE6EFEE44BC91AC90105B9FC551B0635B8BCE8CDAA98E12105B`, raw wrapper `EC1D81B3282933B7E9C2D6A1473382597750A5744116A4D4A6FFDBE9DA715001` and encoded launcher source `ACB9A56F0F70C53C495C64FB2371F25A8542A3E332FDBCEADEB71F57B34EF6D8` before invoking the captured caller with `-Execute`.

The caller rechecked its exact protected six-script/code pins, original attempt records, partial 94-file state, signatures/ACLs/reparse protections, new candidate and unchanged database/backup; it loaded no installed SQLite library. It created one fresh v3 claim and started one hidden RunAs PowerShell. The wrapper independently checked same SID/elevation/code/compiler environment and exact state, passed non-writing WriteData/Delete probes on all 94 remaining files, called the unchanged guarded remover once, then the unchanged pinned installer once. No retry or forced process closure occurred. This was the separately approved third total wrapper invocation, with one invocation under the recovery approval.

Caller record: **attempt 3, Validated, native exit 0**, wrapper PID 36836, completed **2026-10-30T20:18:14.2116862Z**, launched false, retry false. Protected final result also records phase validated/exit 0 and the approved manifest/database. The execution proves that the same-owner elevated identity/environment and compiler policy gates passed; no visual UAC prompt count, compiler-descendant audit, or runtime-helper UAC behavior is inferred.

## Independent post-install evidence

Main separately ran the unchanged protected `Test-OwnerOnlyProtectedTestDeployment.ps1 -RequirePinnedPayload` non-elevated: **Pass**, exactly `pinned-unsigned-v1`. It verifies complete file hashes/provenance, required resources, exact DACL/owner/reparse checks, trusted same-signer apphosts and signed-image reconstruction. A separate full path/size/hash comparison confirmed all 100 non-signed files exactly match the approved payload.

| Identity/evidence | Result |
|---|---|
| Installed root | `C:\Program Files\PC Steward` |
| Approved unsigned manifest | `6873DFA36B6CBDBD04CDE66306B757E1A231DB2B8F5AD0FD689FD058266D5E46` |
| Payload | **102 files**, **100 non-signed exact matches**, both signed images verified; **six XBF resources** |
| New protected ledger | `AC1BF9755A03C42705ED69777B035445ECBCAD04E172A3715F2212E3408080CB` |
| New full signed manifest | `0379C8DF8FE4E223BCBFC7E4AF3B3A64C190B64F6630EC5CDA2AFA364962E57E` |
| Desktop EXE | `18C0C7385447870B8C4EF087EAD2B8C299C75C9646FD9FD4CFB22EB522A1E44A` |
| Desktop DLL | `DFEE7A9D552CBD78F96D48DB5BA5DF23B7BFC377AD973CC9F26FA359BB630E5A` |
| Helper EXE | `7017C5EFF4C7D84434E471E2B56FACBB6576855FE09069B3F7051267B0B890BD` |
| Helper DLL | `EB52970C84D4E9FB3D0D27BA9C6985E773A4B97AB35F236015B7BEE720DFC88C` |
| New same signer | `14F34EFEFA84C367F25917B4B28803DCECE85623`, both signatures Valid; expires **2026-11-13T20:18:09Z** |
| Certificate stores | New signer present in CurrentUser My/Root/TrustedPublisher; old `49D695BA9CA2DEA922659699E5F40710F2B5B264` absent from all three |
| Database and backup | Unchanged **159,744 bytes**, SHA-256 `CEB917F40ED7B5101E2219501B00B852F9B424683EFA33E142FECA94694F6B99`; no WAL/SHM |
| Residual runtime processes | **Zero PCSteward processes**; app/helper/provider were not launched |
| Protected final result | `0A0D6E9C69E1B083C695B3C13935DC64C6934F26DC84A4B3C9197323CC2E7145` |
| Caller execution record | `456664B9BE6651CBC79FEBF30F69B561863F2A4C5FEB35FAE51187B5D8F534D5` |
| Main verification summary | `9648791E19A644EF3C99A94EF0862C27C9B62AB2C9D93E5E15426DF33AAD9F77` |

Protected originals remain under `C:\Program Files\PC Steward OwnerOnlyTest Execution 2a3df1cb2e13455285979c597fe609c8`: `task49-v3-removal-result.json`, `task49-v3-installation-result.json`, `task49-v3-independent-validator.json`, `task49-recovery-result-v3-20261030.json`. Local copied originals, signed manifest and summary are under ignored `artifacts/task-49-host-preparation/recovery-final-verification/`; one-use caller record is `recovery-v3-attempt.json`. Prior execution/compiler/evidence roots, previous failed claims, old candidate copies and user data are retained; no extra cleanup was performed. The remover's `recoveredIncompleteDeployment=false` describes the ledger's completed flag, not proof the old payload was intact; the missing eight resources were pinned and the partial tree was deliberately recovered.

## Verification scope and remaining limits

Deployment/native exit/independent validation and exact byte preservation pass. Earlier unchanged-source Release zero warnings/errors and 415/415 tests, plus local 11/11 recovery, 19/19 wrapper, 15/15 result-ACL and lock fixtures, remain their recorded evidence. No product/canonical script changed and no build/test repeat was needed after host-only execution. `git diff --check` is part of closure documentation checks.

The owner-only fourteen-day signer is not production signing/distribution readiness. No app launch/rendering/responsiveness, real progress, running-app warning, update-provider/helper-UAC/cancellation, Tasks 53/54 or public distribution gate is closed. The failures' original missing stderr and original lock-instant module-observation limits remain historical. Recovery success verifies the exercised route only.

## Concrete separate launch approval — one bounded run

Prepared next action, **not authorized by deployment approval**:

1. Recheck current source HEAD, complete pinned-deployment validator, new ledger/manifest/signer and executable hashes above, zero PCSteward processes, unchanged live/backup database and no WAL/SHM. Stop on drift.
2. From the same verified non-elevated owner, invoke exactly once, no arguments: `Start-Process -FilePath 'C:\Program Files\PC Steward\PCSteward.Desktop.exe' -WorkingDirectory 'C:\Program Files\PC Steward' -PassThru`. No RunAs/UAC is requested. Create a fresh one-use local launch record before dispatch.
3. Observe the normal PC Steward Overview for at most **60 seconds total**, including normal exit; reserve up to 15 seconds for exit. Capture visible UI/read-only screenshot/accessibility evidence and responsiveness. No scan, refresh, consent/settings changes, schedule, clear/reset, update review/dispatch, provider/helper invocation, package/runtime installation or security-policy change. An error/recovery/bootstrap/prerequisite/UAC window counts as failed startup; do not accept installation/repair or retry.
4. Request normal close with that process's `CloseMainWindow()` once; observe natural exit/code and no residual PCSteward processes. No force kill/second launch. Missing normal window or failed close leaves failed/unknown evidence and requires separate reconciliation.
5. After normal exit, preserve a unique coherent database/sidecar copy using stable handles, compare original/copy hashes and run integrity/schema/logical data checks **on the copy only**, in a short-lived isolated reader process. Retain copies. Compare the existing known schema 1–8 and empty non-migration tables from the hash-identical baseline. Explain any actual byte/logical differences rather than asserting byte identity in advance. Rerun full installed validation after close.

Startup opens/migrates the existing SQLite store, may create transient WAL/SHM and initializes local downloads/log state. The persistence migration source is unchanged since the deployed Task 48 baseline; `App.xaml.cs` changes only the coordinator's bounded event-sink composition. Startup is not a scan/refresh/approved update. These are proportionate startup/preservation expectations, not evidence until executed. Shared Microsoft .NET/Windows App Runtime dependencies remain outside the pinned application manifest under the previously disclosed host trust model; failure stops without acquiring new software.

Approval of this bounded launch will permit the above local startup effects and read-only observation/copy verification only. The [host-test contract](../requirements/V2_HOST_TEST_PLANS.md) states **“Deployment approval and app launch approval are separate.”** Deployment approval explicitly excluded launch. An actual Task 49 update-progress/helper-UAC test still needs separately authorized fresh inventory/source evidence and one named package/version/scope with exact action-time approval.
