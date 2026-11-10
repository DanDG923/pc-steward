[CmdletBinding()]
param([string]$SourceCommit = 'd9a754b8da980a84eb4641cd58c98e27bdfd4444')

# Task 50 CB3.7c Windows CI evidence (informational). On a disposable GitHub-hosted runner: make a clean worktree of the
# owner's build commit, produce the toolchain pins (official SDK zip, restored NuGet packages), compare them with the pins
# committed for the owner, then run scripts/Test-OwnerBuildToolchain.ps1 under Windows PowerShell 5.1 twice: on an untouched
# copy of the official SDK and the restored packages (must print CB3.7c-OK), and after planting one change of each kind it
# looks for (must stop and name each one). No signing, certificate store, install or launch.
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') { throw 'This plants test changes and runs only on a GitHub-hosted runner.' }
$repository = Split-Path -Parent $PSScriptRoot
$windowsPowerShell = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::System)) 'WindowsPowerShell\v1.0\powershell.exe'
$temp = $env:RUNNER_TEMP
$worktree = Join-Path $temp 'toolchain-worktree'
$out = Join-Path $temp 'toolchain-pins'
$download = Join-Path $temp 'toolchain-download'
$reference = Join-Path $temp 'dotnet-reference'
$env:NUGET_PACKAGES = Join-Path $temp 'toolchain-nuget'
New-Item -ItemType Directory -Path $env:NUGET_PACKAGES, $out -Force | Out-Null

& git -C $repository worktree add --detach $worktree $SourceCommit | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'git worktree add failed.' }
$generated = Join-Path $out 'owner-build-toolchain-pins.json'
& (Join-Path $PSScriptRoot 'Get-OwnerBuildToolchainPins.ps1') -Worktree $worktree -SourceCommit $SourceCommit -OutputPath $generated -DownloadFolder $download -SdkRoot $reference
$generatedText = [IO.File]::ReadAllText($generated)
Write-Output '----- generated pins -----'; Write-Output $generatedText
$sha = [Security.Cryptography.SHA256]::Create()
try { Write-Output ('GENERATED-PINS-SHA256 ' + [BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($generated))).Replace('-', '')) } finally { $sha.Dispose() }

# The pins the owner runs with must say the same thing as a fresh CI run.
$committedPath = Join-Path $repository 'docs/review/task50-owner-build-toolchain-pins.json'
$pinFile = $generated
if (Test-Path -LiteralPath $committedPath -PathType Leaf) {
    $committed = [IO.File]::ReadAllText($committedPath) | ConvertFrom-Json; $fresh = $generatedText | ConvertFrom-Json
    $key = { param($d) (@($d.sdkZip.sha512, $d.sdkZip.url, $d.sourceCommit, $d.sdkVersion) + @($d.packages | ForEach-Object { $_.id.ToLowerInvariant() + '/' + $_.version.ToLowerInvariant() + '=' + $_.sha512 } | Sort-Object)) -join "`n" }
    $same = (& $key $committed) -ceq (& $key $fresh)
    Write-Output "COMMITTED-PINS match=$same"
    if (!$same) { throw 'The committed toolchain pins differ from this CI run.' }
    $pinFile = $committedPath
} else { Write-Output 'COMMITTED-PINS absent (first run: the generated file above is the candidate pin file)' }

$zip = Join-Path $download (($generatedText | ConvertFrom-Json).sdkZip.name)
# The generator extracted the official zip into $reference and restored with it, so the clean check also shows that a build
# leaves the SDK folder unchanged.

function Invoke-Check([string]$Label) {
    $lines = @(& $windowsPowerShell -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Test-OwnerBuildToolchain.ps1') -Worktree $worktree -PinFile $pinFile -WorkFolder (Join-Path $temp "toolchain-check-$Label") -DotnetRoot $reference -PackagesRoot $env:NUGET_PACKAGES -SdkZipPath $zip 2>&1 | ForEach-Object { [string]$_ })
    $code = $LASTEXITCODE
    # Write-Host: the log lines must not become part of this function's return value.
    Write-Host "----- check: $Label (exit $code) -----"; $lines | ForEach-Object { Write-Host $_ }
    [pscustomobject]@{ Code = $code; Text = ($lines -join "`n") }
}

# A git.exe placed first on Path must be ignored: the check runs Git only from its signed Program Files install, and re-hashes
# what Git returns. hostname.exe stands in for a shim; if the check used it, the worktree step would fail.
$fakeGit = Join-Path $temp 'fake-git'; New-Item -ItemType Directory -Path $fakeGit -Force | Out-Null
Copy-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::System)) 'hostname.exe') -Destination (Join-Path $fakeGit 'git.exe')
$env:PATH = $fakeGit + [IO.Path]::PathSeparator + $env:PATH

