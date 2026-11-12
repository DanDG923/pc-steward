# CI test for the owner's Task 50 cleanup paste (docs/review/TASK_50_CLEANUP_PASTE.ps1; record TASK_50_CLEANUP.md).
# It parses the paste under Windows PowerShell 5.1, builds the leftovers it is meant to remove (and look-alikes it must
# keep) in this disposable runner's real profile, clone location, CurrentUser certificate stores, Program Files and
# ProgramData, then runs the paste three times: answering "no" (nothing may change), "YES" (everything planned goes, the
# skipped and kept items stay) and "YES" again once the skipped items are gone (DONE, no UAC step).
# Three text changes are made to a temporary copy only, each of which must match exactly once: the hosted runner is always
# elevated, so the elevated-window refusal is removed; Read-Host is answered from an environment variable; and the pinned
# stash commit is replaced by the stash made here. The CurrentUser Root store is not exercised: removing from it shows a
# Windows confirmation box that a hosted runner cannot answer.
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') { throw 'This test changes the user profile, Program Files, ProgramData and certificate stores; it runs only on a GitHub-hosted runner.' }
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Run this test in Windows PowerShell 5.1 (powershell.exe).' }

$root = Split-Path -Parent $PSScriptRoot
$pastePath = Join-Path $root 'docs\review\TASK_50_CLEANUP_PASTE.ps1'
$failures = [Collections.Generic.List[string]]::new()
function Check([bool]$condition, [string]$what) { if ($condition) { Write-Host ('ok   ' + $what) } else { Write-Host ('FAIL ' + $what); $failures.Add($what) } }

# --- 1. The paste parses under Windows PowerShell 5.1 and is plain ASCII ---
$tokens = $null; $errors = $null
[void][Management.Automation.Language.Parser]::ParseFile($pastePath, [ref]$tokens, [ref]$errors)
$errors | ForEach-Object { Write-Host ('PARSE-ERROR line ' + $_.Extent.StartLineNumber + ': ' + $_.Message) }
if (@($errors).Count) { throw ('the paste does not parse: ' + @($errors).Count + ' error(s)') }
$bytes = [IO.File]::ReadAllBytes($pastePath)
if (@($bytes | Where-Object { $_ -gt 127 }).Count) { throw 'the paste must be plain ASCII so that it pastes unchanged into Windows PowerShell' }

# --- 2. Leftovers and look-alikes ---
$up = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
$docs = [Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)
$pf = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$pd = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
$g = Join-Path $pf 'Git\cmd\git.exe'
$rc = Join-Path $docs 'Codex\PC Steward\pc-steward'
function G([string[]]$a) {
    $ErrorActionPreference = 'Continue'
    $o = @($null | & $g -C $rc @a 2>&1 | ForEach-Object { [string]$_ })
    if ($LASTEXITCODE -ne 0) { throw ('git ' + ($a -join ' ') + ' failed: ' + ($o -join ' | ')) }
    $o
}
function Put([string]$path, [string]$text) { $null = New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force; [IO.File]::WriteAllText($path, $text) }

foreach ($p in @($rc)) { if (Test-Path -LiteralPath $p) { throw ('fixture path already exists: ' + $p) } }
$null = New-Item -ItemType Directory -Path $rc -Force
$ErrorActionPreference = 'Continue'; $null = & $g init -q $rc 2>&1; $ErrorActionPreference = 'Stop'
$null = G @('config', 'user.email', 'ci@example.invalid'); $null = G @('config', 'user.name', 'CI')
Put (Join-Path $rc 'PCSteward.slnx') '<Solution />'
Put (Join-Path $rc 'tracked.txt') 'base'
$null = G @('add', '-A'); $null = G @('commit', '-q', '-m', 'base')

