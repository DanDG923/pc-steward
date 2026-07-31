[CmdletBinding()]
param(
    [string]$ArtifactRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\private-beta')
)

$ErrorActionPreference = 'Stop'
$ArtifactRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ArtifactRoot)
$reports = Join-Path $ArtifactRoot 'reports'
$payload = Join-Path $ArtifactRoot 'payload'
$archivePath = Join-Path $ArtifactRoot 'PCSteward-0.1.0-private-beta-win-x64.zip'
$inventoryPath = Join-Path $reports 'dependency-inventory.json'
$licencePath = Join-Path $reports 'licence-inventory.json'
$sbomPath = Join-Path $reports 'sbom.cdx.json'
$hashesPath = Join-Path $reports 'payload-sha256.json'
$signingPath = Join-Path $reports 'development-signing.json'
$noticesPath = Join-Path $payload 'THIRD-PARTY-NOTICES.txt'

function Read-RequiredJson([string]$path)
{
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required private-beta evidence is missing: $path" }
    try
    {
        return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -Depth 100
    }
    catch
    {
        throw "Required private-beta evidence is not valid JSON: $path. $($_.Exception.Message)"
    }
}

function Assert-PortableRelativePath([string]$path, [string]$description)
{
    if ([string]::IsNullOrWhiteSpace($path) -or
        $path.StartsWith('/') -or
        $path -match '[\\:]' -or
        $path -match '(^|/)\.\.?(/|$)' -or
        $path -match '//')
    {
        throw "$description is not a normalized portable relative path: $path"
    }
}

function Assert-SameSet([object[]]$actual, [object[]]$expected, [string]$description)
{
    $actualValues = @($actual | ForEach-Object { "$_" } | Sort-Object -Unique)
    $expectedValues = @($expected | ForEach-Object { "$_" } | Sort-Object -Unique)
    if ($actualValues.Count -ne $expectedValues.Count)
    {
        throw "$description count mismatch. Expected $($expectedValues.Count), found $($actualValues.Count)."
    }

    foreach ($value in $expectedValues)
    {
        if ($actualValues -notcontains $value) { throw "$description is missing $value." }
    }
}

foreach ($path in @($noticesPath, $archivePath))
{
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required private-beta artifact is missing: $path" }
}

$inventory = @(Read-RequiredJson $inventoryPath)
$licences = @(Read-RequiredJson $licencePath)
$sbom = Read-RequiredJson $sbomPath
$hashes = @(Read-RequiredJson $hashesPath)
$signing = Read-RequiredJson $signingPath
$notices = Get-Content -LiteralPath $noticesPath -Raw
if ($inventory.Count -eq 0) { throw 'Dependency inventory is empty.' }

