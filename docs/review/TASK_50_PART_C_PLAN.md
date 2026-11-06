# Task 50 Part C — exact-artifact qualification run: route and decisions (revision 1, draft)

Requirements: `FR-UPD-15`, `TM-03/04/07/13`. ADRs: ADR-018 (cross-note), ADR-022, ADR-025 (amendment accepted 2026-11-04). Sources: the [activation host-session plan](TASK_50_ACTIVATION_HOST_SESSION_PLAN.md) §6 and §9 approval 2, the [qualification probe spec](TASK_50_QUALIFICATION_PROBE_SPEC.md) §8–§9 (S7 obligations, D4), the [exact-artifact test design](TASK_50_EXACT_ARTIFACT_TEST_DESIGN.md) C1–C7, and the [recovery plan](TASK_50_RECOVERY_PLAN.md) results. **Nothing here is approved.** This revision sets out the route and asks for decisions. Exact host blocks follow only after those decisions and the code they need.

## 1. Where things stand (2026-11-06)

- **Installed.** The v2 owner-only test deployment (`pinned-unsigned-v2`, helper protocol 2, manifest `23D7A71C…`), built from `febf1ac`.
  - Signer `473DB701…53FD`, which expires **2026-11-20T10:43:26Z**.
  - The database is unchanged (`CEB917F4…`). PC Steward has never been launched on this deployment.
- **Done.** Part A PASS (2026-11-03) and Part B (2026-11-06, through the recovery route).
- **The probe (S1–S6)** is implemented, reviewed and Windows-CI-tested on the Task 50 branch. It first landed in `5ae2de1`, **after** `febf1ac`.

## 2. Why Part C cannot run on the current install

The installed desktop predates the probe. Started with `--qualify-chocolatey-helper`, an old desktop ignores the flag, starts its normal UI and writes no result file (spec §6, "Old desktop, new helper"). S7 obligation 2 therefore applies: **C3 is never offered on the Part B deployment**, and C1 stops with `STOP: not a probe build`.

Owner Decision 3 (c) (2026-11-03) fixes the order: the probe ships **in the activation build**, and its first run is on the **activated deployment**. No Chocolatey review is confirmed until it passes.

## 3. The route, in order

| Step | What | Who / where | Approval |
|---|---|---|---|
| **C-D** | Owner decisions **D4** (code guard) and **G9** (activate with the A-Q1 real-launch residuals open?); see §4 | Owner | Decision text only |
| **C-A** | **Activation change** (code). Admit the Chocolatey routes: `HasSupportedMode`, `CapabilityOperationAllowlist` and dispatcher `Supports`, with their pins changed deliberately and reviewed. Add the D4 guard if one is chosen. The G3 builder record gains the "probe included" mark (S7 obligation 1). Add the S7 host tooling (C1–C7) as a reviewed, pinned script. Independent review and Windows CI | Cloud | ADR-031 decision 7 already approves activation once its gates are met. The owner's final admission approval (obligation 8) comes after Part C passes |
| **C-B** | **Redeploy the activated build**, keeping user data. This repeats the Part B stages with the reviewed tooling now in place. The v2 deployment is complete, so the normal v3 redeploy mode applies (`pinned-unsigned-v2` → `pinned-unsigned-v2`). Prep: worktree at the activation commit, G3 build, database copy, NewPlan, CheckOnly. Exec: one elevated run | Owner's own Windows PowerShell windows | Two approvals, as for 1a and 1b |
| **C-pre** | Host preconditions (S7 obligation 3). **This is the first normal launch.** The owner opens PC Steward, reads Activity → "Reviewed update outcomes", and closes it. No scan, no refresh, no scheduled check. A first launch may migrate the database, so the C2 baseline is taken **after** this step | Owner | Approval 2a |
| **C-run** | C1 pins, including the probe-build mark. C2 baseline (database plus `ProgramData\chocolatey` tree hash). C3 probe: one launch of `PCSteward.Desktop.exe --qualify-chocolatey-helper`, with exactly **one UAC prompt**, for `PCSteward.ElevatedHelper.exe`. C4 repeats C2. C5 reads the result file. C6 interprets it under spec §8. C7 cleans up `%TEMP%\pcsteward-exact-artifact`. With G-a or G-a′, the PASS-record copy | Owner's own console | Approval 2 (activation plan §9 template, filled) |

**Time bound.** C-B's preflight needs the current signer's signatures to be Valid. CheckOnly requires at least one hour of margin, so C-B exec must start before **2026-11-20T09:43:26Z**. If that is missed, the route is the R1/R2 pair instead (remover, then a fresh install of the activated build). The tooling for it now exists, but it needs its own approvals.

**Prompts.**
- **C-B exec:** UAC for Windows PowerShell. Then a Root-store **DELETE** prompt for the current signer `473DB701 E6349AF1 8F672151 E44AC166 F95C53FD` (the remover takes out the old deployment). Then the Security Warning for the new signer. Only on failure: a Root **DELETE** prompt for that same new signer, so the rollback completes.
- **C3:** exactly one UAC prompt. It names the signed helper, and its details show `--kind qualify-chocolatey-helper` right after the program path.
- Anything else → **No**.

## 4. Decisions needed now

**D4: code guard between the activation deploy and a recorded probe PASS** (spec §9).

| Option | Summary |
|---|---|
| G-0 | Procedure only. No code: until the probe passes, the owner does not confirm any Chocolatey review. |
| G-a | The PASS record lives in `%LOCALAPPDATA%\PCSteward`. Confirm stays disabled and elevation is refused without it. This needs factory-reset and ADR changes, and a reset deletes the record. |
| G-a′ | The PASS record lives in the ADR-022 state root, which only administrators can write. An elevated copy step is needed per PASS, plus validator, remover and ADR-022 note changes. |

**Recommendation: G-0.** On this host nothing can reach a real Chocolatey update before the probe runs:
- the reviewed release list holds only 2.7.5, which is already installed;
- there is no Chocolatey-managed target package (G9).

So the window that G-a or G-a′ would guard is empty in practice. Both also add reviewed code, ADR changes and, for G-a′, an extra elevated step. The guard can be reconsidered before any target package or newer release is added.

**G9: may activation proceed with the A-Q1 real-launch residuals open?** (activation plan §11).

The residuals are a native held DLL load, PowerShell helper reads and an elevated launcher holding files. They need a real Chocolatey client launch, which cannot happen without a target package or a newer listed release.

**Recommendation: yes, proceed.** The residuals stay recorded as open. Each one blocks any real Chocolatey update until it is exercised, for example in a VM or with a separately approved target package. Activation plus the probe proves UAC and pre-launch enforcement only (spec §8), and every later step and review will say so.

