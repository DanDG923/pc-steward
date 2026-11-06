# Task 49 — App and UAC host-check preparation

**Latest host result: protected recovery PASS.** The separately approved recovery completed with native/protected exit 0 and independent full validator Pass. [Current execution/launch handoff](TASK_49_RECOVERY_EXECUTION.md) supersedes partial-installation/approval-pending handoffs below. App launch and actual update/helper-UAC observations remain separate and unperformed.

**Execution update:** The owner approved the replacement below. Its one elevated wrapper stopped with native exit 1 before removal; the old installation, signer, ledger and database remain unchanged and independently validated. See [failure, Opus dialogue and corrected preparation](../failures/task-49-host-replacement.md). No retry or launch occurred. Earlier preparation/approval-pending statements below are retained as the original approval record.

Date: 2026-10-30. **Preparation PASS; replacement, launch and real update checks not performed.** Owner requested running through the remaining UAC/app checks. Source baseline `2a13fe5a2a67770485d5751f54fb30a417707ad7`; initially clean working tree. Task 50 remains unstarted. Requirements: `FR-UPD-14`, retained `FR-UPD-01–11`, `FR-UI-01–07`, `NFR-OBS-01–05`; preservation `TM-04/TM-06`.

## Why replacement comes first

The Task 49 implementation report explicitly leaves actual provider/helper/UAC, WinUI and installed-host cancellation evidence open. The installed Task 48 candidate predates that implementation and the Task 90 repair. Its unchanged protocol 1 supplies no new progress-wire capability: Task 49 uses truthful, indeterminate desktop-local milestones. Testing the old installation would not validate the new implementation.

The current non-elevated `Test-OwnerOnlyProtectedTestDeployment.ps1 -RequirePinnedPayload` passed. It checked signatures, same signer, complete payload/provenance, ownership/ACLs and reparse restrictions. Existing installed unsigned manifest: `43F209AD1E79E5B2A251F29D723059F419789E44FF72671D68D5C7C7A0B3B7AE`. Protected ledger: `E29ACFECE08CF8E53658C257939FF40BE5D974076C9EF987A7DA94644C135A20`. Signer: `49D695BA9CA2DEA922659699E5F40710F2B5B264`, previously recorded expiry 2026-11-12. No PCSteward process was present.

## Prepared replacement artifact

- Source: `2a13fe5a2a67770485d5751f54fb30a417707ad7`; no runtime/script edits in this preparation.
- Candidate: `%USERPROFILE%\AppData\Local\Temp\PCSteward-Task49-Candidate-dfc550b989e94987b4bedc0efb320013\payload`.
- Manifest: sibling `candidate-manifest.json`, SHA-256 **`6873DFA36B6CBDBD04CDE66306B757E1A231DB2B8F5AD0FD689FD058266D5E46`**.
- **102 files / 50,131,120 bytes**; strict manifest/tree validator passed. Desktop/helper apphosts remain unsigned until protected signing; final signed hashes must be captured after installation.
- Existing protected six-script execution set matches current repository hashes exactly: `C:\Program Files\PC Steward OwnerOnlyTest Execution 2a3df1cb2e13455285979c597fe609c8\scripts`. Hashes retained in `artifacts/task-49-host-preparation/script-pins.json`. Revalidate its execution ACLs and code pins before any elevation; preserve existing execution/compiler/evidence roots.

## Preservation and local verification

With PC Steward closed and no WAL/SHM present, the database was held through the existing stable-file helper, copied to a unique evidence path and original/copy hashes compared. Native read-only SQLite was used on the copy only, using the independently hash-checked installed SQLite DLL. Copy: `artifacts/task-49-host-preparation/preservation-copy/pcsteward.db`; **159,744 bytes**, SHA-256 `CEB917F40ED7B5101E2219501B00B852F9B424683EFA33E142FECA94694F6B99`. Integrity `ok`, zero foreign-key violations, migrations 1–8. Live database hash unchanged. This is a pre-deployment backup, not proof of future migration/launch preservation.

Native Windows PowerShell 5.1; approved SDK 10.0.403 on command-only PATH/DOTNET_ROOT. No SDK/dependency installation or configuration change.

