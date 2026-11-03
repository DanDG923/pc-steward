# Task 50 Part B — Stage B-prep handover for the host session on the owner's PC (revision 2)

Requirements: `FR-UPD-15`, `TM-03/04/06/07/13`; ADR-022. Authority: **approval 1a** of the [activation host-session plan](TASK_50_ACTIVATION_HOST_SESSION_PLAN.md) revision 2.1 (§9), once the owner has given it, and the owner-approved route of plan §10: a child Claude session that the cloud session starts in the owner's Remote Control bridge environment on the owner's PC (trusted device confirmed). This handover covers **Stage B-prep only (B0–B3)**. Stage B-exec (B4–B5, the elevated redeploy) gets its own handover after approval 1b has been filled from this stage's result and given. Nothing else is authorized.

The text below the line is that session's prompt, verbatim. Its blocks are byte-for-byte line V of plan §5.1 and the blocks of plan §5.2; if the two ever differ, nothing runs. Revision 2 applies the plan's revision 2.1 review disposition (`$null` standard input for every child process, captured Git output in B0.1 and B0.2, the B0.1 Git-hook check, `<CHECKOUT>` quoting, the NuGetAudit stop).

---

You are running a pre-approved preparation stage for the PC Steward project on the owner's Windows PC, as the owner's normal (non-administrator) Windows account. The owner approved exactly the steps below (approval 1a of the Task 50 activation host-session plan, revision 2.1). Follow them literally. You have no authority beyond them.

## What this stage does

It builds a new (unsigned) PC Steward candidate from one fixed commit and prepares, but does **not** run, the redeploy that will later replace the installed test deployment while keeping the owner's data:
- **B0** checks the PC, fetches commit `febf1ac95c207aadf0f3efe742b854d81f903037` into the owner's clone and checks it out as a separate, detached Git worktree in `%USERPROFILE%\PCSteward-Task50B\febf1ac`, outside OneDrive; then verifies its script hashes.
- **B1** builds the candidate with the reviewed builder.
- **B2** copies the PC Steward database as a backup.
- **B3** creates the redeploy plan (NewPlan), checks it (CheckOnly), and runs the read-only validator against the installed deployment.

Nothing in this stage elevates, writes under Program Files, changes certificates or launches PC Steward.

## Rules (all mandatory)

1. Run **only** the blocks in "Steps", in order, each **once**. Never retry, re-run, edit, reformat, re-wrap or "fix" a block. The only substitution is `<CHECKOUT>` in steps 1 and 2 (the same path both times, with every `'` in it doubled to `''`, because it sits inside a single-quoted string). Only step 1 (B0.1, read-only) may be repeated, and only after the owner says they corrected the precondition it reported. Apart from the blocks, you may run only the read-only checks named in "Before you start".
2. Do **not**:
   - run any Git command yourself, other than those inside the blocks and the two read-only checks in "Before you start"; never checkout, switch, stash, reset, clean, commit, push, pull, merge, change Git config, or touch the clone's branch (`phase/5-v2-planning`), its index, its working tree or its modified and untracked files;
   - elevate, use `runas`, `Start-Process -Verb RunAs`, or the caller's `-ExecuteApprovedPlan`; run any script that is not named in a block;
   - write under `C:\Program Files` or `C:\Windows`; change certificates or trust stores, scheduled tasks, the registry, ACLs, services or policies;
   - launch, scan or update PC Steward; open or read `%LOCALAPPDATA%\PCSteward` other than through the blocks;
   - install anything; use the network except through the blocks' `git fetch` (step 2) and the builder's `dotnet restore` (step 4);
   - delete, move or rename anything, including the work folder after a failure;
   - print or paste the plan file (`plan\redeploy-plan.json`), the build log (`b1-build.log`) or raw Git or dotnet console text.
