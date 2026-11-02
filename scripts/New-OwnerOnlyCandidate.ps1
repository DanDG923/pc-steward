[CmdletBinding(DefaultParameterSetName = 'Build')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Build')][string]$OutputRoot,
    [Parameter(ParameterSetName = 'Build')][ValidateSet('Release')][string]$Configuration = 'Release',
    [Parameter(ParameterSetName = 'Build')][switch]$AllowDirty,
    # Internal: the builder re-runs itself in a fresh Windows PowerShell 5.1 process (the installer's runtime) to validate.
    [Parameter(Mandatory, ParameterSetName = 'Validate')][string]$ValidateCandidateRoot,
    [Parameter(Mandatory, ParameterSetName = 'Validate')][string]$ExpectedManifestSha256
)

# Task 50 gap G3 (FR-UPD-15, TM-03/TM-13, ADR-022 and its protocol-2 alignment note): build the unsigned v2 candidate
# payload and its pinned manifest for the owner-only protected test deployment. Non-elevated and local only: no signing,
# certificate store, installation, Program Files write, elevation request or network call by this script. `dotnet restore`
# resolves the centrally pinned package graph from the configured NuGet feeds/cache; that is the only network effect.
# No deterministic-build claim: the candidate is built once and pinned by its manifest SHA-256.
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Core') {
    # A Windows PowerShell child started from pwsh can inherit pwsh module paths; bind this host's own modules.
    foreach ($module in @('Management', 'Utility', 'Security')) { Import-Module ([IO.Path]::Combine($PSHOME, 'Modules', "Microsoft.PowerShell.$module", "Microsoft.PowerShell.$module.psd1")) -Force }
}

$script:CandidateSchema = 'pc-steward-owner-only-candidate-build-v1'
$script:CandidateRuntime = 'win-x64'
$script:ManifestLeaf = 'candidate-manifest.json'
$script:SummaryLeaf = 'candidate-build.json'
$script:CapabilityLeaf = 'PCSteward.DeploymentCapability.txt'

# Pure: the exact, structured dotnet argument arrays (one string per argument; nothing is joined into a command line).
# Profile matches the Task 48/49 candidates and the original installer: Release, win-x64, framework-dependent.
function Get-OwnerOnlyCandidateBuildSteps {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$Configuration,
        [Parameter(Mandatory)][string]$DesktopPublishDirectory,
        [Parameter(Mandatory)][string]$HelperPublishDirectory
    )
    $runtime = 'win-x64'
    $scheduledHost = Join-Path $RepositoryRoot 'src\PCSteward.ScheduledHost\PCSteward.ScheduledHost.csproj'
    $desktop = Join-Path $RepositoryRoot 'src\PCSteward.Desktop\PCSteward.Desktop.csproj'
    $helper = Join-Path $RepositoryRoot 'src\PCSteward.ElevatedHelper\PCSteward.ElevatedHelper.csproj'
    @(
        [pscustomobject]@{ Name = 'restore-scheduledhost'; Arguments = [string[]]@('restore', $scheduledHost, '--runtime', $runtime) }
        [pscustomobject]@{ Name = 'restore-desktop'; Arguments = [string[]]@('restore', $desktop, '--runtime', $runtime) }
        [pscustomobject]@{ Name = 'restore-helper'; Arguments = [string[]]@('restore', $helper, '--runtime', $runtime) }
        # The desktop's CopyScheduledHostRuntimeAfterBuild/Publish targets read the ScheduledHost RID output (Task 48 prerequisite).
        [pscustomobject]@{ Name = 'build-scheduledhost'; Arguments = [string[]]@('build', $scheduledHost, '--configuration', $Configuration, '--runtime', $runtime, '--self-contained', 'false', '--no-restore', '--disable-build-servers') }
        # The desktop RID build also produces App.xbf, the PRI and Resources\*.xbf that publish omits.
        [pscustomobject]@{ Name = 'build-desktop'; Arguments = [string[]]@('build', $desktop, '--configuration', $Configuration, '--runtime', $runtime, '--self-contained', 'false', '--no-restore', '--disable-build-servers') }
        [pscustomobject]@{ Name = 'build-helper'; Arguments = [string[]]@('build', $helper, '--configuration', $Configuration, '--runtime', $runtime, '--self-contained', 'false', '--no-restore', '--disable-build-servers') }
        [pscustomobject]@{ Name = 'publish-desktop'; Arguments = [string[]]@('publish', $desktop, '--configuration', $Configuration, '--runtime', $runtime, '--self-contained', 'false', '--no-restore', '--no-build', '--disable-build-servers', '--output', $DesktopPublishDirectory) }
        [pscustomobject]@{ Name = 'publish-helper'; Arguments = [string[]]@('publish', $helper, '--configuration', $Configuration, '--runtime', $runtime, '--self-contained', 'false', '--no-restore', '--no-build', '--disable-build-servers', '--output', $HelperPublishDirectory) }
    )
}

