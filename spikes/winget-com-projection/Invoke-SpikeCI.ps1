[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('probe', 'surface', 'solution')][string]$Stage,
    # solution stage only: 'baseline' builds the repository unchanged; a version retargets a scratch copy to it.
    [string]$Variant = 'baseline'
)

# Task 54A throwaway spike driver. Runs only on disposable GitHub-hosted Windows runners (it refuses anywhere else).
# Writes evidence into artifacts\winget-com-spike under the checkout. It never touches an owner machine, certificate store,
# release key, installed PC Steward or real application. The 'solution' stage edits this runner's own checkout (a scratch
# copy that is thrown away with the runner); nothing it changes is committed.
#   probe    builds spikes\winget-com-projection\probe against several WinGet COM versions and target frameworks, then runs
#            the in-process and out-of-process activation routes, elevated and as a disposable standard user.
#   surface  downloads the WinGet COM packages from nuget.org, lists each package's files and target frameworks, the
#            native runtime's DLL imports, and the public .winmd API surface, and diffs every version against 1.10.340.
#   solution retargets every Windows project to net10.0-windows10.0.26100.0 with an explicit 10.0.22000.0 minimum and
#            moves both WinGet COM pins to -Variant, then runs the canonical restore, build and CI tests, the Native AOT
#            helper check, the private-beta package and its evidence check, and the desktop launch smoke.
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'This spike runs only on a GitHub-hosted runner.'
}

$repository = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$spike = $PSScriptRoot
$evidence = Join-Path $repository "artifacts\winget-com-spike\$Stage-$Variant"
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$summary = [ordered]@{ stage = $Stage; variant = $Variant; runnerOs = [Environment]::OSVersion.Version.ToString(); steps = [ordered]@{} }
$baselineVersion = '1.10.340'
$surfaceVersions = @('1.10.340', '1.11.560', '1.12.440', '1.28.240', '1.29.380')

function Save-Summary {
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'summary.json') -Encoding utf8
}

function Invoke-Logged([string]$Name, [scriptblock]$Body) {
    $log = Join-Path $evidence "$Name.log"
    $global:LASTEXITCODE = 0
    $code = 0
    try {
        & $Body *>&1 | Tee-Object -FilePath $log | Out-Host
        $code = $LASTEXITCODE
    }
    catch {
        $_ | Out-String | Add-Content -LiteralPath $log
        $code = -1
    }
    $text = if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Raw } else { '' }
    $result = [ordered]@{
        exitCode = $code
        warnings = ([regex]::Matches($text, ': warning [A-Z]+\d+')).Count
        errors = ([regex]::Matches($text, ': error [A-Z]+\d+')).Count
        nu1202 = $text.Contains('NU1202')
        ca1416 = ([regex]::Matches($text, 'CA1416')).Count
    }
    $summary.steps[$Name] = $result
    Save-Summary
    return $result
}

function New-DisposableStandardUser {
    $name = 'pcsp' + [Guid]::NewGuid().ToString('N').Substring(0, 12)
    $password = ConvertTo-SecureString ('Aa1!' + [Guid]::NewGuid().ToString('N')) -AsPlainText -Force
    $account = New-LocalUser -Name $name -Password $password -Description 'Disposable Task 54A spike user'
    Add-LocalGroupMember -SID 'S-1-5-32-545' -Member $account
    [pscustomobject]@{ Account = $account; Credential = [PSCredential]::new("$env:COMPUTERNAME\$name", $password) }
}