3. **Stop rule:** if a line starts with `STOP:`, an `…-EXIT` value is not 0, a CheckOnly or NewPlan report says `"result": "Fail"`, or PowerShell prints an error, run no further step (except B1-X after a B1 stop). Git's own progress or warning text is never an error by itself (steps 1 and 2 capture it and print only `gitLines=<n>`); only the `STOP:` lines and `…-EXIT` values decide. Never retry. Report the stop token or the sanitized error message (rule 4) and the step it came from.
4. **Privacy:** your report must not contain user names, the computer name, profile paths, SIDs, the plan JSON, the build log, or Git or dotnet output. When you report an error message, replace the profile path with `<PROFILE>`, the user name with `<USER>`, the computer name with `<PC>` and any `S-1-5-21-…` value with `<SID>`. The NewPlan and CheckOnly JSON reports contain only hashes and may be copied verbatim (check them anyway).
5. **Prompts:** this stage expects **no** UAC prompt and **no** certificate prompt. If the owner reports one, they answer **No**, and you stop and report it. A Git Credential Manager sign-in window may appear in step 2; the owner decides whether to sign in. You cannot see or answer any prompt.
6. **Timeouts:** give each step a tool timeout of at least 10 minutes. **Step 4 (B1)** can take longer: start it in the background (for example the Bash tool's background option) with a limit of at least 45 minutes, wait for it to finish, and never stop, kill or restart it.
7. If anything is unclear, stop and ask the owner. Do not improvise.

## How to run each block

Each block runs in its own fresh 64-bit Windows PowerShell 5.1 process fed by a single-quoted heredoc, so the lines run exactly as written and nothing is expanded by the outer shell. From Git Bash use exactly:

```bash
powershell.exe -NoLogo -NoProfile -NonInteractive -Command - <<'PS'
<line V>
<the block's lines, unchanged>
PS
```

If your shell tool is PowerShell instead, run `& "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -Command -` with line V and the block's lines on its standard input, unchanged. Inside the blocks, every child process gets `$null |` as its standard input, so it cannot consume the remaining lines; do not remove it. Every block starts with **line V**:

```powershell
$wk = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) 'PCSteward-Task50B'; $wt = Join-Path $wk 'febf1ac'; $sc = Join-Path $wt 'scripts'; $co = Join-Path $wk 'candidate'; $pd = Join-Path $wk 'plan'; $pf = Join-Path $pd 'redeploy-plan.json'; $ps = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
```

## Before you start (read-only; ask the owner)

1. Find `<CHECKOUT>`: the absolute Windows path of the owner's PC Steward clone (the folder this session was started in, if it is the clone; in Git Bash `pwd -W`). You may run `git -C <CHECKOUT> rev-parse --show-toplevel` and `git -C <CHECKOUT> branch --show-current` (both read-only); the second must print `phase/5-v2-planning`. If not, stop and ask. When you substitute it in steps 1 and 2, double every `'` in the path (for example `C:\Users\o'neil\…` becomes `C:\Users\o''neil\…`) and change nothing else.
2. Ask the owner to confirm, and wait for the answer: (a) PC Steward is closed (no window, no tray, nothing running); (b) scheduled checks are off (on this PC the `PCSteward.ScheduledCheck` task was absent at inventory; step 1 checks it again); (c) they will not open PC Steward until the later B-exec stage is complete; (d) approval 1a is given.

## Steps

**Step 1 — B0.1 preflight (read-only).** Substitute `<CHECKOUT>` (rule 1). Expected: `HOST`, `SCHEDULED-CHECK`, `DATA`, `PATHS`, `TOOLS`, `GIT-HOOKS`, `POLICY` and `OLD-SIGNER` lines, then `PREFLIGHT-OK`. On this PC expect `SCHEDULED-CHECK Absent`, `DATA database=True sidecars=0`, `TOOLS sdks=10.0.403 git=True`, `GIT-HOOKS checked=True postCheckout=False referenceTransaction=False`, `OLD-SIGNER inMy=1 notAfterUtc=2026-11-13T20:18:09.0000000Z`. On `STOP: preflight <names>`: tell the owner which precondition failed; after the owner says it is corrected you may run step 1 again. If the names include `gitHook`, the clone has a Git hook that step 2 would run: do not look at, move or change it; tell the owner, who either moves it aside personally before you re-run step 1 or stops this stage for a new approval.

```powershell
$bad = [Collections.Generic.List[string]]::new(); if ($PSVersionTable.PSEdition -cne 'Desktop' -or $PSVersionTable.PSVersion.Major -ne 5 -or ![Environment]::Is64BitProcess -or !$PSHOME.TrimEnd('\').Equals('C:\Windows\System32\WindowsPowerShell\v1.0', [StringComparison]::OrdinalIgnoreCase)) { $bad.Add('host') }; $el = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator); if ($el) { $bad.Add('elevated') }; $pr = @(Get-Process -Name 'PCSteward*' -ErrorAction SilentlyContinue).Count; if ($pr) { $bad.Add('pcstewardProcess') }; 'HOST ps=' + $PSVersionTable.PSVersion + ' x64=' + [Environment]::Is64BitProcess + ' elevated=' + $el + ' pcstewardProcesses=' + $pr
Import-Module ([IO.Path]::Combine($PSHOME, 'Modules', 'ScheduledTasks', 'ScheduledTasks.psd1')) -ErrorAction Stop; $ev = $null; $tk = @(Get-ScheduledTask -TaskName 'PCSteward.ScheduledCheck' -ErrorAction SilentlyContinue -ErrorVariable ev); $qe = @($ev | Where-Object { [string]$_.FullyQualifiedErrorId -notlike 'CmdletizationQuery_NotFound*' }).Count; $st = if ($qe) { 'QueryError' } elseif ($tk.Count -eq 0) { 'Absent' } elseif (@($tk | Where-Object { [string]$_.State -cne 'Disabled' }).Count -eq 0) { 'Disabled' } else { 'Enabled:' + (@($tk | ForEach-Object { [string]$_.State }) -join ',') }; if ($st -cnotin @('Absent', 'Disabled')) { $bad.Add('scheduledCheck') }; 'SCHEDULED-CHECK ' + $st + ' errorIds=' + (@($ev | ForEach-Object { [string]$_.FullyQualifiedErrorId }) -join ';')
$la = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData); $db = Join-Path $la 'PCSteward\pcsteward.db'; $dbOk = Test-Path -LiteralPath $db -PathType Leaf; $sd = @(@('-wal', '-shm', '-journal') | Where-Object { Test-Path -LiteralPath ($db + $_) }).Count; if (!$dbOk) { $bad.Add('database') }; if ($sd) { $bad.Add('sidecar') }; 'DATA database=' + $dbOk + ' sidecars=' + $sd
$up = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile); $rp = $false; for ($i = Get-Item -LiteralPath $up -Force; $null -ne $i; $i = $i.Parent) { if ($i.Attributes -band [IO.FileAttributes]::ReparsePoint) { $rp = $true } }; $we = Test-Path -LiteralPath $wk; if ($rp) { $bad.Add('profileReparse') }; if ($up.Contains('~')) { $bad.Add('profileShortName') }; if ($we) { $bad.Add('workFolderExists') }; 'PATHS profileReparse=' + $rp + ' profileShortName=' + $up.Contains('~') + ' workFolderExists=' + $we
$dn = Join-Path $la 'Microsoft\dotnet\dotnet.exe'; $sdks = @(); if (Test-Path -LiteralPath $dn -PathType Leaf) { $sdks = @($null | & $dn --list-sdks | ForEach-Object { ([string]$_).Split(' ')[0] }) }; if ($sdks -cnotcontains '10.0.403') { $bad.Add('sdk') }; if (@($sdks | Where-Object { $_ -match '^10\.0\.4\d\d$' -and $_ -cne '10.0.403' }).Count) { $bad.Add('sdkRollForward') }; $gt = [bool](Get-Command -Name 'git.exe' -CommandType Application -ErrorAction SilentlyContinue); if (!$gt) { $bad.Add('git') }; 'TOOLS sdks=' + ($sdks -join ',') + ' git=' + $gt
$ErrorActionPreference = 'Continue'; $rc = '<CHECKOUT>'; $hx = @(); if ($gt) { $hk = @($null | & git.exe -C $rc rev-parse --git-path hooks/post-checkout --git-path hooks/reference-transaction 2>&1); if ($LASTEXITCODE -eq 0 -and $hk.Count -eq 2) { $hx = @($hk | ForEach-Object { $p = [string]$_; if (![IO.Path]::IsPathRooted($p)) { $p = Join-Path $rc $p }; (Test-Path -LiteralPath $p) -or (Test-Path -LiteralPath ($p + '.exe')) }) } }; if ($hx.Count -ne 2) { $bad.Add('checkout') } elseif ($hx -contains $true) { $bad.Add('gitHook') }; 'GIT-HOOKS checked=' + ($hx.Count -eq 2) + ' postCheckout=' + ($hx.Count -eq 2 -and $hx[0]) + ' referenceTransaction=' + ($hx.Count -eq 2 -and $hx[1])
$pol = @(@('MachinePolicy', 'UserPolicy') | ForEach-Object { [string](Get-ExecutionPolicy -Scope $_) }); if (@($pol | Where-Object { $_ -cne 'Undefined' }).Count) { $bad.Add('enforcedExecutionPolicy') }; $clr = @(@([Environment]::GetEnvironmentVariables().Keys) + @([Environment]::GetEnvironmentVariables([EnvironmentVariableTarget]::User).Keys) + @([Environment]::GetEnvironmentVariables([EnvironmentVariableTarget]::Machine).Keys) | Where-Object { $_ -match '^(COMPLUS_|COR_ENABLE_PROFILING$|COR_PROFILER|APPDOMAIN_MANAGER_)' }).Count; if ($clr) { $bad.Add('clrOverride') }; 'POLICY machinePolicy/userPolicy=' + ($pol -join '/') + ' clrOverrides=' + $clr
$oc = @(Get-ChildItem -LiteralPath 'Cert:\CurrentUser\My' | Where-Object { $_.Thumbprint -ceq '14F34EFEFA84C367F25917B4B28803DCECE85623' }); $na = $null; if ($oc.Count -eq 1) { $na = $oc[0].NotAfter.ToUniversalTime() }; if ($null -eq $na -or $na -le [DateTime]::UtcNow.AddHours(24)) { $bad.Add('oldSigner') }; 'OLD-SIGNER inMy=' + $oc.Count + ' notAfterUtc=' + $(if ($null -ne $na) { $na.ToString('o') } else { 'none' })
if ($bad.Count) { 'STOP: preflight ' + ($bad -join ',') } else { 'PREFLIGHT-OK' }
```

**Step 2 — B0.2 fetch and worktree (the only Git changes).** Substitute `<CHECKOUT>` once (rule 1). Expected: `FETCH-EXIT 0 gitLines=<n>`, `COMMIT-PRESENT-EXIT 0`, `WORKTREE-EXIT 0 gitLines=<n>`. Git's output is captured and only counted (`gitLines` is informational, not a stop). Stop on any non-zero `…-EXIT` value or any `STOP:` line.

```powershell
$ErrorActionPreference = 'Continue'; $rc = '<CHECKOUT>'; $env:GIT_TERMINAL_PROMPT = '0'; $ok = $false; if (Test-Path -LiteralPath $wk) { 'STOP: work folder already exists' } else { $go = @($null | & git.exe -C $rc -c gc.auto=0 -c maintenance.auto=false fetch --no-tags origin claude/sweet-franklin-j3mw4a 2>&1); 'FETCH-EXIT ' + $LASTEXITCODE + ' gitLines=' + $go.Count; if ($LASTEXITCODE -eq 0) { $null | & git.exe -C $rc cat-file -e 'febf1ac95c207aadf0f3efe742b854d81f903037^{commit}'; 'COMMIT-PRESENT-EXIT ' + $LASTEXITCODE; $ok = $LASTEXITCODE -eq 0 } }
if ($ok) { New-Item -ItemType Directory -Path $wk -ErrorAction Stop | Out-Null; $go = @($null | & git.exe -C $rc -c gc.auto=0 -c maintenance.auto=false worktree add --detach $wt febf1ac95c207aadf0f3efe742b854d81f903037 2>&1); 'WORKTREE-EXIT ' + $LASTEXITCODE + ' gitLines=' + $go.Count; if ($LASTEXITCODE -ne 0) { 'STOP: worktree add failed' } } elseif (!(Test-Path -LiteralPath $wk)) { 'STOP: fetch or commit check did not pass; nothing created' }
```

**Step 3 — B0.3 verify (read-only).** Expected: `HEAD febf1ac95c207aadf0f3efe742b854d81f903037`, `DIRTY 0`, ten `SCRIPT CRLF …` lines, `CAPABILITY 0772DAD8365A8313125A364FBBD70DB2AE7BFC6C203732EA0751D75E579DD9C7`, `EOL CRLF`, `B0-OK`.

```powershell
$hd = [string]($null | & git.exe -C $wt rev-parse --verify HEAD); $dt = @($null | & git.exe -C $wt --no-optional-locks status --porcelain=v1 --untracked-files=all | Where-Object { $_ }).Count; 'HEAD ' + $hd; 'DIRTY ' + $dt
$pins = [ordered]@{ 'Install-OwnerOnlyProtectedTestDeployment.ps1' = @('EFD9C1D78240E521F8CFE169DBCDE824FD90A3AC7413FCA153664468938886C7', 'B994236329346B8E77823BA72E2926D8F7D1D296821993F043C77E5E6A2AB044'); 'Test-OwnerOnlyProtectedTestDeployment.ps1' = @('CCA0ACA362A018E9009A481EBAFBA2EE9DBF9FE6BBC4DEF5725FF785A53D9447', '2CFB6C72121824784ECC634F13404848204E4FEABAA5F8574EB599F9409F8EE2'); 'Remove-OwnerOnlyProtectedTestDeployment.ps1' = @('F42D7E392FDA49199754E516E1ECC53D230FF49C53569548D86DC64388F4248D', 'E5C4796B66A7429FED36FC9D506577FBC9F88C4E903893300214E0FC40E099D7'); 'OwnerOnlyPayloadIntegrity.ps1' = @('57245268969A81F28D4DE0C3AB9F1D8F63195B29D3E9EDCFF49D63EFF2F9F9AB', 'EFF3840697893C8CF176435E3D4F5FB843811965FDBAE2883E97371E0CB6D987'); 'OwnerOnlyExecution.ps1' = @('857F6DC8BCE109F412401B55A1EF966AEE037B249CFBF14CE19586D163EB2728', '33B1B716E6298DCEAEF784DCFCB985FD8485DEFEDF6F886029D6BC8F8A615EF3'); 'Wait-OwnerOnlyDeploymentProcess.ps1' = @('F24B3235FD34AA82421C576EB284C1F8C04D26D9C56170BE0175825ADDF46C59', '2BA1A90DD230CA8587F846F75B84691BA3EA4D27C20069192F46C6602FAC520B'); 'Invoke-OwnerOnlyApprovedRedeployment.ps1' = @('646A3D1B054C5EB505D3BD87293C583D4018A5D0AB9B3410ACAAD694030AACB6', 'A3EF83B74BF8D288FEB5E4F82BFCC750E3D3583C619FCA6E16448716E07DDE4A'); 'Invoke-OwnerOnlyRedeployCaller.ps1' = @('DA4ECB166080F0094D9ACFEAB5249EA4F29B97B8764820DC787CC3FD72F39993', '0CA47D163083B9FAE0449D3B1FB18BEAD002606DAA41AFDCFA1FA96ACD0FC52D'); 'Copy-OwnerOnlyUserDatabase.ps1' = @('2A420E549541242AC0F462657BEFA32772098CF7D5D6CFAE25B6761379471620', '6942B13C93B6D38033689F1BC08972E656F7E8F3F7A1EBD993770B20639DC486'); 'New-OwnerOnlyCandidate.ps1' = @('9DBCE1E2E5CFDB1C2775A85B5D5C570C9709068C464237DD1BAED3BD8D021785', 'BC67E825060E99AFE698E8C11C704498C4025C89BAEB1CF5EE4AC3EF6C975644') }
$forms = @(); foreach ($n in $pins.Keys) { $h = (Get-FileHash -LiteralPath (Join-Path $sc $n) -Algorithm SHA256).Hash; $f = if ($h -ceq $pins[$n][0]) { 'LF' } elseif ($h -ceq $pins[$n][1]) { 'CRLF' } else { 'MISMATCH' }; $forms += $f; 'SCRIPT ' + $f + ' ' + $h + ' ' + $n }; $cap = (Get-FileHash -LiteralPath (Join-Path $wt 'src\PCSteward.ElevatedHelper\PCSteward.DeploymentCapability.txt') -Algorithm SHA256).Hash; 'CAPABILITY ' + $cap
$uf = @($forms | Sort-Object -Unique); if ($hd -ceq 'febf1ac95c207aadf0f3efe742b854d81f903037' -and $dt -eq 0 -and $uf.Count -eq 1 -and $uf[0] -ceq 'CRLF' -and $cap -ceq '0772DAD8365A8313125A364FBBD70DB2AE7BFC6C203732EA0751D75E579DD9C7') { 'EOL ' + $uf[0]; 'B0-OK' } else { 'STOP: B0 verification failed' }
```

**Step 4 — B1 build, in the background.** Expected: `BUILDER BC67E825060E99AFE698E8C11C704498C4025C89BAEB1CF5EE4AC3EF6C975644`, `BUILD-EXIT 0`, a `CANDIDATE` line with `validation=Pass origin=pinned-unsigned-v2 helperProtocol=2 … commit=febf1ac95c207aadf0f3efe742b854d81f903037 dirty=False sdk=10.0.403 sdkExactPin=True signed=False selfContained=False`, `MANIFEST-SHA256` equal to `MANIFEST-FILE-SHA256`, `B1-OK`. Note the build time in minutes. If the build fails because NuGetAudit reports a vulnerability (`NU1901`–`NU1904` treated as errors), that is a normal B1 stop: run B1-X once, report it, and do not retry, change packages or turn the audit off.

```powershell
$bh = (Get-FileHash -LiteralPath (Join-Path $sc 'New-OwnerOnlyCandidate.ps1') -Algorithm SHA256).Hash; $lg = Join-Path $wk 'b1-build.log'; if ($bh -cne 'BC67E825060E99AFE698E8C11C704498C4025C89BAEB1CF5EE4AC3EF6C975644') { 'STOP: builder hash differs' } elseif ((Test-Path -LiteralPath $co) -or (Test-Path -LiteralPath $lg)) { 'STOP: candidate folder or build log already exists' } else { 'BUILDER ' + $bh; $env:DOTNET_ROOT = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'Microsoft\dotnet'; $env:PATH = $env:DOTNET_ROOT + ';' + $env:PATH; $null | & $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $sc 'New-OwnerOnlyCandidate.ps1') -OutputRoot $co 2>&1 > $lg; 'BUILD-EXIT ' + $LASTEXITCODE }
$sj = Join-Path $co 'candidate-build.json'; if ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $sj)) { $j = [IO.File]::ReadAllText($sj) | ConvertFrom-Json; $mh = (Get-FileHash -LiteralPath (Join-Path $co 'candidate-manifest.json') -Algorithm SHA256).Hash; 'CANDIDATE validation=' + $j.validation + ' origin=' + $j.payloadOrigin + ' helperProtocol=' + $j.helperProtocol + ' files=' + $j.fileCount + ' bytes=' + $j.totalBytes + ' commit=' + $j.sourceCommit + ' dirty=' + $j.sourceDirty + ' sdk=' + $j.sdkVersion + ' sdkExactPin=' + $j.sdkExactPin + ' signed=' + $j.signed + ' selfContained=' + $j.selfContained + ' resourceMerges=' + $j.resourceMerges; 'MANIFEST-SHA256 ' + $j.manifestSha256; 'MANIFEST-FILE-SHA256 ' + $mh; if ($j.validation -ceq 'Pass' -and $j.payloadOrigin -ceq 'pinned-unsigned-v2' -and [string]$j.helperProtocol -ceq '2' -and $j.sourceCommit -ceq 'febf1ac95c207aadf0f3efe742b854d81f903037' -and $j.sourceDirty -eq $false -and $j.sdkVersion -ceq '10.0.403' -and $j.signed -eq $false -and $mh -ceq $j.manifestSha256) { 'B1-OK' } else { 'STOP: candidate summary not as expected' } } else { 'STOP: build did not pass' }
```

Only after a B1 stop, run **B1-X once** and report its lines (they are already sanitized; check them):

```powershell
$lg = Join-Path $wk 'b1-build.log'; $up = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile); if (Test-Path -LiteralPath $lg) { @(Get-Content -LiteralPath $lg | Where-Object { $_ -match "dotnet step '|OutputRoot|must|refus|differs|missing|Conflicting|failed|error" } | Select-Object -Last 5) | ForEach-Object { $_.Replace($up, '<PROFILE>').Replace($env:USERNAME, '<USER>').Replace($env:COMPUTERNAME, '<PC>') } } else { 'NO-LOG' }
```

**Step 5 — B2 database copy.** Expected: `COPY-SCRIPT 6942B13C93B6D38033689F1BC08972E656F7E8F3F7A1EBD993770B20639DC486`, `COPY-EXIT 0`, a `DBCOPY` line (`bytes=159744` at inventory; `sourceSha256` equal to `copySha256`; `hashesEqual=True sidecarsPresent=False pcstewardProcesses=0 headerValid=True`), `B2-OK`.

```powershell
$ph = (Get-FileHash -LiteralPath (Join-Path $sc 'Copy-OwnerOnlyUserDatabase.ps1') -Algorithm SHA256).Hash; $cx = $null; if ($ph -cne '6942B13C93B6D38033689F1BC08972E656F7E8F3F7A1EBD993770B20639DC486') { 'STOP: copy script hash differs' } elseif (Test-Path -LiteralPath (Join-Path $wk 'dbcopy')) { 'STOP: copy folder already exists' } else { 'COPY-SCRIPT ' + $ph; $cr = @($null | & $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $sc 'Copy-OwnerOnlyUserDatabase.ps1') -DestinationParent (Join-Path $wk 'dbcopy')); $cx = $LASTEXITCODE; 'COPY-EXIT ' + $cx }
if ($cx -eq 0) { $r = ($cr -join "`n") | ConvertFrom-Json; 'DBCOPY bytes=' + $r.bytes + ' sourceSha256=' + $r.sourceSha256 + ' copySha256=' + $r.copySha256 + ' hashesEqual=' + $r.hashesEqual + ' sidecarsPresent=' + $r.sidecarsPresent + ' pcstewardProcesses=' + $r.pcstewardProcesses + ' headerValid=' + $r.sqliteHeader.headerValid + ' walMode=' + $r.sqliteHeader.walMode + ' pageCountMatchesLength=' + $r.sqliteHeader.pageCountMatchesLength; if ($r.hashesEqual -eq $true -and $r.sourceSha256 -ceq $r.copySha256 -and $r.sqliteHeader.headerValid -eq $true) { 'B2-OK' } else { 'STOP: database copy not as expected' } } else { 'STOP: copy did not pass' }
```

**Step 6 — B3a NewPlan.** Expected: `CALLER 0CA47D163083B9FAE0449D3B1FB18BEAD002606DAA41AFDCFA1FA96ACD0FC52D`, the NewPlan JSON (`"result": "Written"`, `"executed": false`, `"elevated": false`), `PLAN-FILE-SHA256` equal to its `planSha256`, `PLAN-CHECK dbPinEqualsCopy=True manifestSignerOrigins=True`, `B3A-OK`.

```powershell
$cb = [IO.File]::ReadAllBytes((Join-Path $sc 'Invoke-OwnerOnlyRedeployCaller.ps1')); $ch = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($cb)).Replace('-', ''); if ($ch -cne '0CA47D163083B9FAE0449D3B1FB18BEAD002606DAA41AFDCFA1FA96ACD0FC52D') { 'STOP: caller hash differs' } elseif (Test-Path -LiteralPath $pd) { 'STOP: plan folder already exists' } else { 'CALLER ' + $ch; New-Item -ItemType Directory -Path $pd -ErrorAction Stop | Out-Null; & ([ScriptBlock]::Create([Text.Encoding]::UTF8.GetString($cb))) -NewPlanPath $pf -ScriptSource $sc -CandidateRoot (Join-Path $co 'payload') -CandidateManifestPath (Join-Path $co 'candidate-manifest.json') -ExpectedOldOrigin 'pinned-unsigned-v1' -ExpectedNewOrigin 'pinned-unsigned-v2' }
$rf = @(Get-ChildItem -LiteralPath (Join-Path $wk 'dbcopy') -Directory | ForEach-Object { Join-Path $_.FullName 'copy-record.json' }); if ((Test-Path -LiteralPath $pf) -and $rf.Count -eq 1) { $cr = [IO.File]::ReadAllText($rf[0]) | ConvertFrom-Json; $pj = [IO.File]::ReadAllText($pf) | ConvertFrom-Json; $dp = @($pj.userDataFiles | Where-Object { $_.path -ceq 'pcsteward.db' }); $mh = (Get-FileHash -LiteralPath (Join-Path $co 'candidate-manifest.json') -Algorithm SHA256).Hash; 'PLAN-FILE-SHA256 ' + (Get-FileHash -LiteralPath $pf -Algorithm SHA256).Hash; $m1 = @($pj.userDataFiles).Count -eq 1 -and $dp.Count -eq 1 -and $dp[0].sha256 -ceq $cr.sourceSha256 -and [long]$dp[0].bytes -eq [long]$cr.bytes; $m2 = $pj.candidateManifestSha256 -ceq $mh -and $pj.oldThumbprint -ceq '14F34EFEFA84C367F25917B4B28803DCECE85623' -and $pj.oldLedgerSha256 -ceq 'AC1BF9755A03C42705ED69777B035445ECBCAD04E172A3715F2212E3408080CB' -and $pj.expectedOldOrigin -ceq 'pinned-unsigned-v1' -and $pj.expectedNewOrigin -ceq 'pinned-unsigned-v2'; 'PLAN-CHECK dbPinEqualsCopy=' + $m1 + ' manifestSignerOrigins=' + $m2; if ($m1 -and $m2) { 'B3A-OK' } else { 'STOP: plan pins differ from B1, B2 or the known ledger' } } else { 'STOP: plan or copy record missing' }
```

**Step 7 — B3b CheckOnly.** Expected: `CALLER 0CA47D16…C52D`, `PLAN-FILE-SHA256` equal to step 6, `EXECUTION-ROOT-ID` (32 lower-case hex), then the CheckOnly JSON with `"result": "Pass"`. Check, and list any mismatch under `notes`: `planSha256` = step 6; `candidateManifestSha256` = step 4 `MANIFEST-SHA256`; `userDatabase.sha256` and `userDatabase.bytes` = step 5; `oldLedgerSha256` = `AC1BF9755A03C42705ED69777B035445ECBCAD04E172A3715F2212E3408080CB`; `oldThumbprint` = `14F34EFEFA84C367F25917B4B28803DCECE85623`; `bootstrapTemplateSha256` = `A3EF83B74BF8D288FEB5E4F82BFCC750E3D3583C619FCA6E16448716E07DDE4A`; the six `scripts` hashes = the matching step 3 lines; `encodedCommandLength` ≤ 30000; `scheduledCheckTask` = `AbsentOrDisabled`; `executionRootAbsent` and `attemptRecordAbsent` true. A mismatch is a stop.

```powershell
$cb = [IO.File]::ReadAllBytes((Join-Path $sc 'Invoke-OwnerOnlyRedeployCaller.ps1')); $ch = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($cb)).Replace('-', ''); if ($ch -cne '0CA47D163083B9FAE0449D3B1FB18BEAD002606DAA41AFDCFA1FA96ACD0FC52D') { 'STOP: caller hash differs' } elseif (!(Test-Path -LiteralPath $pf)) { 'STOP: plan missing' } else { 'CALLER ' + $ch; 'PLAN-FILE-SHA256 ' + (Get-FileHash -LiteralPath $pf -Algorithm SHA256).Hash; $er = [string](([IO.File]::ReadAllText($pf) | ConvertFrom-Json).executionRoot); 'EXECUTION-ROOT-ID ' + $er.Substring($er.Length - 32); & ([ScriptBlock]::Create([Text.Encoding]::UTF8.GetString($cb))) -PlanPath $pf -CheckOnly }
```

**Step 8 — B3c old-deployment dry run (read-only).** Expected: `VALIDATOR 2CFB6C72121824784ECC634F13404848204E4FEABAA5F8574EB599F9409F8EE2`, `VALIDATOR-EXIT 0`, `OLD-DEPLOYMENT validation=Pass origin=pinned-unsigned-v1 thumbprint=14F34EFEFA84C367F25917B4B28803DCECE85623 launched=False`, `B3C-OK`.

```powershell
$vh = (Get-FileHash -LiteralPath (Join-Path $sc 'Test-OwnerOnlyProtectedTestDeployment.ps1') -Algorithm SHA256).Hash; $vx = $null; if ($vh -cne '2CFB6C72121824784ECC634F13404848204E4FEABAA5F8574EB599F9409F8EE2') { 'STOP: validator hash differs' } else { 'VALIDATOR ' + $vh; $vo = @($null | & $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $sc 'Test-OwnerOnlyProtectedTestDeployment.ps1') -RequirePinnedPayload); $vx = $LASTEXITCODE; 'VALIDATOR-EXIT ' + $vx }
if ($vx -eq 0) { $v = ($vo -join "`n") | ConvertFrom-Json; 'OLD-DEPLOYMENT validation=' + $v.validation + ' origin=' + $v.payloadOrigin + ' thumbprint=' + $v.certificateThumbprint + ' launched=' + $v.launched; if ($v.validation -ceq 'Pass' -and $v.payloadOrigin -ceq 'pinned-unsigned-v1' -and $v.certificateThumbprint -ceq '14F34EFEFA84C367F25917B4B28803DCECE85623') { 'B3C-OK' } else { 'STOP: old deployment not as expected' } } else { 'STOP: validator did not pass' }
```

Then stop. Do **not** run B4 or anything else, and do not clean up: everything in `%USERPROFILE%\PCSteward-Task50B` must stay exactly as it is for the B-exec stage.

## Step 9 — the report to pass back

Print exactly one fenced block in this shape, with nothing private (rule 4), show it to the owner, and pass it back to the cloud session that started you:

````
PART-B-PREP-RESULT (Task 50, approval 1a, handover revision 2)
B0.1: <HOST …> | <SCHEDULED-CHECK …> | <DATA …> | <PATHS …> | <TOOLS …> | <GIT-HOOKS …> | <POLICY …> | <OLD-SIGNER …> | PREFLIGHT-OK   (runs: <n>; or the STOP token)
B0.2: FETCH-EXIT <n> gitLines=<n> | COMMIT-PRESENT-EXIT <n> | WORKTREE-EXIT <n> gitLines=<n>
B0.3: HEAD <40 hex> | DIRTY <n> | CAPABILITY <hash> | EOL <form> | B0-OK
<the ten SCRIPT lines, verbatim>
B1: BUILDER <hash> | BUILD-EXIT <n> | <CANDIDATE …> | MANIFEST-SHA256 <hash> | MANIFEST-FILE-SHA256 <hash> | B1-OK | build minutes ≈ <n>
B2: COPY-SCRIPT <hash> | COPY-EXIT <n> | <DBCOPY …> | B2-OK
B3a: CALLER <hash> | PLAN-FILE-SHA256 <hash> | <PLAN-CHECK …> | B3A-OK
<NewPlan JSON, verbatim>
B3b: CALLER <hash> | PLAN-FILE-SHA256 <hash> | EXECUTION-ROOT-ID <32 hex>
<CheckOnly JSON, verbatim>
B3c: VALIDATOR <hash> | VALIDATOR-EXIT <n> | <OLD-DEPLOYMENT …> | B3C-OK
prompts seen by owner: UAC <n> (expected 0) | certificate <n> (expected 0) | Git sign-in <yes/no>
notes: <one line per anything unexpected or any step-7 mismatch; no paths or names>
````

After a stop, fill the lines up to the stopping step, put the stop token or sanitized error on that step's line, and write `not run` for the rest. Then stop. Do not interpret the results, suggest fixes or run anything else.
