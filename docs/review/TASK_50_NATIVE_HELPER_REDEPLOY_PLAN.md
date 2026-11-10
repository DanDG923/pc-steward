# Task 50: redeploy with the Native AOT elevated helper, then re-run the qualification probe (revision 1, draft)

Requirements: `FR-UPD-15`, `TM-03/04/13`. ADRs: ADR-018, ADR-022, ADR-025, ADR-033. Sources: [ADR-033](../adr/ADR-033-NATIVE-AOT-ELEVATED-HELPER.md) decisions 4–6, the [security review](TASK_50_CHOCOLATEY_UPDATE_SECURITY_REVIEW.md) H1 row, and the [Part C re-run plan](TASK_50_PART_C_RERUN_PLAN.md) (this plan reuses its CB2 and C2 blocks with new pins).

**Nothing here is approved.** Every stage needs the owner's own approval, given in the session. G9-T1 (the ripgrep test) waits until C3 below passes.

## 1. Why, and what it can show

PR #31 (merged as `d9a754b`) publishes the elevated helper as one Native AOT image and changes the hash-pinned candidate builder to match. The owner OKed that builder edit on 2026-11-10. The deployment on the PC is still the C2 build (`5606da2`), whose helper is framework-dependent and does not have the PR #27 environment guard. So:

- **CB3** builds `d9a754b` with the new builder, then redeploys it the same way as CB2 (one UAC prompt, the old signer `C7372974…` removed and a new one made).
- **C3** re-runs the unchanged qualification probe on the new deployment. Its PASS is the first evidence that the native helper starts elevated, connects to the desktop's pipe, passes the peer checks both ways and exits cleanly on the owner's PC.
- With CB3.9's native-image check and a C3 PASS, the review record's H1 row moves to **Fixed** (ADR-033 "Follow-up controls").

A C3 PASS proves UAC and pre-launch enforcement only, as C2 did. It is not a real update.

## 2. Route and deadline

