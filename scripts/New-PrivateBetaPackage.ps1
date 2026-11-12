[CmdletBinding()]
param(
    [string]$CertificateThumbprint
)

$ErrorActionPreference = 'Stop'
# The licence/notice map and the notices text are shared with the release candidate builder (New-OwnerOnlyCandidate.ps1).
. (Join-Path $PSScriptRoot 'PCStewardThirdPartyNotices.ps1')
$root = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $root 'artifacts\private-beta'
$payload = Join-Path $artifactRoot 'payload'
$reports = Join-Path $artifactRoot 'reports'
# The package cache is an input to packaging, not a distributable location. Every
# path written to the release evidence is normalized relative to its NuGet package.
$nugetCache = Join-Path $env:USERPROFILE '.nuget\packages'
$inProcComPackage = 'Microsoft.WindowsPackageManager.InProcCom'
$inProcComVersion = '1.29.380'
$inProcComRuntimeOwnership = @(
    [pscustomobject]@{ PayloadPath = 'Microsoft.Management.Deployment.dll'; PackagePath = 'runtimes/win-x64/native/Microsoft.Management.Deployment.InProc.dll' }
    [pscustomobject]@{ PayloadPath = 'Microsoft.Management.Deployment.dll.manifest'; PackagePath = 'runtimes/win-x64/native/Microsoft.Management.Deployment.InProc.dll.manifest' }
    [pscustomobject]@{ PayloadPath = 'Microsoft.Management.Deployment.winmd'; PackagePath = 'runtimes/win-x64/native/Microsoft.Management.Deployment.winmd' }
    [pscustomobject]@{ PayloadPath = 'WindowsPackageManager.dll'; PackagePath = 'runtimes/win-x64/native/WindowsPackageManager.dll' }
)

Remove-Item -LiteralPath $artifactRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $payload, $reports -Force | Out-Null

$desktopProject = Join-Path $root 'src\PCSteward.Desktop\PCSteward.Desktop.csproj'
dotnet restore $desktopProject --runtime win-x64
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet publish $desktopProject --configuration Release --no-restore --runtime win-x64 --self-contained false --output $payload
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
foreach ($comRuntimeLeaf in @(
    'Microsoft.Management.Deployment.CsWinRTProjection.dll',
    'Microsoft.Management.Deployment.dll',
    'Microsoft.Management.Deployment.dll.manifest',
    'Microsoft.Management.Deployment.winmd',
    'WindowsPackageManager.dll'
))
{
    if (!(Test-Path -LiteralPath (Join-Path $payload $comRuntimeLeaf)))
    {
        throw "Required WinGet structured-authority runtime file is missing: $comRuntimeLeaf"
    }
}
foreach ($runtimeLeaf in $inProcComRuntimeOwnership)
{
    $sourcePath = Join-Path (Join-Path (Join-Path $nugetCache $inProcComPackage.ToLowerInvariant()) $inProcComVersion) $runtimeLeaf.PackagePath
    $payloadPath = Join-Path $payload $runtimeLeaf.PayloadPath
    if (!(Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "Required InProcCom source runtime file is missing: $($runtimeLeaf.PackagePath)" }
    if ((Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash -cne (Get-FileHash -LiteralPath $payloadPath -Algorithm SHA256).Hash)
    {
        throw "Published InProcCom runtime leaf does not match its package source: $($runtimeLeaf.PayloadPath)"
    }
    $runtimeLeaf | Add-Member -NotePropertyName Sha256 -NotePropertyValue (Get-FileHash -LiteralPath $payloadPath -Algorithm SHA256).Hash
    $runtimeLeaf | Add-Member -NotePropertyName Bytes -NotePropertyValue (Get-Item -LiteralPath $payloadPath).Length
}
$winuiOutput = Join-Path $root 'src\PCSteward.Desktop\bin\Release\net10.0-windows10.0.26100.0\win-x64'
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
            path = $target.FullName.Substring($payload.Length + 1).Replace('\', '/')
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

$depsPath = Join-Path $payload 'PCSteward.Desktop.deps.json'
if (!(Test-Path -LiteralPath $depsPath)) { throw 'The published desktop dependency manifest is missing.' }
$deps = Get-Content -LiteralPath $depsPath -Raw | ConvertFrom-Json
$packagesFromDeps = @($deps.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' } | ForEach-Object {
    $name, $version = $_.Name -split '/', 2
    [pscustomobject]@{ Package = $name; Version = $version; BomRef = "pkg:nuget/$name@$version" }
} | Sort-Object Package, Version -Unique)
$inProcComPackageKey = "$inProcComPackage/$inProcComVersion"
$packages = @($packagesFromDeps + [pscustomobject]@{
    Package = $inProcComPackage
    Version = $inProcComVersion
    BomRef = "pkg:nuget/$inProcComPackage@$inProcComVersion"
    Spdx = 'MIT'
    NoticeFiles = @('NOTICE.txt')
    RuntimeOwnership = $inProcComRuntimeOwnership
} | Sort-Object Package, Version -Unique)
if ($packages.Count -eq 0) { throw 'The published desktop dependency manifest returned no package components.' }
$packages | Select-Object Package, Version, BomRef, Spdx, NoticeFiles, RuntimeOwnership | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $reports 'dependency-inventory.json') -Encoding utf8

$licenceEvidence = @(Get-PCStewardLicenceEvidence -Packages $packages -PackageRoot $nugetCache)
$licenceEvidence | Select-Object Package, Version, BomRef, Spdx, DisplayName, Copyright, MetadataPath, LicenseFiles, NoticeFiles | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $reports 'licence-inventory.json') -Encoding utf8

