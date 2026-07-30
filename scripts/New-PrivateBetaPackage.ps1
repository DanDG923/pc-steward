[CmdletBinding()]
param(
    [switch]$CompileInstaller
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $root 'artifacts\private-beta'
$payload = Join-Path $artifactRoot 'payload'
$reports = Join-Path $artifactRoot 'reports'

Remove-Item -LiteralPath $artifactRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $payload, $reports -Force | Out-Null

$desktopProject = Join-Path $root 'src\PCSteward.Desktop\PCSteward.Desktop.csproj'
dotnet restore $desktopProject --runtime win-x64
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet publish $desktopProject --configuration Release --no-restore --runtime win-x64 --self-contained false --output $payload
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$packageList = & dotnet list (Join-Path $root 'PCSteward.slnx') package --include-transitive --format json
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$packageJson = $packageList | ConvertFrom-Json
$packages = foreach ($project in $packageJson.projects) {
    foreach ($framework in $project.frameworks) {
        foreach ($dependency in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
            [pscustomobject]@{ Project = Split-Path $project.path -Leaf; Framework = $framework.framework; Package = $dependency.id; Version = $dependency.resolvedVersion; Type = if ($dependency.requestedVersion) { 'direct' } else { 'transitive' } }
        }
    }
}
$packages = @($packages | Where-Object { $_.Package -and $_.Version })
if ($packages.Count -eq 0) { throw 'Dependency inventory returned no resolved packages.' }
$packages | Sort-Object Project, Package, Version | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $reports 'dependency-inventory.json') -Encoding utf8
$sbom = [ordered]@{ bomFormat = 'CycloneDX'; specVersion = '1.5'; serialNumber = "urn:uuid:$([guid]::NewGuid())"; version = 1; metadata = @{ component = @{ type = 'application'; name = 'PC Steward'; version = '0.1.0-private-beta' } }; components = @($packages | Sort-Object Package, Version | ForEach-Object { @{ type = 'library'; name = $_.Package; version = $_.Version; properties = @(@{ name = 'pcsteward:project'; value = $_.Project }, @{ name = 'pcsteward:dependency-type'; value = $_.Type }) } }) }
$sbom | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $reports 'sbom.cdx.json') -Encoding utf8
Get-FileHash -LiteralPath (Get-ChildItem -LiteralPath $payload -File -Recurse | Select-Object -ExpandProperty FullName) -Algorithm SHA256 | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $reports 'payload-sha256.json') -Encoding utf8

$iscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if ($CompileInstaller) {
    if ($null -eq $iscc) { throw 'Inno Setup 6 (ISCC.exe) is required to compile the installer; no download was attempted.' }
    & $iscc.Source (Join-Path $root 'packaging\PCSteward.iss')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if ($null -eq $iscc) { Write-Warning 'Payload and reports were produced; installer compilation remains unproven because ISCC.exe is unavailable.' }
Write-Host "Private-beta artifacts: $artifactRoot"
