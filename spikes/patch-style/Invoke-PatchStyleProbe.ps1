#Requires -Version 7.0
<#
.SYNOPSIS
    Task 45 owner-run, READ-ONLY probe for the Patch-style spike.

.DESCRIPTION
    Captures the WinGet and Chocolatey facts that the Linux spike environment could not observe.
    It never installs, upgrades, uninstalls, pins, accepts agreements, adds or changes sources,
    or writes anywhere except the output folder.

    Steps:
      1. WinGet/Chocolatey versions.
      2. `winget show --exact` for three sample IDs (agreement and installer sections).
      3. `choco search --exact --limit-output` (with and without --include-headers on 2.5.0+) and
         `choco info` against the fixed Community feed for three sample IDs.
      4. The compiled probe: `winget-preflight` over the sample catalog, and `detect-db` against the
         local PC Steward database (opened read-only). Only catalog IDs and counts are written.

    Review the output folder before sharing or committing anything from it.

.EXAMPLE
    pwsh -NoProfile -File .\spikes\patch-style\Invoke-PatchStyleProbe.ps1 -OutputPath .\spikes\patch-style\results
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $OutputPath,
    [string] $CatalogPath = (Join-Path $PSScriptRoot 'fixtures/sample-catalog.v1.json'),
    [string] $DatabasePath = (Join-Path $env:LOCALAPPDATA 'PCSteward/pcsteward.db')
)

$ErrorActionPreference = 'Stop'
$communityFeed = 'https://community.chocolatey.org/api/v2/'
$wingetSamples = @('Notepad++.Notepad++', 'Mozilla.Firefox', 'Git.Git')
$chocolateySamples = @('notepadplusplus', 'firefox', 'git')
New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null

function Invoke-Captured {
    param([string] $Name, [string] $FilePath, [string[]] $Arguments)
    $target = Join-Path $OutputPath "$Name.txt"
    $command = Get-Command $FilePath -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $command) { "not found: $FilePath" | Set-Content -LiteralPath $target; return }
    $output = & $command.Source @Arguments 2>&1 | Out-String
    "exit=$LASTEXITCODE`nargs=$($Arguments -join ' ')`n$output" | Set-Content -LiteralPath $target
}

Invoke-Captured 'winget-version' 'winget' @('--version')
foreach ($id in $wingetSamples) {
    Invoke-Captured "winget-show-$id" 'winget' @('show', '--id', $id, '--exact', '--source', 'winget', '--disable-interactivity')
}

Invoke-Captured 'choco-version' 'choco' @('--version', '--no-color')
foreach ($id in $chocolateySamples) {
    Invoke-Captured "choco-search-$id" 'choco' @('search', $id, '--exact', '--limit-output', '--source', $communityFeed, '--no-color')
    Invoke-Captured "choco-search-headers-$id" 'choco' @('search', $id, '--exact', '--limit-output', '--include-headers', '--source', $communityFeed, '--no-color')
    Invoke-Captured "choco-search-all-versions-$id" 'choco' @('search', $id, '--exact', '--all-versions', '--limit-output', '--source', $communityFeed, '--no-color')
    Invoke-Captured "choco-info-$id" 'choco' @('info', $id, '--source', $communityFeed, '--no-color')
}

$probeProject = Join-Path $PSScriptRoot 'probe/PatchStyle.Probe.csproj'
dotnet build $probeProject -c Release | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Probe build failed.' }
dotnet run --project $probeProject -c Release --no-build -- winget-preflight $CatalogPath (Join-Path $OutputPath 'winget-preflight.json')
if (Test-Path -LiteralPath $DatabasePath) {
    dotnet run --project $probeProject -c Release --no-build -- detect-db $CatalogPath $DatabasePath (Join-Path $OutputPath 'detect-db.json')
} else {
    "PC Steward database not found at the default path; detect-db skipped." | Set-Content -LiteralPath (Join-Path $OutputPath 'detect-db.txt')
}

Write-Host "Read-only probe complete. Review $OutputPath before sharing."
