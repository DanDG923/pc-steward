# Task 50 G4: read-only preservation copy of the PC Steward user database before an owner-approved redeploy.
# Windows PowerShell 5.1, normal non-elevated owner session. FR-UPD-15, TM-04/06, ADR-022 (per-user data is retained).
#
# It reads %LOCALAPPDATA%\PCSteward\pcsteward.db only as bytes through FileShare.Read handles (an open writer makes the
# read fail), refuses while any PCSteward* process runs or any -wal/-shm/-journal sidecar exists, copies the bytes to a
# NEW timestamped folder under the owner's profile (never Program Files, Windows or the data root), proves
# original == copy by SHA-256 and length, checks the SQLite file header of the copy, and writes one small JSON record.
#
# It never loads a SQLite engine. Task 49 showed that a waiting caller which had loaded the installed e_sqlite3.dll
# blocked removal, and the only other engine on the host is the unsigned candidate payload's native library. A full
# PRAGMA integrity_check is therefore NOT run here; integrity evidence for the redeploy is byte identity: the copy equals
# the live file, the live file equals the approved plan pin before and after the redeploy, and a later, separately
# approved app launch opens the preserved database. Any process that later runs an engine-based check on the copy must
# run separately, exit, and never be the redeploy caller.
#
# Run it with -File from a captured, hash-checked copy beside the reviewed OwnerOnlyExecution.ps1, for example
#   C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -File <folder>\Copy-OwnerOnlyUserDatabase.ps1
# It refuses when $PSScriptRoot is empty (pasted or dot-sourced text) and loads OwnerOnlyExecution.ps1 only when its bytes
# equal the reviewed SHA-256 embedded below (the same constants as Invoke-OwnerOnlyRedeployCaller.ps1).
[CmdletBinding()]
param([string]$DestinationParent)
$ErrorActionPreference='Stop'

