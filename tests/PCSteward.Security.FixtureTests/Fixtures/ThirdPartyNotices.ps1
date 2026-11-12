param(
    [Parameter(Mandatory)][string]$RepositoryRoot,
    [Parameter(Mandatory)][string]$WorkRoot
)
# Phase C close-out (NFR-MNT-01/05, ADR-008): fixtures for scripts/PCStewardThirdPartyNotices.ps1, which writes the release
# payload's THIRD-PARTY-NOTICES.txt (New-OwnerOnlyCandidate.ps1) and the private-beta package's (New-PrivateBetaPackage.ps1).
# Runs under pwsh 7 (Linux or Windows) and Windows PowerShell 5.1 (the candidate builder's runtime). It parses the script,
# applies a small AST policy, then runs the REAL functions on a synthetic NuGet package folder, payload deps.json, package
# pins and helper restore output in WorkRoot, including refusals. No dotnet, git, network, elevation or real package is used.
$ErrorActionPreference = 'Stop'
$isWindowsPowerShell = $PSVersionTable.PSEdition -ne 'Core'
if ($isWindowsPowerShell) {
    foreach ($module in @('Management', 'Utility')) { Import-Module ([IO.Path]::Combine($PSHOME, 'Modules', "Microsoft.PowerShell.$module", "Microsoft.PowerShell.$module.psd1")) -Force }
}
$scriptPath = [IO.Path]::Combine($RepositoryRoot, 'scripts', 'PCStewardThirdPartyNotices.ps1')
$results = [ordered]@{ shell = ($PSVersionTable.PSEdition + ' ' + $PSVersionTable.PSVersion.ToString()) }
function Get-Failure([scriptblock]$Action) { try { & $Action | Out-Null; return $null } catch { return $_.Exception.Message } }

# 1. Parse check, ASCII source (Windows PowerShell 5.1 reads a file without a BOM as ANSI) and AST policy.
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$tokens, [ref]$errors)
$results.parseErrors = @($errors).Count
$results.asciiSource = (@([IO.File]::ReadAllBytes($scriptPath) | Where-Object { $_ -gt 127 }).Count -eq 0)
$forbidden = @('Invoke-Expression', 'iex', 'Invoke-WebRequest', 'iwr', 'Invoke-RestMethod', 'irm', 'curl', 'wget', 'Start-BitsTransfer', 'Start-Process', 'saps',
    'Start-Job', 'Invoke-Command', 'Add-Type', 'Remove-Item', 'rm', 'del', 'Set-Content', 'Out-File', 'Set-Acl', 'Set-ItemProperty', 'New-Item', 'Copy-Item', 'Move-Item',
    'dotnet', 'git', 'powershell', 'pwsh', 'cmd', 'msiexec', 'winget', 'choco', 'certutil', 'Set-AuthenticodeSignature')
$violations = [Collections.Generic.List[string]]::new()
foreach ($command in $ast.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] }, $true)) {
    $name = $command.GetCommandName()
    if ($name -and ($forbidden -contains $name)) { $violations.Add("forbidden command $name") }
    if ($command.InvocationOperator -ne [Management.Automation.Language.TokenKind]::Unknown) { $violations.Add("invocation operator: $($command.Extent.Text)") }
}
foreach ($member in $ast.FindAll({ param($node) $node -is [Management.Automation.Language.InvokeMemberExpressionAst] }, $true)) {
    if ([string]$member.Member.Value -match '\A(Write|Create|Delete|Move|Copy|Start|Download|Open)') { $violations.Add("member call $($member.Member.Value)") }
}
foreach ($variable in $ast.FindAll({ param($node) $node -is [Management.Automation.Language.VariableExpressionAst] -and $node.VariablePath.UserPath -ieq 'ExecutionContext' }, $true)) { $violations.Add('$ExecutionContext') }
$results.astViolations = @($violations)

. $scriptPath