# Work folders the paste removes (two of them are Git worktrees, as on the owner's PC).
$null = New-Item -ItemType Directory -Path (Join-Path $up 'PCSteward-Task50D'), (Join-Path $up 'PCSteward-Task50E'), (Join-Path $up 'PCSteward-Release-20261112-095947') -Force
$null = G @('worktree', 'add', '-q', '--detach', (Join-Path $up 'PCSteward-Task50D\5606da2'))
$null = G @('worktree', 'add', '-q', '--detach', (Join-Path $up 'PCSteward-Task50E\d9a754b'))
Put (Join-Path $up 'PCSteward-Task50D\candidate\payload.bin') ('x' * 4096)
Put (Join-Path $up 'PCSteward-Task50D\dbcopy\pcsteward.db') 'copy'
Put (Join-Path $up 'PCSteward-Task50B\plan-r2\fresh-install-plan.json') '{}'
(Get-Item -LiteralPath (Join-Path $up 'PCSteward-Task50B\plan-r2\fresh-install-plan.json')).Attributes = 'ReadOnly'
Put (Join-Path $up 'PCSteward-UI5\57d8f47\a.txt') 'a'
Put (Join-Path $up 'PCSteward-UI6-20261111-135747\candidate\a.txt') 'a'
# A work folder that holds a junction: skipped, and the junction's target is untouched.
Put (Join-Path $up 'cleanup-ci-sentinel\sentinel.txt') 'keep'
Put (Join-Path $up 'PCSteward-Task50C\ebb4061\a.txt') 'a'
$null = New-Item -ItemType Junction -Path (Join-Path $up 'PCSteward-Task50C\ebb4061\link') -Target (Join-Path $up 'cleanup-ci-sentinel')
# Look-alikes that must stay.
Put (Join-Path $up 'PCSteward-Task50F\keep.txt') 'keep'
Put (Join-Path $up 'PCSteward-UI8-20261111-000000\keep.txt') 'keep'
Put (Join-Path $up 'PCSteward-Release-Notes\keep.txt') 'keep'
# Release folders: one to tidy (worktree beside a signed release), one already tidy, one whose release folder is incomplete.
$null = G @('worktree', 'add', '-q', '--detach', (Join-Path $up 'PCSteward-Release-20261112-095947\9322ca1'))
Put (Join-Path $up 'PCSteward-Release-20261112-095947\release\release.json') '{}'
Put (Join-Path $up 'PCSteward-Release-20261112-095947\release\PCSteward-0.1.2-private-beta.msi') 'msi'
Put (Join-Path $up 'PCSteward-Release-20261111-194622\release\release.json') '{}'
Put (Join-Path $up 'PCSteward-Release-20261111-194622\release\PCSteward-0.1.0-private-beta.msi') 'msi'
Put (Join-Path $up 'PCSteward-Release-20261112-084704\f6ca90f\a.txt') 'a'
Put (Join-Path $up 'PCSteward-Release-20261112-084704\release\PCSteward-0.1.1-private-beta.msi') 'msi'
# A stale worktree record with no gitdir file, like the ones Git could not delete on the owner's PC.
Put (Join-Path $rc '.git\worktrees\41df129\ORIG_HEAD') ('0' * 40)
$null = New-Item -ItemType Directory -Path (Join-Path $rc '.git\worktrees\41df129\logs'), (Join-Path $rc '.git\worktrees\41df129\refs') -Force
# The one old stash, with a tracked change and an untracked file.
Put (Join-Path $rc 'tracked.txt') 'changed'
Put (Join-Path $rc 'untracked-note.md') 'note'
$null = G @('stash', 'push', '-u', '-q', '-m', 'pre-sync local Task 50 edits 2026-11-11')
$pin = @(G @('rev-parse', '--verify', 'refs/stash') | Where-Object { $_ -match '\A[0-9a-f]{40}\z' })
$listed = @(G @('stash', 'list', '--format=%H'))
Write-Host ('stash commit ' + ($pin -join ',') + '; stash list --format=%H gives ' + ($listed -join ','))
if ($pin.Count -ne 1) { throw 'could not read the stash commit' }; $pin = $pin[0]
# Certificates: an owner-only test signer in My (with its key) and TrustedPublisher, and a release-signing look-alike.
$test = New-SelfSignedCertificate -Subject ('CN=PC Steward Owner-Only Test ' + [Guid]::NewGuid()) -CertStoreLocation 'Cert:\CurrentUser\My' -Type CodeSigningCert -KeyExportPolicy NonExportable
$tp = [Security.Cryptography.X509Certificates.X509Store]::new('TrustedPublisher', 'CurrentUser'); $tp.Open('ReadWrite'); $tp.Add([Security.Cryptography.X509Certificates.X509Certificate2]::new($test.RawData)); $tp.Close()
$keep = New-SelfSignedCertificate -Subject 'CN=PC Steward Release Signing (Backup)' -CertStoreLocation 'Cert:\CurrentUser\My' -Type CodeSigningCert
# Administrator folders, and look-alikes that must stay.
$exec = Join-Path $pf ('PC Steward OwnerOnlyTest Execution ' + [Guid]::NewGuid().ToString('N'))
Put (Join-Path $exec 'bootstrap.ps1') 'x'
Put (Join-Path $pf 'PC Steward OwnerOnlyTest Execution keep\keep.txt') 'keep'
Put (Join-Path $pd 'PCSteward-G9T1\ripgrep.14.1.0.nupkg') 'x'
Put (Join-Path $pd ('PCSteward-DotNetSdk-' + [Guid]::NewGuid().ToString('N') + '\sdk.exe')) 'x'
Put (Join-Path $pd 'PCSteward-Other\keep.txt') 'keep'

