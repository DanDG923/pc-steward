# Task 50 G2: reviewed non-elevated caller for one owner-approved exact redeploy with user-data preservation (plan
# schema pc-steward-exact-redeploy-v3). Windows PowerShell 5.1 only. FR-UPD-15, TM-03/04/06/07/13, ADR-022.
#
# Modes (one per run):
#   -NewPlanPath ...        Read-only host inspection. Assembles a canonical v3 plan from the current host (script, ledger,
#                           signer, candidate manifest and database pins) and writes it to ONE new file for the owner to
#                           inspect and approve. Nothing is executed, elevated, deleted or changed.
#   -PlanPath (CheckOnly)   Default. Read-only: verifies the plan and every pin, prints a JSON report including the plan,
#                           program and encoded-command SHA-256 values to approve. Never starts a process or elevates.
#   -ExecuteApprovedPlan    Re-runs every CheckOnly check, requires the approved plan and encoded-command hashes, claims a
#                           one-use attempt record, then requests exactly one UAC elevation of fixed system Windows
#                           PowerShell for the generated encoded command. No retry. Needs a separate exact owner approval.
#
# Run only from captured, hash-checked bytes, never from a mutable path after checking, for example:
#   $b=[IO.File]::ReadAllBytes($caller); <compare the SHA-256 of $b with the approved value>
#   & ([ScriptBlock]::Create([Text.Encoding]::UTF8.GetString($b))) -PlanPath $plan
# This process must never load SQLite (Task 49: a caller that had loaded the installed e_sqlite3.dll blocked removal).
# It executes only: the OwnerOnlyExecution.ps1 and Wait-OwnerOnlyDeploymentProcess.ps1 bytes whose SHA-256 equals a reviewed
# constant embedded below (Get-OwnerOnlyReviewedHelperPins; the plan's pin must also equal it) and, elevated, the generated
# program built from the embedded stub below, the exact plan JSON, that OwnerOnlyExecution.ps1 text and the bootstrap
# template at its reviewed constant. The caller is itself hash-approved, so these constants are the trust anchor; a plan
# cannot name other helper or template bytes. No command, path or argument is taken from other data.
#
# Task 50 R2 (owner decision D-R2; FR-UPD-15, TM-03/04/07/13, ADR-022):
#   Fresh install. NewPlan with -ExpectedOldOrigin none writes plan schema pc-steward-exact-fresh-install-v1: the v3 key set
#   without oldLedgerSha256/oldThumbprint, the same user-data pins and the same protected, hash-pinned execution route. NewPlan
#   and CheckOnly read no old ledger, signer or thumbprint; they require absent install and state roots, no
#   'CN=PC Steward Owner-Only Test *' certificate in CurrentUser My/Root/TrustedPublisher, no PCSteward* process and an
#   inactive scheduled check. The elevated template repeats that preflight, has NO removal step, re-checks the baseline,
#   then runs the installer (-Install -CreateOwnerOnlyCertificate), the independent validator (-RequirePinnedPayload) and the
#   user-data re-check, and writes the same result and attempt records. Redeploy plans (v1/v2/v3) are unchanged.
#   Packaged-process refusal. GetCurrentPackageFullName must return 15700 (APPMODEL_ERROR_NO_PACKAGE): -ExecuteApprovedPlan
#   refuses before the one-use claim and before any elevation; CheckOnly reports it (unpackagedProcess); the elevated
#   template refuses again before creating anything. B-exec failed signing inside a packaged app's process context.
#
# Notes for the embedded OwnerOnlyExecution.ps1 and bootstrap template (kept here, outside the 30,000-character encoded
# command): plan schema v3 (G1) is v2 plus user-data preservation for a host that already has %LOCALAPPDATA%\PCSteward (a
# closed key set, preserveUserData true, exact database pins, no -wal/-shm/-journal sidecar, no PCSteward process or enabled
# scheduled check, no reparse point); it replaces only the v1/v2 "data root absent" checks. Plan schema v1 fixes the legacy-v6
# preflight and the pinned-unsigned-v1 postcondition and cannot name origins; v2 and later name both from closed lists. The
# database is read only as bytes through a FileShare.Read handle and no SQLite engine is loaded. The package query binds
# kernel32 GetCurrentPackageFullName in memory with Reflection.Emit (no compiler, no TEMP file), so it can run in the
# elevated process before its private compiler TEMP exists. Only the hash-verified protected copies can execute or compile
# privileged code; the protected code, compiler area and result are retained as evidence with no cleanup or retry.
[CmdletBinding(DefaultParameterSetName='CheckOnly')]
param(
    [Parameter(Mandatory,ParameterSetName='CheckOnly')][Parameter(Mandatory,ParameterSetName='Execute')][string]$PlanPath,
    [Parameter(ParameterSetName='CheckOnly')][switch]$CheckOnly,
    [Parameter(Mandatory,ParameterSetName='Execute')][switch]$ExecuteApprovedPlan,
    [Parameter(Mandatory,ParameterSetName='Execute')][string]$ApprovedPlanSha256,
    [Parameter(Mandatory,ParameterSetName='Execute')][string]$ApprovedEncodedCommandSha256,
    [Parameter(Mandatory,ParameterSetName='NewPlan')][string]$NewPlanPath,
    [Parameter(Mandatory,ParameterSetName='NewPlan')][string]$ScriptSource,
    [Parameter(Mandatory,ParameterSetName='NewPlan')][string]$CandidateRoot,
    [Parameter(Mandatory,ParameterSetName='NewPlan')][string]$CandidateManifestPath,
    [Parameter(Mandatory,ParameterSetName='NewPlan')][string]$ExpectedOldOrigin,
    [Parameter(Mandatory,ParameterSetName='NewPlan')][string]$ExpectedNewOrigin
)
$ErrorActionPreference='Stop'

function Get-OwnerOnlyReviewedScriptNames {
    # Same six names, same order, as the bootstrap template's allowlist.
    return @('Install-OwnerOnlyProtectedTestDeployment.ps1','Test-OwnerOnlyProtectedTestDeployment.ps1','Remove-OwnerOnlyProtectedTestDeployment.ps1','OwnerOnlyPayloadIntegrity.ps1','OwnerOnlyExecution.ps1','Wait-OwnerOnlyDeploymentProcess.ps1')
}

function Get-OwnerOnlyReviewedHelperPins {
    # Reviewed SHA-256 of the committed bytes, LF form then CRLF form (a Windows checkout may convert line endings).
    # RedeployToolingScriptTests recomputes both from the committed files, so any edit to these three files must update them.
    return @{
        'OwnerOnlyExecution.ps1'=@('DDF07C71EB61E2BF5E69FBA6F1A672D6CC09B8668715A90119BD725023093F9C','A23F2BBB874A280B5C918DAEDBCB1E5316CACB33AA670FDA682C0E2BF2E0B948')
        'Wait-OwnerOnlyDeploymentProcess.ps1'=@('F24B3235FD34AA82421C576EB284C1F8C04D26D9C56170BE0175825ADDF46C59','2BA1A90DD230CA8587F846F75B84691BA3EA4D27C20069192F46C6602FAC520B')
        'Invoke-OwnerOnlyApprovedRedeployment.ps1'=@('82902E5F292F618649D99F34A964A0044EB3E1BF7A08DDEB67053993D849C26A','DA449407A5C29F9E8AE040D32B78B41D05005479E52FCB87339B0933C421DD61')
    }
}

