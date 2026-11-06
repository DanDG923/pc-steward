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

**R2 tooling built (2026-11-05, under D-R2; on the Task 50 branch from `c227743`, review fixes after it; not run on the host).** `FR-UPD-15`, `TM-03/04/07/13`, ADR-022.
- *Fresh-install mode.* `NewPlan -ExpectedOldOrigin none` writes plan schema `pc-steward-exact-fresh-install-v1`: the v3 key set without `oldLedgerSha256`/`oldThumbprint`, the same user-data pins and the same protected, hash-pinned execution copy. NewPlan and CheckOnly read no old ledger, signer or thumbprint. The shared `Assert-OwnerOnlyFreshInstallBaseline` (caller and template) requires absent install and state roots, no `CN=PC Steward Owner-Only Test *` certificate in CurrentUser My/Root/TrustedPublisher (read-only) and no `PCSteward*` process; the scheduled-check and user-data checks are the v3 ones. The template runs that preflight, has **no removal step**, re-checks the baseline and the scheduled check, then runs the installer (`-Install -CreateOwnerOnlyCertificate`), the independent validator (`-RequirePinnedPayload`) and the user-data re-check, and writes the same result and attempt records. Redeploy plans (v1/v2/v3) keep their statements and texts.
- *Packaged-process refusal.* `GetCurrentPackageFullName` (kernel32, bound in memory with Reflection.Emit: no `Add-Type`, compiler or TEMP file) must return exactly 15700. `-ExecuteApprovedPlan` refuses before the one-use claim, so the approval is not used; NewPlan and CheckOnly also refuse, so no plan is written and no hashes are offered for approval from a packaged process (independent review, finding 1). CheckOnly still reports `packageQueryResult` and `unpackagedProcess`; the elevated template refuses again right after the SID and elevation check, before creating anything.
- *Installer diagnostics.* A non-Valid `Set-AuthenticodeSignature` result now throws `Could not sign <leaf> with the owner-only certificate. Set-AuthenticodeSignature: Status=…; StatusMessage=[…]; SignerThumbprint=<thumbprint or none>; ExpectedThumbprint=…. Get-AuthenticodeSignature: Status=…; StatusMessage=[…]`; The same text reaches the template's `result.json` `failure` and the attempt record's `resultFailure`. It reaches the rollback ledger's `originalFailure` only when the rollback is incomplete, because a completed rollback removes the state root. Acceptance is unchanged (Valid and the owned signer's thumbprint).
- *Size.* Comments moved from the embedded helper and template into the caller header; no check was removed. Measured with the fixture's sample plan: redeploy 28,740 and fresh install 28,400 characters (.NET 10 zlib; 28,584 / 28,252 with classic zlib as in Windows PowerShell 5.1). The B-prep host plan measured 148 characters more than the sample, so about 28,750–28,900 is expected on the owner's PC.
- *New pins (LF / CRLF SHA-256).* `OwnerOnlyExecution.ps1` `DDF07C71…3F9C` / `A23F2BBB…B948`; `Invoke-OwnerOnlyApprovedRedeployment.ps1` `AB5D61F3…AF44` / `A9775FCE…31D7` (both embedded in the caller; the helper also in `Copy-OwnerOnlyUserDatabase.ps1`); `Install-OwnerOnlyProtectedTestDeployment.ps1` `F5FD3ADA…6E3D` / `FA247912…138F` (plan-pinned at NewPlan). The caller and copy-script hashes are in the commit report. Full values: `RedeployToolingScriptTests.CallerAndCopyConstantsPinTheCommittedHelperWaiterAndTemplate` recomputes them.
- *Tests.* `RedeployToolingScriptTests` and `Fixtures/RedeployTooling.ps1`: fresh-schema refusals; the template's fresh plan section, preflight, re-check and final guards from the real statements (with no old ledger on disk); the fresh/removal switch structure; canonical fresh plans; the fresh program's bound; packaged refusal (stubbed 122/0, real query 15700 on Windows); Windows-only NewPlan/CheckOnly fresh end to end and the real caller main dispatch with a fresh plan; installer diagnostic texts. Windows rows are Windows-CI evidence.
- *Independent review (2026-11-05): nothing High or Medium.* Fixed: the packaged-process refusal now also covers NewPlan and CheckOnly (finding 1). The fresh baseline treats any existing entry at the install or state root as present, including a dangling junction or symlink, through `File.GetAttributes` (finding 2). Noted, not changed: the certificate-present branch of the baseline is not exercised in CI, and no CI row builds a v3 CheckOnly report end to end. A leftover `CN=PC Steward Owner-Only Test Signing Diagnostic …` certificate would also be refused, which fails closed. Some error texts still say "Plan schema v3" for fresh plans.
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

