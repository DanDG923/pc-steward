param(
    [Parameter(Mandatory)][string]$RepositoryRoot,
    [Parameter(Mandatory)][string]$WorkRoot
)
# Task 50 G1/G2/G4: FR-UPD-15, TM-03/04/06/07/13, ADR-022. Runs under pwsh 7 (Linux or Windows) and Windows PowerShell 5.1.
# It executes the REAL text of the redeploy template statements and of the caller, helper and copy-script functions, taken
# from their parsed AST, against temp directories only. Get-Process is stubbed. No elevation, no certificate store, no
# Program Files, no installed root and no real user data is touched. Windows-only rows (ACL objects, junctions, the real
# generated program refusing non-elevated) are recorded as notRun with a reason on other platforms.
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Desktop') {
    # A .NET-launched Windows PowerShell child can inherit another host's module path; bind this host's modules.
    foreach ($module in @('Utility', 'Management', 'Security')) { Import-Module (Join-Path $PSHOME ('Modules\Microsoft.PowerShell.' + $module + '\Microsoft.PowerShell.' + $module + '.psd1')) -Force }
}
$onWindows = ($PSVersionTable.PSEdition -eq 'Desktop') -or ((Get-Variable -Name IsWindows -ValueOnly -ErrorAction SilentlyContinue) -eq $true)
$shellPath = [Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
$scripts = Join-Path $RepositoryRoot 'scripts'
$results = [ordered]@{ shell = ($PSVersionTable.PSEdition + ' ' + $PSVersionTable.PSVersion.ToString()); windows = $onWindows; notRun = [ordered]@{} }

function Read-ScriptAst([string]$Path) {
    $tokens = $null; $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors)
    [pscustomobject]@{ Ast = $ast; Errors = @($errors).Count }
}
function Get-Failure([scriptblock]$Action) { try { & $Action | Out-Null; return $null } catch { return $_.Exception.Message } }
function Get-Sha256([byte[]]$Bytes) { $sha = [Security.Cryptography.SHA256]::Create(); try { [BitConverter]::ToString($sha.ComputeHash($Bytes)).Replace('-', '') } finally { $sha.Dispose() } }
function Get-TextSha256([string]$Text) { Get-Sha256 ([Text.Encoding]::UTF8.GetBytes($Text)) }
function New-Directory([string]$Path) { [void][IO.Directory]::CreateDirectory($Path); $Path }
function New-Link([string]$Path, [string]$Target) {
    if ($onWindows) { New-Item -ItemType Junction -Path $Path -Value $Target | Out-Null } else { New-Item -ItemType SymbolicLink -Path $Path -Value $Target | Out-Null }
}

# 1. Parse check: zero errors in this shell.
$parse = [ordered]@{}
foreach ($name in @('Invoke-OwnerOnlyApprovedRedeployment.ps1', 'OwnerOnlyExecution.ps1', 'Invoke-OwnerOnlyRedeployCaller.ps1', 'Copy-OwnerOnlyUserDatabase.ps1', 'Remove-OwnerOnlyProtectedTestDeployment.ps1', 'Wait-OwnerOnlyDeploymentProcess.ps1')) {
    $parse[$name] = (Read-ScriptAst (Join-Path $scripts $name)).Errors
}
$parse['Fixtures/RedeployTooling.ps1'] = (Read-ScriptAst $PSCommandPath).Errors
$results.parseErrors = $parse

# 2. Real function definitions: the pure helper file, then every caller and copy-script function from the AST.
. (Join-Path $scripts 'OwnerOnlyExecution.ps1')
$callerAst = (Read-ScriptAst (Join-Path $scripts 'Invoke-OwnerOnlyRedeployCaller.ps1')).Ast
$copyAst = (Read-ScriptAst (Join-Path $scripts 'Copy-OwnerOnlyUserDatabase.ps1')).Ast
$templateAst = (Read-ScriptAst (Join-Path $scripts 'Invoke-OwnerOnlyApprovedRedeployment.ps1')).Ast
$helperAst = (Read-ScriptAst (Join-Path $scripts 'OwnerOnlyExecution.ps1')).Ast
foreach ($definition in @($callerAst.EndBlock.Statements + $copyAst.EndBlock.Statements | Where-Object { $_ -is [Management.Automation.Language.FunctionDefinitionAst] })) {
    . ([scriptblock]::Create($definition.Extent.Text))
}
# Stub: the fixture decides which PCSteward* processes exist.
$script:FakeProcesses = @()
function Get-Process { [CmdletBinding()] param([string[]]$Name) @($script:FakeProcesses) }

# Sample inputs.
$hex = { param([string]$Seed) Get-TextSha256 $Seed }
$sid = 'S-1-5-21-1000-2000-3000-1001'
function New-UserData([string]$Root, [byte[]]$Bytes) {
    New-Directory $Root | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $Root 'pcsteward.db'), $Bytes)
    [pscustomobject]@{ bytes = [long]$Bytes.Length; path = 'pcsteward.db'; sha256 = (Get-Sha256 $Bytes) }
}
function New-SqliteBytes {
    $bytes = [byte[]]::new(8192)
    [Text.Encoding]::ASCII.GetBytes("SQLite format 3`0").CopyTo($bytes, 0)
    $bytes[16] = 0x10; $bytes[17] = 0x00; $bytes[18] = 2; $bytes[19] = 2
    $bytes[27] = 5; $bytes[31] = 2; $bytes[95] = 5
    for ($i = 100; $i -lt $bytes.Length; $i++) { $bytes[$i] = [byte]($i % 251) }
    , $bytes
}
function New-SamplePlanObject([string]$DataRoot, [object[]]$Files, [hashtable]$Change) {
    $scriptsPins = @(Get-OwnerOnlyReviewedScriptNames | ForEach-Object { [pscustomobject]@{ path = $_; sha256 = (& $hex $_) } })
    $plan = [ordered]@{
        schema = 'pc-steward-exact-redeploy-v3'; expectedOldOrigin = 'pinned-unsigned-v1'; expectedNewOrigin = 'pinned-unsigned-v2'; preserveUserData = $true
        userDataRoot = $DataRoot; userDataFiles = [object[]]$Files; installerSid = $sid
        executionRoot = 'C:\Program Files\PC Steward OwnerOnlyTest Execution 0123456789abcdef0123456789abcdef'
        scriptSource = "C:\Users\owner's name\AppData\Local\OwnerOnlyDeploymentCandidates\task-50-redeploy-v3-0123456789AB\scripts-v3"; scripts = [object[]]$scriptsPins
        oldLedgerSha256 = (& $hex 'ledger'); oldThumbprint = '14F34EFEFA84C367F25917B4B28803DCECE85623'
        candidateRoot = 'C:\Users\owner\AppData\Local\OwnerOnlyDeploymentCandidates\task-50-redeploy-v3-0123456789AB\payload'
        candidateManifestPath = 'C:\Users\owner\AppData\Local\OwnerOnlyDeploymentCandidates\task-50-redeploy-v3-0123456789AB\unsigned-candidate-manifest.json'
        candidateManifestSha256 = (& $hex 'manifest')
    }
    if ($Change) { foreach ($key in @($Change.Keys)) { if ($Change[$key] -is [string] -and $Change[$key] -ceq '<remove>') { $plan.Remove($key) } else { $plan[$key] = $Change[$key] } } }
    [pscustomobject]$plan
}
$dbPin = [pscustomobject]@{ bytes = [long]8192; path = 'pcsteward.db'; sha256 = (& $hex 'db') }