# --- 3. A copy of the paste for this runner ---
$text = [IO.File]::ReadAllText($pastePath)
function Swap([string]$t, [string]$old, [string]$new) { $n = ([regex]::Matches($t, [regex]::Escape($old))).Count; if ($n -ne 1) { throw ('expected exactly one "' + $old + '" in the paste, found ' + $n) }; $t.Replace($old, $new) }
$text = Swap $text "if (([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'this window is elevated; open a normal (not administrator) Windows PowerShell window' }" '# CI: the hosted runner is always elevated.'
$text = Swap $text "`$answer = Read-Host 'Type YES to go ahead (anything else stops with nothing changed)'" "`$answer = `$env:PCSTEWARD_CLEANUP_ANSWER"
$text = Swap $text "'ac8ed6004c99e5efe7390fba58e1680be88afebf'" ("'" + $pin + "'")
$copy = Join-Path $env:RUNNER_TEMP 'task50-cleanup-ci.ps1'
[IO.File]::WriteAllText($copy, $text, [Text.UTF8Encoding]::new($true))
function RunPaste([string]$answer) {
    $env:PCSTEWARD_CLEANUP_ANSWER = $answer
    $ErrorActionPreference = 'Continue'
    $o = @(& (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -NoProfile -ExecutionPolicy Bypass -File $copy 2>&1 | ForEach-Object { [string]$_ })
    Write-Host ('--- paste output (answer ' + $answer + ') ---'); $o | ForEach-Object { Write-Host ('  | ' + $_) }
    $o
}
function Has([string[]]$o, [string]$line) { @($o | Where-Object { $_ -ceq $line }).Count -eq 1 }
function Starts([string[]]$o, [string]$prefix) { @($o | Where-Object { $_.StartsWith($prefix, [StringComparison]::Ordinal) }).Count }
function There([string]$p) { Test-Path -LiteralPath $p }

# --- 4. Answer "no": nothing changes ---
$o = RunPaste 'no'
Check (Has $o 'STOPPED: nothing changed.') 'run 1 stops on any answer but YES'
Check (Has $o 'PLAN (nothing has changed yet):') 'run 1 prints the plan first'
Check ((Starts $o '  remove PCSteward-Task50D (') -eq 1) 'run 1 plans the Task 50 work folder'
Check (Has $o '  SKIP PCSteward-Task50C: it contains a link') 'run 1 plans to skip the folder with a junction'
Check ((There (Join-Path $up 'PCSteward-Task50D')) -and (There $exec) -and (There (Join-Path $pd 'PCSteward-G9T1')) -and (There ('Cert:\CurrentUser\My\' + $test.Thumbprint))) 'run 1 changed nothing'
Check (@(G @('stash', 'list')).Count -eq 1) 'run 1 kept the stash'

# --- 5. Answer YES ---
$o = RunPaste 'YES'
foreach ($n in 'PCSteward-Task50B', 'PCSteward-Task50D', 'PCSteward-Task50E', 'PCSteward-UI5', 'PCSteward-UI6-20261111-135747') {
    Check (Has $o ('C1 removed ' + $n)) ('run 2 reports removing ' + $n)
    Check (!(There (Join-Path $up $n))) ('run 2 removed ' + $n)
}
Check (Has $o 'C1 tidied PCSteward-Release-20261112-095947, kept release\PCSteward-0.1.2-private-beta.msi') 'run 2 tidies the release folder'
Check (!(There (Join-Path $up 'PCSteward-Release-20261112-095947\9322ca1')) -and (There (Join-Path $up 'PCSteward-Release-20261112-095947\release\PCSteward-0.1.2-private-beta.msi'))) 'run 2 kept only the signed release'
Check ((There (Join-Path $up 'PCSteward-Release-20261111-194622\release\PCSteward-0.1.0-private-beta.msi')) -and (There (Join-Path $up 'PCSteward-Release-20261112-084704\f6ca90f\a.txt'))) 'run 2 left the tidy and the incomplete release folders alone'
Check (Has $o '  SKIP PCSteward-Release-20261112-084704: its release folder is not one signed release; left as it is') 'run 2 reports the incomplete release folder'
Check ((There (Join-Path $up 'PCSteward-Task50C\ebb4061\a.txt')) -and (There (Join-Path $up 'cleanup-ci-sentinel\sentinel.txt'))) 'run 2 skipped the folder with a junction and its target'
Check ((There (Join-Path $up 'PCSteward-Task50F\keep.txt')) -and (There (Join-Path $up 'PCSteward-UI8-20261111-000000\keep.txt')) -and (There (Join-Path $up 'PCSteward-Release-Notes\keep.txt'))) 'run 2 kept the look-alike folders'
Check (!(There (Join-Path $rc '.git\worktrees\41df129'))) 'run 2 cleared the stale worktree record (Git prunes it here; the paste deletes it when Git cannot)'
Check (@(G @('worktree', 'list', '--porcelain') | Where-Object { $_ -like 'worktree *' }).Count -eq 1) 'run 2 left only the clone in the worktree list'
Check ((Starts $o 'C2 saved the stash (') -eq 1) 'run 2 saved and dropped the stash'
Check (@(G @('stash', 'list')).Count -eq 0) 'run 2 dropped the stash'
$tracked = Join-Path (Split-Path -Parent $rc) 'pc-steward-stash-2026-11-11-tracked.patch'
$untracked = Join-Path (Split-Path -Parent $rc) 'pc-steward-stash-2026-11-11-untracked.patch'
Check ((There $tracked) -and ([IO.File]::ReadAllText($tracked).Contains('+changed'))) 'the tracked patch holds the stashed change'
Check ((There $untracked) -and ([IO.File]::ReadAllText($untracked).Contains('untracked-note.md'))) 'the untracked patch holds the stashed new file'
Check ((Starts $o ('C3 removed test certificate ' + $test.Thumbprint.Substring(0, 8))) -eq 2) 'run 2 removed the test certificate from My and TrustedPublisher'
Check (!(There ('Cert:\CurrentUser\My\' + $test.Thumbprint)) -and !(There ('Cert:\CurrentUser\TrustedPublisher\' + $test.Thumbprint))) 'the test certificate is gone'
Check (There ('Cert:\CurrentUser\My\' + $keep.Thumbprint)) 'the release-signing look-alike certificate stayed'
Check ((Starts $o 'C4 removed PC Steward OwnerOnlyTest Execution ') -eq 1 -and (Has $o 'C4 removed PCSteward-G9T1') -and (Starts $o 'C4 removed PCSteward-DotNetSdk-') -eq 1 -and (Has $o 'C4 RESULT: DONE')) 'run 2 removed the administrator folders'
Check (!(There $exec) -and !(There (Join-Path $pd 'PCSteward-G9T1'))) 'the administrator folders are gone'
Check ((There (Join-Path $pf 'PC Steward OwnerOnlyTest Execution keep\keep.txt')) -and (There (Join-Path $pd 'PCSteward-Other\keep.txt'))) 'the administrator look-alikes stayed'
Check (Has $o 'CHECK work=1 releaseExtras=0 keptReleases=2 worktrees=1 staleWorktrees=0 oldStash=0 testCerts=0 adminFolders=0 skipped=2 stateRoot=False installed=False data=False') 'run 2 check line'
Check (Has $o 'CLEANUP RESULT: SOMETHING WAS LEFT (see the lines above)') 'run 2 reports what it left'
Check (@($o | Where-Object { $_.Contains($up) -or $_.Contains($env:USERNAME) }).Count -eq 0) 'run 2 output has no profile path or user name'

# --- 6. Once the skipped items are gone: DONE, and no UAC step ---
cmd.exe /c rmdir (Join-Path $up 'PCSteward-Task50C\ebb4061\link')
Remove-Item -LiteralPath (Join-Path $up 'PCSteward-Release-20261112-084704') -Recurse -Force
$o = RunPaste 'YES'
Check (Has $o 'C1 removed PCSteward-Task50C') 'run 3 removed the folder once its junction was gone'
Check (There (Join-Path $up 'cleanup-ci-sentinel\sentinel.txt')) 'the former junction target is still intact'
Check (Has $o 'C4 no administrator folders to remove; no UAC prompt') 'run 3 needs no administrator step'
Check (Has $o 'CHECK work=0 releaseExtras=0 keptReleases=2 worktrees=1 staleWorktrees=0 oldStash=0 testCerts=0 adminFolders=0 skipped=0 stateRoot=False installed=False data=False') 'run 3 check line'
Check (Has $o 'CLEANUP RESULT: DONE') 'run 3 is DONE'

Remove-Item -LiteralPath ('Cert:\CurrentUser\My\' + $keep.Thumbprint) -DeleteKey -ErrorAction SilentlyContinue
if ($failures.Count) { throw ($failures.Count.ToString() + ' check(s) failed: ' + ($failures -join '; ')) }
Write-Host 'TASK50-CLEANUP-PASTE-CI PASS'
