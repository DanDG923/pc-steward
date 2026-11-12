[CmdletBinding()]
param([switch]$StandardUser, [string]$WorkRoot, [switch]$ReleasePayload, [switch]$RestoreOnly, [string]$RestoreTree)

# Task 50 G3 Windows CI evidence: run scripts/New-OwnerOnlyCandidate.ps1 end to end under a disposable standard user
# (the builder refuses an elevated token, and hosted runners are elevated), then re-validate the written candidate in a
# fresh Windows PowerShell 5.1 process from the bytes on disk. No signing, certificate store, install or launch.
# -ReleasePayload (ADR-034 §5, release-payload workflow only): the commit must carry both compiled release pins, and the
# validated candidate is kept in artifacts\release-payload with release-payload.json for the owner's signing script.
# Pinned toolchain (Codex security scan finding 1; every run, so every PR proves the release path): the build uses only
# Microsoft's official SDK zip pinned in scripts\release-toolchain-pins.json, and a NuGet cache that a first disposable user
# restored in a separate worktree, that this administrator then locked (administrators-only, read-only to the build user) and
# checked byte for byte against the pins, before a second disposable user builds. A package that is not pinned, or differs
# from its pin, stops the run before any release project builds.
$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
$isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$repository = Split-Path -Parent $PSScriptRoot
$builder = Join-Path $PSScriptRoot 'New-OwnerOnlyCandidate.ps1'
$windowsPowerShell = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::System)) 'WindowsPowerShell\v1.0\powershell.exe'
. (Join-Path $PSScriptRoot 'PCStewardReleaseToolchain.ps1')
# ADR-034 §2: the two compiled release pins of this checkout; a release payload needs both.
function Get-ReleasePins {
    $source = Get-Content -LiteralPath (Join-Path $repository 'src\PCSteward.Execution\ReleaseSigningPins.cs') -Raw
    $pins = [ordered]@{}
    foreach ($name in @('Primary', 'Backup')) {
        $match = [regex]::Match($source, 'internal const string ' + $name + ' = "([0-9A-F]{64})";')
        if (!$match.Success) { throw "ReleaseSigningPins.$name is not set; a release payload needs both compiled pins." }
        $pins[$name.ToLowerInvariant()] = $match.Groups[1].Value
    }
    $pins
}