# 3. Plan schema v3 refusals (Assert-OwnerOnlyRedeployPlanV3, embedded in the elevated program and loaded by the caller).
$planCases = [ordered]@{
    valid = @{}
    missingPreserveUserData = @{ preserveUserData = '<remove>' }
    preserveUserDataFalse = @{ preserveUserData = $false }
    preserveUserDataString = @{ preserveUserData = 'true' }
    extraKey = @{ userDataRootAbsent = $true }
    missingDatabasePin = @{ userDataFiles = [object[]]@([pscustomobject]@{ bytes = [long]1; path = 'other.db'; sha256 = (& $hex 'x') }) }
    sidecarPinned = @{ userDataFiles = [object[]]@($dbPin, [pscustomobject]@{ bytes = [long]1; path = 'pcsteward.db-wal'; sha256 = (& $hex 'x') }) }
    traversingPin = @{ userDataFiles = [object[]]@($dbPin, [pscustomobject]@{ bytes = [long]1; path = '..\other.db'; sha256 = (& $hex 'x') }) }
    lowercaseDatabaseHash = @{ userDataFiles = [object[]]@([pscustomobject]@{ bytes = [long]8192; path = 'pcsteward.db'; sha256 = (& $hex 'db').ToLowerInvariant() }) }
    zeroBytes = @{ userDataFiles = [object[]]@([pscustomobject]@{ bytes = [long]0; path = 'pcsteward.db'; sha256 = (& $hex 'db') }) }
    filesNotArray = @{ userDataFiles = $dbPin }
    fiveScripts = @{ scripts = [object[]]@(Get-OwnerOnlyReviewedScriptNames | Select-Object -First 5 | ForEach-Object { [pscustomobject]@{ path = $_; sha256 = (& $hex $_) } }) }
    badOrigin = @{ expectedNewOrigin = 'legacy-v6' }
    computerSid = @{ installerSid = 'S-1-5-18' }
}
$planResults = [ordered]@{}
foreach ($name in $planCases.Keys) { $planResults[$name] = Get-Failure { Assert-OwnerOnlyRedeployPlanV3 -Plan (New-SamplePlanObject 'C:\Users\owner\AppData\Local\PCSteward' @($dbPin) $planCases[$name]) } }
$results.planV3 = $planResults

# 4. Template top section (schema, v3 pins, origins), executed from the real template text with an embedded plan.
$top = @($templateAst.EndBlock.Statements)
$first = [array]::IndexOf($top, @($top | Where-Object { $_.Extent.Text -like 'if(!$OwnerOnlyPlanJson)*' })[0])
$last = [array]::IndexOf($top, @($top | Where-Object { $_.Extent.Text -like '$principal=*' })[0])
if ($first -lt 0 -or $last -le $first) { throw 'ASSERT: template plan section not found.' }
$planSection = [scriptblock]::Create((($top[$first..($last - 1)] | ForEach-Object { $_.Extent.Text }) -join "`n"))
function Invoke-PlanSection([string]$Json) {
    $OwnerOnlyPlanJson = $Json
    try { . $planSection; [ordered]@{ failure = $null; old = $expectedOldOrigin; new = $expectedNewOrigin; preserve = $preserveUserData; pins = @($userDataPins).Count } }
    catch { [ordered]@{ failure = $_.Exception.Message } }
}
$v1 = '{"schema":"pc-steward-exact-redeploy-v1","scripts":[]}'
$validV3 = ConvertTo-OwnerOnlyCanonicalPlanJson -Plan (New-SamplePlanObject 'C:\Users\owner\AppData\Local\PCSteward' @($dbPin) @{})
$results.templatePlan = [ordered]@{
    v1 = Invoke-PlanSection $v1
    v1NamedOrigin = Invoke-PlanSection '{"schema":"pc-steward-exact-redeploy-v1","expectedOldOrigin":"pinned-unsigned-v1"}'
    v2 = Invoke-PlanSection '{"schema":"pc-steward-exact-redeploy-v2","expectedOldOrigin":"pinned-unsigned-v1","expectedNewOrigin":"pinned-unsigned-v2"}'
    v2BadOrigin = Invoke-PlanSection '{"schema":"pc-steward-exact-redeploy-v2","expectedOldOrigin":"pinned-unsigned-v3","expectedNewOrigin":"pinned-unsigned-v2"}'
    v2PreserveIgnored = Invoke-PlanSection '{"schema":"pc-steward-exact-redeploy-v2","expectedOldOrigin":"pinned-unsigned-v1","expectedNewOrigin":"pinned-unsigned-v2","preserveUserData":true}'
    unknownSchema = Invoke-PlanSection '{"schema":"pc-steward-exact-redeploy-v4"}'
    v3 = Invoke-PlanSection $validV3
    v3MissingPreserve = Invoke-PlanSection ($validV3.Replace('"preserveUserData":true,', ''))
    v3MalformedDatabasePin = Invoke-PlanSection ($validV3.Replace((& $hex 'db'), 'ABC'))
}

