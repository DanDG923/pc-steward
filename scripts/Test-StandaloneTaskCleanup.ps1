[CmdletBinding()]
param(
    [string]$PayloadPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\private-beta\payload')
)

$ErrorActionPreference = 'Stop'
$taskTool = Join-Path $env:SystemRoot 'System32\schtasks.exe'
$primaryTask = 'PCSteward.ScheduledCheck'
$sentinelTask = 'PCSteward.StandaloneEvidenceSentinel'
$cleanupScript = Join-Path $PayloadPath 'Remove-PCSteward.ps1'

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