# Pure: global.json SDK rule. Returns $true for the exact pinned SDK, $false for an accepted latestPatch roll-forward.
function Test-OwnerOnlyCandidateSdkVersion {
    param([Parameter(Mandatory)][string]$Pinned, [string]$RollForward, [Parameter(Mandatory)][string]$Actual)
    $pattern = '\A(\d+)\.(\d+)\.(\d+)\z'
    if ($Pinned -notmatch $pattern) { throw "Unsupported pinned SDK version: $Pinned" }
    $pin = @([int]$Matches[1], [int]$Matches[2], [int]$Matches[3])
    if ($Actual -notmatch $pattern) { throw "The resolved .NET SDK '$Actual' is not a release version that global.json allows." }
    $act = @([int]$Matches[1], [int]$Matches[2], [int]$Matches[3])
    if ($Actual -ceq $Pinned) { return $true }
    if ($RollForward -cne 'latestPatch') { throw "The resolved .NET SDK '$Actual' differs from the pinned '$Pinned' (rollForward '$RollForward')." }
    if ($act[0] -ne $pin[0] -or $act[1] -ne $pin[1] -or [math]::Floor($act[2] / 100) -ne [math]::Floor($pin[2] / 100) -or $act[2] -lt $pin[2]) {
        throw "The resolved .NET SDK '$Actual' is outside the pinned '$Pinned' latestPatch band."
    }
    return $false
}