**Optional C0: an app-only smoke launch of the current v2 install now.** Open PC Steward, look at Overview and Activity, and close it within about a minute. No scan, no refresh, no scheduled check. It would catch a v2 UI problem before the activation work starts. It is the deployment's first launch, which may migrate the database (C-B re-pins it anyway). **Recommendation: optional, low risk.** It needs its own short approval.

## 5. What happens after the decisions

1. **Cloud.** The activation change (C-A) on a new branch from the Task 50 head: code, tests, the C1–C7 script with pins, independent review and Windows CI. The G9 decision is recorded in the admission record.
2. **Plan revision 2.** The exact C-B prep and exec blocks (with the redeploy prompts above), the C-pre instructions and the C-run blocks, with approvals 2a and 2 filled in.
3. **Owner runs.** C-B prep, then exec, before 2026-11-20 09:43Z; then C-pre and C-run. Each step runs once from the owner's own console, under the stop rules already used in Parts A and B.

## 6. Owner decisions (2026-11-06): "D4 G-0, G9 yes, C0 yes"

- **D4 = G-0.** No code guard; no §5.3a reader is built. Until a recorded overall probe PASS on the activated deployment, the owner confirms **no** Chocolatey review (Decision 2 as procedure). Reconsider before any target package or newer listed release is added.
- **G9 = yes.** Activation may proceed with the A-Q1 real-launch residuals open: a native held DLL load, PowerShell helper reads and an elevated launcher holding files. They stay recorded as open, and each blocks any real Chocolatey update until it is exercised.
- **C0 = yes.** One app-only smoke launch of the current v2 install, under the scope in §7.

## 7. C0 — app-only smoke launch (approved scope)

The owner does this personally:
1. Open `C:\Program Files\PC Steward\PCSteward.Desktop.exe` normally, as the normal user, not elevated.
2. Look at **Overview** and **Activity** only.
3. Close the window normally within about a minute.

Not part of C0:
- **Not allowed:** scan, source refresh, review, update, clear or reset; turning on scheduled checks; any change in Settings.
- **Answer No:** any UAC prompt.
- **Dismiss:** any onboarding choice that would turn on scheduled checks.

After closing, one read-only check runs in a normal Windows PowerShell window (block C0-check below). A first launch may migrate the database, so a changed hash is reported, not treated as a failure. C-B re-pins the database at its NewPlan.

```powershell
$db = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'PCSteward\pcsteward.db'; $dh = (Get-FileHash -LiteralPath $db -Algorithm SHA256).Hash; 'DB sha256=' + $dh + ' bytes=' + (Get-Item -LiteralPath $db).Length + ' unchanged=' + ($dh -ceq 'CEB917F40ED7B5101E2219501B00B852F9B424683EFA33E142FECA94694F6B99') + ' sidecars=' + @(@('-wal', '-shm', '-journal') | Where-Object { Test-Path -LiteralPath ($db + $_) }).Count + ' pcstewardProcesses=' + @(Get-Process -Name 'PCSteward*' -ErrorAction SilentlyContinue).Count + ' scheduledTask=' + @(Get-ScheduledTask -TaskName 'PCSteward.ScheduledCheck' -ErrorAction SilentlyContinue).Count
```

Report back: what the windows showed (anything unexpected), any prompt that appeared, and the C0-check line.

### Result C0 (2026-11-06)

The owner launched the installed v2 desktop once and closed it. C0-check printed `DB sha256=78697727CE44A1167537A04BE4554339028F660DA142E00894778D602BEC55BE bytes=200704 unchanged=False sidecars=0 pcstewardProcesses=0 scheduledTask=0`.

The database changed from 159,744 to 200,704 bytes. This is consistent with the first-launch schema migration that §7 expects. It closed cleanly: no WAL/SHM/journal sidecar, no PC Steward process left, and no scheduled task created. The new database pin for C-B is `78697727…55BE` / 200,704 B. C-B's NewPlan re-reads it, and B2's copy step backs it up first. Owner's note: "two screens looked good". Overview and Activity rendered normally, and the owner confirmed no UAC prompt appeared. **C0 PASS** (app-only smoke).

## 8. Owner approval: composition obligation 8 (2026-11-06)

The owner wrote in the cloud session: **"I approve the obligation 8 activation change now"**.

This approves the activation code change (step C-A): admitting `ChocolateyMachineRuntimeLimitsV3` and `ChocolateyClientSelfUpdateV1` at `HasSupportedMode`, `CapabilityOperationAllowlist` and dispatcher `Supports`, together with the non-admission pin changes. The change comes **before** the exact-artifact probe has run. Decision 3 (c) requires that order, which reconciles ADR-031 decision 7 and composition obligation 8.

The approval was asked for, and is recorded, on these terms:
- real Chocolatey updates stay blocked until the probe has an overall PASS on the activated deployment (D4 = G-0, procedure) and the owner gives the final admission approval;
- the A-Q1 residuals stay open (G9).

It covers the code change, its push to the Task 50 branch and Windows CI. Deploying it is step C-B, which still needs its own two approvals.

## 9. C-A built (2026-11-06; code and tests only; no host action)

The activation change is built on the local branch `task50-activation`, which is not pushed and still needs independent review and Windows CI. Details are in the [activation change record](TASK_50_ACTIVATION_CHANGE.md).

- **Gate inventory (§1).** Everything except Part C and the final admission approval is met, or covered by G9 and D4.
- **Admission.** Both routes are admitted at `HasSupportedMode` (exact shapes only), the capability allow-list and the dispatcher's `Supports`. The capability statement is now 149 B, with two tokens.
- **Probe-build mark.** The G3 record is schema v2 with `probeIncluded`, under rule `pc-steward-probe-build-mark:1`: a clean build, a commit that descends from `2172f95`, and the probe types present in the payload.
- **C1–C7 script.** `scripts/evidence/Task50-QualificationProbe.ps1`, CRLF SHA-256 `831B5E17…E77A`.
- **Finding.** The Desktop still composes no Chocolatey review, so Confirm stays disabled in this build. G-0 holds anyway.
- **Next.** Revision 2 of this plan: the exact C-B, C-pre and C-run blocks, once review and Windows CI have passed. C-B exec must start before 2026-11-20T09:43:26Z.

## 10. Stage C-B: redeploy the activated build (revision 2; build commit `ebb40618debccac3f489571f9c6f7e709f3a2e93`, Windows CI green)

