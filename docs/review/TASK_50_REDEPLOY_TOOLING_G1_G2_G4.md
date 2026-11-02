# Task 50 — Redeploy tooling for a host with user data (gaps G1, G2, G4)

**Status:** Code, fixtures and docs only. The owner approved building this tooling. No script was run on a host: nothing ran against Program Files, a certificate store, a real install or real user data. Running any part of it needs a separate, exact owner approval with hash pins (activation plan §9, approval 1). Independent review and Windows CI evidence are still required.

**Requirements:** `FR-UPD-15`; `TM-03`, `TM-04`, `TM-06`, `TM-07`, `TM-13`; ADR-022 (owner-only protected test deployment: per-user data is retained) and its Task 48 amendment.

**Gaps closed in code:** G1, G2 and G4 from the [activation host-session plan](TASK_50_ACTIVATION_HOST_SESSION_PLAN.md) §11. G3 (candidate builder), G5, G6, G7 and G8 remain open.

## Files

| File | Change |
|---|---|
| `scripts/Invoke-OwnerOnlyApprovedRedeployment.ps1` | Bootstrap template: adds plan schema `pc-steward-exact-redeploy-v3` (G1). |
| `scripts/OwnerOnlyExecution.ps1` | Adds pure, read-only user-data functions (G1). This file is one of the six protected scripts, so its pin changes. |
| `scripts/Invoke-OwnerOnlyRedeployCaller.ps1` | New. Non-elevated caller with NewPlan, CheckOnly and ExecuteApprovedPlan modes (G2). |
| `scripts/Copy-OwnerOnlyUserDatabase.ps1` | New. Read-only database preservation copy (G4). |
| `tests/PCSteward.Security.FixtureTests/Fixtures/RedeployTooling.ps1` | New fixture. It runs under pwsh 7 and Windows PowerShell 5.1. |
| `tests/PCSteward.Security.FixtureTests/RedeployToolingScriptTests.cs` | New MSTest class. |

## G1 — plan schema v3 (data present, database preserved)

The database is `%LOCALAPPDATA%\PCSteward\pcsteward.db`. Both `App.xaml.cs` (via `FactoryReset.GetStateRoot()`) and `ScheduledHost/Program.cs` use this path. SQLite runs in WAL mode (`SqliteDatabase.cs`), so a cleanly closed app leaves no `-wal` or `-shm` file.

**Plan v3** is v2 plus these keys, with a closed key set compared in ordinal order:
- `preserveUserData: true`
- `userDataRoot`: the computed `%LOCALAPPDATA%\PCSteward`. The elevated bootstrap requires an exact match.
- `userDataFiles`: one to eight root-level leaves. Each entry has `bytes`, `path` and `sha256`. `pcsteward.db` is required exactly once. Names ending in `-wal`, `-shm` or `-journal`, traversal and lower-case hashes are refused.

`Assert-OwnerOnlyRedeployPlanV3` also validates the origins (same allowlists as v2), a user SID, uppercase hashes, the thumbprint and exactly six script pins.

**Template behaviour for v3 only.** The template sets `$preserveUserData` only for schema v3. The three "data root absent" tests become `(!$preserveUserData -and (Test-Path -LiteralPath $data))`. Each one is followed by `Assert-OwnerOnlyUserDataPreserved` at:
- preflight, together with the user-data-root match and `Assert-OwnerOnlyNoSqliteLoaded`;
- after removal;
- after independent validation.