# The per-user environment of a disposable CI user (both the restore user and the build user).
function Set-DisposableUserEnvironment([string]$WorkRoot) {
    $env:TEMP = Join-Path $WorkRoot 'temp'
    $env:TMP = $env:TEMP
    $env:DOTNET_CLI_HOME = Join-Path $WorkRoot 'dotnet-home'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    # Start-Process -Credential passes the runner account's environment, so APPDATA/LOCALAPPDATA still name
    # runneradmin's profile, which this user cannot read (first CI run: NuGet "Failed to read NuGet.Config due to
    # unauthorized access"). Give this disposable user its own per-run folders under the work root.
    $env:APPDATA = Join-Path $WorkRoot 'appdata'
    $env:LOCALAPPDATA = Join-Path $WorkRoot 'localappdata'
    New-Item -ItemType Directory -Path $env:APPDATA, $env:LOCALAPPDATA -Force | Out-Null
    # HOME/USERPROFILE and HOMEDRIVE+HOMEPATH (read by Git for Windows) also still name runneradmin's profile.
    $userHome = Join-Path $WorkRoot 'home'
    New-Item -ItemType Directory -Path $userHome -Force | Out-Null
    $env:HOME = $userHome
    $env:USERPROFILE = $userHome
    $env:HOMEDRIVE = Split-Path -Qualifier $userHome
    $env:HOMEPATH = $userHome.Substring($env:HOMEDRIVE.Length)
    # Process-scoped, command-level Git configuration for this disposable user only (no Git config file is changed):
    # the checkout belongs to the runner account, so Git's ownership check would refuse rev-parse/status. Only this
    # checkout is trusted (Git compares safe.directory against the forward-slash path).
    $env:GIT_CONFIG_COUNT = '1'
    $env:GIT_CONFIG_KEY_0 = 'safe.directory'
    $env:GIT_CONFIG_VALUE_0 = $repository.Replace('\', '/')
    New-Item -ItemType Directory -Path $env:TEMP, $env:DOTNET_CLI_HOME -Force | Out-Null
}

# The restore user (no build): restore the three release projects of the separate worktree into the toolchain's cache.
if ($RestoreOnly) {
    if ($isAdministrator) { throw 'The toolchain restore requires a standard-user token.' }
    if (!$WorkRoot -or !(Test-Path -LiteralPath $WorkRoot -PathType Container) -or !$RestoreTree -or !(Test-Path -LiteralPath $RestoreTree -PathType Container)) { throw 'A prepared WorkRoot and RestoreTree are required.' }
    Set-DisposableUserEnvironment $WorkRoot
    $env:GIT_CONFIG_VALUE_0 = $RestoreTree.Replace('\', '/')
    Set-Location -LiteralPath $RestoreTree
    $dotnet = (Get-Command -Name 'dotnet' -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    foreach ($project in $script:ReleaseToolchainProjects) {
        & $dotnet @('restore', (Join-Path $RestoreTree $project), '--runtime', 'win-x64', '-nodeReuse:false')
        if ($LASTEXITCODE -ne 0) { throw "The toolchain restore of $project failed with exit code $LASTEXITCODE." }
    }
    exit 0
}

if ($StandardUser) {
    if ($isAdministrator) { throw 'The candidate builder CI run requires a standard-user token.' }
    if (!$WorkRoot -or !(Test-Path -LiteralPath $WorkRoot -PathType Container)) { throw 'A prepared WorkRoot is required.' }
    Set-Location -LiteralPath $repository
    Set-DisposableUserEnvironment $WorkRoot
    $candidate = Join-Path $WorkRoot 'candidate'

    & $windowsPowerShell @('-NoProfile', '-NonInteractive', '-File', $builder, '-OutputRoot', $candidate)
    if ($LASTEXITCODE -ne 0) { throw "The candidate builder failed with exit code $LASTEXITCODE." }
    $summaryPath = Join-Path $candidate 'candidate-build.json'
    $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json

    # Independent re-validation from disk, in a new Windows PowerShell 5.1 process (the installer runtime).
    $validationOutput = @(& $windowsPowerShell @('-NoProfile', '-NonInteractive', '-File', $builder, '-ValidateCandidateRoot', $candidate, '-ExpectedManifestSha256', [string]$summary.manifestSha256))
    if ($LASTEXITCODE -ne 0) { throw "Independent candidate re-validation failed with exit code $LASTEXITCODE." }
    $validation = ConvertFrom-Json -InputObject ([string]($validationOutput | Where-Object { $_ } | Select-Object -Last 1))
    $manifestBytes = [IO.File]::ReadAllBytes((Join-Path $candidate 'candidate-manifest.json'))
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $manifestHash = [BitConverter]::ToString($sha.ComputeHash($manifestBytes)).Replace('-', '') } finally { $sha.Dispose() }
    $failures = [Collections.Generic.List[string]]::new()
    if ($validation.validation -cne 'Pass') { $failures.Add('validation') }
    if ($manifestHash -cne $summary.manifestSha256 -or $validation.manifestSha256 -cne $summary.manifestSha256) { $failures.Add('manifest hash') }
    if ($manifestBytes.Length -ge 3 -and $manifestBytes[0] -eq 0xEF -and $manifestBytes[1] -eq 0xBB -and $manifestBytes[2] -eq 0xBF) { $failures.Add('manifest BOM') }
    if ($summary.payloadOrigin -cne 'pinned-unsigned-v2' -or $validation.payloadOrigin -cne 'pinned-unsigned-v2') { $failures.Add('payload origin') }
    if ([string]$summary.helperProtocol -cne '2' -or [string]$validation.helperProtocol -cne '2') { $failures.Add('helper protocol') }
    if ($validation.fileCount -ne $summary.fileCount -or $validation.totalBytes -ne $summary.totalBytes) { $failures.Add('file count/bytes') }
    if ($summary.sourceDirty -or $summary.allowDirty -or $summary.signed -or $summary.deterministicBuild -or $summary.selfContained) { $failures.Add('summary flags') }
    if ($summary.sourceCommit -notmatch '\A[0-9a-f]{40}\z') { $failures.Add('source commit') }
    # Codex security scan finding 1: the build used exactly the pinned SDK (the launcher put only that one on PATH).
    $toolchainPins = Read-PCStewardReleaseToolchainPins -RepositoryRoot $repository
    if ([string]$summary.sdkVersion -cne $toolchainPins.SdkVersion) { $failures.Add('pinned SDK') }
    # Task 50 S7 obligation 1 (probe spec section 9): a clean build of a commit that contains the probe is marked included.
    if ($summary.schema -cne 'pc-steward-owner-only-candidate-build-v2' -or $summary.probeMark -cne 'pc-steward-probe-build-mark:1' -or $summary.probeIncluded -ne $true -or $summary.probeMarkDetail -cne 'included' -or $summary.probeCommit -cne '2172f955a751ccc464b27075053c835790bc412f') { $failures.Add('probe build mark') }
    # Phase C close-out (ADR-008): the payload carries the generated third-party notices, including the helper's runtime.
    $noticesFile = Join-Path $candidate 'payload\THIRD-PARTY-NOTICES.txt'
    $noticesText = if (Test-Path -LiteralPath $noticesFile -PathType Leaf) { [IO.File]::ReadAllText($noticesFile, [Text.UTF8Encoding]::new($false)) } else { '' }
    foreach ($section in @('Package: Microsoft.WindowsAppSDK ', 'Package: Microsoft.WindowsPackageManager.InProcCom ', 'Package: Microsoft.NETCore.App.Runtime.NativeAOT.win-x64 ', 'Package: runtime.win-x64.Microsoft.DotNet.ILCompiler ')) {
        if (!$noticesText.Contains($section)) { $failures.Add("third-party notices ($($section.Trim()))") }
    }
    if ($failures.Count -gt 0) { throw "Candidate builder CI evidence failed: $($failures -join ', ')" }

    # Small JSON evidence only; the payload itself is not uploaded.
    $evidence = Join-Path $repository 'artifacts\candidate-builder-evidence'
    New-Item -ItemType Directory -Path $evidence -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $candidate 'candidate-manifest.json'), $summaryPath, $noticesFile -Destination $evidence
    [IO.File]::WriteAllText((Join-Path $evidence 'candidate-revalidation.json'), ($validationOutput | Where-Object { $_ } | Select-Object -Last 1), [Text.UTF8Encoding]::new($false))
    if ($ReleasePayload) {
        $release = Join-Path $repository 'artifacts\release-payload'
        New-Item -ItemType Directory -Path $release -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $candidate 'payload'), (Join-Path $candidate 'candidate-manifest.json'), $summaryPath -Destination $release -Recurse
        $props = [xml](Get-Content -LiteralPath (Join-Path $repository 'Directory.Build.props') -Raw)
        $prefix = [string]@($props.Project.PropertyGroup | ForEach-Object { $_.VersionPrefix } | Where-Object { $_ })[0]
        $suffix = [string]@($props.Project.PropertyGroup | ForEach-Object { $_.VersionSuffix } | Where-Object { $_ })[0]
        $record = [ordered]@{
            schema = 'pc-steward-release-payload:1'; repository = $env:GITHUB_REPOSITORY; runId = $env:GITHUB_RUN_ID; sourceCommit = [string]$summary.sourceCommit
            productVersion = $(if ($suffix) { "$prefix-$suffix" } else { $prefix }); manifestSha256 = [string]$summary.manifestSha256
            fileCount = $summary.fileCount; pins = Get-ReleasePins
            # The pin file this payload's toolchain was checked against; the signing script compares it with its own clone.
            toolchain = [ordered]@{ pinsSha256 = $toolchainPins.Sha256; sdkVersion = $toolchainPins.SdkVersion; packages = $toolchainPins.Packages.Count }
        }
        [IO.File]::WriteAllText((Join-Path $release 'release-payload.json'), ($record | ConvertTo-Json -Depth 3), [Text.UTF8Encoding]::new($false))
    }
    Write-Output "PASS: owner-only candidate built and re-validated: $($summary.fileCount) files, $($summary.totalBytes) bytes, manifest $($summary.manifestSha256), commit $($summary.sourceCommit), SDK $($summary.sdkVersion), probe included $($summary.probeIncluded)"
    exit 0
}

