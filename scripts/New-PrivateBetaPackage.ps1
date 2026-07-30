[CmdletBinding()]
param(
    [string]$CertificateThumbprint
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
$winuiOutput = Join-Path $root 'src\PCSteward.Desktop\bin\Release\net10.0-windows10.0.22000.0\win-x64'
foreach ($resourceLeaf in @('App.xbf', 'PCSteward.Desktop.pri'))
{
    $resourcePath = Join-Path $winuiOutput $resourceLeaf
    if (!(Test-Path -LiteralPath $resourcePath)) { throw "Required WinUI resource is missing: $resourceLeaf" }
    Copy-Item -LiteralPath $resourcePath -Destination $payload
}
$compiledResources = Join-Path $winuiOutput 'Resources'
if (!(Test-Path -LiteralPath $compiledResources)) { throw 'Required compiled WinUI resource directory is missing.' }
Copy-Item -LiteralPath $compiledResources -Destination $payload -Recurse
if (Test-Path -LiteralPath (Join-Path $payload 'PCSteward.ElevatedHelper.exe')) { throw 'Standalone payload must not contain the elevated helper.' }
Copy-Item -LiteralPath (Join-Path $root 'packaging\Remove-PCSteward.ps1') -Destination $payload

$signingTargets = @(
    Get-ChildItem -LiteralPath $payload -File -Recurse |
        Where-Object { $_.Name -like 'PCSteward.*.exe' -or $_.Name -like 'PCSteward.*.dll' }
)
$signingTargets += Get-Item -LiteralPath (Join-Path $payload 'Remove-PCSteward.ps1')
$signingEvidence = [ordered]@{ status = 'Not requested'; files = @() }
if ($CertificateThumbprint)
{
    $certificate = Get-Item -LiteralPath (Join-Path 'Cert:\CurrentUser\My' $CertificateThumbprint) -ErrorAction Stop
    if (!$certificate.HasPrivateKey) { throw 'The requested signing certificate has no private key.' }
    if ($certificate.NotAfter -le (Get-Date)) { throw 'The requested signing certificate is expired.' }
    $codeSigningUsage = $certificate.Extensions |
        Where-Object { $_.Oid.Value -eq '2.5.29.37' } |
        ForEach-Object { $_.Format($true) } |
        Where-Object { $_ -match '1\.3\.6\.1\.5\.5\.7\.3\.3' }
    if (!$codeSigningUsage) { throw 'The requested certificate is not valid for code signing.' }

    $signedFiles = foreach ($target in $signingTargets | Sort-Object FullName)
    {
        $signature = Set-AuthenticodeSignature -FilePath $target.FullName -Certificate $certificate
        if (!$signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint)
        {
            throw "Development signing did not attach the expected signer to $($target.Name)."
        }
        [ordered]@{
            path = $target.FullName.Substring($payload.Length + 1)
            status = $signature.Status
            statusMessage = $signature.StatusMessage
            signerThumbprint = $certificate.Thumbprint
        }
    }
    $signingEvidence = [ordered]@{
        status = 'Signed with an untrusted development certificate'
        subject = $certificate.Subject
        thumbprint = $certificate.Thumbprint
        files = @($signedFiles)
    }
}
$signingEvidence | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $reports 'development-signing.json') -Encoding utf8

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

$archive = Join-Path $artifactRoot 'PCSteward-0.1.0-private-beta-win-x64.zip'
Compress-Archive -LiteralPath (Get-ChildItem -LiteralPath $payload | Select-Object -ExpandProperty FullName) -DestinationPath $archive -CompressionLevel Optimal
Write-Host "Private-beta artifacts: $artifactRoot"
