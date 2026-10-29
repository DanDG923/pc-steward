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
    # This ACE replaces implicit owner WRITE_DAC with read-control only on compiler files.
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
