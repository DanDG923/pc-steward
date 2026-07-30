[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$taskName = 'PCSteward.ScheduledCheck'
$running = Get-Process -Name 'PCSteward.Desktop','PCSteward.ScheduledHost' -ErrorAction SilentlyContinue
if ($running)
{
    throw 'Close PC Steward and wait for any scheduled scan to finish before removing the standalone package.'
}

& (Join-Path $env:SystemRoot 'System32\schtasks.exe') /Query /TN $taskName *> $null
if ($LASTEXITCODE -eq 0)
{
    & (Join-Path $env:SystemRoot 'System32\schtasks.exe') /Delete /TN $taskName /F
    if ($LASTEXITCODE -ne 0) { throw 'The PC Steward scheduled task could not be removed.' }
}

Write-Host 'PC Steward scheduled-task cleanup is complete.'
Write-Host 'Delete this extracted package folder to remove the binaries.'
Write-Host 'To remove per-user data, run Factory reset inside PC Steward before closing it.'
