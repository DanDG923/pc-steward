# Task 50 Part C re-run: redeploy with Profile V2 revision 2.1, the owner's `cacheLocation` change, and a new probe (revision 1, draft)

Requirements: `FR-UPD-15`, `TM-03/04/07/13`. ADRs: ADR-022, ADR-025, ADR-031. Sources: the [Task 50 handover](TASK_50_HANDOVER_2026-11-06.md) §5 items 1–2, the [Part C plan](TASK_50_PART_C_PLAN.md) §10–§13 (this plan reuses its C-B, C-pre and C-run blocks with refreshed pins), the [Profile V2 legacy-key revision](TASK_50_PROFILE_V2_LEGACY_KEY_REVISION.md) and [Profile V2](TASK_50_CHOCOLATEY_PROFILE_V2.md) "Configured cacheLocation".

**Nothing here is approved.** Every step below needs the owner's own approval, given in the session, and is run once by the owner. Part C stays recorded as **FAIL** until a new run passes. Until then the owner confirms no Chocolatey review (D4 = G-0).

## 1. Why a re-run, and what it can and cannot show

The C-run probe failed E2 at step 6 for two independent reasons (Part C plan §12, D1):
1. **Cause 1, fixed in code, not deployed.** `containsLegacyPackageInstalls` is admitted (absent or exactly `true`) by Profile V2 revision 2.1 at `bc24a20`, with review fixes in `5606da2`. Windows CI is green on `5606da2` and on the docs-only head `5285cfb` (PR #12).
2. **Cause 2, host precondition.** `cacheLocation` is empty. Profile V2 refuses that by design. Only the owner can change it, outside PC Steward (§3).

A re-run is worth making only when **both** are cleared. Even then:
- the observer may refuse at a **later** step (7–9) that the failed run never reached. That would be a new finding, interpreted under spec §8, not retried;
- an overall PASS proves **UAC and pre-launch enforcement only** for that deployment and its pins. Real Chocolatey use still needs the owner's final admission approval (obligation 8), the desktop composition of a Chocolatey review (handover §5 item 3, unbuilt) and a target package (G9).

## 2. Route and deadline

| Step | What | Elevated? | Approval |
|---|---|---|---|
| **D2a** | Read-only check: the host's `containsLegacyPackageInstalls` state and `cacheLocation` class (§4) | No | D2 |
| **H1** | **Owner's own Chocolatey change**, outside PC Steward: create a protected cache folder and set `cacheLocation` (§3) | Yes (owner's admin window) | Owner's own decision; recorded, not a PC Steward approval |
| **D2b** | D2 again, after H1 | No | D2 (covers both runs) |
| **CB2.1–CB2.7** | Redeploy prep from build commit `5606da23c303429c7c4711e9f5cfd43640c79289` (§5) | No | CB2-prep |
| **CB2.8–CB2.9** | One redeploy run, then post-checks (§6) | One UAC | CB2-exec |
| **C2-pre, C2-check, C2-run** | Part C again on the new deployment, with new pins (§7) | One UAC (C2-run) | C2 |

**Deadline.** The installed signer `67B59B4C9BC3A006FBE39A72967E05F7B8C375DA` expires **2026-11-20T19:53:44Z**.
- CB2.2 requires more than 24 hours left after PowerShell's `[int]` rounding (banker's rounding, so 24.5 h fails), so **CB2.2 must run before 2026-11-19T19:23Z**. Start CB2-prep well before that; CB2.3–CB2.7 have no limit of their own.
- CheckOnly needs a one-hour margin, so **CB2.8 must start before 2026-11-20T18:53:44Z**.
- If that is missed, the route is a separately approved remover, then a fresh install of `5606da2` (the R1/R2 pattern in the [recovery plan](TASK_50_RECOVERY_PLAN.md)). That needs its own plan.

H1 and D2 have no deadline of their own, but C2-check takes its Chocolatey baseline after them, so they must be done before C2. Doing them first means the redeploy is not wasted on a host that would refuse again.

**How to run every step** (unchanged lessons, handover §4):
- a **new** Windows PowerShell 5.1 window per step, opened by the owner from the Start menu, never PowerShell 7 and never a window started by the Claude app (the tooling refuses a packaged process);
- **normal (not administrator)** for every step except H1;
- paste the lines exactly, run once, never retry;
- after a `STOP:` line or red error text, run nothing further and report the output with names and paths replaced by `<USER>`;
- plan files and build logs are never printed or pasted; PC Steward stays closed except during C2-pre.

## 3. H1 — the owner's `cacheLocation` change (outside PC Steward)

PC Steward never creates, hardens or changes a Chocolatey directory or setting (Profile V2: "Observe/refuse; never create/harden a third-party directory"). This section only states what Profile V2 accepts, so the owner can decide.

**What step 6 accepts** (`ChocolateyProfileRules.IsSupportedCacheLocation`, `ChocolateyProfileObserver.Configuration`):
- a nonempty, canonical, absolute local path below a drive root, with no `%`;
- it already exists, is a directory and is not a reparse point;
- neither it nor any ancestor can be modified by the normal (non-elevated) user.

**Suggested location: `C:\ProgramData\ChocolateyCache`**, a sibling of the existing, already-accepted `ChocolateyHttpCache`.
- **Not inside `C:\ProgramData\chocolatey`.** The step-1 walk covers that whole tree within fixed bounds, and every downloaded installer would then join it. A cache there could later push the walk past its bounds and refuse.
- **Not under the user profile or `%TEMP%`**: those are user-writable.
- A new folder under `C:\ProgramData` **inherits a user-writable ACL** (Users may create files there). So H1 removes inheritance and grants only SYSTEM and Administrators full control, and Users read and execute, then sets the owner to Administrators.

