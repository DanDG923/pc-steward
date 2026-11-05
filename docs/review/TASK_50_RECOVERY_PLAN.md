# Task 50 — recovery plan after the B-exec stop (revision 1)

Requirements: `FR-UPD-15`, `TM-03/04/07/13`; ADR-022. Source of facts: [failure record](../failures/task-50-part-b-exec.md). **Approvals (2026-11-05, owner, in the cloud session): "I approve D-R1 and D-R2".** D-R1 approves running Stage R1 exactly as written, by the owner, in their own elevated Windows PowerShell window. D-R2 approves building the R2 tooling only. R2-prep, R2-exec and R3 still need their own approvals.

## 1. Where things stand

- **Safe.**
  - The user database is unchanged (`CEB917F4…6B99`, 159,744 B).
  - No PC Steward is installed.
  - Neither test signer (`14F34EFE…5623`, `ED6FBCE8…4C55`) is in the real per-user My, Root or TrustedPublisher store; the owner confirmed this in certmgr.msc.
- **Left over from the stopped rollback.** `C:\Program Files\PC Steward OwnerOnlyTest State` still holds:
  - `deployment.json`: schema v6, `completed` false, `rollbackRequired` true, signer `ED6FBCE8…`, owned stores My, Root and TrustedPublisher;
  - the candidate manifest copy;
  - the protected staging folder.
- **Kept as evidence.** The execution root `…OwnerOnlyTest Execution 44f5b22d…`.
- **Cause.** Windows CI runs 1 and 2 show the v2 payload signs Valid wherever the signer's root is trusted. The installer's check fails, with `UnknownError` ("untrusted root") and the signature already written, when it is not trusted. On the PC, the installer ran inside a session started by the Claude desktop app, which sees a different per-user certificate store from the owner's own shell.
- **Rule from now on.** Every elevated or certificate step runs in a Windows PowerShell window that the owner opens. Every block refuses to run inside a packaged (app-container) process.

## 2. Why the existing tools are not enough

- The approved-redeploy route (`Invoke-OwnerOnlyRedeployCaller.ps1`) needs a **complete old deployment** that passes the validator. None exists now, so it would stop at `old-deployment-preflight`.
- The installer dot-sources a sibling script from its own folder. Running it elevated straight from the user-writable work folder would skip the protected, hash-pinned execution copy that ADR-022 and Task 48 require. **Rejected.**
- **The remover can be used as it is.** `Remove-OwnerOnlyProtectedTestDeployment.ps1` already recognises an incomplete deployment that has a recorded signer. It does not use `$PSScriptRoot`, so it can be hash-checked and run from memory, the same way the B4 caller was.

## 3. Plan (recommended): three stages

### Stage R1 — finish the cleanup (no new tooling)

One elevated Windows PowerShell window, opened by the owner, runs the febf1ac remover from the existing work folder once. It is hash-pinned to the reviewed LF bytes (`F42D7E39…248D`) or CRLF bytes (`E5C4796B…99D7`) and runs from memory.

**What it does:**
- removes the state root (ledger, manifest copy, staging);
- removes signer `ED6FBCE8…` from CurrentUser My, Root and TrustedPublisher if it is still present (it should not be);
- leaves `%LOCALAPPDATA%\PCSteward` and the execution root untouched.

The remover's own checks refuse, deleting nothing, in any of these cases:
- a reparse point or an ACL that is not exact;
- a different Windows user;
- a ledger it does not recognise;
- a running PC Steward process.

**Prompts, in order:**
1. **UAC**, when the owner opens Windows PowerShell with *Run as administrator*. It names **Windows PowerShell**, verified publisher **Microsoft Windows** → **Yes**.
2. **Only if `ED6FBCE8…` is still in the real Root store:** a **Root Certificate Store** prompt to **DELETE** subject `PC Steward Owner-Only Test <GUID>`, thumbprint `ED6FBCE8 BFB12A56 3B2DBE07 76A9C4AC BE214C55` → **Yes**.
3. Anything else → **No**.

