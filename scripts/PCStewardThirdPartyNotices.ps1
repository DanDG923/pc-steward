# Task 53/54 close-out (Phase C stage C3; NFR-MNT-01/05, ADR-008): the third-party licence evidence and THIRD-PARTY-NOTICES.txt
# text shared by the private-beta package script (New-PrivateBetaPackage.ps1) and the release candidate builder
# (New-OwnerOnlyCandidate.ps1). Dot-source only. Every licence and notice is read from the restored package's own nuspec and
# files in the NuGet package folder; nothing is downloaded and nothing outside the given package root and payload is read.
# Runs under Windows PowerShell 5.1 (the candidate builder) and PowerShell 7: ASCII source, explicit UTF-8 reads and writes.

$script:PCStewardMitLicenceText = @'
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

$script:PCStewardApacheLicenceText = @'
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

# The licence/notice map for every package in the desktop's published dependency manifest plus the explicitly shipped
# InProcCom runtime. It must reconcile exactly: a published package without an entry, or an entry without a package, stops
# the build. Regenerated from each package's nuspec <license>/<copyright> and the licence and notice files in its root.
function Get-PCStewardLicenceMap {
    $ms = ([string][char]0x00A9) + ' Microsoft Corporation. All rights reserved.'
    [ordered]@{
        'Microsoft.Data.Sqlite/10.0.14' = @{ Spdx = 'MIT'; DisplayName = 'MIT License'; MetadataFile = 'microsoft.data.sqlite.nuspec'; LicenseFiles = @(); NoticeFiles = @(); Copyright = $ms }
        'Microsoft.Data.Sqlite.Core/10.0.14' = @{ Spdx = 'MIT'; DisplayName = 'MIT License'; MetadataFile = 'microsoft.data.sqlite.core.nuspec'; LicenseFiles = @(); NoticeFiles = @(); Copyright = $ms }
        'Microsoft.Extensions.Configuration.Abstractions/10.0.10' = @{ Spdx = 'MIT'; DisplayName = 'MIT License'; MetadataFile = 'microsoft.extensions.configuration.abstractions.nuspec'; LicenseFiles = @(); NoticeFiles = @('THIRD-PARTY-NOTICES.TXT'); Copyright = $ms }
        'Microsoft.Extensions.DependencyInjection.Abstractions/10.0.10' = @{ Spdx = 'MIT'; DisplayName = 'MIT License'; MetadataFile = 'microsoft.extensions.dependencyinjection.abstractions.nuspec'; LicenseFiles = @(); NoticeFiles = @('THIRD-PARTY-NOTICES.TXT'); Copyright = $ms }
        'Microsoft.Extensions.Diagnostics.Abstractions/10.0.10' = @{ Spdx = 'MIT'; DisplayName = 'MIT License'; MetadataFile = 'microsoft.extensions.diagnostics.abstractions.nuspec'; LicenseFiles = @(); NoticeFiles = @('THIRD-PARTY-NOTICES.TXT'); Copyright = $ms }
        'Microsoft.Extensions.FileProviders.Abstractions/10.0.10' = @{ Spdx = 'MIT'; DisplayName = 'MIT License'; MetadataFile = 'microsoft.extensions.fileproviders.abstractions.nuspec'; LicenseFiles = @(); NoticeFiles = @('THIRD-PARTY-NOTICES.TXT'); Copyright = $ms }
        'Microsoft.Extensions.Hosting.Abstractions/10.0.10' = @{ Spdx = 'MIT'; DisplayName = 'MIT License'; MetadataFile = 'microsoft.extensions.hosting.abstractions.nuspec'; LicenseFiles = @(); NoticeFiles = @('THIRD-PARTY-NOTICES.TXT'); Copyright = $ms }
        'Microsoft.Extensions.Logging.Abstractions/10.0.10' = @{ Spdx = 'MIT'; DisplayName = 'MIT License'; MetadataFile = 'microsoft.extensions.logging.abstractions.nuspec'; LicenseFiles = @(); NoticeFiles = @('THIRD-PARTY-NOTICES.TXT'); Copyright = $ms }
        'Microsoft.Extensions.Options/10.0.10' = @{ Spdx = 'MIT'; DisplayName = 'MIT License'; MetadataFile = 'microsoft.extensions.options.nuspec'; LicenseFiles = @(); NoticeFiles = @('THIRD-PARTY-NOTICES.TXT'); Copyright = $ms }
        'Microsoft.Extensions.Primitives/10.0.10' = @{ Spdx = 'MIT'; DisplayName = 'MIT License'; MetadataFile = 'microsoft.extensions.primitives.nuspec'; LicenseFiles = @(); NoticeFiles = @('THIRD-PARTY-NOTICES.TXT'); Copyright = $ms }
        'Microsoft.Web.WebView2/1.0.3179.45' = @{ Spdx = $null; DisplayName = 'Microsoft WebView2 LICENSE.txt'; MetadataFile = 'microsoft.web.webview2.nuspec'; LicenseFiles = @('LICENSE.txt'); NoticeFiles = @('NOTICE.txt'); Copyright = $ms }
        'Microsoft.WindowsAppSDK/1.8.260921001' = @{ Spdx = $null; DisplayName = 'Microsoft Windows App SDK License Terms'; MetadataFile = 'microsoft.windowsappsdk.nuspec'; LicenseFiles = @('license.txt'); NoticeFiles = @('NOTICE.txt'); Copyright = $ms }
        'Microsoft.WindowsAppSDK.AI/1.8.79' = @{ Spdx = $null; DisplayName = 'Microsoft Windows App SDK License Terms'; MetadataFile = 'microsoft.windowsappsdk.ai.nuspec'; LicenseFiles = @('license.txt'); NoticeFiles = @(); Copyright = $ms }
        'Microsoft.WindowsAppSDK.Foundation/1.8.260803002' = @{ Spdx = $null; DisplayName = 'Microsoft Windows App SDK License Terms'; MetadataFile = 'microsoft.windowsappsdk.foundation.nuspec'; LicenseFiles = @('license.txt'); NoticeFiles = @(); Copyright = $ms }
        'Microsoft.WindowsAppSDK.InteractiveExperiences/1.8.260708001' = @{ Spdx = $null; DisplayName = 'Microsoft Windows App SDK License Terms'; MetadataFile = 'microsoft.windowsappsdk.interactiveexperiences.nuspec'; LicenseFiles = @('license.txt'); NoticeFiles = @(); Copyright = $ms }
        'Microsoft.WindowsAppSDK.ML/1.8.2223' = @{ Spdx = $null; DisplayName = 'Microsoft Windows App SDK License Terms'; MetadataFile = 'microsoft.windowsappsdk.ml.nuspec'; LicenseFiles = @('license.txt'); NoticeFiles = @('ThirdPartyNotices.txt'); Copyright = $ms }
        'Microsoft.WindowsAppSDK.Widgets/1.8.251231004' = @{ Spdx = $null; DisplayName = 'Microsoft Windows App SDK License Terms'; MetadataFile = 'microsoft.windowsappsdk.widgets.nuspec'; LicenseFiles = @('license.txt'); NoticeFiles = @(); Copyright = $ms }
        'Microsoft.WindowsAppSDK.WinUI/1.8.260803003' = @{ Spdx = $null; DisplayName = 'Microsoft Windows App SDK License Terms'; MetadataFile = 'microsoft.windowsappsdk.winui.nuspec'; LicenseFiles = @('license.txt'); NoticeFiles = @('NOTICE.txt', 'tools/NOTICE.txt'); Copyright = $ms }
        'Microsoft.WindowsPackageManager.ComInterop/1.29.380' = @{ Spdx = 'MIT'; DisplayName = 'MIT License'; MetadataFile = 'microsoft.windowspackagemanager.cominterop.nuspec'; LicenseFiles = @(); NoticeFiles = @('NOTICE.txt'); Copyright = $ms }
        'Microsoft.WindowsPackageManager.InProcCom/1.29.380' = @{ Spdx = 'MIT'; DisplayName = 'MIT License'; MetadataFile = 'microsoft.windowspackagemanager.inproccom.nuspec'; LicenseFiles = @(); NoticeFiles = @('NOTICE.txt'); Copyright = $ms }
        'OpenAI/2.15.0' = @{ Spdx = 'MIT'; DisplayName = 'MIT License'; MetadataFile = 'openai.nuspec'; LicenseFiles = @(); NoticeFiles = @(); Copyright = 'Copyright (c) 2026 OpenAI (https://openai.com)' }
        'SQLitePCLRaw.bundle_e_sqlite3/2.1.13' = @{ Spdx = 'Apache-2.0'; DisplayName = 'Apache License 2.0'; MetadataFile = 'sqlitepclraw.bundle_e_sqlite3.nuspec'; LicenseFiles = @(); NoticeFiles = @(); Copyright = 'Copyright 2014-2024 SourceGear, LLC' }
        'SQLitePCLRaw.core/2.1.13' = @{ Spdx = 'Apache-2.0'; DisplayName = 'Apache License 2.0'; MetadataFile = 'sqlitepclraw.core.nuspec'; LicenseFiles = @(); NoticeFiles = @(); Copyright = 'Copyright 2014-2024 SourceGear, LLC' }
        'SQLitePCLRaw.lib.e_sqlite3/2.1.14' = @{ Spdx = 'Apache-2.0'; DisplayName = 'Apache License 2.0'; MetadataFile = 'sqlitepclraw.lib.e_sqlite3.nuspec'; LicenseFiles = @(); NoticeFiles = @(); Copyright = 'Copyright 2014-2024 SourceGear, LLC' }
        'SQLitePCLRaw.provider.e_sqlite3/2.1.13' = @{ Spdx = 'Apache-2.0'; DisplayName = 'Apache License 2.0'; MetadataFile = 'sqlitepclraw.provider.e_sqlite3.nuspec'; LicenseFiles = @(); NoticeFiles = @(); Copyright = 'Copyright 2014-2024 SourceGear, LLC' }
        'System.ClientModel/1.16.0' = @{ Spdx = 'MIT'; DisplayName = 'MIT License'; MetadataFile = 'system.clientmodel.nuspec'; LicenseFiles = @(); NoticeFiles = @(); Copyright = $ms }
        'System.Memory.Data/10.0.10' = @{ Spdx = 'MIT'; DisplayName = 'MIT License'; MetadataFile = 'system.memory.data.nuspec'; LicenseFiles = @(); NoticeFiles = @('THIRD-PARTY-NOTICES.TXT'); Copyright = $ms }
        'System.Numerics.Tensors/9.0.0' = @{ Spdx = 'MIT'; DisplayName = 'MIT License'; MetadataFile = 'system.numerics.tensors.nuspec'; LicenseFiles = @(); NoticeFiles = @('THIRD-PARTY-NOTICES.TXT'); Copyright = $ms }
    }
}

