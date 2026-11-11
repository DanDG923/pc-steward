# Bootstrap template (ADR-034 security review H1): runs only embedded in the approved encoded command that
# Invoke-PCStewardReleaseInstall.ps1 composes, after $PCStewardReleaseInstallJson and the pinned OwnerOnlyExecution.ps1
# functions. Elevated. It creates a new protected execution root under Program Files, copies the six reviewed scripts into
# it only when each one's SHA-256 is the pin in the embedded record, points TEMP and TMP at its private compiler folder,
# runs Install-PCStewardRelease.ps1 from there, and writes result.json (protected) for the caller.
$ErrorActionPreference='Stop'
$env:PSModulePath=[IO.Path]::Combine($PSHOME,'Modules')
$env:PATH=[IO.Path]::Combine($env:SystemRoot,'System32')
if(!$PCStewardReleaseInstallJson){throw 'The exact embedded release install record is required.'}
$record=ConvertFrom-Json -InputObject $PCStewardReleaseInstallJson
$keys='artefactSha256,artefactZip,backupPin,executionRoot,installerSid,msiPath,primaryPin,removeOwnerOnlyTestDeployment,runId,schema,scriptSource,scripts,sourceCommit'
if($null -eq $record -or $record.schema -cne 'pc-steward-release-install-command:1' -or (Get-OwnerOnlyOrdinalNames -Object $record) -cne $keys){throw 'An exact embedded release install record is required.'}
if($record.removeOwnerOnlyTestDeployment -isnot [bool]){throw 'removeOwnerOnlyTestDeployment must be true or false.'}
$principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if(!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) -or [Security.Principal.WindowsIdentity]::GetCurrent().User.Value -cne $record.installerSid){throw 'Exact installing SID and elevation required.'}
Assert-OwnerOnlyUnpackagedProcess
$programFiles=[Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$execution=[IO.Path]::GetFullPath($record.executionRoot)
$prefix=Join-Path $programFiles 'PC Steward OwnerOnlyTest Execution '
if(!$execution.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or $execution.Substring($prefix.Length) -cnotmatch '^[a-f0-9]{32}$' -or (Test-Path -LiteralPath $execution)){throw 'Unapproved or existing execution root.'}
$null=Assert-OwnerOnlyExecutionPath -Path $programFiles
$scripts=Join-Path $execution 'scripts';$compiler=Join-Path $execution 'compiler';$work=Join-Path $execution 'release'
$allowed=@('Install-PCStewardRelease.ps1','OwnerOnlyExecution.ps1','OwnerOnlyPayloadIntegrity.ps1','PCStewardMsiTables.ps1','PCStewardReleaseSigning.ps1','Remove-OwnerOnlyProtectedTestDeployment.ps1')
if(@($record.scripts).Count -ne $allowed.Count){throw 'Exactly the reviewed script bundle is required.'}
$oldTemp=$env:TEMP;$oldTmp=$env:TMP;$result=$null;$phase='protected-bootstrap';$ownsExecution=$false
try{
    [IO.Directory]::CreateDirectory($execution,(New-OwnerOnlyExecutionSecurity -Directory))|Out-Null
    $null=Assert-OwnerOnlyExecutionPath -Path $execution
    Assert-OwnerOnlyExecutionAcl -Acl (Get-Acl -LiteralPath $execution) -Directory
    $claim=[IO.FileStream]::new((Join-Path $execution 'claimed'),[IO.FileMode]::CreateNew,[Security.AccessControl.FileSystemRights]::Write,[IO.FileShare]::None,4096,[IO.FileOptions]::None,(New-OwnerOnlyExecutionSecurity))
    try{$claim.WriteByte(1);$claim.Flush($true);$ownsExecution=$true}finally{$claim.Dispose()}
    [IO.Directory]::CreateDirectory($scripts,(New-OwnerOnlyExecutionSecurity -Directory))|Out-Null
    [IO.Directory]::CreateDirectory($compiler,(New-OwnerOnlyExecutionSecurity -Directory -Compiler))|Out-Null
    [IO.Directory]::CreateDirectory($work,(New-OwnerOnlyExecutionSecurity -Directory))|Out-Null
    foreach($directory in @($execution,$scripts,$work)){Assert-OwnerOnlyExecutionAcl -Acl (Get-Acl -LiteralPath $directory) -Directory}
    Assert-OwnerOnlyExecutionAcl -Acl (Get-Acl -LiteralPath $compiler) -Directory -Compiler
    $seen=@{}
    $null=Assert-OwnerOnlyExecutionPath -Path $record.scriptSource
    foreach($entry in $record.scripts){
        if((Get-OwnerOnlyOrdinalNames -Object $entry) -cne 'path,sha256' -or $entry.path -cnotin $allowed -or $seen.ContainsKey($entry.path) -or $entry.sha256 -cnotmatch '^[A-F0-9]{64}$'){throw 'Malformed reviewed script entry.'};$seen[$entry.path]=$true
        $source=Join-Path $record.scriptSource $entry.path;$null=Assert-OwnerOnlyExecutionPath -Path $source
        $stream=[IO.File]::Open($source,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
        try{
            if($stream.Length -gt 1024*1024){throw 'Script exceeds approved bootstrap bound.'}
            $buffer=[IO.MemoryStream]::new();try{$stream.CopyTo($buffer);$bytes=$buffer.ToArray()}finally{$buffer.Dispose()}
        }finally{$stream.Dispose()}
        $sha=[Security.Cryptography.SHA256]::Create();try{$hash=[BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-','')}finally{$sha.Dispose()}
        if($hash -cne $entry.sha256){throw "Script hash changed: $($entry.path)"}
        $target=Join-Path $scripts $entry.path
        $output=[IO.FileStream]::new($target,[IO.FileMode]::CreateNew,[Security.AccessControl.FileSystemRights]::Write,[IO.FileShare]::None,4096,[IO.FileOptions]::None,(New-OwnerOnlyExecutionSecurity))
        try{$output.Write($bytes,0,$bytes.Length);$output.Flush($true)}finally{$output.Dispose()}
        Assert-OwnerOnlyExecutionAcl -Acl (Get-Acl -LiteralPath $target)
        if((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -cne $entry.sha256){throw 'Protected script copy differs.'}
    }
    $env:TEMP=$compiler;$env:TMP=$compiler
    Set-Location -LiteralPath $scripts
    Assert-OwnerOnlyExecutionEnvironment -ScriptRoot $scripts
    $phase='release-install'
    $arguments=@{ArtefactZip=[string]$record.artefactZip;MsiPath=[string]$record.msiPath;RunId=[long]$record.runId;SourceCommit=[string]$record.sourceCommit;ArtefactSha256=[string]$record.artefactSha256;PrimaryPin=[string]$record.primaryPin;BackupPin=[string]$record.backupPin;WorkRoot=$work}
    if($record.removeOwnerOnlyTestDeployment){$arguments.RemoveOwnerOnlyTestDeployment=$true}
    $installed=@(& (Join-Path $scripts 'Install-PCStewardRelease.ps1') @arguments)
    $last=$installed[-1]
    if($null -eq $last -or $last.validation -cne 'Pass'){throw 'The release install did not report Pass.'}
    $result=[ordered]@{exitCode=0;phase='validated'}
    foreach($key in $last.Keys){$result[$key]=$last[$key]}
    $result.executionRoot=$execution
}catch{
    $result=[ordered]@{exitCode=1;phase=$phase;failure=$_.Exception.Message;executionRoot=$execution}
}finally{
    $env:TEMP=$oldTemp;$env:TMP=$oldTmp
    if($result -and $ownsExecution -and (Test-Path -LiteralPath $execution)){
        $json=[Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $result -Depth 5))
        $file=Join-Path $execution 'result.json'
        $output=[IO.FileStream]::new($file,[IO.FileMode]::CreateNew,[Security.AccessControl.FileSystemRights]::Write,[IO.FileShare]::None,4096,[IO.FileOptions]::None,(New-OwnerOnlyExecutionSecurity))
        try{$output.Write($json,0,$json.Length);$output.Flush($true)}finally{$output.Dispose()}
    }
}
if(!$result -or $result.exitCode -ne 0){exit 1};exit 0