**Block R1** (paste once into the elevated window; it prints either `STOP: …` or the remover's JSON):

```powershell
$ErrorActionPreference = 'Stop'; Add-Type -Namespace PCStewardRecovery -Name Package -MemberDefinition '[DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern int GetCurrentPackageFullName(ref int length, System.Text.StringBuilder name);'; $n = 0; $pkg = [PCStewardRecovery.Package]::GetCurrentPackageFullName([ref]$n, $null)
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator); $pf = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles); $st = Join-Path $pf 'PC Steward OwnerOnlyTest State'; $in = Join-Path $pf 'PC Steward'
$db = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'PCSteward\pcsteward.db'; $rm = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) 'PCSteward-Task50B\febf1ac\scripts\Remove-OwnerOnlyProtectedTestDeployment.ps1'
$rb = [IO.File]::ReadAllBytes($rm); $rh = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($rb)).Replace('-', ''); $led = $null; if (Test-Path -LiteralPath (Join-Path $st 'deployment.json')) { $led = Get-Content -LiteralPath (Join-Path $st 'deployment.json') -Raw | ConvertFrom-Json }
if ($pkg -ne 15700) { 'STOP: packaged process ' + $pkg } elseif (!$admin) { 'STOP: not elevated' } elseif ($rh -cne 'F42D7E392FDA49199754E516E1ECC53D230FF49C53569548D86DC64388F4248D' -and $rh -cne 'E5C4796B66A7429FED36FC9D506577FBC9F88C4E903893300214E0FC40E099D7') { 'STOP: remover hash differs ' + $rh } elseif (Test-Path -LiteralPath $in) { 'STOP: install root present' } elseif ($null -eq $led -or $led.certificateThumbprint -cne 'ED6FBCE8BFB12A563B2DBE0776A9C4ACBE214C55' -or $led.completed -or !$led.rollbackRequired) { 'STOP: state is not the B4 rollback ledger' } elseif ((Get-FileHash -LiteralPath $db -Algorithm SHA256).Hash -cne 'CEB917F40ED7B5101E2219501B00B852F9B424683EFA33E142FECA94694F6B99') { 'STOP: database changed' } elseif (@(Get-Process -Name 'PCSteward*' -ErrorAction SilentlyContinue).Count) { 'STOP: PC Steward running' } else { 'REMOVER ' + $rh; & ([ScriptBlock]::Create([Text.Encoding]::UTF8.GetString($rb))) -Remove }
```

**Block R1-check** (read-only; paste into the same window afterwards). Expected: `STATE False INSTALL False CERTS 0 DB True SIDECARS 0 EVIDENCE True`.

```powershell
$pf = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles); $db = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'PCSteward\pcsteward.db'
'STATE ' + (Test-Path -LiteralPath (Join-Path $pf 'PC Steward OwnerOnlyTest State')) + ' INSTALL ' + (Test-Path -LiteralPath (Join-Path $pf 'PC Steward')) + ' CERTS ' + @(foreach ($s in 'My', 'Root', 'TrustedPublisher') { Get-ChildItem -LiteralPath ('Cert:\CurrentUser\' + $s) | Where-Object { $_.Subject -like 'CN=PC Steward Owner-Only Test *' } }).Count + ' DB ' + ((Get-FileHash -LiteralPath $db -Algorithm SHA256).Hash -ceq 'CEB917F40ED7B5101E2219501B00B852F9B424683EFA33E142FECA94694F6B99') + ' SIDECARS ' + @(@('-wal', '-shm', '-journal') | Where-Object { Test-Path -LiteralPath ($db + $_) }).Count + ' EVIDENCE ' + (Test-Path -LiteralPath (Join-Path $pf 'PC Steward OwnerOnlyTest Execution 44f5b22d4271463eb91f9a00cda9ecdc'))
```

On `STOP:` or a PowerShell error, do nothing else and report it. Never retry.

### Stage R2 — fresh v2 install (small tooling change first, then two steps from the owner's console)

**Tooling** (cloud work, reviewed and tested on Windows CI; needs an approval to build):
1. **Fresh-install plan mode** for the caller and the elevated template, using the same protected, hash-pinned execution copy.
   - Preflight: install root absent; state root absent; no `CN=PC Steward Owner-Only Test *` certificate in CurrentUser My, Root or TrustedPublisher; user-data pins match; no PC Steward process; scheduled check inactive.
   - Then: no removal step; the installer; the independent validator; a user-data re-check.
2. **Packaged-process refusal** (`GetCurrentPackageFullName` must return 15700), in the non-elevated caller and again in the elevated template.
3. **Installer diagnostics.** On a non-Valid signing result, the failure text and the rollback ledger record `Status`, `StatusMessage`, the signer and a fresh `Get-AuthenticodeSignature` result.
4. Tests and CI for each of these. **Known risk:** the encoded command is 29,296 of 30,000 characters, so the template may need compacting.

**R2 tooling built (2026-11-05, under D-R2; branch `task50-r2-tooling`; not pushed, not run on the host).** `FR-UPD-15`, `TM-03/04/07/13`, ADR-022.
- *Fresh-install mode.* `NewPlan -ExpectedOldOrigin none` writes plan schema `pc-steward-exact-fresh-install-v1`: the v3 key set without `oldLedgerSha256`/`oldThumbprint`, the same user-data pins and the same protected, hash-pinned execution copy. NewPlan and CheckOnly read no old ledger, signer or thumbprint. The shared `Assert-OwnerOnlyFreshInstallBaseline` (caller and template) requires absent install and state roots, no `CN=PC Steward Owner-Only Test *` certificate in CurrentUser My/Root/TrustedPublisher (read-only) and no `PCSteward*` process; the scheduled-check and user-data checks are the v3 ones. The template runs that preflight, has **no removal step**, re-checks the baseline and the scheduled check, then runs the installer (`-Install -CreateOwnerOnlyCertificate`), the independent validator (`-RequirePinnedPayload`) and the user-data re-check, and writes the same result and attempt records. Redeploy plans (v1/v2/v3) keep their statements and texts.
- *Packaged-process refusal.* `GetCurrentPackageFullName` (kernel32, bound in memory with Reflection.Emit: no `Add-Type`, compiler or TEMP file) must return exactly 15700. `-ExecuteApprovedPlan` refuses before the one-use claim, so the approval is not used; CheckOnly reports `packageQueryResult` and `unpackagedProcess`; the elevated template refuses again right after the SID and elevation check, before creating anything.
- *Installer diagnostics.* A non-Valid `Set-AuthenticodeSignature` result now throws `Could not sign <leaf> with the owner-only certificate. Set-AuthenticodeSignature: Status=…; StatusMessage=[…]; SignerThumbprint=<thumbprint or none>; ExpectedThumbprint=…. Get-AuthenticodeSignature: Status=…; StatusMessage=[…]`; the rollback ledger's `originalFailure` carries the same text. Acceptance is unchanged (Valid and the owned signer's thumbprint).
- *Size.* Comments moved from the embedded helper and template into the caller header; no check was removed. Measured with the fixture's sample plan: redeploy 28,740 and fresh install 28,400 characters (.NET 10 zlib; 28,584 / 28,252 with classic zlib as in Windows PowerShell 5.1). The B-prep host plan measured 148 characters more than the sample, so about 28,750–28,900 is expected on the owner's PC.
- *New pins (LF / CRLF SHA-256).* `OwnerOnlyExecution.ps1` `9324DFB6…FBCF` / `EE3157A0…B2DD`; `Invoke-OwnerOnlyApprovedRedeployment.ps1` `AB5D61F3…AF44` / `A9775FCE…31D7` (both embedded in the caller; the helper also in `Copy-OwnerOnlyUserDatabase.ps1`); `Install-OwnerOnlyProtectedTestDeployment.ps1` `F5FD3ADA…6E3D` / `FA247912…138F` (plan-pinned at NewPlan). The caller and copy-script hashes are in the commit report. Full values: `RedeployToolingScriptTests.CallerAndCopyConstantsPinTheCommittedHelperWaiterAndTemplate` recomputes them.
- *Tests.* `RedeployToolingScriptTests` and `Fixtures/RedeployTooling.ps1`: fresh-schema refusals; the template's fresh plan section, preflight, re-check and final guards from the real statements (with no old ledger on disk); the fresh/removal switch structure; canonical fresh plans; the fresh program's bound; packaged refusal (stubbed 122/0, real query 15700 on Windows); Windows-only NewPlan/CheckOnly fresh end to end and the real caller main dispatch with a fresh plan; installer diagnostic texts. Windows rows are Windows-CI evidence.
- The exact R2-prep and R2-exec blocks still need writing and a separate owner approval.

