# Task 49 — Exact partial-deployment recovery plan

**Execution result: PASS.** The owner separately approved this exact plan; its one recovery invocation completed with native/protected exit 0 and an independent full validator Pass. All 102 files/resources are restored, the new signer is verified and the database/backup is unchanged. See [execution and separate launch plan](TASK_49_RECOVERY_EXECUTION.md). Earlier preparation/approval-pending statements below are the historical approval record; no further recovery or launch is implied.

Date: 2026-10-30. **Preparation complete; fresh exact recovery approval pending. No recovery elevation, removal or installation executed.** Opus acknowledged that its agreed conditions are met on main's returned final validation; see consultation limits below. Requirements: `FR-UPD-14`, retained `FR-UPD-01–11`, `FR-UI-01–07`, `NFR-OBS-01–05`; preservation `TM-04/TM-06`. This is host-test infrastructure, not product feature implementation, Task 50 or a release/distribution gate.

## Current host state and cause

The owner separately approved two one-shot replacements. Attempt 1 stopped before removal on a malformed pin array. Its corrected attempt 2 reached removal but stopped with native/protected **27**, `approved-removal`, **Access to the path 'e_sqlite3.dll' is denied**. See the [complete failure/dialogue record](../failures/task-49-host-replacement.md). No automatic retry occurred.

**The installed app is partial and must remain closed.** Exactly eight original resource files were deleted: App.xbf, two Assets files and five Resources XBFs. All remaining **94 files** match the old protected ledger; Assets/Resources directories are absent. Desktop/helper signatures remain Valid, but this is not complete-installation validation. The old protected ledger, signer and user database/backup remain unchanged. No new candidate was installed.

The caller's preflight queried the database copy through a P/Invoke library binding to the installed `e_sqlite3.dll`, leaving that image loaded while waiting for removal. A synthetic temp fixture reproduced the exact access-denied message and eight-resource deletion pattern; a non-writing write-open probe rejected the loaded DLL, and both probe/removal succeeded after the fixture child exited normally. No protected install file was deleted during that reproduction. A separate isolated read-only child integrity check passed, observed the installed library only in the child, exited, and left the waiting parent's installed-module count at zero. Actual module ownership at the original failure instant was not captured; source and reproduction establish this avoidable lock mechanism, without excluding an unrelated future holder.

Recovery avoids SQLite loading altogether: the live and copied database hashes still match the exact bytes already proven integrity `ok`, zero foreign-key violations, migrations 1–8. Hash equivalence preserves that evidence; no repeated SQLite query is needed inside a waiting caller.

## Exact approval pins

| Item | Pin |
|---|---|
| Source HEAD | `2a13fe5a2a67770485d5751f54fb30a417707ad7` |
| Local caller `artifacts/task-49-host-preparation/Invoke-ApprovedRecovery.ps1` | `F81AA70AEA1BAFE6EFEE44BC91AC90105B9FC551B0635B8BCE8CDAA98E12105B` |
| Raw wrapper `recovery-v3-wrapper.ps1` | `EC1D81B3282933B7E9C2D6A1473382597750A5744116A4D4A6FFDBE9DA715001` |
| Encoded-command launcher source `recovery-v3-launcher.ps1` | `ACB9A56F0F70C53C495C64FB2371F25A8542A3E332FDBCEADEB71F57B34EF6D8` |
| Approved new unsigned manifest | `6873DFA36B6CBDBD04CDE66306B757E1A231DB2B8F5AD0FD689FD058266D5E46`; 102 files / 50,131,120 bytes |
| Exact retained 94-file partial manifest | `94336C03FBE30464FF3AF74035471F470BB38150593F6F9934C33750A0CA856A` |
| Old protected ledger | `E29ACFECE08CF8E53658C257939FF40BE5D974076C9EF987A7DA94644C135A20` |
| Old signer to remove | `49D695BA9CA2DEA922659699E5F40710F2B5B264` |
| Live and preserved copied database | `CEB917F40ED7B5101E2219501B00B852F9B424683EFA33E142FECA94694F6B99`; 159,744 bytes |
| Immutable attempt 1 record | `B8B16DD68C56FC7C6528251E1090D89DF1BECDBA630805FFF4FF94D2644C1CE5` |
| Immutable attempt 2 record | `152FDDA2EBDEB88E0EE2BBAE42859AF190D659C174294A8E21428BD28EB27945` |
| Attempt 2 stopped-state record | `957ADCA3BB29F9483091642028C7A5B0E1F98AD99D7C19BED07A2C81209411E8` |

Candidate root remains `%USERPROFILE%\AppData\Local\Temp\PCSteward-Task49-Candidate-dfc550b989e94987b4bedc0efb320013\payload`. Its sibling `candidate-manifest.json` has the approved new manifest hash above; sibling `remaining-old-payload.json` has the partial manifest hash above. Both are fixed local NTFS inputs with strict path/reparse/stream and byte validation.

Existing protected execution root remains `C:\Program Files\PC Steward OwnerOnlyTest Execution 2a3df1cb2e13455285979c597fe609c8`. Its six scripts match the unchanged canonical scripts and `script-pins.json`; execution-root/code/compiler protection rules are unchanged. No new installer/provider/code permission is introduced. Privileged execution remains one fixed literal command, not a mutable elevated `-File`: a self-contained gzip literal expands to the raw wrapper bytes via .NET. Independent byte verification proves the exact expansion; encoded launcher is 17,312 characters. No network input or executable-path/argument metadata is involved.

## Read-only preparation and fixtures

