param(
    [Parameter(Mandatory)][string]$RepositoryRoot,
    [Parameter(Mandatory)][string]$WorkRoot
)
# Task 50 S7 (FR-UPD-15, TM-03/04/07/13; qualification probe spec sections 5.3, 8 and 9): fixtures for
# scripts/evidence/Task50-QualificationProbe.ps1. Runs under pwsh 7 (Linux or Windows) and Windows PowerShell 5.1. It parses
# the script, applies AST policy checks and executes the REAL text of its pure functions (taken from the parsed AST) on
# the result documents the C# test wrote with the real writer (WorkRoot\cases.json) and on synthetic inputs in WorkRoot.
# The host steps (C1 reads, C3 launch, C7 deletion of the real result folder) never run here.
$ErrorActionPreference = 'Stop'
# The script runs its functions under StrictMode 3.0; so do these fixtures.
Set-StrictMode -Version 3.0
$scriptPath = [IO.Path]::Combine($RepositoryRoot, 'scripts', 'evidence', 'Task50-QualificationProbe.ps1')
$results = [ordered]@{ shell = ($PSVersionTable.PSEdition + ' ' + $PSVersionTable.PSVersion.ToString()) }
function Get-Failure([scriptblock]$Action) { try { & $Action | Out-Null; return $null } catch { return [string]$_.Exception.Message } }

# 1. Parse and AST policy.
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$tokens, [ref]$errors)
$results.parseErrors = @($errors).Count
$violations = [Collections.Generic.List[string]]::new()
$forbiddenCommands = @('Invoke-Expression', 'iex', 'Invoke-Command', 'icm', 'Start-Job', 'New-PSSession', 'Enter-PSSession', 'Remove-Item', 'rm', 'del',
    'ri', 'rd', 'rmdir', 'erase', 'Set-Content', 'Add-Content', 'Out-File', 'New-Item', 'Copy-Item', 'Move-Item', 'Rename-Item', 'Start-Transcript',
    'Set-Acl', 'Set-ItemProperty', 'New-ItemProperty', 'Export-Clixml', 'Export-Csv', 'Invoke-WebRequest', 'iwr', 'Invoke-RestMethod', 'irm',
    'curl', 'wget', 'Start-BitsTransfer', 'choco', 'choco.exe', 'runas', 'runas.exe', 'cmd', 'cmd.exe', 'powershell', 'powershell.exe', 'pwsh', 'pwsh.exe',
    'Add-Type', 'Set-ExecutionPolicy', 'Register-ScheduledTask', 'Stop-Process', 'kill', 'Set-AuthenticodeSignature', 'certutil', 'msiexec', 'winget',
    'Tee-Object', 'Set-Variable', 'Import-Module')
$startProcess = [Collections.Generic.List[string]]::new()
foreach ($command in $ast.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] }, $true)) {
    $name = $command.GetCommandName()
    if ($command.InvocationOperator -ne [Management.Automation.Language.TokenKind]::Unknown) { $violations.Add("invocation operator: $($command.Extent.Text)") }
    if ($name -and ($forbiddenCommands -contains $name)) { $violations.Add("forbidden command $name") }
    foreach ($parameter in @($command.CommandElements | Where-Object { $_ -is [Management.Automation.Language.CommandParameterAst] })) {
        if ($parameter.ParameterName -in @('Verb', 'Credential', 'NoNewWindow', 'RedirectStandardOutput', 'RedirectStandardError', 'RedirectStandardInput')) { $violations.Add("parameter -$($parameter.ParameterName)") }
    }
    if ($name -ieq 'Start-Process') { $startProcess.Add($command.Extent.Text) }
}
$results.startProcess = @($startProcess)
foreach ($invoke in $ast.FindAll({ param($node) $node -is [Management.Automation.Language.InvokeMemberExpressionAst] -and $node.Static -and $node.Expression -is [Management.Automation.Language.TypeExpressionAst] }, $true)) {
    $type = $invoke.Expression.TypeName.FullName
    $member = [string]$invoke.Member.Value
    if ($type -match '(^|\.)ScriptBlock$') { $violations.Add('[scriptblock]::' + $member) }
    if ($type -match '(^|\.)(IO\.File|IO\.Directory|File|Directory)$' -and $member -notin @('Open', 'ReadAllBytes', 'ReadAllText', 'Delete')) { $violations.Add("[$type]::$member") }
}
$deletes = @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.InvokeMemberExpressionAst] -and $node.Static -and [string]$node.Member.Value -ceq 'Delete' }, $true) | ForEach-Object { $_.Extent.Text })
$results.deletes = $deletes
foreach ($variable in $ast.FindAll({ param($node) $node -is [Management.Automation.Language.VariableExpressionAst] -and $node.VariablePath.UserPath -ieq 'ExecutionContext' }, $true)) { $violations.Add('$ExecutionContext') }
foreach ($text in $ast.FindAll({ param($node) $node -is [Management.Automation.Language.StringConstantExpressionAst] -or $node -is [Management.Automation.Language.ExpandableStringExpressionAst] }, $true)) {
    foreach ($needle in @('RunAs', '-Verb', 'WebClient', 'HttpClient', 'EncodedCommand', 'ExecutionPolicy', 'choco.exe ', 'upgrade ')) {
        if ($text.Value -and $text.Value.IndexOf($needle, [StringComparison]::OrdinalIgnoreCase) -ge 0) { $violations.Add("string mentions $needle") }
    }
}
foreach ($type in $ast.FindAll({ param($node) $node -is [Management.Automation.Language.TypeExpressionAst] -or $node -is [Management.Automation.Language.TypeConstraintAst] }, $true)) {
    if ($type.TypeName.FullName -match '(^|\.)Net\.|Diagnostics\.Process|X509|Microsoft\.Win32|Runspace') { $violations.Add('type ' + $type.TypeName.FullName) }
}
$results.violations = @($violations)

