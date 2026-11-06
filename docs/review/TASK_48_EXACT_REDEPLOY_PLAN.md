# Task 48 — Exact protected removal and pinned redeployment plan

**Status: Prepared and reviewed for an owner-approved test; exact owner host approval pending.** Opus's fresh re-check found no bootstrap blocker; the parent completed its caller/doc follow-ups. No elevated action below has run. Accepting the ADR amendment authorized local work; it did not authorize this revised host transaction.

Requirements: `FR-DAT-12–15`, `AC-DAT-18`, `FRA-14–16`, retained `TM-04/TM-06`. See [implementation/evidence](TASK_48_PAYLOAD_FIX_IMPLEMENTATION.md), [review](TASK_48_PAYLOAD_OPUS_REVIEW.md) and [response](TASK_48_PAYLOAD_REVIEW_CLOSURE.md).

## Scope of the trust claim

The repaired route closes the reviewed mutable-script and compiler-file substitution paths. It **assumes a trusted owner-controlled Windows account/session**; it does not isolate from active same-user control of CLR startup environment, the non-elevated caller, signer-key use or CurrentUser trust stores. The same-user key/trust scope is inherent in this owner-only route. The owner should approve this test with that limitation understood. A configuration preflight cannot establish that the account is uncompromised.

## Fixed current-host baseline

- Installing identity: the same current Windows SID as the existing protected ledger; raw SID remains only in ignored local authority evidence.
- Existing install: `C:\Program Files\PC Steward`; state: `C:\Program Files\PC Steward OwnerOnlyTest State`.
- Exact old ledger SHA-256: `05884590A90B1242B8963F01F3A9044963029C68F27F82757CD7E2FD43394585`.
- Old complete installed-manifest evidence SHA-256: `39A08C3396E23868DD894A581072E6FC2847F1488DFF09E7B912495D92D16F70`.
- Old owned signer: `61FC6E8B6F34488247EB799B54456BD1EC671B1C`, expires `2026-11-12T17:36:46Z`; owned CurrentUser My/Root/TrustedPublisher entries.
- `%LOCALAPPDATA%\PCSteward` remains absent, including database/sidecars; no PCSteward process. No live database exists to hash, copy or integrity-check. Continued absence must hold before removal, after removal and after installation. Stop on any drift and revise preservation rather than invent a backup pass.
- Old install remains a protection-valid **legacy-v6**, with the failed exact desktop DLL/PDB identity. It has never been launched in Task 48. Read-only old-profile validation is separate from new-profile acceptance.

## Exact candidate, scripts and caller

Payload: `%USERPROFILE%\AppData\Local\OwnerOnlyDeploymentCandidates\task-48-43F209AD1E79\payload`.

Manifest beside it: `unsigned-candidate-manifest.json`, SHA-256 **`43F209AD1E79E5B2A251F29D723059F419789E44FF72671D68D5C7C7A0B3B7AE`**. Same **102 files / 50,051,220 bytes / six XBFs** as the original reviewed candidate. Desktop assembly remains `12C1B111B9DD4832C5D0E49BB554A8766CC2B8795071A547102CEDB51450185D`; helper assembly `9FC90C8A4FCF9D695BAB3ADD1B5FE6B0AF8028D97EB4F409105C011139954D7D`. No rebuild or adoption of the mismatched installed desktop bytes.

Six script files have a separate normal local copy under that root's `scripts-v2` directory. The exact raw hashes and caller authority are pinned in ignored `artifacts/task-48-redeploy-plan-v2/authority.json`; the source/review pin manifest records all code and prepared-program hashes. Only those six fixed script names are accepted by bootstrap. The app never manages this user-writable preparation directory; it is not a security boundary or an implicit cleanup root. Earlier preparation artifacts without the `-v2` suffix are superseded and must not be executed.

