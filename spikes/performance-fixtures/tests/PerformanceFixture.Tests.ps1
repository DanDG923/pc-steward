$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path $root 'fixtures\provider-records.json'
& (Join-Path $root 'New-PerformanceFixtures.ps1') -OutputPath $fixture | Out-Null
$document = Get-Content -Raw -LiteralPath $fixture | ConvertFrom-Json

function Assert-True([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
Assert-True ($document.records.Count -ge 300) 'Fixture matrix must contain at least 300 provider records.'
foreach ($case in 'normal', 'warning', 'deduplicate', 'separate-instance', 'record-unknown-version') {
    Assert-True (($document.records.expectedInventory -contains $case) -or ($case -eq 'normal')) "Expected inventory case missing: $case"
}
foreach ($case in 'exact', 'runtime-policy', 'channel-conflict', 'incomparable-version', 'false-match-reject', 'architecture-conflict', 'unmatched') {
    Assert-True ($document.records.expectedMapping -contains $case) "Expected mapping case missing: $case"
}
Assert-True (($document.records | Where-Object fixtureId -eq 'EDGE-003').duplicateGroup -eq 'raw-duplicate-1') 'Duplicate raw-record fixture missing.'
Assert-True (($document.mappingCases | Where-Object expectedActionable -eq $false).Count -ge 4) 'False-match and unsafe mapping controls must be non-actionable.'
Assert-True (($document.records | Where-Object kind -eq 'runtime').Count -ge 1) 'Runtime fixture missing.'
Write-Output 'PASS: 14 assertions'
