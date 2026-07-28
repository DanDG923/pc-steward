param([string]$ResultPath)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:Assertions++
}

function Assert-Throws([scriptblock]$Action, [string]$Message) {
    $thrown = $false
    try { & $Action } catch { $thrown = $true }
    Assert-True $thrown $Message
}

function Invoke-DiskPart([string[]]$Commands) {
    $text = ($Commands + 'exit') -join [Environment]::NewLine
    $output = $text | & diskpart.exe 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "DiskPart failed with exit code $LASTEXITCODE. Output: $($output | Out-String)"
    }
}

$principal = [Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'This isolated scratch-VHD test must be run elevated; it does not touch existing disks or files.'
}

Add-Type -Path (Join-Path $PSScriptRoot '..\SecurityBoundarySpike.cs')

$script:Assertions = 0
$container = Join-Path ([IO.Path]::GetTempPath()) ('pc-steward-host-evidence-' + [guid]::NewGuid().ToString('N'))
$root = Join-Path $container 'owned'
$outside = Join-Path $container 'outside'
$vhd = Join-Path $container 'scratch.vhd'
$mount = Join-Path $root 'downloads\mounted-vhd'
$vhdAttached = $false
$reparsePaths = [Collections.Generic.List[string]]::new()
[IO.Directory]::CreateDirectory($root) | Out-Null
[IO.Directory]::CreateDirectory($outside) | Out-Null

try {
    $outsideSentinel = Join-Path $outside 'must-survive.txt'
    [IO.File]::WriteAllText($outsideSentinel, 'outside')

    $settings = [PcSteward.SecurityBoundarySpike.SafeDeletion]::GetCategoryPath(
        $root, [PcSteward.SecurityBoundarySpike.ResetCategory]::Settings)
    [IO.Directory]::CreateDirectory($settings) | Out-Null
    $fileLink = Join-Path $settings 'file-link'
    $directoryLink = Join-Path $settings 'directory-link'
    New-Item -ItemType SymbolicLink -Path $fileLink -Target $outsideSentinel | Out-Null
    New-Item -ItemType SymbolicLink -Path $directoryLink -Target $outside | Out-Null
    $reparsePaths.Add($fileLink)
    $reparsePaths.Add($directoryLink)
    Assert-Throws {
        [PcSteward.SecurityBoundarySpike.SafeDeletion]::ValidateCandidate($root, $fileLink)
    } 'A file symbolic link must be rejected.'
    Assert-Throws {
        [PcSteward.SecurityBoundarySpike.SafeDeletion]::ValidateCandidate($root, $directoryLink)
    } 'A directory symbolic link must be rejected.'
    $linkReport = [PcSteward.SecurityBoundarySpike.SafeDeletion]::DeleteCategory(
        $root, [PcSteward.SecurityBoundarySpike.ResetCategory]::Settings)
    Assert-True ($linkReport.Outcome -eq [PcSteward.SecurityBoundarySpike.DeletionOutcome]::Partial) 'Symbolic links must fail closed with Partial.'
    Assert-True ([IO.File]::Exists($outsideSentinel)) 'Symbolic-link rejection must preserve the outside sentinel.'

    $scan = [PcSteward.SecurityBoundarySpike.SafeDeletion]::GetCategoryPath(
        $root, [PcSteward.SecurityBoundarySpike.ResetCategory]::ScanData)
    $raceLeaf = Join-Path $scan 'race-leaf'
    [IO.Directory]::CreateDirectory($raceLeaf) | Out-Null
    $raceState = [pscustomobject]@{ Swapped = $false }
    $raceHook = [Action[string]] {
        param([string]$Path)
        if (-not $raceState.Swapped -and $Path -eq $raceLeaf) {
            [IO.Directory]::Delete($raceLeaf)
            New-Item -ItemType Junction -Path $raceLeaf -Target $outside | Out-Null
            $reparsePaths.Add($raceLeaf)
            $raceState.Swapped = $true
        }
    }
    $raceReport = [PcSteward.SecurityBoundarySpike.SafeDeletion]::DeleteCategory(
        $root, [PcSteward.SecurityBoundarySpike.ResetCategory]::ScanData, 0, 0, $raceHook)
    Assert-True $raceState.Swapped 'The deterministic race fixture must swap after validation and before the handle open.'
    Assert-True ($raceReport.Outcome -eq [PcSteward.SecurityBoundarySpike.DeletionOutcome]::Partial) 'A swapped reparse point must fail closed.'
    Assert-True ([IO.File]::Exists($outsideSentinel)) 'The race fixture must preserve the outside sentinel.'

    [IO.Directory]::CreateDirectory($mount) | Out-Null
    Invoke-DiskPart @(
        "create vdisk file=`"$vhd`" maximum=32 type=expandable",
        "select vdisk file=`"$vhd`"",
        'attach vdisk',
        'create partition primary',
        'format fs=ntfs quick label=PCStewardEvidence',
        "assign mount=`"$mount`""
    )
    $vhdAttached = $true
    $vhdSentinel = Join-Path $mount 'must-survive-vhd.txt'
    [IO.File]::WriteAllText($vhdSentinel, 'scratch-vhd')
    Assert-Throws {
        [PcSteward.SecurityBoundarySpike.SafeDeletion]::ValidateCandidate($root, $mount)
    } 'A volume mount point must be rejected as a reparse component.'
    $mountReport = [PcSteward.SecurityBoundarySpike.SafeDeletion]::DeleteCategory(
        $root, [PcSteward.SecurityBoundarySpike.ResetCategory]::Downloads)
    Assert-True ($mountReport.Outcome -eq [PcSteward.SecurityBoundarySpike.DeletionOutcome]::Partial) 'A volume mount point must fail closed with Partial.'
    Assert-True ([IO.File]::Exists($vhdSentinel)) 'The scratch VHD sentinel must survive rejection.'

    $result = "PASS: $Assertions assertions; file/directory links, deterministic race, and scratch VHD mount were rejected; sentinels survived."
    Write-Output $result
    if ($ResultPath) { [IO.File]::WriteAllText($ResultPath, $result) }
}
catch {
    if ($ResultPath) { [IO.File]::WriteAllText($ResultPath, ($_ | Out-String)) }
    throw
}
finally {
    foreach ($path in $reparsePaths) {
        try {
            if (Test-Path -LiteralPath $path) {
                Remove-Item -LiteralPath $path -Force -ErrorAction Stop
            }
        } catch {}
    }
    if ($vhdAttached -and [IO.File]::Exists($vhd)) {
        try { Invoke-DiskPart @("select vdisk file=`"$vhd`"", 'detach vdisk') } catch {}
    }
    if ([IO.Directory]::Exists($container)) {
        $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        $resolvedContainer = [IO.Path]::GetFullPath($container)
        if ($resolvedContainer.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase) -and
            $resolvedContainer -ne $resolvedTemp) {
            Remove-Item -LiteralPath $resolvedContainer -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