function Assert-OwnerOnlyReviewedHelperPin {
    param([Parameter(Mandatory)][string]$Name,[Parameter(Mandatory)][AllowEmptyString()][string]$Sha256)
    $pins=(Get-OwnerOnlyReviewedHelperPins)[$Name]
    if($null -eq $pins -or $Sha256 -cnotin $pins){throw "$Name is not the reviewed version embedded in this caller; regenerate the plan from the reviewed scripts."}
}

function Assert-OwnerOnlyAbsoluteInputPath {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Path,[Parameter(Mandatory)][string]$Name)
    if($Path -cnotmatch '^[A-Za-z]:\\' -or $Path.Substring(2).Contains(':')){throw "$Name must be an absolute local path such as C:\...; relative, UNC and device paths are refused."}
}

function Get-OwnerOnlySha256Hex {
    param([Parameter(Mandatory)][AllowEmptyCollection()][byte[]]$Bytes)
    $sha=[Security.Cryptography.SHA256]::Create()
    try{return [BitConverter]::ToString($sha.ComputeHash($Bytes)).Replace('-','')}finally{$sha.Dispose()}
}

function Read-OwnerOnlyStableBytes {
    param([Parameter(Mandatory)][string]$Path,[Parameter(Mandatory)][long]$MaxBytes)
    $stream=[IO.FileStream]::new($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    try{
        if($stream.Length -gt $MaxBytes){throw 'A pinned input exceeds its size bound.'}
        $buffer=[IO.MemoryStream]::new()
        try{$stream.CopyTo($buffer);return ,$buffer.ToArray()}finally{$buffer.Dispose()}
    }finally{$stream.Dispose()}
}

function Read-OwnerOnlyPinnedScript {
    param([Parameter(Mandatory)][string]$Source,[Parameter(Mandatory)][string]$Name,[Parameter(Mandatory)][string]$Sha256)
    if($Name -cnotin @(@(Get-OwnerOnlyReviewedScriptNames)+@('Invoke-OwnerOnlyApprovedRedeployment.ps1')) -or $Sha256 -cnotmatch '^[A-F0-9]{64}$'){throw 'Malformed reviewed script entry.'}
    $bytes=Read-OwnerOnlyStableBytes -Path (Join-Path $Source $Name) -MaxBytes 1048576
    if((Get-OwnerOnlySha256Hex -Bytes $bytes) -cne $Sha256){throw "Script hash changed: $Name"}
    return ,$bytes
}

function ConvertFrom-OwnerOnlyScriptBytes {
    param([Parameter(Mandatory)][byte[]]$Bytes)
    return [Text.UTF8Encoding]::new($false,$true).GetString($Bytes).TrimStart([char]0xfeff)
}

function Assert-OwnerOnlyFunctionOnlyScript {
    # Loading a helper must only define functions: no parameters, no top-level statement other than function definitions.
    param([Parameter(Mandatory)][string]$Text)
    $tokens=$null;$errors=$null
    $ast=[Management.Automation.Language.Parser]::ParseInput($Text,[ref]$tokens,[ref]$errors)
    if(@($errors).Count -or $null -ne $ast.ParamBlock -or $null -ne $ast.BeginBlock -or $null -ne $ast.ProcessBlock -or $null -ne $ast.DynamicParamBlock -or $null -eq $ast.EndBlock -or @($ast.EndBlock.Statements|Where-Object{$_ -isnot [Management.Automation.Language.FunctionDefinitionAst]}).Count){throw 'A pinned helper must contain only function definitions.'}
}

function Assert-OwnerOnlyHelperShadowsNothing {
    # A pinned helper may only ADD Verb-OwnerOnly* functions: none may replace a caller function, an alias or a cmdlet.
    param([Parameter(Mandatory)][string]$Text)
    $tokens=$null;$errors=$null
    $ast=[Management.Automation.Language.Parser]::ParseInput($Text,[ref]$tokens,[ref]$errors)
    foreach($definition in @($ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst]},$true))){
        if($definition.Name -cnotmatch '^[A-Z][a-z]+-OwnerOnly[A-Za-z0-9]+$' -or $null -ne (Get-Command -Name $definition.Name -CommandType Alias,Function,Filter,Cmdlet -ErrorAction SilentlyContinue)){throw "A pinned helper may not define or replace the command $($definition.Name)."}
    }
}

function Get-OwnerOnlyPinnedScriptBlock {
    # Returns the captured, hash-checked, function-only script text as a script block; the caller dot-sources it at script scope.
    param([Parameter(Mandatory)][string]$Source,[Parameter(Mandatory)][string]$Name,[Parameter(Mandatory)][string]$Sha256)
    if($Name -cnotin @('OwnerOnlyExecution.ps1','Wait-OwnerOnlyDeploymentProcess.ps1')){throw 'Only the pinned execution helpers may be loaded by the caller.'}
    Assert-OwnerOnlyReviewedHelperPin -Name $Name -Sha256 $Sha256
    $text=ConvertFrom-OwnerOnlyScriptBytes -Bytes (Read-OwnerOnlyPinnedScript -Source $Source -Name $Name -Sha256 $Sha256)
    Assert-OwnerOnlyFunctionOnlyScript -Text $text
    Assert-OwnerOnlyHelperShadowsNothing -Text $text
    return [ScriptBlock]::Create($text)
}

function ConvertTo-OwnerOnlyAsciiJson {
    param([Parameter(Mandatory)][string]$Json)
    $builder=[Text.StringBuilder]::new()
    foreach($character in $Json.ToCharArray()){if([int]$character -gt 126){[void]$builder.AppendFormat('\u{0:x4}',[int]$character)}else{[void]$builder.Append($character)}}
    return $builder.ToString()
}

