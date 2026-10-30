[CmdletBinding()]
param(
    [string]$CertificateThumbprint
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $root 'artifacts\private-beta'
$payload = Join-Path $artifactRoot 'payload'
$reports = Join-Path $artifactRoot 'reports'
# The package cache is an input to packaging, not a distributable location. Every
# path written to the release evidence is normalized relative to its NuGet package.
$nugetCache = Join-Path $env:USERPROFILE '.nuget\packages'
$inProcComPackage = 'Microsoft.WindowsPackageManager.InProcCom'
$inProcComVersion = '1.10.340'
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

$mitLicenseText = @'
MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
'@
$apacheLicenseText = @'
Apache License
                           Version 2.0, January 2004
                        http://www.apache.org/licenses/

   TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION

   1. Definitions.

      "License" shall mean the terms and conditions for use, reproduction,
      and distribution as defined by Sections 1 through 9 of this document.

      "Licensor" shall mean the copyright owner or entity authorized by
      the copyright owner that is granting the License.

      "Legal Entity" shall mean the union of the acting entity and all
      other entities that control, are controlled by, or are under common
      control with that entity. For the purposes of this definition,
      "control" means (i) the power, direct or indirect, to cause the
      direction or management of such entity, whether by contract or
      otherwise, or (ii) ownership of fifty percent (50%) or more of the
      outstanding shares, or (iii) beneficial ownership of such entity.

      "You" (or "Your") shall mean an individual or Legal Entity
      exercising permissions granted by this License.

      "Source" form shall mean the preferred form for making modifications,
      including but not limited to software source code, documentation
      source, and configuration files.

      "Object" form shall mean any form resulting from mechanical
      transformation or translation of a Source form, including but
      not limited to compiled object code, generated documentation,
      and conversions to other media types.

      "Work" shall mean the work of authorship, whether in Source or
      Object form, made available under the License, as indicated by a
      copyright notice that is included in or attached to the work
      (an example is provided in the Appendix below).

      "Derivative Works" shall mean any work, whether in Source or Object
      form, that is based on (or derived from) the Work and for which the
      editorial revisions, annotations, elaborations, or other modifications
      represent, as a whole, an original work of authorship. For the purposes
      of this License, Derivative Works shall not include works that remain
      separable from, or merely link (or bind by name) to the interfaces of,
      the Work and Derivative Works thereof.

      "Contribution" shall mean any work of authorship, including
      the original version of the Work and any modifications or additions
      to that Work or Derivative Works thereof, that is intentionally
      submitted to Licensor for inclusion in the Work by the copyright owner
      or by an individual or Legal Entity authorized to submit on behalf of
      the copyright owner. For the purposes of this definition, "submitted"
      means any form of electronic, verbal, or written communication sent
      to the Licensor or its representatives, including but not limited to
      communication on electronic mailing lists, source code control systems,
      and issue tracking systems that are managed by, or on behalf of, the
      Licensor for the purpose of discussing and improving the Work, but
      excluding communication that is conspicuously marked or otherwise
      designated in writing by the copyright owner as "Not a Contribution."

      "Contributor" shall mean Licensor and any individual or Legal Entity
      on behalf of whom a Contribution has been received by Licensor and
      subsequently incorporated within the Work.

   2. Grant of Copyright License. Subject to the terms and conditions of
      this License, each Contributor hereby grants to You a perpetual,
      worldwide, non-exclusive, no-charge, royalty-free, irrevocable
      copyright license to reproduce, prepare Derivative Works of,
      publicly display, publicly perform, sublicense, and distribute the
      Work and such Derivative Works in Source or Object form.

   3. Grant of Patent License. Subject to the terms and conditions of
      this License, each Contributor hereby grants to You a perpetual,
      worldwide, non-exclusive, no-charge, royalty-free, irrevocable
      (except as stated in this section) patent license to make, have made,
      use, offer to sell, sell, import, and otherwise transfer the Work,
      where such license applies only to those patent claims licensable
      by such Contributor that are necessarily infringed by their
      Contribution(s) alone or by combination of their Contribution(s)
      with the Work to which such Contribution(s) was submitted. If You
      institute patent litigation against any entity (including a
      cross-claim or counterclaim in a lawsuit) alleging that the Work
      or a Contribution incorporated within the Work constitutes direct
      or contributory patent infringement, then any patent licenses
      granted to You under this License for that Work shall terminate
      as of the date such litigation is filed.

   4. Redistribution. You may reproduce and distribute copies of the
      Work or Derivative Works thereof in any medium, with or without
      modifications, and in Source or Object form, provided that You
      meet the following conditions:

      (a) You must give any other recipients of the Work or
          Derivative Works a copy of this License; and

      (b) You must cause any modified files to carry prominent notices
          stating that You changed the files; and

      (c) You must retain, in the Source form of any Derivative Works
          that You distribute, all copyright, patent, trademark, and
          attribution notices from the Source form of the Work,
          excluding those notices that do not pertain to any part of
          the Derivative Works; and

      (d) If the Work includes a "NOTICE" text file as part of its
          distribution, then any Derivative Works that You distribute must
          include a readable copy of the attribution notices contained
          within such NOTICE file, excluding those notices that do not
          pertain to any part of the Derivative Works, in at least one
          of the following places: within a NOTICE text file distributed
          as part of the Derivative Works; within the Source form or
          documentation, if provided along with the Derivative Works; or,
          within a display generated by the Derivative Works, if and
          wherever such third-party notices normally appear. The contents
          of the NOTICE file are for informational purposes only and
          do not modify the License. You may add Your own attribution
          notices within Derivative Works that You distribute, alongside
          or as an addendum to the NOTICE text from the Work, provided
          that such additional attribution notices cannot be construed
          as modifying the License.

      You may add Your own copyright statement to Your modifications and
      may provide additional or different license terms and conditions
      for use, reproduction, or distribution of Your modifications, or
      for any such Derivative Works as a whole, provided Your use,
      reproduction, and distribution of the Work otherwise complies with
      the conditions stated in this License.

   5. Submission of Contributions. Unless You explicitly state otherwise,
      any Contribution intentionally submitted for inclusion in the Work
      by You to the Licensor shall be under the terms and conditions of
      this License, without any additional terms or conditions.
      Notwithstanding the above, nothing herein shall supersede or modify
      the terms of any separate license agreement you may have executed
      with Licensor regarding such Contributions.

   6. Trademarks. This License does not grant permission to use the trade
      names, trademarks, service marks, or product names of the Licensor,
      except as required for reasonable and customary use in describing the
      origin of the Work and reproducing the content of the NOTICE file.

   7. Disclaimer of Warranty. Unless required by applicable law or
      agreed to in writing, Licensor provides the Work (and each
      Contributor provides its Contributions) on an "AS IS" BASIS,
      WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
      implied, including, without limitation, any warranties or conditions
      of TITLE, NON-INFRINGEMENT, MERCHANTABILITY, or FITNESS FOR A
      PARTICULAR PURPOSE. You are solely responsible for determining the
      appropriateness of using or redistributing the Work and assume any
      risks associated with Your exercise of permissions under this License.

   8. Limitation of Liability. In no event and under no legal theory,
      whether in tort (including negligence), contract, or otherwise,
      unless required by applicable law (such as deliberate and grossly
      negligent acts) or agreed to in writing, shall any Contributor be
      liable to You for damages, including any direct, indirect, special,
      incidental, or consequential damages of any character arising as a
      result of this License or out of the use or inability to use the
      Work (including but not limited to damages for loss of goodwill,
      work stoppage, computer failure or malfunction, or any and all
      other commercial damages or losses), even if such Contributor
      has been advised of the possibility of such damages.

   9. Accepting Warranty or Additional Liability. While redistributing
      the Work or Derivative Works thereof, You may choose to offer,
      and charge a fee for, acceptance of support, warranty, indemnity,
      or other liability obligations and/or rights consistent with this
      License. However, in accepting such obligations, You may act only
      on Your own behalf and on Your sole responsibility, not on behalf
      of any other Contributor, and only if You agree to indemnify,
      defend, and hold each Contributor harmless for any liability
      incurred by, or claims asserted against, such Contributor by reason
      of your accepting any such warranty or additional liability.

   END OF TERMS AND CONDITIONS

   APPENDIX: How to apply the Apache License to your work.

      To apply the Apache License to your work, attach the following
      boilerplate notice, with the fields enclosed by brackets "[]"
      replaced with your own identifying information. (Don't include
      the brackets!)  The text should be enclosed in the appropriate
      comment syntax for the file format. We also recommend that a
      file or class name and description of purpose be included on the
      same "printed page" as the copyright notice for easier
      identification within third-party archives.

   Copyright [yyyy] [name of copyright owner]

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
'@

$licenseMap = [ordered]@{
    'Microsoft.Data.Sqlite/10.0.14' = @{ Spdx = 'MIT'; DisplayName = 'MIT License'; MetadataFile = 'microsoft.data.sqlite.nuspec'; LicenseFiles = @(); NoticeFiles = @(); Copyright = '© Microsoft Corporation. All rights reserved.' }
    'Microsoft.Data.Sqlite.Core/10.0.14' = @{ Spdx = 'MIT'; DisplayName = 'MIT License'; MetadataFile = 'microsoft.data.sqlite.core.nuspec'; LicenseFiles = @(); NoticeFiles = @(); Copyright = '© Microsoft Corporation. All rights reserved.' }
    'Microsoft.Web.WebView2/1.0.3179.45' = @{ Spdx = $null; DisplayName = 'Microsoft WebView2 LICENSE.txt'; MetadataFile = 'microsoft.web.webview2.nuspec'; LicenseFiles = @('LICENSE.txt'); NoticeFiles = @('NOTICE.txt'); Copyright = '© Microsoft Corporation. All rights reserved.' }
    'Microsoft.WindowsAppSDK/1.8.250907003' = @{ Spdx = $null; DisplayName = 'Microsoft Windows App SDK License Terms'; MetadataFile = 'microsoft.windowsappsdk.nuspec'; LicenseFiles = @('license.txt'); NoticeFiles = @('NOTICE.txt'); Copyright = '© Microsoft Corporation. All rights reserved.' }
    'Microsoft.WindowsAppSDK.AI/1.8.37' = @{ Spdx = $null; DisplayName = 'Microsoft Windows App SDK License Terms'; MetadataFile = 'microsoft.windowsappsdk.ai.nuspec'; LicenseFiles = @('license.txt'); NoticeFiles = @(); Copyright = '© Microsoft Corporation. All rights reserved.' }
    'Microsoft.WindowsAppSDK.Foundation/1.8.250906002' = @{ Spdx = $null; DisplayName = 'Microsoft Windows App SDK License Terms'; MetadataFile = 'microsoft.windowsappsdk.foundation.nuspec'; LicenseFiles = @('license.txt'); NoticeFiles = @(); Copyright = '© Microsoft Corporation. All rights reserved.' }
    'Microsoft.WindowsAppSDK.InteractiveExperiences/1.8.250906004' = @{ Spdx = $null; DisplayName = 'Microsoft Windows App SDK License Terms'; MetadataFile = 'microsoft.windowsappsdk.interactiveexperiences.nuspec'; LicenseFiles = @('license.txt'); NoticeFiles = @(); Copyright = '© Microsoft Corporation. All rights reserved.' }
    'Microsoft.WindowsAppSDK.Widgets/1.8.250904007' = @{ Spdx = $null; DisplayName = 'Microsoft Windows App SDK License Terms'; MetadataFile = 'microsoft.windowsappsdk.widgets.nuspec'; LicenseFiles = @('license.txt'); NoticeFiles = @(); Copyright = '© Microsoft Corporation. All rights reserved.' }
    'Microsoft.WindowsAppSDK.WinUI/1.8.250906003' = @{ Spdx = $null; DisplayName = 'Microsoft Windows App SDK License Terms'; MetadataFile = 'microsoft.windowsappsdk.winui.nuspec'; LicenseFiles = @('license.txt'); NoticeFiles = @('NOTICE.txt', 'tools/NOTICE.txt'); Copyright = '© Microsoft Corporation. All rights reserved.' }
    'Microsoft.WindowsPackageManager.ComInterop/1.10.340' = @{ Spdx = 'MIT'; DisplayName = 'MIT License'; MetadataFile = 'microsoft.windowspackagemanager.cominterop.nuspec'; LicenseFiles = @(); NoticeFiles = @('NOTICE.txt'); Copyright = '© Microsoft Corporation. All rights reserved.' }
    'Microsoft.WindowsPackageManager.InProcCom/1.10.340' = @{ Spdx = 'MIT'; DisplayName = 'MIT License'; MetadataFile = 'microsoft.windowspackagemanager.inproccom.nuspec'; LicenseFiles = @(); NoticeFiles = @('NOTICE.txt'); Copyright = '© Microsoft Corporation. All rights reserved.' }
    'SQLitePCLRaw.bundle_e_sqlite3/2.1.13' = @{ Spdx = 'Apache-2.0'; DisplayName = 'Apache License 2.0'; MetadataFile = 'sqlitepclraw.bundle_e_sqlite3.nuspec'; LicenseFiles = @(); NoticeFiles = @(); Copyright = 'Copyright 2014-2024 SourceGear, LLC' }
    'SQLitePCLRaw.core/2.1.13' = @{ Spdx = 'Apache-2.0'; DisplayName = 'Apache License 2.0'; MetadataFile = 'sqlitepclraw.core.nuspec'; LicenseFiles = @(); NoticeFiles = @(); Copyright = 'Copyright 2014-2024 SourceGear, LLC' }
    'SQLitePCLRaw.lib.e_sqlite3/2.1.14' = @{ Spdx = 'Apache-2.0'; DisplayName = 'Apache License 2.0'; MetadataFile = 'sqlitepclraw.lib.e_sqlite3.nuspec'; LicenseFiles = @(); NoticeFiles = @(); Copyright = 'Copyright 2014-2024 SourceGear, LLC' }
    'SQLitePCLRaw.provider.e_sqlite3/2.1.13' = @{ Spdx = 'Apache-2.0'; DisplayName = 'Apache License 2.0'; MetadataFile = 'sqlitepclraw.provider.e_sqlite3.nuspec'; LicenseFiles = @(); NoticeFiles = @(); Copyright = 'Copyright 2014-2024 SourceGear, LLC' }
}

function Get-PackageEvidencePath([string]$package, [string]$version, [string]$relativePath)
{
    return Join-Path (Join-Path (Join-Path $nugetCache $package.ToLowerInvariant()) $version.ToLowerInvariant()) $relativePath
}

function Get-PackageLicenceEvidence($package)
{
    $key = "$($package.Package)/$($package.Version)"
    if (!$licenseMap.Contains($key)) { throw "No approved licence/notice mapping exists for published package $key." }
    $mapping = $licenseMap[$key]
    $metadataPath = Get-PackageEvidencePath $package.Package $package.Version $mapping.MetadataFile
    if (!(Test-Path -LiteralPath $metadataPath)) { throw "Licence metadata is missing for ${key}: $($mapping.MetadataFile)" }

    [xml]$nuspec = Get-Content -LiteralPath $metadataPath -Raw
    $license = $nuspec.package.metadata.license
    if (!$license) { throw "Licence metadata has no licence declaration for $key." }
    if ("$($nuspec.package.metadata.copyright)" -cne "$($mapping.Copyright)")
    {
        throw "Unexpected copyright declaration for $key."
    }
    if ($mapping.Spdx)
    {
        if ($license.type -ne 'expression' -or "$($license.'#text')" -ne $mapping.Spdx) { throw "Unexpected SPDX licence declaration for $key." }
    }
    elseif ($license.type -ne 'file' -or $mapping.LicenseFiles -notcontains "$($license.'#text')")
    {
        throw "Unexpected file licence declaration for $key."
    }

    $sourceFiles = @($mapping.LicenseFiles + $mapping.NoticeFiles)
    foreach ($file in $sourceFiles)
    {
        $sourcePath = Get-PackageEvidencePath $package.Package $package.Version $file
        if (!(Test-Path -LiteralPath $sourcePath)) { throw "Required licence/notice file is missing for ${key}: $file" }
        if ((Get-Item -LiteralPath $sourcePath).Length -eq 0) { throw "Required licence/notice file is empty for ${key}: $file" }
    }

    return [pscustomobject]@{
        Package = $package.Package
        Version = $package.Version
        BomRef = $package.BomRef
        Spdx = $mapping.Spdx
        DisplayName = $mapping.DisplayName
        Copyright = $mapping.Copyright
        MetadataPath = "$($package.Package.ToLowerInvariant())/$($package.Version.ToLowerInvariant())/$($mapping.MetadataFile)"
        LicenseFiles = @($mapping.LicenseFiles)
        NoticeFiles = @($mapping.NoticeFiles)
    }
}

$licenceEvidence = @($packages | ForEach-Object { Get-PackageLicenceEvidence $_ })
if ($licenceEvidence.Count -ne $packages.Count -or $licenceEvidence.Count -ne $licenseMap.Count) { throw 'The payload licence map does not exactly reconcile to published packages.' }
$licenceEvidence | Select-Object Package, Version, BomRef, Spdx, DisplayName, Copyright, MetadataPath, LicenseFiles, NoticeFiles | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $reports 'licence-inventory.json') -Encoding utf8