# 5. Template preflight / removal / final postconditions, executed from the real statements (each throw guard plus the
#    v3 statement that follows it) with temp roots and a stubbed process list.
$tryBlock = @($templateAst.FindAll({ param($node) $node -is [Management.Automation.Language.TryStatementAst] }, $true))[0].Body
function Get-GuardPair([string]$ThrowText) {
    $statements = @($tryBlock.Statements)
    $index = [array]::IndexOf($statements, @($statements | Where-Object { $_.Extent.Text.Contains($ThrowText) })[0])
    if ($index -lt 0 -or $statements[$index + 1].Extent.Text -notlike 'if($preserveUserData)*') { throw "ASSERT: guard pair not found: $ThrowText" }
    [scriptblock]::Create($statements[$index].Extent.Text + "`n" + $statements[$index + 1].Extent.Text)
}
$preflight = Get-GuardPair "throw 'Approved old deployment/data/process baseline changed.'"
$removalGuard = Get-GuardPair "throw 'Removal postconditions failed.'"
$finalGuard = Get-GuardPair "throw 'New exact identity/preservation postconditions failed.'"
$caseRoot = New-Directory (Join-Path $WorkRoot 'guards')
$ledger = Join-Path $caseRoot 'deployment.json'; [IO.File]::WriteAllText($ledger, '{"ledger":true}')
$ledgerHash = (Get-FileHash -LiteralPath $ledger -Algorithm SHA256).Hash
$sqlite = New-SqliteBytes
$counter = 0
function Invoke-Guard([scriptblock]$Guard, [string]$Schema, [string]$Setup) {
    $script:counter++
    $root = New-Directory (Join-Path $caseRoot ("case-$script:counter"))
    $data = Join-Path $root 'PCSteward'; $install = Join-Path $root 'install'; $state = Join-Path $root 'state'
    $pin = $null
    if ($Setup -notmatch 'noData') { $pin = New-UserData $data $sqlite }
    else { $pin = [pscustomobject]@{ bytes = [long]$sqlite.Length; path = 'pcsteward.db'; sha256 = (Get-Sha256 $sqlite) } }
    if ($Setup -match 'changed') { $changed = [byte[]]$sqlite.Clone(); $changed[200] = [byte](($changed[200] + 1) % 256); [IO.File]::WriteAllBytes((Join-Path $data 'pcsteward.db'), $changed) }
    foreach ($suffix in @('wal', 'shm', 'journal')) { if ($Setup -match "sidecar-$suffix") { [IO.File]::WriteAllBytes((Join-Path $data "pcsteward.db-$suffix"), [byte[]]@(1)) } }
    if ($Setup -match 'reparse') { $real = Join-Path $root 'real-data'; [IO.Directory]::Move($data, $real); New-Link $data $real }
    if ($Setup -match 'installPresent') { New-Directory $install | Out-Null }
    $script:FakeProcesses = if ($Setup -match 'process') { @([pscustomobject]@{ Name = 'PCSteward.ScheduledHost'; Id = 4242 }) } else { @() }
    $preserveUserData = $Schema -ceq 'v3'
    $userDataPins = @($pin)
    $userData = $null
    $planRoot = if ($Setup -match 'otherRoot') { Join-Path $root 'Other' } else { $data }
    $OwnerOnlyPlan = [pscustomobject]@{ oldLedgerSha256 = $ledgerHash; userDataRoot = $planRoot; candidateManifestSha256 = (& $hex 'manifest') }
    $expectedNewOrigin = 'pinned-unsigned-v2'
    $proof = [pscustomobject]@{ validation = 'Pass'; payloadOrigin = 'pinned-unsigned-v2'; helperProtocol = '2'; candidateManifestSha256 = (& $hex 'manifest') }
    try { . $Guard; $outcome = [ordered]@{ failure = $null; preservedSha256 = $(if ($userData) { @($userData)[0].sha256 } else { $null }) } }
    catch { $outcome = [ordered]@{ failure = $_.Exception.Message } }
    finally { $script:FakeProcesses = @() }
    $outcome
}
$guardResults = [ordered]@{
    preflightV2DataAbsent = Invoke-Guard $preflight 'v2' 'noData'
    preflightV2DataPresent = Invoke-Guard $preflight 'v2' ''
    preflightV3Pinned = Invoke-Guard $preflight 'v3' ''
    preflightV3HashMismatch = Invoke-Guard $preflight 'v3' 'changed'
    preflightV3Wal = Invoke-Guard $preflight 'v3' 'sidecar-wal'
    preflightV3Shm = Invoke-Guard $preflight 'v3' 'sidecar-shm'
    preflightV3Journal = Invoke-Guard $preflight 'v3' 'sidecar-journal'
    preflightV3Process = Invoke-Guard $preflight 'v3' 'process'
    preflightV3DataAbsent = Invoke-Guard $preflight 'v3' 'noData'
    preflightV3OtherRoot = Invoke-Guard $preflight 'v3' 'otherRoot'
    preflightV3ReparseRoot = Invoke-Guard $preflight 'v3' 'reparse'
    removalV2DataPresent = Invoke-Guard $removalGuard 'v2' ''
    removalV2DataAbsent = Invoke-Guard $removalGuard 'v2' 'noData'
    removalV3Unchanged = Invoke-Guard $removalGuard 'v3' ''
    removalV3Changed = Invoke-Guard $removalGuard 'v3' 'changed'
    removalV3Sidecar = Invoke-Guard $removalGuard 'v3' 'sidecar-wal'
    removalV3DataAbsent = Invoke-Guard $removalGuard 'v3' 'noData'
    removalV3InstallPresent = Invoke-Guard $removalGuard 'v3' 'installPresent'
    finalV2DataPresent = Invoke-Guard $finalGuard 'v2' ''
    finalV3Unchanged = Invoke-Guard $finalGuard 'v3' ''
    finalV3Shm = Invoke-Guard $finalGuard 'v3' 'sidecar-shm'
    finalV3Process = Invoke-Guard $finalGuard 'v3' 'process'
    finalV3Changed = Invoke-Guard $finalGuard 'v3' 'changed'
}
$guardResults.expectedPreservedSha256 = Get-Sha256 $sqlite
$results.guards = $guardResults