# The .NET runtime that Native AOT compiles into PCSteward.ElevatedHelper.exe (ADR-033). Keyed by package id only: these
# packs follow the pinned SDK's runtime (global.json latestPatch), so the version is read from the helper's restore and
# the nuspec checks below still apply to it.
function Get-PCStewardHelperRuntimeLicenceMap {
    $ms = ([string][char]0x00A9) + ' Microsoft Corporation. All rights reserved.'
    [ordered]@{
        'Microsoft.NETCore.App.Runtime.NativeAOT.win-x64' = @{ Spdx = 'MIT'; DisplayName = 'MIT License'; LicenseFiles = @('LICENSE.TXT'); NoticeFiles = @('THIRD-PARTY-NOTICES.TXT'); Copyright = $ms }
        'runtime.win-x64.Microsoft.DotNet.ILCompiler' = @{ Spdx = 'MIT'; DisplayName = 'MIT License'; LicenseFiles = @(); NoticeFiles = @('THIRD-PARTY-NOTICES.TXT'); Copyright = $ms }
    }
}

function Get-PCStewardPackageEvidencePath {
    param([Parameter(Mandatory)][string]$PackageRoot, [Parameter(Mandatory)][string]$Package, [Parameter(Mandatory)][string]$Version, [Parameter(Mandatory)][string]$RelativePath)
    Join-Path (Join-Path (Join-Path $PackageRoot $Package.ToLowerInvariant()) $Version.ToLowerInvariant()) $RelativePath
}