$noticeLines = [System.Collections.Generic.List[string]]::new()
$noticeLines.Add('PC Steward third-party notices')
$noticeLines.Add('This file is generated from the payload dependency manifest. It records licence evidence only; private-distribution legal approval remains required before release.')
foreach ($evidence in $licenceEvidence | Sort-Object Package, Version)
{
    $noticeLines.Add('')
    $noticeLines.Add(('='.PadRight(78, '=')))
    $noticeLines.Add("Package: $($evidence.Package) $($evidence.Version)")
    $noticeLines.Add("Licence: $(if ($evidence.Spdx) { "SPDX expression $($evidence.Spdx)" } else { $evidence.DisplayName })")
    $noticeLines.Add("Package metadata source: $($evidence.MetadataPath)")
    if ($evidence.Copyright) { $noticeLines.Add("Copyright metadata: $($evidence.Copyright)") }
    if ($evidence.Spdx)
    {
        $noticeLines.Add("Included standard licence text source: SPDX identifier $($evidence.Spdx)")
        $noticeLines.Add('')
        $noticeLines.Add($(if ($evidence.Spdx -eq 'MIT') { $mitLicenseText } else { $apacheLicenseText }))
    }
    foreach ($file in @($evidence.LicenseFiles + $evidence.NoticeFiles))
    {
        $noticeLines.Add('')
        $noticeLines.Add("Included package file: $($evidence.Package.ToLowerInvariant())/$($evidence.Version.ToLowerInvariant())/$file")
        $noticeLines.Add((Get-Content -LiteralPath (Get-PackageEvidencePath $evidence.Package $evidence.Version $file) -Raw).TrimEnd())
    }
}
$noticesPath = Join-Path $payload 'THIRD-PARTY-NOTICES.txt'
[System.IO.File]::WriteAllText($noticesPath, (($noticeLines -join [Environment]::NewLine) + [Environment]::NewLine), [System.Text.UTF8Encoding]::new($false))

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
