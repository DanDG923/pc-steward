[CmdletBinding()]
param([string]$OutputPath = (Join-Path $PSScriptRoot 'baseline-system.json'))

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$os = Get-CimInstance Win32_OperatingSystem
$computer = Get-CimInstance Win32_ComputerSystem
$capture = [ordered]@{
    schemaVersion = 1
    capturedUtc = [datetime]::UtcNow.ToString('o')
    redaction = 'Host name, user name, serial numbers, network identifiers and absolute paths intentionally omitted.'
    os = @{ caption = $os.Caption; version = $os.Version; build = $os.BuildNumber; architecture = $os.OSArchitecture }
    hardware = @{ logicalProcessors = $computer.NumberOfLogicalProcessors; memoryBytes = [int64]$computer.TotalPhysicalMemory }
    runtime = @{ powershell = $PSVersionTable.PSVersion.ToString(); dotnetSdks = @(& dotnet --list-sdks 2>$null | ForEach-Object { ($_ -split '\s+')[0] }) }
    powerPlan = @(& powercfg /GETACTIVESCHEME 2>$null | ForEach-Object { $_ -replace '[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}', '<redacted-guid>' })
    storage = 'Unverified by this redacted capture; acceptance requires SSD-equivalent storage evidence on the agreed Windows 11 baseline.'
}
$capture | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputPath -Encoding utf8NoBOM
Write-Output $OutputPath
