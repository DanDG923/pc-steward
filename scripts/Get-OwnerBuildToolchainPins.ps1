[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Worktree,
    [Parameter(Mandatory)][string]$SourceCommit,
    [Parameter(Mandatory)][string]$OutputPath,
    [Parameter(Mandatory)][string]$DownloadFolder
)

# Task 50 CB3.7c pins (CI only). From a clean worktree of the owner's build commit, on a GitHub-hosted runner with the pinned
# SDK: look up the official SDK zip for global.json's version in Microsoft's release metadata, download it and check its
# SHA-512, then restore the builder's three projects for win-x64 into an empty NuGet folder and record every package (and
# download-only pack) with its SHA-512. scripts/Test-OwnerBuildToolchain.ps1 checks the owner's toolchain against this file.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$projects = @('src/PCSteward.ScheduledHost/PCSteward.ScheduledHost.csproj', 'src/PCSteward.Desktop/PCSteward.Desktop.csproj', 'src/PCSteward.ElevatedHelper/PCSteward.ElevatedHelper.csproj')
$head = [string](& git -C $Worktree rev-parse --verify HEAD)
if ($head -cne $SourceCommit) { throw "The worktree is at $head, not $SourceCommit." }
$sdkVersion = [string]([IO.File]::ReadAllText((Join-Path $Worktree 'global.json')) | ConvertFrom-Json).sdk.version
Push-Location -LiteralPath $Worktree
try { $actual = ([string](& dotnet --version | Select-Object -Last 1)).Trim() } finally { Pop-Location }
if ($actual -cne $sdkVersion) { throw "dotnet --version is $actual, not the pinned $sdkVersion." }
if (!$env:NUGET_PACKAGES) { throw 'Set NUGET_PACKAGES to an empty folder first.' }
$packagesRoot = $env:NUGET_PACKAGES.TrimEnd('\', '/')

# Official SDK zip.
$channel = ($sdkVersion.Split('.')[0..1] -join '.')
$metadata = Invoke-RestMethod -Uri "https://builds.dotnet.microsoft.com/dotnet/release-metadata/$channel/releases.json"
$sdk = @($metadata.releases | ForEach-Object { @($_.sdks) + @($_.sdk) } | Where-Object { $_ -and $_.version -ceq $sdkVersion } | Select-Object -First 1)
if ($sdk.Count -ne 1) { throw "SDK $sdkVersion is not in the $channel release metadata." }
$file = @($sdk[0].files | Where-Object { $_.rid -ceq 'win-x64' -and $_.name -like '*.zip' })
if ($file.Count -ne 1) { throw 'Expected exactly one win-x64 SDK zip.' }
$zipName = "dotnet-sdk-$sdkVersion-win-x64.zip"
New-Item -ItemType Directory -Path $DownloadFolder -Force | Out-Null
$zipPath = Join-Path $DownloadFolder $zipName
Invoke-WebRequest -Uri ([string]$file[0].url) -OutFile $zipPath -UseBasicParsing
$sha = [Security.Cryptography.SHA512]::Create(); $stream = [IO.File]::OpenRead($zipPath)
try { $zipHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() } finally { $stream.Dispose(); $sha.Dispose() }
if ($zipHash -cne ([string]$file[0].hash).ToLowerInvariant()) { throw 'The downloaded SDK zip does not match the release metadata hash.' }

# Packages restored exactly as the builder restores them.
$packages = @{}
foreach ($project in $projects) {
    & dotnet restore (Join-Path $Worktree $project) --runtime win-x64 | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed for $project." }
    $assets = [IO.File]::ReadAllText((Join-Path (Join-Path $Worktree (Split-Path -Parent $project)) 'obj/project.assets.json')) | ConvertFrom-Json
    $found = @()
    foreach ($library in $assets.libraries.PSObject.Properties) {
        if ($library.Value.type -cne 'package') { continue }
        $slash = $library.Name.IndexOf('/')
        $found += [pscustomobject]@{ Id = $library.Name.Substring(0, $slash); Version = $library.Name.Substring($slash + 1); Kind = 'library'; Sha512 = [string]$library.Value.sha512 }
    }
    foreach ($framework in $assets.project.frameworks.PSObject.Properties) {
        foreach ($download in @($framework.Value.downloadDependencies)) {
            if (!$download) { continue }
            $found += [pscustomobject]@{ Id = [string]$download.name; Version = ([string]$download.version).Trim('[', ']', ' ').Split(',')[0].Trim(); Kind = 'download'; Sha512 = '' }
        }
    }
    foreach ($p in $found) {
        $id = $p.Id.ToLowerInvariant(); $version = $p.Version.ToLowerInvariant()
        $nupkg = Join-Path (Join-Path (Join-Path $packagesRoot $id) $version) "$id.$version.nupkg"
        $hasher = [Security.Cryptography.SHA512]::Create(); $s = [IO.File]::OpenRead($nupkg)
        try { $computed = [Convert]::ToBase64String($hasher.ComputeHash($s)) } finally { $s.Dispose(); $hasher.Dispose() }
        if ($p.Sha512 -and $p.Sha512 -cne $computed) { throw "$id $version does not match its restore record." }
        $key = "$id/$version"
        if (!$packages.ContainsKey($key)) { $packages[$key] = [ordered]@{ id = $p.Id; version = $p.Version; kind = $p.Kind; sha512 = $computed; projects = @() } }
        if ($packages[$key].sha512 -cne $computed) { throw "$key has two hashes." }
        if ($packages[$key].projects -notcontains $project) { $packages[$key].projects += $project }
    }
}

$document = [ordered]@{
    schema = 'pc-steward-owner-build-toolchain-pins:1'
    sourceCommit = $SourceCommit
    sdkVersion = $sdkVersion
    sdkZip = [ordered]@{ name = $zipName; url = [string]$file[0].url; sha512 = $zipHash; bytes = (Get-Item -LiteralPath $zipPath).Length }
    projects = $projects
    packages = @($packages.Keys | Sort-Object | ForEach-Object { $packages[$_] })
}
New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) -Force | Out-Null
[IO.File]::WriteAllText($OutputPath, (($document | ConvertTo-Json -Depth 6) -replace "`r`n", "`n") + "`n", [Text.UTF8Encoding]::new($false))
Write-Output "PINS sdk=$sdkVersion zip=$zipName packages=$($document.packages.Count) file=$OutputPath"