$inventoryByKey = @{}
$inventoryByPackage = @{}
$shippedRuntimeKeys = @{}
$expectedInProcComKey = 'Microsoft.WindowsPackageManager.InProcCom/1.10.340'
$expectedInProcComOwnership = @{
    'Microsoft.Management.Deployment.dll' = 'runtimes/win-x64/native/Microsoft.Management.Deployment.InProc.dll'
    'Microsoft.Management.Deployment.dll.manifest' = 'runtimes/win-x64/native/Microsoft.Management.Deployment.InProc.dll.manifest'
    'Microsoft.Management.Deployment.winmd' = 'runtimes/win-x64/native/Microsoft.Management.Deployment.winmd'
    'WindowsPackageManager.dll' = 'runtimes/win-x64/native/WindowsPackageManager.dll'
}
foreach ($package in $inventory)
{
    $packageId = "$($package.Package)"
    $version = "$($package.Version)"
    $key = "$packageId/$version"
    if ($packageId -notmatch '^[A-Za-z0-9_.-]+$' -or [string]::IsNullOrWhiteSpace($version))
    {
        throw "Dependency inventory has an invalid package identity: $key."
    }
    if ($inventoryByKey.ContainsKey($key)) { throw "Dependency inventory contains duplicate package/version row $key." }
    if ($inventoryByPackage.ContainsKey($packageId)) { throw "Dependency inventory contains more than one resolved version for package $packageId." }
    $expectedBomRef = "pkg:nuget/$packageId@$version"
    if ("$($package.BomRef)" -cne $expectedBomRef) { throw "Dependency inventory has a non-canonical bom-ref for $key." }
    $runtimeOwnership = @($package.RuntimeOwnership | Where-Object { $_ })
    if ($runtimeOwnership.Count -gt 0)
    {
        if ($key -cne $expectedInProcComKey) { throw "Only the explicitly shipped InProcCom runtime may declare runtime ownership: $key." }
        if ("$($package.Spdx)" -cne 'MIT' -or @($package.NoticeFiles).Count -ne 1 -or "$($package.NoticeFiles[0])" -cne 'NOTICE.txt')
        {
            throw 'The explicit InProcCom dependency-inventory evidence must declare MIT and NOTICE.txt.'
        }
        if ($runtimeOwnership.Count -ne $expectedInProcComOwnership.Count) { throw 'InProcCom runtime ownership has an unexpected leaf count.' }
        $ownedLeaves = @{}
        foreach ($ownedLeaf in $runtimeOwnership)
        {
            $payloadPath = "$($ownedLeaf.PayloadPath)"
            $packagePath = "$($ownedLeaf.PackagePath)"
            Assert-PortableRelativePath $payloadPath 'InProcCom runtime ownership payload path'
            Assert-PortableRelativePath $packagePath 'InProcCom runtime ownership package path'
            if (!$expectedInProcComOwnership.ContainsKey($payloadPath) -or $expectedInProcComOwnership[$payloadPath] -cne $packagePath)
            {
                throw "InProcCom runtime ownership has an unexpected leaf mapping: $payloadPath <- $packagePath."
            }
            if ($ownedLeaves.ContainsKey($payloadPath)) { throw "InProcCom runtime ownership repeats $payloadPath." }
            if ("$($ownedLeaf.Sha256)" -notmatch '^[A-Fa-f0-9]{64}$' -or [long]$ownedLeaf.Bytes -lt 0)
            {
                throw "InProcCom runtime ownership has invalid hash evidence for $payloadPath."
            }
            $ownedLeaves[$payloadPath] = $ownedLeaf
        }
        Assert-SameSet @($ownedLeaves.Keys) @($expectedInProcComOwnership.Keys) 'InProcCom runtime ownership leaves'
        $shippedRuntimeKeys[$key] = $ownedLeaves
    }
    $inventoryByKey[$key] = $package
    $inventoryByPackage[$packageId] = $package
}

$requiredPayloadPaths = @(
    'App.xbf',
    'PCSteward.Desktop.exe',
    'PCSteward.Desktop.pri',
    'PCSteward.ScheduledHost.exe',
    'Microsoft.Management.Deployment.CsWinRTProjection.dll',
    'Microsoft.Management.Deployment.dll',
    'Microsoft.Management.Deployment.dll.manifest',
    'Microsoft.Management.Deployment.winmd',
    'WindowsPackageManager.dll',
    'Remove-PCSteward.ps1',
    'Resources/ColorTokens.xbf',
    'Resources/ControlStyles.xbf',
    'Resources/ThemeResources.xbf',
    'Resources/Typography.xbf',
    'THIRD-PARTY-NOTICES.txt'
)
foreach ($relativePath in $requiredPayloadPaths)
{
    if (!(Test-Path -LiteralPath (Join-Path $payload $relativePath) -PathType Leaf))
    {
        throw "Packaged smoke prerequisite is missing: $relativePath."
    }
}
if (!$shippedRuntimeKeys.ContainsKey($expectedInProcComKey)) { throw 'Dependency inventory omits explicit ownership of the shipped InProcCom runtime.' }
foreach ($ownedLeaf in $shippedRuntimeKeys[$expectedInProcComKey].Values)
{
    $payloadPath = Join-Path $payload $ownedLeaf.PayloadPath
    if (!(Test-Path -LiteralPath $payloadPath -PathType Leaf)) { throw "Owned InProcCom runtime leaf is missing from the payload: $($ownedLeaf.PayloadPath)." }
    $payloadItem = Get-Item -LiteralPath $payloadPath
    if ($payloadItem.Length -ne [long]$ownedLeaf.Bytes -or (Get-FileHash -LiteralPath $payloadPath -Algorithm SHA256).Hash -cne "$($ownedLeaf.Sha256)".ToUpperInvariant())
    {
        throw "Owned InProcCom runtime leaf does not match its recorded package evidence: $($ownedLeaf.PayloadPath)."
    }
}
foreach ($entrypoint in @('PCSteward.Desktop', 'PCSteward.ScheduledHost'))
{
    foreach ($companionExtension in @('.dll', '.deps.json', '.runtimeconfig.json'))
    {
        $companion = "$entrypoint$companionExtension"
        if (!(Test-Path -LiteralPath (Join-Path $payload $companion) -PathType Leaf))
        {
            throw "Framework-dependent entrypoint $entrypoint.exe is not runnable because $companion is missing."
        }
    }
}
if (Get-ChildItem -LiteralPath $payload -File -Recurse | Where-Object { $_.Name -like 'PCSteward.ElevatedHelper.*' })
{
    throw 'Standalone payload contains the excluded elevated helper.'
}