# 6. Result record: v3 adds userDataPreserved and the database pin; v1/v2 keep the original keys and order.
$resultIf = @($tryBlock.Statements | Where-Object { $_ -is [Management.Automation.Language.IfStatementAst] -and $_.Extent.Text.Contains('userDataPreserved=$true') })[0]
$resultBlock = [scriptblock]::Create($resultIf.Extent.Text)
$proof = [pscustomobject]@{ payloadOrigin = 'pinned-unsigned-v2'; candidateManifestSha256 = (& $hex 'manifest'); certificateThumbprint = ('A' * 40) }
$execution = 'X'
$userData = @([pscustomobject]@{ path = 'pcsteward.db'; bytes = [long]8192; sha256 = (& $hex 'db') })
$preserveUserData = $true; . $resultBlock; $v3Keys = @($result.Keys); $v3Sha = $result.userDatabaseSha256
$preserveUserData = $false; . $resultBlock; $v2Keys = @($result.Keys)
$results.resultRecord = [ordered]@{ v3Keys = ($v3Keys -join ','); v3Sha256 = $v3Sha; v2Keys = ($v2Keys -join ',') }

# 7. The guarded remover and the installer never address the per-user data root.
$removerText = [IO.File]::ReadAllText((Join-Path $scripts 'Remove-OwnerOnlyProtectedTestDeployment.ps1'))
$removerAst = (Read-ScriptAst (Join-Path $scripts 'Remove-OwnerOnlyProtectedTestDeployment.ps1')).Ast
$removeTargets = @($removerAst.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'Remove-Item' }, $true) | ForEach-Object { $_.CommandElements[2].Extent.Text })
$results.remover = [ordered]@{
    removeTargets = ($removeTargets -join ',')
    reportsUserDataPreserved = $removerText.Contains('userDataPreserved = $true')
    mentionsUserData = @('Remove-OwnerOnlyProtectedTestDeployment.ps1', 'Install-OwnerOnlyProtectedTestDeployment.ps1', 'Test-OwnerOnlyProtectedTestDeployment.ps1' | Where-Object { [IO.File]::ReadAllText((Join-Path $scripts $_)) -match 'LocalApplicationData|LOCALAPPDATA|PCSteward\\pcsteward' }).Count
}

# 8. Caller structure (AST): one Start-Process, only on the execute path, RunAs, literal System32 path, no -Wait.
$functions = @{}
foreach ($definition in @($callerAst.EndBlock.Statements + $helperAst.EndBlock.Statements | Where-Object { $_ -is [Management.Automation.Language.FunctionDefinitionAst] })) { $functions[$definition.Name] = $definition }
function Get-Reachable([string[]]$Roots) {
    $seen = @{}; $queue = [Collections.Generic.Queue[string]]::new(); foreach ($root in $Roots) { $queue.Enqueue($root) }
    while ($queue.Count) {
        $name = $queue.Dequeue(); if ($seen.ContainsKey($name) -or !$functions.ContainsKey($name)) { continue }; $seen[$name] = $true
        foreach ($call in @($functions[$name].Body.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] }, $true))) { $called = $call.GetCommandName(); if ($called) { $queue.Enqueue($called) } }
    }
    @($seen.Keys)
}
$startProcess = @($callerAst.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'Start-Process' }, $true))
$startCalls = @($callerAst.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'Start-OwnerOnlyElevatedRedeploy' }, $true))
function Get-Enclosing($Node, [type]$Type) { for ($current = $Node.Parent; $null -ne $current; $current = $current.Parent) { if ($current -is $Type) { return $current } }; $null }
function Get-ParameterValue($Command, [string]$Name) {
    $elements = @($Command.CommandElements)
    for ($i = 0; $i -lt $elements.Count; $i++) { if ($elements[$i] -is [Management.Automation.Language.CommandParameterAst] -and $elements[$i].ParameterName -eq $Name) { if ($elements[$i].Argument) { return $elements[$i].Argument }; return $elements[$i + 1] } }
    $null
}
$checkReach = Get-Reachable @('Invoke-OwnerOnlyRedeployCheck', 'New-OwnerOnlyRedeployPlan')
$executeReach = Get-Reachable @('Invoke-OwnerOnlyRedeployExecution')
$checkCommands = @($checkReach | ForEach-Object { $functions[$_].Body.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] }, $true) } | ForEach-Object { $_.GetCommandName() } | Where-Object { $_ } | Sort-Object -Unique)
$executionCall = @($callerAst.EndBlock.Statements | Where-Object { $_ -isnot [Management.Automation.Language.FunctionDefinitionAst] } | ForEach-Object { $_.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'Invoke-OwnerOnlyRedeployExecution' }, $true) })
$executionGuard = $false
if ($executionCall.Count -eq 1) {
    $guardIf = Get-Enclosing $executionCall[0] ([Management.Automation.Language.IfStatementAst])
    $executionGuard = $null -ne $guardIf -and $guardIf.Clauses[0].Item1.Extent.Text -ceq "`$mode -ceq 'Execute'" -and $guardIf.Clauses[0].Item2.Extent.StartOffset -le $executionCall[0].Extent.StartOffset -and $executionCall[0].Extent.EndOffset -le $guardIf.Clauses[0].Item2.Extent.EndOffset
}
$mainStatements = @($callerAst.EndBlock.Statements | Where-Object { $_ -isnot [Management.Automation.Language.FunctionDefinitionAst] })
$mainCalls = @($mainStatements | ForEach-Object { $_.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] }, $true) } | ForEach-Object { $_.GetCommandName() } | Where-Object { $_ } | Sort-Object -Unique)
$literal = Get-ParameterValue $startProcess[0] 'FilePath'
$verb = Get-ParameterValue $startProcess[0] 'Verb'
$systemLiteral = @($functions['Assert-OwnerOnlySystemPowerShell'].Body.FindAll({ param($node) $node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -eq '$literal' }, $true))[0].Right.Extent.Text.Trim("'")
$scriptBlockCreates = @($callerAst.FindAll({ param($node) $node -is [Management.Automation.Language.InvokeMemberExpressionAst] -and $node.Member.Extent.Text -eq 'Create' -and $node.Expression.Extent.Text -eq '[ScriptBlock]' }, $true))
$results.caller = [ordered]@{
    startProcessCount = $startProcess.Count
    startProcessFunction = (Get-Enclosing $startProcess[0] ([Management.Automation.Language.FunctionDefinitionAst])).Name
    startProcessInLoop = $null -ne (Get-Enclosing $startProcess[0] ([Management.Automation.Language.LoopStatementAst]))
    filePathIsLiteral = $literal -is [Management.Automation.Language.StringConstantExpressionAst]
    filePath = $literal.Extent.Text.Trim("'")
    systemLiteral = $systemLiteral
    verb = $verb.Extent.Text.Trim("'")
    hasWait = @($startProcess[0].CommandElements | Where-Object { $_ -is [Management.Automation.Language.CommandParameterAst] -and $_.ParameterName -eq 'Wait' }).Count -gt 0
    argumentList = (Get-ParameterValue $startProcess[0] 'ArgumentList').Extent.Text
    startCallCount = $startCalls.Count
    startCallFunction = (Get-Enclosing $startCalls[0] ([Management.Automation.Language.FunctionDefinitionAst])).Name
    startCallInLoop = $null -ne (Get-Enclosing $startCalls[0] ([Management.Automation.Language.LoopStatementAst]))
    checkPathReachesStart = $checkReach -contains 'Start-OwnerOnlyElevatedRedeploy' -or $checkReach -contains 'Invoke-OwnerOnlyRedeployExecution' -or $checkCommands -contains 'Start-Process' -or $checkCommands -contains 'Wait-OwnerOnlyDeploymentProcess'
    executionGuardedByMode = $executionGuard
    executePathReachesStart = $executeReach -contains 'Start-OwnerOnlyElevatedRedeploy'
    mainCallsStartDirectly = $mainCalls -contains 'Start-Process' -or $mainCalls -contains 'Start-OwnerOnlyElevatedRedeploy'
    forbiddenCommands = @($callerAst.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -in @('Invoke-Expression', 'iex', 'Add-Type', 'Remove-Item', 'Invoke-Command', 'Start-Job') }, $true)).Count
    scriptBlockCreateFunctions = (@($scriptBlockCreates | ForEach-Object { (Get-Enclosing $_ ([Management.Automation.Language.FunctionDefinitionAst])).Name }) -join ',')
    helperFileStartProcess = @($helperAst.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'Start-Process' }, $true)).Count
}