function Get-OwnerOnlyCopyHelperScriptBlock {
    # Reviewed SHA-256 of OwnerOnlyExecution.ps1, LF then CRLF form; RedeployToolingScriptTests recomputes both.
    param([AllowEmptyString()][string]$ScriptRoot)
    if([string]::IsNullOrEmpty($ScriptRoot)){throw 'Run this copy with powershell.exe -File; the pinned helper is found only beside the script.'}
    $stream=[IO.FileStream]::new((Join-Path $ScriptRoot 'OwnerOnlyExecution.ps1'),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    try{
        if($stream.Length -gt 1048576){throw 'OwnerOnlyExecution.ps1 exceeds its size bound.'}
        $buffer=[IO.MemoryStream]::new();try{$stream.CopyTo($buffer);$bytes=$buffer.ToArray()}finally{$buffer.Dispose()}
    }finally{$stream.Dispose()}
    $sha=[Security.Cryptography.SHA256]::Create();try{$hash=[BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-','')}finally{$sha.Dispose()}
    if($hash -cnotin @('857F6DC8BCE109F412401B55A1EF966AEE037B249CFBF14CE19586D163EB2728','33B1B716E6298DCEAEF784DCFCB985FD8485DEFEDF6F886029D6BC8F8A615EF3')){throw 'OwnerOnlyExecution.ps1 is not the reviewed version embedded in this copy script; nothing was copied.'}
    $text=[Text.UTF8Encoding]::new($false,$true).GetString($bytes).TrimStart([char]0xfeff)
    $tokens=$null;$errors=$null
    $ast=[Management.Automation.Language.Parser]::ParseInput($text,[ref]$tokens,[ref]$errors)
    if(@($errors).Count -or $null -ne $ast.ParamBlock -or $null -ne $ast.BeginBlock -or $null -ne $ast.ProcessBlock -or $null -eq $ast.EndBlock -or @($ast.EndBlock.Statements|Where-Object{$_ -isnot [Management.Automation.Language.FunctionDefinitionAst]}).Count){throw 'A pinned helper must contain only function definitions.'}
    return [ScriptBlock]::Create($text)
}

function Assert-OwnerOnlyCopyDestinationPath {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Path)
    if($Path -cnotmatch '^[A-Za-z]:\\' -or $Path.Substring(2).Contains(':')){throw 'DestinationParent must be an absolute local path such as C:\...; relative, UNC and device paths are refused.'}
}

function Get-OwnerOnlySqliteHeaderFacts {
    # Pure byte parsing of the 100-byte SQLite header (https://www.sqlite.org/fileformat.html section 1.3). Not an engine.
    param([Parameter(Mandatory)][byte[]]$Header,[Parameter(Mandatory)][long]$Length)
    $magic=[Text.Encoding]::ASCII.GetBytes("SQLite format 3`0")
    $valid=$Header.Length -ge 100
    for($i=0;$valid -and $i -lt 16;$i++){if($Header[$i] -ne $magic[$i]){$valid=$false}}
    if(!$valid){return [ordered]@{headerValid=$false}}
    $pageSize=[int]$Header[16]*256+[int]$Header[17];if($pageSize -eq 1){$pageSize=65536}
    $pageCount=[long]$Header[28]*16777216+[long]$Header[29]*65536+[long]$Header[30]*256+[long]$Header[31]
    $changeCounter=[long]$Header[24]*16777216+[long]$Header[25]*65536+[long]$Header[26]*256+[long]$Header[27]
    $validFor=[long]$Header[92]*16777216+[long]$Header[93]*65536+[long]$Header[94]*256+[long]$Header[95]
    $pageSizeValid=$pageSize -ge 512 -and $pageSize -le 65536 -and ($pageSize -band ($pageSize-1)) -eq 0
    return [ordered]@{
        headerValid=$pageSizeValid -and ($Length % $pageSize) -eq 0
        pageSize=$pageSize;writeVersion=[int]$Header[18];readVersion=[int]$Header[19];walMode=([int]$Header[18] -eq 2 -and [int]$Header[19] -eq 2)
        inHeaderPageCountValid=($changeCounter -eq $validFor -and $pageCount -gt 0);pageCountMatchesLength=($pageSizeValid -and $pageCount * $pageSize -eq $Length)
    }
}

function Copy-OwnerOnlyUserDatabase {
    param([Parameter(Mandatory)][string]$DataRoot,[Parameter(Mandatory)][string]$DestinationParent,[Parameter(Mandatory)][AllowEmptyCollection()][string[]]$ForbiddenRoots)
    $root=[IO.Path]::GetFullPath($DataRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $parent=[IO.Path]::GetFullPath($DestinationParent).TrimEnd([IO.Path]::DirectorySeparatorChar)
    foreach($forbidden in @(@($ForbiddenRoots)+@($root))){
        if([string]::IsNullOrEmpty($forbidden)){continue}
        $full=[IO.Path]::GetFullPath($forbidden).TrimEnd([IO.Path]::DirectorySeparatorChar)
        if($parent.Equals($full,[StringComparison]::OrdinalIgnoreCase) -or $parent.StartsWith($full+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'The copy destination must be outside Program Files, Windows and the PC Steward data root.'}
    }
    $source=Join-Path $root 'pcsteward.db'
    if(!(Test-Path -LiteralPath $root -PathType Container) -or !(Test-Path -LiteralPath $source -PathType Leaf)){throw 'The PC Steward user database is absent.'}
    Assert-OwnerOnlyNoReparseChain -Path $source
    Assert-OwnerOnlyNoPCStewardProcess
    if(@(Get-OwnerOnlyUserDataSidecars -DataRoot $root -Leaves @('pcsteward.db')).Count){throw 'A SQLite sidecar exists; close PC Steward normally and let it finish before copying. Nothing was copied.'}
    Assert-OwnerOnlyNoSqliteLoaded
    # Refuse a reparse point on the nearest existing ancestor BEFORE creating anything, so no refusal leaves a folder behind.
    $existing=$parent
    while(!(Test-Path -LiteralPath $existing -PathType Container)){$up=Split-Path -Parent $existing;if([string]::IsNullOrEmpty($up) -or $up -eq $existing){throw 'The copy destination has no existing parent folder.'};$existing=$up}
    Assert-OwnerOnlyNoReparseChain -Path $existing
    [void][IO.Directory]::CreateDirectory($parent)
    Assert-OwnerOnlyNoReparseChain -Path $parent
    $leaf='pcsteward-db-copy-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
    $folder=Join-Path $parent $leaf
    if(Test-Path -LiteralPath $folder){throw 'The copy folder already exists.'}
    [void][IO.Directory]::CreateDirectory($folder)
    $copy=Join-Path $folder 'pcsteward.db'
    $reader=[IO.FileStream]::new($source,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read,65536,[IO.FileOptions]::SequentialScan)
    try{
        $output=[IO.FileStream]::new($copy,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
        try{
            $sha=[Security.Cryptography.SHA256]::Create()
            try{
                $buffer=[byte[]]::new(65536);$count=[long]0
                while(($read=$reader.Read($buffer,0,$buffer.Length)) -gt 0){[void]$sha.TransformBlock($buffer,0,$read,$null,0);$output.Write($buffer,0,$read);$count+=$read}
                [void]$sha.TransformFinalBlock([byte[]]::new(0),0,0)
                $streamed=[BitConverter]::ToString($sha.Hash).Replace('-','')
            }finally{$sha.Dispose()}
            $output.Flush($true)
            if($count -ne $reader.Length){throw 'The database changed while it was copied.'}
        }finally{$output.Dispose()}
    }finally{$reader.Dispose()}
    $original=Get-OwnerOnlyStableFileIdentity -Path $source
    $copied=Get-OwnerOnlyStableFileIdentity -Path $copy
    if($original.sha256 -cne $streamed -or $copied.sha256 -cne $streamed -or $original.bytes -ne $copied.bytes){throw 'The original and the copy differ; do not redeploy.'}
    # A writer that started during the copy leaves a sidecar or a process behind.
    if(@(Get-OwnerOnlyUserDataSidecars -DataRoot $root -Leaves @('pcsteward.db')).Count){throw 'A SQLite sidecar appeared during the copy; do not redeploy.'}
    Assert-OwnerOnlyNoPCStewardProcess
    $header=[byte[]]::new(100)
    $check=[IO.FileStream]::new($copy,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    try{$headerLength=$check.Read($header,0,100)}finally{$check.Dispose()}
    $facts=Get-OwnerOnlySqliteHeaderFacts -Header ($header[0..([Math]::Max(0,$headerLength-1))]) -Length $copied.bytes
    $record=[ordered]@{
        schema='pc-steward-user-database-copy-v1';createdUtc=[DateTime]::UtcNow.ToString('o');source='%LOCALAPPDATA%\PCSteward\pcsteward.db';copyFolder=$leaf
        bytes=$copied.bytes;sourceSha256=$original.sha256;copySha256=$copied.sha256;hashesEqual=$true;sidecarsPresent=$false;pcstewardProcesses=0;sqliteLoaded=$false
        sqliteHeader=$facts;integrityCheck='not-run-by-design';integrityEvidence='byte identity: copy equals live file; live file must equal the approved plan pin before and after the redeploy; a separately approved app launch opens it'
    }
    $bytes=[Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $record -Depth 4))
    $file=[IO.FileStream]::new((Join-Path $folder 'copy-record.json'),[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try{$file.Write($bytes,0,$bytes.Length);$file.Flush($true)}finally{$file.Dispose()}
    return $record
}

# ---- Main (skipped when a test dot-sources the functions through the AST). ----
if($PSVersionTable.PSEdition -cne 'Desktop' -or $PSVersionTable.PSVersion.Major -ne 5){throw 'Run this copy in Windows PowerShell 5.1.'}
if(![Environment]::Is64BitProcess -or !$PSHOME.TrimEnd('\').Equals('C:\Windows\System32\WindowsPowerShell\v1.0',[StringComparison]::OrdinalIgnoreCase)){throw 'Run this copy in 64-bit system Windows PowerShell (C:\Windows\System32\WindowsPowerShell\v1.0).'}
if(([Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Run this copy from a normal, non-elevated session.'}
if($DestinationParent){Assert-OwnerOnlyCopyDestinationPath -Path $DestinationParent}
. (Get-OwnerOnlyCopyHelperScriptBlock -ScriptRoot $PSScriptRoot)
if(!$DestinationParent){$DestinationParent=Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) 'PCSteward-DatabaseCopies'}
$forbidden=@([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles),[Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86),[Environment]::GetFolderPath([Environment+SpecialFolder]::Windows))
ConvertTo-Json -InputObject (Copy-OwnerOnlyUserDatabase -DataRoot (Get-OwnerOnlyUserDataRoot) -DestinationParent $DestinationParent -ForbiddenRoots $forbidden) -Depth 4