| Command | Result |
|---|---|
| `scripts/Build.ps1` | Exit 0; Release zero warnings/errors |
| `scripts/Test.ps1` | Exit 0; 415/415, zero skipped: Architecture 30, Domain 59, Security 74, Application 252 |
| ScheduledHost then Desktop RID builds, Release/win-x64/`--no-restore` | Exit 0; zero warnings/errors |
| Desktop publish `--no-build --no-restore`, WinUI resource merge, then helper publish `--no-restore`; framework-dependent win-x64 | Exit 0; candidate assembled |
| Strict `Read-OwnerOnlyCandidateManifest` and `PayloadIntegrity.AssertTree` | Pass on final non-OneDrive copy |

Logs and machine-readable summaries are under ignored `artifacts/task-49-host-preparation/`. No production change required new focused tests; full gate verifies the current reviewed source, while RID/publish checks prepare the artifact.

Initial validation rejected the OneDrive `artifacts` ancestor as a reparse point. Fresh normal files were materialized under the exact Temp root above, independently size/hash compared, and validated with unchanged restrictions. Two intermediate PowerShell manifest-handling commands failed (array wrapping and serialization producing an object); preserving the original manifest bytes resolved them. No safety check was relaxed. Intermediate local artifacts are retained; no cleanup was performed.

## Concrete next approval: guarded replacement only

Using the unchanged protected ADR-022 scripts, under the same owner SID and one hidden elevated PowerShell execution:

1. Revalidate source, six code pins/execution ACLs, existing deployment/ledger, candidate hash/tree, backup integrity/hash and no PCSteward process. Any drift stops before removal. The database/sidecar state must still match this plan; otherwise prepare a fresh coherent backup first.
2. Invoke `Remove-OwnerOnlyProtectedTestDeployment.ps1 -Remove` exactly once for `C:\Program Files\PC Steward` and `C:\Program Files\PC Steward OwnerOnlyTest State`. This also removes the **old installer-owned** test certificate from its recorded CurrentUser stores. Preserve `%LOCALAPPDATA%\PCSteward` and all prior execution/evidence roots.
3. Invoke `Install-OwnerOnlyProtectedTestDeployment.ps1 -Install -CandidateRoot <exact candidate above> -CandidateManifestPath <exact manifest above> -CandidateManifestSha256 6873DFA36B6CBDBD04CDE66306B757E1A231DB2B8F5AD0FD689FD058266D5E46 -CreateOwnerOnlyCertificate` once. This creates a fresh non-exportable fourteen-day CurrentUser signing certificate/trust entries and signs/installs the two apphosts through the accepted route. No compiler/rebuild of candidate bytes.
4. Capture native exit and protected result; run the independent pinned-payload validator. Require complete non-signed byte identity, reconstructed apphost identity, same-signer trust, ACL/reparse/resources and unchanged database hash. Capture final signed manifest/new signer. No launch in this approval.

Observe for at most five minutes. Timeout, UAC denial, partial removal/install, unexpected roots, hash/trust/ACL drift or preservation failure stops the sequence. Do not automatically retry, force-kill, replay, roll back or run extra cleanup. The installer's existing transaction rollback remains part of its accepted behavior; separate manual recovery needs a concrete new approval. The owner interacts with Windows UAC; the agent does not bypass it. Existing Task 48 trusted owner-account/session assumptions remain; no distribution-readiness claim.

## Subsequent app and helper checks

After successful replacement, obtain the separate bounded launch approval required by the host contract. Launch the installed desktop non-elevated once, observe overview/navigation/responsiveness, and close normally within 60 seconds. Startup can migrate the preserved database; compare copied post-exit integrity/schema/history/settings and capture any expected changes. No scan or refresh is implicit in that launch.

The current database is the original first-run state, so no fresh named update target is established. Prepare separately authorized inventory/source reads, inspect a fresh exact trusted package/version/scope and visible review, then obtain exact action-time approval. A helper-UAC denial test still records dispatch/history state and requires a reviewed target. An accepted helper/UAC execution can actually update software; it cannot be described as an app-only smoke test. Name its expected effects, warning acknowledgment, deadline, result verification and no-retry rule before approval. User-scope evidence cannot stand in for machine/helper evidence. Cancellation testing needs its own exact scenario and truthful residual-work limits.

Do not fabricate an outdated package or uninstall working software to manufacture a target. Leave unavailable routes unproven. These observations do not close Tasks 53/54 or signing/distribution gates, and do not start Task 50.

## Current outcome

Prepared replacement and preservation evidence only. No elevation, signing/certificate mutation, deployment/removal, app/helper/provider launch, inventory/source refresh, application update, schedule or destructive data action occurred. No push/merge/commit. Next owner decision: approve or decline the exact guarded replacement above; subsequent launch and named real-update actions remain separately scoped.