# 9. Canonical plan JSON: arrays (six pins, ONE database pin) survive the round trip in this shell, including from
#    ConvertFrom-Json-sourced objects (the Task 49 5.1 defect), and the plan file must be byte-canonical ASCII.
$planDir = New-Directory (Join-Path $WorkRoot 'plan')
$canonical = ConvertTo-OwnerOnlyCanonicalPlanJson -Plan (New-SamplePlanObject 'C:\Users\owner\AppData\Local\PCSteward' @($dbPin) @{})
$reparsed = ConvertFrom-Json -InputObject $canonical
$again = ConvertTo-OwnerOnlyCanonicalPlanJson -Plan $reparsed
$planFile = Join-Path $planDir 'plan.json'; [IO.File]::WriteAllBytes($planFile, [Text.Encoding]::ASCII.GetBytes($canonical))
$read = Read-OwnerOnlyRedeployPlan -Path $planFile
$prettyFile = Join-Path $planDir 'pretty.json'; [IO.File]::WriteAllText($prettyFile, (ConvertTo-Json -InputObject $reparsed -Depth 6))
$spacedFile = Join-Path $planDir 'spaced.json'; [IO.File]::WriteAllBytes($spacedFile, [Text.Encoding]::ASCII.GetBytes($canonical.Replace('{"schema":', '{ "schema":')))
$nonAsciiFile = Join-Path $planDir 'nonascii.json'; [IO.File]::WriteAllBytes($nonAsciiFile, [Text.Encoding]::UTF8.GetBytes($canonical.Replace('owner', [string][char]0x00E9)))
$unicodePlan = ConvertTo-OwnerOnlyCanonicalPlanJson -Plan (New-SamplePlanObject ('C:\Users\J' + [char]0x00FC + 'rgen\AppData\Local\PCSteward') @($dbPin) @{})
$results.canonical = [ordered]@{
    roundTrip = $canonical -ceq $again
    scriptsIsArray = $canonical.Contains('"scripts":[{')
    oneDatabasePinIsArray = $canonical.Contains('"userDataFiles":[{"bytes":8192,"path":"pcsteward.db","sha256":"')
    wrappedArray = $canonical.Contains('"value":') -or $canonical.Contains('"Count":')
    reparsedScriptCount = @($reparsed.scripts).Count
    reparsedDatabaseCount = @($reparsed.userDataFiles).Count
    readSha256Matches = $read.Sha256 -ceq (Get-Sha256 ([IO.File]::ReadAllBytes($planFile)))
    readPins = @($read.Pins).Count
    multiLine = Get-Failure { Read-OwnerOnlyRedeployPlan -Path $prettyFile }
    nonCanonical = Get-Failure { Read-OwnerOnlyRedeployPlan -Path $spacedFile }
    nonAscii = Get-Failure { Read-OwnerOnlyRedeployPlan -Path $nonAsciiFile }
    unicodeEscaped = $unicodePlan.Contains('J\u00fcrgen') -and ((ConvertFrom-Json -InputObject $unicodePlan).userDataRoot -ceq ('C:\Users\J' + [char]0x00FC + 'rgen\AppData\Local\PCSteward'))
}