# Account creation is confined to disposable GitHub-hosted Windows runners.
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or !$isAdministrator) {
    throw 'Run New-OwnerOnlyCandidate.ps1 directly on a developer machine; this launcher requires an administrator on a GitHub-hosted runner.'
}
if (!$env:RUNNER_TEMP) { throw 'RUNNER_TEMP is required.' }
if ($ReleasePayload) { $null = Get-ReleasePins }

# Administrators and SYSTEM only on the folder itself, nothing inherited from above (a runner drive root lets every user change
# what is created below it); everything inside is owned by Administrators and only inherits from the folder.
function Set-AdministratorsOnly([string]$Path) {
    & icacls.exe $Path /setowner '*S-1-5-32-544' /T /C /Q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not take ownership of $Path." }
    & icacls.exe $Path /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' /Q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not protect $Path." }
    if (@(Get-ChildItem -LiteralPath $Path -Force).Count -gt 0) {
        & icacls.exe (Join-Path $Path '*') /reset /T /C /Q | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Could not make everything in $Path inherit its protection." }
    }
}

# An inheritable grant on the folder; everything inside inherits it (icacls propagates it to children that inherit).
function Grant-DisposableUser([string]$Path, [string]$Sid, [string]$Rights) {
    & icacls.exe $Path /grant "*$($Sid):(OI)(CI)$Rights" /Q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not grant the CI user access to $Path." }
}

