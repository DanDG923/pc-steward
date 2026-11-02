# Bootstrap template. The exact reviewed plan and pure ACL functions must be embedded
# into the approved encoded command; never invoke this template from a mutable -File.
$ErrorActionPreference='Stop'
$env:PSModulePath=[IO.Path]::Combine($PSHOME,'Modules')
$env:PATH=[IO.Path]::Combine($env:SystemRoot,'System32')
if(!$OwnerOnlyPlanJson){throw 'The exact embedded approval record is required.'}
$OwnerOnlyPlan=ConvertFrom-Json -InputObject $OwnerOnlyPlanJson
if($null -eq $OwnerOnlyPlan -or $OwnerOnlyPlan.schema -cnotin @('pc-steward-exact-redeploy-v1','pc-steward-exact-redeploy-v2','pc-steward-exact-redeploy-v3')){throw 'An exact embedded owner-approved redeployment plan is required.'}
# Plan schema v3 (Task 50 G1) is v2 plus user-data preservation for a host that already has %LOCALAPPDATA%\PCSteward: a closed
# key set, preserveUserData true and exact database pins (bytes and SHA-256; no -wal/-shm/-journal sidecar, no PCSteward
# process, no reparse point). It replaces only the "data root absent" checks; v1 and v2 keep requiring an absent data root.
# Executing a v3 plan likewise needs its own separate exact owner approval with these pins.
$preserveUserData=$OwnerOnlyPlan.schema -ceq 'pc-steward-exact-redeploy-v3';$userDataPins=$null;$userData=$null
if($preserveUserData){$userDataPins=@(Assert-OwnerOnlyRedeployPlanV3 -Plan $OwnerOnlyPlan)}
# Expected payload origins (Task 50 adapter gates A2-L2). Plan schema v1 is the existing approved plan: it fixes the legacy-v6
# preflight and the pinned-unsigned-v1 postcondition and cannot name origins, so it can still deploy only v1. Schema v2 must
# name both origins from closed lists. Executing ANY new plan (v1 or v2) needs its own separate exact owner approval.
if($OwnerOnlyPlan.schema -ceq 'pc-steward-exact-redeploy-v1'){
    if($null -ne $OwnerOnlyPlan.PSObject.Properties['expectedOldOrigin'] -or $null -ne $OwnerOnlyPlan.PSObject.Properties['expectedNewOrigin']){throw 'Plan schema v1 cannot name payload origins.'}
    $expectedOldOrigin='legacy-v6';$expectedNewOrigin='pinned-unsigned-v1'
}else{
    $expectedOldOrigin=$OwnerOnlyPlan.expectedOldOrigin;$expectedNewOrigin=$OwnerOnlyPlan.expectedNewOrigin
    if($expectedOldOrigin -isnot [string] -or $expectedOldOrigin -cnotin @('legacy-v6','pinned-unsigned-v1','pinned-unsigned-v2') -or $expectedNewOrigin -isnot [string] -or $expectedNewOrigin -cnotin @('pinned-unsigned-v1','pinned-unsigned-v2')){throw 'Plan schema v2 must name an allowlisted expected old and new payload origin.'}
}
$principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if(!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) -or [Security.Principal.WindowsIdentity]::GetCurrent().User.Value -cne $OwnerOnlyPlan.installerSid){throw 'Exact installing SID and elevation required.'}
$programFiles=[Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$execution=[IO.Path]::GetFullPath($OwnerOnlyPlan.executionRoot)
$prefix=Join-Path $programFiles 'PC Steward OwnerOnlyTest Execution '
if(!$execution.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or $execution.Substring($prefix.Length) -cnotmatch '^[a-f0-9]{32}$' -or (Test-Path -LiteralPath $execution)){throw 'Unapproved or existing execution root.'}
$null=Assert-OwnerOnlyExecutionPath -Path $programFiles
$scripts=Join-Path $execution 'scripts';$compiler=Join-Path $execution 'compiler'
$allowed=@('Install-OwnerOnlyProtectedTestDeployment.ps1','Test-OwnerOnlyProtectedTestDeployment.ps1','Remove-OwnerOnlyProtectedTestDeployment.ps1','OwnerOnlyPayloadIntegrity.ps1','OwnerOnlyExecution.ps1','Wait-OwnerOnlyDeploymentProcess.ps1')
if(@($OwnerOnlyPlan.scripts).Count -ne $allowed.Count){throw 'Exactly the reviewed script bundle is required.'}
$oldTemp=$env:TEMP;$oldTmp=$env:TMP;$result=$null;$phase='protected-bootstrap';$ownsExecution=$false
try{
    [IO.Directory]::CreateDirectory($execution,(New-OwnerOnlyExecutionSecurity -Directory))|Out-Null
    $null=Assert-OwnerOnlyExecutionPath -Path $execution
    Assert-OwnerOnlyExecutionAcl -Acl (Get-Acl -LiteralPath $execution) -Directory
    $claim=[IO.FileStream]::new((Join-Path $execution 'claimed'),[IO.FileMode]::CreateNew,[Security.AccessControl.FileSystemRights]::Write,[IO.FileShare]::None,4096,[IO.FileOptions]::None,(New-OwnerOnlyExecutionSecurity))
    try{$claim.WriteByte(1);$claim.Flush($true);$ownsExecution=$true}finally{$claim.Dispose()}
    [IO.Directory]::CreateDirectory($scripts,(New-OwnerOnlyExecutionSecurity -Directory))|Out-Null
    [IO.Directory]::CreateDirectory($compiler,(New-OwnerOnlyExecutionSecurity -Directory -Compiler))|Out-Null
    foreach($directory in @($execution,$scripts)){Assert-OwnerOnlyExecutionAcl -Acl (Get-Acl -LiteralPath $directory) -Directory}
    Assert-OwnerOnlyExecutionAcl -Acl (Get-Acl -LiteralPath $compiler) -Directory -Compiler
    $seen=@{}
    $null=Assert-OwnerOnlyExecutionPath -Path $OwnerOnlyPlan.scriptSource
    foreach($entry in $OwnerOnlyPlan.scripts){
        if($entry.path -cnotin $allowed -or $seen.ContainsKey($entry.path) -or $entry.sha256 -cnotmatch '^[A-F0-9]{64}$'){throw 'Malformed reviewed script entry.'};$seen[$entry.path]=$true
        $source=Join-Path $OwnerOnlyPlan.scriptSource $entry.path;$null=Assert-OwnerOnlyExecutionPath -Path $source
        $input=[IO.File]::Open($source,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
        try{
            if($input.Length -gt 1024*1024){throw 'Script exceeds approved bootstrap bound.'}
            $buffer=[IO.MemoryStream]::new();try{$input.CopyTo($buffer);$bytes=$buffer.ToArray()}finally{$buffer.Dispose()}
            $sha=[Security.Cryptography.SHA256]::Create();try{$hash=[BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-','')}finally{$sha.Dispose()}
            if($hash -cne $entry.sha256){throw "Script hash changed: $($entry.path)"}
            $target=Join-Path $scripts $entry.path
            $output=[IO.FileStream]::new($target,[IO.FileMode]::CreateNew,[Security.AccessControl.FileSystemRights]::Write,[IO.FileShare]::None,4096,[IO.FileOptions]::None,(New-OwnerOnlyExecutionSecurity))
            try{$output.Write($bytes,0,$bytes.Length);$output.Flush($true)}finally{$output.Dispose()}
            Assert-OwnerOnlyExecutionAcl -Acl (Get-Acl -LiteralPath $target)
            if((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -cne $entry.sha256){throw 'Protected script copy differs.'}
        }finally{$input.Dispose()}
    }
    $env:TEMP=$compiler;$env:TMP=$compiler
    Set-Location -LiteralPath $scripts
    Assert-OwnerOnlyExecutionEnvironment -ScriptRoot $scripts
    # Only these hash-verified protected copies can now execute/compile privileged code.
    . (Join-Path $scripts 'OwnerOnlyPayloadIntegrity.ps1')
    $install=Join-Path $programFiles 'PC Steward';$state=Join-Path $programFiles 'PC Steward OwnerOnlyTest State';$ledger=Join-Path $state 'deployment.json'
    $data=Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'PCSteward'
    $phase='old-deployment-preflight'
    if((Get-FileHash -LiteralPath $ledger -Algorithm SHA256).Hash -cne $OwnerOnlyPlan.oldLedgerSha256 -or (!$preserveUserData -and (Test-Path -LiteralPath $data)) -or @(Get-Process -Name 'PCSteward*' -ErrorAction SilentlyContinue).Count){throw 'Approved old deployment/data/process baseline changed.'}
    if($preserveUserData){if([IO.Path]::GetFullPath($OwnerOnlyPlan.userDataRoot).TrimEnd('\') -cne $data){throw 'The approved user data root differs.'};Assert-OwnerOnlyNoSqliteLoaded;$userData=@(Assert-OwnerOnlyUserDataPreserved -DataRoot $data -Files $userDataPins)}
    $old=Get-Content -LiteralPath $ledger -Raw|ConvertFrom-Json
    if($old.certificateThumbprint -cne $OwnerOnlyPlan.oldThumbprint){throw 'Old signer changed.'}
    if($expectedOldOrigin -ceq 'legacy-v6'){$legacyText=& (Join-Path $scripts 'Test-OwnerOnlyProtectedTestDeployment.ps1') -AllowLegacyPayload}
    else{$legacyText=& (Join-Path $scripts 'Test-OwnerOnlyProtectedTestDeployment.ps1') -RequirePinnedPayload}
    $legacy=$legacyText|ConvertFrom-Json;if($legacy.validation -cne 'Pass' -or $legacy.payloadOrigin -cne $expectedOldOrigin){if($expectedOldOrigin -ceq 'legacy-v6'){throw 'Exact legacy preflight failed.'};throw 'Exact old-deployment preflight failed.'}
    $candidate=Read-OwnerOnlyCandidateManifest -Path $OwnerOnlyPlan.candidateManifestPath -ExpectedSha256 $OwnerOnlyPlan.candidateManifestSha256 -PayloadRoot $OwnerOnlyPlan.candidateRoot
    if($candidate.PayloadOrigin -cne $expectedNewOrigin){throw 'The pinned candidate payload origin differs from the plan.'}
    [PCSteward.Deployment.PayloadIntegrity]::AssertTree($OwnerOnlyPlan.candidateRoot,$candidate.Files)
    $phase='approved-removal'
    $removal=& (Join-Path $scripts 'Remove-OwnerOnlyProtectedTestDeployment.ps1') -Remove
    Write-OwnerOnlyProtectedJson -Path (Join-Path $execution 'removal-result.json') -Value ($removal|ConvertFrom-Json)
    if((Test-Path -LiteralPath $install) -or (Test-Path -LiteralPath $state) -or (!$preserveUserData -and (Test-Path -LiteralPath $data))){throw 'Removal postconditions failed.'}
    if($preserveUserData){$userData=@(Assert-OwnerOnlyUserDataPreserved -DataRoot $data -Files $userDataPins)}
    foreach($store in @('My','Root','TrustedPublisher')){if(Test-Path -LiteralPath ("Cert:\CurrentUser\$store\"+$OwnerOnlyPlan.oldThumbprint)){throw 'Old owned signer remains.'}}
    $phase='approved-pinned-installation'
    $installed=& (Join-Path $scripts 'Install-OwnerOnlyProtectedTestDeployment.ps1') -Install -CreateOwnerOnlyCertificate -CandidateRoot $OwnerOnlyPlan.candidateRoot -CandidateManifestPath $OwnerOnlyPlan.candidateManifestPath -CandidateManifestSha256 $OwnerOnlyPlan.candidateManifestSha256
    Write-OwnerOnlyProtectedJson -Path (Join-Path $execution 'installation-result.json') -Value ($installed|ConvertFrom-Json)
    $phase='independent-pinned-validation'
    $validated=& (Join-Path $scripts 'Test-OwnerOnlyProtectedTestDeployment.ps1') -RequirePinnedPayload
    $proof=$validated|ConvertFrom-Json
    if($proof.validation -cne 'Pass' -or $proof.payloadOrigin -cne $expectedNewOrigin -or ($expectedNewOrigin -ceq 'pinned-unsigned-v2' -and $proof.helperProtocol -cne '2') -or $proof.candidateManifestSha256 -cne $OwnerOnlyPlan.candidateManifestSha256 -or (!$preserveUserData -and (Test-Path -LiteralPath $data)) -or @(Get-Process -Name 'PCSteward*' -ErrorAction SilentlyContinue).Count){throw 'New exact identity/preservation postconditions failed.'}
    if($preserveUserData){$userData=@(Assert-OwnerOnlyUserDataPreserved -DataRoot $data -Files $userDataPins)}
    Write-OwnerOnlyProtectedJson -Path (Join-Path $execution 'independent-validator.json') -Value $proof
    if($preserveUserData){
        $database=@($userData|Where-Object{$_.path -ceq 'pcsteward.db'})[0]
        $result=[ordered]@{exitCode=0;phase='validated';payloadOrigin=$proof.payloadOrigin;candidateManifestSha256=$proof.candidateManifestSha256;certificateThumbprint=$proof.certificateThumbprint;launched=$false;applicationUpdates=0;userDataPreserved=$true;userDatabaseBytes=$database.bytes;userDatabaseSha256=$database.sha256;executionRoot=$execution}
    }else{
    $result=[ordered]@{exitCode=0;phase='validated';payloadOrigin=$proof.payloadOrigin;candidateManifestSha256=$proof.candidateManifestSha256;certificateThumbprint=$proof.certificateThumbprint;launched=$false;applicationUpdates=0;userDataRootAbsent=$true;executionRoot=$execution}
    }
}catch{
    $result=[ordered]@{exitCode=1;phase=$phase;failure=$_.Exception.Message;launched=$false;retry=$false;executionRoot=$execution}
}finally{
    $env:TEMP=$oldTemp;$env:TMP=$oldTmp
    # Retain the protected code, compiler area and result as declared evidence. No
    # recursive bootstrap cleanup or retry occurs after a partial/unknown result.
    if($result -and $ownsExecution -and (Test-Path -LiteralPath $execution)){
        $json=[Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $result -Depth 5))
        $file=Join-Path $execution 'result.json'
        $output=[IO.FileStream]::new($file,[IO.FileMode]::CreateNew,[Security.AccessControl.FileSystemRights]::Write,[IO.FileShare]::None,4096,[IO.FileOptions]::None,(New-OwnerOnlyExecutionSecurity))
        try{$output.Write($json,0,$json.Length);$output.Flush($true)}finally{$output.Dispose()}
    }
}
if(!$result -or $result.exitCode -ne 0){exit 1};exit 0