# 10. Program and encoded command from the real helper and template text: bounded, lossless, hash-guarded.
$helperText = [IO.File]::ReadAllText((Join-Path $scripts 'OwnerOnlyExecution.ps1'))
$templateText = [IO.File]::ReadAllText((Join-Path $scripts 'Invoke-OwnerOnlyApprovedRedeployment.ps1'))
$program = New-OwnerOnlyRedeployProgram -PlanJson $canonical -ExecutionHelpersText $helperText -TemplateText $templateText
$encoded = ConvertTo-OwnerOnlyEncodedCommand -ProgramText $program
$stubText = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String($encoded.Encoded))
$gzipLiteral = [regex]::Match($stubText, "FromBase64String\('([A-Za-z0-9+/=]+)'\)").Groups[1].Value
$inflate = [IO.Compression.GZipStream]::new([IO.MemoryStream]::new([Convert]::FromBase64String($gzipLiteral)), [IO.Compression.CompressionMode]::Decompress)
$expanded = [IO.MemoryStream]::new(); try { $inflate.CopyTo($expanded) } finally { $inflate.Dispose() }
$programErrors = $null; [void][Management.Automation.Language.Parser]::ParseInput($program, [ref]$null, [ref]$programErrors)
$stubErrors = $null; [void][Management.Automation.Language.Parser]::ParseInput($stubText, [ref]$null, [ref]$stubErrors)
$programAst = [Management.Automation.Language.Parser]::ParseInput($program, [ref]$null, [ref]$null)
$embeddedPlan = & ([scriptblock]::Create(@($programAst.EndBlock.Statements)[0].Extent.Text + '; $OwnerOnlyPlanJson'))
function Invoke-Encoded([string]$Program, [string]$Mutate) {
    $command = ConvertTo-OwnerOnlyEncodedCommand -ProgramText $Program
    $value = $command.Encoded
    if ($Mutate) { $value = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command.Stub.Replace($command.ProgramSha256, ('0' * 64)))) }
    $ErrorActionPreference = 'Continue'
    $out = & $shellPath -NoProfile -NonInteractive -EncodedCommand $value 2>&1 | Out-String
    [ordered]@{ exitCode = $LASTEXITCODE; output = $out }
}
$results.program = [ordered]@{
    encodedLength = $encoded.Encoded.Length
    lossless = (Get-Sha256 $expanded.ToArray()) -ceq (Get-Sha256 ([Text.UTF8Encoding]::new($false).GetBytes($program)))
    stubPinsProgram = $stubText.Contains($encoded.ProgramSha256)
    encodedSha256 = $encoded.EncodedSha256 -ceq (Get-Sha256 ([Text.Encoding]::ASCII.GetBytes($encoded.Encoded)))
    programParseErrors = @($programErrors).Count
    stubParseErrors = @($stubErrors).Count
    embeddedPlanExact = $embeddedPlan -ceq $canonical
    deterministic = (ConvertTo-OwnerOnlyEncodedCommand -ProgramText $program).EncodedSha256 -ceq $encoded.EncodedSha256
    helperInsidePlanRefused = Get-Failure { New-OwnerOnlyRedeployProgram -PlanJson $canonical -ExecutionHelpersText ($helperText + '$OwnerOnlyPlanJson') -TemplateText $templateText }
    nonAsciiPlanRefused = Get-Failure { New-OwnerOnlyRedeployProgram -PlanJson ($canonical + [char]0x2019) -ExecutionHelpersText $helperText -TemplateText $templateText }
    stubRunsVerifiedProgram = Invoke-Encoded "exit 7" ''
    stubRefusesChangedExpansion = Invoke-Encoded "exit 7" 'mutate'
}
if ($onWindows) {
    # The real generated v3 program, run NON-elevated with a synthetic installer SID, must refuse before creating anything.
    $results.program.realProgramNonElevated = Invoke-Encoded $program ''
} else { $results.notRun.realProgramNonElevated = 'The generated bootstrap needs Windows (SystemRoot, WindowsPrincipal); Windows-CI evidence.' }

# 11. One-use attempt record, attempt classification and CLR override names.
$attemptDir = New-Directory (Join-Path $WorkRoot 'attempt')
$attemptPath = Get-OwnerOnlyAttemptRecordPath -PlanPath $planFile -PlanSha256 $read.Sha256
$attemptStream = New-OwnerOnlyAttemptRecord -Path (Join-Path $attemptDir 'record.json') -Value ([ordered]@{ status = 'Claimed'; attempt = 1 })
Write-OwnerOnlyAttemptRecord -Stream $attemptStream -Value ([ordered]@{ status = 'Validated'; attempt = 1; nativeExitCode = 0 })
$attemptStream.Dispose()
$okResult = [pscustomobject]@{ exitCode = 0; phase = 'validated'; userDataPreserved = $true; userDatabaseSha256 = $dbPin.sha256; userDatabaseBytes = 8192; launched = $false }
$results.attempt = [ordered]@{
    pathLeaf = [IO.Path]::GetFileName($attemptPath)
    pathBesidePlan = [IO.Path]::GetDirectoryName($attemptPath) -ceq [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($planFile))
    second = Get-Failure { $stream = New-OwnerOnlyAttemptRecord -Path (Join-Path $attemptDir 'record.json') -Value ([ordered]@{ status = 'Claimed' }); $stream.Dispose() }
    finalStatus = (ConvertFrom-Json -InputObject ([IO.File]::ReadAllText((Join-Path $attemptDir 'record.json')))).status
    classifyValidated = Get-OwnerOnlyAttemptClassification -NativeExitCode 0 -Result $okResult -Pins @($dbPin) -PostCheck @($dbPin)
    classifyTimeout = Get-OwnerOnlyAttemptClassification -NativeExitCode $null -Result $null -Pins @($dbPin) -PostCheck $null
    classifyNativeFailure = Get-OwnerOnlyAttemptClassification -NativeExitCode 1 -Result $okResult -Pins @($dbPin) -PostCheck @($dbPin)
    classifyMissingResult = Get-OwnerOnlyAttemptClassification -NativeExitCode 0 -Result $null -Pins @($dbPin) -PostCheck @($dbPin)
    classifyOtherDatabase = Get-OwnerOnlyAttemptClassification -NativeExitCode 0 -Result ([pscustomobject]@{ exitCode = 0; phase = 'validated'; userDataPreserved = $true; userDatabaseSha256 = (& $hex 'other'); userDatabaseBytes = 8192; launched = $false }) -Pins @($dbPin) -PostCheck @($dbPin)
    classifyPostcheckFailed = Get-OwnerOnlyAttemptClassification -NativeExitCode 0 -Result $okResult -Pins @($dbPin) -PostCheck $null
    clrOverrides = ((Get-OwnerOnlyClrOverrideNames -Names @('PATH', 'COMPlus_EnableDiagnostics', 'COR_ENABLE_PROFILING', 'COR_PROFILER_PATH_64', 'APPDOMAIN_MANAGER_ASM', 'DOTNET_ROOT', 'DOTNET_CLI_TELEMETRY_OPTOUT')) -join ',')
    helpersFunctionOnly = (Get-Failure { Assert-OwnerOnlyFunctionOnlyScript -Text $helperText }) -eq $null -and (Get-Failure { Assert-OwnerOnlyFunctionOnlyScript -Text ([IO.File]::ReadAllText((Join-Path $scripts 'Wait-OwnerOnlyDeploymentProcess.ps1'))) }) -eq $null
    statementHelperRefused = Get-Failure { Assert-OwnerOnlyFunctionOnlyScript -Text ("function A {}`nStart-Sleep 1") }
    sqliteNotLoaded = (Get-Failure { Assert-OwnerOnlyNoSqliteLoaded }) -eq $null
}