function ConvertTo-OwnerOnlyCanonicalPlanJson {
    # Task 49 defect: Windows PowerShell 5.1 re-serialized a decorated ConvertFrom-Json array as {value:[...],Count:n}.
    # Every array here is rebuilt from plain ordered records into a fresh object[] and serialized with -InputObject.
    param([Parameter(Mandatory)]$Plan)
    $pins=@(Assert-OwnerOnlyRedeployPlanV3 -Plan $Plan)
    $names=@(Get-OwnerOnlyReviewedScriptNames)
    $byName=@{}
    foreach($entry in @($Plan.scripts)){if($entry.path -cnotin $names -or $byName.ContainsKey($entry.path)){throw 'Malformed reviewed script entry.'};$byName[$entry.path]=[string]$entry.sha256}
    $scripts=[Collections.Generic.List[object]]::new()
    foreach($name in $names){$record=[ordered]@{};$record['path']=$name;$record['sha256']=$byName[$name];$scripts.Add($record)}
    $keys=[string[]]@($pins|ForEach-Object{[string]$_.path});[Array]::Sort($keys,[StringComparer]::Ordinal)
    $files=[Collections.Generic.List[object]]::new()
    foreach($key in $keys){$pin=@($pins|Where-Object{$_.path -ceq $key})[0];$record=[ordered]@{};$record['bytes']=[long]$pin.bytes;$record['path']=[string]$pin.path;$record['sha256']=[string]$pin.sha256;$files.Add($record)}
    $canonical=[ordered]@{}
    $fresh=$Plan.schema -ceq 'pc-steward-exact-fresh-install-v1'
    $canonical['schema']=if($fresh){'pc-steward-exact-fresh-install-v1'}else{'pc-steward-exact-redeploy-v3'}
    $canonical['expectedOldOrigin']=[string]$Plan.expectedOldOrigin
    $canonical['expectedNewOrigin']=[string]$Plan.expectedNewOrigin
    $canonical['preserveUserData']=$true
    $canonical['userDataRoot']=[string]$Plan.userDataRoot
    $canonical['userDataFiles']=$files.ToArray()
    $canonical['installerSid']=[string]$Plan.installerSid
    $canonical['executionRoot']=[string]$Plan.executionRoot
    $canonical['scriptSource']=[string]$Plan.scriptSource
    $canonical['scripts']=$scripts.ToArray()
    if(!$fresh){$canonical['oldLedgerSha256']=[string]$Plan.oldLedgerSha256;$canonical['oldThumbprint']=[string]$Plan.oldThumbprint}
    $canonical['candidateRoot']=[string]$Plan.candidateRoot
    $canonical['candidateManifestPath']=[string]$Plan.candidateManifestPath
    $canonical['candidateManifestSha256']=[string]$Plan.candidateManifestSha256
    return (ConvertTo-OwnerOnlyAsciiJson -Json (ConvertTo-Json -InputObject $canonical -Depth 6 -Compress))
}

function Read-OwnerOnlyRedeployPlan {
    param([Parameter(Mandatory)][string]$Path)
    $bytes=Read-OwnerOnlyStableBytes -Path $Path -MaxBytes 65536
    foreach($byte in $bytes){if($byte -lt 0x20 -or $byte -gt 0x7E){throw 'The plan must be one line of printable ASCII JSON.'}}
    $text=[Text.Encoding]::ASCII.GetString($bytes)
    $plan=ConvertFrom-Json -InputObject $text
    if($null -eq $plan -or $plan -is [array]){throw 'The plan must be one JSON object.'}
    $json=ConvertTo-OwnerOnlyCanonicalPlanJson -Plan $plan
    if($json -cne $text){throw 'The plan is not in canonical form; regenerate it and approve the regenerated plan.'}
    return [pscustomobject]@{Plan=$plan;Json=$json;Sha256=(Get-OwnerOnlySha256Hex -Bytes $bytes);Pins=@(Assert-OwnerOnlyRedeployPlanV3 -Plan $plan)}
}

function New-OwnerOnlyRedeployProgram {
    # The elevated program: exact plan JSON, then the pinned pure helpers, then the bootstrap template, all as text.
    param([Parameter(Mandatory)][string]$PlanJson,[Parameter(Mandatory)][string]$ExecutionHelpersText,[Parameter(Mandatory)][string]$TemplateText)
    foreach($character in $PlanJson.ToCharArray()){if([int]$character -lt 0x20 -or [int]$character -gt 0x7E){throw 'The embedded plan must be printable ASCII.'}}
    if(!$TemplateText.Contains('$OwnerOnlyPlanJson') -or $ExecutionHelpersText.Contains('$OwnerOnlyPlanJson')){throw 'Unexpected bootstrap template or helper text.'}
    return ("# PC Steward owner-only exact redeploy program, generated by Invoke-OwnerOnlyRedeployCaller.ps1. Review before approval.`n" + '$OwnerOnlyPlanJson=''' + $PlanJson.Replace("'","''") + "'`n" + $ExecutionHelpersText + "`n" + $TemplateText)
}

function ConvertTo-OwnerOnlyEncodedCommand {
    # The command line bound forces compression (Task 49 precedent). The stub below is the only code template; it expands
    # the embedded gzip literal, requires its exact SHA-256 and only then runs it. Exit 20 means the expansion differed.
    param([Parameter(Mandatory)][string]$ProgramText)
    $program=[Text.UTF8Encoding]::new($false).GetBytes($ProgramText)
    $buffer=[IO.MemoryStream]::new()
    try{
        $gzip=[IO.Compression.GZipStream]::new($buffer,[IO.Compression.CompressionMode]::Compress,$true)
        try{$gzip.Write($program,0,$program.Length)}finally{$gzip.Dispose()}
        $compressed=[Convert]::ToBase64String($buffer.ToArray())
    }finally{$buffer.Dispose()}
    $programSha256=Get-OwnerOnlySha256Hex -Bytes $program
    $stub=@'
$ErrorActionPreference='Stop'
$z=[IO.Compression.GZipStream]::new([IO.MemoryStream]::new([Convert]::FromBase64String('@@GZIP@@')),[IO.Compression.CompressionMode]::Decompress)
$m=[IO.MemoryStream]::new();try{$z.CopyTo($m)}finally{$z.Dispose()}
$b=$m.ToArray();$s=[Security.Cryptography.SHA256]::Create();try{$h=[BitConverter]::ToString($s.ComputeHash($b)).Replace('-','')}finally{$s.Dispose()}
if($h -cne '@@SHA256@@'){exit 20}
. ([ScriptBlock]::Create([Text.UTF8Encoding]::new($false,$true).GetString($b)))
'@
    $stub=$stub.Replace("`r`n","`n").Replace('@@GZIP@@',$compressed).Replace('@@SHA256@@',$programSha256)
    $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($stub))
    if($encoded.Length -gt 30000){throw 'The encoded command exceeds the fixed 30,000 character bound.'}
    return [pscustomobject]@{Encoded=$encoded;EncodedSha256=(Get-OwnerOnlySha256Hex -Bytes ([Text.Encoding]::ASCII.GetBytes($encoded)));ProgramSha256=$programSha256;Stub=$stub}
}

function Get-OwnerOnlyClrOverrideNames {
    # .NET Framework honours these startup/profiler/AppDomain-manager overrides; refuse rather than run under them.
    param([AllowEmptyCollection()][string[]]$Names)
    return @(@($Names)|Where-Object{$_ -match '^(COMPLUS_|COR_ENABLE_PROFILING$|COR_PROFILER|APPDOMAIN_MANAGER_)'})
}