# UTF-8 (a byte-order mark, if any, is honoured and dropped). Windows PowerShell 5.1's Get-Content would read a file without
# a BOM as the ANSI code page.
function Read-PCStewardUtf8Text {
    param([Parameter(Mandatory)][string]$Path)
    [IO.File]::ReadAllText($Path, [Text.UTF8Encoding]::new($false))
}

# Checks one package against its map entry (nuspec licence declaration and copyright, non-empty licence/notice files) and
# returns the evidence row the inventory and the notices are built from. Paths in the row are package-relative.
function Get-PCStewardPackageLicenceEvidence {
    param([Parameter(Mandatory)]$Package, [Parameter(Mandatory)][hashtable]$Mapping, [Parameter(Mandatory)][string]$PackageRoot)
    $key = "$($Package.Package)/$($Package.Version)"
    $metadataFile = if ($Mapping.MetadataFile) { [string]$Mapping.MetadataFile } else { "$($Package.Package)".ToLowerInvariant() + '.nuspec' }
    $metadataPath = Get-PCStewardPackageEvidencePath -PackageRoot $PackageRoot -Package $Package.Package -Version $Package.Version -RelativePath $metadataFile
    if (!(Test-Path -LiteralPath $metadataPath -PathType Leaf)) { throw "Licence metadata is missing for ${key}: $metadataFile" }

    [xml]$nuspec = Read-PCStewardUtf8Text -Path $metadataPath
    $license = $nuspec.package.metadata.license
    if (!$license) { throw "Licence metadata has no licence declaration for $key." }
    if ("$($nuspec.package.metadata.copyright)" -cne "$($Mapping.Copyright)") { throw "Unexpected copyright declaration for $key." }
    if ($Mapping.Spdx) {
        if ($license.type -ne 'expression' -or "$($license.'#text')" -ne $Mapping.Spdx) { throw "Unexpected SPDX licence declaration for $key." }
    }
    elseif ($license.type -ne 'file' -or @($Mapping.LicenseFiles) -notcontains "$($license.'#text')") {
        throw "Unexpected file licence declaration for $key."
    }

    foreach ($file in @(@($Mapping.LicenseFiles) + @($Mapping.NoticeFiles))) {
        $sourcePath = Get-PCStewardPackageEvidencePath -PackageRoot $PackageRoot -Package $Package.Package -Version $Package.Version -RelativePath $file
        if (!(Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "Required licence/notice file is missing for ${key}: $file" }
        if ((Get-Item -LiteralPath $sourcePath).Length -eq 0) { throw "Required licence/notice file is empty for ${key}: $file" }
    }

    [pscustomobject]@{
        Package = $Package.Package
        Version = $Package.Version
        BomRef = $Package.BomRef
        Spdx = $Mapping.Spdx
        DisplayName = $Mapping.DisplayName
        Copyright = $Mapping.Copyright
        MetadataPath = "$($Package.Package.ToLowerInvariant())/$($Package.Version.ToLowerInvariant())/$metadataFile"
        LicenseFiles = @($Mapping.LicenseFiles)
        NoticeFiles = @($Mapping.NoticeFiles)
    }
}

# Evidence for every package, reconciled exactly against the map: nothing published without an entry, no unused entry.
function Get-PCStewardLicenceEvidence {
    param([Parameter(Mandatory)][object[]]$Packages, [Parameter(Mandatory)][string]$PackageRoot)
    $map = Get-PCStewardLicenceMap
    $evidence = @(foreach ($package in $Packages) {
        $key = "$($package.Package)/$($package.Version)"
        if (!$map.Contains($key)) { throw "No approved licence/notice mapping exists for published package $key." }
        Get-PCStewardPackageLicenceEvidence -Package $package -Mapping $map[$key] -PackageRoot $PackageRoot
    })
    if ($evidence.Count -ne $Packages.Count -or $evidence.Count -ne $map.Count) { throw 'The payload licence map does not exactly reconcile to published packages.' }
    $evidence
}

# The notices text: one section per package (sorted), the standard licence text for an SPDX licence, then every mapped
# licence and notice file verbatim. Line breaks are the platform's; the file is written as UTF-8 without a BOM.
function ConvertTo-PCStewardThirdPartyNotices {
    param([Parameter(Mandatory)][object[]]$Evidence, [Parameter(Mandatory)][string]$PackageRoot, [Parameter(Mandatory)][string[]]$Introduction)
    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add('PC Steward third-party notices')
    foreach ($line in $Introduction) { $lines.Add($line) }
    foreach ($item in @($Evidence | Sort-Object Package, Version)) {
        $lines.Add('')
        $lines.Add(('='.PadRight(78, '=')))
        $lines.Add("Package: $($item.Package) $($item.Version)")
        $lines.Add('Licence: ' + $(if ($item.Spdx) { "SPDX expression $($item.Spdx)" } else { $item.DisplayName }))
        $lines.Add("Package metadata source: $($item.MetadataPath)")
        if ($item.Copyright) { $lines.Add("Copyright metadata: $($item.Copyright)") }
        if ($item.Spdx) {
            if ($item.Spdx -ceq 'MIT') { $standard = $script:PCStewardMitLicenceText }
            elseif ($item.Spdx -ceq 'Apache-2.0') { $standard = $script:PCStewardApacheLicenceText }
            else { throw "No standard licence text is held for SPDX expression $($item.Spdx)." }
            $lines.Add("Included standard licence text source: SPDX identifier $($item.Spdx)")
            $lines.Add('')
            $lines.Add($standard)
        }
        foreach ($file in @(@($item.LicenseFiles) + @($item.NoticeFiles))) {
            $lines.Add('')
            $lines.Add("Included package file: $($item.Package.ToLowerInvariant())/$($item.Version.ToLowerInvariant())/$file")
            $lines.Add((Read-PCStewardUtf8Text -Path (Get-PCStewardPackageEvidencePath -PackageRoot $PackageRoot -Package $item.Package -Version $item.Version -RelativePath $file)).TrimEnd())
        }
    }
    ($lines -join [Environment]::NewLine) + [Environment]::NewLine
}

# The published packages named by the desktop dependency manifest (deps.json "package" libraries), plus the InProcCom
# runtime the desktop ships outside that manifest.
function Get-PCStewardDesktopPackages {
    param([Parameter(Mandatory)][string]$DepsPath, [Parameter(Mandatory)][string]$InProcComVersion)
    if (!(Test-Path -LiteralPath $DepsPath -PathType Leaf)) { throw 'The published desktop dependency manifest is missing.' }
    $deps = ConvertFrom-Json -InputObject (Read-PCStewardUtf8Text -Path $DepsPath)
    $fromDeps = @($deps.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' } | ForEach-Object {
        $name, $version = $_.Name -split '/', 2
        [pscustomobject]@{ Package = $name; Version = $version; BomRef = "pkg:nuget/$name@$version" }
    })
    $inProcCom = 'Microsoft.WindowsPackageManager.InProcCom'
    $all = @($fromDeps + [pscustomobject]@{ Package = $inProcCom; Version = $InProcComVersion; BomRef = "pkg:nuget/$inProcCom@$InProcComVersion" })
    $packages = @($all | Sort-Object Package, Version -Unique)
    if ($packages.Count -lt 2) { throw 'The published desktop dependency manifest returned no package components.' }
    $packages
}