# 12. Candidate tree and old-ledger pins.
$payload = New-Directory (Join-Path $WorkRoot 'candidate\payload')
New-Directory (Join-Path $payload 'Resources') | Out-Null
$entries = @()
foreach ($leaf in @('PCSteward.Desktop.exe', 'PCSteward.DeploymentCapability.txt', 'Resources\Main.xbf')) {
    $bytes = [Text.Encoding]::UTF8.GetBytes('content ' + $leaf)
    [IO.File]::WriteAllBytes((Join-Path $payload $leaf.Replace('\', [IO.Path]::DirectorySeparatorChar)), $bytes)
    $entries += [ordered]@{ bytes = $bytes.Length; path = $leaf; sha256 = (Get-Sha256 $bytes) }
}
$manifest = [Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject @($entries) -Depth 3))
$manifestV1 = [Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject @($entries | Where-Object { $_.path -cne 'PCSteward.DeploymentCapability.txt' }) -Depth 3))
$traversal = [Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject @($entries + @([ordered]@{ bytes = 1; path = 'Resources\..\x'; sha256 = ('A' * 64) })) -Depth 3))
$candidateResults = [ordered]@{ valid = (Assert-OwnerOnlyCandidateTree -Root $payload -ManifestBytes $manifest -ExpectedOrigin 'pinned-unsigned-v2').files }
$candidateResults.originMismatch = Get-Failure { Assert-OwnerOnlyCandidateTree -Root $payload -ManifestBytes $manifest -ExpectedOrigin 'pinned-unsigned-v1' }
$candidateResults.unpinnedFile = Get-Failure { Assert-OwnerOnlyCandidateTree -Root $payload -ManifestBytes $manifestV1 -ExpectedOrigin 'pinned-unsigned-v1' }
$candidateResults.traversal = Get-Failure { Assert-OwnerOnlyCandidateTree -Root $payload -ManifestBytes $traversal -ExpectedOrigin 'pinned-unsigned-v2' }
[IO.File]::WriteAllBytes((Join-Path $payload 'Resources' | Join-Path -ChildPath 'Main.xbf'), [Text.Encoding]::UTF8.GetBytes('changed'))
$candidateResults.changed = Get-Failure { Assert-OwnerOnlyCandidateTree -Root $payload -ManifestBytes $manifest -ExpectedOrigin 'pinned-unsigned-v2' }
Remove-Item -LiteralPath (Join-Path $payload 'Resources' | Join-Path -ChildPath 'Main.xbf')
$candidateResults.missing = Get-Failure { Assert-OwnerOnlyCandidateTree -Root $payload -ManifestBytes $manifest -ExpectedOrigin 'pinned-unsigned-v2' }
$linkTarget = New-Directory (Join-Path $WorkRoot 'candidate\elsewhere')
New-Link (Join-Path $payload 'linked') $linkTarget
$candidateResults.reparse = Get-Failure { Assert-OwnerOnlyCandidateTree -Root $payload -ManifestBytes $manifest -ExpectedOrigin 'pinned-unsigned-v2' }
$results.candidate = $candidateResults

$installRoot = 'C:\Program Files\PC Steward'
$ledgerPath = Join-Path $WorkRoot 'ledger.json'
[IO.File]::WriteAllText($ledgerPath, (ConvertTo-Json -InputObject ([ordered]@{ schema = 'pc-steward-owner-only-protected-test-v6'; installRoot = $installRoot; installerSid = $sid; completed = $true; certificateThumbprint = '14F34EFEFA84C367F25917B4B28803DCECE85623'; payloadOrigin = 'pinned-unsigned-v1' })))
$ledgerPlan = New-SamplePlanObject 'C:\Users\owner\AppData\Local\PCSteward' @($dbPin) @{ oldLedgerSha256 = (Get-Sha256 ([IO.File]::ReadAllBytes($ledgerPath))) }
$results.ledger = [ordered]@{
    valid = Get-Failure { Assert-OwnerOnlyOldLedger -LedgerPath $ledgerPath -Plan $ledgerPlan -InstallRoot $installRoot }
    hashChanged = Get-Failure { Assert-OwnerOnlyOldLedger -LedgerPath $ledgerPath -Plan (New-SamplePlanObject 'C:\D' @($dbPin) @{}) -InstallRoot $installRoot }
    otherSigner = Get-Failure { $ledgerPlan.oldThumbprint = ('B' * 40); try { Assert-OwnerOnlyOldLedger -LedgerPath $ledgerPath -Plan $ledgerPlan -InstallRoot $installRoot } finally { $ledgerPlan.oldThumbprint = '14F34EFEFA84C367F25917B4B28803DCECE85623' } }
    otherOrigin = Get-Failure { $ledgerPlan.expectedOldOrigin = 'legacy-v6'; try { Assert-OwnerOnlyOldLedger -LedgerPath $ledgerPath -Plan $ledgerPlan -InstallRoot $installRoot } finally { $ledgerPlan.expectedOldOrigin = 'pinned-unsigned-v1' } }
}