- Check-only recovery caller passed in fresh Windows PowerShell 5.1: exact partial tree/ACLs/old signatures and stores, old ledger, candidate, original claims, database/backup and six code pins; the caller has no installed module loaded. It neither loads SQLite nor runs the complete-deployment validator against the knowingly partial old tree.
- `Test-LoadedSqliteRemoval.ps1`: synthetic copied SQLite image reproduces loaded write-probe failure, exact removal failure/eight deleted resources; normal child exit 0 then write-probe and synthetic removal pass. No installed-tree mutation.
- `Test-RecoveryPartial.ps1`: **11/11**, actual extracted recovery functions. Live partial-state pass; synthetic 94-path pass; ninth missing file, extra file, changed hash, wrong manifest digest, wrong ACL owner and junction reject. Source inspection rejects installed SQLite loading and full-validator invocation in the pre-removal baseline. Exact compressed launcher, non-elevated: exit **21**, no recovery claim/result and unchanged database.
- `Test-RecoveryWrapper.ps1`: **19/19**, actual emitted bytes, roundtripped six pins, extracted schema/hash/ACL loop, outer-try structure, negative schema/hash and synthetic baseline comparisons; exact raw wrapper non-elevated exit 21.
- Compression roundtrip is byte-identical to the pinned wrapper. The existing three-entry result ACL reader remains separate from the four-entry execution-code ACL; its previous **15/15** evidence applies to the unchanged functions.

Logs/JSON evidence and fixture scripts remain in ignored `artifacts/task-49-host-preparation/`. A first source-search assertion matched the validator's name inside the pin allowlist rather than an invocation; it was corrected to inspect the actual baseline phase, then the 11 scenarios passed. Product/canonical/protected source is unchanged. Earlier product Release zero-warning/error and 415/415 evidence remains applicable; it is not a fresh execution of product or host checks in this recovery preparation.

## Proposed one-shot approved operation

After exact owner approval of this record:

1. Recheck every relevant pin above, retained tree/signers/ACLs/reparse protections, byte-identical database/backup, no WAL/SHM or PCSteward process, and absence of prior v3 claim/result. Any drift stops before elevation. Capture and hash-check caller bytes before invoking their script block with `-Execute`.
2. Claim `recovery-v3-attempt.json` with CreateNew; start **one hidden RunAs Windows PowerShell** under the same installing SID. Owner responds to Windows UAC. No bypass of Windows consent.
3. Elevated wrapper independently validates identity, execution/code/compiler protection, canonical six-script pins, exact partial tree/old ledger/signatures/stores and database hash. It checks its own module list contains nothing under the install root, then probes **all 94 remaining files** with `FileSystemRights.WriteData | Delete`, sharing ReadWrite/Delete, with no writes, delete-on-close or disposition. It closes each probe and stops before deletion on any failure. The fixture rejects both a loaded image and a handle that shares read/write but not delete; after normal holder exit it passes with identical file hashes and LastWriteTimeUtc. This narrow lock check avoids enumerating unrelated protected Windows processes. It does not prove future global quiescence; a new external lock after the probe may still stop removal.
4. Invoke the unchanged `Remove-OwnerOnlyProtectedTestDeployment.ps1 -Remove` once. Its existing partial-deployment route validates the protected ledger/SID/allowlisted roots and every retained path's exact ACL/reparse state. Delete only the partial `C:\Program Files\PC Steward` and `C:\Program Files\PC Steward OwnerOnlyTest State` roots and the recorded installer-owned old certificate entries. Preserve the database and all previous execution/compiler/evidence roots.
5. Invoke the unchanged installer once with `-Install -CreateOwnerOnlyCertificate`, the exact candidate root, candidate manifest path and approved hash. Create the accepted fourteen-day non-exportable CurrentUser signer/trust entries; sign/install the two apphosts using the pinned payload. No rebuild.
6. Run the independent complete pinned-deployment validator and capture native exit/protected result, new signer/final signed manifest, exact unchanged non-signed payload and reconstructed signed-apphost identity. Verify unchanged user database/backup and zero residual PCSteward process. Final result is the new protected `task49-recovery-result-v3-20261030.json`; do not reuse earlier results or attempt claims.

Observe at most five minutes. On denial/timeout/partial removal/install/unknown state or pin/data/trust/lock failure: stop, record exact phase/native result and actual partial state; **no retry, force kill, provider replay, manual rollback/restoration, extra removal or cleanup**. Only the existing installer's accepted internal transaction handling is included. No app/helper launch, scan, source refresh, schedule change, application update, publish/push/merge or Task 50 is included. A successful deployment will need the separately approved launch plan before app interaction.

## Consultation and authority

Opus confirmed the source-level installed-library lock mechanism and supported existing canonical partial recovery. Main returned the exact synthetic reproduction, partial-state fixtures, latest code pins, compression byte proof and narrow write-probe alternative. Opus withdrew global process enumeration after main challenged its inaccessible-process and scope problems. It requested probing all remaining files and testing combined WriteData/Delete rights. Main made those small changes, proved loaded-image and deny-delete rejection plus unchanged hashes/LastWriteTimeUtc, regenerated artifacts and reran 11/11 + 19/19 and exact compressed launcher rejection/byte expansion. Main returned final results to the same dialogue.

**Opus acknowledged receipt:** on main's report, all defined conditions are met and no unresolved contradiction prevents requesting a new recovery approval. Opus used no tools for that final receipt and did not independently inspect/hash the final small probe edits. This is consultation agreement, not fresh independent code review, phase acceptance or host authority. All three final hashes, including the changed launcher hash, are recorded above and in `recovery-launcher-byte-check.json`. Fable/Astra escalation is unnecessary because the scoped disagreement was resolved with returned evidence. Residual probe-to-delete race, newly partial state requiring fresh pins/approval, no retry and no forced process closure remain explicit.