function Invoke-ProbeStage {
    $cases = @(
        @{ Name = 'p1-1.10.340-tfm22000'; Tfm = 'net10.0-windows10.0.22000.0'; Com = '1.10.340'; Min = '' }
        @{ Name = 'p2-1.11.560-tfm22000'; Tfm = 'net10.0-windows10.0.22000.0'; Com = '1.11.560'; Min = '' }
        @{ Name = 'p3-1.29.380-tfm22000'; Tfm = 'net10.0-windows10.0.22000.0'; Com = '1.29.380'; Min = '' }
        @{ Name = 'p4-1.10.340-tfm26100-min22000'; Tfm = 'net10.0-windows10.0.26100.0'; Com = '1.10.340'; Min = '10.0.22000.0' }
        @{ Name = 'p5-1.11.560-tfm26100-min22000'; Tfm = 'net10.0-windows10.0.26100.0'; Com = '1.11.560'; Min = '10.0.22000.0' }
        @{ Name = 'p6-1.29.380-tfm26100-min22000'; Tfm = 'net10.0-windows10.0.26100.0'; Com = '1.29.380'; Min = '10.0.22000.0' }
    )
    $runnerWinget = [ordered]@{}
    try { $runnerWinget.appInstaller = (Get-AppxPackage -Name Microsoft.DesktopAppInstaller | Select-Object -First 1).Version } catch { $runnerWinget.appInstaller = 'not found' }
    try { $runnerWinget.wingetCli = (& winget --version 2>&1 | Out-String).Trim() } catch { $runnerWinget.wingetCli = 'not found' }
    $summary.runnerWinget = $runnerWinget

    $work = Join-Path $spike '.work'
    $built = @()
    foreach ($case in $cases) {
        $folder = Join-Path $work $case.Name
        New-Item -ItemType Directory -Path $folder -Force | Out-Null
        Copy-Item -Path (Join-Path $spike 'probe\*') -Destination $folder -Recurse -Force
        $out = Join-Path $folder 'out'
        $arguments = @('build', (Join-Path $folder 'WinGetComProbe.csproj'), '--configuration', 'Release', "-p:ProbeTargetFramework=$($case.Tfm)", "-p:ProbeComVersion=$($case.Com)", '--output', $out, '--disable-build-servers')
        if ($case.Min) { $arguments += "-p:ProbeMinimumOs=$($case.Min)" }
        $result = Invoke-Logged "build-$($case.Name)" { & dotnet @arguments }
        if ($result.exitCode -eq 0) {
            $built += [pscustomobject]@{ Name = $case.Name; Exe = (Join-Path $out 'WinGetComProbe.exe'); Out = $out }
            Get-ChildItem -LiteralPath $out -File | Sort-Object Name | ForEach-Object { "{0}`t{1}" -f $_.Name, $_.Length } |
                Set-Content -LiteralPath (Join-Path $evidence "files-$($case.Name).txt")
        }
    }

    $runs = [ordered]@{}
    foreach ($probe in $built) {
        foreach ($route in @('inproc', 'outofproc')) {
            $file = Join-Path $evidence "run-$($probe.Name)-$route-elevated.txt"
            & $probe.Exe $route '--query' '--out' $file | Out-Host
            $runs["$($probe.Name)/$route/elevated"] = if (Test-Path -LiteralPath $file) { Get-Content -LiteralPath $file } else { @('no output') }
        }
    }

    # The desktop runs with a standard user's token, so the activation routes are repeated as a disposable standard user.
    $user = $null
    try {
        $user = New-DisposableStandardUser
        $results = Join-Path $spike '.work\standard-user-results'
        New-Item -ItemType Directory -Path $results -Force | Out-Null
        & icacls.exe $work /grant "*$($user.Account.SID.Value):(OI)(CI)M" /T /Q | Out-Null
        foreach ($probe in $built) {
            foreach ($route in @('inproc', 'outofproc')) {
                $file = Join-Path $results "run-$($probe.Name)-$route-standard.txt"
                $process = Start-Process -FilePath $probe.Exe -ArgumentList @($route, '--query', '--out', ('"' + $file + '"')) -Credential $user.Credential -LoadUserProfile -WorkingDirectory $probe.Out -WindowStyle Hidden -PassThru
                $null = $process.Handle
                if (!$process.WaitForExit(360000)) { $process.Kill($true); $process.WaitForExit() }
                $lines = if (Test-Path -LiteralPath $file) { Get-Content -LiteralPath $file } else { @("no output; exit $($process.ExitCode)") }
                $runs["$($probe.Name)/$route/standard"] = $lines
                $lines | Set-Content -LiteralPath (Join-Path $evidence "run-$($probe.Name)-$route-standard.txt")
                $process.Dispose()
            }
        }
    }
    catch {
        $runs['standard-user-setup'] = @($_.Exception.Message)
    }
    finally {
        if ($user) { Remove-LocalUser -SID $user.Account.SID }
    }
    $summary.runs = $runs
    Save-Summary
}