# 13. Database preservation copy (G4) on a synthetic SQLite-format file.
$copyRoot = New-Directory (Join-Path $WorkRoot 'copy')
$copyData = Join-Path $copyRoot 'PCSteward'
$null = New-UserData $copyData $sqlite
$destination = Join-Path $copyRoot 'profile\PCSteward-DatabaseCopies'
$record = Copy-OwnerOnlyUserDatabase -DataRoot $copyData -DestinationParent $destination -ForbiddenRoots @((Join-Path $copyRoot 'ProgramFiles'))
$copied = Join-Path (Join-Path $destination $record.copyFolder) 'pcsteward.db'
$copyResults = [ordered]@{
    hashesEqual = $record.hashesEqual -and $record.sourceSha256 -ceq (Get-Sha256 $sqlite) -and $record.copySha256 -ceq (Get-Sha256 ([IO.File]::ReadAllBytes($copied)))
    sourceUnchanged = (Get-Sha256 ([IO.File]::ReadAllBytes((Join-Path $copyData 'pcsteward.db')))) -ceq (Get-Sha256 $sqlite)
    recordWritten = Test-Path -LiteralPath (Join-Path (Join-Path $destination $record.copyFolder) 'copy-record.json')
    headerValid = $record.sqliteHeader.headerValid -and $record.sqliteHeader.walMode -and $record.sqliteHeader.pageCountMatchesLength
    integrityCheck = $record.integrityCheck
    recordHasNoProfilePath = !((ConvertTo-Json -InputObject $record -Depth 4).Contains($copyRoot))
    destinationInsideData = Get-Failure { Copy-OwnerOnlyUserDatabase -DataRoot $copyData -DestinationParent (Join-Path $copyData 'copies') -ForbiddenRoots @() }
    destinationInsideForbidden = Get-Failure { Copy-OwnerOnlyUserDatabase -DataRoot $copyData -DestinationParent (Join-Path $copyRoot 'ProgramFiles\x') -ForbiddenRoots @((Join-Path $copyRoot 'ProgramFiles')) }
}
$foldersBefore = @(Get-ChildItem -LiteralPath $destination -Directory).Count
[IO.File]::WriteAllBytes((Join-Path $copyData 'pcsteward.db-wal'), [byte[]]@(1))
$copyResults.sidecar = Get-Failure { Copy-OwnerOnlyUserDatabase -DataRoot $copyData -DestinationParent $destination -ForbiddenRoots @() }
Remove-Item -LiteralPath (Join-Path $copyData 'pcsteward.db-wal')
$script:FakeProcesses = @([pscustomobject]@{ Name = 'PCSteward.Desktop'; Id = 7 })
$copyResults.process = Get-Failure { Copy-OwnerOnlyUserDatabase -DataRoot $copyData -DestinationParent $destination -ForbiddenRoots @() }
$script:FakeProcesses = @()
$copyResults.noFolderOnRefusal = @(Get-ChildItem -LiteralPath $destination -Directory).Count -eq $foldersBefore
$realCopyData = Join-Path $copyRoot 'real-data'; [IO.Directory]::Move($copyData, $realCopyData); New-Link $copyData $realCopyData
$copyResults.reparse = Get-Failure { Copy-OwnerOnlyUserDatabase -DataRoot $copyData -DestinationParent $destination -ForbiddenRoots @() }
$copyText = [IO.File]::ReadAllText((Join-Path $scripts 'Copy-OwnerOnlyUserDatabase.ps1'))
$copyResults.forbiddenCommands = @($copyAst.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -in @('Add-Type', 'Remove-Item', 'Start-Process', 'Invoke-Expression', 'Move-Item', 'Set-Content') }, $true)).Count
$copyResults.mentionsEngine = $copyText -match 'DllImport|Microsoft\.Data\.Sqlite|SQLitePCL|LoadLibrary'
$results.copy = $copyResults

# 14. Windows-only: the caller's result.json ACL check matches the template's actual result writer (four entries).
if ($onWindows) {
    $finallyIf = @($templateAst.FindAll({ param($node) $node -is [Management.Automation.Language.IfStatementAst] -and $node.Extent.Text.Contains("'result.json'") }, $true))[0]
    $writer = @($finallyIf.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'New-OwnerOnlyExecutionSecurity' }, $true))
    $bootstrapAcl = New-OwnerOnlyExecutionSecurity
    $threeEntry = [Security.AccessControl.FileSecurity]::new(); $threeEntry.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')); $threeEntry.SetAccessRuleProtection($true, $false)
    foreach ($rule in @(@('S-1-5-18', 'FullControl'), @('S-1-5-32-544', 'FullControl'), @('S-1-5-32-545', 'ReadAndExecute, Synchronize'))) { $threeEntry.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($rule[0]), [Security.AccessControl.FileSystemRights]$rule[1], [Security.AccessControl.AccessControlType]::Allow)) }
    $extra = New-OwnerOnlyExecutionSecurity; $extra.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new('S-1-1-0'), [Security.AccessControl.FileSystemRights]::Read, [Security.AccessControl.AccessControlType]::Allow))
    $usersWrite = New-OwnerOnlyExecutionSecurity; $usersWrite.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new('S-1-5-32-545'), [Security.AccessControl.FileSystemRights]::Write, [Security.AccessControl.AccessControlType]::Allow))
    $unprotected = New-OwnerOnlyExecutionSecurity; $unprotected.SetAccessRuleProtection($false, $true)
    $wrongOwner = New-OwnerOnlyExecutionSecurity; $wrongOwner.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-545'))
    $results.resultAcl = [ordered]@{
        writerIsLeafExecutionSecurity = $writer.Count -eq 1 -and $writer[0].CommandElements.Count -eq 1
        bootstrapWriter = Get-Failure { Assert-OwnerOnlyBootstrapResultAcl -Acl $bootstrapAcl }
        threeEntryWriter = Get-Failure { Assert-OwnerOnlyBootstrapResultAcl -Acl $threeEntry }
        directory = Get-Failure { Assert-OwnerOnlyBootstrapResultAcl -Acl (New-OwnerOnlyExecutionSecurity -Directory) }
        extraEntry = Get-Failure { Assert-OwnerOnlyBootstrapResultAcl -Acl $extra }
        usersWrite = Get-Failure { Assert-OwnerOnlyBootstrapResultAcl -Acl $usersWrite }
        unprotected = Get-Failure { Assert-OwnerOnlyBootstrapResultAcl -Acl $unprotected }
        wrongOwner = Get-Failure { Assert-OwnerOnlyBootstrapResultAcl -Acl $wrongOwner }
    }
} else { $results.notRun.resultAcl = 'Windows security descriptors (SecurityIdentifier, FileSecurity) exist only on Windows; Windows-CI evidence.' }
if (!$onWindows) { $results.notRun.reparseKind = 'Junctions are Windows-only; the reparse rows used a symbolic link here and use a junction on Windows CI.' }

[IO.File]::WriteAllText((Join-Path $WorkRoot 'results.json'), (ConvertTo-Json -InputObject $results -Depth 8), [Text.UTF8Encoding]::new($false))
Write-Output "PASS: redeploy tooling fixtures ($($results.shell))"