$noticeText = ConvertTo-PCStewardThirdPartyNotices -Evidence $licenceEvidence -PackageRoot $nugetCache -Introduction @('This file is generated from the payload dependency manifest. It records licence evidence only; private-distribution legal approval remains required before release.')
$noticesPath = Join-Path $payload 'THIRD-PARTY-NOTICES.txt'
[System.IO.File]::WriteAllText($noticesPath, $noticeText, [System.Text.UTF8Encoding]::new($false))

$applicationRef = 'pkg:generic/pc-steward@0.1.0-private-beta'
$runtimeTargetName = "$($deps.runtimeTarget.name)"
if ([string]::IsNullOrWhiteSpace($runtimeTargetName)) { throw 'The desktop dependency manifest has no runtime target name.' }
$targetGraphProperty = $deps.targets.PSObject.Properties[$runtimeTargetName]
if (!$targetGraphProperty) { throw "The desktop dependency manifest has no graph for runtime target $runtimeTargetName." }
$targetGraph = $targetGraphProperty.Value
$sbomComponents = @($licenceEvidence | ForEach-Object {
    $licence = if ($_.Spdx) { @{ id = $_.Spdx; acknowledgement = 'declared' } } else { @{ name = $_.DisplayName; acknowledgement = 'declared' } }
    @{ type = 'library'; name = $_.Package; version = $_.Version; 'bom-ref' = $_.BomRef; purl = $_.BomRef; licenses = @(@{ license = $licence }) }
})
$componentDependencies = @($licenceEvidence | ForEach-Object {
    $component = $_
    $componentKey = "$($component.Package)/$($component.Version)"
    $children = @()
    if ($componentKey -ne $inProcComPackageKey)
    {
        $nodeProperty = $targetGraph.PSObject.Properties[$componentKey]
        if (!$nodeProperty) { throw "The runtime dependency graph omits published package $componentKey." }
        $node = $nodeProperty.Value
        if ($node.dependencies)
        {
            $children = @($node.dependencies.PSObject.Properties | ForEach-Object {
                $dependencyName = $_.Name
                $dependencyVersion = "$($_.Value)"
                $dependencyByName = @($licenceEvidence | Where-Object { $_.Package -eq $dependencyName })
                if ($dependencyByName.Count -gt 0)
                {
                    $dependency = $dependencyByName | Where-Object { $_.Version -eq $dependencyVersion } | Select-Object -First 1
                    if (!$dependency) { throw "Runtime dependency $dependencyName/$dependencyVersion does not exactly match the published package inventory." }
                    $dependency.BomRef
                }
            } | Where-Object { $_ })
        }
    }
    @{ ref = $component.BomRef; dependsOn = @($children | Sort-Object -Unique) }
})
$sbom = [ordered]@{ bomFormat = 'CycloneDX'; specVersion = '1.5'; serialNumber = 'urn:uuid:4ce4a8bf-a31c-5e1a-a463-7bd50a1fb46c'; version = 1; metadata = @{ component = @{ type = 'application'; name = 'PC Steward'; version = '0.1.0-private-beta'; 'bom-ref' = $applicationRef } }; components = $sbomComponents; dependencies = @(@{ ref = $applicationRef; dependsOn = @($packages | ForEach-Object { $_.BomRef }) }) + $componentDependencies }
$sbom | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $reports 'sbom.cdx.json') -Encoding utf8
$payloadHashes = @(Get-ChildItem -LiteralPath $payload -File -Recurse | Sort-Object FullName | ForEach-Object { [ordered]@{ relativePath = $_.FullName.Substring($payload.Length + 1).Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash; bytes = $_.Length } })
$payloadHashes | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $reports 'payload-sha256.json') -Encoding utf8

$archive = Join-Path $artifactRoot 'PCSteward-0.1.0-private-beta-win-x64.zip'
Compress-Archive -LiteralPath (Get-ChildItem -LiteralPath $payload | Select-Object -ExpandProperty FullName) -DestinationPath $archive -CompressionLevel Optimal
& (Join-Path $PSScriptRoot 'Test-PrivateBetaPackageEvidence.ps1') -ArtifactRoot $artifactRoot | Write-Host
Write-Host "Private-beta artifacts: $artifactRoot"