**How to run.** Every step is run by the owner personally, in a **normal (not administrator) Windows PowerShell 5.1 window**: the blue *Windows PowerShell* from the Start menu, not PowerShell 7 and not a window started by the Claude app.
- Use a **new window for each step**, paste the step's lines exactly as written, and run each step once.
- After a `STOP:` line or any red error text, run nothing further and report the output with names and paths replaced by `<USER>`.
- Plan files and the build log are never printed or pasted.
- Keep PC Steward closed throughout. NewPlan, CheckOnly and the install refuse to run in a packaged (Claude app) process.

**Host facts this stage starts from.**
- The v2 deployment: manifest `23D7A71C…`, signer `473DB701E6349AF18F672151E44AC166F95C53FD`, which expires 2026-11-20T10:43:26Z.
- The database after C0: `78697727CE44A1167537A04BE4554339028F660DA142E00894778D602BEC55BE`, 200,704 B.
- **Deadline:** CB.8 must start before **2026-11-20T09:43:26Z**.

**New work folder:** `%USERPROFILE%\PCSteward-Task50C`. The Part B and R2 folders are left alone. Line **V3** starts every C-B step:

```powershell
$wk = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) 'PCSteward-Task50C'; $wt = Join-Path $wk 'ebb4061'; $sc = Join-Path $wt 'scripts'; $co = Join-Path $wk 'candidate'; $pd = Join-Path $wk 'plan'; $pf = Join-Path $pd 'redeploy-plan.json'; $ps = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
```

### C-B prep (approval CB-prep): seven steps, none elevated

**CB.1 — fetch the build commit and add a detached worktree (changes Git state; network).**

First `cd` into the PC Steward clone, then paste line V3 and these lines. The step:
- checks that this is the clone and that no Git hooks exist;
- runs `git fetch` of `claude/sweet-franklin-j3mw4a`, with gc and maintenance off for this command;
- checks that the commit is present;
- creates the work folder;
- runs `git worktree add --detach`.

The clone's branch and files are untouched.

Expected: `GIT-HOOKS postCheckout=False referenceTransaction=False`, `FETCH-EXIT 0 …`, `COMMIT-PRESENT-EXIT 0`, `WORKTREE-EXIT 0 …`.

```powershell
$ErrorActionPreference = 'Continue'; $rc = (Get-Location).ProviderPath; $env:GIT_TERMINAL_PROMPT = '0'; $ok = $false; $url = [string]($null | & git.exe -C $rc config --get remote.origin.url 2>$null); $hk = @($null | & git.exe -C $rc rev-parse --git-path hooks/post-checkout --git-path hooks/reference-transaction 2>$null); $hx = @($hk | ForEach-Object { $p = [string]$_; if (![IO.Path]::IsPathRooted($p)) { $p = Join-Path $rc $p }; (Test-Path -LiteralPath $p) -or (Test-Path -LiteralPath ($p + '.exe')) })
if (Test-Path -LiteralPath $wk) { 'STOP: work folder already exists' } elseif (!(Test-Path -LiteralPath (Join-Path $rc 'PCSteward.slnx')) -or $url -notmatch 'pc-steward') { 'STOP: not in the PC Steward clone' } elseif ($hx.Count -ne 2) { 'STOP: hook check failed' } else { 'GIT-HOOKS postCheckout=' + $hx[0] + ' referenceTransaction=' + $hx[1]; if ($hx -contains $true) { 'STOP: a Git hook exists' } else { $go = @($null | & git.exe -C $rc -c gc.auto=0 -c maintenance.auto=false fetch --no-tags origin claude/sweet-franklin-j3mw4a 2>&1); 'FETCH-EXIT ' + $LASTEXITCODE + ' gitLines=' + $go.Count; if ($LASTEXITCODE -eq 0) { $null | & git.exe -C $rc cat-file -e 'ebb40618debccac3f489571f9c6f7e709f3a2e93^{commit}'; 'COMMIT-PRESENT-EXIT ' + $LASTEXITCODE; $ok = $LASTEXITCODE -eq 0 } } }
if ($ok) { New-Item -ItemType Directory -Path $wk -ErrorAction Stop | Out-Null; $go = @($null | & git.exe -C $rc -c gc.auto=0 -c maintenance.auto=false worktree add --detach $wt ebb40618debccac3f489571f9c6f7e709f3a2e93 2>&1); 'WORKTREE-EXIT ' + $LASTEXITCODE + ' gitLines=' + $go.Count; if ($LASTEXITCODE -ne 0) { 'STOP: worktree add failed' } }
```

**CB.2 — verify the worktree and the host (read-only).**

Expected:
- `HEAD ebb40618debccac3f489571f9c6f7e709f3a2e93` and `DIRTY 0`;
- ten `SCRIPT CRLF …` lines;
- `PROBE-SCRIPT 831B5E17…E77A` and `CAPABILITY 50F169DD…654B`;
- `DB … equalsPin=True sidecars=0 pcstewardProcesses=0 scheduledTask=0`;
- `OLD-SIGNER inMy=1 notAfterUtc=2026-11-20T10:43:26.0000000Z hoursLeft=<n>`, with more than 24 hours left;
- `EOL CRLF`, then `CB2-OK`.

