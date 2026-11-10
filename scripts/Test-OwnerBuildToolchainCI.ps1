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
    $key = { param($d) (@($d.sdkZip.sha512, $d.sdkZip.url, $d.sourceCommit, $d.sdkVersion) + @($d.packages | ForEach-Object { $_.id.ToLowerInvariant() + '/' + $_.version.ToLowerInvariant() + '=' + $_.sha512 } | Sort-Object) + @($d.sdkSideBySideManifests | ForEach-Object { $m = $_; $_.band + '/' + $_.id + '/' + $_.version + '=' + $_.sha512 + ':' + (@($m.files.PSObject.Properties | ForEach-Object { $_.Name + '=' + $_.Value }) -join ',') } | Sort-Object)) -join "`n" }
    $same = (& $key $committed) -ceq (& $key $fresh)
    Write-Output "COMMITTED-PINS match=$same"
    if (!$same) { throw 'The committed toolchain pins differ from this CI run.' }
    $pinFile = $committedPath
} else { Write-Output 'COMMITTED-PINS absent (first run: the generated file above is the candidate pin file)' }

$zip = Join-Path $download (($generatedText | ConvertFrom-Json).sdkZip.name)
# The generator extracted the official zip into $reference and restored with it, so the clean check also shows that a build
# leaves the SDK folder unchanged.

