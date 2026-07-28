[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$FixturePath,
    [ValidateSet('local', 'online', 'cancellable')][string]$Mode = 'local',
    [string]$Connector = 'local',
    [int]$DelayMilliseconds = 60,
    [string]$CancelFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$document = Get-Content -Raw -LiteralPath $FixturePath | ConvertFrom-Json
$checksum = 17
foreach ($record in $document.records) {
    $checksum = (($checksum * 31) + $record.fixtureId.Length + $record.displayName.Length) % 2147483647
}

if ($Mode -in @('online', 'cancellable')) {
    $remaining = [Math]::Max(0, $DelayMilliseconds)
    while ($remaining -gt 0) {
        if ($CancelFile -and (Test-Path -LiteralPath $CancelFile -PathType Leaf)) {
            [pscustomobject]@{ status = 'cancelled'; connector = $Connector; recordsProcessed = $document.records.Count; checksum = $checksum } | ConvertTo-Json -Compress
            exit 0
        }
        Start-Sleep -Milliseconds ([Math]::Min(10, $remaining))
        $remaining -= 10
    }
}

[pscustomobject]@{ status = 'completed'; connector = $Connector; recordsProcessed = $document.records.Count; checksum = $checksum } | ConvertTo-Json -Compress
