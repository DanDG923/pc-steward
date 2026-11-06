param(
    [Parameter(Mandatory)][string]$RepositoryRoot,
    [Parameter(Mandatory)][string]$WorkRoot
)
# Task 50 G3 (FR-UPD-15, TM-03/TM-13, ADR-022): fixtures for scripts/New-OwnerOnlyCandidate.ps1. Runs under pwsh 7
# (Linux or Windows) and Windows PowerShell 5.1. It parses the builder and its CI launcher, applies AST policy checks,
# and executes the REAL text of the builder's pure functions (taken from the parsed AST) on synthetic inputs in WorkRoot.
# Under Windows PowerShell 5.1 it also feeds the written manifest to the installer's Read-OwnerOnlyCandidateManifest.
# No dotnet, git, elevation, signing, certificate store, network, Program Files or installed root is used.
$ErrorActionPreference = 'Stop'
$isWindowsPowerShell = $PSVersionTable.PSEdition -ne 'Core'
if ($isWindowsPowerShell) {
    foreach ($module in @('Management', 'Utility', 'Security')) { Import-Module ([IO.Path]::Combine($PSHOME, 'Modules', "Microsoft.PowerShell.$module", "Microsoft.PowerShell.$module.psd1")) -Force }
}
$scripts = Join-Path $RepositoryRoot 'scripts'
$builderPath = Join-Path $scripts 'New-OwnerOnlyCandidate.ps1'
$launcherPath = Join-Path $scripts 'Test-OwnerOnlyCandidateBuilderCI.ps1'
$results = [ordered]@{ shell = ($PSVersionTable.PSEdition + ' ' + $PSVersionTable.PSVersion.ToString()) }
function Read-ScriptAst([string]$Path) {
    $tokens = $null; $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors)
    [pscustomobject]@{ Ast = $ast; Errors = @($errors).Count }
}
function Get-Failure([scriptblock]$Action) { try { & $Action | Out-Null; return $null } catch { return $_.Exception.Message } }

# 1. Parse check.
$builder = Read-ScriptAst $builderPath
$launcher = Read-ScriptAst $launcherPath
$results.parseErrors = [ordered]@{
    'New-OwnerOnlyCandidate.ps1' = $builder.Errors
    'Test-OwnerOnlyCandidateBuilderCI.ps1' = $launcher.Errors
    'Fixtures/OwnerOnlyCandidateBuilder.ps1' = (Read-ScriptAst $PSCommandPath).Errors
}

# 2. AST policy. Violations are collected as text; the C# test requires none.
$violations = [Collections.Generic.List[string]]::new()
$networkOrSigningText = @('RunAs', 'Cert:', 'WebClient', 'WebRequest', 'HttpClient', 'Net.Sockets', 'DownloadString', 'DownloadFile', 'signtool', 'EncodedCommand', 'ExecutionPolicy')
$commonForbidden = @('Invoke-Expression', 'iex', 'Set-AuthenticodeSignature', 'New-SelfSignedCertificate', 'Import-Certificate', 'Import-PfxCertificate',
    'Invoke-WebRequest', 'iwr', 'Invoke-RestMethod', 'irm', 'curl', 'wget', 'Start-BitsTransfer', 'certutil', 'certutil.exe', 'Set-ExecutionPolicy',
    'Invoke-Command', 'icm', 'New-PSSession', 'Enter-PSSession', 'Start-Job', 'msiexec', 'msiexec.exe', 'winget', 'choco', 'runas', 'runas.exe')
$builderOnlyForbidden = @('Start-Process', 'saps', 'start', 'cmd', 'cmd.exe', 'Add-Type', 'Remove-Item', 'rm', 'del', 'ri', 'rd', 'rmdir', 'erase',
    'Set-Acl', 'Set-ItemProperty', 'New-ItemProperty', 'New-Service', 'Register-ScheduledTask', 'Stop-Process', 'kill', 'Restart-Computer',
    'dotnet', 'dotnet.exe', 'git', 'git.exe', 'powershell', 'powershell.exe', 'pwsh', 'pwsh.exe')
