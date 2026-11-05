# Pure PowerShell/.NET bootstrap policy: loading this file never compiles or mutates state.
function New-OwnerOnlyExecutionSecurity {
    param([switch]$Directory,[switch]$Compiler)
    $acl=if($Directory){[Security.AccessControl.DirectorySecurity]::new()}else{[Security.AccessControl.FileSecurity]::new()}
    $acl.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
    $acl.SetAccessRuleProtection($true,$false)
    $inherit=if($Directory){[Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit'}else{[Security.AccessControl.InheritanceFlags]::None}
    foreach($sid in @('S-1-5-18','S-1-5-32-544')){
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($sid),[Security.AccessControl.FileSystemRights]::FullControl,$inherit,[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow))
    }
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new('S-1-3-4'),[Security.AccessControl.FileSystemRights]::ReadPermissions,$inherit,[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow))
    if(!$Compiler){$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new('S-1-5-32-545'),([Security.AccessControl.FileSystemRights]::ReadAndExecute -bor [Security.AccessControl.FileSystemRights]::Synchronize),$inherit,[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow))}
    return $acl
}

function Assert-OwnerOnlyExecutionAcl {
    param([Parameter(Mandatory)][Security.AccessControl.FileSystemSecurity]$Acl,[switch]$Compiler,[switch]$Directory)
    if(!$Acl.AreAccessRulesProtected -or $Acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -ne 'S-1-5-32-544'){throw 'Execution infrastructure must be protected and Administrators-owned.'}
    $expected=@{'S-1-5-18'=[Security.AccessControl.FileSystemRights]::FullControl;'S-1-5-32-544'=[Security.AccessControl.FileSystemRights]::FullControl;'S-1-3-4'=([Security.AccessControl.FileSystemRights]::ReadPermissions -bor [Security.AccessControl.FileSystemRights]::Synchronize)}
    if(!$Compiler){$expected['S-1-5-32-545']=[Security.AccessControl.FileSystemRights]::ReadAndExecute -bor [Security.AccessControl.FileSystemRights]::Synchronize}
    $inherit=if($Directory){[Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit'}else{[Security.AccessControl.InheritanceFlags]::None}
    $seen=@{}
    foreach($rule in $Acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])){
        $sid=$rule.IdentityReference.Value
        if($rule.IsInherited -or $rule.AccessControlType -ne 'Allow' -or !$expected.ContainsKey($sid) -or $seen.ContainsKey($sid) -or $rule.FileSystemRights -ne $expected[$sid] -or $rule.InheritanceFlags -ne $inherit -or $rule.PropagationFlags -ne 'None'){throw 'Execution infrastructure ACL has unexpected authority.'}
        $seen[$sid]=$true
    }
    if($seen.Count -ne $expected.Count){throw 'Execution infrastructure ACL is incomplete.'}
}