function Invoke-SurfaceStage {
    $tool = Join-Path $spike '.work\surface-tool'
    $toolResult = Invoke-Logged 'build-surface-tool' { & dotnet build (Join-Path $spike 'surface\WinmdSurface.csproj') --configuration Release --output $tool --disable-build-servers }
    if ($toolResult.exitCode -ne 0) { throw 'The surface tool did not build.' }
    $packages = Join-Path $spike '.work\packages'
    $dumpbin = $null
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        $vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
        if ($vs) { $dumpbin = Get-ChildItem -Path (Join-Path $vs 'VC\Tools\MSVC\*\bin\Hostx64\x64\dumpbin.exe') -ErrorAction SilentlyContinue | Select-Object -First 1 }
    }
    $packageFacts = [ordered]@{}
    foreach ($version in $surfaceVersions) {
        $facts = [ordered]@{}
        foreach ($id in @('microsoft.windowspackagemanager.cominterop', 'microsoft.windowspackagemanager.inproccom')) {
            $file = Join-Path $packages "$id.$version.nupkg"
            $folder = Join-Path $packages "$id.$version"
            New-Item -ItemType Directory -Path $packages -Force | Out-Null
            Invoke-WebRequest -Uri "https://api.nuget.org/v3-flatcontainer/$id/$version/$id.$version.nupkg" -OutFile $file -UseBasicParsing
            Expand-Archive -LiteralPath $file -DestinationPath $folder -Force
            $facts["$id/sha256"] = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
            $facts["$id/files"] = @(Get-ChildItem -LiteralPath $folder -File -Recurse | ForEach-Object { $_.FullName.Substring($folder.Length + 1).Replace('\', '/') } |
                Where-Object { $_ -notmatch '^(_rels/|package/|\[Content_Types\]\.xml$|\.signature\.p7s$)' } | Sort-Object)
            [xml]$nuspec = Get-Content -LiteralPath (Get-ChildItem -LiteralPath $folder -Filter '*.nuspec').FullName -Raw
            $facts["$id/dependencyGroups"] = @($nuspec.package.metadata.dependencies.group | ForEach-Object { $_.targetFramework })
            $facts["$id/license"] = [string]$nuspec.package.metadata.license.'#text'
        }
        $winmd = Join-Path $packages "microsoft.windowspackagemanager.cominterop.$version\lib\uap10.0\Microsoft.Management.Deployment.winmd"
        & (Join-Path $tool 'WinmdSurface.exe') $winmd | Set-Content -LiteralPath (Join-Path $evidence "surface-$version.txt")
        $runtime = Join-Path $packages "microsoft.windowspackagemanager.inproccom.$version\runtimes\win-x64\native"
        $runtimeWinmd = Join-Path $runtime 'Microsoft.Management.Deployment.winmd'
        & (Join-Path $tool 'WinmdSurface.exe') $runtimeWinmd | Set-Content -LiteralPath (Join-Path $evidence "surface-runtime-$version.txt")
        $facts['runtimeWinmdMatchesComInteropSurface'] = ((Get-FileHash (Join-Path $evidence "surface-$version.txt")).Hash -eq (Get-FileHash (Join-Path $evidence "surface-runtime-$version.txt")).Hash)
        $facts['inProcManifestActivatableClassCount'] = ([regex]::Matches((Get-Content -LiteralPath (Join-Path $runtime 'Microsoft.Management.Deployment.InProc.dll.manifest') -Raw), 'activatableClass')).Count
        if ($dumpbin) {
            foreach ($leaf in @('WindowsPackageManager.dll', 'Microsoft.Management.Deployment.InProc.dll')) {
                $imports = & $dumpbin.FullName /nologo /dependents (Join-Path $runtime $leaf) | Where-Object { $_ -match '^\s+\S+\.dll\s*$' } | ForEach-Object { $_.Trim().ToLowerInvariant() } | Sort-Object -Unique
                $facts["$leaf/imports"] = @($imports)
                $facts["$leaf/vcRuntimeImports"] = @($imports | Where-Object { $_ -match '^(vcruntime140|vcruntime140_1|msvcp140|concrt140)\.dll$' })
            }
        }
        else {
            $facts['imports'] = 'dumpbin not found on this runner'
        }
        $packageFacts[$version] = $facts
    }
    foreach ($version in $surfaceVersions | Where-Object { $_ -ne $baselineVersion }) {
        $old = Get-Content -LiteralPath (Join-Path $evidence "surface-$baselineVersion.txt")
        $new = Get-Content -LiteralPath (Join-Path $evidence "surface-$version.txt")
        $diff = Compare-Object -ReferenceObject $old -DifferenceObject $new
        @($diff | ForEach-Object { ($(if ($_.SideIndicator -eq '=>') { '+ ' } else { '- ' })) + $_.InputObject }) |
            Set-Content -LiteralPath (Join-Path $evidence "surface-diff-$baselineVersion-to-$version.txt")
        $packageFacts["diff-$version"] = [ordered]@{
            added = @($diff | Where-Object SideIndicator -eq '=>').Count
            removed = @($diff | Where-Object SideIndicator -eq '<=').Count
        }
    }
    $summary.packages = $packageFacts
    Save-Summary
}

function Set-Retarget([string]$Version) {
    $newTfm = 'net10.0-windows10.0.26100.0'
    $minimum = "<SupportedOSPlatformVersion>10.0.22000.0</SupportedOSPlatformVersion><TargetPlatformMinVersion>10.0.22000.0</TargetPlatformMinVersion>"
    $projects = @(Get-ChildItem -Path (Join-Path $repository 'src'), (Join-Path $repository 'tests') -Filter '*.csproj' -Recurse)
    foreach ($project in $projects) {
        $text = [IO.File]::ReadAllText($project.FullName)
        if ($text.Contains('net10.0-windows10.0.22000.0')) {
            $text = $text.Replace('<TargetFramework>net10.0-windows10.0.22000.0</TargetFramework>', "<TargetFramework>$newTfm</TargetFramework>$minimum")
            [IO.File]::WriteAllText($project.FullName, $text)
        }
    }
    $props = Join-Path $repository 'Directory.Packages.props'
    [IO.File]::WriteAllText($props, ([IO.File]::ReadAllText($props).Replace("Version=""$baselineVersion""", "Version=""$Version""")))
    foreach ($relative in @('scripts\New-PrivateBetaPackage.ps1', 'scripts\Test-PrivateBetaPackageEvidence.ps1', 'tests\PCSteward.Security.FixtureTests\DeploymentCapabilityScriptTests.cs')) {
        $path = Join-Path $repository $relative
        $text = [IO.File]::ReadAllText($path).Replace($baselineVersion, $Version).Replace('net10.0-windows10.0.22000.0', $newTfm)
        [IO.File]::WriteAllText($path, $text)
    }
}

function Invoke-SolutionStage {
    Push-Location $repository
    try {
        if ($Variant -ne 'baseline') { Set-Retarget $Variant }
        & git diff --stat | Set-Content -LiteralPath (Join-Path $evidence 'retarget-diffstat.txt')
        & git diff | Set-Content -LiteralPath (Join-Path $evidence 'retarget.diff')

        Invoke-Logged 'restore' { & pwsh -NoProfile -File .\scripts\Restore.ps1 } | Out-Null
        $assets = Join-Path $repository 'src\PCSteward.Desktop\obj\project.assets.json'
        if (Test-Path -LiteralPath $assets) {
            $libraries = (Get-Content -LiteralPath $assets -Raw | ConvertFrom-Json).libraries.PSObject.Properties.Name
            $summary.desktopRestoredWindowsPackages = @($libraries | Where-Object { $_ -match '^Microsoft\.Windows(PackageManager|\.SDK|AppSDK)' })
        }
        Invoke-Logged 'build' { & pwsh -NoProfile -File .\scripts\Build.ps1 } | Out-Null
        Invoke-Logged 'test-ci' { & pwsh -NoProfile -File .\scripts\Test-CI.ps1 } | Out-Null
        $testLog = Get-Content -LiteralPath (Join-Path $evidence 'test-ci.log') -Raw
        $summary.testTotals = @([regex]::Matches($testLog, '(Passed|Failed)!\s+-\s+Failed:\s+\d+,\s+Passed:\s+\d+,\s+Skipped:\s+\d+,\s+Total:\s+\d+[^\r\n]*') | ForEach-Object { $_.Value })
        $summary.failedTests = @([regex]::Matches($testLog, '(?m)^\s+Failed\s+\S+.*$') | ForEach-Object { $_.Value.Trim() } | Select-Object -First 40)
        Invoke-Logged 'helper-native-aot' { & pwsh -NoProfile -File .\scripts\Test-ElevatedHelperNativeAotCI.ps1 } | Out-Null

        # The packaging script reads the default per-user NuGet cache, so it runs without the CI cache override.
        $savedCache = $env:NUGET_PACKAGES
        Remove-Item Env:NUGET_PACKAGES -ErrorAction SilentlyContinue
        try {
            Invoke-Logged 'private-beta-package' { & pwsh -NoProfile -File .\scripts\New-PrivateBetaPackage.ps1 } | Out-Null
            Invoke-Logged 'private-beta-evidence' { & pwsh -NoProfile -File .\scripts\Test-PrivateBetaPackageEvidence.ps1 } | Out-Null
        }
        finally {
            if ($savedCache) { $env:NUGET_PACKAGES = $savedCache }
        }
        $payload = Join-Path $repository 'artifacts\private-beta\payload'
        if (Test-Path -LiteralPath $payload) {
            Get-ChildItem -LiteralPath $payload -File -Recurse | Sort-Object FullName | ForEach-Object {
                "{0}`t{1}`t{2}" -f $_.FullName.Substring($payload.Length + 1).Replace('\', '/'), $_.Length, (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            } | Set-Content -LiteralPath (Join-Path $evidence 'payload-files.txt')
            $files = @(Get-ChildItem -LiteralPath $payload -File -Recurse)
            $summary.payload = [ordered]@{ files = $files.Count; bytes = ($files | Measure-Object Length -Sum).Sum }
        }
        Invoke-Logged 'desktop-launch-smoke' { & powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-DesktopLaunchSmokeCI.ps1 } | Out-Null
    }
    finally {
        Pop-Location
        Save-Summary
    }
}

switch ($Stage) {
    'probe' { Invoke-ProbeStage }
    'surface' { Invoke-SurfaceStage }
    'solution' { Invoke-SolutionStage }
}
Save-Summary
Get-Content -LiteralPath (Join-Path $evidence 'summary.json') | Out-Host
exit 0