# A canary script proves the dynamic-code and process-creation bans below actually fire.
$canaryText = "[scriptblock]::Create('x')`n[Management.Automation.ScriptBlock]::Create('x')`n`$ExecutionContext.InvokeCommand.InvokeScript('x')`nNew-Object System.Diagnostics.Process`nNew-Object -TypeName 'Diagnostics.ProcessStartInfo'`n[Diagnostics.Process]::Start('x')`n"
$canary = [Management.Automation.Language.Parser]::ParseInput($canaryText, [ref]$null, [ref]$null)
foreach ($subject in @(@{ Name = 'builder'; Ast = $builder.Ast; Forbidden = $commonForbidden + $builderOnlyForbidden }, @{ Name = 'launcher'; Ast = $launcher.Ast; Forbidden = $commonForbidden }, @{ Name = 'canary'; Ast = $canary; Forbidden = $commonForbidden })) {
    foreach ($invoke in $subject.Ast.FindAll({ param($node) $node -is [Management.Automation.Language.InvokeMemberExpressionAst] -and $node.Static -and $node.Expression -is [Management.Automation.Language.TypeExpressionAst] }, $true)) {
        if ($invoke.Expression.TypeName.FullName -match '(^|\.)ScriptBlock$' -and [string]$invoke.Member.Value -ieq 'Create') { $violations.Add("$($subject.Name): [scriptblock]::Create") }
    }
    foreach ($variable in $subject.Ast.FindAll({ param($node) $node -is [Management.Automation.Language.VariableExpressionAst] -and $node.VariablePath.UserPath -ieq 'ExecutionContext' }, $true)) {
        $violations.Add("$($subject.Name): `$ExecutionContext")
    }
    foreach ($command in $subject.Ast.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] }, $true)) {
        $name = $command.GetCommandName()
        if ($name -and ($subject.Forbidden -contains $name)) { $violations.Add("$($subject.Name): forbidden command $name") }
        if ($name -ieq 'New-Object' -and $command.Extent.Text -match 'Diagnostics\.Process') { $violations.Add("$($subject.Name): New-Object Diagnostics.Process") }
        foreach ($parameter in @($command.CommandElements | Where-Object { $_ -is [Management.Automation.Language.CommandParameterAst] })) {
            if ($parameter.ParameterName -ieq 'Verb') { $violations.Add("$($subject.Name): -Verb parameter") }
        }
    }
    foreach ($text in $subject.Ast.FindAll({ param($node) $node -is [Management.Automation.Language.StringConstantExpressionAst] -or $node -is [Management.Automation.Language.ExpandableStringExpressionAst] }, $true)) {
        foreach ($needle in $networkOrSigningText) { if ($text.Value -and $text.Value.IndexOf($needle, [StringComparison]::OrdinalIgnoreCase) -ge 0) { $violations.Add("$($subject.Name): string mentions $needle") } }
    }
    foreach ($type in $subject.Ast.FindAll({ param($node) $node -is [Management.Automation.Language.TypeExpressionAst] -or $node -is [Management.Automation.Language.TypeConstraintAst] }, $true)) {
        $typeName = $type.TypeName.FullName
        if ($typeName -match '(^|\.)Net\.' -or $typeName -match 'Diagnostics\.Process' -or $typeName -match 'X509') { $violations.Add("$($subject.Name): type $typeName") }
    }
}
# Every '&' invocation in the builder runs a resolved executable variable with structured arguments only: a splatted
# variable or an array expression of constants/variables. No string is assembled into a command line.
$invocations = [ordered]@{}
foreach ($command in $builder.Ast.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] -and $node.InvocationOperator -eq [Management.Automation.Language.TokenKind]::Ampersand }, $true)) {
    $target = $command.CommandElements[0]
    if ($target -isnot [Management.Automation.Language.VariableExpressionAst]) { $violations.Add("builder: & target is not a variable: $($command.Extent.Text)"); continue }
    $key = $target.VariablePath.UserPath.ToLowerInvariant()
    if (!$invocations.Contains($key)) { $invocations[$key] = 0 }
    $invocations[$key]++
    foreach ($argument in @($command.CommandElements | Select-Object -Skip 1)) {
        $structured = ($argument -is [Management.Automation.Language.VariableExpressionAst] -and $argument.Splatted) -or ($argument -is [Management.Automation.Language.ArrayExpressionAst])
        if (!$structured) { $violations.Add("builder: unstructured argument in $($command.Extent.Text)"); continue }
        foreach ($leaf in $argument.FindAll({ param($node) $node -is [Management.Automation.Language.ExpandableStringExpressionAst] -or $node -is [Management.Automation.Language.BinaryExpressionAst] -or $node -is [Management.Automation.Language.SubExpressionAst] }, $true)) {
            $violations.Add("builder: computed argument in $($command.Extent.Text)")
        }
    }
}
$results.invocations = $invocations
$dotSources = @($builder.Ast.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] -and $node.InvocationOperator -eq [Management.Automation.Language.TokenKind]::Dot }, $true))
$results.dotSources = @($dotSources | ForEach-Object { $_.Extent.Text })
$splattedDotnet = @($builder.Ast.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] -and $node.InvocationOperator -eq [Management.Automation.Language.TokenKind]::Ampersand -and $node.CommandElements[0] -is [Management.Automation.Language.VariableExpressionAst] -and $node.CommandElements[0].VariablePath.UserPath -ceq 'Dotnet' }, $true))
$results.splattedDotnet = ($splattedDotnet.Count -eq 1 -and $splattedDotnet[0].CommandElements.Count -eq 2 -and $splattedDotnet[0].CommandElements[1].Splatted -and $splattedDotnet[0].CommandElements[1].VariablePath.UserPath -ceq 'Arguments')
# Build-step arguments are typed string arrays of constants and variables (no concatenation or interpolation).
$stepsFunction = @($builder.Ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Get-OwnerOnlyCandidateBuildSteps' }, $true))[0]
$argumentValues = 0
foreach ($pair in @($stepsFunction.FindAll({ param($node) $node -is [Management.Automation.Language.HashtableAst] }, $true) | ForEach-Object { $_.KeyValuePairs })) {
    if ([string]$pair.Item1.Value -cne 'Arguments') { continue }
    $argumentValues++
    $value = $pair.Item2
    if ($value.Extent.Text -notmatch '\A\[string\[\]\]@\(') { $violations.Add("builder: build-step arguments are not a [string[]] array: $($value.Extent.Text)") }
    foreach ($leaf in $value.FindAll({ param($node) $node -is [Management.Automation.Language.ExpandableStringExpressionAst] -or $node -is [Management.Automation.Language.BinaryExpressionAst] -or $node -is [Management.Automation.Language.SubExpressionAst] -or $node -is [Management.Automation.Language.InvokeMemberExpressionAst] }, $true)) {
        $violations.Add("builder: computed build-step argument: $($leaf.Extent.Text)")
    }
}
$results.buildStepArgumentArrays = $argumentValues
$results.canaryViolations = @($violations | Where-Object { $_.StartsWith('canary: ', [StringComparison]::Ordinal) })
$results.violations = @($violations | Where-Object { !$_.StartsWith('canary: ', [StringComparison]::Ordinal) })

# 3. Real pure functions from the builder.
foreach ($name in @('Get-OwnerOnlyCandidateBuildSteps', 'Test-OwnerOnlyCandidateSdkVersion', 'Assert-OwnerOnlyCandidateOutputRootRule', 'Get-OwnerOnlyCandidateFileSha256',
        'Get-OwnerOnlyCandidateFileEntries', 'Get-OwnerOnlyCandidateExpectedXbf', 'ConvertTo-OwnerOnlyCandidateManifestBytes', 'Write-OwnerOnlyCandidateManifest', 'Copy-OwnerOnlyCandidateFile',
        'Get-OwnerOnlyCandidateProbeTypeNames', 'Test-OwnerOnlyCandidateProbeMark')) {
    $definition = @($builder.Ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }.GetNewClosure(), $true))
    if ($definition.Count -ne 1) { throw "ASSERT: expected one builder function $name, found $($definition.Count)." }
    . ([scriptblock]::Create($definition[0].Extent.Text))
}
$results.steps = @(Get-OwnerOnlyCandidateBuildSteps -RepositoryRoot 'C:\src\pc steward' -Configuration 'Release' -DesktopPublishDirectory 'C:\out dir\publish-staging\desktop' -HelperPublishDirectory 'C:\out dir\publish-staging\helper' |
    ForEach-Object { [ordered]@{ name = $_.Name; type = $_.Arguments.GetType().FullName; arguments = @($_.Arguments) } })

