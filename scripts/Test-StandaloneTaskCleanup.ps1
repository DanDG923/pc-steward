[CmdletBinding()]
param(
    [string]$PayloadPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\private-beta\payload'),
    [switch]$ContractOnly
)

$ErrorActionPreference = 'Stop'
$taskTool = Join-Path $env:SystemRoot 'System32\schtasks.exe'
$primaryTask = 'PCSteward.ScheduledCheck'
$sentinelTask = 'PCSteward.StandaloneEvidenceSentinel'
$cleanupScript = Join-Path $PayloadPath 'Remove-PCSteward.ps1'
$sourceCleanupScript = Join-Path (Split-Path -Parent $PSScriptRoot) 'packaging\Remove-PCSteward.ps1'

function Assert-Contract([bool]$Condition, [string]$Message)
{
    if (!$Condition) { throw "Standalone cleanup contract failure: $Message" }
}

function New-FakeTaskSchedulerAdapter([scriptblock]$GetExactTask, [scriptblock]$DeleteExactTask)
{
    return [pscustomobject]@{
        GetExactTask = $GetExactTask
        DeleteExactTask = $DeleteExactTask
    }
}

function Invoke-Cleanup([object]$Adapter, [bool]$ExpectFailure)
{
    $failed = $false
    $output = ''
    try
    {
        $output = (& {
            . $sourceCleanupScript
            function Get-ExactScheduledTask([string]$Name) { return & $Adapter.GetExactTask $Name }
            function Remove-ExactScheduledTask([string]$Name) { & $Adapter.DeleteExactTask $Name }
            Invoke-PCStewardTaskCleanup
        } 6>&1 | Out-String)
    }
    catch { $failed = $true; $output += $_.Exception.Message }

    Assert-Contract ($failed -eq $ExpectFailure) "Unexpected success state; expected failure: $ExpectFailure."
    if ($ExpectFailure) { Assert-Contract (!$output.Contains('PC Steward scheduled-task cleanup is complete.')) 'Cleanup reported Success before its required verification.' }
    else { Assert-Contract ($output.Contains('PC Steward scheduled-task cleanup is complete.')) 'Cleanup did not report Success after exact absence verification.' }
    return $output
}

function Invoke-CleanupContractTests
{
    $absent = [pscustomobject]@{ Queries = 0; Deletes = 0; Names = [System.Collections.Generic.List[string]]::new() }
    $absentAdapter = New-FakeTaskSchedulerAdapter {
        param($name) $absent.Queries++; $absent.Names.Add($name); return $null
    } {
        param($name) $absent.Deletes++; $absent.Names.Add($name)
    }
    $null = Invoke-Cleanup $absentAdapter $false
    Assert-Contract ($absent.Queries -eq 1 -and $absent.Deletes -eq 0) 'Exact absence did not remain idempotent.'
    Assert-Contract (@($absent.Names | Where-Object { $_ -ne $primaryTask }).Count -eq 0) 'Cleanup queried an unrelated task.'

    $present = [pscustomobject]@{ Queries = 0; Deletes = 0; Names = [System.Collections.Generic.List[string]]::new(); Task = [pscustomobject]@{ Name = $primaryTask } }
    $presentAdapter = New-FakeTaskSchedulerAdapter {
        param($name) $present.Queries++; $present.Names.Add($name); if ($present.Queries -eq 1) { return $present.Task }; return $null
    } {
        param($name) $present.Deletes++; $present.Names.Add($name)
    }
    $null = Invoke-Cleanup $presentAdapter $false
    Assert-Contract ($present.Queries -eq 2 -and $present.Deletes -eq 1) 'Present task was not queried, deleted, and re-queried exactly once.'
    Assert-Contract (@($present.Names | Where-Object { $_ -ne $primaryTask }).Count -eq 0) 'Cleanup touched an unrelated task.'

    $queryFailure = New-FakeTaskSchedulerAdapter { param($name) throw 'Simulated exact-task query failure.' } { param($name) throw 'Delete must not run after query failure.' }
    $null = Invoke-Cleanup $queryFailure $true

    $deleteFailure = New-FakeTaskSchedulerAdapter { param($name) return [pscustomobject]@{ Name = $primaryTask } } { param($name) throw 'Simulated deletion failure.' }
    $null = Invoke-Cleanup $deleteFailure $true

    $residual = [pscustomobject]@{ Deletes = 0; Task = [pscustomobject]@{ Name = $primaryTask } }
    $residualAdapter = New-FakeTaskSchedulerAdapter { param($name) return $residual.Task } { param($name) $residual.Deletes++ }
    $null = Invoke-Cleanup $residualAdapter $true
    Assert-Contract ($residual.Deletes -eq 1) 'Residual task was not given one exact deletion attempt.'
}

function Invoke-ExactAbsenceClassificationTests
{
    $missingTask = [System.Runtime.InteropServices.COMException]::new('Simulated missing task.', [int]0x80070002)
    $setupFailed = $false
    try
    {
        & {
            . $sourceCleanupScript
            function Get-TaskSchedulerRootFolder { throw $missingTask }
            Get-ExactScheduledTask $primaryTask
        }
    }
    catch { $setupFailed = $true }
    Assert-Contract $setupFailed 'A setup-stage FileNotFound HRESULT was incorrectly treated as task absence.'

    $exactLookupResult = & {
        . $sourceCleanupScript
        function Get-TaskSchedulerRootFolder { return [pscustomobject]@{} }
        function Find-ExactScheduledTask([object]$Folder, [string]$Name) { throw $missingTask }
        return Get-ExactScheduledTask $primaryTask
    }
    Assert-Contract ($null -eq $exactLookupResult) 'An exact GetTask FileNotFound HRESULT was not treated as task absence.'
}

if (!(Test-Path -LiteralPath $sourceCleanupScript)) { throw "Source cleanup script was not found: $sourceCleanupScript" }
Invoke-CleanupContractTests
Invoke-ExactAbsenceClassificationTests
if ($ContractOnly) { return }

function Test-TaskExists([string]$Name)
{
    & $taskTool /Query /TN $Name *> $null
    return $LASTEXITCODE -eq 0
}

foreach ($taskName in @($primaryTask, $sentinelTask))
{
    if (Test-TaskExists $taskName) { throw "Refusing to alter an existing task: $taskName" }
}
if (!(Test-Path -LiteralPath $cleanupScript)) { throw "Standalone cleanup script was not found: $cleanupScript" }

try
{
    foreach ($taskName in @($primaryTask, $sentinelTask))
    {
        & $taskTool /Create /TN $taskName /TR "$env:ComSpec /c exit 0" /SC ONCE /ST 23:59 /F | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Could not create evidence task: $taskName" }
    }

    & $cleanupScript
    if (Test-TaskExists $primaryTask) { throw 'Standalone cleanup did not remove PCSteward.ScheduledCheck.' }
    if (!(Test-TaskExists $sentinelTask)) { throw 'Standalone cleanup removed the unrelated sentinel task.' }

    [ordered]@{
        primaryTaskRemoved = $true
        unrelatedTaskRetained = $true
        cleanupScript = $cleanupScript
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PayloadPath '..\reports\task-removal-isolation.json') -Encoding utf8
}
finally
{
    foreach ($taskName in @($primaryTask, $sentinelTask))
    {
        if (Test-TaskExists $taskName) { & $taskTool /Delete /TN $taskName /F | Out-Null }
    }
}