# Reads a project's restore output (obj\project.assets.json). Returns the NuGet package folder the restore used and the
# Native AOT runtime packages with the exact versions it resolved: the ILCompiler runtime package from "libraries" and the
# NativeAOT runtime pack from the exact-version "downloadDependencies".
function Read-PCStewardHelperRestore {
    param([Parameter(Mandatory)][string]$AssetsPath)
    if (!(Test-Path -LiteralPath $AssetsPath -PathType Leaf)) { throw "The helper restore output is missing: $AssetsPath" }
    $assets = ConvertFrom-Json -InputObject (Read-PCStewardUtf8Text -Path $AssetsPath)
    $folders = @($assets.packageFolders.PSObject.Properties | ForEach-Object { $_.Name })
    if ($folders.Count -lt 1) { throw 'The helper restore output names no package folder.' }
    $packageRoot = [string]$folders[0]
    if (![IO.Path]::IsPathRooted($packageRoot) -or !(Test-Path -LiteralPath $packageRoot -PathType Container)) { throw 'The helper restore package folder is not an existing absolute folder.' }

    $found = @{}
    foreach ($library in @($assets.libraries.PSObject.Properties)) {
        $name, $version = $library.Name -split '/', 2
        if ($name -ceq 'runtime.win-x64.Microsoft.DotNet.ILCompiler') { $found[$name] = @($found[$name]) + @($version) | Where-Object { $_ } }
    }
    foreach ($framework in @($assets.project.frameworks.PSObject.Properties)) {
        foreach ($download in @($framework.Value.downloadDependencies)) {
            if ([string]$download.name -ceq 'Microsoft.NETCore.App.Runtime.NativeAOT.win-x64') {
                if ([string]$download.version -notmatch '\A\[([0-9A-Za-z.+-]+), ?([0-9A-Za-z.+-]+)\]\z' -or $Matches[1] -cne $Matches[2]) { throw "The NativeAOT runtime pack version is not exact: $($download.version)" }
                $found[[string]$download.name] = @($found[[string]$download.name]) + @($Matches[1]) | Where-Object { $_ }
            }
        }
    }
    $packages = foreach ($name in @((Get-PCStewardHelperRuntimeLicenceMap).Keys)) {
        $versions = @($found[$name] | Sort-Object -Unique)
        if ($versions.Count -ne 1) { throw "The helper restore must resolve exactly one version of ${name}; found $($versions.Count)." }
        [pscustomobject]@{ Package = $name; Version = [string]$versions[0]; BomRef = "pkg:nuget/$name@$($versions[0])" }
    }
    [pscustomobject]@{ PackageRoot = $packageRoot; Packages = @($packages) }
}