# Pure (string rules only): a canonical absolute drive path that is not, and is not under, a forbidden root.
function Assert-OwnerOnlyCandidateOutputRootRule {
    param(
        [Parameter(Mandatory)][string]$Path,
        [string[]]$ForbiddenRoots = @(),
        [string[]]$ForbiddenPrefixes = @()
    )
    if ($Path -notmatch '\A[A-Za-z]:\\[^\\]') { throw 'OutputRoot must be an absolute local drive path such as C:\Users\<you>\AppData\Local\Temp\<name>.' }
    $segments = $Path.Substring(3).Split('\')
    foreach ($segment in $segments) {
        # '~' refuses 8.3 short names, which the integrity reader would reject as aliases later anyway.
        if ($segment.Length -eq 0 -or $segment -eq '.' -or $segment -eq '..' -or $segment.EndsWith('.') -or $segment.EndsWith(' ') -or $segment.StartsWith(' ') -or $segment -match '[:*?"<>|/~]') {
            throw "OutputRoot is not a canonical long-form path: $Path"
        }
    }
    foreach ($root in @($ForbiddenRoots | Where-Object { $_ })) {
        $trimmed = $root.TrimEnd('\')
        if ($Path.Equals($trimmed, [StringComparison]::OrdinalIgnoreCase) -or $Path.StartsWith($trimmed + '\', [StringComparison]::OrdinalIgnoreCase) -or $trimmed.StartsWith($Path + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "OutputRoot must not be, contain or be under a protected or repository location: $root"
        }
    }
    foreach ($prefix in @($ForbiddenPrefixes | Where-Object { $_ })) {
        if ($Path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw "OutputRoot must not share the PC Steward data-root prefix: $prefix" }
    }
    $Path
}

function Get-OwnerOnlyCandidateFileSha256 {
    param([Parameter(Mandatory)][string]$Path)
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $sha = [Security.Cryptography.SHA256]::Create()
        try { [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') } finally { $sha.Dispose() }
    } finally { $stream.Dispose() }
}

# Enumerates a tree without following reparse points. Paths are relative with '\' separators (the manifest convention).
function Get-OwnerOnlyCandidateFileEntries {
    param([Parameter(Mandatory)][string]$Root)
    $rootInfo = [IO.DirectoryInfo]::new($Root)
    if (!$rootInfo.Exists -or ($rootInfo.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Candidate tree root is missing or a reparse point: $Root" }
    $entries = [Collections.Generic.List[object]]::new()
    $pending = [Collections.Generic.Stack[object]]::new()
    $pending.Push([pscustomobject]@{ Directory = $rootInfo; Relative = '' })
    while ($pending.Count -gt 0) {
        $current = $pending.Pop()
        foreach ($item in $current.Directory.GetFileSystemInfos()) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse point refused in candidate tree: $($item.FullName)" }
            $relative = if ($current.Relative) { $current.Relative + '\' + $item.Name } else { $item.Name }
            if ($item -is [IO.DirectoryInfo]) { $pending.Push([pscustomobject]@{ Directory = $item; Relative = $relative }); continue }
            $entries.Add([pscustomobject]@{ path = $relative; bytes = [long]$item.Length; sha256 = (Get-OwnerOnlyCandidateFileSha256 -Path $item.FullName) })
        }
    }
    $entries.ToArray()
}

# One expected XBF per source XAML (App.xaml -> App.xbf, Resources\X.xaml -> Resources\X.xbf); bin/obj are skipped.
function Get-OwnerOnlyCandidateExpectedXbf {
    param([Parameter(Mandatory)][string]$ProjectDirectory)
    $result = [Collections.Generic.List[string]]::new()
    $pending = [Collections.Generic.Stack[object]]::new()
    $pending.Push([pscustomobject]@{ Directory = [IO.DirectoryInfo]::new($ProjectDirectory); Relative = '' })
    while ($pending.Count -gt 0) {
        $current = $pending.Pop()
        foreach ($item in $current.Directory.GetFileSystemInfos()) {
            $relative = if ($current.Relative) { $current.Relative + '\' + $item.Name } else { $item.Name }
            if ($item -is [IO.DirectoryInfo]) {
                if (!$current.Relative -and ($item.Name -ieq 'bin' -or $item.Name -ieq 'obj')) { continue }
                if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse point refused in the desktop project: $($item.FullName)" }
                $pending.Push([pscustomobject]@{ Directory = $item; Relative = $relative }); continue
            }
            if ($item.Extension -ieq '.xaml') { $result.Add($relative.Substring(0, $relative.Length - 5) + '.xbf') }
        }
    }
    $sorted = $result.ToArray(); [Array]::Sort($sorted, [StringComparer]::Ordinal)
    , $sorted
}

# Pure: the exact manifest text read by Read-OwnerOnlyCandidateManifest: a JSON array of closed {bytes,path,sha256}
# objects, ordinal-sorted by path, '\' separators, upper-case SHA-256, LF line endings. Encoded as UTF-8 without BOM.
function ConvertTo-OwnerOnlyCandidateManifestBytes {
    param([Parameter(Mandatory)][object[]]$Entries)
    if ($Entries.Count -lt 1 -or $Entries.Count -gt 512) { throw 'A candidate manifest needs 1 to 512 files.' }
    $byPath = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
    [long]$total = 0
    foreach ($entry in $Entries) {
        $path = [string]$entry.path
        if ($path.Length -lt 1 -or $path.Length -gt 240) { throw "Candidate path length is outside its bound: $path" }
        foreach ($part in $path.Split('\')) {
            if ($part.Length -eq 0 -or $part -eq '.' -or $part -eq '..' -or $part.EndsWith('.') -or $part.EndsWith(' ') -or $part.StartsWith(' ') -or $part -cnotmatch '\A[A-Za-z0-9_. -]+\z' -or $part -match '\A(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\.|$)') {
                throw "Noncanonical candidate path: $path"
            }
        }
        if ([string]$entry.sha256 -cnotmatch '\A[0-9A-F]{64}\z' -or [long]$entry.bytes -lt 0) { throw "Invalid candidate entry: $path" }
        if ($byPath.ContainsKey($path)) { throw "Duplicate or case-colliding candidate path: $path" }
        $byPath.Add($path, $entry)
        $total += [long]$entry.bytes
    }
    if ($total -gt 256MB) { throw 'The candidate payload exceeds the 256 MiB manifest bound.' }
    $paths = [string[]]@($byPath.Keys)
    [Array]::Sort($paths, [StringComparer]::Ordinal)
    $builder = [Text.StringBuilder]::new()
    [void]$builder.Append("[`n")
    for ($index = 0; $index -lt $paths.Length; $index++) {
        $entry = $byPath[$paths[$index]]
        $escaped = $paths[$index].Replace('\', '\\')
        [void]$builder.Append('  {"bytes":').Append(([long]$entry.bytes).ToString([Globalization.CultureInfo]::InvariantCulture)).Append(',"path":"').Append($escaped).Append('","sha256":"').Append([string]$entry.sha256).Append('"}')
        if ($index -lt $paths.Length - 1) { [void]$builder.Append(',') }
        [void]$builder.Append("`n")
    }
    [void]$builder.Append("]`n")
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($builder.ToString())
    if ($bytes.Length -gt 1MB) { throw 'The candidate manifest exceeds its 1 MiB bound.' }
    , $bytes
}

# Writes the manifest with CreateNew (never overwrites) and returns its SHA-256 re-read from disk.
function Write-OwnerOnlyCandidateManifest {
    param([Parameter(Mandatory)][object[]]$Entries, [Parameter(Mandatory)][string]$Path)
    $bytes = ConvertTo-OwnerOnlyCandidateManifestBytes -Entries $Entries
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush() } finally { $stream.Dispose() }
    Get-OwnerOnlyCandidateFileSha256 -Path $Path
}

# Copies one file into the payload. An existing destination must already be byte-identical: no silent overwrite.
function Copy-OwnerOnlyCandidateFile {
    param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Destination, [Parameter(Mandatory)][string]$Origin)
    if ([IO.File]::Exists($Destination)) {
        if ((Get-OwnerOnlyCandidateFileSha256 -Path $Source) -cne (Get-OwnerOnlyCandidateFileSha256 -Path $Destination)) {
            throw "Conflicting payload file from ${Origin}: $Destination differs from an earlier output."
        }
        return $false
    }
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Destination))
    [IO.File]::Copy($Source, $Destination, $false)
    return $true
}

function Invoke-OwnerOnlyCandidateDotnet {
    param([Parameter(Mandatory)][string]$Dotnet, [Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][string[]]$Arguments)
    Write-Host "== $Name"
    & $Dotnet @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet step '$Name' failed with exit code $LASTEXITCODE." }
}

function Get-OwnerOnlyCandidateGitState {
    param([Parameter(Mandatory)][string]$Git, [Parameter(Mandatory)][string]$RepositoryRoot)
    $commit = & $Git @('-C', $RepositoryRoot, 'rev-parse', '--verify', 'HEAD')
    if ($LASTEXITCODE -ne 0 -or [string]$commit -notmatch '\A[0-9a-f]{40}\z') { throw 'Could not read the source commit.' }
    $status = @(& $Git @('-C', $RepositoryRoot, '--no-optional-locks', 'status', '--porcelain=v1', '--untracked-files=all'))
    if ($LASTEXITCODE -ne 0) { throw 'Could not read the source tree status.' }
    [pscustomobject]@{ Commit = [string]$commit; DirtyEntries = @($status | Where-Object { $_ }).Count }
}

function Assert-OwnerOnlyCandidateNonElevatedWindows {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'The owner-only candidate builder requires Windows.' }
    $principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run the owner-only candidate builder from a non-elevated session.' }
}

# Validation in a Windows PowerShell 5.1 process: the installer's own reader, tree check and PE profile, before approval.
function Invoke-OwnerOnlyCandidateValidation {
    param([Parameter(Mandatory)][string]$CandidateRoot, [Parameter(Mandatory)][string]$ExpectedManifestSha256)
    $payload = Join-Path $CandidateRoot 'payload'
    $candidate = Read-OwnerOnlyCandidateManifest -Path (Join-Path $CandidateRoot 'candidate-manifest.json') -ExpectedSha256 $ExpectedManifestSha256 -PayloadRoot $payload
    [PCSteward.Deployment.PayloadIntegrity]::AssertTree($payload, $candidate.Files)
    if ($candidate.PayloadOrigin -cne 'pinned-unsigned-v2' -or $candidate.HelperProtocol -cne '2') { throw 'The candidate is not a protocol-2 pinned-unsigned-v2 payload.' }
    Assert-OwnerOnlyPayloadProfile -Origin $candidate.PayloadOrigin
    $images = foreach ($leaf in @('PCSteward.Desktop.exe', 'PCSteward.ElevatedHelper.exe')) {
        $path = Join-Path $payload $leaf
        if ((Get-AuthenticodeSignature -LiteralPath $path).Status -ne 'NotSigned') { throw "Candidate executable must be unsigned: $leaf" }
        $image = [PCSteward.Deployment.PayloadIntegrity]::GetUnsignedImage($path)
        [ordered]@{ path = $leaf; bytes = $image.bytes; originalChecksum = [long]$image.originalChecksum }
    }
    [long]$total = 0; foreach ($file in $candidate.Files) { $total += $file.bytes }
    [ordered]@{
        validation = 'Pass'; validator = ('Windows PowerShell ' + $PSVersionTable.PSVersion.ToString()); manifestSha256 = $candidate.Sha256
        payloadOrigin = $candidate.PayloadOrigin; helperProtocol = $candidate.HelperProtocol; fileCount = $candidate.Files.Count; totalBytes = $total
        unsignedExecutables = @($images)
    }
}

if ($PSCmdlet.ParameterSetName -eq 'Validate') {
    Assert-OwnerOnlyCandidateNonElevatedWindows
    if ($PSVersionTable.PSEdition -eq 'Core') { throw 'Candidate validation runs under Windows PowerShell 5.1, the installer runtime.' }
    . (Join-Path $PSScriptRoot 'OwnerOnlyPayloadIntegrity.ps1')
    $result = Invoke-OwnerOnlyCandidateValidation -CandidateRoot $ValidateCandidateRoot -ExpectedManifestSha256 $ExpectedManifestSha256
    Write-Output (ConvertTo-Json -InputObject $result -Depth 4 -Compress)
    exit 0
}

# ---- Build ----
Assert-OwnerOnlyCandidateNonElevatedWindows
$repositoryRoot = Split-Path -Parent $PSScriptRoot
foreach ($required in @('global.json', 'PCSteward.slnx')) { if (!(Test-Path -LiteralPath (Join-Path $repositoryRoot $required) -PathType Leaf)) { throw "Repository file is missing: $required" } }
$programRoots = @([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles), [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86), $env:ProgramW6432, $env:ProgramFiles, [Environment]::GetFolderPath([Environment+SpecialFolder]::Windows))
$dataPrefixes = @(); if ($env:LOCALAPPDATA) { $dataPrefixes += (Join-Path $env:LOCALAPPDATA 'PCSteward') }
$OutputRoot = Assert-OwnerOnlyCandidateOutputRootRule -Path $OutputRoot -ForbiddenRoots ($programRoots + @($repositoryRoot)) -ForbiddenPrefixes $dataPrefixes
if (Test-Path -LiteralPath $OutputRoot) { throw "OutputRoot must not exist: $OutputRoot" }
$parent = Split-Path -Parent $OutputRoot
if (!(Test-Path -LiteralPath $parent -PathType Container)) { throw "OutputRoot's parent directory must already exist: $parent" }
for ($ancestor = $parent; $ancestor; $ancestor = Split-Path -Parent $ancestor) {
    if ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "OutputRoot has a reparse-point ancestor (for example OneDrive or a junction): $ancestor" }
}

$globalJson = Get-Content -LiteralPath (Join-Path $repositoryRoot 'global.json') -Raw | ConvertFrom-Json
$dotnet = (Get-Command -Name 'dotnet' -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$git = (Get-Command -Name 'git' -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$windowsPowerShell = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::System)) 'WindowsPowerShell\v1.0\powershell.exe'
if (!(Test-Path -LiteralPath $windowsPowerShell -PathType Leaf)) { throw 'Windows PowerShell 5.1 is required to validate the candidate.' }

$gitBefore = Get-OwnerOnlyCandidateGitState -Git $git -RepositoryRoot $repositoryRoot
if ($gitBefore.DirtyEntries -gt 0 -and !$AllowDirty) { throw "The source tree has $($gitBefore.DirtyEntries) uncommitted change(s). Commit them, or pass -AllowDirty to build and record a dirty candidate." }

$savedEnvironment = @{}
foreach ($name in @('DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_NOLOGO', 'MSBUILDDISABLENODEREUSE')) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
Push-Location -LiteralPath $repositoryRoot
try {
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'; $env:MSBUILDDISABLENODEREUSE = '1'
    # global.json is resolved from the repository working directory.
    $sdkVersion = [string](& $dotnet @('--version') | Select-Object -Last 1)
    if ($LASTEXITCODE -ne 0) { throw 'dotnet --version failed; the pinned SDK from global.json is required.' }
    $sdkExact = Test-OwnerOnlyCandidateSdkVersion -Pinned ([string]$globalJson.sdk.version) -RollForward ([string]$globalJson.sdk.rollForward) -Actual $sdkVersion.Trim()

    New-Item -ItemType Directory -Path $OutputRoot -ErrorAction Stop | Out-Null
    $staging = Join-Path $OutputRoot 'publish-staging'
    $desktopPublish = Join-Path $staging 'desktop'
    $helperPublish = Join-Path $staging 'helper'
    $payload = Join-Path $OutputRoot 'payload'
    New-Item -ItemType Directory -Path $staging, $payload -ErrorAction Stop | Out-Null

    foreach ($step in Get-OwnerOnlyCandidateBuildSteps -RepositoryRoot $repositoryRoot -Configuration $Configuration -DesktopPublishDirectory $desktopPublish -HelperPublishDirectory $helperPublish) {
        Invoke-OwnerOnlyCandidateDotnet -Dotnet $dotnet -Name $step.Name -Arguments $step.Arguments
    }

    # 1. Desktop publish output.
    foreach ($entry in Get-OwnerOnlyCandidateFileEntries -Root $desktopPublish) {
        [void](Copy-OwnerOnlyCandidateFile -Source (Join-Path $desktopPublish $entry.path) -Destination (Join-Path $payload $entry.path) -Origin 'desktop publish')
    }
    # 2. WinUI resources from the desktop RID build: App.xbf, the PRI and one XBF per source XAML (no stale extras).
    $desktopProjectDirectory = Join-Path $repositoryRoot 'src\PCSteward.Desktop'
    $targetFramework = [string]([xml](Get-Content -LiteralPath (Join-Path $desktopProjectDirectory 'PCSteward.Desktop.csproj') -Raw)).Project.PropertyGroup.TargetFramework
    $targetFramework = @($targetFramework.Split(' ') | Where-Object { $_ })[0]
    $winuiOutput = Join-Path $desktopProjectDirectory ('bin\' + $Configuration + '\' + $targetFramework + '\' + $script:CandidateRuntime)
    $expectedXbf = [Collections.Generic.List[string]]::new([string[]](Get-OwnerOnlyCandidateExpectedXbf -ProjectDirectory $desktopProjectDirectory))
    if (!$expectedXbf.Contains('App.xbf')) { throw 'The desktop project has no App.xaml.' }
    $builtResources = Join-Path $winuiOutput 'Resources'
    if (Test-Path -LiteralPath $builtResources) {
        foreach ($built in Get-OwnerOnlyCandidateFileEntries -Root $builtResources) {
            if (!$expectedXbf.Contains('Resources\' + $built.path)) { throw "Unexpected or stale compiled resource in the desktop build output: Resources\$($built.path)" }
        }
    }
    $resourceMerges = 0
    foreach ($resource in @($expectedXbf) + @('PCSteward.Desktop.pri')) {
        $source = Join-Path $winuiOutput $resource
        if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "The desktop RID build is missing a WinUI resource: $resource" }
        if (Copy-OwnerOnlyCandidateFile -Source $source -Destination (Join-Path $payload $resource) -Origin 'WinUI resource build') { $resourceMerges++ }
    }
    # 3. Elevated helper publish output, including the root capability statement.
    foreach ($entry in Get-OwnerOnlyCandidateFileEntries -Root $helperPublish) {
        [void](Copy-OwnerOnlyCandidateFile -Source (Join-Path $helperPublish $entry.path) -Destination (Join-Path $payload $entry.path) -Origin 'helper publish')
    }
    $capabilitySource = Join-Path $repositoryRoot ('src\PCSteward.ElevatedHelper\' + $script:CapabilityLeaf)
    if ((Get-OwnerOnlyCandidateFileSha256 -Path $capabilitySource) -cne (Get-OwnerOnlyCandidateFileSha256 -Path (Join-Path $payload $script:CapabilityLeaf))) { throw 'The payload-root capability statement differs from the checked-in bytes.' }
    foreach ($required in @('PCSteward.Desktop.exe', 'PCSteward.ElevatedHelper.exe', 'PCSteward.ScheduledHost.dll', 'App.xbf', 'PCSteward.Desktop.pri', 'Assets\pc-steward.ico', 'Assets\logo-mark-64.png')) {
        if (!(Test-Path -LiteralPath (Join-Path $payload $required) -PathType Leaf)) { throw "The candidate payload is missing: $required" }
    }
    # Framework-dependent profile, as in the Task 48/49 candidates and the original installer.
    foreach ($runtimeLeaf in @('coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll')) {
        if (Test-Path -LiteralPath (Join-Path $payload $runtimeLeaf)) { throw "The candidate must be framework-dependent, but contains $runtimeLeaf." }
    }

    $entries = Get-OwnerOnlyCandidateFileEntries -Root $payload
    $manifestPath = Join-Path $OutputRoot $script:ManifestLeaf
    $manifestSha256 = Write-OwnerOnlyCandidateManifest -Entries $entries -Path $manifestPath
    [long]$totalBytes = 0; foreach ($entry in $entries) { $totalBytes += $entry.bytes }

    # Self-validation in a fresh Windows PowerShell 5.1 process with the installer's integrity script.
    $validationOutput = @(& $windowsPowerShell @('-NoProfile', '-NonInteractive', '-File', $PSCommandPath, '-ValidateCandidateRoot', $OutputRoot, '-ExpectedManifestSha256', $manifestSha256))
    if ($LASTEXITCODE -ne 0) { throw "Candidate self-validation failed (exit $LASTEXITCODE): $($validationOutput -join ' ')" }
    $validation = ConvertFrom-Json -InputObject ([string]($validationOutput | Where-Object { $_ } | Select-Object -Last 1))
    if ($validation.validation -cne 'Pass' -or $validation.manifestSha256 -cne $manifestSha256 -or $validation.fileCount -ne $entries.Count -or $validation.totalBytes -ne $totalBytes) { throw 'Candidate self-validation disagrees with the written manifest.' }

    $gitAfter = Get-OwnerOnlyCandidateGitState -Git $git -RepositoryRoot $repositoryRoot
    if ($gitAfter.Commit -cne $gitBefore.Commit) { throw 'The source commit changed during the build.' }
    if ($gitAfter.DirtyEntries -gt 0 -and !$AllowDirty) { throw 'The build changed tracked or untracked source files.' }

    $summary = [ordered]@{
        schema = $script:CandidateSchema
        validation = 'Pass'
        payloadOrigin = $validation.payloadOrigin
        helperProtocol = $validation.helperProtocol
        fileCount = $entries.Count
        totalBytes = $totalBytes
        manifestSha256 = $manifestSha256
        manifestPath = $manifestPath
        payloadRoot = $payload
        sourceCommit = $gitBefore.Commit
        sourceDirty = ($gitBefore.DirtyEntries -gt 0 -or $gitAfter.DirtyEntries -gt 0)
        allowDirty = [bool]$AllowDirty
        sdkVersion = $sdkVersion.Trim()
        sdkPinned = [string]$globalJson.sdk.version
        sdkExactPin = $sdkExact
        configuration = $Configuration
        runtimeIdentifier = $script:CandidateRuntime
        selfContained = $false
        resourceMerges = $resourceMerges
        signed = $false
        deterministicBuild = $false
        buildIdentity = 'Built once and pinned by manifestSha256; no deterministic-build claim.'
        unsignedExecutables = @($validation.unsignedExecutables)
        validator = $validation.validator
        builtAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    }
    $summaryJson = ConvertTo-Json -InputObject $summary -Depth 4
    [IO.File]::WriteAllBytes((Join-Path $OutputRoot $script:SummaryLeaf), [Text.UTF8Encoding]::new($false).GetBytes($summaryJson))
    Write-Output $summaryJson
}
finally {
    Pop-Location
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
}