function Revoke-DisposableUser([string]$Path, [string]$Sid) {
    if (Test-Path -LiteralPath $Path) { & icacls.exe $Path /remove:g "*$Sid" /Q | Out-Null }
}

# Runs this launcher again as a new disposable standard user: -StandardUser (build) or -RestoreOnly. GrantModify/GrantRead are
# the only folders the account is given. The account and every process it left are removed afterwards.
function Invoke-AsDisposableUser {
    param([Parameter(Mandatory)][string]$Mode, [Parameter(Mandatory)][string]$Work, [string[]]$GrantModify = @(), [string[]]$GrantRead = @(), [string]$ExtraArguments = '', [Parameter(Mandatory)][string]$LogName, [string]$ScriptRoot = $PSScriptRoot)
    $name = 'pcsc' + [Guid]::NewGuid().ToString('N').Substring(0, 12)
    $password = ConvertTo-SecureString ('Aa1!' + [Guid]::NewGuid().ToString('N')) -AsPlainText -Force
    $account = $null; $process = $null; $exitCode = 1; $stdout = $null; $stderr = $null
    try {
        $account = New-LocalUser -Name $name -Password $password -Description 'Disposable PC Steward candidate-builder CI user'
        Add-LocalGroupMember -SID 'S-1-5-32-545' -Member $account
        New-Item -ItemType Directory -Path $Work | Out-Null
        foreach ($path in @($GrantModify) + @($Work)) { Grant-DisposableUser $path $account.SID.Value 'M' }
        foreach ($path in @($GrantRead)) { Grant-DisposableUser $path $account.SID.Value 'RX' }
        $logs = Join-Path $repository 'artifacts\candidate-builder-logs'
        New-Item -ItemType Directory -Path $logs -Force | Out-Null
        $stdout = Join-Path $logs "$LogName-stdout.log"
        $stderr = Join-Path $logs "$LogName-stderr.log"
        $credential = [PSCredential]::new("$env:COMPUTERNAME\$name", $password)
        $arguments = '-NoProfile -File "' + (Join-Path $ScriptRoot 'Test-OwnerOnlyCandidateBuilderCI.ps1') + '" ' + $Mode + ' -WorkRoot "' + $Work + '"' + $ExtraArguments
        $process = Start-Process -FilePath (Get-Command pwsh.exe).Source -ArgumentList $arguments -Credential $credential -LoadUserProfile -WorkingDirectory $Work -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
        # Retain the handle before waiting so ExitCode remains available after exit.
        $null = $process.Handle
        if (!$process.WaitForExit(2400000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "The $LogName run exceeded forty minutes."
        }
        $exitCode = $process.ExitCode
    }
    finally {
        if ($process) { $process.Dispose() }
        foreach ($log in @($stdout, $stderr)) {
            if ($log -and (Test-Path -LiteralPath $log)) { Get-Content -LiteralPath $log | Out-Host }
        }
        if ($account) {
            # Its grants go with it, so no orphaned entry is left on the SDK, the cache or the checkout.
            foreach ($path in @($GrantModify) + @($GrantRead)) { Revoke-DisposableUser $path $account.SID.Value }
            # Nothing the account started (an MSBuild node, a compiler server) may outlive it or touch the cache later.
            Get-Process -IncludeUserName -ErrorAction SilentlyContinue | Where-Object { $_.UserName -and $_.UserName.EndsWith('\' + $name, [StringComparison]::OrdinalIgnoreCase) } | ForEach-Object { try { $_.Kill($true) } catch { } }
            Remove-LocalUser -SID $account.SID
        }
        $password.Dispose()
    }
    $exitCode
}

# 1. The pinned SDK, in a new administrators-only folder (Codex security scan finding 1).
$toolchainPins = Read-PCStewardReleaseToolchainPins -RepositoryRoot $repository
$toolchain = Join-Path $env:RUNNER_TEMP ('pcsteward-toolchain-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $toolchain | Out-Null
Set-AdministratorsOnly $toolchain
$sdkRoot = Install-PCStewardPinnedSdk -Pins $toolchainPins -Folder $toolchain
Set-AdministratorsOnly $toolchain
$cache = Join-Path $toolchain 'nuget'
New-Item -ItemType Directory -Path $cache | Out-Null
# Every child (both disposable users and their dotnet) gets only the pinned SDK and the toolchain cache.
$env:DOTNET_ROOT = $sdkRoot
$env:PATH = $sdkRoot + [IO.Path]::PathSeparator + $env:PATH
$env:NUGET_PACKAGES = $cache
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
$env:DOTNET_NOLOGO = '1'
Write-Output "TOOLCHAIN sdk=$($toolchainPins.SdkVersion) pins=$($toolchainPins.Sha256) pinnedPackages=$($toolchainPins.Packages.Count)"

# 2. A first disposable user restores the release projects of a separate worktree of this commit into the empty cache. It never
#    gets the checkout the payload is built from.
$restoreTree = Join-Path $toolchain 'restore-tree'
& git -C $repository worktree add --detach $restoreTree HEAD | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'git worktree add failed.' }
try {
    $restoreCode = Invoke-AsDisposableUser -Mode '-RestoreOnly' -Work (Join-Path $env:RUNNER_TEMP ('pcsteward-restore-' + [Guid]::NewGuid().ToString('N'))) -GrantModify @($restoreTree, $cache) -GrantRead @($sdkRoot) -ExtraArguments (' -RestoreTree "' + $restoreTree + '"') -LogName 'toolchain-restore' -ScriptRoot (Join-Path $restoreTree 'scripts')
    if ($restoreCode -ne 0) { throw "The toolchain restore failed with exit code $restoreCode." }
    # 3. Lock the cache (that user is gone), then check it byte for byte against the pins before anything builds.
    Set-AdministratorsOnly $cache
    $assets = @($script:ReleaseToolchainProjects | ForEach-Object { Join-Path (Join-Path $restoreTree (Split-Path -Parent $_)) 'obj\project.assets.json' })
    $restored = Get-PCStewardRestoredPackages -AssetsPaths $assets -PackagesRoot $cache
    $problems = @($restored.Problems) + @(Test-PCStewardRestoredPackages -Used $restored.Used -Pins $toolchainPins -PackagesRoot $cache)
    Write-Output "TOOLCHAIN-PACKAGES used=$($restored.Used.Count) pinned=$($toolchainPins.Packages.Count) problems=$($problems.Count)"
    if ($problems.Count -gt 0) {
        $problems | Select-Object -First 40 | ForEach-Object { Write-Output "  TOOLCHAIN-PROBLEM $_" }
        Write-Output '----- generated release toolchain pins (review, then commit as scripts/release-toolchain-pins.json) -----'
        Write-Output (ConvertTo-PCStewardReleaseToolchainPinsJson -Pins $toolchainPins -Used $restored.Used -PackagesRoot $cache)
        Write-Output '----- end of generated pins -----'
        throw 'The restored toolchain differs from scripts/release-toolchain-pins.json; nothing was built.'
    }
    $pinnedUsed = @($restored.Used.Keys)
}
finally {
    & git -C $repository worktree remove --force $restoreTree | Out-Host
}

# 4. A second disposable user builds, reading the locked cache and SDK only.
$work = Join-Path $env:RUNNER_TEMP ('pcsteward-candidate-' + [Guid]::NewGuid().ToString('N'))
# Only the disposable checkout and work root are writable; installed programs remain protected.
$exitCode = Invoke-AsDisposableUser -Mode ('-StandardUser' + $(if ($ReleasePayload) { ' -ReleasePayload' } else { '' })) -Work $work -GrantModify @($repository) -GrantRead @($sdkRoot, $cache) -LogName 'build'
if ($exitCode -eq 0) {
    # The build restored exactly the checked set from the locked cache, with the pinned SDK.
    $buildAssets = @($script:ReleaseToolchainProjects | ForEach-Object { Join-Path (Join-Path $repository (Split-Path -Parent $_)) 'obj\project.assets.json' })
    $built = Get-PCStewardRestoredPackages -AssetsPaths $buildAssets -PackagesRoot $cache
    $summary = Get-Content -LiteralPath (Join-Path $work 'candidate\candidate-build.json') -Raw | ConvertFrom-Json
    $sameSet = ((@($built.Used.Keys) -join "`n") -ceq ($pinnedUsed -join "`n"))
    if ($built.Problems.Count -gt 0 -or !$sameSet -or [string]$summary.sdkVersion -cne $toolchainPins.SdkVersion) {
        throw "The build did not use the checked toolchain: problems=$($built.Problems.Count) samePackages=$sameSet sdk=$($summary.sdkVersion)."
    }
    Write-Output "TOOLCHAIN-OK sdk=$($summary.sdkVersion) packages=$($pinnedUsed.Count) pins=$($toolchainPins.Sha256)"
}
exit $exitCode