# The runner's own variables (GitHub Actions, setup-dotnet, this script) are named so the window check allows exactly them.
$runnerWindowNames = (@(Get-ChildItem Env: | ForEach-Object { $_.Name } | Where-Object { $_ -notmatch '[,\s]' }) -join ',')
function Invoke-Check([string]$Label) {
    $lines = @(& $windowsPowerShell -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Test-OwnerBuildToolchain.ps1') -Worktree $worktree -PinFile $pinFile -WorkFolder (Join-Path $temp "toolchain-check-$Label") -DotnetRoot $reference -PackagesRoot $env:NUGET_PACKAGES -SdkZipPath $zip -AllowWindowEnvironmentName $runnerWindowNames 2>&1 | ForEach-Object { [string]$_ })
    $code = $LASTEXITCODE
    # Write-Host: the log lines must not become part of this function's return value.
    Write-Host "----- check: $Label (exit $code) -----"; $lines | ForEach-Object { Write-Host $_ }
    [pscustomobject]@{ Code = $code; Text = ($lines -join "`n") }
}

# Mirror the owner's dotnet folder: the older 10.0.111 workload manifests sit beside the zip's own, unpacked from the same
# signed packages the generator pinned.
$generatedPins = $generatedText | ConvertFrom-Json
foreach ($m in @($generatedPins.sdkSideBySideManifests)) {
    $target = Join-Path $reference ('sdk-manifests\' + $m.band + '\' + $m.id + '\' + $m.version)
    $package = [IO.Compression.ZipFile]::OpenRead((Join-Path $download ($m.package + '.' + $m.version + '.nupkg')))
    try { foreach ($entry in @($package.Entries | Where-Object { $_.Name -and $_.FullName.StartsWith('data/', [StringComparison]::Ordinal) })) { $file = Join-Path $target $entry.FullName.Substring(5).Replace('/', '\'); New-Item -ItemType Directory -Path (Split-Path -Parent $file) -Force | Out-Null; [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $file) } } finally { $package.Dispose() }
}
$sideBySideCount = @($generatedPins.sdkSideBySideManifests | ForEach-Object { @($_.files.PSObject.Properties).Count } | Measure-Object -Sum).Sum
Write-Output "SIDE-BY-SIDE manifests=$(@($generatedPins.sdkSideBySideManifests).Count) files=$sideBySideCount"

# A git.exe placed first on Path must be ignored: the check runs Git only from its signed Program Files install, and re-hashes
# what Git returns. hostname.exe stands in for a shim; if the check used it, the worktree step would fail.
$fakeGit = Join-Path $temp 'fake-git'; New-Item -ItemType Directory -Path $fakeGit -Force | Out-Null
Copy-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::System)) 'hostname.exe') -Destination (Join-Path $fakeGit 'git.exe')
$env:PATH = $fakeGit + [IO.Path]::PathSeparator + $env:PATH

$clean = Invoke-Check 'clean'
$cleanExpected = @('(?m)^GIT path=ProgramFiles\\Git\\cmd\\git\.exe binaries=[1-9][0-9]* signers=.+ problems=0$', '(?m)^WORKTREE head=[0-9a-f]{40} commitVerified=True treesVerified=True ', "(?m)^SDK-FILES .* extra=0 .* sideBySideManifestFiles=$sideBySideCount workloadStateFiles=0$", '(?m)^BUILD-HOOKS .* buildEnvironmentInThisWindow=0 unrecognizedWindowEnvironment=0 volatileBuildEnvironment=0$', '(?m)^CB3\.7c-OK$')
$cleanMissed = @($cleanExpected | Where-Object { $clean.Text -notmatch $_ })
if ($clean.Code -ne 0 -or $cleanMissed.Count -gt 0) { throw ('The toolchain check did not pass on the untouched official toolchain: ' + ($cleanMissed -join ' | ')) }

# Plant one change of each kind.
$sdkFolder = Join-Path $reference ('sdk\' + ($generatedText | ConvertFrom-Json).sdkVersion)
$sdkTarget = Get-ChildItem -LiteralPath $sdkFolder -Filter '*.targets' -Recurse -File | Select-Object -First 1
[IO.File]::AppendAllText($sdkTarget.FullName, "<!-- planted -->`n")
[IO.File]::WriteAllText((Join-Path $sdkFolder 'planted.props'), '<Project />')
$olderManifest = @(Get-ChildItem -LiteralPath (Join-Path $reference 'sdk-manifests') -Recurse -Filter 'WorkloadManifest.json' -File | Where-Object { $_.Directory.Name -eq '10.0.111' } | Select-Object -First 1)[0]
[IO.File]::AppendAllText($olderManifest.FullName, ' ')
New-Item -ItemType Directory -Path (Join-Path $reference 'metadata\workloads\10.0.100\InstallState') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $reference 'metadata\workloads\10.0.100\InstallState\default.json'), '{}')
$ilcompiler = Get-ChildItem -LiteralPath (Join-Path $env:NUGET_PACKAGES 'microsoft.dotnet.ilcompiler') -Directory | Select-Object -First 1
$packageTarget = Get-ChildItem -LiteralPath $ilcompiler.FullName -Filter '*.targets' -Recurse -File | Select-Object -First 1
[IO.File]::AppendAllText($packageTarget.FullName, "<!-- planted -->`n")
New-Item -ItemType Directory -Path (Join-Path $worktree 'src\PCSteward.ElevatedHelper\tmp') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $worktree 'src\PCSteward.ElevatedHelper\tmp\Planted.cs'), '// planted')
[IO.File]::WriteAllText((Join-Path $worktree 'src\PCSteward.ElevatedHelper\obj\PCSteward.ElevatedHelper.csproj.planted.props'), '<Project />')
[IO.File]::AppendAllText((Join-Path $worktree 'src\PCSteward.ElevatedHelper\Program.cs'), "`n// planted`n")
# The helper's generated targets file can be an empty <Project ... />, so the target is added as an XML element.
$generatedTargets = Join-Path $worktree 'src\PCSteward.ElevatedHelper\obj\PCSteward.ElevatedHelper.csproj.nuget.g.targets'
$targetsXml = New-Object Xml.XmlDocument; $targetsXml.PreserveWhitespace = $true; $targetsXml.Load($generatedTargets)
$plantedTarget = $targetsXml.CreateElement('Target', $targetsXml.DocumentElement.NamespaceURI); $plantedTarget.SetAttribute('Name', 'Planted'); $plantedTarget.SetAttribute('BeforeTargets', 'CoreCompile')
[void]$targetsXml.DocumentElement.AppendChild($plantedTarget); $targetsXml.Save($generatedTargets)
if ([IO.File]::ReadAllText($generatedTargets) -notmatch '<Target Name="Planted"') { throw 'The target was not planted.' }
# An import that keeps a used package's prefix but hides a property function behind an XML character reference (&#40; is '(').
$encodedImport = '<ImportGroup><Import Project="$(NuGetPackageRoot)microsoft.dotnet.ilcompiler/' + $ilcompiler.Name + '/$&#40;[System.String]::Copy(''x''))/x.targets" /></ImportGroup></Project>'
[IO.File]::WriteAllText($generatedTargets, [IO.File]::ReadAllText($generatedTargets).Replace('</Project>', $encodedImport))
if ([IO.File]::ReadAllText($generatedTargets) -notmatch '&#40;') { throw 'The encoded import was not planted.' }
# A Pkg<Id> path pointing at another folder under the package root, and an MSBuild variable in the window.
$desktopProps = Join-Path $worktree 'src\PCSteward.Desktop\obj\PCSteward.Desktop.csproj.nuget.g.props'
$propsXml = New-Object Xml.XmlDocument; $propsXml.PreserveWhitespace = $true; $propsXml.Load($desktopProps)
$pkgProperty = @($propsXml.GetElementsByTagName('*') | Where-Object { $_.LocalName -like 'Pkg*' } | Select-Object -First 1)[0]
if (!$pkgProperty) { throw 'The desktop has no Pkg property to plant.' }
$pkgProperty.InnerText = Join-Path $env:NUGET_PACKAGES 'planted.package\1.0.0'; $propsXml.Save($desktopProps)
$env:MSBuildSDKsPath = Join-Path $temp 'planted-sdks'
# An import-control property with no risky prefix in the window and in the volatile logon environment, pointing at a targets
# file in a subfolder of obj (Codex review of PR #32). The runner is disposable, so writing HKCU here is harmless.
$env:CustomAfterMicrosoftCommonTargets = Join-Path $worktree 'src\PCSteward.ElevatedHelper\obj\hooks\planted.targets'
New-Item -ItemType Directory -Path (Join-Path $worktree 'src\PCSteward.ElevatedHelper\obj\hooks') -Force | Out-Null
[IO.File]::WriteAllText($env:CustomAfterMicrosoftCommonTargets, '<Project />')
New-ItemProperty -Path 'HKCU:\Volatile Environment' -Name 'CustomBeforeMicrosoftCommonTargets' -Value $env:CustomAfterMicrosoftCommonTargets -PropertyType String -Force | Out-Null