```powershell
$hd = [string]($null | & git.exe -C $wt rev-parse --verify HEAD); $dt = @($null | & git.exe -C $wt --no-optional-locks status --porcelain=v1 --untracked-files=all | Where-Object { $_ }).Count; 'HEAD ' + $hd; 'DIRTY ' + $dt
$pins = [ordered]@{ 'Install-OwnerOnlyProtectedTestDeployment.ps1' = @('F5FD3ADA4784DA795792E455890DDE41DA271E0EB199617760099C5B62116E3D', 'FA2479125A43FC5DCE00C6EB8BC0E840863BA5D207BCDA3560CEADFF66EF138F'); 'Test-OwnerOnlyProtectedTestDeployment.ps1' = @('CCA0ACA362A018E9009A481EBAFBA2EE9DBF9FE6BBC4DEF5725FF785A53D9447', '2CFB6C72121824784ECC634F13404848204E4FEABAA5F8574EB599F9409F8EE2'); 'Remove-OwnerOnlyProtectedTestDeployment.ps1' = @('F42D7E392FDA49199754E516E1ECC53D230FF49C53569548D86DC64388F4248D', 'E5C4796B66A7429FED36FC9D506577FBC9F88C4E903893300214E0FC40E099D7'); 'OwnerOnlyPayloadIntegrity.ps1' = @('0874B755D88800BEBDD0D093BF90152C0B36729A6A9BBBFBE78E71136EEBA0A6', '83B87BBDE621992E32C0F042D26ED0707C106E18DF5CDD0EDF0CEA84521DD09F'); 'OwnerOnlyExecution.ps1' = @('DDF07C71EB61E2BF5E69FBA6F1A672D6CC09B8668715A90119BD725023093F9C', 'A23F2BBB874A280B5C918DAEDBCB1E5316CACB33AA670FDA682C0E2BF2E0B948'); 'Wait-OwnerOnlyDeploymentProcess.ps1' = @('F24B3235FD34AA82421C576EB284C1F8C04D26D9C56170BE0175825ADDF46C59', '2BA1A90DD230CA8587F846F75B84691BA3EA4D27C20069192F46C6602FAC520B'); 'Invoke-OwnerOnlyApprovedRedeployment.ps1' = @('AB5D61F3C61D3FCC1F7DB14B5A43B1FEF6E0B6AF5278518110FAA856F89FAF44', 'A9775FCEAE897AEBA18C838DD5FCA9BC69E355ABA4376D0EE0481E9B39D831D7'); 'Invoke-OwnerOnlyRedeployCaller.ps1' = @('EA5618D18939F3514DD81CBFAEE23BCC304168C073D98843F0B2C3BD69A60191', '62DDE7B43EAE4D5193DFAF37F76F46CD8C1B3EA6B0916E35F4071DC0014CE3C8'); 'Copy-OwnerOnlyUserDatabase.ps1' = @('1954CEC3B5249CB14B6192EC22BD3949C393A0224AC19BD67D665A24FF6E3B03', '0E7FDC16464B19A6BBD0B5F55908ECD16FBE0C4B2DC21A6418CE86C58630D313'); 'New-OwnerOnlyCandidate.ps1' = @('B9341F75BBE2CC0CB22F5BFBB158F606CF7D04221C8C7E17E780FFF4EF5912FD', 'BB6E0A60A8324341A3D5DA1606977D23CF653B2480724EB33E092E1BEE7D68D7') }
$forms = @(); foreach ($n in $pins.Keys) { $h = (Get-FileHash -LiteralPath (Join-Path $sc $n) -Algorithm SHA256).Hash; $f = if ($h -ceq $pins[$n][0]) { 'LF' } elseif ($h -ceq $pins[$n][1]) { 'CRLF' } else { 'MISMATCH' }; $forms += $f; 'SCRIPT ' + $f + ' ' + $h + ' ' + $n }; $prh = (Get-FileHash -LiteralPath (Join-Path $sc 'evidence\Task50-QualificationProbe.ps1') -Algorithm SHA256).Hash; 'PROBE-SCRIPT ' + $prh; $cap = (Get-FileHash -LiteralPath (Join-Path $wt 'src\PCSteward.ElevatedHelper\PCSteward.DeploymentCapability.txt') -Algorithm SHA256).Hash; 'CAPABILITY ' + $cap
$db = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'PCSteward\pcsteward.db'; $dh = (Get-FileHash -LiteralPath $db -Algorithm SHA256).Hash; $dl = (Get-Item -LiteralPath $db).Length; $sd = @(@('-wal', '-shm', '-journal') | Where-Object { Test-Path -LiteralPath ($db + $_) }).Count; $pn = @(Get-Process -Name 'PCSteward*' -ErrorAction SilentlyContinue).Count; $st = @(Get-ScheduledTask -TaskName 'PCSteward.ScheduledCheck' -ErrorAction SilentlyContinue).Count; $dbOk = $dh -ceq '78697727CE44A1167537A04BE4554339028F660DA142E00894778D602BEC55BE' -and $dl -eq 200704; 'DB sha256=' + $dh + ' bytes=' + $dl + ' equalsPin=' + $dbOk + ' sidecars=' + $sd + ' pcstewardProcesses=' + $pn + ' scheduledTask=' + $st
$os = @(Get-ChildItem -LiteralPath 'Cert:\CurrentUser\My' | Where-Object { $_.Thumbprint -ceq '473DB701E6349AF18F672151E44AC166F95C53FD' }); $hl = if ($os.Count -eq 1) { [int]($os[0].NotAfter.ToUniversalTime() - [DateTime]::UtcNow).TotalHours } else { -1 }; 'OLD-SIGNER inMy=' + $os.Count + ' notAfterUtc=' + $(if ($os.Count -eq 1) { $os[0].NotAfter.ToUniversalTime().ToString('o') } else { 'none' }) + ' hoursLeft=' + $hl
$uf = @($forms | Sort-Object -Unique); if ($hd -ceq 'ebb40618debccac3f489571f9c6f7e709f3a2e93' -and $dt -eq 0 -and $uf.Count -eq 1 -and $uf[0] -cne 'MISMATCH' -and $prh -ceq '831B5E174E1E5965CA0EF7E325F06936C4AB697A00A373C554690824C230E77A' -and $cap -ceq '50F169DDE7EDF6C8A2FC1A2FCC763BD04B43ED61E102C806947689EBB573654B' -and $dbOk -and $sd -eq 0 -and $pn -eq 0 -and $st -eq 0 -and $os.Count -eq 1 -and $hl -gt 24) { 'EOL ' + $uf[0]; 'CB2-OK' } else { 'STOP: CB.2 verification failed' }
```

**CB.3 — build the activated candidate once (not elevated; restore may use the network; allow up to 45 minutes).**

The reviewed builder runs from the worktree. Its console output goes to `b1-build.log`, which is never pasted. It refuses an elevated token, an existing output folder, a dirty tree or a disallowed SDK.

Expected:
- `BUILDER BB6E0A60…68D7` and `BUILD-EXIT 0`;
- a `CANDIDATE` line with `validation=Pass origin=pinned-unsigned-v2 helperProtocol=2 commit=ebb40618debccac3f489571f9c6f7e709f3a2e93 dirty=False sdk=10.0.403 signed=False probeIncluded=True probeMark=pc-steward-probe-build-mark:1`;
- `MANIFEST-SHA256` equal to `MANIFEST-FILE-SHA256`, and `BUILD-RECORD-SHA256 <hash>` (needed later by the probe);
- `CB3-OK`.

On a stop, run CB.3-X once. A NuGet audit error is a stop that needs a reviewed package change; never suppress the audit and never retry.