function Assert-OwnerOnlySystemPowerShell {
    $literal='C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe'
    $system=[Environment]::GetFolderPath([Environment+SpecialFolder]::System)
    if(!$system.Equals('C:\Windows\System32',[StringComparison]::OrdinalIgnoreCase) -or !(Test-Path -LiteralPath $literal -PathType Leaf)){throw 'The fixed system Windows PowerShell path is unavailable.'}
    Assert-OwnerOnlyNoReparseChain -Path $literal
    return $literal
}

function Test-OwnerOnlyElevated {
    return ([Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-OwnerOnlyWorkingDirectoryOutside {
    # A working directory inside the install or state root holds it open and blocks the elevated removal.
    param([Parameter(Mandatory)][string[]]$Roots)
    $paths=@([Environment]::CurrentDirectory)
    $location=Get-Location
    if($location.Provider.Name -ceq 'FileSystem'){$paths+=$location.ProviderPath}
    foreach($path in $paths){
        $full=[IO.Path]::GetFullPath($path).TrimEnd([IO.Path]::DirectorySeparatorChar)
        foreach($root in $Roots){
            $rootFull=[IO.Path]::GetFullPath($root).TrimEnd([IO.Path]::DirectorySeparatorChar)
            if($full.Equals($rootFull,[StringComparison]::OrdinalIgnoreCase) -or $full.StartsWith($rootFull+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Run the caller from a working directory outside the PC Steward install and state roots.'}
        }
    }
}

function Assert-OwnerOnlyCallerEnvironment {
    param([Parameter(Mandatory)][string]$InstallerSid)
    if($PSVersionTable.PSEdition -cne 'Desktop' -or $PSVersionTable.PSVersion.Major -ne 5){throw 'Run this caller in Windows PowerShell 5.1.'}
    # 64-bit system Windows PowerShell only: a 32-bit host would resolve Program Files (x86) for the execution root.
    if(![Environment]::Is64BitProcess -or !$PSHOME.TrimEnd('\').Equals('C:\Windows\System32\WindowsPowerShell\v1.0',[StringComparison]::OrdinalIgnoreCase)){throw 'Run this caller in 64-bit system Windows PowerShell (C:\Windows\System32\WindowsPowerShell\v1.0).'}
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
    if(Test-OwnerOnlyElevated){throw 'Run this caller from a normal, non-elevated session; it requests elevation itself only in -ExecuteApprovedPlan.'}
    if($identity.User.Value -cne $InstallerSid){throw 'The current Windows user is not the approved installing user.'}
    $names=@([Environment]::GetEnvironmentVariables().Keys)+@([Environment]::GetEnvironmentVariables([EnvironmentVariableTarget]::User).Keys)+@([Environment]::GetEnvironmentVariables([EnvironmentVariableTarget]::Machine).Keys)
    if(@(Get-OwnerOnlyClrOverrideNames -Names ([string[]]$names)).Count){throw 'A CLR startup override is configured; remove it before the redeploy.'}
    foreach($scope in @('MachinePolicy','UserPolicy')){if([string](Get-ExecutionPolicy -Scope $scope) -cne 'Undefined'){throw 'An enforced PowerShell execution policy is configured; this caller does not override it.'}}
    $null=Assert-OwnerOnlySystemPowerShell
}

function Assert-OwnerOnlyCandidateTree {
    param([Parameter(Mandatory)][string]$Root,[Parameter(Mandatory)][byte[]]$ManifestBytes,[Parameter(Mandatory)][string]$ExpectedOrigin)
    $text=[Text.UTF8Encoding]::new($false,$true).GetString($ManifestBytes).TrimStart([char]0xfeff)
    if(!$text.TrimStart().StartsWith('[')){throw 'Candidate manifest must be an array.'}
    $parsed=ConvertFrom-Json -InputObject $text
    $expected=@{};$total=[long]0;$origin='pinned-unsigned-v1'
    foreach($entry in $parsed){
        if($null -eq $entry -or (Get-OwnerOnlyOrdinalNames -Object $entry) -cne 'bytes,path,sha256' -or $entry.path -isnot [string] -or $entry.sha256 -isnot [string] -or $entry.sha256 -cnotmatch '^[A-Fa-f0-9]{64}$' -or ($entry.bytes -isnot [int] -and $entry.bytes -isnot [long]) -or $entry.bytes -lt 0){throw 'Malformed candidate manifest entry.'}
        if($entry.path -cnotmatch '^[^\\/:*?"<>|\x00-\x1F]+(\\[^\\/:*?"<>|\x00-\x1F]+)*$' -or @($entry.path.Split('\')|Where-Object{$_ -eq '.' -or $_ -eq '..' -or $_.EndsWith('.') -or $_.EndsWith(' ')}).Count -or $expected.ContainsKey($entry.path.ToUpperInvariant())){throw 'Unsafe candidate manifest path.'}
        $expected[$entry.path.ToUpperInvariant()]=$entry
        if($entry.path -ceq 'PCSteward.DeploymentCapability.txt'){$origin='pinned-unsigned-v2'}
    }
    if($expected.Count -eq 0){throw 'Candidate manifest is empty.'}
    if($origin -cne $ExpectedOrigin){throw 'The pinned candidate payload origin differs from the plan.'}
    Assert-OwnerOnlyNoReparseChain -Path $Root
    $rootFull=(Get-Item -LiteralPath $Root -Force).FullName.TrimEnd([IO.Path]::DirectorySeparatorChar)
    $seen=0
    foreach($item in @(Get-ChildItem -LiteralPath $rootFull -Recurse -Force)){
        if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Reparse path refused in the candidate payload.'}
        if($item -is [IO.DirectoryInfo]){continue}
        $relative=$item.FullName.Substring($rootFull.Length+1).Replace([IO.Path]::DirectorySeparatorChar,'\')
        $entry=$expected[$relative.ToUpperInvariant()]
        if($null -eq $entry -or $entry.path -cne $relative){throw 'The candidate payload has an unpinned file.'}
        $identity=Get-OwnerOnlyStableFileIdentity -Path $item.FullName
        if($identity.bytes -ne [long]$entry.bytes -or !$identity.sha256.Equals($entry.sha256,[StringComparison]::OrdinalIgnoreCase)){throw 'The candidate payload differs from its pinned manifest.'}
        $total+=$identity.bytes;$seen++
    }
    if($seen -ne $expected.Count){throw 'The candidate payload is missing a pinned file.'}
    return [pscustomobject]@{files=$seen;bytes=$total;payloadOrigin=$origin}
}

function Assert-OwnerOnlyOldLedger {
    param([Parameter(Mandatory)][string]$LedgerPath,[Parameter(Mandatory)]$Plan,[Parameter(Mandatory)][string]$InstallRoot)
    $bytes=Read-OwnerOnlyStableBytes -Path $LedgerPath -MaxBytes 4194304
    if((Get-OwnerOnlySha256Hex -Bytes $bytes) -cne $Plan.oldLedgerSha256){throw 'Approved old ledger changed.'}
    $ledger=ConvertFrom-Json -InputObject ([Text.UTF8Encoding]::new($false,$true).GetString($bytes).TrimStart([char]0xfeff))
    if($ledger.schema -cne 'pc-steward-owner-only-protected-test-v6' -or $ledger.completed -isnot [bool] -or !$ledger.completed -or $ledger.certificateThumbprint -cne $Plan.oldThumbprint -or $ledger.installerSid -cne $Plan.installerSid -or $ledger.installRoot -cne $InstallRoot){throw 'Old ledger identity changed.'}
    $origin=if($null -eq $ledger.PSObject.Properties['payloadOrigin']){'legacy-v6'}else{[string]$ledger.payloadOrigin}
    if($origin -cne $Plan.expectedOldOrigin){throw 'The old deployment origin differs from the plan.'}
    return $ledger
}

function Assert-OwnerOnlyOldSigner {
    param([Parameter(Mandatory)][string]$InstallRoot,[Parameter(Mandatory)][string]$Thumbprint)
    foreach($leaf in @('PCSteward.Desktop.exe','PCSteward.ElevatedHelper.exe')){
        $signature=Get-AuthenticodeSignature -LiteralPath (Join-Path $InstallRoot $leaf)
        if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -cne $Thumbprint){throw 'The old signed deployment is not valid for the approved signer.'}
    }
    $notAfter=$null
    foreach($name in @('My','Root','TrustedPublisher')){
        $store=[Security.Cryptography.X509Certificates.X509Store]::new($name,[Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
        $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
        try{
            $found=$store.Certificates.Find([Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint,$Thumbprint,$false)
            if($found.Count -ne 1){throw "The old owned signer is not present exactly once in CurrentUser\$name."}
            $notAfter=$found[0].NotAfter.ToUniversalTime()
        }finally{$store.Close()}
    }
    if($notAfter -le [DateTime]::UtcNow.AddHours(1)){throw 'The old signer expires within one hour; the redeploy fails closed (gap G8).'}
    return $notAfter
}

function Get-OwnerOnlyExecutionRootPrefix {
    return (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)) 'PC Steward OwnerOnlyTest Execution ')
}

function Get-OwnerOnlyAttemptRecordPath {
    param([Parameter(Mandatory)][string]$PlanPath,[Parameter(Mandatory)][string]$PlanSha256)
    if($PlanSha256 -cnotmatch '^[A-F0-9]{64}$'){throw 'Malformed plan hash.'}
    return (Join-Path (Split-Path -Parent ([IO.Path]::GetFullPath($PlanPath))) ('redeploy-attempt-' + $PlanSha256.Substring(0,16) + '.json'))
}

function New-OwnerOnlyAttemptRecord {
    # One use: CreateNew fails if any record for this plan already exists. The handle stays open for the final write.
    param([Parameter(Mandatory)][string]$Path,[Parameter(Mandatory)]$Value)
    try{$stream=[IO.FileStream]::new($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)}
    catch{if(Test-Path -LiteralPath $Path){throw 'An attempt record for this plan already exists; the approval is used. Never delete or reuse it to run again.'};throw}
    try{Write-OwnerOnlyAttemptRecord -Stream $stream -Value $Value}catch{$stream.Dispose();throw}
    return $stream
}

function Write-OwnerOnlyAttemptRecord {
    param([Parameter(Mandatory)][IO.FileStream]$Stream,[Parameter(Mandatory)]$Value)
    $bytes=[Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $Value -Depth 5))
    $Stream.SetLength(0);$Stream.Position=0;$Stream.Write($bytes,0,$bytes.Length);$Stream.Flush($true)
}

function Assert-OwnerOnlyBootstrapResultAcl {
    # The template writes result.json with New-OwnerOnlyExecutionSecurity (leaf): SYSTEM and Administrators full control,
    # OWNER RIGHTS read-control and Users read/execute, four explicit entries. Task 49's caller checked a different writer's
    # three-entry ACL against this four-entry shape; this check matches the writer that actually produces result.json.
    param([Parameter(Mandatory)]$Acl)
    if($Acl -isnot [Security.AccessControl.FileSecurity]){throw 'The bootstrap result must be a file.'}
    Assert-OwnerOnlyExecutionAcl -Acl $Acl
}

function Read-OwnerOnlyBootstrapResult {
    param([Parameter(Mandatory)][string]$ExecutionRoot)
    $path=Join-Path $ExecutionRoot 'result.json'
    if(!(Test-Path -LiteralPath $path -PathType Leaf)){return $null}
    Assert-OwnerOnlyNoReparseChain -Path $path
    Assert-OwnerOnlyBootstrapResultAcl -Acl (Get-Acl -LiteralPath $path)
    return (ConvertFrom-Json -InputObject (ConvertFrom-OwnerOnlyScriptBytes -Bytes (Read-OwnerOnlyStableBytes -Path $path -MaxBytes 65536)))
}

function Get-OwnerOnlyAttemptClassification {
    param($NativeExitCode,$Result,[Parameter(Mandatory)][object[]]$Pins,$PostCheck)
    if($null -eq $NativeExitCode){return 'PendingOrUnknown'}
    if($NativeExitCode -ne 0 -or $null -eq $Result){return 'StoppedOrUnknown'}
    $database=@($Pins|Where-Object{$_.path -ceq 'pcsteward.db'})[0]
    if($Result.exitCode -ne 0 -or $Result.phase -cne 'validated' -or $Result.userDataPreserved -isnot [bool] -or !$Result.userDataPreserved -or $Result.userDatabaseSha256 -cne $database.sha256 -or [long]$Result.userDatabaseBytes -ne [long]$database.bytes -or $Result.launched -isnot [bool] -or $Result.launched){return 'StoppedOrUnknown'}
    if($null -eq $PostCheck -or @($PostCheck).Count -ne @($Pins).Count){return 'ValidatedPostcheckFailed'}
    return 'Validated'
}

function Invoke-OwnerOnlyRedeployCheck {
    # Read-only. Every check runs again in -ExecuteApprovedPlan immediately before the claim.
    param([Parameter(Mandatory)][string]$PlanPath)
    $record=Read-OwnerOnlyRedeployPlan -Path $PlanPath
    $plan=$record.Plan
    Assert-OwnerOnlyCallerEnvironment -InstallerSid $plan.installerSid
    Assert-OwnerOnlyNoSqliteLoaded
    $programFiles=[Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
    $installRoot=Join-Path $programFiles 'PC Steward';$stateRoot=Join-Path $programFiles 'PC Steward OwnerOnlyTest State'
    Assert-OwnerOnlyWorkingDirectoryOutside -Roots @($installRoot,$stateRoot)
    $source=Assert-OwnerOnlyExecutionPath -Path $plan.scriptSource
    $scripts=[Collections.Generic.List[object]]::new();$helpers=$null
    foreach($entry in @($plan.scripts)){
        if($entry.path -cin @('OwnerOnlyExecution.ps1','Wait-OwnerOnlyDeploymentProcess.ps1')){Assert-OwnerOnlyReviewedHelperPin -Name $entry.path -Sha256 $entry.sha256}
        $bytes=Read-OwnerOnlyPinnedScript -Source $source -Name $entry.path -Sha256 $entry.sha256
        if($entry.path -ceq 'OwnerOnlyExecution.ps1'){$helpers=ConvertFrom-OwnerOnlyScriptBytes -Bytes $bytes}
        $scripts.Add([ordered]@{path=$entry.path;sha256=$entry.sha256})
    }
    $templateBytes=Read-OwnerOnlyStableBytes -Path (Join-Path $source 'Invoke-OwnerOnlyApprovedRedeployment.ps1') -MaxBytes 1048576
    $templateSha256=Get-OwnerOnlySha256Hex -Bytes $templateBytes
    Assert-OwnerOnlyReviewedHelperPin -Name 'Invoke-OwnerOnlyApprovedRedeployment.ps1' -Sha256 $templateSha256
    $fresh=$plan.schema -ceq 'pc-steward-exact-fresh-install-v1';$notAfter=$null
    if($fresh){Assert-OwnerOnlyFreshInstallBaseline -InstallRoot $installRoot -StateRoot $stateRoot}
    else{
        $null=Assert-OwnerOnlyOldLedger -LedgerPath (Join-Path $stateRoot 'deployment.json') -Plan $plan -InstallRoot $installRoot
        $notAfter=Assert-OwnerOnlyOldSigner -InstallRoot $installRoot -Thumbprint $plan.oldThumbprint
    }
    $null=Assert-OwnerOnlyExecutionPath -Path $plan.candidateRoot
    $null=Assert-OwnerOnlyExecutionPath -Path $plan.candidateManifestPath
    $manifestBytes=Read-OwnerOnlyStableBytes -Path $plan.candidateManifestPath -MaxBytes 1048576
    if((Get-OwnerOnlySha256Hex -Bytes $manifestBytes) -cne $plan.candidateManifestSha256){throw 'Approved candidate manifest changed.'}
    $candidate=Assert-OwnerOnlyCandidateTree -Root $plan.candidateRoot -ManifestBytes $manifestBytes -ExpectedOrigin $plan.expectedNewOrigin
    $prefix=Get-OwnerOnlyExecutionRootPrefix
    $execution=[IO.Path]::GetFullPath($plan.executionRoot)
    if(!$execution.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or $execution.Substring($prefix.Length) -cnotmatch '^[a-f0-9]{32}$' -or (Test-Path -LiteralPath $execution)){throw 'Unapproved or existing execution root.'}
    $data=Get-OwnerOnlyUserDataRoot
    if([IO.Path]::GetFullPath($plan.userDataRoot).TrimEnd('\') -cne $data){throw 'The approved user data root differs.'}
    Assert-OwnerOnlyScheduledCheckInactive
    $userData=@(Assert-OwnerOnlyUserDataPreserved -DataRoot $data -Files $record.Pins)
    $attemptPath=Get-OwnerOnlyAttemptRecordPath -PlanPath $PlanPath -PlanSha256 $record.Sha256
    if(Test-Path -LiteralPath $attemptPath){throw 'An attempt record for this plan already exists; the approval is used. Never delete or reuse it to run again.'}
    $encoded=ConvertTo-OwnerOnlyEncodedCommand -ProgramText (New-OwnerOnlyRedeployProgram -PlanJson $record.Json -ExecutionHelpersText $helpers -TemplateText (ConvertFrom-OwnerOnlyScriptBytes -Bytes $templateBytes))
    $database=@($userData|Where-Object{$_.path -ceq 'pcsteward.db'})[0]
    $package=try{Get-OwnerOnlyPackageFullNameResult}catch{$_.Exception.Message}
    $report=[ordered]@{
        mode='CheckOnly';result='Pass';schema=$plan.schema;planSha256=$record.Sha256
        programSha256=$encoded.ProgramSha256;encodedCommandSha256=$encoded.EncodedSha256;encodedCommandLength=$encoded.Encoded.Length
        bootstrapTemplateSha256=$templateSha256;scripts=$scripts.ToArray()
        expectedOldOrigin=$plan.expectedOldOrigin;expectedNewOrigin=$plan.expectedNewOrigin
        candidateManifestSha256=$plan.candidateManifestSha256;candidateFiles=$candidate.files;candidateBytes=$candidate.bytes
    }
    if($fresh){$report['installRootAbsent']=$true;$report['stateRootAbsent']=$true;$report['ownerOnlyTestCertificates']=0}
    else{$report['oldLedgerSha256']=$plan.oldLedgerSha256;$report['oldThumbprint']=$plan.oldThumbprint;$report['oldSignerNotAfterUtc']=$notAfter.ToString('o')}
    $report['userDatabase']=[ordered]@{path=$database.path;bytes=$database.bytes;sha256=$database.sha256};$report['userDataFiles']=$userData.Count
    $report['sqliteSidecars']=0;$report['pcstewardProcesses']=0;$report['scheduledCheckTask']='AbsentOrDisabled';$report['sqliteLoaded']=$false
    $report['elevated']=$false;$report['executionRootAbsent']=$true;$report['attemptRecordAbsent']=$true
    # The main dispatch refuses CheckOnly and -ExecuteApprovedPlan unless the query returned exactly 15700.
    $report['packageQueryResult']=$package;$report['unpackagedProcess']=($package -is [int] -and $package -eq 15700)
    return [pscustomobject]@{Report=$report;Encoded=$encoded.Encoded;ExecutionRoot=$execution;UserDataRoot=$data;Pins=$record.Pins;AttemptRecordPath=$attemptPath}
}

function Start-OwnerOnlyElevatedRedeploy {
    param([Parameter(Mandatory)][string]$EncodedCommand)
    if($EncodedCommand.Length -gt 30000 -or $EncodedCommand -cnotmatch '^[A-Za-z0-9+/]+={0,2}$'){throw 'Refusing an unbounded or malformed encoded command.'}
    $null=Assert-OwnerOnlySystemPowerShell
    # Exactly one UAC request. No -Wait: the Task 48 review traced a null exit code to that pattern; the pinned native
    # waiter captures the real exit from this original process object instead.
    return (Start-Process -FilePath 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -ArgumentList @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-EncodedCommand',$EncodedCommand) -WorkingDirectory 'C:\Windows\System32' -Verb RunAs -WindowStyle Hidden -PassThru)
}

function Invoke-OwnerOnlyRedeployExecution {
    param([Parameter(Mandatory)][string]$PlanPath,[Parameter(Mandatory)][string]$ApprovedPlanSha256,[Parameter(Mandatory)][string]$ApprovedEncodedCommandSha256)
    if($ApprovedPlanSha256 -cnotmatch '^[A-F0-9]{64}$' -or $ApprovedEncodedCommandSha256 -cnotmatch '^[A-F0-9]{64}$'){throw 'The approved hashes must be uppercase SHA-256 values.'}
    if(!(Get-Command -Name Wait-OwnerOnlyDeploymentProcess -CommandType Function -ErrorAction SilentlyContinue)){throw 'The pinned native waiter is not loaded.'}
    $check=Invoke-OwnerOnlyRedeployCheck -PlanPath $PlanPath
    if($check.Report.planSha256 -cne $ApprovedPlanSha256 -or $check.Report.encodedCommandSha256 -cne $ApprovedEncodedCommandSha256){throw 'The plan or encoded command differs from the approved pins.'}
    # R2: never elevate from a process with package identity; refused before the one-use claim, so the approval is not used.
    Assert-OwnerOnlyUnpackagedProcess
    # Compile the waiter's exit-code type before the claim, so a compile failure cannot follow an elevated start.
    if(!('PCSteward.Deployment.ChildExit' -as [type])){try{$null=Wait-OwnerOnlyDeploymentProcess -Process ([Diagnostics.Process]::GetCurrentProcess()) -TimeoutMilliseconds 1}catch{}}
    if(!('PCSteward.Deployment.ChildExit' -as [type])){throw 'The pinned native waiter could not be prepared.'}
    Assert-OwnerOnlyNoSqliteLoaded
    $attempt=[ordered]@{schema='pc-steward-owner-only-redeploy-attempt-v1';attempt=1;status='Claimed';planSha256=$check.Report.planSha256;encodedCommandSha256=$check.Report.encodedCommandSha256;startedUtc=[DateTime]::UtcNow.ToString('o');processId=$null;nativeExitCode=$null;resultExitCode=$null;resultPhase=$null;resultFailure=$null;userDatabaseSha256=$check.Report.userDatabase.sha256;postcheckFailure=$null;recordFailure=$null;failure=$null;completedUtc=$null;retry=$false;launched=$false}
    $stream=New-OwnerOnlyAttemptRecord -Path $check.AttemptRecordPath -Value $attempt
    try{
        $process=$null
        try{$process=Start-OwnerOnlyElevatedRedeploy -EncodedCommand $check.Encoded}catch{$attempt.status='NotStarted';$attempt.failure=$_.Exception.Message}
        if($null -ne $process){
            # The elevated child is never left unobserved: a failed "Started" write is recorded and the wait still happens.
            $attempt.status='Started';$attempt.processId=$process.Id
            try{Write-OwnerOnlyAttemptRecord -Stream $stream -Value $attempt}catch{$attempt.recordFailure=$_.Exception.Message}
            try{$attempt.nativeExitCode=[long](Wait-OwnerOnlyDeploymentProcess -Process $process -TimeoutMilliseconds 900000)}
            catch{$attempt.status='PendingOrUnknown';$attempt.failure=$_.Exception.Message}
        }
        if($null -ne $attempt.nativeExitCode){
            $result=$null;$post=$null
            try{$result=Read-OwnerOnlyBootstrapResult -ExecutionRoot $check.ExecutionRoot}catch{$attempt.failure=$_.Exception.Message}
            if($null -ne $result){$attempt.resultExitCode=$result.exitCode;$attempt.resultPhase=$result.phase;if($null -ne $result.PSObject.Properties['failure']){$attempt.resultFailure=$result.failure}}
            try{$post=@(Assert-OwnerOnlyUserDataPreserved -DataRoot $check.UserDataRoot -Files $check.Pins)}catch{$attempt.postcheckFailure=$_.Exception.Message}
            $attempt.status=Get-OwnerOnlyAttemptClassification -NativeExitCode $attempt.nativeExitCode -Result $result -Pins $check.Pins -PostCheck $post
        }
    }finally{
        $attempt.completedUtc=[DateTime]::UtcNow.ToString('o')
        try{Write-OwnerOnlyAttemptRecord -Stream $stream -Value $attempt}finally{$stream.Dispose()}
    }
    return $attempt
}

function New-OwnerOnlyRedeployPlan {
    # Read-only host inspection; the only write is the new plan file. Nothing is executed, elevated, deleted or changed.
    param([Parameter(Mandatory)][string]$NewPlanPath,[Parameter(Mandatory)][string]$ScriptSource,[Parameter(Mandatory)][string]$CandidateRoot,[Parameter(Mandatory)][string]$CandidateManifestPath,[Parameter(Mandatory)][string]$ExpectedOldOrigin,[Parameter(Mandatory)][string]$ExpectedNewOrigin)
    Assert-OwnerOnlyAbsoluteInputPath -Path $ScriptSource -Name 'ScriptSource'
    Assert-OwnerOnlyAbsoluteInputPath -Path $NewPlanPath -Name 'NewPlanPath'
    $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    Assert-OwnerOnlyCallerEnvironment -InstallerSid $sid
    Assert-OwnerOnlyNoSqliteLoaded
    $source=Assert-OwnerOnlyExecutionPath -Path $ScriptSource
    $scripts=[Collections.Generic.List[object]]::new()
    foreach($name in @(Get-OwnerOnlyReviewedScriptNames)){$scripts.Add([pscustomobject]@{path=$name;sha256=(Get-OwnerOnlySha256Hex -Bytes (Read-OwnerOnlyStableBytes -Path (Join-Path $source $name) -MaxBytes 1048576))})}
    foreach($entry in @($scripts|Where-Object{$_.path -cin @('OwnerOnlyExecution.ps1','Wait-OwnerOnlyDeploymentProcess.ps1')})){Assert-OwnerOnlyReviewedHelperPin -Name $entry.path -Sha256 $entry.sha256}
    Assert-OwnerOnlyReviewedHelperPin -Name 'Invoke-OwnerOnlyApprovedRedeployment.ps1' -Sha256 (Get-OwnerOnlySha256Hex -Bytes (Read-OwnerOnlyStableBytes -Path (Join-Path $source 'Invoke-OwnerOnlyApprovedRedeployment.ps1') -MaxBytes 1048576))
    $programFiles=[Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
    $installRoot=Join-Path $programFiles 'PC Steward';$stateRoot=Join-Path $programFiles 'PC Steward OwnerOnlyTest State'
    # R2: -ExpectedOldOrigin none selects the fresh-install schema, which never reads an old ledger, signer or thumbprint.
    $fresh=$ExpectedOldOrigin -ceq 'none'
    $ledgerPath=Join-Path $stateRoot 'deployment.json'
    $manifestPath=Assert-OwnerOnlyExecutionPath -Path $CandidateManifestPath
    $data=Get-OwnerOnlyUserDataRoot
    $database=Get-OwnerOnlyStableFileIdentity -Path (Join-Path $data 'pcsteward.db')
    $fields=[ordered]@{
        schema=$(if($fresh){'pc-steward-exact-fresh-install-v1'}else{'pc-steward-exact-redeploy-v3'});expectedOldOrigin=$ExpectedOldOrigin;expectedNewOrigin=$ExpectedNewOrigin;preserveUserData=$true
        userDataRoot=$data;userDataFiles=[object[]]@([pscustomobject]@{bytes=[long]$database.bytes;path='pcsteward.db';sha256=$database.sha256})
        installerSid=$sid;executionRoot=((Get-OwnerOnlyExecutionRootPrefix)+[Guid]::NewGuid().ToString('N'));scriptSource=$source;scripts=[object[]]$scripts.ToArray()
        candidateRoot=(Assert-OwnerOnlyExecutionPath -Path $CandidateRoot);candidateManifestPath=$manifestPath
        candidateManifestSha256=(Get-OwnerOnlySha256Hex -Bytes (Read-OwnerOnlyStableBytes -Path $manifestPath -MaxBytes 1048576))
    }
    if(!$fresh){
        $ledger=ConvertFrom-Json -InputObject (ConvertFrom-OwnerOnlyScriptBytes -Bytes (Read-OwnerOnlyStableBytes -Path $ledgerPath -MaxBytes 4194304))
        $fields['oldLedgerSha256']=(Get-OwnerOnlySha256Hex -Bytes (Read-OwnerOnlyStableBytes -Path $ledgerPath -MaxBytes 4194304));$fields['oldThumbprint']=[string]$ledger.certificateThumbprint
    }
    $plan=[pscustomobject]$fields
    $json=ConvertTo-OwnerOnlyCanonicalPlanJson -Plan $plan
    # Cross-check the assembled pins against the host exactly as CheckOnly will (minus the plan file itself).
    if($fresh){Assert-OwnerOnlyFreshInstallBaseline -InstallRoot $installRoot -StateRoot $stateRoot}
    else{
        $null=Assert-OwnerOnlyOldLedger -LedgerPath $ledgerPath -Plan $plan -InstallRoot $installRoot
        $null=Assert-OwnerOnlyOldSigner -InstallRoot $installRoot -Thumbprint $plan.oldThumbprint
    }
    $null=Assert-OwnerOnlyCandidateTree -Root $plan.candidateRoot -ManifestBytes (Read-OwnerOnlyStableBytes -Path $manifestPath -MaxBytes 1048576) -ExpectedOrigin $ExpectedNewOrigin
    Assert-OwnerOnlyScheduledCheckInactive
    $null=Assert-OwnerOnlyUserDataPreserved -DataRoot $data -Files @(Assert-OwnerOnlyRedeployPlanV3 -Plan $plan)
    $target=[IO.Path]::GetFullPath($NewPlanPath)
    foreach($forbidden in @($programFiles,$data,[Environment]::GetFolderPath([Environment+SpecialFolder]::Windows))){if($target.StartsWith($forbidden.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Write the plan outside Program Files, Windows and the PC Steward data root.'}}
    $bytes=[Text.Encoding]::ASCII.GetBytes($json)
    $output=[IO.FileStream]::new($target,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try{$output.Write($bytes,0,$bytes.Length);$output.Flush($true)}finally{$output.Dispose()}
    return [ordered]@{mode='NewPlan';result='Written';planSha256=(Get-OwnerOnlySha256Hex -Bytes $bytes);schema=$plan.schema;userDatabaseSha256=$database.sha256;userDatabaseBytes=$database.bytes;executed=$false;elevated=$false}
}

# ---- Main: dispatch one mode. No exit: a failure prints a JSON report and then throws (the console stays open). ----
$mode=$PSCmdlet.ParameterSetName;$report=$null
try{
    if($PSVersionTable.PSEdition -cne 'Desktop' -or $PSVersionTable.PSVersion.Major -ne 5){throw 'Run this caller in Windows PowerShell 5.1.'}
    if($mode -ceq 'NewPlan'){
        # Absolute local paths only, before anything is read; the helper loads only at a reviewed constant.
        Assert-OwnerOnlyAbsoluteInputPath -Path $ScriptSource -Name 'ScriptSource'
        Assert-OwnerOnlyAbsoluteInputPath -Path $NewPlanPath -Name 'NewPlanPath'
        $helperSha=Get-OwnerOnlySha256Hex -Bytes (Read-OwnerOnlyStableBytes -Path (Join-Path $ScriptSource 'OwnerOnlyExecution.ps1') -MaxBytes 1048576)
        . (Get-OwnerOnlyPinnedScriptBlock -Source $ScriptSource -Name 'OwnerOnlyExecution.ps1' -Sha256 $helperSha)
        # R2 review: a packaged process may see a private view of %LOCALAPPDATA% and the per-user stores; plan nothing there.
        Assert-OwnerOnlyUnpackagedProcess
        $report=New-OwnerOnlyRedeployPlan -NewPlanPath $NewPlanPath -ScriptSource $ScriptSource -CandidateRoot $CandidateRoot -CandidateManifestPath $CandidateManifestPath -ExpectedOldOrigin $ExpectedOldOrigin -ExpectedNewOrigin $ExpectedNewOrigin
    }else{
        # Execution first requires the approved plan hash. The helpers then load only when the plan's pin equals the reviewed
        # constant embedded in this caller (each a function-only file adding only new functions); the full check follows.
        $planBytes=Read-OwnerOnlyStableBytes -Path $PlanPath -MaxBytes 65536
        if($mode -ceq 'Execute' -and (Get-OwnerOnlySha256Hex -Bytes $planBytes) -cne $ApprovedPlanSha256){throw 'The plan differs from the approved plan hash.'}
        $preview=ConvertFrom-Json -InputObject ([Text.Encoding]::ASCII.GetString($planBytes))
        foreach($name in @('OwnerOnlyExecution.ps1','Wait-OwnerOnlyDeploymentProcess.ps1')){
            $pin=@(@($preview.scripts)|Where-Object{$_.path -ceq $name})
            if($pin.Count -ne 1 -or $preview.scriptSource -isnot [string]){throw 'Malformed reviewed script entry.'}
            if($name -ceq 'OwnerOnlyExecution.ps1' -or $mode -ceq 'Execute'){. (Get-OwnerOnlyPinnedScriptBlock -Source $preview.scriptSource -Name $name -Sha256 ([string]$pin[0].sha256))}
        }
        # R2 review: CheckOnly fails too in a packaged process, so its hashes are never offered for approval from one.
        Assert-OwnerOnlyUnpackagedProcess
        if($mode -ceq 'Execute'){$report=Invoke-OwnerOnlyRedeployExecution -PlanPath $PlanPath -ApprovedPlanSha256 $ApprovedPlanSha256 -ApprovedEncodedCommandSha256 $ApprovedEncodedCommandSha256}
        else{$report=(Invoke-OwnerOnlyRedeployCheck -PlanPath $PlanPath).Report}
    }
    ConvertTo-Json -InputObject $report -Depth 6
    if($mode -ceq 'Execute' -and $report.status -cne 'Validated'){throw "The approved redeploy did not validate: $($report.status). Do not retry."}
}catch{
    if($null -eq $report -or $mode -cne 'Execute'){ConvertTo-Json -InputObject ([ordered]@{mode=$mode;result='Fail';failure=$_.Exception.Message}) -Depth 3}
    throw
}
