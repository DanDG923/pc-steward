# Task 48 — Owner-only redeployment and responsive launch complete

Date: 2026-10-29. **Outcome: PASS for the exact owner-only task. Task 49 is not started.** Requirements: retained `FR-DAT-12–15`, `AC-DAT-18`, `FRA-14–16`; security boundaries `TM-04/TM-06`.

## Authority and exact artifact

The owner separately approved the pinned guarded removal/redeployment and the first bounded launch. That launch stopped at missing .NET before managed app startup. The owner installed .NET, the requested Opus 5.5 consultation and independent prerequisite checks completed, and the owner then replied **“Approved”** to one fresh unchanged launch under the disclosed shared-runtime trust and bootstrap fail-and-stop plan. The successful run below used that new approval. There were **two separately approved launch attempts in total**, no automatic retry, and only one invocation under the final approval.

[Deployment evidence](TASK_48_PINNED_REDEPLOY_EXECUTION.md) records the exact transaction, native exit 0, protected result Validated, fresh same-signer SHA-256 Authenticode, full permissions/reparse/resources and original artifact identity. [First launch/prerequisite evidence](TASK_48_LAUNCH_RUNTIME_STOP.md) retains the failure, owner's runtime installation, completed consultation, targeted shared-runtime signatures/ACLs and exact fresh plan; it is not overwritten or converted to a success.

| Identity | Approved and final value |
|---|---|
| Source HEAD | `cd6d4530ac4e8c1e31a07b1c9096ffd358e75342`; Task 42 runtime baseline `52b43130130b25a850f43f1fe478d112c3ce9cb7` |
| Unsigned manifest | `43F209AD1E79E5B2A251F29D723059F419789E44FF72671D68D5C7C7A0B3B7AE` |
| Exact installed target | `C:\Program Files\PC Steward\PCSteward.Desktop.exe`, no arguments, installed working directory, same non-elevated owner |
| Signed desktop EXE | `5B4BEC38BD02904021139B6EE6B22719706FB2FC8DECD9402144AD403706B6A2` |
| Desktop DLL | `12C1B111B9DD4832C5D0E49BB554A8766CC2B8795071A547102CEDB51450185D` |
| Protected completed ledger | `E29ACFECE08CF8E53658C257939FF40BE5D974076C9EF987A7DA94644C135A20`, unchanged after launch |
| Fresh observer | `artifacts/task-48-deployment/Invoke-LaunchAfterOwnerRuntimeInstall.ps1`, SHA-256 `36E7266125C69A49D53A0F33721602DB640561B94F2009CDFB68B4A15422C0BA` |
| Signer | `49D695BA9CA2DEA922659699E5F40710F2B5B264`, expires **2026-11-12T19:15:42Z**, no timestamp |

All **16 code/test/prepared-program pins** from the final approval snapshot still match. Runtime/deployment source was not changed to achieve this launch. Global .NETCore.App **10.0.14** and Windows App Runtime **1.8 / 8000.994.2142.0 x64** are shared Microsoft/OS-managed dependencies outside the app's pinned 102-file ledger; the preceding prerequisite snapshot records that distinction.

## Successful approved live launch

| Check | Actual result |
|---|---|
| Preflight | Strict protected validator **Pass / pinned-unsigned-v1**, exact observer/EXE/ledger, non-elevated owner, absent data root, no PCSteward process, global .NET 10.0.14 listed |
| Process | PID **11040**; `2026-10-29T19:32:22.1845714Z` to `19:32:45.0112074Z` |
| Observation | **45 process samples**, zero `Responding=false`; first expected title sample at 562 ms. This title sample is not a rendering-latency benchmark |
| Correct window | Exactly one returned **PC Steward** window, ID **1378730**. Computer-use screenshot and accessibility tree show **Overview**, **Ready**, **Start local inventory**, and **Private by default**; no runtime/startup/recovery dialog |
| UI actions | Read-only observation only; no navigation, inventory/source/update/schedule/reset/cleanup control activated |
| Normal close | Observer received the recorded overview observation, `CloseMainWindow()` returned **true**, process exited naturally with **native code 0** |
| Bound | **22,826 ms total**, inside the approved 60 seconds including normal exit |
| Residual state | Zero PCSteward processes and no PC Steward window after exit; no forced termination or additional invocation |

The bootstrap succeeded on this observed host without another runtime/package installation or environment redirection by the agent. Missing Main/DDLM 1.8 package entries did not prevent this run; no assertion is made about the exact internal bootstrap branch or other OS/package configurations. This is a bounded responsiveness/normal-close smoke check, not frame-time, long-duration, broad accessibility or provider/update evidence.

The screenshot is preserved in the computer-use tool transcript. The captured accessibility text and overview observation are retained in local evidence; no screenshot file was manufactured or recaptured after closing the app.

## Fresh data, preservation and copied-database verification