```powershell
$bh = (Get-FileHash -LiteralPath (Join-Path $sc 'New-OwnerOnlyCandidate.ps1') -Algorithm SHA256).Hash; $lg = Join-Path $wk 'b1-build.log'; if ($bh -cne 'BB6E0A60A8324341A3D5DA1606977D23CF653B2480724EB33E092E1BEE7D68D7' -and $bh -cne 'B9341F75BBE2CC0CB22F5BFBB158F606CF7D04221C8C7E17E780FFF4EF5912FD') { 'STOP: builder hash differs' } elseif ((Test-Path -LiteralPath $co) -or (Test-Path -LiteralPath $lg)) { 'STOP: candidate folder or build log already exists' } else { 'BUILDER ' + $bh; $env:DOTNET_ROOT = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'Microsoft\dotnet'; $env:PATH = $env:DOTNET_ROOT + ';' + $env:PATH; $null | & $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $sc 'New-OwnerOnlyCandidate.ps1') -OutputRoot $co 2>&1 > $lg; 'BUILD-EXIT ' + $LASTEXITCODE }
$sj = Join-Path $co 'candidate-build.json'; if ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $sj)) { $j = [IO.File]::ReadAllText($sj) | ConvertFrom-Json; $mh = (Get-FileHash -LiteralPath (Join-Path $co 'candidate-manifest.json') -Algorithm SHA256).Hash; 'CANDIDATE validation=' + $j.validation + ' origin=' + $j.payloadOrigin + ' helperProtocol=' + $j.helperProtocol + ' files=' + $j.fileCount + ' bytes=' + $j.totalBytes + ' commit=' + $j.sourceCommit + ' dirty=' + $j.sourceDirty + ' sdk=' + $j.sdkVersion + ' signed=' + $j.signed + ' probeIncluded=' + $j.probeIncluded + ' probeMark=' + $j.probeMark; 'MANIFEST-SHA256 ' + $j.manifestSha256; 'MANIFEST-FILE-SHA256 ' + $mh; 'BUILD-RECORD-SHA256 ' + (Get-FileHash -LiteralPath $sj -Algorithm SHA256).Hash; if ($j.validation -ceq 'Pass' -and $j.payloadOrigin -ceq 'pinned-unsigned-v2' -and [string]$j.helperProtocol -ceq '2' -and $j.sourceCommit -ceq 'ebb40618debccac3f489571f9c6f7e709f3a2e93' -and $j.sourceDirty -eq $false -and $j.sdkVersion -ceq '10.0.403' -and $j.signed -eq $false -and $j.probeIncluded -eq $true -and $j.probeMark -ceq 'pc-steward-probe-build-mark:1' -and $mh -ceq $j.manifestSha256) { 'CB3-OK' } else { 'STOP: candidate summary not as expected' } } else { 'STOP: build did not pass' }
```

**CB.3-X — sanitized failure excerpt (read-only; only after a CB.3 stop).**

```powershell
$lg = Join-Path $wk 'b1-build.log'; $up = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile); if (Test-Path -LiteralPath $lg) { @(Get-Content -LiteralPath $lg | Where-Object { $_ -match "dotnet step '|OutputRoot|must|refus|differs|missing|Conflicting|failed|error" } | Select-Object -Last 5) | ForEach-Object { $_.Replace($up, '<PROFILE>').Replace($env:USERNAME, '<USER>').Replace($env:COMPUTERNAME, '<PC>') } } else { 'NO-LOG' }
```

**CB.4 — copy the database (read-only on the live file; PC Steward closed).**

Expected: `COPY-SCRIPT 0E7FDC16…D313`, `COPY-EXIT 0`, then `DBCOPY bytes=200704 sourceSha256=78697727… copySha256=78697727… hashesEqual=True sidecarsPresent=False pcstewardProcesses=0 headerValid=True …`, then `CB4-OK`.

