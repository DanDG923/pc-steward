# PC Steward: Task 50 cleanup (revision 1). Record: docs/review/TASK_50_CLEANUP.md.
# Removes what the Task 50 work, the UI redeploys and the release pastes left behind: their work folders and Git
# worktrees in your user folder, the clone's stale worktree records and its one old stash, the owner-only test
# certificates, and the one-off "PC Steward OwnerOnlyTest Execution <id>" and download folders in Program Files and
# ProgramData.
#
# Run it once in a NEW Windows PowerShell 5.1 window opened from the Start menu: the blue "Windows PowerShell", not
# PowerShell 7, not "Run as administrator", and not a window started by the Claude app.
# It first lists everything it would change, then waits for you to type YES. Nothing changes before that.
#
# Prompts, in order. Answer No to anything else:
#   1. Only if an old test certificate is still in your Root store: a "Root Certificate Store" box asking to DELETE
#      "PC Steward Owner-Only Test <id>". Answer Yes, once for each such certificate.
#   2. Only if there are administrator folders to remove: one UAC prompt for Windows PowerShell (publisher Microsoft
#      Windows). Answer Yes.
#
# It keeps: the installed PC Steward and your PC Steward data; every signed release (the "release" folder inside each
# PCSteward-Release-<date> folder, holding the signed MSI); the release token; the YubiKey and the release signing
# certificates; Chocolatey and ripgrep; your clone's branches and files. Before it drops the stash it saves it as two
# patch files next to your clone. It skips, and reports, any folder that holds a link to somewhere else.
# Paste the lines it prints back to Claude. User names and paths are replaced with <USER>, <PROFILE> and <PC>.
& {
$ErrorActionPreference = 'Stop'
# Load PowerShell's own modules only from Windows PowerShell's protected folder, never from a user-writable one.
$env:PSModulePath = [IO.Path]::Combine($PSHOME, 'Modules')
$up = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
function San([string]$t) { if ($null -eq $t) { return '' }; $t.Replace($up, '<PROFILE>').Replace($env:USERNAME, '<USER>').Replace($env:COMPUTERNAME, '<PC>') }
function Say([string]$t) { Write-Host (San $t) }
function HasLink([string]$p) { (((Get-Item -LiteralPath $p -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) -or (@(Get-ChildItem -LiteralPath $p -Recurse -Force -Attributes ReparsePoint -ErrorAction Stop).Count -gt 0) }
function SizeMB([string]$p) { $s = (@(Get-ChildItem -LiteralPath $p -Recurse -Force -File -ErrorAction SilentlyContinue) | Measure-Object -Property Length -Sum).Sum; if ($null -eq $s) { $s = 0 }; [math]::Round($s / 1MB, 1) }
function RemoveTree([string]$p) {
    try { Remove-Item -LiteralPath $p -Recurse -Force }
    catch {
        foreach ($f in @(Get-ChildItem -LiteralPath $p -Recurse -Force -File -ErrorAction SilentlyContinue)) { try { $f.Attributes = [IO.FileAttributes]::Normal } catch { } }
        [IO.Directory]::Delete($p, $true)
    }
    if (Test-Path -LiteralPath $p) { throw 'it is still there' }
}
try {
    # --- Checks: the right window, Git and the clone ---
    if ($PSVersionTable.PSEdition -ne 'Desktop' -or ![Environment]::Is64BitProcess -or !$PSHOME.TrimEnd('\').Equals((Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0'), [StringComparison]::OrdinalIgnoreCase)) { throw 'use 64-bit Windows PowerShell 5.1 from the Start menu, not PowerShell 7' }
    if (([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'this window is elevated; open a normal (not administrator) Windows PowerShell window' }
    $pt = [Reflection.Emit.AssemblyBuilder]::DefineDynamicAssembly([Reflection.AssemblyName]::new('PCStewardCleanupPackage'), 'Run').DefineDynamicModule('P').DefineType('P', 'Public,Sealed,Abstract')
    $pt.DefinePInvokeMethod('GetCurrentPackageFullName', 'kernel32.dll', 'Public,Static,PinvokeImpl', 'Standard', [int], [Type[]]@([uint32].MakeByRefType(), [IntPtr]), 'Winapi', 'Unicode').SetImplementationFlags('PreserveSig')
    $pkg = [int]$pt.CreateType().GetMethod('GetCurrentPackageFullName').Invoke($null, [object[]]@([uint32]0, [IntPtr]::Zero))
    if ($pkg -ne 15700) { throw ('this window belongs to an app package (' + $pkg + '); open Windows PowerShell from the Start menu') }
    # Nothing may change how the elevated .NET Framework PowerShell starts (same rule as Invoke-PCStewardReleaseInstall.ps1).
    $envNames = @([Environment]::GetEnvironmentVariables().Keys) + @([Environment]::GetEnvironmentVariables([EnvironmentVariableTarget]::User).Keys) + @([Environment]::GetEnvironmentVariables([EnvironmentVariableTarget]::Machine).Keys)
    if (@($envNames | Where-Object { $_ -match '^(COMPLUS_|COR_ENABLE_PROFILING$|COR_PROFILER|APPDOMAIN_MANAGER_)' }).Count) { throw 'a .NET Framework startup override (COMPLUS_, COR_PROFILER or APPDOMAIN_MANAGER_) is set in the environment; remove it first. Nothing changed.' }
    foreach ($scope in @('MachinePolicy', 'UserPolicy')) { if ([string](Get-ExecutionPolicy -Scope $scope) -cne 'Undefined') { throw 'an enforced PowerShell execution policy is configured; this paste does not override it. Nothing changed.' } }
    $g = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)) 'Git\cmd\git.exe'
    $rc = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)) 'Codex\PC Steward\pc-steward'
    if (!(Test-Path -LiteralPath $g -PathType Leaf) -or !(Test-Path -LiteralPath (Join-Path $rc 'PCSteward.slnx') -PathType Leaf)) { throw 'Git or the PC Steward clone was not found' }
    $env:GIT_TERMINAL_PROMPT = '0'
    $pf = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
    $pd = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
    $stateRoot = Join-Path $pf 'PC Steward OwnerOnlyTest State'
    $stashPin = 'ac8ed6004c99e5efe7390fba58e1680be88afebf'
    $workPattern = '\APCSteward-(Task50[B-E]|UI5|UI[5-7]-\d{8}-\d{6})\z'
    $releasePattern = '\APCSteward-Release-\d{8}-\d{6}\z'
    $adminPf = '\APC Steward OwnerOnlyTest Execution [0-9a-f]{32}\z'
    $adminPd = '\APCSteward-((YubicoTools|DotNetSdk)-[0-9a-f]{32}|G9T1)\z'
    function Git([string[]]$a) { $ErrorActionPreference = 'Continue'; $o = @($null | & $g -C $rc -c gc.auto=0 -c maintenance.auto=false @a 2>&1 | ForEach-Object { [string]$_ }); $script:gitExit = $LASTEXITCODE; $o }

    # --- Inventory (read-only) ---
    $work = @(); $linked = @(); $tidy = @(); $odd = @()
    foreach ($d in @(Get-ChildItem -LiteralPath $up -Directory -Force | Where-Object { $_.Name -cmatch $workPattern } | Sort-Object Name)) {
        if (HasLink $d.FullName) { $linked += $d.Name } else { $work += $d }
    }
    foreach ($d in @(Get-ChildItem -LiteralPath $up -Directory -Force | Where-Object { $_.Name -cmatch $releasePattern } | Sort-Object Name)) {
        $release = Join-Path $d.FullName 'release'
        $msis = @(); if (Test-Path -LiteralPath $release -PathType Container) { $msis = @(Get-ChildItem -LiteralPath $release -Filter 'PCSteward-*.msi' -File -Force) }
        $extras = @(Get-ChildItem -LiteralPath $d.FullName -Force | Where-Object { $_.Name -ne 'release' })
        if (!(Test-Path -LiteralPath (Join-Path $release 'release.json') -PathType Leaf) -or $msis.Count -ne 1) { $odd += $d.Name }
        elseif ($extras.Count -eq 0) { }
        elseif (HasLink $d.FullName) { $linked += $d.Name }
        else { $tidy += [pscustomobject]@{ Folder = $d; Extras = $extras; Msi = $msis[0].Name } }
    }
    $stashes = @(Git @('stash', 'list', '--format=%H') | Where-Object { $_ -match '\A[0-9a-f]{40}\z' })
    $dropStash = $stashes.Count -eq 1 -and $stashes[0] -ceq $stashPin
    $certs = @(foreach ($s in 'TrustedPublisher', 'My', 'Root') { foreach ($c in @(Get-ChildItem -LiteralPath ('Cert:\CurrentUser\' + $s))) { if ($c.Subject -like 'CN=PC Steward Owner-Only Test *') { [pscustomobject]@{ Store = $s; Thumbprint = $c.Thumbprint } } } })
    $stateThere = Test-Path -LiteralPath $stateRoot
    $admin = @(@(Get-ChildItem -LiteralPath $pf -Directory -Force | Where-Object { $_.Name -cmatch $adminPf }) + @(Get-ChildItem -LiteralPath $pd -Directory -Force | Where-Object { $_.Name -cmatch $adminPd }))

    Say 'PLAN (nothing has changed yet):'
    $freed = 0.0
    foreach ($d in $work) { $mb = SizeMB $d.FullName; $freed += $mb; Say ('  remove ' + $d.Name + ' (' + $mb + ' MB)') }
    foreach ($t in $tidy) { $mb = 0.0; foreach ($x in $t.Extras) { $mb += SizeMB $x.FullName }; $freed += $mb; Say ('  tidy ' + $t.Folder.Name + ': keep release\' + $t.Msi + ', remove ' + (($t.Extras | ForEach-Object { $_.Name }) -join ', ') + ' (' + $mb + ' MB)') }
    Say '  Git: forget the removed worktrees and clear stale worktree records in the clone'
    if ($dropStash) { Say '  Git: save the 2026-11-11 stash ("pre-sync local Task 50 edits") as two patch files next to the clone, then drop it' }
    elseif ($stashes.Count) { Say ('  Git: leave the stash alone (' + $stashes.Count + ' entr(ies), first ' + $stashes[0].Substring(0, 8) + ', not the expected ' + $stashPin.Substring(0, 8) + ')') }
    if ($stateThere -and $certs.Count) { Say '  certificates: LEFT ALONE, because an owner-only test deployment still exists (Program Files\PC Steward OwnerOnlyTest State)' }
    else { foreach ($c in $certs) { Say ('  certificate: remove PC Steward Owner-Only Test ' + $c.Thumbprint.Substring(0, 8) + '... from CurrentUser\' + $c.Store) } }
    foreach ($a in $admin) { Say ('  administrator: remove ' + $a.Name + ' (' + (SizeMB $a.FullName) + ' MB)') }
    foreach ($n in $linked) { Say ('  SKIP ' + $n + ': it contains a link') }
    foreach ($n in $odd) { Say ('  SKIP ' + $n + ': its release folder is not one signed release; left as it is') }
    Say ('  about ' + [math]::Round($freed / 1024, 2) + ' GB in your user folder')
    $answer = Read-Host 'Type YES to go ahead (anything else stops with nothing changed)'
    if ($answer -cne 'YES') { Say 'STOPPED: nothing changed.'; return }

    # --- C1: work folders and release tidy-up (your user folder) ---
    foreach ($d in $work) { try { RemoveTree $d.FullName; Say ('C1 removed ' + $d.Name) } catch { Say ('C1 COULD NOT remove ' + $d.Name + ': ' + $_.Exception.Message) } }
    foreach ($t in $tidy) {
        $ok = $true
        foreach ($x in $t.Extras) { try { RemoveTree $x.FullName } catch { $ok = $false; Say ('C1 COULD NOT remove ' + $t.Folder.Name + '\' + $x.Name + ': ' + $_.Exception.Message) } }
        if ($ok) { Say ('C1 tidied ' + $t.Folder.Name + ', kept release\' + $t.Msi) }
    }

    # --- C2: the clone (worktree records and the stash) ---
    $null = Git @('worktree', 'prune')
    Say ('C2 git worktree prune exit ' + $script:gitExit)
    $records = Join-Path $rc '.git\worktrees'
    if (Test-Path -LiteralPath $records -PathType Container) {
        foreach ($r in @(Get-ChildItem -LiteralPath $records -Directory -Force)) {
            if (Test-Path -LiteralPath (Join-Path $r.FullName 'gitdir')) { continue }
            try { if (HasLink $r.FullName) { throw 'it contains a link' }; RemoveTree $r.FullName; Say ('C2 removed stale worktree record ' + $r.Name) }
            catch { Say ('C2 could not remove stale worktree record ' + $r.Name + ' (harmless): ' + $_.Exception.Message) }
        }
    }
    if ($dropStash) {
        $base = Split-Path -Parent $rc
        $p1 = Join-Path $base 'pc-steward-stash-2026-11-11-tracked.patch'; $p2 = Join-Path $base 'pc-steward-stash-2026-11-11-untracked.patch'
        if ((Test-Path -LiteralPath $p1) -or (Test-Path -LiteralPath $p2)) { Say 'C2 stash KEPT: a patch file with that name already exists' }
        else {
            $null = Git @('diff', '--binary', ('--output=' + $p1), ($stashPin + '^1'), $stashPin); $e1 = $script:gitExit
            $e2 = 0; $null = Git @('rev-parse', '--verify', '-q', ($stashPin + '^3')); if ($script:gitExit -eq 0) { $null = Git @('show', '--binary', '--format=', ('--output=' + $p2), ($stashPin + '^3')); $e2 = $script:gitExit }
            $bytes = 0; foreach ($p in $p1, $p2) { if (Test-Path -LiteralPath $p -PathType Leaf) { $bytes += (Get-Item -LiteralPath $p).Length } }
            $now = @(Git @('rev-parse', '--verify', '-q', 'stash@{0}'))
            if ($e1 -ne 0 -or $e2 -ne 0 -or $bytes -eq 0) { Say ('C2 stash KEPT: saving it failed (git ' + $e1 + '/' + $e2 + ', ' + $bytes + ' bytes)') }
            elseif ($now.Count -ne 1 -or $now[0] -cne $stashPin) { Say 'C2 stash KEPT: it changed while this ran' }
            else { $null = Git @('stash', 'drop', '-q', 'stash@{0}'); Say ('C2 saved the stash (' + $bytes + ' bytes in 2 patch files next to the clone) and dropped it, git exit ' + $script:gitExit) }
        }
    }

    # --- C3: owner-only test certificates (CurrentUser stores) ---
    if (!$stateThere) {
        foreach ($c in $certs) {
            try {
                if ($c.Store -eq 'My') { Remove-Item -LiteralPath ('Cert:\CurrentUser\My\' + $c.Thumbprint) -DeleteKey }
                else {
                    $store = [Security.Cryptography.X509Certificates.X509Store]::new($c.Store, 'CurrentUser'); $store.Open('ReadWrite')
                    try { foreach ($x in @($store.Certificates.Find('FindByThumbprint', $c.Thumbprint, $false))) { if ($x.Subject -like 'CN=PC Steward Owner-Only Test *') { $store.Remove($x) } } } finally { $store.Close() }
                }
                Say ('C3 removed test certificate ' + $c.Thumbprint.Substring(0, 8) + '... from ' + $c.Store)
            }
            catch { Say ('C3 KEPT test certificate ' + $c.Thumbprint.Substring(0, 8) + '... in ' + $c.Store + ': ' + $_.Exception.Message) }
        }
    }

    # --- C4: one-off administrator folders (one UAC prompt, only if there are any) ---
    if ($admin.Count -eq 0) { Say 'C4 no administrator folders to remove; no UAC prompt' }
    else {
        $resultFile = Join-Path ([IO.Path]::GetTempPath()) ('PCSteward-Cleanup-' + [Guid]::NewGuid().ToString('N') + '.txt')
        $elevated = @'
$ErrorActionPreference = 'Stop'
$env:PSModulePath = [IO.Path]::Combine($PSHOME, 'Modules')
$env:PATH = [IO.Path]::Combine($env:SystemRoot, 'System32')
$log = [Collections.Generic.List[string]]::new()
try {
    $pf = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
    $pd = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
    $targets = @(@(Get-ChildItem -LiteralPath $pf -Directory -Force | Where-Object { $_.Name -cmatch '\APC Steward OwnerOnlyTest Execution [0-9a-f]{32}\z' }) + @(Get-ChildItem -LiteralPath $pd -Directory -Force | Where-Object { $_.Name -cmatch '\APCSteward-((YubicoTools|DotNetSdk)-[0-9a-f]{32}|G9T1)\z' }))
    foreach ($t in $targets) {
        try {
            $links = (($t.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) -or (@(Get-ChildItem -LiteralPath $t.FullName -Recurse -Force -Attributes ReparsePoint -ErrorAction Stop).Count -gt 0)
            if ($links) { $log.Add('C4 SKIPPED ' + $t.Name + ': it contains a link'); continue }
            Remove-Item -LiteralPath $t.FullName -Recurse -Force
            $log.Add('C4 removed ' + $t.Name)
        }
        catch { $log.Add('C4 FAILED ' + $t.Name + ': ' + $_.Exception.Message) }
    }
    $log.Add('C4 RESULT: DONE')
}
catch { $log.Add('C4 RESULT: FAIL ' + $_.Exception.Message) }
finally { [IO.File]::WriteAllLines('@@RESULT@@', $log.ToArray()) }
'@
        $elevated = $elevated.Replace('@@RESULT@@', $resultFile.Replace("'", "''"))
        $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($elevated))
        Say 'C4 approve the one UAC prompt (Windows PowerShell, Microsoft Windows).'
        $process = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -ArgumentList @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $encoded) -WorkingDirectory (Join-Path $env:SystemRoot 'System32') -Verb RunAs -WindowStyle Hidden -PassThru
        $null = $process.Handle
        if (!$process.WaitForExit(600000)) { throw 'the administrator step is still running after 10 minutes' }
        if (Test-Path -LiteralPath $resultFile) { Get-Content -LiteralPath $resultFile | ForEach-Object { Say $_ }; Remove-Item -LiteralPath $resultFile -Force }
        else { Say 'C4 the administrator step wrote no result (was the UAC prompt declined?)' }
    }

    # --- Check (read-only) ---
    $leftWork = @(Get-ChildItem -LiteralPath $up -Directory -Force | Where-Object { $_.Name -cmatch $workPattern }).Count
    $leftExtras = 0; foreach ($d in @(Get-ChildItem -LiteralPath $up -Directory -Force | Where-Object { $_.Name -cmatch $releasePattern -and $odd -notcontains $_.Name })) { $leftExtras += @(Get-ChildItem -LiteralPath $d.FullName -Force | Where-Object { $_.Name -ne 'release' }).Count }
    $keptReleases = @(Get-ChildItem -LiteralPath $up -Directory -Force | Where-Object { $_.Name -cmatch $releasePattern -and (Test-Path -LiteralPath (Join-Path $_.FullName 'release\release.json') -PathType Leaf) }).Count
    $trees = @(Git @('worktree', 'list', '--porcelain') | Where-Object { $_ -like 'worktree *' }).Count
    $stale = @(Git @('worktree', 'list', '--porcelain') | Where-Object { $_ -like 'prunable*' }).Count
    $leftStash = @(Git @('stash', 'list', '--format=%H') | Where-Object { $_ -ceq $stashPin }).Count
    $leftCerts = @(foreach ($s in 'TrustedPublisher', 'My', 'Root') { Get-ChildItem -LiteralPath ('Cert:\CurrentUser\' + $s) | Where-Object { $_.Subject -like 'CN=PC Steward Owner-Only Test *' } }).Count
    $leftAdmin = @(@(Get-ChildItem -LiteralPath $pf -Directory -Force | Where-Object { $_.Name -cmatch $adminPf }) + @(Get-ChildItem -LiteralPath $pd -Directory -Force | Where-Object { $_.Name -cmatch $adminPd })).Count
    $installed = Test-Path -LiteralPath (Join-Path $pf 'PC Steward\PCSteward.Desktop.exe') -PathType Leaf
    $data = Test-Path -LiteralPath (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'PCSteward') -PathType Container
    Say ('CHECK work=' + $leftWork + ' releaseExtras=' + $leftExtras + ' keptReleases=' + $keptReleases + ' worktrees=' + $trees + ' staleWorktrees=' + $stale + ' oldStash=' + $leftStash + ' testCerts=' + $leftCerts + ' adminFolders=' + $leftAdmin + ' skipped=' + ($linked.Count + $odd.Count) + ' stateRoot=' + (Test-Path -LiteralPath $stateRoot) + ' installed=' + $installed + ' data=' + $data)
    if ($leftWork + $leftExtras + $stale + $leftStash + $leftCerts + $leftAdmin + $linked.Count + $odd.Count -eq 0) { Say 'CLEANUP RESULT: DONE' } else { Say 'CLEANUP RESULT: SOMETHING WAS LEFT (see the lines above)' }
}
catch { Say ('STOP: ' + $_.Exception.Message) }
}