$clean = Invoke-Check 'clean'
$cleanExpected = @('(?m)^GIT path=ProgramFiles\\Git\\cmd\\git\.exe signers=.+ problems=0$', '(?m)^WORKTREE head=[0-9a-f]{40} commitVerified=True treesVerified=True ', '(?m)^CB3\.7c-OK$')
$cleanMissed = @($cleanExpected | Where-Object { $clean.Text -notmatch $_ })
if ($clean.Code -ne 0 -or $cleanMissed.Count -gt 0) { throw ('The toolchain check did not pass on the untouched official toolchain: ' + ($cleanMissed -join ' | ')) }

# Plant one change of each kind.
$sdkFolder = Join-Path $reference ('sdk\' + ($generatedText | ConvertFrom-Json).sdkVersion)
$sdkTarget = Get-ChildItem -LiteralPath $sdkFolder -Filter '*.targets' -Recurse -File | Select-Object -First 1
[IO.File]::AppendAllText($sdkTarget.FullName, "<!-- planted -->`n")
[IO.File]::WriteAllText((Join-Path $sdkFolder 'planted.props'), '<Project />')
$ilcompiler = Get-ChildItem -LiteralPath (Join-Path $env:NUGET_PACKAGES 'microsoft.dotnet.ilcompiler') -Directory | Select-Object -First 1
$packageTarget = Get-ChildItem -LiteralPath $ilcompiler.FullName -Filter '*.targets' -Recurse -File | Select-Object -First 1
[IO.File]::AppendAllText($packageTarget.FullName, "<!-- planted -->`n")
New-Item -ItemType Directory -Path (Join-Path $worktree 'src\PCSteward.ElevatedHelper\tmp') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $worktree 'src\PCSteward.ElevatedHelper\tmp\Planted.cs'), '// planted')
[IO.File]::WriteAllText((Join-Path $worktree 'src\PCSteward.ElevatedHelper\obj\PCSteward.ElevatedHelper.csproj.planted.props'), '<Project />')
[IO.File]::AppendAllText((Join-Path $worktree 'src\PCSteward.ElevatedHelper\Program.cs'), "`n// planted`n")
$generatedTargets = Join-Path $worktree 'src\PCSteward.ElevatedHelper\obj\PCSteward.ElevatedHelper.csproj.nuget.g.targets'
[IO.File]::WriteAllText($generatedTargets, [IO.File]::ReadAllText($generatedTargets).Replace('</Project>', '<Target Name="Planted" BeforeTargets="CoreCompile" /></Project>'))

$tampered = Invoke-Check 'tampered'
$expected = @('(?m)^SDK-FILES .* changed=1 extra=1 ', '(?m)^PACKAGES .* changed=1 ', '(?m)^WORKTREE .* mismatched=1 ', '(?m)^BUILD-HOOKS .* untrackedOutsideBinObj=1 nugetGeneratedFiles=\d+ objImports=2 ', '(?m)^  HOOK-OBJ .*nuget\.g\.targets: element Target', '(?m)^STOP: the \.NET SDK differs', '(?m)^STOP: a restored package differs', '(?m)^STOP: worktree files differ', '(?m)^STOP: a build hook')
$missed = @($expected | Where-Object { $tampered.Text -notmatch $_ })
if ($tampered.Code -eq 0 -or $tampered.Text -match '(?m)^CB3\.7c-OK$' -or $missed.Count -gt 0) { throw ('The toolchain check missed planted changes: ' + ($missed -join ' | ')) }
Write-Output 'PASS: CB3.7c passes on the official toolchain (ignoring a git.exe shim first on Path) and stops on each planted change (SDK file, extra SDK file, package file, source file, ignored source file, obj import, target added to a generated NuGet file).'