$runtimeGraphs = @()
$packageKeysFromDeps = @{}
$depsFiles = @(Get-ChildItem -LiteralPath $payload -Filter 'PCSteward.*.deps.json' -File | Sort-Object Name)
if ($depsFiles.Count -lt 2) { throw 'Payload does not contain both desktop and scheduled-host dependency manifests.' }
foreach ($depsFile in $depsFiles)
{
    $deps = Read-RequiredJson $depsFile.FullName
    $runtimeTargetName = "$($deps.runtimeTarget.name)"
    if ($runtimeTargetName -ne '.NETCoreApp,Version=v10.0/win-x64')
    {
        throw "$($depsFile.Name) has unexpected runtime target $runtimeTargetName."
    }
    $targetProperty = $deps.targets.PSObject.Properties[$runtimeTargetName]
    if (!$targetProperty) { throw "$($depsFile.Name) omits its declared runtime dependency graph." }
    $runtimeGraphs += [pscustomobject]@{ Name = $depsFile.Name; Graph = $targetProperty.Value }
    foreach ($library in $deps.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' })
    {
        $packageKeysFromDeps[$library.Name] = $true
        if (!$targetProperty.Value.PSObject.Properties[$library.Name])
        {
            throw "$($depsFile.Name) runtime graph omits package library $($library.Name)."
        }
    }
}
Assert-SameSet @($inventoryByKey.Keys | Where-Object { !$shippedRuntimeKeys.ContainsKey($_) }) @($packageKeysFromDeps.Keys) 'Dependency inventory versus packaged deps manifests'

$licencesByKey = @{}
$allowedSpdx = @('MIT', 'Apache-2.0')
$allowedFileLicences = @('Microsoft WebView2 LICENSE.txt', 'Microsoft Windows App SDK License Terms')
foreach ($licence in $licences)
{
    $packageId = "$($licence.Package)"
    $version = "$($licence.Version)"
    $key = "$packageId/$version"
    if (!$inventoryByKey.ContainsKey($key)) { throw "Licence inventory has unexpected package $key." }
    if ($licencesByKey.ContainsKey($key)) { throw "Licence inventory contains duplicate package/version row $key." }
    if ("$($licence.BomRef)" -cne "$($inventoryByKey[$key].BomRef)") { throw "Licence inventory bom-ref does not match dependency inventory for $key." }
    Assert-PortableRelativePath "$($licence.MetadataPath)" "Licence metadata path for $key"
    $expectedMetadataPrefix = "$($packageId.ToLowerInvariant())/$($version.ToLowerInvariant())/"
    if (!"$($licence.MetadataPath)".StartsWith($expectedMetadataPrefix, [StringComparison]::Ordinal))
    {
        throw "Licence metadata path does not belong to $key."
    }

    $spdx = "$($licence.Spdx)"
    $displayName = "$($licence.DisplayName)"
    if ($spdx)
    {
        if ($allowedSpdx -cnotcontains $spdx) { throw "Licence inventory has an unapproved SPDX expression for ${key}: $spdx." }
    }
    elseif ($allowedFileLicences -cnotcontains $displayName)
    {
        throw "Licence inventory has an unapproved file licence for ${key}: $displayName."
    }
    if ([string]::IsNullOrWhiteSpace("$($licence.Copyright)")) { throw "Licence inventory omits copyright metadata for $key." }

    $packageHeader = "Package: $packageId $version"
    if ([regex]::Matches($notices, "(?m)^$([regex]::Escape($packageHeader))\r?$").Count -ne 1)
    {
        throw "Notices must contain exactly one section for $key."
    }
    if ($notices -notmatch [regex]::Escape("Copyright metadata: $($licence.Copyright)"))
    {
        throw "Notices omit copyright metadata for $key."
    }

    $sourceFiles = @(@($licence.LicenseFiles) + @($licence.NoticeFiles))
    if (@($sourceFiles | Sort-Object -Unique).Count -ne $sourceFiles.Count) { throw "Licence inventory repeats a source file for $key." }
    foreach ($sourceFile in $sourceFiles)
    {
        Assert-PortableRelativePath "$sourceFile" "Licence/notice source path for $key"
        $sourceMarker = "Included package file: $($packageId.ToLowerInvariant())/$($version.ToLowerInvariant())/$sourceFile"
        if ([regex]::Matches($notices, "(?m)^$([regex]::Escape($sourceMarker))\r?$").Count -ne 1)
        {
            throw "Notices must contain exactly one mapped source-file section for ${key}: $sourceFile."
        }
    }
    $licencesByKey[$key] = $licence
}
Assert-SameSet @($licencesByKey.Keys) @($inventoryByKey.Keys) 'Licence inventory versus dependency inventory'

if ("$($sbom.bomFormat)" -cne 'CycloneDX' -or "$($sbom.specVersion)" -cne '1.5')
{
    throw 'SBOM is not CycloneDX 1.5.'
}
$applicationRef = 'pkg:generic/pc-steward@0.1.0-private-beta'
if ("$($sbom.metadata.component.'bom-ref')" -cne $applicationRef -or
    "$($sbom.metadata.component.name)" -cne 'PC Steward' -or
    "$($sbom.metadata.component.version)" -cne '0.1.0-private-beta')
{
    throw 'SBOM application identity is not the private-beta package identity.'
}

$componentsByKey = @{}
foreach ($component in @($sbom.components))
{
    $key = "$($component.name)/$($component.version)"
    if (!$inventoryByKey.ContainsKey($key)) { throw "SBOM has unexpected component $key." }
    if ($componentsByKey.ContainsKey($key)) { throw "SBOM contains duplicate component $key." }
    $expectedBomRef = "$($inventoryByKey[$key].BomRef)"
    if ("$($component.type)" -cne 'library' -or
        "$($component.'bom-ref')" -cne $expectedBomRef -or
        "$($component.purl)" -cne $expectedBomRef)
    {
        throw "SBOM component identity is not canonical for $key."
    }
    $componentLicences = @($component.licenses)
    if ($componentLicences.Count -ne 1) { throw "SBOM must have exactly one declared licence for $key." }
    $declared = $componentLicences[0].license
    $expectedLicence = $licencesByKey[$key]
    if ("$($declared.acknowledgement)" -cne 'declared') { throw "SBOM licence acknowledgement is not declared for $key." }
    if ("$($expectedLicence.Spdx)")
    {
        if ("$($declared.id)" -cne "$($expectedLicence.Spdx)" -or "$($declared.name)")
        {
            throw "SBOM SPDX licence does not reconcile for $key."
        }
    }
    elseif ("$($declared.name)" -cne "$($expectedLicence.DisplayName)" -or "$($declared.id)")
    {
        throw "SBOM file licence does not reconcile for $key."
    }
    $componentsByKey[$key] = $component
}
Assert-SameSet @($componentsByKey.Keys) @($inventoryByKey.Keys) 'SBOM components versus dependency inventory'

$expectedDependencies = @{}
$expectedDependencies[$applicationRef] = @($inventory | ForEach-Object { "$($_.BomRef)" } | Sort-Object -Unique)
$seenPackageNodes = @{}
foreach ($package in $inventory)
{
    $key = "$($package.Package)/$($package.Version)"
    $children = @{}
    if ($shippedRuntimeKeys.ContainsKey($key))
    {
        $expectedDependencies["$($package.BomRef)"] = @()
        continue
    }
    foreach ($runtimeGraph in $runtimeGraphs)
    {
        $nodeProperty = $runtimeGraph.Graph.PSObject.Properties[$key]
        if (!$nodeProperty) { continue }
        $seenPackageNodes[$key] = $true
        if (!$nodeProperty.Value.dependencies) { continue }
        foreach ($dependency in $nodeProperty.Value.dependencies.PSObject.Properties)
        {
            if (!$inventoryByPackage.ContainsKey($dependency.Name)) { continue }
            $dependencyKey = "$($dependency.Name)/$($dependency.Value)"
            if (!$inventoryByKey.ContainsKey($dependencyKey))
            {
                throw "Runtime dependency $dependencyKey does not exactly match the packaged dependency inventory."
            }
            $children["$($inventoryByKey[$dependencyKey].BomRef)"] = $true
        }
    }
    if (!$seenPackageNodes.ContainsKey($key)) { throw "Packaged runtime graphs omit dependency node $key." }
    $expectedDependencies["$($package.BomRef)"] = @($children.Keys | Sort-Object)
}

$actualDependencies = @{}
foreach ($dependencyNode in @($sbom.dependencies))
{
    $reference = "$($dependencyNode.ref)"
    if (!$expectedDependencies.ContainsKey($reference)) { throw "SBOM dependency graph has unexpected node $reference." }
    if ($actualDependencies.ContainsKey($reference)) { throw "SBOM dependency graph repeats node $reference." }
    $dependsOn = @($dependencyNode.dependsOn)
    if (@($dependsOn | Sort-Object -Unique).Count -ne $dependsOn.Count) { throw "SBOM dependency graph repeats an edge from $reference." }
    foreach ($target in $dependsOn)
    {
        if ($target -eq $reference -or (!$expectedDependencies.ContainsKey("$target") -and $target -ne $applicationRef))
        {
            throw "SBOM dependency graph has invalid edge $reference -> $target."
        }
    }
    Assert-SameSet $dependsOn $expectedDependencies[$reference] "SBOM dependency edges from $reference"
    $actualDependencies[$reference] = $true
}
Assert-SameSet @($actualDependencies.Keys) @($expectedDependencies.Keys) 'SBOM dependency graph nodes'

$hashesByPath = @{}
foreach ($hashRow in $hashes)
{
    $relativePath = "$($hashRow.relativePath)"
    Assert-PortableRelativePath $relativePath 'Payload hash path'
    if ($hashesByPath.ContainsKey($relativePath)) { throw "Payload hash manifest repeats path $relativePath." }
    if ("$($hashRow.sha256)" -notmatch '^[A-Fa-f0-9]{64}$') { throw "Payload hash manifest has an invalid SHA-256 for $relativePath." }
    if ([long]$hashRow.bytes -lt 0) { throw "Payload hash manifest has an invalid byte count for $relativePath." }
    $hashesByPath[$relativePath] = $hashRow
}

$payloadItems = @(Get-ChildItem -LiteralPath $payload -Recurse -Force)
if ($payloadItems | Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 })
{
    throw 'Payload contains a reparse point.'
}
$payloadFiles = @($payloadItems | Where-Object { !$_.PSIsContainer })
$payloadPaths = @()
foreach ($file in $payloadFiles)
{
    $relativePath = $file.FullName.Substring($payload.Length + 1).Replace('\', '/')
    $payloadPaths += $relativePath
    if (!$hashesByPath.ContainsKey($relativePath)) { throw "Payload hash manifest omits $relativePath." }
    $expectedHash = $hashesByPath[$relativePath]
    if ($file.Length -ne [long]$expectedHash.bytes) { throw "Payload byte count does not match manifest for $relativePath." }
    $actualHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    if ($actualHash -cne "$($expectedHash.sha256)".ToUpperInvariant()) { throw "Payload SHA-256 does not match manifest for $relativePath." }
}
Assert-SameSet $payloadPaths @($hashesByPath.Keys) 'Payload files versus payload hash manifest'

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archiveEntries = @{}
$archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
try
{
    foreach ($entry in $archive.Entries)
    {
        if ([string]::IsNullOrEmpty($entry.Name)) { throw "Archive contains unexpected directory entry $($entry.FullName)." }
        $relativePath = $entry.FullName
        Assert-PortableRelativePath $relativePath 'Archive entry path'
        if ($archiveEntries.ContainsKey($relativePath)) { throw "Archive repeats entry $relativePath." }
        if (!$hashesByPath.ContainsKey($relativePath)) { throw "Archive has an entry omitted from the payload hash manifest: $relativePath." }
        $expectedHash = $hashesByPath[$relativePath]
        if ($entry.Length -ne [long]$expectedHash.bytes) { throw "Archive byte count does not match manifest for $relativePath." }
        $stream = $entry.Open()
        $sha256 = [Security.Cryptography.SHA256]::Create()
        try
        {
            $actualHash = [Convert]::ToHexString($sha256.ComputeHash($stream))
        }
        finally
        {
            $sha256.Dispose()
            $stream.Dispose()
        }
        if ($actualHash -cne "$($expectedHash.sha256)".ToUpperInvariant()) { throw "Archive SHA-256 does not match manifest for $relativePath." }
        $archiveEntries[$relativePath] = $true
    }
}
finally
{
    $archive.Dispose()
}
Assert-SameSet @($archiveEntries.Keys) @($hashesByPath.Keys) 'Archive entries versus payload hash manifest'

$signingStatus = "$($signing.status)"
if ($signingStatus -eq 'Not requested')
{
    if (@($signing.files).Count -ne 0) { throw 'Unsigned signing evidence unexpectedly lists signed files.' }
}
elseif ($signingStatus -eq 'Signed with an untrusted development certificate')
{
    if ("$($signing.thumbprint)" -notmatch '^[A-Fa-f0-9]{40}$' -or [string]::IsNullOrWhiteSpace("$($signing.subject)"))
    {
        throw 'Development-signing evidence omits a valid signer identity.'
    }
    $expectedSignedPaths = @($payloadFiles |
        Where-Object { $_.Name -like 'PCSteward.*.exe' -or $_.Name -like 'PCSteward.*.dll' -or $_.Name -eq 'Remove-PCSteward.ps1' } |
        ForEach-Object { $_.FullName.Substring($payload.Length + 1).Replace('\', '/') })
    $reportedSignedPaths = @()
    foreach ($signedFile in @($signing.files))
    {
        $relativePath = "$($signedFile.path)"
        Assert-PortableRelativePath $relativePath 'Development-signing path'
        if ("$($signedFile.signerThumbprint)" -cne "$($signing.thumbprint)") { throw "Development-signing thumbprint mismatch for $relativePath." }
        $signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $payload $relativePath)
        if (!$signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -cne "$($signing.thumbprint)")
        {
            throw "Packaged signature does not match signing evidence for $relativePath."
        }
        $reportedSignedPaths += $relativePath
    }
    Assert-SameSet $reportedSignedPaths $expectedSignedPaths 'Development-signing coverage'
}
else
{
    throw "Unknown development-signing evidence status: $signingStatus."
}

$runtimePrerequisites = @()
foreach ($runtimeConfigName in @('PCSteward.Desktop.runtimeconfig.json', 'PCSteward.ScheduledHost.runtimeconfig.json'))
{
    $runtimeConfig = Read-RequiredJson (Join-Path $payload $runtimeConfigName)
    if ("$($runtimeConfig.runtimeOptions.tfm)" -cne 'net10.0' -or
        "$($runtimeConfig.runtimeOptions.framework.name)" -cne 'Microsoft.NETCore.App' -or
        "$($runtimeConfig.runtimeOptions.framework.version)" -cne '10.0.0')
    {
        throw "$runtimeConfigName has an unexpected framework-dependent runtime prerequisite."
    }
    $runtimePrerequisites += "$($runtimeConfig.runtimeOptions.framework.name) $($runtimeConfig.runtimeOptions.framework.version)"
}

[int]$dependencyEdgeCount = ($expectedDependencies.Values | ForEach-Object { @($_).Count } | Measure-Object -Sum).Sum
[long]$payloadByteCount = ($payloadFiles | Measure-Object -Property Length -Sum).Sum
[ordered]@{
    mechanicalValidation = 'Pass'
    packageCount = $inventory.Count
    sbomComponentCount = @($sbom.components).Count
    sbomDependencyNodeCount = @($sbom.dependencies).Count
    sbomDependencyEdgeCount = $dependencyEdgeCount
    payloadFileCount = $payloadFiles.Count
    payloadBytes = $payloadByteCount
    archiveBytes = (Get-Item -LiteralPath $archivePath).Length
    archiveSha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    noticeBytes = (Get-Item -LiteralPath $noticesPath).Length
    signingStatus = $signingStatus
    runtimePrerequisites = @($runtimePrerequisites | Sort-Object -Unique)
    packagedLaunchSmoke = 'Not performed by this non-executing validator'
} | ConvertTo-Json -Depth 4