Prepared protected execution root (currently absent): **`C:\Program Files\PC Steward OwnerOnlyTest Execution 2a3df1cb2e13455285979c597fe609c8`**. This is additional temporary owner-only infrastructure, not a PC Steward product data root/service/distribution channel. Code, compiler area and results there are retained for inspection; its later deletion needs separate exact authority. An atomic protected claim file prevents two executions of the same plan from writing results into each other's transaction.

Ignored prepared-program artifacts:

| Artifact | Exact identity |
|---|---|
| Plain owner-reviewable program | `artifacts/task-48-redeploy-plan-v2/bootstrap-plain.ps1` |
| Plain program SHA-256 | `74B6CE6AAF016D5D68025449712A587D5A6133A1D529C8DAE0217AF42A413B49` |
| Encoded command file | `artifacts/task-48-redeploy-plan-v2/bootstrap-encoded.txt` |
| Encoded command file SHA-256 | `3D3B1DD87FADB221C73E86C6BD447CD3CBD506A3C7D7FB35FFC28CD74B816C3F` |
| Exact non-elevated caller | `artifacts/task-48-redeploy-plan-v2/invoke-caller-v2.ps1` |
| Caller SHA-256 | `92B8DB08DD9CE23ECD9448B4E624F921C41140E56AA59523AC90F0EE4E55A21A` |
| Authority JSON SHA-256 | `A40853AD2C6FCCEEADF6BBD3FF6F5CC27F4C43EE5704AFF0F3E123D414B8AED2` |
| Command size / verification | 18,604 encoded characters; below 30,000 bound; strict program/stub parse and gzip/raw-byte round-trip pass |

The prepared caller defaults to **CheckOnly**. It hash-checks authority, all six source scripts and the encoded command, verifies the full 102-file candidate and old ledger, refuses a changed SID/data/process/root baseline, and rejects configured CLR override names or enforced Group Policy. It currently passes without elevation or execution. Actual execution needs explicit **`-ExecuteApprovedPlan`** after this exact plan is approved. Read the caller itself through a stable handle and compare its raw SHA-256 with the literal above before executing those captured bytes; do not load an unverified mutable caller path.