## 6. Stage R2: exact steps (revision 1; tooling commit `385efe0a4fecdc39cb3faa08cb402e825fd729a8`, Windows CI green)

**How to run.** The owner runs every step personally in a **normal (not administrator) Windows PowerShell 5.1 window**, opened from the Start menu. That is the blue *Windows PowerShell*, not PowerShell 7 and not a window started by the Claude app.
- Use **a new window for each step**.
- Paste the step's lines exactly as written, once.
- If a line starts with `STOP:` or red error text appears, run nothing further and report the output with user names and paths replaced by `<USER>`.
- The tooling itself refuses to run inside the Claude app's process: NewPlan, CheckOnly and the install all require `GetCurrentPackageFullName` = 15700.
- Plan files are never printed or pasted, because they contain the SID and profile paths.

**What is reused from B-prep.** The work folder `%USERPROFILE%\PCSteward-Task50B` with the v2 candidate `candidate\` (manifest `23D7A71C…07B`, 103 files) is reused unchanged, with no rebuild. So are the B2 database copy under `dbcopy\` (a backup, not reused for pins) and the old `febf1ac\` worktree and `plan\` folder (left alone as evidence).

**What is new.**
- A detached worktree `r2-385efe0\` at the tooling commit.
- A plan folder `plan-r2\` holding `fresh-install-plan.json` and, in R2-exec, its one-use attempt record.

Line **V2**, which starts every R2 and R3 step:

```powershell
$wk = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) 'PCSteward-Task50B'; $wt = Join-Path $wk 'r2-385efe0'; $sc = Join-Path $wt 'scripts'; $co = Join-Path $wk 'candidate'; $pd = Join-Path $wk 'plan-r2'; $pf = Join-Path $pd 'fresh-install-plan.json'; $ps = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
```

### R2-prep (approval R2-prep): four steps, none elevated

**R2.1 — fetch the tooling commit and add a detached worktree (changes Git state; network).**

First `cd` into the PC Steward clone, the folder that holds `PCSteward.slnx`; this replaces B-prep's `<CHECKOUT>` substitution. Then paste line V2 and the lines below.

The step:
- checks that the folder is a clone whose `origin` names `pc-steward`;
- checks for Git hooks (B0.1's check), which must find none;
- runs `git fetch` of `claude/sweet-franklin-j3mw4a` with gc and maintenance off for this command only;
- checks that the commit is present;
- runs `git worktree add --detach`.

The clone's current branch and files are untouched.

Expected: `GIT-HOOKS postCheckout=False referenceTransaction=False`, `FETCH-EXIT 0 gitLines=<n>`, `COMMIT-PRESENT-EXIT 0`, `WORKTREE-EXIT 0 gitLines=<n>`.

```powershell
$ErrorActionPreference = 'Continue'; $rc = (Get-Location).ProviderPath; $env:GIT_TERMINAL_PROMPT = '0'; $ok = $false; $url = [string]($null | & git.exe -C $rc config --get remote.origin.url 2>$null); $hk = @($null | & git.exe -C $rc rev-parse --git-path hooks/post-checkout --git-path hooks/reference-transaction 2>$null); $hx = @($hk | ForEach-Object { $p = [string]$_; if (![IO.Path]::IsPathRooted($p)) { $p = Join-Path $rc $p }; (Test-Path -LiteralPath $p) -or (Test-Path -LiteralPath ($p + '.exe')) })
if (!(Test-Path -LiteralPath (Join-Path $co 'candidate-manifest.json'))) { 'STOP: B-prep candidate not found' } elseif (Test-Path -LiteralPath $wt) { 'STOP: R2 worktree folder already exists' } elseif (!(Test-Path -LiteralPath (Join-Path $rc 'PCSteward.slnx')) -or $url -notmatch 'pc-steward') { 'STOP: not in the PC Steward clone' } elseif ($hx.Count -ne 2) { 'STOP: hook check failed' } else { 'GIT-HOOKS postCheckout=' + $hx[0] + ' referenceTransaction=' + $hx[1]; if ($hx -contains $true) { 'STOP: a Git hook exists' } else { $go = @($null | & git.exe -C $rc -c gc.auto=0 -c maintenance.auto=false fetch --no-tags origin claude/sweet-franklin-j3mw4a 2>&1); 'FETCH-EXIT ' + $LASTEXITCODE + ' gitLines=' + $go.Count; if ($LASTEXITCODE -eq 0) { $null | & git.exe -C $rc cat-file -e '385efe0a4fecdc39cb3faa08cb402e825fd729a8^{commit}'; 'COMMIT-PRESENT-EXIT ' + $LASTEXITCODE; $ok = $LASTEXITCODE -eq 0 } } }
if ($ok) { $go = @($null | & git.exe -C $rc -c gc.auto=0 -c maintenance.auto=false worktree add --detach $wt 385efe0a4fecdc39cb3faa08cb402e825fd729a8 2>&1); 'WORKTREE-EXIT ' + $LASTEXITCODE + ' gitLines=' + $go.Count; if ($LASTEXITCODE -ne 0) { 'STOP: worktree add failed' } }
```

**R2.2 — verify the worktree, the candidate and the database (read-only).**

Expected:
- `HEAD 385efe0a4fecdc39cb3faa08cb402e825fd729a8` and `DIRTY 0`;
- ten `SCRIPT CRLF <hash> <name>` lines (CRLF as in B-prep; all LF is also accepted, but the two must not be mixed);
- `CAPABILITY 0772DAD8…D9C7`;
- `CANDIDATE-MANIFEST 23D7A71CBF8D14ED1471E0F0F625CF75F3D4C0A6CE0778D26C0DC17983F2807B`;
- `DB … equalsPin=True sidecars=0 pcstewardProcesses=0`;
- `EOL CRLF`, then `R2-VERIFY-OK`.

```powershell
$hd = [string]($null | & git.exe -C $wt rev-parse --verify HEAD); $dt = @($null | & git.exe -C $wt --no-optional-locks status --porcelain=v1 --untracked-files=all | Where-Object { $_ }).Count; 'HEAD ' + $hd; 'DIRTY ' + $dt
$pins = [ordered]@{ 'Install-OwnerOnlyProtectedTestDeployment.ps1' = @('F5FD3ADA4784DA795792E455890DDE41DA271E0EB199617760099C5B62116E3D', 'FA2479125A43FC5DCE00C6EB8BC0E840863BA5D207BCDA3560CEADFF66EF138F'); 'Test-OwnerOnlyProtectedTestDeployment.ps1' = @('CCA0ACA362A018E9009A481EBAFBA2EE9DBF9FE6BBC4DEF5725FF785A53D9447', '2CFB6C72121824784ECC634F13404848204E4FEABAA5F8574EB599F9409F8EE2'); 'Remove-OwnerOnlyProtectedTestDeployment.ps1' = @('F42D7E392FDA49199754E516E1ECC53D230FF49C53569548D86DC64388F4248D', 'E5C4796B66A7429FED36FC9D506577FBC9F88C4E903893300214E0FC40E099D7'); 'OwnerOnlyPayloadIntegrity.ps1' = @('57245268969A81F28D4DE0C3AB9F1D8F63195B29D3E9EDCFF49D63EFF2F9F9AB', 'EFF3840697893C8CF176435E3D4F5FB843811965FDBAE2883E97371E0CB6D987'); 'OwnerOnlyExecution.ps1' = @('DDF07C71EB61E2BF5E69FBA6F1A672D6CC09B8668715A90119BD725023093F9C', 'A23F2BBB874A280B5C918DAEDBCB1E5316CACB33AA670FDA682C0E2BF2E0B948'); 'Wait-OwnerOnlyDeploymentProcess.ps1' = @('F24B3235FD34AA82421C576EB284C1F8C04D26D9C56170BE0175825ADDF46C59', '2BA1A90DD230CA8587F846F75B84691BA3EA4D27C20069192F46C6602FAC520B'); 'Invoke-OwnerOnlyApprovedRedeployment.ps1' = @('AB5D61F3C61D3FCC1F7DB14B5A43B1FEF6E0B6AF5278518110FAA856F89FAF44', 'A9775FCEAE897AEBA18C838DD5FCA9BC69E355ABA4376D0EE0481E9B39D831D7'); 'Invoke-OwnerOnlyRedeployCaller.ps1' = @('EA5618D18939F3514DD81CBFAEE23BCC304168C073D98843F0B2C3BD69A60191', '62DDE7B43EAE4D5193DFAF37F76F46CD8C1B3EA6B0916E35F4071DC0014CE3C8'); 'Copy-OwnerOnlyUserDatabase.ps1' = @('1954CEC3B5249CB14B6192EC22BD3949C393A0224AC19BD67D665A24FF6E3B03', '0E7FDC16464B19A6BBD0B5F55908ECD16FBE0C4B2DC21A6418CE86C58630D313'); 'New-OwnerOnlyCandidate.ps1' = @('9DBCE1E2E5CFDB1C2775A85B5D5C570C9709068C464237DD1BAED3BD8D021785', 'BC67E825060E99AFE698E8C11C704498C4025C89BAEB1CF5EE4AC3EF6C975644') }
$forms = @(); foreach ($n in $pins.Keys) { $h = (Get-FileHash -LiteralPath (Join-Path $sc $n) -Algorithm SHA256).Hash; $f = if ($h -ceq $pins[$n][0]) { 'LF' } elseif ($h -ceq $pins[$n][1]) { 'CRLF' } else { 'MISMATCH' }; $forms += $f; 'SCRIPT ' + $f + ' ' + $h + ' ' + $n }; $cap = (Get-FileHash -LiteralPath (Join-Path $wt 'src\PCSteward.ElevatedHelper\PCSteward.DeploymentCapability.txt') -Algorithm SHA256).Hash; 'CAPABILITY ' + $cap
$mh = (Get-FileHash -LiteralPath (Join-Path $co 'candidate-manifest.json') -Algorithm SHA256).Hash; 'CANDIDATE-MANIFEST ' + $mh; $db = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'PCSteward\pcsteward.db'; $dh = (Get-FileHash -LiteralPath $db -Algorithm SHA256).Hash; $dl = (Get-Item -LiteralPath $db).Length; $sd = @(@('-wal', '-shm', '-journal') | Where-Object { Test-Path -LiteralPath ($db + $_) }).Count; $pn = @(Get-Process -Name 'PCSteward*' -ErrorAction SilentlyContinue).Count; $dbOk = $dh -ceq 'CEB917F40ED7B5101E2219501B00B852F9B424683EFA33E142FECA94694F6B99' -and $dl -eq 159744; 'DB sha256=' + $dh + ' bytes=' + $dl + ' equalsPin=' + $dbOk + ' sidecars=' + $sd + ' pcstewardProcesses=' + $pn
$uf = @($forms | Sort-Object -Unique); if ($hd -ceq '385efe0a4fecdc39cb3faa08cb402e825fd729a8' -and $dt -eq 0 -and $uf.Count -eq 1 -and $uf[0] -cne 'MISMATCH' -and $cap -ceq '0772DAD8365A8313125A364FBBD70DB2AE7BFC6C203732EA0751D75E579DD9C7' -and $mh -ceq '23D7A71CBF8D14ED1471E0F0F625CF75F3D4C0A6CE0778D26C0DC17983F2807B' -and $dbOk -and $sd -eq 0 -and $pn -eq 0) { 'EOL ' + $uf[0]; 'R2-VERIFY-OK' } else { 'STOP: R2 verification failed' }
```

**R2.3 — NewPlan in fresh-install mode (read-only host inspection; writes one plan file).**

The caller is hash-checked and runs from memory. It builds the canonical `pc-steward-exact-fresh-install-v1` plan, which contains:
- the current SID and a fresh execution-root GUID;
- the six protected-script hashes;
- the candidate manifest hash;
- the `pcsteward.db` pin.

Before writing, it re-checks:
- that the process is not packaged;
- the fresh baseline: no install or state root, no owner-only test certificate, no PC Steward process;
- the candidate tree, the scheduled check and the user data.

Expected: `CALLER <hash>`, then the NewPlan JSON with `"result": "Written"`, `"schema": "pc-steward-exact-fresh-install-v1"`, `"executed": false` and `"elevated": false`. Then `PLAN-FILE-SHA256` equal to `planSha256`, `PLAN-CHECK pins=True`, `R2-PLAN-OK`.

```powershell
$cb = [IO.File]::ReadAllBytes((Join-Path $sc 'Invoke-OwnerOnlyRedeployCaller.ps1')); $ch = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($cb)).Replace('-', ''); if ($ch -cne '62DDE7B43EAE4D5193DFAF37F76F46CD8C1B3EA6B0916E35F4071DC0014CE3C8' -and $ch -cne 'EA5618D18939F3514DD81CBFAEE23BCC304168C073D98843F0B2C3BD69A60191') { 'STOP: caller hash differs' } elseif (Test-Path -LiteralPath $pd) { 'STOP: plan folder already exists' } else { 'CALLER ' + $ch; New-Item -ItemType Directory -Path $pd -ErrorAction Stop | Out-Null; & ([ScriptBlock]::Create([Text.Encoding]::UTF8.GetString($cb))) -NewPlanPath $pf -ScriptSource $sc -CandidateRoot (Join-Path $co 'payload') -CandidateManifestPath (Join-Path $co 'candidate-manifest.json') -ExpectedOldOrigin 'none' -ExpectedNewOrigin 'pinned-unsigned-v2' }
if (Test-Path -LiteralPath $pf) { $pj = [IO.File]::ReadAllText($pf) | ConvertFrom-Json; $dp = @($pj.userDataFiles | Where-Object { $_.path -ceq 'pcsteward.db' }); 'PLAN-FILE-SHA256 ' + (Get-FileHash -LiteralPath $pf -Algorithm SHA256).Hash; $ok = $pj.schema -ceq 'pc-steward-exact-fresh-install-v1' -and $pj.expectedOldOrigin -ceq 'none' -and $pj.expectedNewOrigin -ceq 'pinned-unsigned-v2' -and $pj.candidateManifestSha256 -ceq '23D7A71CBF8D14ED1471E0F0F625CF75F3D4C0A6CE0778D26C0DC17983F2807B' -and @($pj.userDataFiles).Count -eq 1 -and $dp.Count -eq 1 -and $dp[0].sha256 -ceq 'CEB917F40ED7B5101E2219501B00B852F9B424683EFA33E142FECA94694F6B99' -and [long]$dp[0].bytes -eq 159744 -and $null -eq $pj.PSObject.Properties['oldLedgerSha256'] -and $null -eq $pj.PSObject.Properties['oldThumbprint']; 'PLAN-CHECK pins=' + $ok; if ($ok) { 'R2-PLAN-OK' } else { 'STOP: plan pins differ' } } else { 'STOP: plan missing' }
```

**R2.4 — CheckOnly (read-only).**

The caller re-reads the plan, requires its byte-canonical form, re-runs every check, and builds the elevated program and its encoded command without starting it. It reports hashes only.

Expected: `CALLER <hash>`, `PLAN-FILE-SHA256` equal to R2.3, `EXECUTION-ROOT-ID <32 hex>`, then the CheckOnly JSON with:
- `"result": "Pass"` and `"schema": "pc-steward-exact-fresh-install-v1"`;
- `encodedCommandLength` ≤ 30000;
- `bootstrapTemplateSha256` at the R2.2 template value;
- six `scripts` entries;
- origins `none` and `pinned-unsigned-v2`, and `candidateManifestSha256` `23D7A71C…`;
- `installRootAbsent` true, `stateRootAbsent` true and `ownerOnlyTestCertificates` 0;
- `userDatabase` at `CEB917F4…`/159744, `scheduledCheckTask` `AbsentOrDisabled`;
- `elevated` false, `executionRootAbsent` true and `attemptRecordAbsent` true;
- `packageQueryResult` 15700 and `unpackagedProcess` true.

**The owner pastes this whole output back.** The R2-exec block below is then filled in with its `planSha256` and `encodedCommandSha256` for a separate approval.

```powershell
$cb = [IO.File]::ReadAllBytes((Join-Path $sc 'Invoke-OwnerOnlyRedeployCaller.ps1')); $ch = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($cb)).Replace('-', ''); if ($ch -cne '62DDE7B43EAE4D5193DFAF37F76F46CD8C1B3EA6B0916E35F4071DC0014CE3C8' -and $ch -cne 'EA5618D18939F3514DD81CBFAEE23BCC304168C073D98843F0B2C3BD69A60191') { 'STOP: caller hash differs' } elseif (!(Test-Path -LiteralPath $pf)) { 'STOP: plan missing' } else { 'CALLER ' + $ch; 'PLAN-FILE-SHA256 ' + (Get-FileHash -LiteralPath $pf -Algorithm SHA256).Hash; $er = [string](([IO.File]::ReadAllText($pf) | ConvertFrom-Json).executionRoot); 'EXECUTION-ROOT-ID ' + $er.Substring($er.Length - 32); & ([ScriptBlock]::Create([Text.Encoding]::UTF8.GetString($cb))) -PlanPath $pf -CheckOnly }
```

### R2-exec (approval R2-exec): filled with the R2.4 hashes (2026-11-06)

**R2.5 — one `-ExecuteApprovedPlan`.** The owner runs it from a normal Windows PowerShell window and stays at the PC; PC Steward stays closed.

The caller:
- requires the approved plan hash;
- re-runs every check;
- refuses a packaged process before the one-use claim;
- claims `plan-r2\redeploy-attempt-<16 hex>.json`;
- starts exactly one hidden elevated Windows PowerShell with the encoded command.

The elevated program:
- creates the protected execution root and copies and re-hashes the six scripts;
- refuses a packaged process again;
- runs the fresh preflight and re-check, with **no removal step**;
- runs the installer once: a fresh 14-day signer, signing, protected ACLs;
- runs the independent validator;
- re-checks the database pin and writes `result.json`.

The caller waits at most 15 minutes and records the outcome. Task 48's comparable run took about 1.5 minutes. **Never retry, never close the window early, and never delete the attempt record.**

**Prompts, in order:**
1. **UAC** for **Windows PowerShell**, verified publisher **Microsoft Windows** → **Yes**.
2. **Security Warning** about installing a certificate claiming to represent `PC Steward Owner-Only Test <GUID>` → **Yes**. **Write down the first 8 characters of its thumbprint.**
3. **Only if the install then fails:** a **Root Certificate Store** prompt to **DELETE** that **same** subject and thumbprint → **Yes**, so that the rollback finishes.
4. Anything else, a second UAC prompt, or a different subject or thumbprint → **No**.

Expected:
- `CALLER <hash>`, then the attempt JSON with `"status": "Validated"`, `"nativeExitCode": 0`, `"resultExitCode": 0` and `"resultPhase": "validated"`;
- `userDatabaseSha256` `CEB917F4…6B99`;
- `"retry": false` and `"launched": false`.

```powershell
$cb = [IO.File]::ReadAllBytes((Join-Path $sc 'Invoke-OwnerOnlyRedeployCaller.ps1')); $ch = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($cb)).Replace('-', ''); if ($ch -cne '62DDE7B43EAE4D5193DFAF37F76F46CD8C1B3EA6B0916E35F4071DC0014CE3C8' -and $ch -cne 'EA5618D18939F3514DD81CBFAEE23BCC304168C073D98843F0B2C3BD69A60191') { 'STOP: caller hash differs' } else { 'CALLER ' + $ch; & ([ScriptBlock]::Create([Text.Encoding]::UTF8.GetString($cb))) -PlanPath $pf -ExecuteApprovedPlan -ApprovedPlanSha256 'E05D0D08532770495DDB5FA062E223A77C635F0ED43AE9AC33BF16AA4895030F' -ApprovedEncodedCommandSha256 '96A577D5085A8196E1AB3F4724FEA36F54CB8D4E8E5BBCDEB4A021EAB56EF212' }
```

**Stop rule.** The run must end `Validated`. Any other status (`NotStarted`, `StoppedOrUnknown`, `ValidatedPostcheckFailed`, `PendingOrUnknown`), `STOP:`, or an error is final. Report it and run nothing else. The recorded signing diagnostics will then say exactly why.

### R3 — read-only post-checks (in a new window, after a Validated R2.5)

Expected:
- `DB … equalsPin=True sidecars=0 pcstewardProcesses=0`;
- `VALIDATOR-EXIT 0`;
- `NEW-DEPLOYMENT validation=Pass origin=pinned-unsigned-v2 helperProtocol=2 manifest=23D7A71C… thumbprint=<new> launched=False`, where the new thumbprint matches what the owner wrote down;
- `STORE My new=1 old=0`, `STORE Root new=1 old=0` and `STORE TrustedPublisher new=1 old=0`, where `old` counts both `14F34EFE…` and `ED6FBCE8…`;
- `NEW-SIGNER notAfterUtc=<about 14 days later> ownerOnlySubject=True`.

**PC Steward is not launched.**

```powershell
$db = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'PCSteward\pcsteward.db'; $dh = (Get-FileHash -LiteralPath $db -Algorithm SHA256).Hash; $dl = (Get-Item -LiteralPath $db).Length; $sd = @(@('-wal', '-shm', '-journal') | Where-Object { Test-Path -LiteralPath ($db + $_) }).Count; 'DB sha256=' + $dh + ' bytes=' + $dl + ' equalsPin=' + ($dh -ceq 'CEB917F40ED7B5101E2219501B00B852F9B424683EFA33E142FECA94694F6B99' -and $dl -eq 159744) + ' sidecars=' + $sd + ' pcstewardProcesses=' + @(Get-Process -Name 'PCSteward*' -ErrorAction SilentlyContinue).Count
$vh = (Get-FileHash -LiteralPath (Join-Path $sc 'Test-OwnerOnlyProtectedTestDeployment.ps1') -Algorithm SHA256).Hash; $vx = $null; if ($vh -cne '2CFB6C72121824784ECC634F13404848204E4FEABAA5F8574EB599F9409F8EE2' -and $vh -cne 'CCA0ACA362A018E9009A481EBAFBA2EE9DBF9FE6BBC4DEF5725FF785A53D9447') { 'STOP: validator hash differs' } else { $vo = @($null | & $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $sc 'Test-OwnerOnlyProtectedTestDeployment.ps1') -RequirePinnedPayload); $vx = $LASTEXITCODE; 'VALIDATOR-EXIT ' + $vx }
if ($vx -eq 0) { $v = ($vo -join "`n") | ConvertFrom-Json; 'NEW-DEPLOYMENT validation=' + $v.validation + ' origin=' + $v.payloadOrigin + ' helperProtocol=' + $v.helperProtocol + ' manifest=' + $v.candidateManifestSha256 + ' thumbprint=' + $v.certificateThumbprint + ' launched=' + $v.launched; $nt = [string]$v.certificateThumbprint; foreach ($sn in @('My', 'Root', 'TrustedPublisher')) { $all = @(Get-ChildItem -LiteralPath ('Cert:\CurrentUser\' + $sn)); 'STORE ' + $sn + ' new=' + @($all | Where-Object { $_.Thumbprint -ceq $nt }).Count + ' old=' + @($all | Where-Object { $_.Thumbprint -ceq '14F34EFEFA84C367F25917B4B28803DCECE85623' -or $_.Thumbprint -ceq 'ED6FBCE8BFB12A563B2DBE0776A9C4ACBE214C55' }).Count }; $nc = @(Get-ChildItem -LiteralPath 'Cert:\CurrentUser\My' | Where-Object { $_.Thumbprint -ceq $nt }); if ($nc.Count -eq 1) { 'NEW-SIGNER notAfterUtc=' + $nc[0].NotAfter.ToUniversalTime().ToString('o') + ' ownerOnlySubject=' + ($nc[0].Subject -like 'CN=PC Steward Owner-Only Test *') } else { 'STOP: new signer not found once in CurrentUser My' } } else { 'STOP: validator did not pass' }
```

### Approvals

- **R2-prep** (R2.1–R2.4): a Git fetch and a new detached worktree in the owner's clone, plus one new plan folder. Nothing is elevated, signed or installed. **Approved by the owner on 2026-11-06 in the cloud session ("I approve R2-prep").**
- **R2-exec** (R2.5 + R3): given separately, after the R2.4 output is pasted back and the block is filled in with its two hashes.

### Result R2-prep (2026-11-06): PASS

The owner ran each step in their own normal Windows PowerShell 5.1 window, a new window per step.
- **R2.1:** `GIT-HOOKS postCheckout=False referenceTransaction=False`, `FETCH-EXIT 0 gitLines=3`, `COMMIT-PRESENT-EXIT 0`, `WORKTREE-EXIT 0 gitLines=2`.
- **R2.2:** `HEAD 385efe0a4fecdc39cb3faa08cb402e825fd729a8`, `DIRTY 0`, all ten scripts at their CRLF pins, `CAPABILITY 0772DAD8…D9C7`, `CANDIDATE-MANIFEST 23D7A71C…07B`, `DB … equalsPin=True sidecars=0 pcstewardProcesses=0`, `EOL CRLF`, `R2-VERIFY-OK`.
- **R2.3:** `CALLER 62DDE7B4…3CE3C8` (CRLF), `"result": "Written"`, schema `pc-steward-exact-fresh-install-v1`, `planSha256` `E05D0D08532770495DDB5FA062E223A77C635F0ED43AE9AC33BF16AA4895030F` equal to `PLAN-FILE-SHA256`, `PLAN-CHECK pins=True`, `R2-PLAN-OK`.
- **R2.4:**
  - `"result": "Pass"`, `EXECUTION-ROOT-ID ae66a440150e4e6f9e1eb1334c0055e9`;
  - `programSha256` `E049207E…8BCA`, `encodedCommandSha256` `96A577D5085A8196E1AB3F4724FEA36F54CB8D4E8E5BBCDEB4A021EAB56EF212`, `encodedCommandLength` 28644;
  - `bootstrapTemplateSha256` `A9775FCE…31D7`, six scripts at the R2.2 CRLF values;
  - origins `none` → `pinned-unsigned-v2`, manifest `23D7A71C…`, 103 files / 51,474,164 B;
  - `installRootAbsent`, `stateRootAbsent` true, `ownerOnlyTestCertificates` 0;
  - database `CEB917F4…`/159,744 B, no sidecars, no process, scheduled check `AbsentOrDisabled`, `sqliteLoaded` false, `elevated` false;
  - `executionRootAbsent` and `attemptRecordAbsent` true;
  - `packageQueryResult` 15700, `unpackagedProcess` true.

The R2.5 block above now carries these two hashes. It needs approval R2-exec.

### Result R2.5 (2026-11-06): Validated

The owner ran R2.5 personally in their own normal Windows PowerShell window, starting at 10:43:19 UTC. They posted the output without a separate typed "I approve R2-exec"; running the block themselves is recorded as the owner's go-ahead. Prompts, as the owner reported them: one UAC prompt and the certificate Security Warning, nothing else. That is exactly the expected success path, with no rollback prompt.
- `CALLER 62DDE7B43EAE4D5193DFAF37F76F46CD8C1B3EA6B0916E35F4071DC0014CE3C8`
- Attempt record: `"status": "Validated"`, `planSha256` `E05D0D08…030F`, `encodedCommandSha256` `96A577D5…F212`, `nativeExitCode` 0, `resultExitCode` 0, `resultPhase` `validated`.
- `resultFailure`, `postcheckFailure`, `recordFailure` and `failure` are all null.
- `userDatabaseSha256` `CEB917F40ED7B5101E2219501B00B852F9B424683EFA33E142FECA94694F6B99`.
- `retry` false, `launched` false. Completed 10:43:41 UTC, about 22 seconds.

### Result R3 (2026-11-06): PASS; recovery complete

The owner ran R3 in a new normal Windows PowerShell window.
- `DB sha256=CEB917F40ED7B5101E2219501B00B852F9B424683EFA33E142FECA94694F6B99 bytes=159744 equalsPin=True sidecars=0 pcstewardProcesses=0`
- `VALIDATOR-EXIT 0`
- `NEW-DEPLOYMENT validation=Pass origin=pinned-unsigned-v2 helperProtocol=2 manifest=23D7A71CBF8D14ED1471E0F0F625CF75F3D4C0A6CE0778D26C0DC17983F2807B thumbprint=473DB701E6349AF18F672151E44AC166F95C53FD launched=False`
- `STORE My new=1 old=0`, `STORE Root new=1 old=0`, `STORE TrustedPublisher new=1 old=0`; `old` counts both `14F34EFE…` and `ED6FBCE8…`.
- `NEW-SIGNER notAfterUtc=2026-11-20T10:43:26.0000000Z ownerOnlySubject=True`

The owner did not note the thumbprint from the Security Warning. The single new signer `473DB701…53FD` is the one the validator binds to the installed, signed payload, and it is the only owner-only test certificate in each store, so no other certificate can have been the one shown.

**Outcome.** The v2 owner-only test deployment (`pinned-unsigned-v2`, helper protocol 2, manifest `23D7A71C…`) is installed and independently validated. The user database is unchanged, and PC Steward has not been launched.

This completes Part B of the Task 50 activation host-session plan, through the recovery route instead of the B4 redeploy. The new signer expires on 2026-11-20. Launching the app (Part C) needs its own approval.

Owner cross-check in certmgr.msc (2026-11-06): Current User → Trusted Publishers holds exactly one certificate, "PC Steward Owner-Only Test 7343ceaa-acb6-4ada-88e8-288d0993a323". It is self-issued, for Code Signing, and expires on 20/11/2026, matching R3's single new signer and its `notAfterUtc`.