$tampered = Invoke-Check 'tampered'
$expected = @(('(?m)^SDK-FILES .* changed=2 extra=1 .* sideBySideManifestFiles=' + ($sideBySideCount - 1) + ' workloadStateFiles=1$'), '(?m)^  SDK-CHANGED .*\\10\.0\.111\\WorkloadManifest\.json$', '(?m)^  SDK-WORKLOAD-STATE metadata\\workloads\\', '(?m)^PACKAGES .* changed=1 ', '(?m)^WORKTREE .* mismatched=1 ', '(?m)^BUILD-HOOKS .* untrackedOutsideBinObj=1 nugetGeneratedFiles=\d+ objImports=6 .* buildEnvironmentInThisWindow=2 unrecognizedWindowEnvironment=2 volatileBuildEnvironment=1$', '(?m)^  HOOK-WINDOW-ENVIRONMENT-NAME MSBuildSDKsPath$', '(?m)^  HOOK-WINDOW-UNRECOGNIZED-NAME CustomAfterMicrosoftCommonTargets$', '(?m)^  HOOK-VOLATILE-ENVIRONMENT-NAME CustomBeforeMicrosoftCommonTargets$', '(?m)^  HOOK-OBJ .*\\obj\\hooks\\planted\.targets$', '(?m)^  HOOK-OBJ .*Desktop\.csproj\.nuget\.g\.props: Pkg\w+ value is not its package folder$', '(?m)^  HOOK-OBJ .*nuget\.g\.targets: element Target', '(?m)^  HOOK-OBJ .*nuget\.g\.targets: expression in Import attribute Project$', '(?m)^STOP: the \.NET SDK differs', '(?m)^STOP: a restored package differs', '(?m)^STOP: worktree files differ', '(?m)^STOP: a build hook')
$missed = @($expected | Where-Object { $tampered.Text -notmatch $_ })
if ($tampered.Code -eq 0 -or $tampered.Text -match '(?m)^CB3\.7c-OK$' -or $missed.Count -gt 0) { throw ('The toolchain check missed planted changes: ' + ($missed -join ' | ')) }
Write-Output 'PASS: CB3.7c passes on the official toolchain (ignoring a git.exe shim first on Path) and stops on each planted change (SDK file, extra SDK file, older side-by-side manifest file, workload install state, package file, source file, ignored source file, obj import, target added to a generated NuGet file, entity-encoded property function in a generated import, Pkg path to another folder, MSBuild variable in the window, import property in the window and the volatile environment, targets file in an obj subfolder).'