The prior data root was absent at final preflight. As explicitly disclosed and approved, startup created `%LOCALAPPDATA%\PCSteward\pcsteward.db` and an empty `downloads` directory. After normal exit, the live tree contains only those two entries; no live WAL/SHM sidecars remain. There was no old database/history to back up before startup.

`artifacts/task-48-deployment/Verify-LaunchDatabaseCopy.ps1` required the successful bounded exit/no process, rejected unexpected state files/reparse paths, opened the source through stable read handles denying writers/deletion, copied its default bytes to a unique ignored evidence directory, and compared original/copy hashes plus source length/mtime while held. All source handles were released before checks on the copy. The verifier never opened the live database through SQLite.

| Copied-data check | Result |
|---|---|
| Database | **159,744 bytes**; original and copy SHA-256 **`CEB917F40ED7B5101E2219501B00B852F9B424683EFA33E142FECA94694F6B99`** |
| Copy destination | `artifacts/task-48-launch-after-runtime-result/database-copy/pcsteward.db` |
| Tool | Isolated Windows PowerShell/C# P/Invoke harness, pinned installed native `e_sqlite3.dll`; `SQLITE_OPEN_READONLY` against the copy only; no packages downloaded/installed |
| Integrity | `PRAGMA integrity_check` = **ok**; `PRAGMA foreign_key_check` = **zero rows** |
| Schema | All **eight migrations**, exactly `1,2,3,4,5,6,7,8`; **17 tables** |
| Freshness | All **16 non-migration tables empty**, including inventory/scan/source/update/history/download ledger and app metadata; downloads has zero files |
| Stability | Original and copied input hashes unchanged after SQLite queries; original remains untouched |
| Copy sidecars | SQLite created `pcsteward.db-wal`/`pcsteward.db-shm` **in the copy directory** while reading the WAL-mode database. These are verification-copy artifacts, not original app output; they are retained |

The populated-state preservation path and destructive data controls were not exercised. This task proves preservation of the originally absent state during deployment, then the approved creation and integrity of a fresh store during launch. It does not claim a migration of pre-existing user history or a destructive reset test.

## Final gates and evidence

Final protected validator **Pass / pinned-unsigned-v1** rechecked complete installed ledger/path set, all **100 non-signed hashes**, both raw signed-image reconstructions, same-signer trusted signatures, ownership/DACL/reparse and six XBF/PRI resources. Protected ledger is byte-identical after launch. All 102 approved payload paths are retained. No scan, source refresh, application update, schedule change, data deletion, key export or distribution action was performed.

Ignored `artifacts/task-48-launch-after-runtime-result/` evidence:

| File | SHA-256 |
|---|---|
| `process-result.json` | `F640A65CD7FA15E9D8F335FCCDCC4C81C493F50B90ED8922EC54B5174A864005` |
| `overview-observed.json` | `D20621BD451A121D25CF728C7466E326F374771D51EDF9B4B33B5767306BEF0F` |
| `overview-accessibility.txt` | `CBEC73DF8F4859BE689253076844819A981C9950425B51D206B016892ADDCDA1` |
| `database-and-final-validation.json` | `6EF749AFC4E81AC2E9DFBEDF1BA94722C26FCB18BAECCF07C6AB4DB0461399BF` |

`attempt.json`, `running.json`, the copied DB/sidecars and `completion-summary.json` are also retained. The earlier failed launch remains separately under `artifacts/task-48-launch-result/`; protected deployment/code/compiler/evidence and materialized candidate roots remain retained without implicit cleanup authority.

Code validation completed before deployment remains applicable to the unchanged pinned source: **85 focused assertions**, focused **1/1**, canonical Release build **0 warnings/errors**, full **291/291**, zero skipped. No new production code followed those gates; this continuation added only ignored evidence helpers/results and documentation. Fresh live verification above supplies the previously missing deployment/launch/database evidence. PowerShell parse, UTF-8/Markdown links, source/program pins and `git diff --check` are checked at finalization.

| Requirement boundary | Evidence and limit |
|---|---|
| `FR-DAT-12–15` | No destructive data action; absent state retained during deployment, approved fresh state created and copied integrity/hash verified. Existing data-control behavior remains unchanged; no populated-data/reset test claimed |
| `AC-DAT-18` | Guarded old deployment removal/pinned replacement and owned certificate accounting verified; data preservation and current fresh-store retention explicit |
| `FRA-14–16`, `TM-04/TM-06` | Exact owner-only bytes/signer/protected ACLs/provenance and disclosed external-runtime/account trust; no public/stable/other-recipient distribution authority gained |

Remaining limits: trusted owner-account/session assumption; shared-runtime trust outside app pins; private compiler descendant inheritance not independently audited; current signing/trust passed but no time-shift expiry test. The temporary signer expires on **12 November 2026 at 19:15:42 UTC**, after which current-time validation must fail closed. No public release gate is closed by Task 48.

**Task 48 is complete. Stop here.** Task 49 requires its own instruction and entry gates. No commit was made because Git user.name/email remain unset; identity/global configuration were not changed. Pre-existing Task 46/47 planning changes are preserved.