| Step | What | Elevated? | Approval |
|---|---|---|---|
| **P0** | Visual Studio Build Tools with the C++ workload | Yes (owner's UAC) | Done by the owner, 2026-11-10 |
| **P1** | Read-only pre-read: linker, SDK, database, runtime-control names, signer, ledger (§3) | No | Run through Remote Control; fills the database and SDK pins |
| **CB3.1–CB3.7c** | Fetch, verify, build `d9a754b`, copy the database, NewPlan, CheckOnly, old-deployment dry run, worktree integrity, toolchain check (§4) | No | CB3-prep (CB3.7c: owner decision 2026-11-10) |
| **CB3.8–CB3.9** | One redeploy run, then post-checks (§5) | One UAC | CB3-exec |
| **C3-pre, C3-check, C3-run** | The probe again on the new deployment (§6) | One UAC (C3-run) | C3 |

**Deadline.** The installed signer `C7372974A9E6257671E106F11E660BC29C928849` expires **2026-11-20T22:01:22Z**.
- CB3.2 needs more than 24 hours left after PowerShell's `[int]` rounding, so **CB3.2 must run before 2026-11-19T21:31Z**.
- CheckOnly needs a one-hour margin, so **CB3.8 must start before 2026-11-20T21:01:22Z**.
- If that is missed, the route is a separately approved remover and a fresh install (the R1/R2 pattern in the [recovery plan](TASK_50_RECOVERY_PLAN.md)), under its own plan.

**How to run every step** (unchanged, handover §4): a **new** Windows PowerShell 5.1 window per step, opened from the Start menu, never PowerShell 7 and never a window started by the Claude app; **normal (not administrator)**; paste the lines exactly, run once, never retry; after a `STOP:` line or red error text run nothing further and report the output with names and paths replaced by `<USER>`; plan files and build logs are never printed or pasted; PC Steward stays closed except during C3-pre. As for CB2, the unelevated steps (CB3.1–CB3.7, CB3.9, C3-check) may run through a Remote Control session on the PC when its process is unpackaged (the owner's standing rule: threads run non-admin steps; the owner answers admin and UAC steps). CB3.8, C3-pre and C3-run stay the owner's own, because they carry prompts or a first launch only the owner should answer or judge.

## 3. P1: read-only pre-read (fills the pins)

P1 runs once through Remote Control and changes nothing. Its result is in §8. It reports:
- the Visual Studio Build Tools instance (vswhere), the MSVC tools folder and that `link.exe` exists under `bin\Hostx64\x64`, and the Windows SDK library folders. Without the linker, CB3.3's `publish-helper-native` stops with the compiler's "platform linker not found" error before any payload is written (ADR-033 decision 6);
- `dotnet --list-sdks` and the SDK the clone's `global.json` (`10.0.403`, `rollForward: latestPatch`) selects. That version is CB3.3's SDK pin;
- the database SHA-256, length, sidecars and `PCSteward*` processes. These are CB3.2's and CB3.4's database pins. If PC Steward is opened after P1, CB3.2 stops and P1 is run again;
- the value **names** (never values) with a .NET runtime-control prefix (`DOTNET_`, `COMPlus_`, `CORECLR_`, `COR_`, `APPDOMAIN_MANAGER_`) in `HKCU\Environment`, `HKCU\Volatile Environment` and its subkeys, and a fresh `powershell.exe -NoProfile`. **The new deployment carries the PR #27 guard**, which refuses every elevated launch while such a name is present (outside the two SDK-only names). Any name found here must be removed by the owner before C3, or C3-run will be refused;
- the old signer's count, NotAfter and hours left in `CurrentUser\My`, and the installed ledger's SHA-256 (expected `C04D177CC383C255825E21D3CAF8D0E4D13C9E438EA6C27B394C73C1769A0F1C`, the CB2.9 value).

## 4. Stage CB3: build and prepare `d9a754b` (approval CB3-prep: CB3.1–CB3.7, none elevated)

These are the [Part C re-run plan](TASK_50_PART_C_RERUN_PLAN.md) §5 blocks with only these changes:

| Pin | CB2 | CB3 |
|---|---|---|
| Work folder / worktree | `PCSteward-Task50D\5606da2` | `PCSteward-Task50E\d9a754b` |
| Build commit | `5606da23…` | `d9a754b8da980a84eb4641cd58c98e27bdfd4444` (merge of PR #31) |
| Builder `New-OwnerOnlyCandidate.ps1` (LF / CRLF) | `B9341F75…` / `BB6E0A60…` | **`B4159B53CFEE3040B9E6270BBA759A15A76FCB12A31D67E2210D9E6C504C606D` / `8C4F46AE0942F95F28399B1299B3F3D5E0D23376B9AD51C2C52ABDA90ACD77C1`** (the owner-approved ADR-033 edit) |
| The other nine tooling scripts, probe script, capability statement | — | **unchanged**: byte-identical at `d9a754b` (the only `scripts` changes since `5606da2` are the builder and the new CI-only `Test-ElevatedHelperNativeAotCI.ps1`) |
| Old signer (CB3.2, CB3.5, CB3.7, CB3.8 prompt) | `67B59B4C…` | `C7372974A9E6257671E106F11E660BC29C928849`, NotAfter `2026-11-20T22:01:22Z` |
| Old ledger (CB3.5/CB3.6 report) | `CBAB8C0A…` | expected `C04D177CC383C255825E21D3CAF8D0E4D13C9E438EA6C27B394C73C1769A0F1C` |
| Database | `78697727…` / 200,704 B | unchanged pin, confirmed by P1. If PC Steward is opened before CB3.2, CB3.2 stops and P1 is run again |
| SDK in the build record | `10.0.403` | unchanged, confirmed by P1 |
| CB3.9 "old" certificates | 4 thumbprints | adds `C7372974…` |

The candidate's payload changes in one place (ADR-033 decision 2): a native `PCSteward.ElevatedHelper.exe` and no helper `.dll`, `.deps.json` or `.runtimeconfig.json`. CB3.3 adds a check of that. The manifest, build record, file count and size all differ from CB2 and are pinned from CB3.3's output.

**Line V5** starts every CB3 step:

```powershell
$wk = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) 'PCSteward-Task50E'; $wt = Join-Path $wk 'd9a754b'; $sc = Join-Path $wt 'scripts'; $co = Join-Path $wk 'candidate'; $pd = Join-Path $wk 'plan'; $pf = Join-Path $pd 'redeploy-plan.json'; $ps = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
```

**CB3.1 — fetch the build commit and add a detached worktree** (changes Git state; network). First `cd` into the PC Steward clone, paste V5, then these lines. Expected: `GIT-HOOKS postCheckout=False referenceTransaction=False`, `FETCH-EXIT 0`, `COMMIT-PRESENT-EXIT 0`, `WORKTREE-EXIT 0`.

```powershell
$ErrorActionPreference = 'Continue'; $rc = (Get-Location).ProviderPath; $env:GIT_TERMINAL_PROMPT = '0'; $ok = $false; $url = [string]($null | & git.exe -C $rc config --get remote.origin.url 2>$null); $hk = @($null | & git.exe -C $rc rev-parse --git-path hooks/post-checkout --git-path hooks/reference-transaction 2>$null); $hx = @($hk | ForEach-Object { $p = [string]$_; if (![IO.Path]::IsPathRooted($p)) { $p = Join-Path $rc $p }; (Test-Path -LiteralPath $p) -or (Test-Path -LiteralPath ($p + '.exe')) })
if (Test-Path -LiteralPath $wk) { 'STOP: work folder already exists' } elseif (!(Test-Path -LiteralPath (Join-Path $rc 'PCSteward.slnx')) -or $url -notmatch 'pc-steward') { 'STOP: not in the PC Steward clone' } elseif ($hx.Count -ne 2) { 'STOP: hook check failed' } else { 'GIT-HOOKS postCheckout=' + $hx[0] + ' referenceTransaction=' + $hx[1]; if ($hx -contains $true) { 'STOP: a Git hook exists' } else { $go = @($null | & git.exe -C $rc -c gc.auto=0 -c maintenance.auto=false fetch --no-tags origin phase/4-beta 2>&1); 'FETCH-EXIT ' + $LASTEXITCODE + ' gitLines=' + $go.Count; if ($LASTEXITCODE -eq 0) { $null | & git.exe -C $rc cat-file -e 'd9a754b8da980a84eb4641cd58c98e27bdfd4444^{commit}'; 'COMMIT-PRESENT-EXIT ' + $LASTEXITCODE; $ok = $LASTEXITCODE -eq 0 } } }
if ($ok) { New-Item -ItemType Directory -Path $wk -ErrorAction Stop | Out-Null; $go = @($null | & git.exe -C $rc -c gc.auto=0 -c maintenance.auto=false worktree add --detach $wt d9a754b8da980a84eb4641cd58c98e27bdfd4444 2>&1); 'WORKTREE-EXIT ' + $LASTEXITCODE + ' gitLines=' + $go.Count; if ($LASTEXITCODE -ne 0) { 'STOP: worktree add failed' } }
```

**CB3.2 — verify the worktree and the host (read-only).** Expected: `HEAD d9a754b8da980a84eb4641cd58c98e27bdfd4444`, `DIRTY 0`, ten `SCRIPT` lines in one form (all `CRLF` or all `LF`), the unchanged probe and capability hashes, `DB … equalsPin=True sidecars=0 pcstewardProcesses=0 scheduledTask=0`, `OLD-SIGNER inMy=1 notAfterUtc=2026-11-20T22:01:22.0000000Z hoursLeft=<more than 24>`, then `CB3.2-OK`.

```powershell
$hd = [string]($null | & git.exe -C $wt rev-parse --verify HEAD); $dt = @($null | & git.exe -C $wt --no-optional-locks status --porcelain=v1 --untracked-files=all | Where-Object { $_ }).Count; 'HEAD ' + $hd; 'DIRTY ' + $dt
$pins = [ordered]@{ 'Install-OwnerOnlyProtectedTestDeployment.ps1' = @('F5FD3ADA4784DA795792E455890DDE41DA271E0EB199617760099C5B62116E3D', 'FA2479125A43FC5DCE00C6EB8BC0E840863BA5D207BCDA3560CEADFF66EF138F'); 'Test-OwnerOnlyProtectedTestDeployment.ps1' = @('CCA0ACA362A018E9009A481EBAFBA2EE9DBF9FE6BBC4DEF5725FF785A53D9447', '2CFB6C72121824784ECC634F13404848204E4FEABAA5F8574EB599F9409F8EE2'); 'Remove-OwnerOnlyProtectedTestDeployment.ps1' = @('F42D7E392FDA49199754E516E1ECC53D230FF49C53569548D86DC64388F4248D', 'E5C4796B66A7429FED36FC9D506577FBC9F88C4E903893300214E0FC40E099D7'); 'OwnerOnlyPayloadIntegrity.ps1' = @('0874B755D88800BEBDD0D093BF90152C0B36729A6A9BBBFBE78E71136EEBA0A6', '83B87BBDE621992E32C0F042D26ED0707C106E18DF5CDD0EDF0CEA84521DD09F'); 'OwnerOnlyExecution.ps1' = @('DDF07C71EB61E2BF5E69FBA6F1A672D6CC09B8668715A90119BD725023093F9C', 'A23F2BBB874A280B5C918DAEDBCB1E5316CACB33AA670FDA682C0E2BF2E0B948'); 'Wait-OwnerOnlyDeploymentProcess.ps1' = @('F24B3235FD34AA82421C576EB284C1F8C04D26D9C56170BE0175825ADDF46C59', '2BA1A90DD230CA8587F846F75B84691BA3EA4D27C20069192F46C6602FAC520B'); 'Invoke-OwnerOnlyApprovedRedeployment.ps1' = @('AB5D61F3C61D3FCC1F7DB14B5A43B1FEF6E0B6AF5278518110FAA856F89FAF44', 'A9775FCEAE897AEBA18C838DD5FCA9BC69E355ABA4376D0EE0481E9B39D831D7'); 'Invoke-OwnerOnlyRedeployCaller.ps1' = @('EA5618D18939F3514DD81CBFAEE23BCC304168C073D98843F0B2C3BD69A60191', '62DDE7B43EAE4D5193DFAF37F76F46CD8C1B3EA6B0916E35F4071DC0014CE3C8'); 'Copy-OwnerOnlyUserDatabase.ps1' = @('1954CEC3B5249CB14B6192EC22BD3949C393A0224AC19BD67D665A24FF6E3B03', '0E7FDC16464B19A6BBD0B5F55908ECD16FBE0C4B2DC21A6418CE86C58630D313'); 'New-OwnerOnlyCandidate.ps1' = @('B4159B53CFEE3040B9E6270BBA759A15A76FCB12A31D67E2210D9E6C504C606D', '8C4F46AE0942F95F28399B1299B3F3D5E0D23376B9AD51C2C52ABDA90ACD77C1') }
$forms = @(); foreach ($n in $pins.Keys) { $h = (Get-FileHash -LiteralPath (Join-Path $sc $n) -Algorithm SHA256).Hash; $f = if ($h -ceq $pins[$n][0]) { 'LF' } elseif ($h -ceq $pins[$n][1]) { 'CRLF' } else { 'MISMATCH' }; $forms += $f; 'SCRIPT ' + $f + ' ' + $h + ' ' + $n }; $prh = (Get-FileHash -LiteralPath (Join-Path $sc 'evidence\Task50-QualificationProbe.ps1') -Algorithm SHA256).Hash; 'PROBE-SCRIPT ' + $prh; $cap = (Get-FileHash -LiteralPath (Join-Path $wt 'src\PCSteward.ElevatedHelper\PCSteward.DeploymentCapability.txt') -Algorithm SHA256).Hash; 'CAPABILITY ' + $cap
$db = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'PCSteward\pcsteward.db'; $dh = (Get-FileHash -LiteralPath $db -Algorithm SHA256).Hash; $dl = (Get-Item -LiteralPath $db).Length; $sd = @(@('-wal', '-shm', '-journal') | Where-Object { Test-Path -LiteralPath ($db + $_) }).Count; $pn = @(Get-Process -Name 'PCSteward*' -ErrorAction SilentlyContinue).Count; $st = @(Get-ScheduledTask -TaskName 'PCSteward.ScheduledCheck' -ErrorAction SilentlyContinue).Count; $dbOk = $dh -ceq '78697727CE44A1167537A04BE4554339028F660DA142E00894778D602BEC55BE' -and $dl -eq 200704; 'DB sha256=' + $dh + ' bytes=' + $dl + ' equalsPin=' + $dbOk + ' sidecars=' + $sd + ' pcstewardProcesses=' + $pn + ' scheduledTask=' + $st
$os = @(Get-ChildItem -LiteralPath 'Cert:\CurrentUser\My' | Where-Object { $_.Thumbprint -ceq 'C7372974A9E6257671E106F11E660BC29C928849' }); $hl = if ($os.Count -eq 1) { [int]($os[0].NotAfter.ToUniversalTime() - [DateTime]::UtcNow).TotalHours } else { -1 }; 'OLD-SIGNER inMy=' + $os.Count + ' notAfterUtc=' + $(if ($os.Count -eq 1) { $os[0].NotAfter.ToUniversalTime().ToString('o') } else { 'none' }) + ' hoursLeft=' + $hl
$uf = @($forms | Sort-Object -Unique); if ($hd -ceq 'd9a754b8da980a84eb4641cd58c98e27bdfd4444' -and $dt -eq 0 -and $uf.Count -eq 1 -and $uf[0] -cne 'MISMATCH' -and $prh -ceq '831B5E174E1E5965CA0EF7E325F06936C4AB697A00A373C554690824C230E77A' -and $cap -ceq '50F169DDE7EDF6C8A2FC1A2FCC763BD04B43ED61E102C806947689EBB573654B' -and $dbOk -and $sd -eq 0 -and $pn -eq 0 -and $st -eq 0 -and $os.Count -eq 1 -and $hl -gt 24) { 'EOL ' + $uf[0]; 'CB3.2-OK' } else { 'STOP: CB3.2 verification failed' }
```

**CB3.3 — build the candidate once** (not elevated; network for restore, including the Native AOT compiler package; up to 60 minutes). Expected: `BUILD-EXIT 0`, then `CANDIDATE validation=Pass origin=pinned-unsigned-v2 helperProtocol=2 … commit=d9a754b8… dirty=False sdk=10.0.403 signed=False probeIncluded=True probeMark=pc-steward-probe-build-mark:1`, and `CB3.3-OK`. Record `MANIFEST-SHA256` and `BUILD-RECORD-SHA256`: they become C3 pins. On a stop, run CB3.3-X once.

```powershell
$bh = (Get-FileHash -LiteralPath (Join-Path $sc 'New-OwnerOnlyCandidate.ps1') -Algorithm SHA256).Hash; $lg = Join-Path $wk 'b1-build.log'; if ($bh -cne '8C4F46AE0942F95F28399B1299B3F3D5E0D23376B9AD51C2C52ABDA90ACD77C1' -and $bh -cne 'B4159B53CFEE3040B9E6270BBA759A15A76FCB12A31D67E2210D9E6C504C606D') { 'STOP: builder hash differs' } elseif ((Test-Path -LiteralPath $co) -or (Test-Path -LiteralPath $lg)) { 'STOP: candidate folder or build log already exists' } else { 'BUILDER ' + $bh; $env:DOTNET_ROOT = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'Microsoft\dotnet'; $env:PATH = $env:DOTNET_ROOT + ';' + $env:PATH; $null | & $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $sc 'New-OwnerOnlyCandidate.ps1') -OutputRoot $co 2>&1 > $lg; 'BUILD-EXIT ' + $LASTEXITCODE }
$sj = Join-Path $co 'candidate-build.json'; if ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $sj)) { $j = [IO.File]::ReadAllText($sj) | ConvertFrom-Json; $mh = (Get-FileHash -LiteralPath (Join-Path $co 'candidate-manifest.json') -Algorithm SHA256).Hash; 'CANDIDATE validation=' + $j.validation + ' origin=' + $j.payloadOrigin + ' helperProtocol=' + $j.helperProtocol + ' files=' + $j.fileCount + ' bytes=' + $j.totalBytes + ' commit=' + $j.sourceCommit + ' dirty=' + $j.sourceDirty + ' sdk=' + $j.sdkVersion + ' signed=' + $j.signed + ' probeIncluded=' + $j.probeIncluded + ' probeMark=' + $j.probeMark; 'MANIFEST-SHA256 ' + $j.manifestSha256; 'MANIFEST-FILE-SHA256 ' + $mh; 'BUILD-RECORD-SHA256 ' + (Get-FileHash -LiteralPath $sj -Algorithm SHA256).Hash; if ($j.validation -ceq 'Pass' -and $j.payloadOrigin -ceq 'pinned-unsigned-v2' -and [string]$j.helperProtocol -ceq '2' -and $j.sourceCommit -ceq 'd9a754b8da980a84eb4641cd58c98e27bdfd4444' -and $j.sourceDirty -eq $false -and $j.sdkVersion -ceq '10.0.403' -and $j.signed -eq $false -and $j.probeIncluded -eq $true -and $j.probeMark -ceq 'pc-steward-probe-build-mark:1' -and $mh -ceq $j.manifestSha256) { 'CB3.3-OK' } else { 'STOP: candidate summary not as expected' } } else { 'STOP: build did not pass' }
```

Then, in the same window, the native-helper check (read-only). Expected: `NATIVE-HELPER bytes=<about 11.5 million> pe32plus=True clrHeaderRva=0 managedHelperFiles=0`, then `CB3.3-NATIVE-OK`. A CLR header of 0 with no helper `.dll` beside it means no managed code and no runtime host: the image is the native one.

```powershell
$hp = Join-Path $co 'payload\PCSteward.ElevatedHelper.exe'; if (Test-Path -LiteralPath $hp -PathType Leaf) { $hb = [IO.File]::ReadAllBytes($hp); $pe = [BitConverter]::ToInt32($hb, 0x3C); $op = $pe + 24; $mg = [BitConverter]::ToUInt16($hb, $op); $clr = if ($mg -eq 0x20B) { [BitConverter]::ToUInt32($hb, $op + 224) } else { [uint32]::MaxValue }; $mf = @(@('PCSteward.ElevatedHelper.dll', 'PCSteward.ElevatedHelper.deps.json', 'PCSteward.ElevatedHelper.runtimeconfig.json') | Where-Object { Test-Path -LiteralPath (Join-Path $co ('payload\' + $_)) }).Count; 'NATIVE-HELPER bytes=' + $hb.Length + ' pe32plus=' + ($mg -eq 0x20B) + ' clrHeaderRva=' + $clr + ' managedHelperFiles=' + $mf; if ($mg -eq 0x20B -and $clr -eq 0 -and $mf -eq 0 -and $hb.Length -gt 5MB) { 'CB3.3-NATIVE-OK' } else { 'STOP: the candidate helper is not the native image' } } else { 'STOP: candidate helper missing' }
```

**CB3.3-X — sanitized failure excerpt** (read-only; only after a CB3.3 stop).

```powershell
$lg = Join-Path $wk 'b1-build.log'; $up = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile); if (Test-Path -LiteralPath $lg) { @(Get-Content -LiteralPath $lg | Where-Object { $_ -match "dotnet step '|OutputRoot|must|refus|differs|missing|Conflicting|failed|error" } | Select-Object -Last 5) | ForEach-Object { $_.Replace($up, '<PROFILE>').Replace($env:USERNAME, '<USER>').Replace($env:COMPUTERNAME, '<PC>') } } else { 'NO-LOG' }
```

**CB3.4 — copy the database** (read-only on the live file). Expected: `COPY-EXIT 0`, hashes equal to `78697727CE44A1167537A04BE4554339028F660DA142E00894778D602BEC55BE`, `headerValid=True`, `CB3.4-OK`.

```powershell
$ph = (Get-FileHash -LiteralPath (Join-Path $sc 'Copy-OwnerOnlyUserDatabase.ps1') -Algorithm SHA256).Hash; $cx = $null; if ($ph -cne '0E7FDC16464B19A6BBD0B5F55908ECD16FBE0C4B2DC21A6418CE86C58630D313' -and $ph -cne '1954CEC3B5249CB14B6192EC22BD3949C393A0224AC19BD67D665A24FF6E3B03') { 'STOP: copy script hash differs' } elseif (Test-Path -LiteralPath (Join-Path $wk 'dbcopy')) { 'STOP: copy folder already exists' } else { 'COPY-SCRIPT ' + $ph; $cr = @($null | & $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $sc 'Copy-OwnerOnlyUserDatabase.ps1') -DestinationParent (Join-Path $wk 'dbcopy')); $cx = $LASTEXITCODE; 'COPY-EXIT ' + $cx }
if ($cx -eq 0) { $r = ($cr -join "`n") | ConvertFrom-Json; 'DBCOPY bytes=' + $r.bytes + ' sourceSha256=' + $r.sourceSha256 + ' copySha256=' + $r.copySha256 + ' hashesEqual=' + $r.hashesEqual + ' sidecarsPresent=' + $r.sidecarsPresent + ' pcstewardProcesses=' + $r.pcstewardProcesses + ' headerValid=' + $r.sqliteHeader.headerValid + ' walMode=' + $r.sqliteHeader.walMode + ' pageCountMatchesLength=' + $r.sqliteHeader.pageCountMatchesLength; if ($r.hashesEqual -eq $true -and $r.sourceSha256 -ceq $r.copySha256 -and $r.sourceSha256 -ceq '78697727CE44A1167537A04BE4554339028F660DA142E00894778D602BEC55BE' -and $r.sqliteHeader.headerValid -eq $true) { 'CB3.4-OK' } else { 'STOP: database copy not as expected' } } else { 'STOP: copy did not pass' }
```

**CB3.5 — NewPlan** (redeploy v3, `pinned-unsigned-v2` → `pinned-unsigned-v2`, user data kept). Report `OLD-LEDGER-SHA256`; it should be `C04D177C…0F1C`. Expected `CB3.5-OK`.

```powershell
$cb = [IO.File]::ReadAllBytes((Join-Path $sc 'Invoke-OwnerOnlyRedeployCaller.ps1')); $ch = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($cb)).Replace('-', ''); if ($ch -cne '62DDE7B43EAE4D5193DFAF37F76F46CD8C1B3EA6B0916E35F4071DC0014CE3C8' -and $ch -cne 'EA5618D18939F3514DD81CBFAEE23BCC304168C073D98843F0B2C3BD69A60191') { 'STOP: caller hash differs' } elseif (Test-Path -LiteralPath $pd) { 'STOP: plan folder already exists' } else { 'CALLER ' + $ch; New-Item -ItemType Directory -Path $pd -ErrorAction Stop | Out-Null; & ([ScriptBlock]::Create([Text.Encoding]::UTF8.GetString($cb))) -NewPlanPath $pf -ScriptSource $sc -CandidateRoot (Join-Path $co 'payload') -CandidateManifestPath (Join-Path $co 'candidate-manifest.json') -ExpectedOldOrigin 'pinned-unsigned-v2' -ExpectedNewOrigin 'pinned-unsigned-v2' }
$rf = @(Get-ChildItem -LiteralPath (Join-Path $wk 'dbcopy') -Directory | ForEach-Object { Join-Path $_.FullName 'copy-record.json' }); if ((Test-Path -LiteralPath $pf) -and $rf.Count -eq 1) { $cr = [IO.File]::ReadAllText($rf[0]) | ConvertFrom-Json; $pj = [IO.File]::ReadAllText($pf) | ConvertFrom-Json; $dp = @($pj.userDataFiles | Where-Object { $_.path -ceq 'pcsteward.db' }); $mh = (Get-FileHash -LiteralPath (Join-Path $co 'candidate-manifest.json') -Algorithm SHA256).Hash; 'PLAN-FILE-SHA256 ' + (Get-FileHash -LiteralPath $pf -Algorithm SHA256).Hash; 'OLD-LEDGER-SHA256 ' + $pj.oldLedgerSha256; $m1 = @($pj.userDataFiles).Count -eq 1 -and $dp.Count -eq 1 -and $dp[0].sha256 -ceq $cr.sourceSha256 -and [long]$dp[0].bytes -eq [long]$cr.bytes; $m2 = $pj.schema -ceq 'pc-steward-exact-redeploy-v3' -and $pj.candidateManifestSha256 -ceq $mh -and $pj.oldThumbprint -ceq 'C7372974A9E6257671E106F11E660BC29C928849' -and $pj.expectedOldOrigin -ceq 'pinned-unsigned-v2' -and $pj.expectedNewOrigin -ceq 'pinned-unsigned-v2'; 'PLAN-CHECK dbPinEqualsCopy=' + $m1 + ' manifestSignerOrigins=' + $m2; if ($m1 -and $m2) { 'CB3.5-OK' } else { 'STOP: plan pins differ from CB3.3, CB3.4 or the old signer' } } else { 'STOP: plan or copy record missing' }
```

**CB3.6 — CheckOnly** (read-only). **Paste the whole output back.** Expected: Pass, `oldThumbprint` `C7372974…`, `oldSignerNotAfterUtc` `2026-11-20T22:01:22.0000000Z`, `oldLedgerSha256` `C04D177C…`, the CB3.3 manifest, database `78697727CE44A1167537A04BE4554339028F660DA142E00894778D602BEC55BE` and `unpackagedProcess` true. Its `planSha256` and `encodedCommandSha256` fill CB3.8.

```powershell
$cb = [IO.File]::ReadAllBytes((Join-Path $sc 'Invoke-OwnerOnlyRedeployCaller.ps1')); $ch = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($cb)).Replace('-', ''); if ($ch -cne '62DDE7B43EAE4D5193DFAF37F76F46CD8C1B3EA6B0916E35F4071DC0014CE3C8' -and $ch -cne 'EA5618D18939F3514DD81CBFAEE23BCC304168C073D98843F0B2C3BD69A60191') { 'STOP: caller hash differs' } elseif (!(Test-Path -LiteralPath $pf)) { 'STOP: plan missing' } else { 'CALLER ' + $ch; 'PLAN-FILE-SHA256 ' + (Get-FileHash -LiteralPath $pf -Algorithm SHA256).Hash; $er = [string](([IO.File]::ReadAllText($pf) | ConvertFrom-Json).executionRoot); 'EXECUTION-ROOT-ID ' + $er.Substring($er.Length - 32); & ([ScriptBlock]::Create([Text.Encoding]::UTF8.GetString($cb))) -PlanPath $pf -CheckOnly }
```

**CB3.7 — old-deployment dry run** (read-only). Expected: `VALIDATOR-EXIT 0`, `OLD-DEPLOYMENT validation=Pass origin=pinned-unsigned-v2 thumbprint=C7372974A9E6257671E106F11E660BC29C928849 launched=False`, `CB3.7-OK`. (The validator at `d9a754b` is byte-identical to the one that installed `5606da2`, and the old ledger names its own binding set, so the old framework-dependent deployment still validates.)

```powershell
$vh = (Get-FileHash -LiteralPath (Join-Path $sc 'Test-OwnerOnlyProtectedTestDeployment.ps1') -Algorithm SHA256).Hash; $vx = $null; if ($vh -cne '2CFB6C72121824784ECC634F13404848204E4FEABAA5F8574EB599F9409F8EE2' -and $vh -cne 'CCA0ACA362A018E9009A481EBAFBA2EE9DBF9FE6BBC4DEF5725FF785A53D9447') { 'STOP: validator hash differs' } else { 'VALIDATOR ' + $vh; $vo = @($null | & $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $sc 'Test-OwnerOnlyProtectedTestDeployment.ps1') -RequirePinnedPayload); $vx = $LASTEXITCODE; 'VALIDATOR-EXIT ' + $vx }
if ($vx -eq 0) { $v = ($vo -join "`n") | ConvertFrom-Json; 'OLD-DEPLOYMENT validation=' + $v.validation + ' origin=' + $v.payloadOrigin + ' thumbprint=' + $v.certificateThumbprint + ' launched=' + $v.launched; if ($v.validation -ceq 'Pass' -and $v.payloadOrigin -ceq 'pinned-unsigned-v2' -and $v.certificateThumbprint -ceq 'C7372974A9E6257671E106F11E660BC29C928849') { 'CB3.7-OK' } else { 'STOP: old deployment not as expected' } } else { 'STOP: validator did not pass' }
```

**CB3.7b — worktree integrity (read-only; added after the Codex security review of this plan).** CB3.1 checks two Git hooks and CB3.2 checks `git status`, but a clean/smudge filter configured in the clone (or a global or system Git config, or an `info/attributes` file) could change the bytes `worktree add` wrote while `git status` still reports clean, and the build would then compile them. This step reads every file the commit tracks from the worktree and recomputes its Git blob hash, first as is and then with CRLF line endings turned back into LF (the only change `core.autocrlf` makes). It also counts configured filter drivers and attribute files. Run after CB3.7 and before CB3.8, in a new window after line V5. Expected: `WORKTREE-BLOBS entries=<about 1,100> checked=<same> crlfOnly=<n> mismatched=0 filterDrivers=0 infoAttributes=False attributesFile=False`, then `CB3.7b-OK`. Because the worktree's source files are what CB3.3 compiled, a pass shows the build read the commit's own bytes.

```powershell
$l1 = [Text.Encoding]::GetEncoding(28591); $bs = { param([byte[]]$b) $h = [Text.Encoding]::ASCII.GetBytes('blob ' + $b.Length + [char]0); $s = [Security.Cryptography.SHA1]::Create(); [void]$s.TransformBlock($h, 0, $h.Length, $null, 0); [void]$s.TransformFinalBlock($b, 0, $b.Length); [BitConverter]::ToString($s.Hash).Replace('-', '').ToLowerInvariant() }
$fd = @($null | & git.exe -C $wt config --get-regexp '^filter\.' 2>$null).Count; $af = [string]($null | & git.exe -C $wt config --get core.attributesFile 2>$null); $ia = [string]($null | & git.exe -C $wt rev-parse --git-path info/attributes 2>$null); if ($ia -and ![IO.Path]::IsPathRooted($ia)) { $ia = Join-Path $wt $ia }; $iax = [bool]($ia -and (Test-Path -LiteralPath $ia))
$tr = @($null | & git.exe -C $wt -c core.quotePath=false ls-tree -r --full-tree HEAD); $n = 0; $bad = 0; $crlf = 0; foreach ($e in $tr) { $m = [regex]::Match([string]$e, '^(\d{6}) blob ([0-9a-f]{40})\t(.+)$'); if (!$m.Success -or $m.Groups[1].Value -eq '120000') { $bad++; continue }; $n++; $p = Join-Path $wt $m.Groups[3].Value.Replace('/', '\'); if (!(Test-Path -LiteralPath $p -PathType Leaf)) { $bad++; continue }; $b = [IO.File]::ReadAllBytes($p); if ((& $bs $b) -ceq $m.Groups[2].Value) { continue }; if ((& $bs $l1.GetBytes($l1.GetString($b).Replace("`r`n", "`n"))) -ceq $m.Groups[2].Value) { $crlf++ } else { $bad++ } }
'WORKTREE-BLOBS entries=' + $tr.Count + ' checked=' + $n + ' crlfOnly=' + $crlf + ' mismatched=' + $bad + ' filterDrivers=' + $fd + ' infoAttributes=' + $iax + ' attributesFile=' + ($af.Length -gt 0); if ($bad -eq 0 -and $n -eq $tr.Count -and $n -gt 1000 -and $fd -eq 0 -and !$iax -and $af.Length -eq 0) { 'CB3.7b-OK' } else { 'STOP: the worktree does not match the build commit' }
```

**CB3.7b result and amendment.** On the owner's PC CB3.7b printed `mismatched=0` for all 1,107 tracked files (954 differ only by CRLF), but stopped on `filterDrivers=4`. All four are `filter.lfs.clean/smudge/process/required` in Git for Windows' own system config under Program Files (it adds them by default for Git LFS); the tree's only `.gitattributes` applies no filter and the clone has no LFS files. The worktree matches the build commit; the `filterDrivers=0` condition was stricter than a default Git for Windows install can meet. CB3.7c repeats the blob comparison and replaces that condition with: only those four names, only at system scope, and `git check-attr` reports no filter on any tracked path.

**CB3.7c — toolchain check (read-only; Codex security review of PR #32, owner decision 2026-11-10 "Accept it, add a signature check", carried out as a byte comparison, which is stronger).** The builder runs as the owner with the user-local SDK and the NuGet package cache, both writable by any program running as the owner, so same-user malware could have swapped a compiler file or an MSBuild script before CB3.3 and the manifest would then pin its output. The owner accepted that residual (§8) and asked for a check that rules out tampering still on disk. [`Test-OwnerBuildToolchain.ps1`](../../scripts/Test-OwnerBuildToolchain.ps1) checks, against [the pin file](task50-owner-build-toolchain-pins.json) that the CI job `owner-build-toolchain-check` produced from Microsoft's release metadata and a clean restore of `d9a754b` on a GitHub-hosted runner:

1. **Worktree:** every tracked file equals the commit's blob (as is, or with CRLF folded), and no Git filter applies (above). Git is run only from the signed Git for Windows install under Program Files, never from `Path`, and what it returns is not trusted: the commit object is re-hashed to the pinned commit ID and every tree, folder by folder, to the commit's tree, so the file list compared with the disk is the build commit's own (Codex security review of PR #32: a `git.exe` shim on `Path` could otherwise forge HEAD or the tree listing).
2. **.NET SDK:** it downloads the official `dotnet-sdk-10.0.403-win-x64.zip` into the work folder (about 300 MB), checks its pinned SHA-512, and compares every file in it with the owner's `%LocalAppData%\Microsoft\dotnet`: none missing, changed or extra in the zip's versioned folders, no newer host, runtime or SDK folder that would be chosen instead, and no junction. The one tolerated extra is an older side-by-side workload manifest folder whose every file equals Microsoft's signed manifest package from nuget.org (pinned, signature checked in CI) while the zip's own version of that manifest is higher; any workload install state or workload set under `metadata\workloads` is a stop, since it could pin the older version.
3. **NuGet packages:** every package and download-only pack in the three projects' restore output is pinned; the cached `.nupkg` matches its pinned SHA-512 and every extracted file equals its entry in that `.nupkg`; the package folder is the default one.
4. **Build hooks:** no files in `%LocalAppData%\Microsoft\MSBuild\Current`, no `Directory.Build.*`, `Directory.Packages.props`, `NuGet.Config` or `global.json` above the worktree, no untracked or ignored file outside `bin`/`obj` (the SDK would compile an ignored `.cs` file; found by listing the folder, not by asking Git), no extra `obj` import and only package-root imports in the generated NuGet props/targets, and no unexpected user-scope environment variable (MSBuild reads every environment variable as a property).

CI runs the check under Windows PowerShell 5.1 on an untouched copy (it must print `CB3.7c-OK`) and again after planting one change of each kind (it must stop and name each). Run 42879126322 did both: clean `SDK-FILES zipEntries=5577 same=5577`, `PACKAGES used=28 verified=28 filesSame=3003`, `CB3.7c-OK`; tampered, it named the edited SDK `.targets`, the extra SDK `.props`, the edited ILCompiler `.targets`, the edited `Program.cs`, the ignored `tmp/Planted.cs` and the extra `obj` import, and stopped. The committed pin file is byte-identical to that run's (`F1C4100E…345F`), and every package SHA-512 in it was re-checked against a separate download from nuget.org. After the amendment below, run 42884203759 (commit `ca8cfcb`) regenerated the pins byte-identical to the committed file (`9850FC62…BBF2`; the ten manifest packages passed `dotnet nuget verify`, author signature Microsoft Corporation, repository signature nuget.org), then: clean, with a `git.exe` shim first on `Path` and the owner's 10.0.111 manifests mirrored, `GIT … binaries=1 signers=Johannes Schindelin problems=0`, `WORKTREE … commitVerified=True treesVerified=True trees=64 … mismatched=0`, `SDK-FILES … same=5577 … extra=0 … sideBySideManifestFiles=93 workloadStateFiles=0`, `PACKAGES used=28 verified=28 filesSame=3003`, `CB3.7c-OK`; tampered, it named the edited SDK `.targets`, the extra SDK `.props`, the edited 10.0.111 `WorkloadManifest.json`, the planted install-state file, the edited ILCompiler `.targets`, the edited `Program.cs`, the ignored `tmp\Planted.cs`, the extra `obj` import and the `Target` added to the helper's `nuget.g.targets`, and stopped. It writes only its work folder.

**CB3.7c owner run 1 and amendment (2026-11-10).** The owner's first run matched everything (worktree 1,107/1,107; SDK zip SHA-512; all 5,577 zip files the same; 28/28 packages, 3,003 files; no build hooks) and stopped only on `SDK-FILES … extra=93`: an earlier 10.0.111 copy of the ten emscripten and mono.toolchain workload manifests in `sdk-manifests\10.0.100`, beside the zip's own 10.0.114 copy, written seconds before the zip was unpacked on 2026-10-29. There is no workload install state or workload set, so the SDK resolves each manifest to 10.0.114 (the zip's files) and never imports the 10.0.111 copy, and PC Steward uses no workloads. The amendment adds the ten 10.0.111 packages to the pin file (93 files, each SHA-256 from the signed nuget.org package) and accepts that folder only when every file matches and the zip's own version is higher; CI now mirrors the owner's folder in the clean run and, in the tampered run, edits one 10.0.111 file and plants an install-state file. The same update applies the Git fix in step 1. The build CB3.3 made is unchanged and needs no rebuild: the check is of the toolchain it used. **Owner run 2 (`ca8cfcb`)** passed every area (Git signed, commit and trees verified, SDK `extra=0 sideBySideManifestFiles=93 workloadStateFiles=0`, 28/28 packages, no untracked files or unexpected environment) and stopped only on `objImports=13`: every project's `nuget.g.props` holds `NuGetPackageRoot` as `$(UserProfile)\.nuget\packages\`, the form NuGet writes when the package folder is under the profile, and the check compared that literal text with the expanded path. `4240f35` expands only that exact leading macro (and only when `USERPROFILE` names the same profile); any other macro still fails. **Owner run 3 (`4240f35`, script `E1EE3A08…A3BD`) PASSED:** `GIT … problems=0`, `WORKTREE … commitVerified=True treesVerified=True trees=64 checked=1107 … mismatched=0 … pathsWithFilter=0`, `SDK-ZIP … sha512Matches=True`, `SDK-FILES … same=5577 missing=0 changed=0 extra=0 newerFolders=0 reparsePoints=0 sideBySideManifestFiles=93 workloadStateFiles=0`, `PACKAGES used=28 verified=28 filesSame=3003 … folderProblems=0`, `BUILD-HOOKS … untrackedOutsideBinObj=0 nugetGeneratedFiles=26 objImports=0 … unexpectedUserEnvironment=0`, `CB3.7c-OK`, `CHECK-EXIT 0`. Minutes earlier the Codex code review had found that an XML character reference (`&#40;`) could hide a property function from the raw-text scan; `b2af2c9` checks every value after XML decoding and requires import paths to be literal files in a verified package. The paste below runs that final version; it only adds stops.

**What it cannot show:** malware that is active while the check runs can hide from it; it rules out tampering that is still on disk, which is what a planted toolchain is. Same new window as CB3.7b (line V5 first), after CB3.7 and before CB3.8. Expected: `FETCH-EXIT 0`, the two pinned hashes, `GIT path=ProgramFiles\Git\cmd\git.exe binaries=… problems=0`, `WORKTREE … commitVerified=True treesVerified=True … mismatched=0 … nonDefaultFilterEntries=0 … pathsWithFilter=0`, `SDK-ZIP … sha512Matches=True`, `SDK-FILES … missing=0 changed=0 extra=0 newerFolders=0 reparsePoints=0 sideBySideManifestFiles=93 workloadStateFiles=0`, `PACKAGES … unpinned=0 nupkgChanged=0 missing=0 changed=0 extra=0 reparsePoints=0 folderProblems=0`, `BUILD-HOOKS msbuildUserCurrent=0 … aboveWorktree=0 untrackedOutsideBinObj=0 objImports=0 … unexpectedUserEnvironment=0 …`, then `CB3.7c-OK` and `CHECK-EXIT 0`. A `STOP:` line names the files or names (never the user's paths); report it and run nothing further.

```powershell
$cm = 'b2af2c9b847b2b5dd5078f83271beb22f91cbc1d'; $g = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'Git\cmd\git.exe'; $tf = Join-Path $wk 'toolchain-check'; $null = New-Item -ItemType Directory -Path $tf -Force; $null | & $g -C $wt fetch --quiet origin $cm 2>&1 | Out-Null; 'FETCH-EXIT ' + $LASTEXITCODE
$gb = { param([string]$spec, [string]$dst) $pi = New-Object Diagnostics.ProcessStartInfo $g, ('-C "' + $wt + '" cat-file blob ' + $spec); $pi.UseShellExecute = $false; $pi.RedirectStandardOutput = $true; $pr = [Diagnostics.Process]::Start($pi); $ms = New-Object IO.MemoryStream; $pr.StandardOutput.BaseStream.CopyTo($ms); $pr.WaitForExit(); [IO.File]::WriteAllBytes($dst, $ms.ToArray()); (Get-FileHash -LiteralPath $dst -Algorithm SHA256).Hash }
$ck = Join-Path $tf 'Test-OwnerBuildToolchain.ps1'; $pn = Join-Path $tf 'task50-owner-build-toolchain-pins.json'; $h1 = & $gb ($cm + ':scripts/Test-OwnerBuildToolchain.ps1') $ck; $h2 = & $gb ($cm + ':docs/review/task50-owner-build-toolchain-pins.json') $pn; 'CHECK-SCRIPT ' + $h1; 'PIN-FILE ' + $h2
if ($h1 -cne '9EA268071A051FD29D0FBA82C0C2C205F7642C0ADBDC57E917CA09B29AD98418' -or $h2 -cne '9850FC627070D6D3655E4FC8A9E2ECAB86AF55F56816C5B059667563A4D4BBF2') { 'STOP: the check script or pin file differs' } else { $null | & $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $ck -Worktree $wt -PinFile $pn -WorkFolder $tf; 'CHECK-EXIT ' + $LASTEXITCODE }
```

## 5. CB3 exec (approval CB3-exec, given after CB3.6 and CB3.7c are pasted back)

**CB3.8 — one `-ExecuteApprovedPlan`.** Filled from CB3.6 (2026-11-10: plan `CFEF3D77…C0FA`, encoded command `111458A4…41A3`). The remover takes out the current deployment and its signer `C7372974…`; it never touches the data folder.

**Prompts, in order:**
1. **UAC** for **Windows PowerShell**, verified publisher **Microsoft Windows** → **Yes**.
2. **Root Certificate Store: DELETE** subject `PC Steward Owner-Only Test <GUID>`, thumbprint `C7372974 A9E62576 71E106F1 1E660BC2 9C928849` → **Yes**.
3. **Security Warning**: install a certificate claiming to represent `PC Steward Owner-Only Test <new GUID>` → **Yes**. Note the first 8 characters of its thumbprint.
4. **Only if the install then fails:** a Root **DELETE** prompt for that **same new** certificate → **Yes**, so the rollback completes.
5. Anything else, a second UAC prompt, or a different subject or thumbprint → **No**.

Expected: `"status": "Validated"`, exit codes 0, `userDatabaseSha256` `78697727CE44A1167537A04BE4554339028F660DA142E00894778D602BEC55BE`, `"retry": false`, `"launched": false`.

```powershell
$cb = [IO.File]::ReadAllBytes((Join-Path $sc 'Invoke-OwnerOnlyRedeployCaller.ps1')); $ch = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($cb)).Replace('-', ''); if ($ch -cne '62DDE7B43EAE4D5193DFAF37F76F46CD8C1B3EA6B0916E35F4071DC0014CE3C8' -and $ch -cne 'EA5618D18939F3514DD81CBFAEE23BCC304168C073D98843F0B2C3BD69A60191') { 'STOP: caller hash differs' } else { 'CALLER ' + $ch; & ([ScriptBlock]::Create([Text.Encoding]::UTF8.GetString($cb))) -PlanPath $pf -ExecuteApprovedPlan -ApprovedPlanSha256 'CFEF3D77B6F513C2B7477ECA38BCD8BD3DF86791C00ACE5B5C69913316FBC0FA' -ApprovedEncodedCommandSha256 '111458A4A55916D929B2F12839A4800DDA2B9A4C4CCBD919A9FA5F3B867941A3' }
```

**CB3.9 — post-checks** (read-only, new window). Expected: the CB3.3 manifest, `INSTALLED-CAPABILITY 50F169DD…654B`, `STORE My new=1 old=0`, `STORE Root new=1 old=0`, `STORE TrustedPublisher new=1 old=0` (`old` now also counts `C7372974…` and `67B59B4C…`). Before 2026-11-19T21:57Z, `TrustedPublisher old=1` is the known stale private-view entry `ED6FBCE8…`, as at CB2.9, and does not block, and a new signer about 14 days later. The new thumbprint, its `notAfterUtc` and `LEDGER-SHA256` become C3 pins.

```powershell
$db = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'PCSteward\pcsteward.db'; $dh = (Get-FileHash -LiteralPath $db -Algorithm SHA256).Hash; $dl = (Get-Item -LiteralPath $db).Length; $sd = @(@('-wal', '-shm', '-journal') | Where-Object { Test-Path -LiteralPath ($db + $_) }).Count; 'DB sha256=' + $dh + ' bytes=' + $dl + ' equalsPin=' + ($dh -ceq '78697727CE44A1167537A04BE4554339028F660DA142E00894778D602BEC55BE' -and $dl -eq 200704) + ' sidecars=' + $sd + ' pcstewardProcesses=' + @(Get-Process -Name 'PCSteward*' -ErrorAction SilentlyContinue).Count
$vh = (Get-FileHash -LiteralPath (Join-Path $sc 'Test-OwnerOnlyProtectedTestDeployment.ps1') -Algorithm SHA256).Hash; $vx = $null; if ($vh -cne '2CFB6C72121824784ECC634F13404848204E4FEABAA5F8574EB599F9409F8EE2' -and $vh -cne 'CCA0ACA362A018E9009A481EBAFBA2EE9DBF9FE6BBC4DEF5725FF785A53D9447') { 'STOP: validator hash differs' } else { $vo = @($null | & $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $sc 'Test-OwnerOnlyProtectedTestDeployment.ps1') -RequirePinnedPayload); $vx = $LASTEXITCODE; 'VALIDATOR-EXIT ' + $vx }
if ($vx -eq 0) { $v = ($vo -join "`n") | ConvertFrom-Json; 'NEW-DEPLOYMENT validation=' + $v.validation + ' origin=' + $v.payloadOrigin + ' helperProtocol=' + $v.helperProtocol + ' manifest=' + $v.candidateManifestSha256 + ' thumbprint=' + $v.certificateThumbprint + ' launched=' + $v.launched; $pfx = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles); 'INSTALLED-CAPABILITY ' + (Get-FileHash -LiteralPath (Join-Path $pfx 'PC Steward\PCSteward.DeploymentCapability.txt') -Algorithm SHA256).Hash; $nt = [string]$v.certificateThumbprint; foreach ($sn in @('My', 'Root', 'TrustedPublisher')) { $all = @(Get-ChildItem -LiteralPath ('Cert:\CurrentUser\' + $sn)); 'STORE ' + $sn + ' new=' + @($all | Where-Object { $_.Thumbprint -ceq $nt }).Count + ' old=' + @($all | Where-Object { $_.Thumbprint -ceq 'C7372974A9E6257671E106F11E660BC29C928849' -or $_.Thumbprint -ceq '67B59B4C9BC3A006FBE39A72967E05F7B8C375DA' -or $_.Thumbprint -ceq '473DB701E6349AF18F672151E44AC166F95C53FD' -or $_.Thumbprint -ceq 'ED6FBCE8BFB12A563B2DBE0776A9C4ACBE214C55' -or $_.Thumbprint -ceq '14F34EFEFA84C367F25917B4B28803DCECE85623' }).Count }; $nc = @(Get-ChildItem -LiteralPath 'Cert:\CurrentUser\My' | Where-Object { $_.Thumbprint -ceq $nt }); if ($nc.Count -eq 1) { 'NEW-SIGNER notAfterUtc=' + $nc[0].NotAfter.ToUniversalTime().ToString('o') + ' ownerOnlySubject=' + ($nc[0].Subject -like 'CN=PC Steward Owner-Only Test *') } else { 'STOP: new signer not found once in CurrentUser My' }; 'LEDGER-SHA256 ' + (Get-FileHash -LiteralPath (Join-Path $pfx 'PC Steward OwnerOnlyTest State\deployment.json') -Algorithm SHA256).Hash } else { 'STOP: validator did not pass' }
```

Then, in the same window, the installed native-helper check. Expected: `INSTALLED-NATIVE-HELPER bytes=<as CB3.3> pe32plus=True clrHeaderRva=0 managedHelperFiles=0 signature=Valid`. This line is the host evidence ADR-033 asks for before H1 is marked Fixed.

```powershell
$pfx = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles); $ih = Join-Path $pfx 'PC Steward\PCSteward.ElevatedHelper.exe'; $hb = [IO.File]::ReadAllBytes($ih); $pe = [BitConverter]::ToInt32($hb, 0x3C); $op = $pe + 24; $mg = [BitConverter]::ToUInt16($hb, $op); $clr = if ($mg -eq 0x20B) { [BitConverter]::ToUInt32($hb, $op + 224) } else { [uint32]::MaxValue }; $mf = @(@('PCSteward.ElevatedHelper.dll', 'PCSteward.ElevatedHelper.deps.json', 'PCSteward.ElevatedHelper.runtimeconfig.json') | Where-Object { Test-Path -LiteralPath (Join-Path $pfx ('PC Steward\' + $_)) }).Count; 'INSTALLED-NATIVE-HELPER bytes=' + $hb.Length + ' pe32plus=' + ($mg -eq 0x20B) + ' clrHeaderRva=' + $clr + ' managedHelperFiles=' + $mf + ' signature=' + (Get-AuthenticodeSignature -LiteralPath $ih).Status
```

## 6. C3: the qualification probe on the new deployment (approval C3)

The same three steps as C2 ([Part C re-run plan](TASK_50_PART_C_RERUN_PLAN.md) §7), run once each, with the probe script unchanged at `831B5E174E1E5965CA0EF7E325F06936C4AB697A00A373C554690824C230E77A`. **Precondition:** P1 found no runtime-control name, or the owner has removed each one since; otherwise the new desktop's guard refuses C3-run's launch.

**C3-pre:** the first normal launch of the new deployment, from the Start menu. Open Activity → "Reviewed update outcomes", confirm "No reviewed update outcomes are recorded.", then close PC Steward without a scan or refresh.

**C3-check** (read-only; launches nothing). Paste line V5 first. The five pins are filled from CB3.3 and CB3.9.

```powershell
$pr = Join-Path $sc 'evidence\Task50-QualificationProbe.ps1'; $prh = (Get-FileHash -LiteralPath $pr -Algorithm SHA256).Hash; if ($prh -cne '831B5E174E1E5965CA0EF7E325F06936C4AB697A00A373C554690824C230E77A') { 'STOP: probe script hash differs' } else { 'PROBE-SCRIPT ' + $prh; $null | & $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $pr -Mode Check -ActivityCheck NoBlockingRecord -BuildRecordPath (Join-Path $co 'candidate-build.json') -ExpectedBuildRecordSha256 'B3E9C256B3344D6862C7A81C3F7276256E37C2FA191AC967BA0EFA080DBB4535' -ExpectedManifestSha256 '7CAD14C5DA2FA0056CBEEFBA26B79423861E8388FB0222C64A0CDF0E7F494BE3' -ExpectedLedgerSha256 '<CB3.9-LEDGER-SHA256>' -ExpectedSignerThumbprint '<CB3.9-THUMBPRINT>' -ExpectedSignerNotAfterUtc '<CB3.9-NOTAFTER>'; 'PROBE-EXIT ' + $LASTEXITCODE }
```

**C3-run** (one launch; exactly one UAC prompt, for `PCSteward.ElevatedHelper.exe` signed by the new signer, with `--kind qualify-chocolatey-helper` in its details). Same pins.

```powershell
$pr = Join-Path $sc 'evidence\Task50-QualificationProbe.ps1'; $prh = (Get-FileHash -LiteralPath $pr -Algorithm SHA256).Hash; if ($prh -cne '831B5E174E1E5965CA0EF7E325F06936C4AB697A00A373C554690824C230E77A') { 'STOP: probe script hash differs' } else { 'PROBE-SCRIPT ' + $prh; $null | & $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $pr -Mode Run -ActivityCheck NoBlockingRecord -BuildRecordPath (Join-Path $co 'candidate-build.json') -ExpectedBuildRecordSha256 'B3E9C256B3344D6862C7A81C3F7276256E37C2FA191AC967BA0EFA080DBB4535' -ExpectedManifestSha256 '7CAD14C5DA2FA0056CBEEFBA26B79423861E8388FB0222C64A0CDF0E7F494BE3' -ExpectedLedgerSha256 '<CB3.9-LEDGER-SHA256>' -ExpectedSignerThumbprint '<CB3.9-THUMBPRINT>' -ExpectedSignerNotAfterUtc '<CB3.9-NOTAFTER>'; 'PROBE-EXIT ' + $LASTEXITCODE }
```

**Expected and interpretation:** as C2-run, spec §8. Any FAIL stops use with no retry; INCONCLUSIVE proves nothing. A FAIL at the helper's own start (no `pipeConnected`) on a native image is a new finding under ADR-033 "Reconsider when", not a reason to go back to the framework-dependent helper without the owner's decision.

## 7. Approvals

- **CB3-prep (CB3.1–CB3.7):** a Git fetch and a detached worktree in my clone, one build (network for restore), a database copy and one plan folder. Nothing elevated, signed or installed.
- **CB3-exec (CB3.8 + CB3.9):** given after CB3.6 and CB3.7c are pasted back and CB3.8 is filled in.
- **C3 (C3-pre, C3-check, C3-run):** given after CB3.9, with the pins filled in.

## 8. Results

**P0 (owner, 2026-11-10):** Visual Studio Build Tools 2022 with the C++ workload installed.

### CI pre-flight: the native helper signs as the installer needs (2026-11-10, run 42870642053)

The informational `native-helper-signing-diagnostic` job built this branch's candidate with the real builder (98 files, native `PCSteward.ElevatedHelper.exe` of 11,488,256 bytes) and signed copies of both executables the installer's way. With the throwaway certificate trusted, `Set-AuthenticodeSignature` returned `Valid` with the expected signer for the native helper, in a temp folder and in a Program Files folder with the installer's protected DACL, so the installer's acceptance check would pass (`installerWouldAccept=True`, `signtool verify` exit 0, no overlay). The desktop apphost and both controls gave the same result. Untrusted, every file gave the expected untrusted-root error. This is CI evidence only; CB3.8 and CB3.9 are the evidence on the owner's PC.

### Result P1 (2026-11-10, about 18:00Z): ready for CB3

Run once through Remote Control in Windows PowerShell 5.1, unelevated, `-NoProfile`; nothing changed.

```text
BUILD-TOOLS instance=Microsoft Visual Studio\2022\BuildTools msvc=14.44.35207 linkExe=True windowsSdkLib=10.0.26100.0
SDKS 10.0.403 (user-local dotnet); clone global.json selects 10.0.403
DB sha256=78697727CE44A1167537A04BE4554339028F660DA142E00894778D602BEC55BE bytes=200704 sidecars=0 pcstewardProcesses=0
RUNTIME-CONTROL-NAMES HKCU\Environment=none HKCU\Volatile Environment (and subkeys)=none freshPowerShell=none machine=none
OLD-SIGNER C7372974A9E6257671E106F11E660BC29C928849 inMy=1 notAfterUtc=2026-11-20T22:01:22Z hoursLeft=244
LEDGER-SHA256 C04D177CC383C255825E21D3CAF8D0E4D13C9E438EA6C27B394C73C1769A0F1C
```

Every pin above matches §4: the database is unchanged since C2, the SDK is the pinned one, and the ledger is the CB2.9 value. No runtime-control name is set, so the new desktop's guard will not refuse C3-run. **Next:** approval CB3-prep. CB3.2 must run before 2026-11-19T21:31Z.

### Result CB3.1–CB3.7 (2026-11-10, 18:15–18:31Z): passed

CB3.1 and CB3.2 were pasted by the owner; CB3.3–CB3.7 ran once each, unelevated, in fresh Windows PowerShell 5.1 processes from a runner script on the owner's PC that used this plan's blocks unchanged and stopped at the first failure. Every line matched §4 (sanitized):

```text
CB3.2  HEAD d9a754b8da980a84eb4641cd58c98e27bdfd4444 DIRTY 0; ten SCRIPT lines CRLF and pinned; PROBE and CAPABILITY pinned; DB equalsPin=True; OLD-SIGNER hoursLeft=244; CB3.2-OK
CB3.3  BUILDER 8C4F46AE…77C1 (CRLF pin)  BUILD-EXIT 0
       CANDIDATE validation=Pass origin=pinned-unsigned-v2 helperProtocol=2 files=98 bytes=63874351 commit=d9a754b8… dirty=False sdk=10.0.403 signed=False probeIncluded=True probeMark=pc-steward-probe-build-mark:1
       MANIFEST-SHA256 7CAD14C5DA2FA0056CBEEFBA26B79423861E8388FB0222C64A0CDF0E7F494BE3 (file hash equal)
       BUILD-RECORD-SHA256 B3E9C256B3344D6862C7A81C3F7276256E37C2FA191AC967BA0EFA080DBB4535   CB3.3-OK
       NATIVE-HELPER bytes=11488256 pe32plus=True clrHeaderRva=0 managedHelperFiles=0   CB3.3-NATIVE-OK
CB3.4  COPY-SCRIPT 0E7FDC16…D313 (CRLF pin)  DBCOPY bytes=200704 source=copy=78697727…55BE headerValid=True walMode=True   CB3.4-OK
CB3.5  CALLER 62DDE7B4…C3C8 (CRLF pin)  NewPlan Written, executed=false elevated=false
       PLAN-FILE-SHA256 CFEF3D77B6F513C2B7477ECA38BCD8BD3DF86791C00ACE5B5C69913316FBC0FA  OLD-LEDGER-SHA256 C04D177C…0F1C  PLAN-CHECK True/True   CB3.5-OK
CB3.6  CheckOnly Pass: planSha256 CFEF3D77…C0FA, encodedCommandSha256 111458A4A55916D929B2F12839A4800DDA2B9A4C4CCBD919A9FA5F3B867941A3,
       candidate manifest 7CAD14C5…4BE3 (98 files, 63,874,351 bytes), old signer C7372974… (NotAfter 2026-11-20T22:01:22Z), old ledger C04D177C…0F1C,
       database 78697727…55BE, six script hashes equal to their CRLF pins, elevated=false, unpackaged (15700)
CB3.7  VALIDATOR 2CFB6C72…8EE2 (CRLF pin)  OLD-DEPLOYMENT validation=Pass origin=pinned-unsigned-v2 thumbprint=C7372974… launched=False   CB3.7-OK
CB3.7b WORKTREE-BLOBS entries=1107 checked=1107 crlfOnly=954 mismatched=0 filterDrivers=4 infoAttributes=False attributesFile=False   (STOP on filterDrivers; see §4)
```

The helper in the candidate is the native image. CB3.8 is filled in from CB3.6 and the two C3 build pins from CB3.3. The CB3.7b stop is explained in §4 (Git for Windows' default system Git LFS entries; no filter applies to any tracked file); CB3.7c repeats that check with the amended condition.

### Owner decision on the toolchain residual (2026-11-10, 18:30Z)

The Codex security review of this plan (PR #32) reported that same-user malware could swap the user-local SDK before CB3.3, so the redeploy would sign and install its output. Asked how to handle it, the owner chose **"Accept it, add a signature check"**: the residual of building on the owner's own PC is accepted (ADR-022, ADR-033 decision 5 option a), and a read-only check runs before CB3.8. It was carried out as CB3.7c, which compares bytes with Microsoft's published SDK zip and NuGet packages rather than only Authenticode signatures, so edited MSBuild scripts, which carry no signature, are covered too. A full fix (an attested CI build) was offered and not chosen.