```powershell
$ph = (Get-FileHash -LiteralPath (Join-Path $sc 'Copy-OwnerOnlyUserDatabase.ps1') -Algorithm SHA256).Hash; $cx = $null; if ($ph -cne '0E7FDC16464B19A6BBD0B5F55908ECD16FBE0C4B2DC21A6418CE86C58630D313' -and $ph -cne '1954CEC3B5249CB14B6192EC22BD3949C393A0224AC19BD67D665A24FF6E3B03') { 'STOP: copy script hash differs' } elseif (Test-Path -LiteralPath (Join-Path $wk 'dbcopy')) { 'STOP: copy folder already exists' } else { 'COPY-SCRIPT ' + $ph; $cr = @($null | & $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $sc 'Copy-OwnerOnlyUserDatabase.ps1') -DestinationParent (Join-Path $wk 'dbcopy')); $cx = $LASTEXITCODE; 'COPY-EXIT ' + $cx }
if ($cx -eq 0) { $r = ($cr -join "`n") | ConvertFrom-Json; 'DBCOPY bytes=' + $r.bytes + ' sourceSha256=' + $r.sourceSha256 + ' copySha256=' + $r.copySha256 + ' hashesEqual=' + $r.hashesEqual + ' sidecarsPresent=' + $r.sidecarsPresent + ' pcstewardProcesses=' + $r.pcstewardProcesses + ' headerValid=' + $r.sqliteHeader.headerValid + ' walMode=' + $r.sqliteHeader.walMode + ' pageCountMatchesLength=' + $r.sqliteHeader.pageCountMatchesLength; if ($r.hashesEqual -eq $true -and $r.sourceSha256 -ceq $r.copySha256 -and $r.sourceSha256 -ceq '78697727CE44A1167537A04BE4554339028F660DA142E00894778D602BEC55BE' -and $r.sqliteHeader.headerValid -eq $true) { 'CB4-OK' } else { 'STOP: database copy not as expected' } } else { 'STOP: copy did not pass' }
```

**CB.5 — NewPlan (redeploy v3: `pinned-unsigned-v2` → `pinned-unsigned-v2`, user data kept; writes one plan file).**

Expected:
- `CALLER 62DDE7B4…`;
- the NewPlan JSON with `"result": "Written"` and `"schema": "pc-steward-exact-redeploy-v3"`;
- `PLAN-FILE-SHA256` equal to `planSha256`, and `OLD-LEDGER-SHA256 <hash>`;
- `PLAN-CHECK dbPinEqualsCopy=True manifestSignerOrigins=True`, then `CB5-OK`.

```powershell
$cb = [IO.File]::ReadAllBytes((Join-Path $sc 'Invoke-OwnerOnlyRedeployCaller.ps1')); $ch = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($cb)).Replace('-', ''); if ($ch -cne '62DDE7B43EAE4D5193DFAF37F76F46CD8C1B3EA6B0916E35F4071DC0014CE3C8' -and $ch -cne 'EA5618D18939F3514DD81CBFAEE23BCC304168C073D98843F0B2C3BD69A60191') { 'STOP: caller hash differs' } elseif (Test-Path -LiteralPath $pd) { 'STOP: plan folder already exists' } else { 'CALLER ' + $ch; New-Item -ItemType Directory -Path $pd -ErrorAction Stop | Out-Null; & ([ScriptBlock]::Create([Text.Encoding]::UTF8.GetString($cb))) -NewPlanPath $pf -ScriptSource $sc -CandidateRoot (Join-Path $co 'payload') -CandidateManifestPath (Join-Path $co 'candidate-manifest.json') -ExpectedOldOrigin 'pinned-unsigned-v2' -ExpectedNewOrigin 'pinned-unsigned-v2' }
$rf = @(Get-ChildItem -LiteralPath (Join-Path $wk 'dbcopy') -Directory | ForEach-Object { Join-Path $_.FullName 'copy-record.json' }); if ((Test-Path -LiteralPath $pf) -and $rf.Count -eq 1) { $cr = [IO.File]::ReadAllText($rf[0]) | ConvertFrom-Json; $pj = [IO.File]::ReadAllText($pf) | ConvertFrom-Json; $dp = @($pj.userDataFiles | Where-Object { $_.path -ceq 'pcsteward.db' }); $mh = (Get-FileHash -LiteralPath (Join-Path $co 'candidate-manifest.json') -Algorithm SHA256).Hash; 'PLAN-FILE-SHA256 ' + (Get-FileHash -LiteralPath $pf -Algorithm SHA256).Hash; 'OLD-LEDGER-SHA256 ' + $pj.oldLedgerSha256; $m1 = @($pj.userDataFiles).Count -eq 1 -and $dp.Count -eq 1 -and $dp[0].sha256 -ceq $cr.sourceSha256 -and [long]$dp[0].bytes -eq [long]$cr.bytes; $m2 = $pj.schema -ceq 'pc-steward-exact-redeploy-v3' -and $pj.candidateManifestSha256 -ceq $mh -and $pj.oldThumbprint -ceq '473DB701E6349AF18F672151E44AC166F95C53FD' -and $pj.expectedOldOrigin -ceq 'pinned-unsigned-v2' -and $pj.expectedNewOrigin -ceq 'pinned-unsigned-v2'; 'PLAN-CHECK dbPinEqualsCopy=' + $m1 + ' manifestSignerOrigins=' + $m2; if ($m1 -and $m2) { 'CB5-OK' } else { 'STOP: plan pins differ from CB.3, CB.4 or the old signer' } } else { 'STOP: plan or copy record missing' }
```

**CB.6 — CheckOnly (read-only).** **Paste the whole output back.**

Expected:
- `CALLER …`, `PLAN-FILE-SHA256` equal to CB.5, and `EXECUTION-ROOT-ID <32 hex>`;
- the JSON with `"result": "Pass"` and schema `pc-steward-exact-redeploy-v3`;
- `encodedCommandLength` ≤ 30000;
- origins `pinned-unsigned-v2` and `pinned-unsigned-v2`, and the CB.3 manifest;
- `oldThumbprint` `473DB701…` and `oldSignerNotAfterUtc` `2026-11-20T10:43:26.0000000Z`;
- `userDatabase` `78697727…`/200704;
- `elevated` false, `executionRootAbsent` and `attemptRecordAbsent` true;
- `unpackagedProcess` true.

```powershell
$cb = [IO.File]::ReadAllBytes((Join-Path $sc 'Invoke-OwnerOnlyRedeployCaller.ps1')); $ch = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($cb)).Replace('-', ''); if ($ch -cne '62DDE7B43EAE4D5193DFAF37F76F46CD8C1B3EA6B0916E35F4071DC0014CE3C8' -and $ch -cne 'EA5618D18939F3514DD81CBFAEE23BCC304168C073D98843F0B2C3BD69A60191') { 'STOP: caller hash differs' } elseif (!(Test-Path -LiteralPath $pf)) { 'STOP: plan missing' } else { 'CALLER ' + $ch; 'PLAN-FILE-SHA256 ' + (Get-FileHash -LiteralPath $pf -Algorithm SHA256).Hash; $er = [string](([IO.File]::ReadAllText($pf) | ConvertFrom-Json).executionRoot); 'EXECUTION-ROOT-ID ' + $er.Substring($er.Length - 32); & ([ScriptBlock]::Create([Text.Encoding]::UTF8.GetString($cb))) -PlanPath $pf -CheckOnly }
```

**CB.7 — old-deployment dry run (read-only).**

CB.8's elevated preflight runs this validator against the installed deployment. Running it here first finds any failure before the exec approval is used.

Expected: `VALIDATOR 2CFB6C72…8EE2`, `VALIDATOR-EXIT 0`, `OLD-DEPLOYMENT validation=Pass origin=pinned-unsigned-v2 thumbprint=473DB701E6349AF18F672151E44AC166F95C53FD launched=False`, `CB7-OK`.

```powershell
$vh = (Get-FileHash -LiteralPath (Join-Path $sc 'Test-OwnerOnlyProtectedTestDeployment.ps1') -Algorithm SHA256).Hash; $vx = $null; if ($vh -cne '2CFB6C72121824784ECC634F13404848204E4FEABAA5F8574EB599F9409F8EE2' -and $vh -cne 'CCA0ACA362A018E9009A481EBAFBA2EE9DBF9FE6BBC4DEF5725FF785A53D9447') { 'STOP: validator hash differs' } else { 'VALIDATOR ' + $vh; $vo = @($null | & $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $sc 'Test-OwnerOnlyProtectedTestDeployment.ps1') -RequirePinnedPayload); $vx = $LASTEXITCODE; 'VALIDATOR-EXIT ' + $vx }
if ($vx -eq 0) { $v = ($vo -join "`n") | ConvertFrom-Json; 'OLD-DEPLOYMENT validation=' + $v.validation + ' origin=' + $v.payloadOrigin + ' thumbprint=' + $v.certificateThumbprint + ' launched=' + $v.launched; if ($v.validation -ceq 'Pass' -and $v.payloadOrigin -ceq 'pinned-unsigned-v2' -and $v.certificateThumbprint -ceq '473DB701E6349AF18F672151E44AC166F95C53FD') { 'CB7-OK' } else { 'STOP: old deployment not as expected' } } else { 'STOP: validator did not pass' }
```

### C-B exec (approval CB-exec): filled in with the CB.6 hashes (2026-11-06)

**CB.8 — one `-ExecuteApprovedPlan`.** This works like R2.5, but with the old-deployment preflight and the **guarded remover**. The remover removes the v2 deployment and its signer `473DB701…`; it never touches the data folder. It runs from a normal window, the owner stays at the PC, and PC Steward stays closed. Never retry, never close early, and never delete the attempt record.

**Prompts, in order:**
1. **UAC** for **Windows PowerShell**, verified publisher **Microsoft Windows** → **Yes**.
2. **Root Certificate Store**: **DELETE** subject `PC Steward Owner-Only Test 7343ceaa-acb6-4ada-88e8-288d0993a323`, thumbprint `473DB701 E6349AF1 8F672151 E44AC166 F95C53FD` → **Yes**.
3. **Security Warning**: install a certificate claiming to represent `PC Steward Owner-Only Test <new GUID>` → **Yes**. Note its subject's GUID or the first 8 characters of its thumbprint.
4. **Only if the install then fails:** a Root **DELETE** prompt for that **same new** certificate → **Yes**, so the rollback completes.
5. Anything else, a second UAC prompt, or a different subject or thumbprint → **No**.

Expected: `CALLER …`, then the attempt JSON with `"status": "Validated"`, exit codes 0, `"resultPhase": "validated"`, `userDatabaseSha256` `78697727…`, `"retry": false` and `"launched": false`.

```powershell
$cb = [IO.File]::ReadAllBytes((Join-Path $sc 'Invoke-OwnerOnlyRedeployCaller.ps1')); $ch = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($cb)).Replace('-', ''); if ($ch -cne '62DDE7B43EAE4D5193DFAF37F76F46CD8C1B3EA6B0916E35F4071DC0014CE3C8' -and $ch -cne 'EA5618D18939F3514DD81CBFAEE23BCC304168C073D98843F0B2C3BD69A60191') { 'STOP: caller hash differs' } else { 'CALLER ' + $ch; & ([ScriptBlock]::Create([Text.Encoding]::UTF8.GetString($cb))) -PlanPath $pf -ExecuteApprovedPlan -ApprovedPlanSha256 '28B3374D53A21893A195725569A70802815F56CCD06FADCC614BEA8F72FCE0BC' -ApprovedEncodedCommandSha256 '07559C00D7AA66A692E3ED00F09B50174474357534C8C90CAE405D8029DDA740' }
```

**CB.9 — post-checks (read-only, in a new window).**

Expected:
- `DB … equalsPin=True sidecars=0 pcstewardProcesses=0`;
- `VALIDATOR-EXIT 0`;
- `NEW-DEPLOYMENT validation=Pass origin=pinned-unsigned-v2 helperProtocol=2 manifest=<CB.3> thumbprint=<new> launched=False`;
- `INSTALLED-CAPABILITY 50F169DD…654B`;
- `STORE My new=1 old=0`, `STORE Root new=1 old=0` and `STORE TrustedPublisher new=1 old=0`, where `old` counts `473DB701…`, `ED6FBCE8…` and `14F34EFE…`;
- `NEW-SIGNER notAfterUtc=<about 14 days later> ownerOnlySubject=True`;
- `LEDGER-SHA256 <hash>`.

The new thumbprint, `notAfterUtc` and `LEDGER-SHA256` become the C-run pins.

```powershell
$db = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'PCSteward\pcsteward.db'; $dh = (Get-FileHash -LiteralPath $db -Algorithm SHA256).Hash; $dl = (Get-Item -LiteralPath $db).Length; $sd = @(@('-wal', '-shm', '-journal') | Where-Object { Test-Path -LiteralPath ($db + $_) }).Count; 'DB sha256=' + $dh + ' bytes=' + $dl + ' equalsPin=' + ($dh -ceq '78697727CE44A1167537A04BE4554339028F660DA142E00894778D602BEC55BE' -and $dl -eq 200704) + ' sidecars=' + $sd + ' pcstewardProcesses=' + @(Get-Process -Name 'PCSteward*' -ErrorAction SilentlyContinue).Count
$vh = (Get-FileHash -LiteralPath (Join-Path $sc 'Test-OwnerOnlyProtectedTestDeployment.ps1') -Algorithm SHA256).Hash; $vx = $null; if ($vh -cne '2CFB6C72121824784ECC634F13404848204E4FEABAA5F8574EB599F9409F8EE2' -and $vh -cne 'CCA0ACA362A018E9009A481EBAFBA2EE9DBF9FE6BBC4DEF5725FF785A53D9447') { 'STOP: validator hash differs' } else { $vo = @($null | & $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $sc 'Test-OwnerOnlyProtectedTestDeployment.ps1') -RequirePinnedPayload); $vx = $LASTEXITCODE; 'VALIDATOR-EXIT ' + $vx }
if ($vx -eq 0) { $v = ($vo -join "`n") | ConvertFrom-Json; 'NEW-DEPLOYMENT validation=' + $v.validation + ' origin=' + $v.payloadOrigin + ' helperProtocol=' + $v.helperProtocol + ' manifest=' + $v.candidateManifestSha256 + ' thumbprint=' + $v.certificateThumbprint + ' launched=' + $v.launched; $pfx = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles); 'INSTALLED-CAPABILITY ' + (Get-FileHash -LiteralPath (Join-Path $pfx 'PC Steward\PCSteward.DeploymentCapability.txt') -Algorithm SHA256).Hash; $nt = [string]$v.certificateThumbprint; foreach ($sn in @('My', 'Root', 'TrustedPublisher')) { $all = @(Get-ChildItem -LiteralPath ('Cert:\CurrentUser\' + $sn)); 'STORE ' + $sn + ' new=' + @($all | Where-Object { $_.Thumbprint -ceq $nt }).Count + ' old=' + @($all | Where-Object { $_.Thumbprint -ceq '473DB701E6349AF18F672151E44AC166F95C53FD' -or $_.Thumbprint -ceq 'ED6FBCE8BFB12A563B2DBE0776A9C4ACBE214C55' -or $_.Thumbprint -ceq '14F34EFEFA84C367F25917B4B28803DCECE85623' }).Count }; $nc = @(Get-ChildItem -LiteralPath 'Cert:\CurrentUser\My' | Where-Object { $_.Thumbprint -ceq $nt }); if ($nc.Count -eq 1) { 'NEW-SIGNER notAfterUtc=' + $nc[0].NotAfter.ToUniversalTime().ToString('o') + ' ownerOnlySubject=' + ($nc[0].Subject -like 'CN=PC Steward Owner-Only Test *') } else { 'STOP: new signer not found once in CurrentUser My' }; 'LEDGER-SHA256 ' + (Get-FileHash -LiteralPath (Join-Path $pfx 'PC Steward OwnerOnlyTest State\deployment.json') -Algorithm SHA256).Hash } else { 'STOP: validator did not pass' }
```

### Approvals

- **CB-prep** (CB.1–CB.7): a Git fetch and a detached worktree in the owner's clone; one build (network for restore); a database copy; one plan folder. Nothing is elevated, signed or installed. **Approved by the owner on 2026-11-06 ("I approve CB-prep").**
- **CB-exec** (CB.8 + CB.9): given separately, after the CB.6 output is pasted back and the block is filled in. **Approved by the owner on 2026-11-06 ("I approve CB-exec")**, for the CB.8 block filled with plan `28B3374D…` and encoded command `07559C00…`.
- **C-pre and C-run** (approval 2 of the activation plan): drafted after CB.9, using its pins.

### Result CB-prep, steps CB.1–CB.3 (2026-11-06)

- **CB.1:** `GIT-HOOKS postCheckout=False referenceTransaction=False`, `FETCH-EXIT 0 gitLines=3`, `COMMIT-PRESENT-EXIT 0`, `WORKTREE-EXIT 0 gitLines=2`.
- **CB.2:**
  - `HEAD ebb40618…` and `DIRTY 0`;
  - all ten scripts at their CRLF pins, including PayloadIntegrity `83B87BBD…` and the builder `BB6E0A60…`;
  - `PROBE-SCRIPT 831B5E17…E77A` and `CAPABILITY 50F169DD…654B`;
  - `DB 78697727… bytes=200704 equalsPin=True sidecars=0 pcstewardProcesses=0 scheduledTask=0`;
  - `OLD-SIGNER inMy=1 notAfterUtc=2026-11-20T10:43:26.0000000Z hoursLeft=327`;
  - `EOL CRLF`, `CB2-OK`.
- **CB.3:**
  - `BUILDER BB6E0A60…`, `BUILD-EXIT 0`;
  - `CANDIDATE validation=Pass origin=pinned-unsigned-v2 helperProtocol=2 files=103 bytes=51716503 commit=ebb40618debccac3f489571f9c6f7e709f3a2e93 dirty=False sdk=10.0.403 signed=False probeIncluded=True probeMark=pc-steward-probe-build-mark:1`;
  - **`MANIFEST-SHA256 9F6CC13668DE533CF070731C30E12E006D66EB9E1B3C3AB3E8C8A566FA0DF84A`**, equal to the manifest file's hash;
  - **`BUILD-RECORD-SHA256 04D4BF87810EE2D89DBF78913592E14BFAA6099770AF1AEE5EC98E52C990EBB9`**, the C-run `-ExpectedBuildRecordSha256`;
  - `CB3-OK`.

### Result CB-prep, steps CB.4–CB.6 (2026-11-06)

- **CB.4:** `COPY-SCRIPT 0E7FDC16…`, `COPY-EXIT 0`, `DBCOPY bytes=200704 sourceSha256=copySha256=78697727… hashesEqual=True sidecarsPresent=False pcstewardProcesses=0 headerValid=True walMode=True pageCountMatchesLength=True`, `CB4-OK`.
- **CB.5:** `CALLER 62DDE7B4…`, `"result": "Written"`, schema v3, `planSha256` `28B3374D53A21893A195725569A70802815F56CCD06FADCC614BEA8F72FCE0BC` equal to `PLAN-FILE-SHA256`, `OLD-LEDGER-SHA256 413F80D3FA3F947C7B0615113FDE43D9DD614E5A6422CE3918FB40732911E401`, `PLAN-CHECK dbPinEqualsCopy=True manifestSignerOrigins=True`, `CB5-OK`.
- **CB.6:**
  - `"result": "Pass"`, `EXECUTION-ROOT-ID 3cf323d337f44a0ea53be1c857dddd62`;
  - `programSha256` `9B713F40…DF36`, **`encodedCommandSha256` `07559C00D7AA66A692E3ED00F09B50174474357534C8C90CAE405D8029DDA740`**, `encodedCommandLength` 28944;
  - template `A9775FCE…`, six scripts at the CB.2 CRLF values;
  - origins v2 → v2, manifest `9F6CC136…`, 103 files / 51,716,503 B;
  - `oldLedgerSha256` `413F80D3…`, `oldThumbprint` `473DB701…`, `oldSignerNotAfterUtc` `2026-11-20T10:43:26.0000000Z`;
  - database `78697727…`/200,704, no sidecars, no process, scheduled check `AbsentOrDisabled`, `sqliteLoaded` false, `elevated` false;
  - execution root and attempt record absent;
  - `packageQueryResult` 15700, `unpackagedProcess` true.

CB.8 above now carries these two hashes. CB.7 runs next, then approval CB-exec.
- **CB.7:** `VALIDATOR 2CFB6C72…`, `VALIDATOR-EXIT 0`, `OLD-DEPLOYMENT validation=Pass origin=pinned-unsigned-v2 thumbprint=473DB701E6349AF18F672151E44AC166F95C53FD launched=False`, `CB7-OK`. **CB-prep complete: PASS.**

### Result CB.8 (2026-11-06): Validated

The owner ran CB.8 once, in their own normal Windows PowerShell window, starting at 19:53:36 UTC.
- **Prompts** (as the owner reported them): UAC, the Root delete for `473DB701…`, and the new-certificate warning. Nothing else, which is exactly the expected success path.
- `CALLER 62DDE7B4…`.
- **Attempt record:** `"status": "Validated"`, plan `28B3374D…`, encoded command `07559C00…`, `nativeExitCode` 0, `resultExitCode` 0, `resultPhase` `validated`.
- All failure fields are null.
- `userDatabaseSha256` `78697727…55BE`.
- `retry` false and `launched` false. Completed at 19:53:49 UTC, about 13 s.

CB.9 post-checks are pending.

### Result CB.9 (2026-11-06): PASS; C-B complete

- `DB sha256=78697727…55BE bytes=200704 equalsPin=True sidecars=0 pcstewardProcesses=0`
- `VALIDATOR-EXIT 0`
- `NEW-DEPLOYMENT validation=Pass origin=pinned-unsigned-v2 helperProtocol=2 manifest=9F6CC13668DE533CF070731C30E12E006D66EB9E1B3C3AB3E8C8A566FA0DF84A thumbprint=67B59B4C9BC3A006FBE39A72967E05F7B8C375DA launched=False`
- `INSTALLED-CAPABILITY 50F169DDE7EDF6C8A2FC1A2FCC763BD04B43ED61E102C806947689EBB573654B` (the two-token activated statement)
- `STORE My new=1 old=0`, `STORE Root new=1 old=0`, `STORE TrustedPublisher new=1 old=0`
- `NEW-SIGNER notAfterUtc=2026-11-20T19:53:44.0000000Z ownerOnlySubject=True`
- `LEDGER-SHA256 CBAB8C0AEB29DB34076F16A9176674CA08DA4A655447988B1368B456733AC852`

**C-run pins:**

| Pin | Value |
|---|---|
| manifest | `9F6CC136…F84A` |
| ledger | `CBAB8C0A…C852` |
| signer | `67B59B4C9BC3A006FBE39A72967E05F7B8C375DA` |
| signer NotAfter | `2026-11-20T19:53:44Z` |
| build record | `04D4BF87…EBB9` |

The activated, probe-carrying build is deployed and the database is unchanged. The new signer expires 2026-11-20T19:53:44Z, which bounds C-run.