`Assert-OwnerOnlyUserDataPreserved` requires all of the following:
- the data root exists;
- there is no reparse point anywhere on the root or database path, up to the drive root;
- no `PCSteward*` process is running (the template's existing process check also still applies);
- no `-wal`, `-shm` or `-journal` sidecar exists for any pinned leaf or for `pcsteward.db`;
- each pinned file's length and SHA-256 equal its pin.

It reads each file through a single `FileShare.Read` handle, so an open writer makes the read fail. It never loads SQLite. Afterwards it re-checks for sidecars and processes.

The v3 `result.json` reports `userDataPreserved=true`, `userDatabaseBytes` and `userDatabaseSha256`. v1 and v2 keep the original literal, with `userDataRootAbsent=true`, the same key order and the same error texts.

**Remover check (read, not changed).** `Remove-OwnerOnlyProtectedTestDeployment.ps1` deletes only `$installRoot` and `$stateRoot` (the only two `Remove-Item` targets) plus ledger-owned certificates. It never names LocalApplicationData and reports `userDataPreserved = $true`. The installer and validator do not reference the data root either. Fixture section 7 asserts this.

## G2 — committed caller, plan generator and CheckOnly

`Invoke-OwnerOnlyRedeployCaller.ps1` requires 64-bit system Windows PowerShell 5.1. It reconstructs the design of the Task 48 and Task 49 callers (TASK_48_EXACT_REDEPLOY_PLAN, TASK_48_PINNED_REDEPLOY_EXECUTION, TASK_49_PARTIAL_RECOVERY_PLAN, the failure record) and fixes the two Task 49 defects.

**Modes**
- **`-NewPlanPath`** (read-only). Assembles a canonical, ASCII-only, one-line v3 plan from the host: the current SID, a fresh execution-root GUID, the six script hashes, the ledger hash and thumbprint, the candidate manifest hash, and the `pcsteward.db` length and hash. It cross-checks these exactly as CheckOnly does, then writes one new file (CreateNew; not under Program Files, Windows or the data root). It executes and elevates nothing.
- **CheckOnly** (the default, read-only). Never starts a process.
  - Plan: byte-canonical form (re-serialised and compared) and the plan SHA-256.
  - Environment: Windows PowerShell 5.1, 64-bit system host, not elevated, SID equals the plan, no `COMPlus_`, `COR_PROFILER`/`COR_ENABLE_PROFILING` or `APPDOMAIN_MANAGER_` overrides (process, User or Machine), no MachinePolicy or UserPolicy execution policy, and the fixed System32 path.
  - Scripts: the six pins, read under stable handles from a fixed NTFS, non-reparse source; the template bytes.
  - Old deployment: ledger hash, then schema v6, completed, SID, install root, thumbprint and origin; both executables Valid and signed by that thumbprint; the signer present once in each of CurrentUser My, Root and TrustedPublisher and not expiring within one hour (fails closed for G8).
  - Candidate: manifest hash, then the full tree (exact file set, length and hash, no reparse point, origin = `expectedNewOrigin`).
  - Execution root: absent, with the 32-hex name rule.
  - User data: the v3 assertion above.
  - Attempt record: absent.
  - The caller itself has no SQLite loaded.
  - Output: a JSON report of hashes only (plan, program, encoded command, template, scripts, manifest, ledger, thumbprint, database), with no paths and no SID.
- **`-ExecuteApprovedPlan -ApprovedPlanSha256 … -ApprovedEncodedCommandSha256 …`**
  1. Before any helper is loaded, requires the plan bytes to match the approved hash.
  2. Re-runs every CheckOnly check and requires both approved hashes.
  3. Pre-compiles the pinned waiter's exit-code type before the claim.
  4. Creates `redeploy-attempt-<first 16 hex of the plan hash>.json` beside the plan with CreateNew. The record is one use: an existing record refuses.
  5. Starts exactly one `Start-Process -FilePath 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -ArgumentList '-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-EncodedCommand',<generated> -Verb RunAs -WindowStyle Hidden -PassThru`, with no retry and no loop.
  6. Captures the native exit with the pinned `Wait-OwnerOnlyDeploymentProcess` (900,000 ms).
  7. Reads `result.json` only after a matching ACL check and through a stable handle.
  8. Re-checks the database pin while not elevated.
  9. Records one of `Validated`, `ValidatedPostcheckFailed`, `StoppedOrUnknown`, `PendingOrUnknown` or `NotStarted` in the attempt file.

**Encoded command.** The elevated program is assembled as text in this order:
1. a header comment;
2. `$OwnerOnlyPlanJson='<exact canonical plan>'`;
3. the pinned `OwnerOnlyExecution.ps1` text;
4. the bootstrap template text.

The command line bound needs compression, as in Task 49. The program is gzip-compressed into one fixed, embedded stub. The stub expands the literal, requires its exact SHA-256 (exit 20 otherwise) and only then dot-sources it. The stub template is the only code template. The caller takes no command, path or argument from data.

Measured size: about 28,150 encoded characters with LF, and about 28,250 with CRLF, against the fixed bound of 30,000. Task 49's 29,212-character command ran through RunAs. Template growth beyond roughly 650 compressed bytes would fail closed at the bound.

**Running the caller.** It loads only:
- the pinned `OwnerOnlyExecution.ps1` and `Wait-OwnerOnlyDeploymentProcess.ps1`, after each passes its hash check and an AST check that the file contains function definitions only;
- in Execute only, the waiter.

The owner runs the caller from captured, hash-checked bytes (procedure in the header comment). A failure prints a JSON report and then throws. It never calls `exit`, so the owner's console stays open.

**Task 49 defects fixed**
1. *5.1 `pins` serialisation.* Every array is rebuilt from plain ordered records into a fresh `object[]` and serialised with `ConvertTo-Json -InputObject -Depth 6 -Compress`. The plan file must equal that canonical form byte for byte. The fixture round-trips six script pins and a one-element database pin, including through `ConvertFrom-Json`, in both shells.
2. *Result ACL with 3 entries versus 4.* The template writes `result.json` with `New-OwnerOnlyExecutionSecurity` (leaf, four entries: SYSTEM, Administrators, OWNER RIGHTS and Users). The caller checks exactly that writer's shape (`Assert-OwnerOnlyBootstrapResultAcl` → `Assert-OwnerOnlyExecutionAcl`). It rejects the three-entry `WriteProtectedFile` shape, directories, extra entries, Users write, unprotected ACLs and the wrong owner. Fixture section 14 also checks through the AST that the template's writer call has no switches.

**Deviations from the task brief (deliberate; for review)**
- No `-Wait` on `Start-Process`. The Task 48 Opus review and closure traced the original null exit code to that pattern, and required the RunAs/PassThru object to go straight to the native waiter. The 900,000 ms bound means a timeout records `PendingOrUnknown`, with no kill and no retry.
- The child also gets `-ExecutionPolicy Bypass`. This follows the Task 48 precedent and is process-scoped. Without it, the protected `.ps1` copies the bootstrap runs would be blocked under a Restricted policy. The caller refuses enforced Group Policy rather than overriding it.
- The switch is named `-ExecuteApprovedPlan` to match the activation plan §5 B4 and Task 48. The plan generator is a mode of the caller, so there is one canonicaliser.
- Only plan schema v3 is supported by the caller. v1 and v2 remain template-only, and their historical callers are not revived.

## G4 — database preservation copy

`Copy-OwnerOnlyUserDatabase.ps1` runs in Windows PowerShell 5.1, not elevated. It works as follows:
1. Refuses if any `PCSteward*` process runs, if any `-wal`, `-shm` or `-journal` sidecar exists, or if a reparse point is on the path.
2. Streams `pcsteward.db` through a `FileShare.Read` handle into a new `pcsteward-db-copy-<UTC>-<8 hex>` folder, under `%USERPROFILE%\PCSteward-DatabaseCopies` by default. Destinations under Program Files, Program Files (x86), Windows or the data root are refused.
3. Requires the streamed hash, the re-read original and the copy to have equal SHA-256 and length.
4. Re-checks for sidecars and processes.
5. Parses the 100-byte SQLite header of the copy only, as bytes: magic, page size, WAL versions, in-header page count against the file length.
6. Writes `copy-record.json` (CreateNew). The record holds no profile path.

**Integrity decision (smallest safe option).** No SQLite engine is loaded and no `PRAGMA integrity_check` is run:
- The only engines on the host are the installed `e_sqlite3.dll`, which caused the Task 49 removal lock, and the unsigned candidate payload's native library. Running candidate native code outside the approval scope is new execution authority.
- Integrity evidence is therefore byte identity:
  1. the copy equals the live file;
  2. the live file equals the plan pin at preflight, after removal and after validation, with the results in the elevated template and in the caller's post-check;
  3. a later, separately approved app launch opens the preserved database.
- Task 49 used the same hash-equivalence argument. Any future engine-based check must run on the copy only, in a separate process that exits before the redeploy, and never in the caller.

## Tests

Command: `dotnet test tests/PCSteward.Security.FixtureTests --filter FullyQualifiedName~RedeployToolingScriptTests`.

The fixture runs the real template statements and the real caller, helper and copy functions, extracted from their AST, on temp directories, with `Get-Process` stubbed.

| Area | Rows |
|---|---|
| Parse | Zero errors in six scripts and the fixture, per shell. |
| G1 plan | 14 `Assert-OwnerOnlyRedeployPlanV3` cases, including missing, false or string `preserveUserData`, an extra key, a missing `pcsteward.db` pin, a pinned sidecar, traversal, a lower-case hash, zero bytes, a non-array, five scripts, a bad origin and the SYSTEM SID. |
| G1 template plan section | 9 cases. v1, v1 with origin, v2, v2 bad origin and unknown schema keep the exact old texts. v2 with `preserveUserData` stays data-absent. v3 valid, v3 missing preserve and v3 malformed pin. |
| G1 guards | 23 executions of the real preflight, removal and final guard pairs: v2 data present or absent; v3 pinned, hash mismatch, `-wal`, `-shm`, `-journal`, process, root absent, other root and reparse root (junction on Windows, symlink elsewhere); removal and final variants. |
| G1 result | Exact v2 key order; v3 keys and database hash. |
| G1 remover | Remove targets are `$installRoot,$stateRoot`; no data-root reference in the remover, installer or validator. |
| G2 AST | One `Start-Process`, only inside `Start-OwnerOnlyElevatedRedeploy`; literal System32 path equal to the runtime check; `-Verb RunAs`; no `-Wait`; not in a loop. It is called once, only from `Invoke-OwnerOnlyRedeployExecution`, under `if($mode -ceq 'Execute')`. Neither the CheckOnly nor the NewPlan call graph reaches it or the waiter. No `Invoke-Expression`, `Add-Type` or `Remove-Item`. `[ScriptBlock]::Create` appears only in the pinned-helper loader. |
| G2 data | Canonical round trip (one-element arrays; no `value`/`Count` wrapping); multi-line, non-canonical and non-ASCII plans refused; `\u` escaping. |
| G2 program | Bound ≤ 30,000; lossless gzip; stub pins the program hash; deterministic; embedded plan exact. The stub was run in a child shell: exit 7 for a verified program, exit 20 for a changed expansion. |
| G2 attempt | One-use record; classification of six outcomes; CLR override names (`DOTNET_*` not refused, since .NET Framework ignores them); helpers are function-only; no SQLite loaded. |
| G2 pins | Candidate tree: valid, origin, unpinned, traversal, changed, missing, reparse. Ledger: valid, hash, signer, origin. |
| G4 | Copy equals original; source unchanged; record has no profile path; header facts; refusals for destination in data or forbidden roots, sidecar, process and reparse; no folder created on refusal; no engine or `Add-Type` text. |
| Windows-only | Result-ACL rows (four-entry writer accepted; three-entry, directory, extra entry, Users write, unprotected and wrong owner rejected); the real generated v3 program run non-elevated with a synthetic SID must exit 1 with "Exact installing SID and elevation required."; junction reparse rows. |

There are also static tests: template data checks confined to `!$preserveUserData`; scripts ASCII-only with no SQLite or `DllImport`; and Windows CI (`windows-latest` → `Test-CI.ps1` → `dotnet test PCSteward.slnx`) includes this project.

**Local results (Linux, pwsh 7.6.9)**
- Fixture build 0 warnings and 0 errors.
- Redeploy tests: 3 passed, 2 Inconclusive with reasons. The pwsh row passed every portable assertion and reports the Windows-only rows explicitly; the 5.1 row is Windows only.
- A mutation of the removal guard was detected by two tests, then reverted.
- `DeploymentCapabilityPolicyTests` 114/114 and `DeploymentCapabilityScriptTests` 3 passed + 1 Inconclusive (5.1).
- ArchitectureTests 108/108 after rebuilding Application, ElevatedHelper and ScheduledHost.

**Windows-only evidence still needed:** the Windows PowerShell 5.1 row (including the one-element pins round trip under 5.1), result-ACL objects, junction rows, the real program's non-elevated refusal, and an actual CheckOnly on the host (owner-run, read-only, separately approved).

## Owner procedure this enables (for the plan revision; not authorised here)

1. **B1** (G3, open): build the candidate and record the manifest hash.
2. **B2:** close PC Steward normally, then run `Copy-OwnerOnlyUserDatabase.ps1` and keep its record.
3. Copy the six protected scripts and the template into a fixed NTFS `scriptSource`.
4. Run the caller with `-NewPlanPath` and inspect the plan.
5. **B3:** run CheckOnly and record the plan, encoded-command, template, caller and six script hashes.
6. Obtain approval 1 with those pins plus the copy-script hash.
7. **B4:** run `-ExecuteApprovedPlan` once.
8. **B5:** check the attempt record shows `Validated` and that `userDatabaseSha256` equals the pin.

## Risks and open questions

- **Scheduled host.** If a PC Steward scheduled task launches `PCSteward.ScheduledHost` during the window, it can open the database or hold install files. Checks are point-in-time: a process or sidecar seen at any check stops the run, but a process that starts mid-removal can still cause a partial state. The owner should confirm that no scheduled run is due (or none is registered) before B4.
- **Same-user trust.** The Task 48 assumption still holds. The plan, script source and candidate are user-writable between CheckOnly and the elevated read, and only hash pins bind them.
- **Unpinned files.** Only the database is pinned. Other data-root contents, such as logs, downloads and the reset marker, are not hashed. They are preserved because nothing deletes them, not because they are verified.
- **SQLite detection.** `Assert-OwnerOnlyNoSqliteLoaded` refuses any loaded module or assembly whose name contains "sqlite", which includes Windows `winsqlite3.dll` if something loaded it. This fails closed.
- **Encoded command size.** The margin is about 1.8k characters, as noted above.
- **Compression determinism.** It is assumed per machine (the same .NET Framework zlib). A mismatch between CheckOnly and Execute fails closed at the approved hash.
- **G8.** The signer expires on 2026-11-13T20:18:09Z. CheckOnly refuses within one hour of expiry, and this tooling does not change G8.
