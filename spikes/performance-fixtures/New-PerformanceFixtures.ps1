[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$records = [System.Collections.Generic.List[object]]::new()
for ($number = 1; $number -le 300; $number++) {
    $scope = if ($number % 2) { 'machine' } else { 'user' }
    $architecture = if ($number % 3) { 'x64' } else { 'x86' }
    $kind = if ($number % 25 -eq 0) { 'runtime' } else { 'application' }
    $records.Add([ordered]@{
        fixtureId = ('FIX-{0:D4}' -f $number)
        provider = if ($scope -eq 'machine') { 'registry-machine' } else { 'registry-user' }
        displayName = ('Contoso Fixture Application {0:D3}' -f $number)
        version = ('{0}.{1}.{2}' -f (1 + ($number % 4)), ($number % 10), ($number % 100))
        scope = $scope
        architecture = $architecture
        kind = $kind
        channel = 'stable'
        instanceFamily = ('contoso-{0:D3}' -f $number)
        duplicateGroup = $null
        expectedInventory = 'record'
        expectedMapping = 'exact'
    })
}

$edgeCases = @(
    @{ fixtureId = 'EDGE-001'; provider = 'registry-machine'; displayName = 'Malformed missing name'; version = '1.0'; scope = 'machine'; architecture = 'x64'; kind = 'application'; channel = 'stable'; instanceFamily = 'malformed-name'; duplicateGroup = $null; expectedInventory = 'warning'; expectedMapping = 'unmatched' },
    @{ fixtureId = 'EDGE-002'; provider = 'registry-user'; displayName = 'Missing version'; version = $null; scope = 'user'; architecture = 'x64'; kind = 'application'; channel = 'stable'; instanceFamily = 'missing-version'; duplicateGroup = $null; expectedInventory = 'record-unknown-version'; expectedMapping = 'review' },
    @{ fixtureId = 'EDGE-003'; provider = 'registry-machine'; displayName = 'Duplicate Raw App'; version = '2.1.0'; scope = 'machine'; architecture = 'x64'; kind = 'application'; channel = 'stable'; instanceFamily = 'duplicate-raw-app'; duplicateGroup = 'raw-duplicate-1'; expectedInventory = 'deduplicate'; expectedMapping = 'exact' },
    @{ fixtureId = 'EDGE-004'; provider = 'registry-machine'; displayName = 'Duplicate Raw App'; version = '2.1.0'; scope = 'machine'; architecture = 'x64'; kind = 'application'; channel = 'stable'; instanceFamily = 'duplicate-raw-app'; duplicateGroup = 'raw-duplicate-1'; expectedInventory = 'deduplicate'; expectedMapping = 'exact' },
    @{ fixtureId = 'EDGE-005'; provider = 'registry-user'; displayName = 'Scope Split App'; version = '3.0.0'; scope = 'user'; architecture = 'x64'; kind = 'application'; channel = 'stable'; instanceFamily = 'scope-split-app'; duplicateGroup = $null; expectedInventory = 'separate-instance'; expectedMapping = 'exact' },
    @{ fixtureId = 'EDGE-006'; provider = 'registry-machine'; displayName = 'Scope Split App'; version = '3.0.0'; scope = 'machine'; architecture = 'x64'; kind = 'application'; channel = 'stable'; instanceFamily = 'scope-split-app'; duplicateGroup = $null; expectedInventory = 'separate-instance'; expectedMapping = 'exact' },
    @{ fixtureId = 'EDGE-007'; provider = 'registry-machine'; displayName = 'Architecture Split App'; version = '4.0.0'; scope = 'machine'; architecture = 'x86'; kind = 'application'; channel = 'stable'; instanceFamily = 'architecture-split-app'; duplicateGroup = $null; expectedInventory = 'separate-instance'; expectedMapping = 'exact' },
    @{ fixtureId = 'EDGE-008'; provider = 'registry-machine'; displayName = 'Architecture Split App'; version = '4.0.0'; scope = 'machine'; architecture = 'x64'; kind = 'application'; channel = 'stable'; instanceFamily = 'architecture-split-app'; duplicateGroup = $null; expectedInventory = 'separate-instance'; expectedMapping = 'exact' },
    @{ fixtureId = 'EDGE-009'; provider = 'registry-machine'; displayName = 'Contoso Visual Runtime'; version = '14.38.33135'; scope = 'machine'; architecture = 'x64'; kind = 'runtime'; channel = 'stable'; instanceFamily = 'contoso-runtime'; duplicateGroup = $null; expectedInventory = 'record'; expectedMapping = 'runtime-policy' },
    @{ fixtureId = 'EDGE-010'; provider = 'registry-user'; displayName = 'Preview Tool'; version = '5.0.0-preview.2'; scope = 'user'; architecture = 'x64'; kind = 'application'; channel = 'prerelease'; instanceFamily = 'preview-tool'; duplicateGroup = $null; expectedInventory = 'record'; expectedMapping = 'channel-conflict' },
    @{ fixtureId = 'EDGE-011'; provider = 'registry-machine'; displayName = 'Odd Version Tool'; version = 'release-candidate'; scope = 'machine'; architecture = 'x64'; kind = 'application'; channel = 'stable'; instanceFamily = 'odd-version-tool'; duplicateGroup = $null; expectedInventory = 'record'; expectedMapping = 'incomparable-version' },
    @{ fixtureId = 'EDGE-012'; provider = 'registry-machine'; displayName = 'False Match Target'; version = '1.0.0'; scope = 'machine'; architecture = 'x64'; kind = 'application'; channel = 'stable'; instanceFamily = 'false-match-target'; duplicateGroup = $null; expectedInventory = 'record'; expectedMapping = 'false-match-reject' },
    @{ fixtureId = 'EDGE-013'; provider = 'appx'; displayName = 'Store Fixture'; version = '1.2.3.4'; scope = 'user'; architecture = 'x64'; kind = 'application'; channel = 'stable'; instanceFamily = 'store-fixture'; duplicateGroup = $null; expectedInventory = 'record'; expectedMapping = 'medium-review' },
    @{ fixtureId = 'EDGE-014'; provider = 'registry-machine'; displayName = 'Quoted & Metacharacter Fixture; $()'; version = '1.0.0'; scope = 'machine'; architecture = 'x64'; kind = 'application'; channel = 'stable'; instanceFamily = 'hostile-string'; duplicateGroup = $null; expectedInventory = 'record'; expectedMapping = 'unmatched' },
    @{ fixtureId = 'EDGE-015'; provider = 'registry-machine'; displayName = 'Empty publisher fixture'; version = '2.0.0'; scope = 'machine'; architecture = 'x64'; kind = 'application'; channel = 'stable'; instanceFamily = 'empty-publisher'; duplicateGroup = $null; expectedInventory = 'record'; expectedMapping = 'low-confidence' },
    @{ fixtureId = 'EDGE-016'; provider = 'registry-machine'; displayName = 'Architecture Conflict Tool'; version = '2.0.0'; scope = 'machine'; architecture = 'x86'; kind = 'application'; channel = 'stable'; instanceFamily = 'architecture-conflict'; duplicateGroup = $null; expectedInventory = 'record'; expectedMapping = 'architecture-conflict' },
    @{ fixtureId = 'EDGE-017'; provider = 'registry-machine'; displayName = 'Unmatched Application'; version = '9.9.9'; scope = 'machine'; architecture = 'x64'; kind = 'application'; channel = 'stable'; instanceFamily = 'unmatched-app'; duplicateGroup = $null; expectedInventory = 'record'; expectedMapping = 'unmatched' },
    @{ fixtureId = 'EDGE-018'; provider = 'registry-machine'; displayName = 'Oversized Field Fixture'; version = '1.0.0'; scope = 'machine'; architecture = 'x64'; kind = 'application'; channel = 'stable'; instanceFamily = 'oversized-field'; duplicateGroup = $null; expectedInventory = 'warning'; expectedMapping = 'unmatched' },
    @{ fixtureId = 'EDGE-019'; provider = 'registry-user'; displayName = 'No Display Version'; version = ''; scope = 'user'; architecture = 'x86'; kind = 'application'; channel = 'stable'; instanceFamily = 'blank-version'; duplicateGroup = $null; expectedInventory = 'record-unknown-version'; expectedMapping = 'review' },
    @{ fixtureId = 'EDGE-020'; provider = 'registry-machine'; displayName = 'Malformed Architecture'; version = '1.0.0'; scope = 'machine'; architecture = 'unknown'; kind = 'application'; channel = 'stable'; instanceFamily = 'malformed-architecture'; duplicateGroup = $null; expectedInventory = 'warning'; expectedMapping = 'review' }
)
foreach ($edgeCase in $edgeCases) { $records.Add([pscustomobject]$edgeCase) }

$document = [ordered]@{
    schemaVersion = 1
    generatedBy = 'New-PerformanceFixtures.ps1'
    records = $records
    mappingCases = @(
        @{ fixtureId = 'EDGE-012'; candidatePackageId = 'Contoso.False.Match'; expectedActionable = $false; reason = 'deliberate false executable match must be rejected' },
        @{ fixtureId = 'EDGE-010'; candidatePackageId = 'Contoso.Preview'; expectedActionable = $false; reason = 'prerelease channel conflicts with stable policy' },
        @{ fixtureId = 'EDGE-011'; candidatePackageId = 'Contoso.OddVersion'; expectedActionable = $false; reason = 'version cannot be safely ordered' },
        @{ fixtureId = 'EDGE-016'; candidatePackageId = 'Contoso.ArchConflict'; expectedActionable = $false; reason = 'architecture conflict' },
        @{ fixtureId = 'FIX-0001'; candidatePackageId = 'Contoso.Fixture001'; expectedActionable = $true; reason = 'exact deterministic mapping control' }
    )
}

$parent = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $parent -PathType Container)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
$document | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding utf8NoBOM
Write-Output ("Created {0} records at {1}" -f $records.Count, $OutputPath)