# 2. The real pure functions.
foreach ($name in @('Get-ProbeContract', 'Test-ProbeHex', 'ConvertFrom-ProbeCompactJson', 'Read-ProbeJsonValue', 'Read-ProbeJsonString', 'Assert-ProbeKeys',
        'Assert-ProbeMember', 'Assert-ProbeTimestamp', 'Assert-ProbeCount', 'Test-ProbeResultDocument', 'Test-ProbeBuildRecord', 'Read-ProbeLedgerPins',
        'Get-ProbeFileSha256', 'Get-ProbeTreeDigest', 'New-ProbeVerdict', 'Get-ProbeVerdicts', 'Format-ProbeVerdicts', 'Test-ProbeResultFolderRemovable',
        'Get-ProbeSafeMessage')) {
    $definition = @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }.GetNewClosure(), $true))
    if ($definition.Count -ne 1) { throw "ASSERT: expected one function $name, found $($definition.Count)." }
    . ([scriptblock]::Create($definition[0].Extent.Text))
}
$contract = Get-ProbeContract
$dump = [ordered]@{}
foreach ($key in $contract.Keys) { $dump[$key] = $contract[$key] }
$results.contract = $dump

# 3. The result documents written by the C# test with QualificationProbeResultDocument.Create (and their mutations).
$cases = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText((Join-Path $WorkRoot 'cases.json')))
$pins = [ordered]@{
    ledgerSha256 = $cases.pins.ledgerSha256; capabilityFileSha256 = $cases.pins.capabilityFileSha256; desktopImageSha256 = $cases.pins.desktopImageSha256
    helperImageSha256 = $cases.pins.helperImageSha256; signerThumbprint = $cases.pins.signerThumbprint; signerNotAfterUtc = ([string]$cases.pins.signerNotAfterUtc).Substring(3)
}
$same = [pscustomobject]@{ Database = 'D' * 64; Data = 'E' * 64; Chocolatey = 'F' * 64; ChocoProcesses = 0; OnlyResultFolderNew = $true }
$documents = [ordered]@{}
foreach ($case in $cases.cases) {
    $state = 'absent'; $result = $null; $reason = $null
    if ($case.file) {
        try { $result = Test-ProbeResultDocument ([IO.File]::ReadAllBytes((Join-Path $WorkRoot $case.file))); $state = 'valid' }
        catch { $state = 'malformed'; $reason = [string]$_.Exception.Message }
    }
    $after = switch ($case.e5) {
        'db-changed' { [pscustomobject]@{ Database = '0' * 64; Data = $same.Data; Chocolatey = $same.Chocolatey; ChocoProcesses = 0; OnlyResultFolderNew = $true } }
        'choco-running' { [pscustomobject]@{ Database = $same.Database; Data = $same.Data; Chocolatey = $same.Chocolatey; ChocoProcesses = 1; OnlyResultFolderNew = $true } }
        'missing' { $null }
        default { $same }
    }
    $verdicts = Get-ProbeVerdicts $state $result $pins $same $after
    $documents[$case.name] = [ordered]@{
        state = $state; reason = $reason
        verdicts = [ordered]@{ E1 = $verdicts['E1'].Verdict; A3 = $verdicts['A3'].Verdict; E2 = $verdicts['E2'].Verdict; E3 = $verdicts['E3'].Verdict; E5 = $verdicts['E5'].Verdict; OVERALL = $verdicts['OVERALL'].Verdict }
        details = [ordered]@{ E1 = $verdicts['E1'].Detail; A3 = $verdicts['A3'].Detail; E2 = $verdicts['E2'].Detail; E3 = $verdicts['E3'].Detail; E5 = $verdicts['E5'].Detail }
        lines = @(Format-ProbeVerdicts $verdicts)
    }
}
$results.documents = $documents