# 2. A synthetic NuGet package folder that satisfies the map, with notice text that is not ASCII.
$map = Get-PCStewardLicenceMap
$helperMap = Get-PCStewardHelperRuntimeLicenceMap
$helperVersion = '10.0.14'
$inProcComKey = @($map.Keys | Where-Object { $_ -like 'Microsoft.WindowsPackageManager.InProcCom/*' })
if ($inProcComKey.Count -ne 1) { throw 'The map must hold exactly one InProcCom entry.' }
$inProcComVersion = $inProcComKey[0].Split('/')[1]
$utf8 = [Text.UTF8Encoding]::new($false)
$marker = 'Notice text ' + ([string][char]0x00A9) + ' ' + ([string][char]0x00E9) + 't' + ([string][char]0x00E9)

function Write-FixtureFile([string]$Path, [string]$Text) {
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path))
    [IO.File]::WriteAllText($Path, $Text, $utf8)
}

function Write-FixturePackage([string]$PackageRoot, [string]$Package, [string]$Version, [hashtable]$Mapping) {
    $folder = [IO.Path]::Combine($PackageRoot, $Package.ToLowerInvariant(), $Version.ToLowerInvariant())
    $metadataFile = if ($Mapping.MetadataFile) { [string]$Mapping.MetadataFile } else { $Package.ToLowerInvariant() + '.nuspec' }
    $license = if ($Mapping.Spdx) { "<license type=`"expression`">$($Mapping.Spdx)</license>" } else { "<license type=`"file`">$(@($Mapping.LicenseFiles)[0])</license>" }
    $copyright = [Security.SecurityElement]::Escape([string]$Mapping.Copyright)
    Write-FixtureFile (Join-Path $folder $metadataFile) "<?xml version=`"1.0`" encoding=`"utf-8`"?>`n<package xmlns=`"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd`"><metadata><id>$Package</id><version>$Version</version>$license<copyright>$copyright</copyright></metadata></package>`n"
    foreach ($file in @(@($Mapping.LicenseFiles) + @($Mapping.NoticeFiles))) { Write-FixtureFile (Join-Path $folder $file) "$marker for $Package $file`n" }
}

function New-Fixture {
    param([string]$Name, [string[]]$SkipDeps = @(), [string[]]$ExtraDeps = @(), [string]$PackageFolder, [string]$RuntimeRange, [string]$IlcLibrary)
    $root = Join-Path $WorkRoot $Name
    $packages = Join-Path $root 'nuget'
    $repository = Join-Path $root 'repo'
    $payload = Join-Path $root 'payload'
    foreach ($key in $map.Keys) { $parts = $key.Split('/'); Write-FixturePackage $packages $parts[0] $parts[1] $map[$key] }
    foreach ($id in $helperMap.Keys) { Write-FixturePackage $packages $id $helperVersion $helperMap[$id] }
    $libraries = [ordered]@{ 'PCSteward.Desktop/0.1.4' = @{ type = 'project' } }
    foreach ($key in $map.Keys) { if ($key -cne $inProcComKey[0] -and $SkipDeps -cnotcontains $key) { $libraries[$key] = @{ type = 'package' } } }
    foreach ($key in $ExtraDeps) { $libraries[$key] = @{ type = 'package' } }
    Write-FixtureFile (Join-Path $payload 'PCSteward.Desktop.deps.json') (ConvertTo-Json -Depth 5 -InputObject ([ordered]@{ libraries = $libraries }))
    Write-FixtureFile (Join-Path $repository 'Directory.Packages.props') "<Project><ItemGroup><PackageVersion Include=`"Microsoft.WindowsPackageManager.InProcCom`" Version=`"$inProcComVersion`" /><PackageVersion Include=`"Other`" Version=`"1.0.0`" /></ItemGroup></Project>`n"
    if (!$PackageFolder) { $PackageFolder = $packages + [IO.Path]::DirectorySeparatorChar }
    if (!$RuntimeRange) { $RuntimeRange = "[$helperVersion, $helperVersion]" }
    if (!$IlcLibrary) { $IlcLibrary = 'runtime.win-x64.Microsoft.DotNet.ILCompiler/' + $helperVersion }
    $assets = [ordered]@{
        packageFolders = [ordered]@{ $PackageFolder = @{} }
        libraries = [ordered]@{ $IlcLibrary = @{ type = 'package' }; ('Microsoft.DotNet.ILCompiler/' + $helperVersion) = @{ type = 'package' } }
        project = @{ frameworks = @{ 'net10.0-windows10.0.26100.0' = @{ downloadDependencies = @(@{ name = 'Microsoft.NETCore.App.Runtime.NativeAOT.win-x64'; version = $RuntimeRange }, @{ name = 'Microsoft.NETCore.App.Ref'; version = '[10.0.14, 10.0.14]' }) } } }
    }
    Write-FixtureFile ([IO.Path]::Combine($repository, 'src', 'PCSteward.ElevatedHelper', 'obj', 'project.assets.json')) (ConvertTo-Json -Depth 8 -InputObject $assets)
    [pscustomobject]@{ Root = $root; Packages = $packages; Repository = $repository; Payload = $payload }
}

function Invoke-Release($Fixture) { New-PCStewardReleaseThirdPartyNotices -RepositoryRoot $Fixture.Repository -PayloadRoot $Fixture.Payload }

# 3. The accepted case.
$good = New-Fixture -Name 'good'
$release = Invoke-Release $good
$text = [string]$release.Text
$headersOk = $true
foreach ($key in $map.Keys) {
    $parts = $key.Split('/')
    if ([regex]::Matches($text, '(?m)^' + [regex]::Escape("Package: $($parts[0]) $($parts[1])") + '\r?$').Count -ne 1) { $headersOk = $false }
    foreach ($file in @(@($map[$key].LicenseFiles) + @($map[$key].NoticeFiles))) {
        if ([regex]::Matches($text, '(?m)^' + [regex]::Escape("Included package file: $($parts[0].ToLowerInvariant())/$($parts[1].ToLowerInvariant())/$file") + '\r?$').Count -ne 1) { $headersOk = $false }
    }
}
foreach ($id in $helperMap.Keys) { if ([regex]::Matches($text, '(?m)^' + [regex]::Escape("Package: $id $helperVersion") + '\r?$').Count -ne 1) { $headersOk = $false } }
$results.accepted = [ordered]@{
    packageCount = [int]$release.PackageCount
    expectedPackageCount = $map.Count + $helperMap.Count
    everySectionOnce = $headersOk
    nonAsciiNoticeKept = $text.Contains($marker)
    copyrightKept = $text.Contains('Copyright metadata: ' + ([string][char]0x00A9) + ' Microsoft Corporation. All rights reserved.')
    standardMit = $text.Contains('Permission is hereby granted, free of charge')
    standardApache = $text.Contains('Apache License') -and $text.Contains('Version 2.0, January 2004')
    noLocalPath = !$text.Contains($WorkRoot) -and !$text.Contains($good.Packages)
    endsWithNewLine = $text.EndsWith([Environment]::NewLine)
    stable = ([string](Invoke-Release $good).Text -ceq $text)
}

# 4. Refusals: each starts from a fresh good fixture and changes one thing.
$refusals = [ordered]@{}
$f = New-Fixture -Name 'unmapped' -ExtraDeps @('Unreviewed.Package/1.0.0')
$refusals.unmappedPackage = Get-Failure { Invoke-Release $f }
$someKey = @($map.Keys | Where-Object { $_ -cne $inProcComKey[0] })[0]
$f = New-Fixture -Name 'unused' -SkipDeps @($someKey)
$refusals.unusedMapEntry = Get-Failure { Invoke-Release $f }
$f = New-Fixture -Name 'copyright'
$parts = $someKey.Split('/')
$nuspec = [IO.Path]::Combine($f.Packages, $parts[0].ToLowerInvariant(), $parts[1].ToLowerInvariant(), [string]$map[$someKey].MetadataFile)
Write-FixtureFile $nuspec ((Read-PCStewardUtf8Text $nuspec) -replace '<copyright>[^<]*</copyright>', '<copyright>Someone else</copyright>')
$refusals.changedCopyright = Get-Failure { Invoke-Release $f }
$spdxKey = @($map.Keys | Where-Object { $map[$_].Spdx -ceq 'MIT' -and $_ -cne $inProcComKey[0] })[0]
$f = New-Fixture -Name 'spdx'
$parts = $spdxKey.Split('/')
$nuspec = [IO.Path]::Combine($f.Packages, $parts[0].ToLowerInvariant(), $parts[1].ToLowerInvariant(), [string]$map[$spdxKey].MetadataFile)
Write-FixtureFile $nuspec ((Read-PCStewardUtf8Text $nuspec).Replace('>MIT</license>', '>GPL-3.0-only</license>'))
$refusals.changedSpdx = Get-Failure { Invoke-Release $f }
$noticeKey = @($map.Keys | Where-Object { @($map[$_].NoticeFiles).Count -gt 0 })[0]
$parts = $noticeKey.Split('/')
$f = New-Fixture -Name 'empty'
[IO.File]::WriteAllBytes([IO.Path]::Combine($f.Packages, $parts[0].ToLowerInvariant(), $parts[1].ToLowerInvariant(), @($map[$noticeKey].NoticeFiles)[0]), [byte[]]@())
$refusals.emptyNotice = Get-Failure { Invoke-Release $f }
$f = New-Fixture -Name 'missing'
[IO.File]::Delete([IO.Path]::Combine($f.Packages, $parts[0].ToLowerInvariant(), $parts[1].ToLowerInvariant(), @($map[$noticeKey].NoticeFiles)[0]))
$refusals.missingNotice = Get-Failure { Invoke-Release $f }
$f = New-Fixture -Name 'range' -RuntimeRange "[$helperVersion, )"
$refusals.inexactRuntimePack = Get-Failure { Invoke-Release $f }
$f = New-Fixture -Name 'noilc' -IlcLibrary 'Something.Else/1.0.0'
$refusals.missingHelperRuntime = Get-Failure { Invoke-Release $f }
$f = New-Fixture -Name 'helpercopyright'
$ilc = @($helperMap.Keys)[1]
$nuspec = [IO.Path]::Combine($f.Packages, $ilc.ToLowerInvariant(), $helperVersion, $ilc.ToLowerInvariant() + '.nuspec')
Write-FixtureFile $nuspec ((Read-PCStewardUtf8Text $nuspec) -replace '<copyright>[^<]*</copyright>', '<copyright>Someone else</copyright>')
$refusals.helperCopyright = Get-Failure { Invoke-Release $f }
$f = New-Fixture -Name 'pins'
$props = Join-Path $f.Repository 'Directory.Packages.props'
Write-FixtureFile $props ((Read-PCStewardUtf8Text $props).Replace('<PackageVersion Include="Other"', '<PackageVersion Include="Microsoft.WindowsPackageManager.InProcCom" Version="9.9.9" /><PackageVersion Include="Other"'))
$refusals.twoInProcComPins = Get-Failure { Invoke-Release $f }
$f = New-Fixture -Name 'folder' -PackageFolder 'relative/nuget/'
$refusals.relativePackageFolder = Get-Failure { Invoke-Release $f }
$f = New-Fixture -Name 'nodeps'
[IO.File]::Delete((Join-Path $f.Payload 'PCSteward.Desktop.deps.json'))
$refusals.missingDeps = Get-Failure { Invoke-Release $f }
$results.refusals = $refusals

[IO.File]::WriteAllText((Join-Path $WorkRoot 'results.json'), (ConvertTo-Json -Depth 5 -InputObject $results), $utf8)
Write-Output 'PASS: third-party notices fixtures'
