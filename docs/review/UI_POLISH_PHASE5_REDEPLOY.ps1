# PC Steward UI polish phase 5: build 57d8f47 and redeploy it to the Program Files test install (one paste).
# Run in a NEW Windows PowerShell 5.1 window opened from the Start menu (not PowerShell 7, not Run as administrator, not a
# window started by the Claude app). Keep PC Steward closed. It stops at the first problem and prints a STOP line.
# Steps (Task 50 native redeploy plan, CB3 blocks with new pins): checks, worktree, build (10 to 60 minutes), toolchain
# check, database copy, plan, CheckOnly, then it waits for you to press Y before the one UAC prompt, then post-checks.
& {
$ErrorActionPreference = 'Continue'
$cm = '57d8f47d03e3c15ccd4b51938662d52cbea21692'
$oldTp = 'A935E49391C5D111D932BBAB1B9B8B0C05C1A476'
$oldLedger = '87E0173B49F7B52F41FB05F8096B50D4F71423CE3F404247D56F09D4AA590FCD'
$up = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
$wk = Join-Path $up 'PCSteward-UI5'; $wt = Join-Path $wk '57d8f47'; $sc = Join-Path $wt 'scripts'; $co = Join-Path $wk 'candidate'; $pd = Join-Path $wk 'plan'; $pf = Join-Path $pd 'redeploy-plan.json'; $tf = Join-Path $wk 'toolchain-check'
$ps = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$pfx = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$g = Join-Path $pfx 'Git\cmd\git.exe'
$rc = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)) 'Codex\PC Steward\pc-steward'
$db = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'PCSteward\pcsteward.db'
function San([string]$t) { if ($null -eq $t) { return '' }; $t.Replace($up, '<PROFILE>').Replace($env:USERNAME, '<USER>').Replace($env:COMPUTERNAME, '<PC>') }
function Say([string]$t) { Write-Host (San $t) }
function Halt([string]$t) { throw ('STOP: ' + $t) }
function Sha([string]$p) { (Get-FileHash -LiteralPath $p -Algorithm SHA256 -ErrorAction Stop).Hash }
$cl = New-Object Collections.Generic.List[string]
$old = @($oldTp, 'C7372974A9E6257671E106F11E660BC29C928849', '67B59B4C9BC3A006FBE39A72967E05F7B8C375DA', '473DB701E6349AF18F672151E44AC166F95C53FD', 'ED6FBCE8BFB12A563B2DBE0776A9C4ACBE214C55', '14F34EFEFA84C367F25917B4B28803DCECE85623')
$pins = [ordered]@{ 'Install-OwnerOnlyProtectedTestDeployment.ps1' = @('F5FD3ADA4784DA795792E455890DDE41DA271E0EB199617760099C5B62116E3D', 'FA2479125A43FC5DCE00C6EB8BC0E840863BA5D207BCDA3560CEADFF66EF138F'); 'Test-OwnerOnlyProtectedTestDeployment.ps1' = @('CCA0ACA362A018E9009A481EBAFBA2EE9DBF9FE6BBC4DEF5725FF785A53D9447', '2CFB6C72121824784ECC634F13404848204E4FEABAA5F8574EB599F9409F8EE2'); 'Remove-OwnerOnlyProtectedTestDeployment.ps1' = @('F42D7E392FDA49199754E516E1ECC53D230FF49C53569548D86DC64388F4248D', 'E5C4796B66A7429FED36FC9D506577FBC9F88C4E903893300214E0FC40E099D7'); 'OwnerOnlyPayloadIntegrity.ps1' = @('0874B755D88800BEBDD0D093BF90152C0B36729A6A9BBBFBE78E71136EEBA0A6', '83B87BBDE621992E32C0F042D26ED0707C106E18DF5CDD0EDF0CEA84521DD09F'); 'OwnerOnlyExecution.ps1' = @('DDF07C71EB61E2BF5E69FBA6F1A672D6CC09B8668715A90119BD725023093F9C', 'A23F2BBB874A280B5C918DAEDBCB1E5316CACB33AA670FDA682C0E2BF2E0B948'); 'Wait-OwnerOnlyDeploymentProcess.ps1' = @('F24B3235FD34AA82421C576EB284C1F8C04D26D9C56170BE0175825ADDF46C59', '2BA1A90DD230CA8587F846F75B84691BA3EA4D27C20069192F46C6602FAC520B'); 'Invoke-OwnerOnlyApprovedRedeployment.ps1' = @('AB5D61F3C61D3FCC1F7DB14B5A43B1FEF6E0B6AF5278518110FAA856F89FAF44', 'A9775FCEAE897AEBA18C838DD5FCA9BC69E355ABA4376D0EE0481E9B39D831D7'); 'Invoke-OwnerOnlyRedeployCaller.ps1' = @('EA5618D18939F3514DD81CBFAEE23BCC304168C073D98843F0B2C3BD69A60191', '62DDE7B43EAE4D5193DFAF37F76F46CD8C1B3EA6B0916E35F4071DC0014CE3C8'); 'Copy-OwnerOnlyUserDatabase.ps1' = @('1954CEC3B5249CB14B6192EC22BD3949C393A0224AC19BD67D665A24FF6E3B03', '0E7FDC16464B19A6BBD0B5F55908ECD16FBE0C4B2DC21A6418CE86C58630D313'); 'New-OwnerOnlyCandidate.ps1' = @('B4159B53CFEE3040B9E6270BBA759A15A76FCB12A31D67E2210D9E6C504C606D', '8C4F46AE0942F95F28399B1299B3F3D5E0D23376B9AD51C2C52ABDA90ACD77C1') }
try {
    # --- U0: pre-flight (read-only) ---
    Say ('U0 start ' + [DateTime]::UtcNow.ToString('u'))
    if ($PSVersionTable.PSEdition -ne 'Desktop') { Halt 'use Windows PowerShell 5.1 from the Start menu, not PowerShell 7' }
    if (([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { Halt 'this window is elevated; open a normal (not administrator) Windows PowerShell window' }
    if (!(Test-Path -LiteralPath $g -PathType Leaf)) { Halt 'Git for Windows not found under Program Files' }
    $url = [string]($null | & $g -C $rc config --get remote.origin.url 2>$null)
    if (!(Test-Path -LiteralPath (Join-Path $rc 'PCSteward.slnx')) -or $url -notmatch 'pc-steward') { Halt 'the PC Steward clone was not found in Documents\Codex\PC Steward\pc-steward' }
    if (Test-Path -LiteralPath $wk) { Halt 'work folder PCSteward-UI5 already exists (this paste runs once; ask for a new one)' }
    $pn = @(Get-Process -Name 'PCSteward*' -ErrorAction SilentlyContinue).Count; $sd = @(@('-wal', '-shm', '-journal') | Where-Object { Test-Path -LiteralPath ($db + $_) }).Count; $st = @(Get-ScheduledTask -TaskName 'PCSteward.ScheduledCheck' -ErrorAction SilentlyContinue).Count
    $dbh = Sha $db; $dbl = (Get-Item -LiteralPath $db).Length
    Say ('U0 DB sha256=' + $dbh + ' bytes=' + $dbl + ' sidecars=' + $sd + ' pcstewardProcesses=' + $pn + ' scheduledTask=' + $st)
    if ($pn -ne 0) { Halt 'PC Steward is running; close it and paste again' }
    if ($sd -ne 0 -or $st -ne 0) { Halt 'database sidecar files or the scheduled check task exist' }
    $os = @(Get-ChildItem -LiteralPath 'Cert:\CurrentUser\My' | Where-Object { $_.Thumbprint -ceq $oldTp }); $hl = if ($os.Count -eq 1) { [int]($os[0].NotAfter.ToUniversalTime() - [DateTime]::UtcNow).TotalHours } else { -1 }
    Say ('U0 OLD-SIGNER inMy=' + $os.Count + ' hoursLeft=' + $hl)
    if ($os.Count -ne 1 -or $hl -le 24) { Halt 'the installed test signer A935E493 is missing or expires within 24 hours' }
    $lh = Sha (Join-Path $pfx 'PC Steward OwnerOnlyTest State\deployment.json'); Say ('U0 OLD-LEDGER ' + $lh)
    if ($lh -cne $oldLedger) { Halt 'the installed ledger is not the one recorded at CB3.9' }
    $hk = @($null | & $g -C $rc rev-parse --git-path hooks/post-checkout --git-path hooks/reference-transaction 2>$null); $hx = @($hk | ForEach-Object { $p = [string]$_; if (![IO.Path]::IsPathRooted($p)) { $p = Join-Path $rc $p }; (Test-Path -LiteralPath $p) -or (Test-Path -LiteralPath ($p + '.exe')) })
    if ($hx.Count -ne 2 -or ($hx -contains $true)) { Halt 'a Git hook exists in the clone (or the hook check failed)' }
    Say 'U0-OK'

    # --- U1: fetch the build commit and add a detached worktree (CB3.1) ---
    $env:GIT_TERMINAL_PROMPT = '0'
    $go = @($null | & $g -C $rc -c gc.auto=0 -c maintenance.auto=false fetch --no-tags origin phase/4-beta 2>&1); Say ('U1 FETCH-EXIT ' + $LASTEXITCODE)
    if ($LASTEXITCODE -ne 0) { Halt 'git fetch failed' }
    $null | & $g -C $rc cat-file -e ($cm + '^{commit}') 2>$null; if ($LASTEXITCODE -ne 0) { Halt 'build commit not present after fetch' }
    New-Item -ItemType Directory -Path $wk -ErrorAction Stop | Out-Null
    $go = @($null | & $g -C $rc -c gc.auto=0 -c maintenance.auto=false worktree add --detach $wt $cm 2>&1); Say ('U1 WORKTREE-EXIT ' + $LASTEXITCODE)
    if ($LASTEXITCODE -ne 0) { Halt 'worktree add failed' }

    # --- U2: verify the worktree's scripts (CB3.2) ---
    $hd = [string]($null | & $g -C $wt rev-parse --verify HEAD); $dt = @($null | & $g -C $wt --no-optional-locks status --porcelain=v1 --untracked-files=all | Where-Object { $_ }).Count
    $forms = @(); foreach ($n in $pins.Keys) { $h = Sha (Join-Path $sc $n); $f = if ($h -ceq $pins[$n][0]) { 'LF' } elseif ($h -ceq $pins[$n][1]) { 'CRLF' } else { 'MISMATCH' }; $forms += $f; if ($f -eq 'MISMATCH') { Say ('U2 SCRIPT MISMATCH ' + $h + ' ' + $n) } }
    $prh = Sha (Join-Path $sc 'evidence\Task50-QualificationProbe.ps1'); $cap = Sha (Join-Path $wt 'src\PCSteward.ElevatedHelper\PCSteward.DeploymentCapability.txt')
    $uf = @($forms | Sort-Object -Unique)
    Say ('U2 HEAD ' + $hd + ' DIRTY ' + $dt + ' scripts=' + ($uf -join ',') + ' probe=' + $prh.Substring(0, 8) + ' capability=' + $cap.Substring(0, 8))
    if ($hd -cne $cm -or $dt -ne 0 -or $uf.Count -ne 1 -or $uf[0] -ceq 'MISMATCH' -or $prh -cne '831B5E174E1E5965CA0EF7E325F06936C4AB697A00A373C554690824C230E77A' -or $cap -cne '50F169DDE7EDF6C8A2FC1A2FCC763BD04B43ED61E102C806947689EBB573654B') { Halt 'worktree verification failed' }
    Say 'U2-OK'

    # --- U3: the current deployment still validates (CB3.7, read-only) ---
    $vo = @($null | & $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $sc 'Test-OwnerOnlyProtectedTestDeployment.ps1') -RequirePinnedPayload); $vx = $LASTEXITCODE
    if ($vx -ne 0) { Halt ('validator of the current deployment exited ' + $vx) }
    $v = ($vo -join "`n") | ConvertFrom-Json
    Say ('U3 OLD-DEPLOYMENT validation=' + $v.validation + ' origin=' + $v.payloadOrigin + ' thumbprint=' + $v.certificateThumbprint + ' launched=' + $v.launched)
    if ($v.validation -cne 'Pass' -or $v.payloadOrigin -cne 'pinned-unsigned-v2' -or $v.certificateThumbprint -cne $oldTp) { Halt 'current deployment not as expected' }
    Say 'U3-OK'

    # --- U4: build the candidate once (CB3.3; 10 to 60 minutes) ---
    Say ('U4 building ' + $cm.Substring(0, 7) + ' (this is the long step) ' + [DateTime]::UtcNow.ToString('u'))
    $lg = Join-Path $wk 'build.log'
    $env:DOTNET_ROOT = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'Microsoft\dotnet'; $env:PATH = $env:DOTNET_ROOT + ';' + $env:PATH
    $null | & $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $sc 'New-OwnerOnlyCandidate.ps1') -OutputRoot $co 2>&1 > $lg; $bx = $LASTEXITCODE
    Say ('U4 BUILD-EXIT ' + $bx + ' ' + [DateTime]::UtcNow.ToString('u'))
    $sj = Join-Path $co 'candidate-build.json'
    if ($bx -ne 0 -or !(Test-Path -LiteralPath $sj)) { @(Get-Content -LiteralPath $lg | Where-Object { $_ -match "dotnet step '|OutputRoot|must|refus|differs|missing|Conflicting|failed|error" } | Select-Object -Last 5) | ForEach-Object { Say ('  ' + $_) }; Halt 'build did not pass' }
    $j = [IO.File]::ReadAllText($sj) | ConvertFrom-Json; $mh = Sha (Join-Path $co 'candidate-manifest.json')
    Say ('U4 CANDIDATE validation=' + $j.validation + ' origin=' + $j.payloadOrigin + ' helperProtocol=' + $j.helperProtocol + ' files=' + $j.fileCount + ' bytes=' + $j.totalBytes + ' commit=' + $j.sourceCommit + ' dirty=' + $j.sourceDirty + ' sdk=' + $j.sdkVersion + ' signed=' + $j.signed + ' probeIncluded=' + $j.probeIncluded)
    Say ('U4 MANIFEST-SHA256 ' + $j.manifestSha256 + ' BUILD-RECORD-SHA256 ' + (Sha $sj))
    if ($j.validation -cne 'Pass' -or $j.payloadOrigin -cne 'pinned-unsigned-v2' -or [string]$j.helperProtocol -cne '2' -or $j.sourceCommit -cne $cm -or $j.sourceDirty -ne $false -or $j.sdkVersion -cne '10.0.403' -or $j.signed -ne $false -or $j.probeIncluded -ne $true -or $j.probeMark -cne 'pc-steward-probe-build-mark:1' -or $mh -cne $j.manifestSha256) { Halt 'candidate summary not as expected' }
    $hp = Join-Path $co 'payload\PCSteward.ElevatedHelper.exe'; if (!(Test-Path -LiteralPath $hp -PathType Leaf)) { Halt 'candidate helper missing' }
    $hb = [IO.File]::ReadAllBytes($hp); $pe = [BitConverter]::ToInt32($hb, 0x3C); $op = $pe + 24; $mg = [BitConverter]::ToUInt16($hb, $op); $clr = if ($mg -eq 0x20B) { [BitConverter]::ToUInt32($hb, $op + 224) } else { [uint32]::MaxValue }
    $mf = @(@('PCSteward.ElevatedHelper.dll', 'PCSteward.ElevatedHelper.deps.json', 'PCSteward.ElevatedHelper.runtimeconfig.json') | Where-Object { Test-Path -LiteralPath (Join-Path $co ('payload\' + $_)) }).Count
    Say ('U4 NATIVE-HELPER bytes=' + $hb.Length + ' pe32plus=' + ($mg -eq 0x20B) + ' clrHeaderRva=' + $clr + ' managedHelperFiles=' + $mf)
    if ($mg -ne 0x20B -or $clr -ne 0 -or $mf -ne 0 -or $hb.Length -le 5MB) { Halt 'the candidate helper is not the native image' }
    Say 'U4-OK'

    # --- U5: toolchain check (CB3.7c; downloads the official SDK zip, about 300 MB) ---
    $null = New-Item -ItemType Directory -Path $tf -Force
    # Reuse the SDK zip CB3.7c downloaded on 2026-11-10 if it is still there; the check verifies its pinned SHA-512 either way.
    $oz = Join-Path $up 'PCSteward-Task50E\toolchain-check\dotnet-sdk-10.0.403-win-x64.zip'; if (Test-Path -LiteralPath $oz -PathType Leaf) { Copy-Item -LiteralPath $oz -Destination (Join-Path $tf 'dotnet-sdk-10.0.403-win-x64.zip') -ErrorAction Stop; Say 'U5 reusing the SDK zip from CB3.7c' }
    $gb = { param([string]$spec, [string]$dst) $pi = New-Object Diagnostics.ProcessStartInfo $g, ('-C "' + $wt + '" cat-file blob ' + $spec); $pi.UseShellExecute = $false; $pi.RedirectStandardOutput = $true; $pr = [Diagnostics.Process]::Start($pi); $ms = New-Object IO.MemoryStream; $pr.StandardOutput.BaseStream.CopyTo($ms); $pr.WaitForExit(); [IO.File]::WriteAllBytes($dst, $ms.ToArray()); Sha $dst }
    $ck = Join-Path $tf 'Test-OwnerBuildToolchain.ps1'; $po = Join-Path $tf 'pins-original.json'; $pn2 = Join-Path $tf 'task50-owner-build-toolchain-pins.json'
    $h1 = & $gb ($cm + ':scripts/Test-OwnerBuildToolchain.ps1') $ck; $h2 = & $gb ($cm + ':docs/review/task50-owner-build-toolchain-pins.json') $po
    if ($h1 -cne 'E69A2C06181979FC21ED53947FD109D2DE6E542EBA4A1B1C04B91A363340820B' -or $h2 -cne '9850FC627070D6D3655E4FC8A9E2ECAB86AF55F56816C5B059667563A4D4BBF2') { Halt 'the check script or pin file differs' }
    # The packages and SDK are unchanged since d9a754b; only the pinned build commit moves to 57d8f47.
    $pt = [IO.File]::ReadAllText($po, [Text.Encoding]::ASCII); [IO.File]::WriteAllText($pn2, $pt.Replace('d9a754b8da980a84eb4641cd58c98e27bdfd4444', $cm), (New-Object Text.ASCIIEncoding))
    $h3 = Sha $pn2; Say ('U5 CHECK-SCRIPT ' + $h1.Substring(0, 8) + ' PIN-FILE ' + $h3.Substring(0, 8))
    if ($h3 -cne '2A54AC23FB89E2FAC620FA57F8A6217B3E8EB0D2DC5E877ABB92A0DB3027A8D7') { Halt 'the re-pinned toolchain file is not the expected one' }
    $tc = @($null | & $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $ck -Worktree $wt -PinFile $pn2 -WorkFolder $tf); $tx = $LASTEXITCODE
    $tc | ForEach-Object { Say ('  ' + $_) }
    if ($tx -ne 0 -or ($tc -notcontains 'CB3.7c-OK')) { Halt ('toolchain check did not pass (exit ' + $tx + ')') }
    Say 'U5-OK'

    # --- U6: copy the database (CB3.4) ---
    $cr = @($null | & $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $sc 'Copy-OwnerOnlyUserDatabase.ps1') -DestinationParent (Join-Path $wk 'dbcopy')); $cx = $LASTEXITCODE
    if ($cx -ne 0) { Halt ('database copy exited ' + $cx) }
    $r = ($cr -join "`n") | ConvertFrom-Json
    Say ('U6 DBCOPY bytes=' + $r.bytes + ' hashesEqual=' + $r.hashesEqual + ' sameAsStart=' + ($r.sourceSha256 -ceq $dbh) + ' headerValid=' + $r.sqliteHeader.headerValid + ' pcstewardProcesses=' + $r.pcstewardProcesses)
    if ($r.hashesEqual -ne $true -or $r.sourceSha256 -cne $r.copySha256 -or $r.sourceSha256 -cne $dbh -or $r.sqliteHeader.headerValid -ne $true) { Halt 'database copy not as expected (was PC Steward opened during the build?)' }
    Say 'U6-OK'

    # --- U7: NewPlan (CB3.5) ---
    $cb = [IO.File]::ReadAllBytes((Join-Path $sc 'Invoke-OwnerOnlyRedeployCaller.ps1')); $ch = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($cb)).Replace('-', '')
    if ($ch -cne $pins['Invoke-OwnerOnlyRedeployCaller.ps1'][0] -and $ch -cne $pins['Invoke-OwnerOnlyRedeployCaller.ps1'][1]) { Halt 'caller hash differs' }
    $caller = [ScriptBlock]::Create([Text.Encoding]::UTF8.GetString($cb))
    New-Item -ItemType Directory -Path $pd -ErrorAction Stop | Out-Null
    $cl.Clear(); & $caller -NewPlanPath $pf -ScriptSource $sc -CandidateRoot (Join-Path $co 'payload') -CandidateManifestPath (Join-Path $co 'candidate-manifest.json') -ExpectedOldOrigin 'pinned-unsigned-v2' -ExpectedNewOrigin 'pinned-unsigned-v2' | ForEach-Object { [void]$cl.Add([string]$_) }
    $npj = ($cl -join "`n") | ConvertFrom-Json
    $rf = @(Get-ChildItem -LiteralPath (Join-Path $wk 'dbcopy') -Directory | ForEach-Object { Join-Path $_.FullName 'copy-record.json' })
    if ($npj.result -cne 'Written' -or !(Test-Path -LiteralPath $pf) -or $rf.Count -ne 1) { Halt 'plan was not written' }
    $crr = [IO.File]::ReadAllText($rf[0]) | ConvertFrom-Json; $pj = [IO.File]::ReadAllText($pf) | ConvertFrom-Json; $dp = @($pj.userDataFiles | Where-Object { $_.path -ceq 'pcsteward.db' })
    $m1 = @($pj.userDataFiles).Count -eq 1 -and $dp.Count -eq 1 -and $dp[0].sha256 -ceq $crr.sourceSha256 -and $dp[0].sha256 -ceq $dbh -and [long]$dp[0].bytes -eq [long]$crr.bytes
    $m2 = $pj.schema -ceq 'pc-steward-exact-redeploy-v3' -and $pj.candidateManifestSha256 -ceq $mh -and $pj.oldThumbprint -ceq $oldTp -and $pj.oldLedgerSha256 -ceq $oldLedger -and $pj.expectedOldOrigin -ceq 'pinned-unsigned-v2' -and $pj.expectedNewOrigin -ceq 'pinned-unsigned-v2'
    Say ('U7 PLAN-FILE-SHA256 ' + (Sha $pf) + ' dbPinEqualsCopy=' + $m1 + ' manifestSignerLedgerOrigins=' + $m2)
    if (!$m1 -or !$m2) { Halt 'plan pins differ from the build, the database copy, the old signer or the old ledger' }
    Say 'U7-OK'

    # --- U8: CheckOnly (CB3.6, read-only) ---
    $cl.Clear(); & $caller -PlanPath $pf -CheckOnly | ForEach-Object { [void]$cl.Add([string]$_) }
    $c6 = ($cl -join "`n") | ConvertFrom-Json
    Say ('U8 CHECKONLY result=' + $c6.result + ' plan=' + $c6.planSha256 + ' encodedCommand=' + $c6.encodedCommandSha256 + ' manifest=' + $c6.candidateManifestSha256 + ' files=' + $c6.candidateFiles + ' oldThumbprint=' + $c6.oldThumbprint + ' oldSignerNotAfterUtc=' + $c6.oldSignerNotAfterUtc + ' oldLedger=' + $c6.oldLedgerSha256 + ' db=' + $c6.userDatabase.sha256 + ' elevated=' + $c6.elevated + ' unpackagedProcess=' + $c6.unpackagedProcess)
    if ($c6.result -cne 'Pass' -or $c6.candidateManifestSha256 -cne $mh -or $c6.oldThumbprint -cne $oldTp -or $c6.oldLedgerSha256 -cne $oldLedger -or $c6.userDatabase.sha256 -cne $dbh -or $c6.elevated -ne $false -or $c6.unpackagedProcess -ne $true -or $c6.planSha256 -cne (Sha $pf) -or $c6.encodedCommandSha256 -cnotmatch '^[A-F0-9]{64}$') { Halt 'CheckOnly did not pass as expected' }
    Say 'U8-OK'

    # --- U9: the one redeploy run (CB3.8; one UAC prompt and two certificate prompts) ---
    [Console]::Beep(880, 300); [Console]::Beep(660, 300)
    Write-Host ''
    Write-Host 'Everything checked. Next: ONE UAC prompt for Windows PowerShell (publisher Microsoft Windows) -> Yes;' -ForegroundColor Yellow
    Write-Host ('then a Root certificate DELETE prompt for thumbprint A935E493 91C5D111 ... -> Yes;') -ForegroundColor Yellow
    Write-Host 'then a Security Warning to install a new "PC Steward Owner-Only Test" certificate -> Yes.' -ForegroundColor Yellow
    Write-Host 'Only if the install then fails: a DELETE prompt for that same new certificate -> Yes. Anything else -> No.' -ForegroundColor Yellow
    Write-Host 'Press Y to start the redeploy now, or N to stop here (nothing installed yet).' -ForegroundColor Yellow
    while ([Console]::KeyAvailable) { [void][Console]::ReadKey($true) }
    $key = [Console]::ReadKey($true)
    if ($key.KeyChar -ne 'y' -and $key.KeyChar -ne 'Y') { Halt 'you chose not to redeploy (nothing was installed; ask for a new paste to try again)' }
    Say ('U9 redeploy starting ' + [DateTime]::UtcNow.ToString('u'))
    $cl.Clear(); & $caller -PlanPath $pf -ExecuteApprovedPlan -ApprovedPlanSha256 $c6.planSha256 -ApprovedEncodedCommandSha256 $c6.encodedCommandSha256 | ForEach-Object { [void]$cl.Add([string]$_) }
    $e9 = ($cl -join "`n") | ConvertFrom-Json
    Say ('U9 status=' + $e9.status + ' resultPhase=' + $e9.resultPhase + ' nativeExitCode=' + $e9.nativeExitCode + ' resultExitCode=' + $e9.resultExitCode + ' resultFailure=' + $e9.resultFailure + ' postcheckFailure=' + $e9.postcheckFailure + ' failure=' + $e9.failure + ' dbUnchanged=' + ($e9.userDatabaseSha256 -ceq $dbh) + ' retry=' + $e9.retry + ' launched=' + $e9.launched)
    if ($e9.status -cne 'Validated' -or $e9.userDatabaseSha256 -cne $dbh) { Halt 'the redeploy did not validate; do not retry' }
    Say 'U9-OK'

    # --- U10: post-checks (CB3.9, read-only) ---
    $dh2 = Sha $db; Say ('U10 DB unchanged=' + ($dh2 -ceq $dbh) + ' pcstewardProcesses=' + @(Get-Process -Name 'PCSteward*' -ErrorAction SilentlyContinue).Count)
    $vo = @($null | & $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $sc 'Test-OwnerOnlyProtectedTestDeployment.ps1') -RequirePinnedPayload); $vx = $LASTEXITCODE
    if ($vx -ne 0) { Halt ('validator of the new deployment exited ' + $vx) }
    $v = ($vo -join "`n") | ConvertFrom-Json; $nt = [string]$v.certificateThumbprint
    Say ('U10 NEW-DEPLOYMENT validation=' + $v.validation + ' origin=' + $v.payloadOrigin + ' helperProtocol=' + $v.helperProtocol + ' manifestMatches=' + ($v.candidateManifestSha256 -ceq $mh) + ' thumbprint=' + $nt + ' launched=' + $v.launched)
    $capOk = (Sha (Join-Path $pfx 'PC Steward\PCSteward.DeploymentCapability.txt')) -ceq '50F169DDE7EDF6C8A2FC1A2FCC763BD04B43ED61E102C806947689EBB573654B'
    $storeOk = $true
    foreach ($sn in @('My', 'Root', 'TrustedPublisher')) {
        $all = @(Get-ChildItem -LiteralPath ('Cert:\CurrentUser\' + $sn)); $nn = @($all | Where-Object { $_.Thumbprint -ceq $nt }).Count; $oo = @($all | Where-Object { $old -contains $_.Thumbprint })
        Say ('U10 STORE ' + $sn + ' new=' + $nn + ' old=' + $oo.Count + $(if ($oo.Count) { ' (' + (($oo | ForEach-Object { $_.Thumbprint.Substring(0, 8) }) -join ',') + ')' } else { '' }))
        $staleOk = $sn -ceq 'TrustedPublisher' -and $oo.Count -eq 1 -and $oo[0].Thumbprint -ceq 'ED6FBCE8BFB12A563B2DBE0776A9C4ACBE214C55'
        if ($nn -ne 1 -or ($oo.Count -ne 0 -and !$staleOk)) { $storeOk = $false }
    }
    $nc = @(Get-ChildItem -LiteralPath 'Cert:\CurrentUser\My' | Where-Object { $_.Thumbprint -ceq $nt })
    if ($nc.Count -eq 1) { Say ('U10 NEW-SIGNER notAfterUtc=' + $nc[0].NotAfter.ToUniversalTime().ToString('o') + ' ownerOnlySubject=' + ($nc[0].Subject -like 'CN=PC Steward Owner-Only Test *')) }
    Say ('U10 INSTALLED-CAPABILITY ok=' + $capOk + ' LEDGER-SHA256 ' + (Sha (Join-Path $pfx 'PC Steward OwnerOnlyTest State\deployment.json')))
    $ih = Join-Path $pfx 'PC Steward\PCSteward.ElevatedHelper.exe'; $hb = [IO.File]::ReadAllBytes($ih); $pe = [BitConverter]::ToInt32($hb, 0x3C); $op = $pe + 24; $mg = [BitConverter]::ToUInt16($hb, $op); $clr = if ($mg -eq 0x20B) { [BitConverter]::ToUInt32($hb, $op + 224) } else { [uint32]::MaxValue }
    $mf = @(@('PCSteward.ElevatedHelper.dll', 'PCSteward.ElevatedHelper.deps.json', 'PCSteward.ElevatedHelper.runtimeconfig.json') | Where-Object { Test-Path -LiteralPath (Join-Path $pfx ('PC Steward\' + $_)) }).Count; $sig = (Get-AuthenticodeSignature -LiteralPath $ih).Status
    Say ('U10 INSTALLED-NATIVE-HELPER bytes=' + $hb.Length + ' pe32plus=' + ($mg -eq 0x20B) + ' clrHeaderRva=' + $clr + ' managedHelperFiles=' + $mf + ' signature=' + $sig)
    if ($v.validation -cne 'Pass' -or $v.candidateManifestSha256 -cne $mh -or $nt -ceq $oldTp -or !$capOk -or !$storeOk -or $nc.Count -ne 1 -or $mg -ne 0x20B -or $clr -ne 0 -or $mf -ne 0 -or [string]$sig -cne 'Valid') { Halt 'post-checks not as expected (the new build is installed; report this output before opening it)' }
    Say 'U10-OK'
    Say ('REDEPLOY-COMPLETE ' + [DateTime]::UtcNow.ToString('u') + ': open PC Steward from the File Explorer window that opens now (PCSteward.Desktop.exe).')
    Start-Process -FilePath (Join-Path $env:SystemRoot 'explorer.exe') -ArgumentList ('"' + (Join-Path $pfx 'PC Steward') + '"')
} catch {
    if ($cl.Count) { $cl | ForEach-Object { Write-Host (San $_) } }
    Write-Host (San ([string]$_.Exception.Message)) -ForegroundColor Red
    if ([string]$_.Exception.Message -notmatch '^STOP:') { Write-Host 'STOP: unexpected error (above). Nothing further runs; paste this output back.' -ForegroundColor Red }
}
}