# 4. The G3 build record (S7 obligation 1).
$manifest = 'A1' * 32
function New-Record([hashtable]$Change, [string[]]$Drop = @()) {
    $record = [ordered]@{ schema = 'pc-steward-owner-only-candidate-build-v2'; validation = 'Pass'; manifestSha256 = $manifest; sourceCommit = ('ab' * 20)
        sourceDirty = $false; probeIncluded = $true; probeMark = 'pc-steward-probe-build-mark:1'; probeMarkDetail = 'included'; probeCommit = '2172f955a751ccc464b27075053c835790bc412f' }
    foreach ($key in $Change.Keys) { $record[$key] = $Change[$key] }
    foreach ($key in $Drop) { $record.Remove($key) }
    ConvertTo-Json -InputObject $record
}
$records = [ordered]@{}
foreach ($vector in @(
        @('valid', @{}, @()), @('v1-schema', @{ schema = 'pc-steward-owner-only-candidate-build-v1' }, @()), @('no-mark', @{}, @('probeIncluded')),
        @('not-included', @{ probeIncluded = $false; probeMarkDetail = 'commit-lacks-probe' }, @()), @('string-true', @{ probeIncluded = 'true' }, @()),
        @('other-manifest', @{ manifestSha256 = ('B2' * 32) }, @()), @('lowercase-manifest', @{ manifestSha256 = $manifest.ToLowerInvariant() }, @()),
        @('dirty', @{ sourceDirty = $true }, @()), @('other-rule', @{ probeMark = 'pc-steward-probe-build-mark:2' }, @()),
        @('other-commit', @{ probeCommit = ('cd' * 20) }, @()), @('not-validated', @{ validation = 'Fail' }, @()), @('detail-mismatch', @{ probeMarkDetail = 'source-dirty' }, @()))) {
    $records[$vector[0]] = try { 'commit:' + (Test-ProbeBuildRecord (New-Record $vector[1] $vector[2]) $manifest) } catch { [string]$_.Exception.Message }
}
$records['unreadable'] = try { Test-ProbeBuildRecord '{not json' $manifest } catch { [string]$_.Exception.Message }
$results.buildRecords = $records

# 5. The deployment record (ADR-022 ledger).
$signer = 'C3' * 20
$ledgerFill = @{ 'PCSteward.Desktop.exe' = 'a'; 'PCSteward.ElevatedHelper.exe' = 'b'; 'PCSteward.DeploymentCapability.txt' = 'c'; 'Other.dll' = 'd' }
function New-Ledger([hashtable]$Change, [string]$DropFile) {
    $files = @(foreach ($leaf in @('PCSteward.Desktop.exe', 'PCSteward.ElevatedHelper.exe', 'PCSteward.DeploymentCapability.txt', 'Other.dll')) {
            # Lower-case hex: the reader normalises the ledger's hashes to upper case.
            if ($leaf -cne $DropFile) { [ordered]@{ path = $leaf; sha256 = $ledgerFill[$leaf] * 64 } } })
    $ledger = [ordered]@{ schema = 'pc-steward-owner-only-protected-test-v6'; installRoot = 'C:\Program Files\PC Steward'; installerSid = 'S-1-5-21-1-2-3-1001'
        certificateThumbprint = $signer; certificateSubject = 'CN=x'; ownedCertificateStores = @('My'); createdAtUtc = '2026-11-06T00:00:00.0000000+00:00'; completed = $true
        files = $files; payloadOrigin = 'pinned-unsigned-v2'; candidateManifestSha256 = $manifest; executableImages = @() }
    foreach ($key in $Change.Keys) { $ledger[$key] = $Change[$key] }
    ConvertTo-Json -InputObject $ledger -Depth 4
}
$ledgers = [ordered]@{}
foreach ($vector in @(@('valid', @{}, ''), @('other-manifest', @{ candidateManifestSha256 = ('B2' * 32) }, ''), @('other-signer', @{ certificateThumbprint = ('D4' * 20) }, ''),
        @('v1-origin', @{ payloadOrigin = 'pinned-unsigned-v1' }, ''), @('incomplete', @{ completed = $false }, ''), @('other-schema', @{ schema = 'pc-steward-owner-only-protected-test-v5' }, ''),
        @('no-helper', @{}, 'PCSteward.ElevatedHelper.exe'))) {
    $ledgers[$vector[0]] = try { $value = Read-ProbeLedgerPins (New-Ledger $vector[1] $vector[2]) $manifest $signer; 'pins:' + (@($value.Keys | ForEach-Object { $value[$_] }) -join ',') } catch { [string]$_.Exception.Message }
}
$results.ledgers = $ledgers

