param(
    [Parameter(Mandatory)]
    [ValidateSet('InstallClean', 'InstallTampered', 'InstallWrongSigner', 'InstallWritable', 'InstallReparse', 'WaitReplace', 'Teardown')]
    [string]$Action,
    [Parameter(Mandatory)]
    [ValidatePattern('^[a-f0-9]{32}$')]
    [string]$RunId,
    [string]$DesktopStage,
    [string]$HelperStage,
    [string]$ReplacementHelper,
    [string]$RequestPath,
    [string]$CompletedPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'The evidence administrator step requires elevation.'
}

$programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$installedRoot = [IO.Path]::GetFullPath((Join-Path $programFiles 'PC Steward'))
$expectedRoot = [IO.Path]::GetFullPath((Join-Path $programFiles 'PC Steward'))
if (-not $installedRoot.Equals($expectedRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The fixed evidence root did not resolve exactly.'
}

$marker = Join-Path $installedRoot '.pcsteward-installed-helper-evidence'
$helper = Join-Path $installedRoot 'PCSteward.ElevatedHelper.exe'
$started = Join-Path $installedRoot '.pcsteward-evidence-helper-started'

function Assert-OwnedRoot {
    if (-not [IO.Directory]::Exists($installedRoot) -or
        -not [IO.File]::Exists($marker) -or
        -not ([IO.File]::ReadAllText($marker)).Equals($RunId, [StringComparison]::Ordinal)) {
        throw 'The fixed root is absent or is not owned by this evidence run.'
    }
}

function Grant-AdministratorControl {
    if ([IO.Directory]::Exists($installedRoot)) {
        & icacls.exe $installedRoot /grant '*S-1-5-32-544:(OI)(CI)F' /T /C | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Unable to grant teardown access to the evidence root.' }
    }
}

function Protect-Root {
    & icacls.exe $installedRoot /inheritance:r | Out-Null
    & icacls.exe $installedRoot /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-32-545:(OI)(CI)RX' /T /C | Out-Null
    & icacls.exe $installedRoot /setowner '*S-1-5-32-544' /T /C | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Unable to protect the evidence root.' }
}

function Reset-CleanLayout {
    if ([IO.Directory]::Exists($installedRoot)) {
        Assert-OwnedRoot
        Grant-AdministratorControl
        Remove-Item -LiteralPath $installedRoot -Recurse -Force
    }

    if (-not [IO.Directory]::Exists($DesktopStage) -or -not [IO.Directory]::Exists($HelperStage)) {
        throw 'The signed fixture stages are missing.'
    }

    [IO.Directory]::CreateDirectory($installedRoot) | Out-Null
    Copy-Item -Path (Join-Path $DesktopStage '*') -Destination $installedRoot -Recurse -Force
    Copy-Item -Path (Join-Path $HelperStage '*') -Destination $installedRoot -Recurse -Force
    [IO.File]::WriteAllText($marker, $RunId)
    Protect-Root
}

switch ($Action) {
    'InstallClean' {
        Reset-CleanLayout
    }
    'InstallTampered' {
        Reset-CleanLayout
        if (-not [IO.File]::Exists($ReplacementHelper)) { throw 'The tampered replacement helper is missing.' }
        Copy-Item -LiteralPath $ReplacementHelper -Destination $helper -Force
        Protect-Root
    }
    'InstallWrongSigner' {
        Reset-CleanLayout
        if (-not [IO.File]::Exists($ReplacementHelper)) { throw 'The wrong-signer helper is missing.' }
        Copy-Item -LiteralPath $ReplacementHelper -Destination $helper -Force
        Protect-Root
    }
    'InstallWritable' {
        Reset-CleanLayout
        & icacls.exe $installedRoot /grant '*S-1-5-32-545:(OI)(CI)M' /T /C | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Unable to create the caller-writable hostile layout.' }
    }
    'InstallReparse' {
        Reset-CleanLayout
        $target = Join-Path $installedRoot 'PCSteward.ElevatedHelper.EvidenceTarget.exe'
        Move-Item -LiteralPath $helper -Destination $target -Force
        New-Item -ItemType SymbolicLink -Path $helper -Target $target | Out-Null
        Protect-Root
    }
    'WaitReplace' {
        Assert-OwnedRoot
        if (-not [IO.File]::Exists($ReplacementHelper)) { throw 'The replacement helper is missing.' }
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(35)
        while (-not [IO.File]::Exists($RequestPath) -and [DateTimeOffset]::UtcNow -lt $deadline) {
            Start-Sleep -Milliseconds 50
        }
        if (-not [IO.File]::Exists($RequestPath)) { throw 'The replacement request was not received.' }
        Copy-Item -LiteralPath $ReplacementHelper -Destination $helper -Force
        Protect-Root
        [IO.File]::WriteAllText($CompletedPath, 'replaced')
    }
    'Teardown' {
        if ([IO.Directory]::Exists($installedRoot)) {
            Assert-OwnedRoot
            Grant-AdministratorControl
            Remove-Item -LiteralPath $installedRoot -Recurse -Force
        }
    }
}