function Assert-OwnerOnlyExecutionPath {
    param([Parameter(Mandatory)][string]$Path)
    if($Path -notmatch '^[A-Za-z]:\\' -or $Path.Substring(2).Contains(':')){throw 'Execution infrastructure requires a local absolute path.'}
    $full=[IO.Path]::GetFullPath($Path).TrimEnd('\')
    $drive=[IO.DriveInfo]::new([IO.Path]::GetPathRoot($full))
    if($drive.DriveType -ne 'Fixed' -or $drive.DriveFormat -ne 'NTFS'){throw 'Execution infrastructure requires fixed NTFS.'}
    for($current=Get-Item -LiteralPath $full -Force;$null -ne $current;){if($current.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Execution infrastructure reparse path refused.'};$current=if($current -is [IO.DirectoryInfo]){$current.Parent}else{$current.Directory}}
    return $full
}

function Assert-OwnerOnlyExecutionEnvironment {
    param([Parameter(Mandatory)][string]$ScriptRoot)
    $root=Assert-OwnerOnlyExecutionPath -Path $ScriptRoot
    $parent=Split-Path -Parent $root
    $prefix=Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)) 'PC Steward OwnerOnlyTest Execution '
    if([IO.Path]::GetFileName($root) -cne 'scripts' -or !$parent.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or $parent.Substring($prefix.Length) -cnotmatch '^[a-f0-9]{32}$'){throw 'Run only the reviewed protected execution bundle.'}
    foreach($directory in @($parent,$root)){Assert-OwnerOnlyExecutionAcl -Acl (Get-Acl -LiteralPath $directory) -Directory}
    $compiler=Join-Path $parent 'compiler'
    if([IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') -cne $compiler -or [IO.Path]::GetFullPath($env:TMP).TrimEnd('\') -cne $compiler){throw 'Privileged compilation requires the private compiler TEMP/TMP.'}
    $null=Assert-OwnerOnlyExecutionPath -Path $compiler
    Assert-OwnerOnlyExecutionAcl -Acl (Get-Acl -LiteralPath $compiler) -Directory -Compiler
    foreach($file in Get-ChildItem -LiteralPath $root -File -Force){$null=Assert-OwnerOnlyExecutionPath -Path $file.FullName;Assert-OwnerOnlyExecutionAcl -Acl (Get-Acl -LiteralPath $file.FullName)}
}

function Get-OwnerOnlyUserDataRoot {
    return (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'PCSteward')
}

function Get-OwnerOnlyOrdinalNames {
    param($Object)
    $names=[string[]]@($Object.PSObject.Properties|ForEach-Object{$_.Name})
    [Array]::Sort($names,[StringComparer]::Ordinal)
    return ($names -join ',')
}

function Assert-OwnerOnlyRedeployPlanV3 {
    param([Parameter(Mandatory)]$Plan)
    $fresh=$Plan.schema -ceq 'pc-steward-exact-fresh-install-v1'
    $keys=@('candidateManifestPath','candidateManifestSha256','candidateRoot','executionRoot','expectedNewOrigin','expectedOldOrigin','installerSid')+@(if(!$fresh){'oldLedgerSha256','oldThumbprint'})+@('preserveUserData','schema','scriptSource','scripts','userDataFiles','userDataRoot')
    if($Plan.schema -cne 'pc-steward-exact-redeploy-v3' -and !$fresh){throw 'Plan schema v3 is required.'}
    if($Plan.preserveUserData -isnot [bool] -or !$Plan.preserveUserData){throw 'Plan schema v3 requires preserveUserData true.'}
    if((Get-OwnerOnlyOrdinalNames -Object $Plan) -cne ($keys -join ',')){throw 'Plan schema v3 requires exactly the reviewed key set.'}
    if($Plan.expectedOldOrigin -isnot [string] -or $Plan.expectedOldOrigin -cnotin @(if($fresh){'none'}else{'legacy-v6','pinned-unsigned-v1','pinned-unsigned-v2'}) -or $Plan.expectedNewOrigin -isnot [string] -or $Plan.expectedNewOrigin -cnotin @('pinned-unsigned-v1','pinned-unsigned-v2')){throw 'Plan schema v3 must name an allowlisted expected old and new payload origin.'}
    if($Plan.installerSid -isnot [string] -or $Plan.installerSid -cnotmatch '^S-1-5-21-\d+-\d+-\d+-\d+$'){throw 'Plan schema v3 requires a user installer SID.'}
    foreach($name in @('candidateManifestPath','candidateRoot','executionRoot','scriptSource','userDataRoot')){if($Plan.$name -isnot [string] -or [string]::IsNullOrWhiteSpace($Plan.$name)){throw "Plan schema v3 requires the path $name."}}
    foreach($name in @('candidateManifestSha256')+@(if(!$fresh){'oldLedgerSha256'})){if($Plan.$name -isnot [string] -or $Plan.$name -cnotmatch '^[A-F0-9]{64}$'){throw "Plan schema v3 requires an uppercase SHA-256 $name."}}
    if(!$fresh -and ($Plan.oldThumbprint -isnot [string] -or $Plan.oldThumbprint -cnotmatch '^[A-F0-9]{40}$')){throw 'Plan schema v3 requires an uppercase old signer thumbprint.'}
    if($Plan.scripts -isnot [array] -or @($Plan.scripts).Count -ne 6){throw 'Plan schema v3 requires exactly six reviewed script pins.'}
    foreach($entry in @($Plan.scripts)){if($null -eq $entry -or (Get-OwnerOnlyOrdinalNames -Object $entry) -cne 'path,sha256' -or $entry.path -isnot [string] -or $entry.sha256 -isnot [string] -or $entry.sha256 -cnotmatch '^[A-F0-9]{64}$'){throw 'Malformed reviewed script entry.'}}
    if($Plan.userDataFiles -isnot [array] -or @($Plan.userDataFiles).Count -lt 1 -or @($Plan.userDataFiles).Count -gt 8){throw 'Plan schema v3 requires one to eight pinned user database files.'}
    $seen=@{};$pins=[Collections.Generic.List[object]]::new()
    foreach($entry in @($Plan.userDataFiles)){
        if($null -eq $entry -or (Get-OwnerOnlyOrdinalNames -Object $entry) -cne 'bytes,path,sha256' -or $entry.path -isnot [string] -or $entry.path -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$' -or $entry.path -match '-(wal|shm|journal)$' -or $entry.path.EndsWith('.') -or $seen.ContainsKey($entry.path.ToUpperInvariant()) -or ($entry.bytes -isnot [int] -and $entry.bytes -isnot [long]) -or $entry.bytes -lt 1 -or $entry.sha256 -isnot [string] -or $entry.sha256 -cnotmatch '^[A-F0-9]{64}$'){throw 'Malformed pinned user database entry.'}
        $seen[$entry.path.ToUpperInvariant()]=$true
        $pins.Add([pscustomobject]@{path=[string]$entry.path;bytes=[long]$entry.bytes;sha256=[string]$entry.sha256})
    }
    if(@($pins|Where-Object{$_.path -ceq 'pcsteward.db'}).Count -ne 1){throw 'Plan schema v3 must pin pcsteward.db.'}
    return $pins.ToArray()
}

function Get-OwnerOnlyStableFileIdentity {
    param([Parameter(Mandatory)][string]$Path)
    $stream=[IO.FileStream]::new($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read,65536,[IO.FileOptions]::SequentialScan)
    try{
        $length=$stream.Length
        $sha=[Security.Cryptography.SHA256]::Create()
        try{$hash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')}finally{$sha.Dispose()}
        if($stream.Length -ne $length){throw 'A pinned file changed while it was read.'}
        return [pscustomobject]@{bytes=[long]$length;sha256=$hash}
    }finally{$stream.Dispose()}
}

function Assert-OwnerOnlyNoReparseChain {
    param([Parameter(Mandatory)][string]$Path)
    for($current=Get-Item -LiteralPath $Path -Force;$null -ne $current;){
        if($current.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Reparse path refused for user data or a pinned file.'}
        $current=if($current -is [IO.DirectoryInfo]){$current.Parent}else{$current.Directory}
    }
}

function Get-OwnerOnlyUserDataSidecars {
    param([Parameter(Mandatory)][string]$DataRoot,[Parameter(Mandatory)][string[]]$Leaves)
    $present=[Collections.Generic.List[string]]::new()
    foreach($leaf in @(@($Leaves)+@('pcsteward.db')|Sort-Object -Unique)){foreach($suffix in @('-wal','-shm','-journal')){if(Test-Path -LiteralPath (Join-Path $DataRoot ($leaf+$suffix))){$present.Add($leaf+$suffix)}}}
    return $present.ToArray()
}

function Assert-OwnerOnlyNoPCStewardProcess {
    if(@(Get-Process -Name 'PCSteward*' -ErrorAction SilentlyContinue).Count){throw 'A PC Steward process is running; user data cannot be preserved.'}
}

function Assert-OwnerOnlyScheduledCheckInactive {
    Import-Module ([IO.Path]::Combine($PSHOME,'Modules','ScheduledTasks','ScheduledTasks.psd1')) -ErrorAction Stop
    $failures=$null;$tasks=@(Get-ScheduledTask -TaskName 'PCSteward.ScheduledCheck' -ErrorAction SilentlyContinue -ErrorVariable failures)
    if(@($failures|Where-Object{[string]$_.FullyQualifiedErrorId -notlike 'CmdletizationQuery_NotFound*'}).Count -or @($tasks|Where-Object{[string]$_.State -cne 'Disabled'}).Count){throw 'The PC Steward scheduled check task is enabled or unreadable; disable it before the redeploy.'}
}

function Assert-OwnerOnlyUserDataPreserved {
    param([Parameter(Mandatory)][string]$DataRoot,[Parameter(Mandatory)][object[]]$Files)
    if(!(Test-Path -LiteralPath $DataRoot -PathType Container)){throw 'The preserved user data root is absent.'}
    Assert-OwnerOnlyNoReparseChain -Path $DataRoot
    Assert-OwnerOnlyNoPCStewardProcess
    $leaves=@($Files|ForEach-Object{[string]$_.path})
    if(@(Get-OwnerOnlyUserDataSidecars -DataRoot $DataRoot -Leaves $leaves).Count){throw 'A SQLite sidecar exists beside the preserved user database.'}
    $observed=[Collections.Generic.List[object]]::new()
    foreach($file in $Files){
        $path=Join-Path $DataRoot $file.path
        if(!(Test-Path -LiteralPath $path -PathType Leaf)){throw 'A pinned user database file is absent.'}
        Assert-OwnerOnlyNoReparseChain -Path $path
        $identity=Get-OwnerOnlyStableFileIdentity -Path $path
        if($identity.bytes -ne [long]$file.bytes -or $identity.sha256 -cne $file.sha256){throw 'The preserved user database differs from its pin.'}
        $observed.Add([pscustomobject]@{path=[string]$file.path;bytes=$identity.bytes;sha256=$identity.sha256})
    }
    if(@(Get-OwnerOnlyUserDataSidecars -DataRoot $DataRoot -Leaves $leaves).Count){throw 'A SQLite sidecar exists beside the preserved user database.'}
    Assert-OwnerOnlyNoPCStewardProcess
    return $observed.ToArray()
}

function Assert-OwnerOnlyNoSqliteLoaded {
    foreach($module in @([Diagnostics.Process]::GetCurrentProcess().Modules)){if($module.ModuleName -match 'sqlite'){throw 'A SQLite engine is loaded in this process; start a fresh process that never loads SQLite.'}}
    foreach($assembly in @([AppDomain]::CurrentDomain.GetAssemblies())){if($assembly.GetName().Name -match 'sqlite'){throw 'A SQLite engine is loaded in this process; start a fresh process that never loads SQLite.'}}
}

function Get-OwnerOnlyPackageFullNameResult {
    $t=[Reflection.Emit.AssemblyBuilder]::DefineDynamicAssembly([Reflection.AssemblyName]::new('PCStewardOwnerOnlyPackage'),'Run').DefineDynamicModule('P').DefineType('P','Public,Sealed,Abstract')
    $t.DefinePInvokeMethod('GetCurrentPackageFullName','kernel32.dll','Public,Static,PinvokeImpl','Standard',[int],[Type[]]@([uint32].MakeByRefType(),[IntPtr]),'Winapi','Unicode').SetImplementationFlags('PreserveSig')
    return [int]$t.CreateType().GetMethod('GetCurrentPackageFullName').Invoke($null,[object[]]@([uint32]0,[IntPtr]::Zero))
}

function Assert-OwnerOnlyUnpackagedProcess {
    $code=Get-OwnerOnlyPackageFullNameResult
    if($code -ne 15700){throw "This process has a package identity (GetCurrentPackageFullName returned $code, not 15700); run from a Windows PowerShell console started directly, not from a packaged app."}
}

function Assert-OwnerOnlyFreshInstallBaseline {
    param([Parameter(Mandatory)][string]$InstallRoot,[Parameter(Mandatory)][string]$StateRoot)
    if((Test-Path -LiteralPath $InstallRoot) -or (Test-Path -LiteralPath $StateRoot)){throw 'A fresh install requires absent PC Steward install and state roots.'}
    foreach($name in @('My','Root','TrustedPublisher')){
        $store=[Security.Cryptography.X509Certificates.X509Store]::new($name,'CurrentUser')
        $store.Open('ReadOnly')
        try{if(@($store.Certificates|Where-Object{$_.Subject -like 'CN=PC Steward Owner-Only Test *'}).Count){throw "A PC Steward owner-only test certificate is in CurrentUser\$name; a fresh install requires none."}}finally{$store.Close()}
    }
    Assert-OwnerOnlyNoPCStewardProcess
}