**What H1 changes on the PC.** One new folder, and one Chocolatey setting: Chocolatey will cache downloads there instead of the user's `%TEMP%\chocolatey`. It also runs `choco config set`, which rewrites `chocolatey.config` (the C2 baseline is taken later, so this is expected). **To undo:** `choco config unset --name=cacheLocation` in an administrator window, then delete the folder.

**H1 block** (owner's own **administrator** Windows PowerShell 5.1 window; one UAC prompt for Windows PowerShell when the owner opens it). Expected: `ICACLS-EXIT 0`, `SETOWNER-EXIT 0`, `CHOCO-CONFIG-EXIT 0`.

```powershell
$cc = Join-Path $env:ProgramData 'ChocolateyCache'; if (!([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { 'STOP: H1 needs an administrator window' } elseif (Test-Path -LiteralPath $cc) { 'STOP: the folder already exists' } else { New-Item -ItemType Directory -Path $cc -ErrorAction Stop | Out-Null; $null | & icacls.exe $cc /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-32-545:(OI)(CI)RX' | Out-Null; 'ICACLS-EXIT ' + $LASTEXITCODE; $null | & icacls.exe $cc /setowner '*S-1-5-32-544' | Out-Null; 'SETOWNER-EXIT ' + $LASTEXITCODE; $null | & choco.exe config set --name=cacheLocation --value=$cc --limit-output | Out-Null; 'CHOCO-CONFIG-EXIT ' + $LASTEXITCODE }
```


## 4. D2 — read-only Chocolatey configuration check (approval D2)

D2 reads `chocolatey.config` and the cache folder's ACL. It prints **no values, paths, names or SIDs**: only fixed tokens, booleans, counts and the class of `cacheLocation`. It does not run choco and changes nothing. Key names are matched case-insensitively and a repeated key is reported, as `ChocolateyConfigurationParser` does; the legacy-key value is compared exactly (`true`), as the parser does. It is an approximation: it checks only the cache folder's own explicit and inherited Allow entries for the normal user, their groups and the broad principals (Everyone, Authenticated Users, Users, INTERACTIVE, CREATOR OWNER, OWNER RIGHTS). Ancestors and deny entries are proven only by the elevated observer in C2-run.

```powershell
$cf = Join-Path $env:ProgramData 'chocolatey\config\chocolatey.config'; $cr = Join-Path $env:ProgramData 'chocolatey'; $up = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile); $me = [Security.Principal.WindowsIdentity]::GetCurrent(); 'D2 ELEVATED ' + ([Security.Principal.WindowsPrincipal]$me).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (!(Test-Path -LiteralPath $cf -PathType Leaf)) { 'D2 CONFIG absent' } else { try { $x = [xml][IO.File]::ReadAllText($cf) } catch { $x = $null; 'D2 XML parse-failed' }
  if ($x) { $ad = @($x.DocumentElement.SelectNodes('config/add')); $lk = @($ad | Where-Object { $_.GetAttribute('key') -eq 'containsLegacyPackageInstalls' }); $ls = if ($lk.Count -eq 0) { 'absent' } elseif ($lk.Count -gt 1) { 'duplicate' } elseif ($lk[0].HasAttribute('value') -and $lk[0].GetAttribute('value') -ceq 'true') { 'true' } else { 'other' }; 'D2 LEGACY-KEY state=' + $ls + ' admitted=' + ($ls -eq 'absent' -or $ls -eq 'true')
    $cl = @($ad | Where-Object { $_.GetAttribute('key') -eq 'cacheLocation' }); $cv = if ($cl.Count -eq 1) { $cl[0].GetAttribute('value') } else { '' }
    if ($cl.Count -gt 1) { 'D2 CACHELOCATION duplicate' } elseif ($cv.Length -eq 0) { 'D2 CACHELOCATION empty' } elseif ($cv.Contains('%')) { 'D2 CACHELOCATION has-percent' } elseif ($cv -notmatch '^[A-Za-z]:\\[^\\]' -or $cv.EndsWith('\') -or $cv.Contains('\\') -or $cv -match '\\\.\.?(\\|$)' -or $cv -match '[ .](\\|$)' -or $cv.Contains('/')) { 'D2 CACHELOCATION not-canonical-absolute-local' } else {
      $ex = Test-Path -LiteralPath $cv -PathType Container; $line = 'D2 CACHELOCATION absolute-local exists=' + $ex + ' underUserProfile=' + $cv.StartsWith($up + '\', [StringComparison]::OrdinalIgnoreCase) + ' underChocolateyRoot=' + $cv.StartsWith($cr + '\', [StringComparison]::OrdinalIgnoreCase)
      if ($ex) { $it = Get-Item -LiteralPath $cv -Force; $rp = [bool]($it.Attributes -band [IO.FileAttributes]::ReparsePoint); $sids = @($me.User.Value) + @($me.Groups | ForEach-Object { $_.Value } | Where-Object { $_ -ne 'S-1-5-32-544' }) + @('S-1-1-0', 'S-1-5-11', 'S-1-5-32-545', 'S-1-5-4', 'S-1-3-0', 'S-1-3-4') | Sort-Object -Unique; $wm = 0x2 -bor 0x4 -bor 0x10 -bor 0x40 -bor 0x100 -bor 0x10000 -bor 0x40000 -bor 0x80000 -bor 0x10000000 -bor 0x40000000; $acl = Get-Acl -LiteralPath $cv; $wa = @($acl.Access | Where-Object { $_.AccessControlType -eq 'Allow' -and !($_.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly) -and (([int]$_.FileSystemRights) -band $wm) -ne 0 -and $sids -contains $(try { $_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value } catch { '' }) }).Count; $ow = try { (New-Object Security.Principal.NTAccount($acl.Owner)).Translate([Security.Principal.SecurityIdentifier]).Value } catch { '' }; $line += ' reparsePoint=' + $rp + ' requesterWritableAces=' + $wa + ' ownerIsRequester=' + ($ow -eq $me.User.Value) + ' inheritanceProtected=' + $acl.AreAccessRulesProtected }
      $line } } }
```


**Expected after H1 (D2b):**
- `D2 ELEVATED False`;
- `D2 LEGACY-KEY state=true admitted=True` (Profile V2 rev. 2.1 refuses any other value, so `other` or `duplicate` means **stop**: the re-run would refuse at step 6 again);
- `CACHELOCATION duplicate` also means **stop**;
- `D2 CACHELOCATION absolute-local exists=True underUserProfile=False underChocolateyRoot=False reparsePoint=False requesterWritableAces=0 ownerIsRequester=False inheritanceProtected=True`.

**Expected before H1 (D2a):** `LEGACY-KEY state=true admitted=True` and `CACHELOCATION empty`. D2a confirms the host value of the legacy key, which D1 could not show (handover §5 item 2).

## 5. Stage CB2: redeploy build `5606da2` (approval CB2-prep: CB2.1–CB2.7, none elevated)

These are the Part C plan §10 blocks with only these changes:

| Pin | §10 (C-B) | CB2 |
|---|---|---|
| Work folder / worktree | `PCSteward-Task50C\ebb4061` | `PCSteward-Task50D\5606da2` |
| Build commit | `ebb40618…` | `5606da23c303429c7c4711e9f5cfd43640c79289` |
| Old signer (CB2.2, CB2.5, CB2.7, CB2.8 prompt) | `473DB701…` | `67B59B4C9BC3A006FBE39A72967E05F7B8C375DA`, NotAfter `2026-11-20T19:53:44Z` |
| Old ledger (CB2.5/CB2.6 report) | `413F80D3…` | expected `CBAB8C0AEB29DB34076F16A9176674CA08DA4A655447988B1368B456733AC852` |
| Database | `78697727…`/200,704 B | unchanged pin. If PC Steward was opened since C-run, CB2.2 stops and the pin must be refreshed under a new approval |
| CB2.9 "old" certificates | 3 thumbprints | adds `67B59B4C…` |
| Ten tooling scripts, probe script, capability statement | — | **unchanged**: verified byte-identical at `5606da2` (the only `src`/`scripts` change since `ebb4061` is `ChocolateyConfigurationParser.cs`) |

The new candidate's manifest and build record will differ from §10, because the parser assembly changed. They are pinned from CB2.3's output.

**Line V4** starts every CB2 step:

```powershell
$wk = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) 'PCSteward-Task50D'; $wt = Join-Path $wk '5606da2'; $sc = Join-Path $wt 'scripts'; $co = Join-Path $wk 'candidate'; $pd = Join-Path $wk 'plan'; $pf = Join-Path $pd 'redeploy-plan.json'; $ps = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
```

**CB2.1 — fetch the build commit and add a detached worktree** (changes Git state; network). First `cd` into the PC Steward clone, paste V4, then these lines. Expected as CB.1.

```powershell
$ErrorActionPreference = 'Continue'; $rc = (Get-Location).ProviderPath; $env:GIT_TERMINAL_PROMPT = '0'; $ok = $false; $url = [string]($null | & git.exe -C $rc config --get remote.origin.url 2>$null); $hk = @($null | & git.exe -C $rc rev-parse --git-path hooks/post-checkout --git-path hooks/reference-transaction 2>$null); $hx = @($hk | ForEach-Object { $p = [string]$_; if (![IO.Path]::IsPathRooted($p)) { $p = Join-Path $rc $p }; (Test-Path -LiteralPath $p) -or (Test-Path -LiteralPath ($p + '.exe')) })
if (Test-Path -LiteralPath $wk) { 'STOP: work folder already exists' } elseif (!(Test-Path -LiteralPath (Join-Path $rc 'PCSteward.slnx')) -or $url -notmatch 'pc-steward') { 'STOP: not in the PC Steward clone' } elseif ($hx.Count -ne 2) { 'STOP: hook check failed' } else { 'GIT-HOOKS postCheckout=' + $hx[0] + ' referenceTransaction=' + $hx[1]; if ($hx -contains $true) { 'STOP: a Git hook exists' } else { $go = @($null | & git.exe -C $rc -c gc.auto=0 -c maintenance.auto=false fetch --no-tags origin claude/sweet-franklin-j3mw4a 2>&1); 'FETCH-EXIT ' + $LASTEXITCODE + ' gitLines=' + $go.Count; if ($LASTEXITCODE -eq 0) { $null | & git.exe -C $rc cat-file -e '5606da23c303429c7c4711e9f5cfd43640c79289^{commit}'; 'COMMIT-PRESENT-EXIT ' + $LASTEXITCODE; $ok = $LASTEXITCODE -eq 0 } } }
if ($ok) { New-Item -ItemType Directory -Path $wk -ErrorAction Stop | Out-Null; $go = @($null | & git.exe -C $rc -c gc.auto=0 -c maintenance.auto=false worktree add --detach $wt 5606da23c303429c7c4711e9f5cfd43640c79289 2>&1); 'WORKTREE-EXIT ' + $LASTEXITCODE + ' gitLines=' + $go.Count; if ($LASTEXITCODE -ne 0) { 'STOP: worktree add failed' } }
```

**CB2.2 — verify the worktree and the host (read-only).** Expected as CB.2, with `HEAD 5606da23c303429c7c4711e9f5cfd43640c79289` and `OLD-SIGNER inMy=1 notAfterUtc=2026-11-20T19:53:44.0000000Z hoursLeft=<n>` with more than 24 hours left, then `CB2-OK`.

```powershell
$hd = [string]($null | & git.exe -C $wt rev-parse --verify HEAD); $dt = @($null | & git.exe -C $wt --no-optional-locks status --porcelain=v1 --untracked-files=all | Where-Object { $_ }).Count; 'HEAD ' + $hd; 'DIRTY ' + $dt
$pins = [ordered]@{ 'Install-OwnerOnlyProtectedTestDeployment.ps1' = @('F5FD3ADA4784DA795792E455890DDE41DA271E0EB199617760099C5B62116E3D', 'FA2479125A43FC5DCE00C6EB8BC0E840863BA5D207BCDA3560CEADFF66EF138F'); 'Test-OwnerOnlyProtectedTestDeployment.ps1' = @('CCA0ACA362A018E9009A481EBAFBA2EE9DBF9FE6BBC4DEF5725FF785A53D9447', '2CFB6C72121824784ECC634F13404848204E4FEABAA5F8574EB599F9409F8EE2'); 'Remove-OwnerOnlyProtectedTestDeployment.ps1' = @('F42D7E392FDA49199754E516E1ECC53D230FF49C53569548D86DC64388F4248D', 'E5C4796B66A7429FED36FC9D506577FBC9F88C4E903893300214E0FC40E099D7'); 'OwnerOnlyPayloadIntegrity.ps1' = @('0874B755D88800BEBDD0D093BF90152C0B36729A6A9BBBFBE78E71136EEBA0A6', '83B87BBDE621992E32C0F042D26ED0707C106E18DF5CDD0EDF0CEA84521DD09F'); 'OwnerOnlyExecution.ps1' = @('DDF07C71EB61E2BF5E69FBA6F1A672D6CC09B8668715A90119BD725023093F9C', 'A23F2BBB874A280B5C918DAEDBCB1E5316CACB33AA670FDA682C0E2BF2E0B948'); 'Wait-OwnerOnlyDeploymentProcess.ps1' = @('F24B3235FD34AA82421C576EB284C1F8C04D26D9C56170BE0175825ADDF46C59', '2BA1A90DD230CA8587F846F75B84691BA3EA4D27C20069192F46C6602FAC520B'); 'Invoke-OwnerOnlyApprovedRedeployment.ps1' = @('AB5D61F3C61D3FCC1F7DB14B5A43B1FEF6E0B6AF5278518110FAA856F89FAF44', 'A9775FCEAE897AEBA18C838DD5FCA9BC69E355ABA4376D0EE0481E9B39D831D7'); 'Invoke-OwnerOnlyRedeployCaller.ps1' = @('EA5618D18939F3514DD81CBFAEE23BCC304168C073D98843F0B2C3BD69A60191', '62DDE7B43EAE4D5193DFAF37F76F46CD8C1B3EA6B0916E35F4071DC0014CE3C8'); 'Copy-OwnerOnlyUserDatabase.ps1' = @('1954CEC3B5249CB14B6192EC22BD3949C393A0224AC19BD67D665A24FF6E3B03', '0E7FDC16464B19A6BBD0B5F55908ECD16FBE0C4B2DC21A6418CE86C58630D313'); 'New-OwnerOnlyCandidate.ps1' = @('B9341F75BBE2CC0CB22F5BFBB158F606CF7D04221C8C7E17E780FFF4EF5912FD', 'BB6E0A60A8324341A3D5DA1606977D23CF653B2480724EB33E092E1BEE7D68D7') }
$forms = @(); foreach ($n in $pins.Keys) { $h = (Get-FileHash -LiteralPath (Join-Path $sc $n) -Algorithm SHA256).Hash; $f = if ($h -ceq $pins[$n][0]) { 'LF' } elseif ($h -ceq $pins[$n][1]) { 'CRLF' } else { 'MISMATCH' }; $forms += $f; 'SCRIPT ' + $f + ' ' + $h + ' ' + $n }; $prh = (Get-FileHash -LiteralPath (Join-Path $sc 'evidence\Task50-QualificationProbe.ps1') -Algorithm SHA256).Hash; 'PROBE-SCRIPT ' + $prh; $cap = (Get-FileHash -LiteralPath (Join-Path $wt 'src\PCSteward.ElevatedHelper\PCSteward.DeploymentCapability.txt') -Algorithm SHA256).Hash; 'CAPABILITY ' + $cap
$db = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'PCSteward\pcsteward.db'; $dh = (Get-FileHash -LiteralPath $db -Algorithm SHA256).Hash; $dl = (Get-Item -LiteralPath $db).Length; $sd = @(@('-wal', '-shm', '-journal') | Where-Object { Test-Path -LiteralPath ($db + $_) }).Count; $pn = @(Get-Process -Name 'PCSteward*' -ErrorAction SilentlyContinue).Count; $st = @(Get-ScheduledTask -TaskName 'PCSteward.ScheduledCheck' -ErrorAction SilentlyContinue).Count; $dbOk = $dh -ceq '78697727CE44A1167537A04BE4554339028F660DA142E00894778D602BEC55BE' -and $dl -eq 200704; 'DB sha256=' + $dh + ' bytes=' + $dl + ' equalsPin=' + $dbOk + ' sidecars=' + $sd + ' pcstewardProcesses=' + $pn + ' scheduledTask=' + $st
$os = @(Get-ChildItem -LiteralPath 'Cert:\CurrentUser\My' | Where-Object { $_.Thumbprint -ceq '67B59B4C9BC3A006FBE39A72967E05F7B8C375DA' }); $hl = if ($os.Count -eq 1) { [int]($os[0].NotAfter.ToUniversalTime() - [DateTime]::UtcNow).TotalHours } else { -1 }; 'OLD-SIGNER inMy=' + $os.Count + ' notAfterUtc=' + $(if ($os.Count -eq 1) { $os[0].NotAfter.ToUniversalTime().ToString('o') } else { 'none' }) + ' hoursLeft=' + $hl
$uf = @($forms | Sort-Object -Unique); if ($hd -ceq '5606da23c303429c7c4711e9f5cfd43640c79289' -and $dt -eq 0 -and $uf.Count -eq 1 -and $uf[0] -cne 'MISMATCH' -and $prh -ceq '831B5E174E1E5965CA0EF7E325F06936C4AB697A00A373C554690824C230E77A' -and $cap -ceq '50F169DDE7EDF6C8A2FC1A2FCC763BD04B43ED61E102C806947689EBB573654B' -and $dbOk -and $sd -eq 0 -and $pn -eq 0 -and $st -eq 0 -and $os.Count -eq 1 -and $hl -gt 24) { 'EOL ' + $uf[0]; 'CB2-OK' } else { 'STOP: CB.2 verification failed' }
```

**CB2.3 — build the candidate once** (not elevated; network for restore; up to 45 minutes). Expected as CB.3, with `commit=5606da23c303429c7c4711e9f5cfd43640c79289`. Record `MANIFEST-SHA256` and `BUILD-RECORD-SHA256`: they become C2 pins. On a stop, run CB2.3-X once.

```powershell
$bh = (Get-FileHash -LiteralPath (Join-Path $sc 'New-OwnerOnlyCandidate.ps1') -Algorithm SHA256).Hash; $lg = Join-Path $wk 'b1-build.log'; if ($bh -cne 'BB6E0A60A8324341A3D5DA1606977D23CF653B2480724EB33E092E1BEE7D68D7' -and $bh -cne 'B9341F75BBE2CC0CB22F5BFBB158F606CF7D04221C8C7E17E780FFF4EF5912FD') { 'STOP: builder hash differs' } elseif ((Test-Path -LiteralPath $co) -or (Test-Path -LiteralPath $lg)) { 'STOP: candidate folder or build log already exists' } else { 'BUILDER ' + $bh; $env:DOTNET_ROOT = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'Microsoft\dotnet'; $env:PATH = $env:DOTNET_ROOT + ';' + $env:PATH; $null | & $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $sc 'New-OwnerOnlyCandidate.ps1') -OutputRoot $co 2>&1 > $lg; 'BUILD-EXIT ' + $LASTEXITCODE }
$sj = Join-Path $co 'candidate-build.json'; if ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $sj)) { $j = [IO.File]::ReadAllText($sj) | ConvertFrom-Json; $mh = (Get-FileHash -LiteralPath (Join-Path $co 'candidate-manifest.json') -Algorithm SHA256).Hash; 'CANDIDATE validation=' + $j.validation + ' origin=' + $j.payloadOrigin + ' helperProtocol=' + $j.helperProtocol + ' files=' + $j.fileCount + ' bytes=' + $j.totalBytes + ' commit=' + $j.sourceCommit + ' dirty=' + $j.sourceDirty + ' sdk=' + $j.sdkVersion + ' signed=' + $j.signed + ' probeIncluded=' + $j.probeIncluded + ' probeMark=' + $j.probeMark; 'MANIFEST-SHA256 ' + $j.manifestSha256; 'MANIFEST-FILE-SHA256 ' + $mh; 'BUILD-RECORD-SHA256 ' + (Get-FileHash -LiteralPath $sj -Algorithm SHA256).Hash; if ($j.validation -ceq 'Pass' -and $j.payloadOrigin -ceq 'pinned-unsigned-v2' -and [string]$j.helperProtocol -ceq '2' -and $j.sourceCommit -ceq '5606da23c303429c7c4711e9f5cfd43640c79289' -and $j.sourceDirty -eq $false -and $j.sdkVersion -ceq '10.0.403' -and $j.signed -eq $false -and $j.probeIncluded -eq $true -and $j.probeMark -ceq 'pc-steward-probe-build-mark:1' -and $mh -ceq $j.manifestSha256) { 'CB3-OK' } else { 'STOP: candidate summary not as expected' } } else { 'STOP: build did not pass' }
```

**CB2.3-X — sanitized failure excerpt** (read-only; only after a CB2.3 stop).

```powershell
$lg = Join-Path $wk 'b1-build.log'; $up = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile); if (Test-Path -LiteralPath $lg) { @(Get-Content -LiteralPath $lg | Where-Object { $_ -match "dotnet step '|OutputRoot|must|refus|differs|missing|Conflicting|failed|error" } | Select-Object -Last 5) | ForEach-Object { $_.Replace($up, '<PROFILE>').Replace($env:USERNAME, '<USER>').Replace($env:COMPUTERNAME, '<PC>') } } else { 'NO-LOG' }
```

**CB2.4 — copy the database** (read-only on the live file). Expected as CB.4.

```powershell
$ph = (Get-FileHash -LiteralPath (Join-Path $sc 'Copy-OwnerOnlyUserDatabase.ps1') -Algorithm SHA256).Hash; $cx = $null; if ($ph -cne '0E7FDC16464B19A6BBD0B5F55908ECD16FBE0C4B2DC21A6418CE86C58630D313' -and $ph -cne '1954CEC3B5249CB14B6192EC22BD3949C393A0224AC19BD67D665A24FF6E3B03') { 'STOP: copy script hash differs' } elseif (Test-Path -LiteralPath (Join-Path $wk 'dbcopy')) { 'STOP: copy folder already exists' } else { 'COPY-SCRIPT ' + $ph; $cr = @($null | & $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $sc 'Copy-OwnerOnlyUserDatabase.ps1') -DestinationParent (Join-Path $wk 'dbcopy')); $cx = $LASTEXITCODE; 'COPY-EXIT ' + $cx }
if ($cx -eq 0) { $r = ($cr -join "`n") | ConvertFrom-Json; 'DBCOPY bytes=' + $r.bytes + ' sourceSha256=' + $r.sourceSha256 + ' copySha256=' + $r.copySha256 + ' hashesEqual=' + $r.hashesEqual + ' sidecarsPresent=' + $r.sidecarsPresent + ' pcstewardProcesses=' + $r.pcstewardProcesses + ' headerValid=' + $r.sqliteHeader.headerValid + ' walMode=' + $r.sqliteHeader.walMode + ' pageCountMatchesLength=' + $r.sqliteHeader.pageCountMatchesLength; if ($r.hashesEqual -eq $true -and $r.sourceSha256 -ceq $r.copySha256 -and $r.sourceSha256 -ceq '78697727CE44A1167537A04BE4554339028F660DA142E00894778D602BEC55BE' -and $r.sqliteHeader.headerValid -eq $true) { 'CB4-OK' } else { 'STOP: database copy not as expected' } } else { 'STOP: copy did not pass' }
```

**CB2.5 — NewPlan** (redeploy v3, `pinned-unsigned-v2` → `pinned-unsigned-v2`, user data kept). Expected as CB.5, with `oldThumbprint` `67B59B4C…`. Report `OLD-LEDGER-SHA256`; it should be `CBAB8C0A…C852`.

```powershell
$cb = [IO.File]::ReadAllBytes((Join-Path $sc 'Invoke-OwnerOnlyRedeployCaller.ps1')); $ch = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($cb)).Replace('-', ''); if ($ch -cne '62DDE7B43EAE4D5193DFAF37F76F46CD8C1B3EA6B0916E35F4071DC0014CE3C8' -and $ch -cne 'EA5618D18939F3514DD81CBFAEE23BCC304168C073D98843F0B2C3BD69A60191') { 'STOP: caller hash differs' } elseif (Test-Path -LiteralPath $pd) { 'STOP: plan folder already exists' } else { 'CALLER ' + $ch; New-Item -ItemType Directory -Path $pd -ErrorAction Stop | Out-Null; & ([ScriptBlock]::Create([Text.Encoding]::UTF8.GetString($cb))) -NewPlanPath $pf -ScriptSource $sc -CandidateRoot (Join-Path $co 'payload') -CandidateManifestPath (Join-Path $co 'candidate-manifest.json') -ExpectedOldOrigin 'pinned-unsigned-v2' -ExpectedNewOrigin 'pinned-unsigned-v2' }
$rf = @(Get-ChildItem -LiteralPath (Join-Path $wk 'dbcopy') -Directory | ForEach-Object { Join-Path $_.FullName 'copy-record.json' }); if ((Test-Path -LiteralPath $pf) -and $rf.Count -eq 1) { $cr = [IO.File]::ReadAllText($rf[0]) | ConvertFrom-Json; $pj = [IO.File]::ReadAllText($pf) | ConvertFrom-Json; $dp = @($pj.userDataFiles | Where-Object { $_.path -ceq 'pcsteward.db' }); $mh = (Get-FileHash -LiteralPath (Join-Path $co 'candidate-manifest.json') -Algorithm SHA256).Hash; 'PLAN-FILE-SHA256 ' + (Get-FileHash -LiteralPath $pf -Algorithm SHA256).Hash; 'OLD-LEDGER-SHA256 ' + $pj.oldLedgerSha256; $m1 = @($pj.userDataFiles).Count -eq 1 -and $dp.Count -eq 1 -and $dp[0].sha256 -ceq $cr.sourceSha256 -and [long]$dp[0].bytes -eq [long]$cr.bytes; $m2 = $pj.schema -ceq 'pc-steward-exact-redeploy-v3' -and $pj.candidateManifestSha256 -ceq $mh -and $pj.oldThumbprint -ceq '67B59B4C9BC3A006FBE39A72967E05F7B8C375DA' -and $pj.expectedOldOrigin -ceq 'pinned-unsigned-v2' -and $pj.expectedNewOrigin -ceq 'pinned-unsigned-v2'; 'PLAN-CHECK dbPinEqualsCopy=' + $m1 + ' manifestSignerOrigins=' + $m2; if ($m1 -and $m2) { 'CB5-OK' } else { 'STOP: plan pins differ from CB.3, CB.4 or the old signer' } } else { 'STOP: plan or copy record missing' }
```

**CB2.6 — CheckOnly** (read-only). **Paste the whole output back.** Expected as CB.6, with `oldThumbprint` `67B59B4C…`, `oldSignerNotAfterUtc` `2026-11-20T19:53:44.0000000Z`, `oldLedgerSha256` `CBAB8C0A…`, the CB2.3 manifest, database `78697727…`/200704 and `unpackagedProcess` true.

```powershell
$cb = [IO.File]::ReadAllBytes((Join-Path $sc 'Invoke-OwnerOnlyRedeployCaller.ps1')); $ch = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($cb)).Replace('-', ''); if ($ch -cne '62DDE7B43EAE4D5193DFAF37F76F46CD8C1B3EA6B0916E35F4071DC0014CE3C8' -and $ch -cne 'EA5618D18939F3514DD81CBFAEE23BCC304168C073D98843F0B2C3BD69A60191') { 'STOP: caller hash differs' } elseif (!(Test-Path -LiteralPath $pf)) { 'STOP: plan missing' } else { 'CALLER ' + $ch; 'PLAN-FILE-SHA256 ' + (Get-FileHash -LiteralPath $pf -Algorithm SHA256).Hash; $er = [string](([IO.File]::ReadAllText($pf) | ConvertFrom-Json).executionRoot); 'EXECUTION-ROOT-ID ' + $er.Substring($er.Length - 32); & ([ScriptBlock]::Create([Text.Encoding]::UTF8.GetString($cb))) -PlanPath $pf -CheckOnly }
```

**CB2.7 — old-deployment dry run** (read-only). Expected: `VALIDATOR-EXIT 0`, `OLD-DEPLOYMENT validation=Pass origin=pinned-unsigned-v2 thumbprint=67B59B4C9BC3A006FBE39A72967E05F7B8C375DA launched=False`, `CB7-OK`.

```powershell
$vh = (Get-FileHash -LiteralPath (Join-Path $sc 'Test-OwnerOnlyProtectedTestDeployment.ps1') -Algorithm SHA256).Hash; $vx = $null; if ($vh -cne '2CFB6C72121824784ECC634F13404848204E4FEABAA5F8574EB599F9409F8EE2' -and $vh -cne 'CCA0ACA362A018E9009A481EBAFBA2EE9DBF9FE6BBC4DEF5725FF785A53D9447') { 'STOP: validator hash differs' } else { 'VALIDATOR ' + $vh; $vo = @($null | & $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $sc 'Test-OwnerOnlyProtectedTestDeployment.ps1') -RequirePinnedPayload); $vx = $LASTEXITCODE; 'VALIDATOR-EXIT ' + $vx }
if ($vx -eq 0) { $v = ($vo -join "`n") | ConvertFrom-Json; 'OLD-DEPLOYMENT validation=' + $v.validation + ' origin=' + $v.payloadOrigin + ' thumbprint=' + $v.certificateThumbprint + ' launched=' + $v.launched; if ($v.validation -ceq 'Pass' -and $v.payloadOrigin -ceq 'pinned-unsigned-v2' -and $v.certificateThumbprint -ceq '67B59B4C9BC3A006FBE39A72967E05F7B8C375DA') { 'CB7-OK' } else { 'STOP: old deployment not as expected' } } else { 'STOP: validator did not pass' }
```

## 6. CB2 exec (approval CB2-exec, given after CB2.6 is pasted back)

**CB2.8 — one `-ExecuteApprovedPlan`.** **Not runnable yet:** the two placeholders are filled from CB2.6's `planSha256` and `encodedCommandSha256` in a later revision of this plan. The remover takes out the current deployment and its signer `67B59B4C…`; it never touches the data folder.

**Prompts, in order:**
1. **UAC** for **Windows PowerShell**, verified publisher **Microsoft Windows** → **Yes**.
2. **Root Certificate Store: DELETE** subject `PC Steward Owner-Only Test <GUID>`, thumbprint `67B59B4C 9BC3A006 FBE39A72 967E05F7 B8C375DA` → **Yes**.
3. **Security Warning**: install a certificate claiming to represent `PC Steward Owner-Only Test <new GUID>` → **Yes**. Note the first 8 characters of its thumbprint.
4. **Only if the install then fails:** a Root **DELETE** prompt for that **same new** certificate → **Yes**, so the rollback completes.
5. Anything else, a second UAC prompt, or a different subject or thumbprint → **No**.

Expected as CB.8: `"status": "Validated"`, exit codes 0, `userDatabaseSha256` `78697727…`, `"retry": false`, `"launched": false`.

```powershell
$cb = [IO.File]::ReadAllBytes((Join-Path $sc 'Invoke-OwnerOnlyRedeployCaller.ps1')); $ch = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($cb)).Replace('-', ''); if ($ch -cne '62DDE7B43EAE4D5193DFAF37F76F46CD8C1B3EA6B0916E35F4071DC0014CE3C8' -and $ch -cne 'EA5618D18939F3514DD81CBFAEE23BCC304168C073D98843F0B2C3BD69A60191') { 'STOP: caller hash differs' } else { 'CALLER ' + $ch; & ([ScriptBlock]::Create([Text.Encoding]::UTF8.GetString($cb))) -PlanPath $pf -ExecuteApprovedPlan -ApprovedPlanSha256 '<CB2.6-planSha256>' -ApprovedEncodedCommandSha256 '<CB2.6-encodedCommandSha256>' }
```

**CB2.9 — post-checks** (read-only, new window). Expected as CB.9, with the CB2.3 manifest, `INSTALLED-CAPABILITY 50F169DD…654B`, every `STORE … new=1 old=0` (where `old` now also counts `67B59B4C…`), and a new signer about 14 days later. The new thumbprint, its `notAfterUtc` and `LEDGER-SHA256` become C2 pins.

```powershell
$db = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'PCSteward\pcsteward.db'; $dh = (Get-FileHash -LiteralPath $db -Algorithm SHA256).Hash; $dl = (Get-Item -LiteralPath $db).Length; $sd = @(@('-wal', '-shm', '-journal') | Where-Object { Test-Path -LiteralPath ($db + $_) }).Count; 'DB sha256=' + $dh + ' bytes=' + $dl + ' equalsPin=' + ($dh -ceq '78697727CE44A1167537A04BE4554339028F660DA142E00894778D602BEC55BE' -and $dl -eq 200704) + ' sidecars=' + $sd + ' pcstewardProcesses=' + @(Get-Process -Name 'PCSteward*' -ErrorAction SilentlyContinue).Count
$vh = (Get-FileHash -LiteralPath (Join-Path $sc 'Test-OwnerOnlyProtectedTestDeployment.ps1') -Algorithm SHA256).Hash; $vx = $null; if ($vh -cne '2CFB6C72121824784ECC634F13404848204E4FEABAA5F8574EB599F9409F8EE2' -and $vh -cne 'CCA0ACA362A018E9009A481EBAFBA2EE9DBF9FE6BBC4DEF5725FF785A53D9447') { 'STOP: validator hash differs' } else { $vo = @($null | & $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $sc 'Test-OwnerOnlyProtectedTestDeployment.ps1') -RequirePinnedPayload); $vx = $LASTEXITCODE; 'VALIDATOR-EXIT ' + $vx }
if ($vx -eq 0) { $v = ($vo -join "`n") | ConvertFrom-Json; 'NEW-DEPLOYMENT validation=' + $v.validation + ' origin=' + $v.payloadOrigin + ' helperProtocol=' + $v.helperProtocol + ' manifest=' + $v.candidateManifestSha256 + ' thumbprint=' + $v.certificateThumbprint + ' launched=' + $v.launched; $pfx = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles); 'INSTALLED-CAPABILITY ' + (Get-FileHash -LiteralPath (Join-Path $pfx 'PC Steward\PCSteward.DeploymentCapability.txt') -Algorithm SHA256).Hash; $nt = [string]$v.certificateThumbprint; foreach ($sn in @('My', 'Root', 'TrustedPublisher')) { $all = @(Get-ChildItem -LiteralPath ('Cert:\CurrentUser\' + $sn)); 'STORE ' + $sn + ' new=' + @($all | Where-Object { $_.Thumbprint -ceq $nt }).Count + ' old=' + @($all | Where-Object { $_.Thumbprint -ceq '67B59B4C9BC3A006FBE39A72967E05F7B8C375DA' -or $_.Thumbprint -ceq '473DB701E6349AF18F672151E44AC166F95C53FD' -or $_.Thumbprint -ceq 'ED6FBCE8BFB12A563B2DBE0776A9C4ACBE214C55' -or $_.Thumbprint -ceq '14F34EFEFA84C367F25917B4B28803DCECE85623' }).Count }; $nc = @(Get-ChildItem -LiteralPath 'Cert:\CurrentUser\My' | Where-Object { $_.Thumbprint -ceq $nt }); if ($nc.Count -eq 1) { 'NEW-SIGNER notAfterUtc=' + $nc[0].NotAfter.ToUniversalTime().ToString('o') + ' ownerOnlySubject=' + ($nc[0].Subject -like 'CN=PC Steward Owner-Only Test *') } else { 'STOP: new signer not found once in CurrentUser My' }; 'LEDGER-SHA256 ' + (Get-FileHash -LiteralPath (Join-Path $pfx 'PC Steward OwnerOnlyTest State\deployment.json') -Algorithm SHA256).Hash } else { 'STOP: validator did not pass' }
```

## 7. C2: Part C on the new deployment (approval C2)

The same three steps as Part C plan §11, run once each, with the probe script unchanged at `831B5E174E1E5965CA0EF7E325F06936C4AB697A00A373C554690824C230E77A`.

**C2-pre** is Part C plan §11 "C-pre" exactly: the first normal launch of the new deployment, Activity → "Reviewed update outcomes", then close. It may migrate the database; C2-check's baseline is taken after it.

**C2-check** (read-only; launches nothing). Paste line V4 first. **Not runnable yet:** the five placeholders are filled from CB2.3 and CB2.9.

```powershell
$pr = Join-Path $sc 'evidence\Task50-QualificationProbe.ps1'; $prh = (Get-FileHash -LiteralPath $pr -Algorithm SHA256).Hash; if ($prh -cne '831B5E174E1E5965CA0EF7E325F06936C4AB697A00A373C554690824C230E77A') { 'STOP: probe script hash differs' } else { 'PROBE-SCRIPT ' + $prh; $null | & $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $pr -Mode Check -ActivityCheck NoBlockingRecord -BuildRecordPath (Join-Path $co 'candidate-build.json') -ExpectedBuildRecordSha256 '<CB2.3-BUILD-RECORD-SHA256>' -ExpectedManifestSha256 '<CB2.3-MANIFEST-SHA256>' -ExpectedLedgerSha256 '<CB2.9-LEDGER-SHA256>' -ExpectedSignerThumbprint '<CB2.9-new-thumbprint>' -ExpectedSignerNotAfterUtc '<CB2.9-notAfterUtc>'; 'PROBE-EXIT ' + $LASTEXITCODE }
```

**C2-run** (one launch; exactly one UAC prompt, for `PCSteward.ElevatedHelper.exe` signed by the new signer, with `--kind qualify-chocolatey-helper` in its details). Same placeholders.

```powershell
$pr = Join-Path $sc 'evidence\Task50-QualificationProbe.ps1'; $prh = (Get-FileHash -LiteralPath $pr -Algorithm SHA256).Hash; if ($prh -cne '831B5E174E1E5965CA0EF7E325F06936C4AB697A00A373C554690824C230E77A') { 'STOP: probe script hash differs' } else { 'PROBE-SCRIPT ' + $prh; $null | & $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $pr -Mode Run -ActivityCheck NoBlockingRecord -BuildRecordPath (Join-Path $co 'candidate-build.json') -ExpectedBuildRecordSha256 '<CB2.3-BUILD-RECORD-SHA256>' -ExpectedManifestSha256 '<CB2.3-MANIFEST-SHA256>' -ExpectedLedgerSha256 '<CB2.9-LEDGER-SHA256>' -ExpectedSignerThumbprint '<CB2.9-new-thumbprint>' -ExpectedSignerNotAfterUtc '<CB2.9-notAfterUtc>'; 'PROBE-EXIT ' + $LASTEXITCODE }
```

**Expected and interpretation:** Part C plan §11 "C-run" and spec §8, unchanged. Any FAIL stops use with no retry. INCONCLUSIVE proves nothing. A PASS proves UAC and pre-launch enforcement only (§1).

## 8. Approvals (templates; none given yet)

- **D2:** "I approve D2", covering D2a now and D2b after H1, each once, read-only, in my own normal Windows PowerShell window.
- **H1:** the owner's own decision to change Chocolatey. It is recorded here when the owner reports it; PC Steward neither performs nor approves it.
- **CB2-prep (CB2.1–CB2.7):** a Git fetch and a detached worktree in my clone, one build (network for restore), a database copy and one plan folder. Nothing elevated, signed or installed.
- **CB2-exec (CB2.8 + CB2.9):** given after CB2.6 is pasted back and CB2.8 is filled in.
- **C2 (C2-pre, C2-check, C2-run):** given after CB2.9, with the pins filled in.

## 9. Results

None yet.