# 6. The tree digest (C2/C4) on a synthetic tree; a reparse point is recorded and never followed.
$tree = Join-Path $WorkRoot 'tree'
[void][IO.Directory]::CreateDirectory((Join-Path $tree 'a'))
[void][IO.Directory]::CreateDirectory((Join-Path $tree 'c'))
[IO.File]::WriteAllBytes([IO.Path]::Combine($tree, 'a', 'x.txt'), [Text.Encoding]::ASCII.GetBytes('abc'))
[IO.File]::WriteAllBytes((Join-Path $tree 'b.txt'), [byte[]]@())
$first = Get-ProbeTreeDigest $tree
$second = Get-ProbeTreeDigest $tree
[IO.File]::WriteAllBytes((Join-Path $tree 'b.txt'), [byte[]]@(1))
$changed = Get-ProbeTreeDigest $tree
[IO.File]::WriteAllBytes((Join-Path $tree 'b.txt'), [byte[]]@())
$linkPath = Join-Path $tree 'link'
if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { New-Item -ItemType Junction -Path $linkPath -Target (Join-Path $tree 'a') | Out-Null }
else { New-Item -ItemType SymbolicLink -Path $linkPath -Target (Join-Path $tree 'a') | Out-Null }
$linked = Get-ProbeTreeDigest $tree
if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { [IO.Directory]::Delete($linkPath) } else { [IO.File]::Delete($linkPath) }
$results.tree = [ordered]@{ first = $first.Sha256; files = $first.Files; stable = ($first.Sha256 -ceq $second.Sha256); changed = $changed.Sha256; linked = $linked.Sha256; linkedFiles = $linked.Files }
$results.treeRootRefused = Get-Failure { Get-ProbeTreeDigest (Join-Path $WorkRoot 'absent') }

# 7. The C7 removal precondition.
$folders = [ordered]@{}
$base = Join-Path $WorkRoot 'folders'
$folders['absent'] = Test-ProbeResultFolderRemovable (Join-Path $base 'absent')
foreach ($vector in @(@('only-result', @('qualification-result.json'), $false), @('empty', @(), $false), @('extra-file', @('qualification-result.json', 'other.txt'), $false),
        @('subdirectory', @('qualification-result.json'), $true))) {
    $folder = Join-Path $base $vector[0]
    [void][IO.Directory]::CreateDirectory($folder)
    foreach ($leaf in $vector[1]) { [IO.File]::WriteAllBytes((Join-Path $folder $leaf), [byte[]]@(1)) }
    if ($vector[2]) { [void][IO.Directory]::CreateDirectory((Join-Path $folder 'nested')) }
    $folders[$vector[0]] = Test-ProbeResultFolderRemovable $folder
}
$results.folders = $folders

# 8. Only the script's own fixed tokens are printed.
$results.safeMessages = [ordered]@{
    stop = Get-ProbeSafeMessage 'STOP: pin differs (desktop)' '\ASTOP: [A-Za-z0-9 ().;,=-]+\z' 'fallback'
    path = Get-ProbeSafeMessage 'Could not find file C:\Users\owner\x.json' '\ASTOP: [A-Za-z0-9 ().;,=-]+\z' 'fallback'
    resultToken = Get-ProbeSafeMessage 'result-capabilityStatus-keys' '\Aresult-[A-Za-z0-9-]+\z' 'fallback'
    resultOther = Get-ProbeSafeMessage 'Index was outside the bounds of the array.' '\Aresult-[A-Za-z0-9-]+\z' 'fallback'
}

[IO.File]::WriteAllText((Join-Path $WorkRoot 'results.json'), (ConvertTo-Json -InputObject $results -Depth 8), [Text.UTF8Encoding]::new($false))
Write-Output 'PASS: qualification probe host tooling fixtures'