$sdk = [ordered]@{}
foreach ($vector in @(
        @('exact', '10.0.403', 'latestPatch', '10.0.403'), @('patch-up', '10.0.403', 'latestPatch', '10.0.405'), @('patch-down', '10.0.403', 'latestPatch', '10.0.402'),
        @('band-up', '10.0.403', 'latestPatch', '10.0.500'), @('band-down', '10.0.403', 'latestPatch', '10.0.399'), @('minor', '10.0.403', 'latestPatch', '10.1.403'),
        @('major', '10.0.403', 'latestPatch', '11.0.403'), @('prerelease', '10.0.403', 'latestPatch', '10.0.404-preview.1'), @('disable-up', '10.0.403', 'disable', '10.0.404'),
        @('disable-exact', '10.0.403', 'disable', '10.0.403'), @('feature-roll', '10.0.403', 'latestFeature', '10.0.500'), @('garbage', '10.0.403', 'latestPatch', 'not a version'))) {
    $outcome = try { if (Test-OwnerOnlyCandidateSdkVersion -Pinned $vector[1] -RollForward $vector[2] -Actual $vector[3]) { 'exact' } else { 'rolled' } } catch { 'refused' }
    $sdk[$vector[0]] = $outcome
}
$results.sdk = $sdk

$forbiddenRoots = @('C:\Program Files', 'C:\Program Files (x86)', 'C:\Windows', 'C:\src\pc-steward')
$forbiddenPrefixes = @('C:\Users\o\AppData\Local\PCSteward')
$outputRoots = [ordered]@{}
foreach ($vector in @(
        @('temp', 'C:\Users\o\AppData\Local\Temp\pcs-candidate-1'), @('runner-temp', 'D:\a\_temp\pcsteward-candidate-1\candidate'),
        @('program-files', 'C:\Program Files\PC Steward'), @('program-files-case', 'c:\program files\x'), @('program-files-x86', 'C:\Program Files (x86)\x'),
        @('program-files-itself', 'C:\Program Files'), @('windows', 'C:\Windows\Temp\x'), @('repository', 'C:\src\pc-steward\artifacts\candidate'), @('contains-repository', 'C:\src'),
        @('data-root', 'C:\Users\o\AppData\Local\PCSteward'), @('data-prefix', 'C:\Users\o\AppData\Local\PCSteward-Task49'), @('volume-root', 'C:\'),
        @('relative', 'relative\path'), @('unc', '\\server\share\x'), @('traversal', 'C:\Users\o\..\x'), @('short-name', 'C:\Users\OWNER~1\x'),
        @('trailing-dot', 'C:\Users\o\x.'), @('stream', 'C:\Users\o\x:ads'), @('forward-slash', 'C:/Users/o/x'), @('empty-segment', 'C:\Users\o\\x'), @('trailing-separator', 'C:\Users\o\x\'))) {
    $outputRoots[$vector[0]] = if (Get-Failure { Assert-OwnerOnlyCandidateOutputRootRule -Path $vector[1] -ForbiddenRoots $forbiddenRoots -ForbiddenPrefixes $forbiddenPrefixes }) { 'refused' } else { 'accepted' }
}
$results.outputRoots = $outputRoots

# 4. Manifest writer on a synthetic payload tree.
function New-FixturePe() {
    # The PayloadIntegrity fixture's minimal FileAlignment-aligned PE32+ x64 image with no certificate or overlay.
    $bytes = [byte[]]::new(1024)
    $bytes[0] = 77; $bytes[1] = 90
    [BitConverter]::GetBytes([int]128).CopyTo($bytes, 60)
    $bytes[128] = 80; $bytes[129] = 69
    [BitConverter]::GetBytes([uint16]0x8664).CopyTo($bytes, 132)
    [BitConverter]::GetBytes([uint16]1).CopyTo($bytes, 134)
    [BitConverter]::GetBytes([uint16]240).CopyTo($bytes, 148)
    [BitConverter]::GetBytes([uint16]0x20b).CopyTo($bytes, 152)
    [BitConverter]::GetBytes([uint32]512).CopyTo($bytes, 188)
    [BitConverter]::GetBytes([uint32]512).CopyTo($bytes, 212)
    [BitConverter]::GetBytes([uint32]16).CopyTo($bytes, 260)
    [BitConverter]::GetBytes([uint32]512).CopyTo($bytes, 408)
    [BitConverter]::GetBytes([uint32]512).CopyTo($bytes, 412)
    for ($index = 512; $index -lt 1024; $index++) { $bytes[$index] = [byte]($index % 251) }
    , $bytes
}
function Write-FixtureFile([string]$Root, [string]$Relative, [byte[]]$Bytes) {
    $path = [IO.Path]::Combine([string[]](@($Root) + $Relative.Split('\')))
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
    [IO.File]::WriteAllBytes($path, $Bytes)
}
$candidateRoot = Join-Path $WorkRoot 'candidate'
$tree = Join-Path $candidateRoot 'payload'
$pe = New-FixturePe
$capability = [IO.File]::ReadAllBytes([IO.Path]::Combine($RepositoryRoot, 'src', 'PCSteward.ElevatedHelper', 'PCSteward.DeploymentCapability.txt'))
foreach ($leaf in @('PCSteward.Desktop.exe', 'PCSteward.ElevatedHelper.exe')) { Write-FixtureFile $tree $leaf $pe }
foreach ($relative in @('PCSteward.ScheduledHost.dll', 'App.xbf', 'PCSteward.Desktop.pri', 'Resources\ColorTokens.xbf', 'Resources\Typography.xbf', 'Assets\pc-steward.ico', 'en-us\Microsoft.ui.xaml.dll.mui', 'a.txt', 'B.txt', 'with space.dat')) {
    Write-FixtureFile $tree $relative ([Text.Encoding]::ASCII.GetBytes("fixture $relative"))
}
Write-FixtureFile $tree 'empty.bin' ([byte[]]@())
Write-FixtureFile $tree 'PCSteward.DeploymentCapability.txt' $capability
$entries = @(Get-OwnerOnlyCandidateFileEntries -Root $tree)
$manifestPath = Join-Path $candidateRoot 'candidate-manifest.json'
$manifestSha256 = Write-OwnerOnlyCandidateManifest -Entries $entries -Path $manifestPath
$results.manifest = [ordered]@{ path = $manifestPath; payloadRoot = $tree; sha256 = $manifestSha256; entries = $entries.Count }
$results.rendering = [ordered]@{
    stable = ([Convert]::ToBase64String((ConvertTo-OwnerOnlyCandidateManifestBytes -Entries $entries)) -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes($manifestPath)))
    reversedInputSameBytes = ([Convert]::ToBase64String((ConvertTo-OwnerOnlyCandidateManifestBytes -Entries ([object[]]($entries[($entries.Count - 1)..0])))) -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes($manifestPath)))
}

$sample = $entries[0]
$writerRefusals = [ordered]@{
    overwrite = Get-Failure { Write-OwnerOnlyCandidateManifest -Entries $entries -Path $manifestPath }
    noncanonical = Get-Failure { ConvertTo-OwnerOnlyCandidateManifestBytes -Entries @([pscustomobject]@{ path = 'bad+name.txt'; bytes = 1; sha256 = $sample.sha256 }) }
    traversal = Get-Failure { ConvertTo-OwnerOnlyCandidateManifestBytes -Entries @([pscustomobject]@{ path = '..\x.txt'; bytes = 1; sha256 = $sample.sha256 }) }
    forwardSlash = Get-Failure { ConvertTo-OwnerOnlyCandidateManifestBytes -Entries @([pscustomobject]@{ path = 'Resources/x.xbf'; bytes = 1; sha256 = $sample.sha256 }) }
    reservedName = Get-Failure { ConvertTo-OwnerOnlyCandidateManifestBytes -Entries @([pscustomobject]@{ path = 'Resources\NUL.txt'; bytes = 1; sha256 = $sample.sha256 }) }
    caseCollision = Get-Failure { ConvertTo-OwnerOnlyCandidateManifestBytes -Entries @([pscustomobject]@{ path = 'A.txt'; bytes = 1; sha256 = $sample.sha256 }, [pscustomobject]@{ path = 'a.txt'; bytes = 1; sha256 = $sample.sha256 }) }
    lowerCaseHash = Get-Failure { ConvertTo-OwnerOnlyCandidateManifestBytes -Entries @([pscustomobject]@{ path = 'x.txt'; bytes = 1; sha256 = $sample.sha256.ToLowerInvariant() }) }
    negativeLength = Get-Failure { ConvertTo-OwnerOnlyCandidateManifestBytes -Entries @([pscustomobject]@{ path = 'x.txt'; bytes = -1; sha256 = $sample.sha256 }) }
    tooMany = Get-Failure { ConvertTo-OwnerOnlyCandidateManifestBytes -Entries ([object[]](1..513 | ForEach-Object { [pscustomobject]@{ path = "f$_.txt"; bytes = 1; sha256 = $sample.sha256 } })) }
    oversize = Get-Failure { ConvertTo-OwnerOnlyCandidateManifestBytes -Entries @([pscustomobject]@{ path = 'x.txt'; bytes = 256MB + 1; sha256 = $sample.sha256 }) }
}
$results.writerRefusals = $writerRefusals

# Copy rule: new file copied, identical duplicate tolerated, different duplicate refused (no silent overwrite).
$copyRoot = Join-Path $WorkRoot 'copy'
$copyTarget = [IO.Path]::Combine($copyRoot, 'nested', 'a.txt')
$results.copy = [ordered]@{
    first = Copy-OwnerOnlyCandidateFile -Source ([IO.Path]::Combine($tree, 'a.txt')) -Destination $copyTarget -Origin 'fixture'
    identical = Copy-OwnerOnlyCandidateFile -Source ([IO.Path]::Combine($tree, 'a.txt')) -Destination $copyTarget -Origin 'fixture'
    conflict = Get-Failure { Copy-OwnerOnlyCandidateFile -Source ([IO.Path]::Combine($tree, 'B.txt')) -Destination $copyTarget -Origin 'fixture' }
}

# Reparse points are refused while enumerating (a junction on Windows, a symbolic link elsewhere).
$reparseTree = Join-Path $WorkRoot 'reparse'
Write-FixtureFile $reparseTree 'real\x.txt' ([byte[]]@(1))
$linkPath = Join-Path $reparseTree 'link'
if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { New-Item -ItemType Junction -Path $linkPath -Target (Join-Path $reparseTree 'real') | Out-Null }
else { New-Item -ItemType SymbolicLink -Path $linkPath -Target (Join-Path $reparseTree 'real') | Out-Null }
$results.reparseRefused = Get-Failure { Get-OwnerOnlyCandidateFileEntries -Root $reparseTree }
if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { [IO.Directory]::Delete($linkPath) } else { [IO.File]::Delete($linkPath) }

# Expected XBF set: one per source XAML, bin/obj skipped.
$project = Join-Path $WorkRoot 'project'
foreach ($relative in @('App.xaml', 'App.xaml.cs', 'Resources\ColorTokens.xaml', 'Views\Sub\Page.xaml', 'bin\Release\Stale.xaml', 'obj\Generated.xaml')) { Write-FixtureFile $project $relative ([byte[]]@(1)) }
$results.expectedXbf = [string[]](Get-OwnerOnlyCandidateExpectedXbf -ProjectDirectory $project)

# 4b. Task 50 S7 obligation 1: the probe-build mark (rule pc-steward-probe-build-mark:1) over synthetic payload assemblies.
function Write-ProbeAssemblies([string]$Root, [hashtable]$Drop) {
    $names = Get-OwnerOnlyCandidateProbeTypeNames
    foreach ($leaf in $names.Keys) {
        if ($Drop.ContainsKey($leaf) -and $null -eq $Drop[$leaf]) { continue }
        $content = 'MZ' + [char]0 + 'header' + [char]0
        foreach ($name in $names[$leaf]) { if (!($Drop.ContainsKey($leaf) -and $Drop[$leaf] -ceq $name)) { $content += $name + [char]0 } }
        Write-FixtureFile $Root $leaf ([Text.Encoding]::ASCII.GetBytes($content))
    }
}
$probeMark = [ordered]@{}
$probeRoot = Join-Path $WorkRoot 'probe-mark'
foreach ($vector in @(
        @('included', $false, $true, @{}),
        @('dirty', $true, $true, @{}),
        @('not-descendant', $false, $false, @{}),
        @('missing-desktop', $false, $true, @{ 'PCSteward.Desktop.dll' = $null }),
        @('missing-gate-type', $false, $true, @{ 'PCSteward.Desktop.dll' = 'QualificationProbeLaunchGate' }),
        @('missing-codec-type', $false, $true, @{ 'PCSteward.Execution.dll' = 'QualificationProbeWireCodec' }),
        @('missing-native-type', $false, $true, @{ 'PCSteward.Provider.Chocolatey.dll' = 'NativeChocolateyProbePort' }))) {
    $root = Join-Path $probeRoot $vector[0]
    [void][IO.Directory]::CreateDirectory($root)
    Write-ProbeAssemblies $root $vector[3]
    $mark = Test-OwnerOnlyCandidateProbeMark -SourceDirty $vector[1] -DescendsFromProbeCommit $vector[2] -PayloadRoot $root
    $probeMark[$vector[0]] = [ordered]@{ included = [bool]$mark.Included; detail = [string]$mark.Detail }
}
# A name that appears only as a prefix of a longer name (no NUL terminator right after it) is not the type.
$prefixRoot = Join-Path $probeRoot 'prefix-only'
[void][IO.Directory]::CreateDirectory($prefixRoot)
Write-ProbeAssemblies $prefixRoot @{ 'PCSteward.Desktop.dll' = 'QualificationProbeLaunchGate' }
[IO.File]::WriteAllBytes((Join-Path $prefixRoot 'PCSteward.Desktop.dll'), [Text.Encoding]::ASCII.GetBytes('MZ' + [char]0 + 'QualificationProbeLaunchGateX' + [char]0 + 'QualificationProbeEntry' + [char]0))
$mark = Test-OwnerOnlyCandidateProbeMark -SourceDirty $false -DescendsFromProbeCommit $true -PayloadRoot $prefixRoot
$probeMark['prefix-only'] = [ordered]@{ included = [bool]$mark.Included; detail = [string]$mark.Detail }
$results.probeMark = $probeMark
$results.probeTypeNames = Get-OwnerOnlyCandidateProbeTypeNames

# 5. The installer's own reader (Windows PowerShell 5.1 only: it compiles Windows file primitives).
if ($isWindowsPowerShell -and [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
    . (Join-Path $scripts 'OwnerOnlyPayloadIntegrity.ps1')
    $candidate = Read-OwnerOnlyCandidateManifest -Path $manifestPath -ExpectedSha256 $manifestSha256 -PayloadRoot $tree
    [PCSteward.Deployment.PayloadIntegrity]::AssertTree($tree, $candidate.Files)
    Assert-OwnerOnlyPayloadProfile -Origin $candidate.PayloadOrigin
    $image = [PCSteward.Deployment.PayloadIntegrity]::GetUnsignedImage((Join-Path $tree 'PCSteward.Desktop.exe'))
    # Drift probe: change one pinned byte, require refusal, then restore the exact bytes so the C# test's independent
    # re-derivation of the manifest from the tree still sees the pinned payload (the probe must not leak into it).
    $drift = Join-Path $tree 'a.txt'; $pinnedBytes = [IO.File]::ReadAllBytes($drift); [IO.File]::AppendAllText($drift, 'x')
    try { $driftRefused = Get-Failure { [PCSteward.Deployment.PayloadIntegrity]::AssertTree($tree, $candidate.Files) } }
    finally { [IO.File]::WriteAllBytes($drift, $pinnedBytes) }
    $restoredAccepted = $null -eq (Get-Failure { [PCSteward.Deployment.PayloadIntegrity]::AssertTree($tree, $candidate.Files) })
    $results.integrity = [ordered]@{ ran = $true; accepted = $true; payloadOrigin = $candidate.PayloadOrigin; helperProtocol = $candidate.HelperProtocol; files = $candidate.Files.Count; imageBytes = $image.bytes; driftRefused = $driftRefused; restoredAccepted = $restoredAccepted }
}
else {
    $results.integrity = [ordered]@{ ran = $false; reason = 'Read-OwnerOnlyCandidateManifest compiles Windows file primitives and runs under Windows PowerShell 5.1 only.' }
}

[IO.File]::WriteAllText((Join-Path $WorkRoot 'results.json'), (ConvertTo-Json -InputObject $results -Depth 6), [Text.UTF8Encoding]::new($false))
Write-Output 'PASS: owner-only candidate builder fixtures'