The caller reads the encoded file under a stable handle and literal hash, then supplies the immutable string to fixed system Windows PowerShell with **`-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand <verified string>`**. Bypass affects only that child process/session; it does not change CurrentUser/LocalMachine registry policy. The caller refuses any configured MachinePolicy/UserPolicy rather than overriding organization policy. Artifact hashes/protected code/CMS/ACL checks are not bypassed. This use removes hidden PowerShell prompt/policy variability for the specifically approved bytes. See [Microsoft's execution-policy scope documentation](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_execution_policies?view=powershell-5.1).

UAC launch uses `Start-Process -Verb RunAs -WindowStyle Hidden -PassThru` without `-Wait`, requesting native working directory System32. The original process object goes straight to the native waiter with **900,000 ms**. Native exit and a protected validated result are both required. A single-use `caller-execution.json` records PID/native exit/status/unknown outcomes, with no retry. Null/timeout/denial never means success. The prepared caller was added and checked by the parent after Opus's v4 re-check; the reviewed bootstrap/program artifacts are unchanged.

## Proposed single transaction and effects

1. Revalidate review/code/prepared-program pins, fixed candidate/manifest and source-copy hashes, same SID, old ledger/signer/manifest, absent user-data/execution roots and no PCSteward processes. Any drift stops before elevation. No OneDrive/reparse source is used for elevated reads.
2. Request UAC once for the exact encoded command. It creates the declared protected execution root/scripts/compiler directories with admin ownership and restrictive ACLs at creation. It copies/hashes the exact six scripts before executing them. System-only module path/PATH and protected PowerShell location are process-local. Set-Location does not change native CWD; the caller requests System32 and no stronger CWD assertion is claimed. Compiler TEMP/TMP are private; OWNER RIGHTS removes implicit owner WriteDac while inherited ACLs allow writes only to SYSTEM/Administrators.
3. Run the protected read-only validator with **`-AllowLegacyPayload`**. Require Pass/legacy-v6 and exact old ledger/signer identity, then revalidate candidate hashes. This does not accept the old desktop as the approved new artifact.
4. Run the unchanged protected copy of **`Remove-OwnerOnlyProtectedTestDeployment.ps1 -Remove` once**. This intentionally deletes the exact old protected install/state roots and the three ledger-owned old signer entries. It retains per-user data by contract. Require roots/old signer entries absent and data root still absent before continuing. No broad certificate/other application cleanup.
5. Run the protected amended installer once with **`-Install -CreateOwnerOnlyCertificate -CandidateRoot <exact payload> -CandidateManifestPath <exact manifest> -CandidateManifestSha256 43F209…B7AE`**. It freezes/verifies the complete staged payload before any signer mutation, creates a fresh 14-day non-exportable CurrentUser RSA-2048/SHA-256 signer, and trusts its public certificate only in CurrentUser Root/TrustedPublisher.
6. Sign/check both executables in protected staging with explicit SHA-256. Require raw-byte reconstruction to the manifest, strict CMS and Valid embedded same-signer Authenticode under a held handle before copying to the install root. Create all protected descendants with restrictive ACLs at creation; keep all non-signed bytes exact. Staging must be deleted before committing the completed protected ledger. Failure invokes only the installer's declared transaction-owned cleanup/incomplete ledger; it does not restore or replay the removed old deployment.
7. Run the protected independent validator with **`-RequirePinnedPayload`**. Require Pass and **exactly `payloadOrigin=pinned-unsigned-v1`**, the approved manifest hash, complete installed manifest/non-signed bytes, signed-executable raw reconstruction, trusted same signer, exact ownership/ACL/reparse/resource checks and continued data-root absence/no PCSteward process. A plain legacy Pass is insufficient.
8. Record protected removal/install/independent-validator JSON plus `result.json` in the execution root. Restore process TEMP/TMP in finally. Retain code/compiler/results even after partial failure; no implicit bootstrap cleanup, retry, forced termination, extra install or alternate scope/version follows.

## Stop/recovery and test limits

Expected Windows prompts, if presented, are **(1) UAC for system Windows PowerShell, (2) deletion of the old CurrentUser Root certificate `61FC6E8B6F34488247EB799B54456BD1EC671B1C`, (3) installation/trust of the freshly generated `CN=PC Steward Owner-Only Test <GUID>` Root certificate**. The approved test needs Yes for the matching expected prompts; an unexpected identity/prompt should be declined and the run stopped. The old remover deletes the installed binaries before certificate removal. Declining/missing the Root deletion prompt can therefore leave the old binaries gone, signer/state retained and phase `approved-removal`. Declining new Root trust can leave the old installation gone while the new installer rolls back its own transaction. Neither case authorizes a retry, automatic consent, alternative signer or restoration. Native prompt behavior with this exact hidden/noninteractive route is still a live-test observation, not proven by CheckOnly.

At most one transaction is approved if the owner accepts this plan. Allow 15 minutes after UAC returns for execution/certificate consent; timeout means pending/unknown and requires process/ledger/result inspection, not a kill or retry. Installation failure may leave the old deployment already removed and no new install; no automatic rollback to old bytes/signer is possible or implied. The existing guarded remover is the separately reviewable recovery route for any partial new deployment; further recovery actions need exact approval.

The first approved run is still required to prove elevated owner/DACL-at-creation behavior, actual compiler-file inheritance, Windows SHA-256 signing/trust, native RunAs exit capture and the new full profile. Local fixture/old-signature evidence must not be reported as those live results. No timestamp is used; signer expiry must fail closed rather than relax policy.

**App launch is excluded.** No scan, source refresh, software update, other app install/uninstall, schedule/data reset, production signing/public distribution, key export or network package operation is part of this plan. After all deployment gates pass, inspect startup effects and seek separate approval for a bounded responsive launch/normal close. Task 49 remains unstarted.