# The release payload's THIRD-PARTY-NOTICES.txt (New-OwnerOnlyCandidate.ps1): every desktop package as in the private-beta
# package, plus the .NET runtime compiled into the Native AOT helper. Reads the payload's desktop deps.json, the repository's
# InProcCom pin and the helper's restore output; returns the text and the package count.
function New-PCStewardReleaseThirdPartyNotices {
    param([Parameter(Mandatory)][string]$RepositoryRoot, [Parameter(Mandatory)][string]$PayloadRoot)
    $props = [xml](Read-PCStewardUtf8Text -Path (Join-Path $RepositoryRoot 'Directory.Packages.props'))
    $inProcComVersion = @($props.Project.ItemGroup | ForEach-Object { $_.PackageVersion } | Where-Object { $_ -and $_.Include -ceq 'Microsoft.WindowsPackageManager.InProcCom' } | ForEach-Object { [string]$_.Version })
    if ($inProcComVersion.Count -ne 1) { throw 'Directory.Packages.props must pin Microsoft.WindowsPackageManager.InProcCom exactly once.' }
    $helper = Read-PCStewardHelperRestore -AssetsPath ([IO.Path]::Combine($RepositoryRoot, 'src', 'PCSteward.ElevatedHelper', 'obj', 'project.assets.json'))
    $desktopPackages = @(Get-PCStewardDesktopPackages -DepsPath (Join-Path $PayloadRoot 'PCSteward.Desktop.deps.json') -InProcComVersion $inProcComVersion[0])
    $evidence = @(Get-PCStewardLicenceEvidence -Packages $desktopPackages -PackageRoot $helper.PackageRoot)
    $helperMap = Get-PCStewardHelperRuntimeLicenceMap
    $evidence += @(foreach ($package in $helper.Packages) { Get-PCStewardPackageLicenceEvidence -Package $package -Mapping $helperMap[$package.Package] -PackageRoot $helper.PackageRoot })
    $introduction = @(
        'This file is generated when the release payload is built. It lists every NuGet package whose files are in this installation, and the .NET runtime packages compiled into PCSteward.ElevatedHelper.exe, with their licences and notices.'
        'Source code and licence for PC Steward itself: https://github.com/DanDG923/pc-steward'
    )
    [pscustomobject]@{ Text = (ConvertTo-PCStewardThirdPartyNotices -Evidence $evidence -PackageRoot $helper.PackageRoot -Introduction $introduction); PackageCount = $evidence.Count }
}