**R2-prep** (owner's console, not elevated, read-only apart from a new work folder):
- check out the tooling commit into a new work folder;
- re-verify the existing B-prep candidate (`23D7A71C…`, 103 files) and reuse it unchanged, with no rebuild;
- run `NewPlan` and `CheckOnly` to produce the plan and encoded-command hashes.

**R2-exec** (one elevated run started from the owner's console). Prompts, in order:
1. **UAC** for **Windows PowerShell**, **Microsoft Windows** → **Yes**.
2. **Security Warning** about installing a certificate claiming to represent `PC Steward Owner-Only Test <GUID>` → **Yes**. **Note the first 8 characters of its thumbprint.**
3. **Only if the install then fails:** a **Root Certificate Store** prompt to **DELETE** that **same** subject and thumbprint → **Yes**, so that the rollback can finish. (B4's instructions said No to this prompt, which is why its rollback stopped.)
4. Anything else, a second UAC prompt, or a different subject or thumbprint → **No**.

### Stage R3 — read-only checks

These are the B5 checks, extended to confirm that neither old signer is present. They cover: the database pin, the validator `Pass` with `pinned-unsigned-v2` and protocol 2, the manifest `23D7A71C…`, the new signer exactly once in each of My, Root and TrustedPublisher, and `launched=False`. PC Steward is **not** launched; launching it has its own approval in Part C.

## 4. Residue accepted, not cleaned

- **The `ED6FBCE8…` private key file.** It is non-exportable and may sit in the Claude app's private per-user view. No certificate in any store refers to it, so it trusts nothing. Deleting it would mean touching paths outside the PC Steward roots, so it is left alone unless the owner asks.
- **A stale `ED6FBCE8…` TrustedPublisher entry** visible only inside that private view. Without a trusted root it makes nothing trusted, and it affects only processes inside that container.
- **Execution root `44f5b22d…`.** It stays as evidence and is removed later under its own approval.

## 5. Owner decisions

- **D-R1:** approve Stage R1 as written (the owner runs it).
- **D-R2:** approve building the R2 tooling (items 1–4). The exact R2 blocks then follow for a separate approval.

## Result R1 (2026-11-05): PASS

The owner ran Stage R1 in their own elevated Windows PowerShell window (`C:\WINDOWS\system32` prompt), with both blocks exactly as written.
- Block R1 printed `REMOVER E5C4796B66A7429FED36FC9D506577FBC9F88C4E903893300214E0FC40E099D7` (the reviewed CRLF bytes). The packaged-process, elevation, ledger, database and process checks all passed. The remover returned `{"deployment": "owner-only-protected-test", "removed": true, "recoveredIncompleteDeployment": true, "certificateThumbprint": "ED6FBCE8BFB12A563B2DBE0776A9C4ACBE214C55", "userDataPreserved": true}`.
- Block R1-check printed `STATE False INSTALL False CERTS 0 DB True SIDECARS 0 EVIDENCE True`.
- Prompts: the owner opened the window with *Run as administrator* (one UAC prompt) and reported no other prompt.

The host now matches the fresh-install preflight in R2: no install root, no state root, no owner-only test certificate in the owner's real CurrentUser My, Root or TrustedPublisher stores, and the database unchanged at its pin. Execution root `44f5b22d…` is kept as evidence.
