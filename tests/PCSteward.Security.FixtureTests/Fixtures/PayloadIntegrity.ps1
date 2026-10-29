param([Parameter(Mandatory)][string]$RepositoryRoot)
$ErrorActionPreference = 'Stop'
# The .NET-launched Windows PowerShell child can inherit another host's module paths.
# Bind hash/utility functions to this host instead of relying on module auto-loading.
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Utility\Microsoft.PowerShell.Utility.psd1') -Force
$helper = Join-Path $RepositoryRoot 'scripts\OwnerOnlyPayloadIntegrity.ps1'
if (Test-Path -LiteralPath $helper) { . $helper }
$waitHelper = Join-Path $RepositoryRoot 'scripts\Wait-OwnerOnlyDeploymentProcess.ps1'
if (Test-Path -LiteralPath $waitHelper) { . $waitHelper }
. (Join-Path $RepositoryRoot 'scripts\OwnerOnlyExecution.ps1')
if (!(Get-Command Read-OwnerOnlyCandidateManifest -ErrorAction SilentlyContinue)) { throw 'ASSERT: pinned payload integrity interface unavailable.' }
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('pc-steward-payload-fixture-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$checks = 0
function Check([bool]$Condition, [string]$Message) { if (!$Condition) { throw "ASSERT: $Message" }; $script:checks++ }
function Reject([scriptblock]$Action, [string]$Message) { $rejected = $false; try { & $Action | Out-Null } catch { $rejected = $true }; Check $rejected $Message }
function Make-Pe() {
    $bytes = [byte[]]::new(1024)
    $bytes[0] = 77; $bytes[1] = 90
    [BitConverter]::GetBytes([int]128).CopyTo($bytes,60)
    $bytes[128] = 80; $bytes[129] = 69
    [BitConverter]::GetBytes([uint16]0x8664).CopyTo($bytes,132)
    [BitConverter]::GetBytes([uint16]1).CopyTo($bytes,134)
    [BitConverter]::GetBytes([uint16]240).CopyTo($bytes,148)
    [BitConverter]::GetBytes([uint16]0x20b).CopyTo($bytes,152)
    [BitConverter]::GetBytes([uint32]512).CopyTo($bytes,188)
    [BitConverter]::GetBytes([uint32]512).CopyTo($bytes,212)
    [BitConverter]::GetBytes([uint32]16).CopyTo($bytes,260)
    [BitConverter]::GetBytes([uint32]512).CopyTo($bytes,408)
    [BitConverter]::GetBytes([uint32]512).CopyTo($bytes,412)
    for ($index=512; $index -lt 1024; $index++) { $bytes[$index] = [byte]($index % 251) }
    return ,$bytes
}
function With-Cms([byte[]]$Pe,[byte[]]$Blob) {
    $record=8+$Blob.Length; $aligned=($record+7)-band(-bnot 7)
    $result=[byte[]]::new($Pe.Length+$aligned);$Pe.CopyTo($result,0)
    [BitConverter]::GetBytes([uint32]$Pe.Length).CopyTo($result,296)
    [BitConverter]::GetBytes([uint32]$aligned).CopyTo($result,300)
    [BitConverter]::GetBytes([uint32]$record).CopyTo($result,$Pe.Length)
    [BitConverter]::GetBytes([uint16]0x200).CopyTo($result,$Pe.Length+4)
    [BitConverter]::GetBytes([uint16]2).CopyTo($result,$Pe.Length+6)
    $Blob.CopyTo($result,$Pe.Length+8)
    return ,$result
}
try {
    $source = Join-Path $fixture 'source'; New-Item -ItemType Directory -Path $source | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $source 'Resources') | Out-Null
    $unsigned = Make-Pe
    foreach ($name in @('PCSteward.Desktop.exe','PCSteward.ElevatedHelper.exe')) { [IO.File]::WriteAllBytes((Join-Path $source $name),$unsigned) }
    foreach ($name in @('PCSteward.ScheduledHost.dll','App.xbf','PCSteward.Desktop.pri','Resources\Main.xbf')) { [IO.File]::WriteAllText((Join-Path $source $name),'fixture') }
    $entries = @(Get-ChildItem -LiteralPath $source -File -Recurse | ForEach-Object { [ordered]@{path=$_.FullName.Substring($source.Length+1);bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash} })
    $manifestPath = Join-Path $fixture 'manifest.json'
    ConvertTo-Json -InputObject $entries -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    $manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
    $manifest = Read-OwnerOnlyCandidateManifest -Path $manifestPath -ExpectedSha256 $manifestHash
    Check ($manifest.Files.Count -eq 6) 'good pinned manifest'
    [PCSteward.Deployment.PayloadIntegrity]::AssertTree($source,$manifest.Files)
    $copy = Join-Path $fixture 'copy'; New-Item -ItemType Directory -Path $copy | Out-Null
    [PCSteward.Deployment.PayloadIntegrity]::CopyTree($source,$copy,$manifest.Files)
    [PCSteward.Deployment.PayloadIntegrity]::AssertTree($copy,$manifest.Files); $checks++
    Reject { Read-OwnerOnlyCandidateManifest -Path $manifestPath -ExpectedSha256 ('0' * 64) } 'wrong approved hash rejected'
    $locked = [IO.File]::Open((Join-Path $source 'App.xbf'),[IO.FileMode]::Open,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try { Reject { [PCSteward.Deployment.PayloadIntegrity]::AssertTree($source,$manifest.Files) } 'locked source rejected' } finally { $locked.Dispose() }
    [IO.File]::WriteAllText((Join-Path $source 'App.xbf'),'mutated')
    Reject { [PCSteward.Deployment.PayloadIntegrity]::AssertTree($source,$manifest.Files) } 'changed source rejected'
    [PCSteward.Deployment.PayloadIntegrity]::AssertTree($copy,$manifest.Files); $checks++
    [IO.File]::WriteAllText((Join-Path $source 'App.xbf'),'fixture')
    Set-Content -LiteralPath (Join-Path $source 'App.xbf') -Stream Zone.Identifier -Value 'fixture-zone'
    try { Reject { [PCSteward.Deployment.PayloadIntegrity]::AssertTree($source,$manifest.Files) } 'alternate data stream rejected' }
    finally { Remove-Item -LiteralPath (Join-Path $source 'App.xbf') -Stream Zone.Identifier }
    $extra = Join-Path $source 'hidden.txt'; [IO.File]::WriteAllText($extra,'extra'); [IO.File]::SetAttributes($extra,[IO.FileAttributes]::Hidden)
    Reject { [PCSteward.Deployment.PayloadIntegrity]::AssertTree($source,$manifest.Files) } 'hidden extra rejected'
    [IO.File]::SetAttributes($extra,[IO.FileAttributes]::Normal); [IO.File]::Delete($extra)
    $extraDirectory = Join-Path $source 'empty-extra'; [IO.Directory]::CreateDirectory($extraDirectory) | Out-Null
    Reject { [PCSteward.Deployment.PayloadIntegrity]::AssertTree($source,$manifest.Files) } 'empty extra directory rejected'
    [IO.Directory]::Delete($extraDirectory)
    foreach ($badPath in @('..\outside','C:\outside','\outside','name:stream','CON.txt','a.','a ','a\..\b','a//b','a\b?','a\b*','a\\b')) {
        $bad = [PCSteward.Deployment.PayloadFile]::new($badPath,1,('A'*64))
        Reject { [PCSteward.Deployment.PayloadIntegrity]::ValidateManifest([PCSteward.Deployment.PayloadFile[]]@($bad)) } "unsafe path $badPath"
    }
    $one = [PCSteward.Deployment.PayloadFile]::new('A.txt',1,('A'*64)); $two = [PCSteward.Deployment.PayloadFile]::new('a.txt',1,('A'*64))
    Reject { [PCSteward.Deployment.PayloadIntegrity]::ValidateManifest([PCSteward.Deployment.PayloadFile[]]@($one,$two)) } 'case collision'
    $two = [PCSteward.Deployment.PayloadFile]::new('A.txt\child',1,('A'*64))
    Reject { [PCSteward.Deployment.PayloadIntegrity]::ValidateManifest([PCSteward.Deployment.PayloadFile[]]@($one,$two)) } 'file directory collision'
    $badJsons = @(
        ('[{"path":"x","bytes":"1","sha256":"'+('A'*64)+'"}]'),
        ('[{"path":"x","bytes":-1,"sha256":"'+('A'*64)+'"}]'),
        ('[{"path":"x","bytes":1,"sha256":"'+('A'*64)+'","unknown":true}]'),
        '[]', '{}'
    )
    foreach ($json in $badJsons) {
        $badManifest = Join-Path $fixture 'bad.json'; [IO.File]::WriteAllText($badManifest,$json)
        $badHash = (Get-FileHash -LiteralPath $badManifest -Algorithm SHA256).Hash
        Reject { Read-OwnerOnlyCandidateManifest -Path $badManifest -ExpectedSha256 $badHash } 'bad manifest shape/values'
    }
    $validJson=Get-Content -LiteralPath $manifestPath -Raw
    $duplicateJson=[regex]::new('"path"\s*:\s*"App\.xbf"').Replace($validJson,'"path":"other", "path":"App.xbf"',1)
    Check ($duplicateJson -ne $validJson) 'duplicate-key fixture actually changes a complete valid manifest'
    $duplicatePath=Join-Path $fixture 'duplicate.json';[IO.File]::WriteAllText($duplicatePath,$duplicateJson)
    Reject { Read-OwnerOnlyCandidateManifest -Path $duplicatePath -ExpectedSha256 ((Get-FileHash -LiteralPath $duplicatePath -Algorithm SHA256).Hash) } 'duplicate JSON keys rejected'
    $outside = Join-Path $fixture 'outside'; [IO.Directory]::CreateDirectory($outside) | Out-Null
    $junction = Join-Path $source 'escape'
    New-Item -ItemType Junction -Path $junction -Target $outside | Out-Null
    try { Reject { [PCSteward.Deployment.PayloadIntegrity]::AssertTree($source,$manifest.Files) } 'junction traversal rejected' }
    finally { [IO.Directory]::Delete($junction) }
    $unsignedPath = Join-Path $copy 'PCSteward.Desktop.exe'
    $identity = [PCSteward.Deployment.PayloadIntegrity]::GetUnsignedImage($unsignedPath)
    Check ($identity.sha256 -eq (Get-FileHash -LiteralPath $unsignedPath -Algorithm SHA256).Hash) 'identity bound to raw pinned hash'
    $signed = [byte[]]::new(1048); $unsigned.CopyTo($signed,0)
    [BitConverter]::GetBytes([uint32]1024).CopyTo($signed,296)
    [BitConverter]::GetBytes([uint32]24).CopyTo($signed,300)
    [BitConverter]::GetBytes([uint32]123).CopyTo($signed,216)
    [BitConverter]::GetBytes([uint32]24).CopyTo($signed,1024)
    [BitConverter]::GetBytes([uint16]0x200).CopyTo($signed,1028)
    [BitConverter]::GetBytes([uint16]2).CopyTo($signed,1030)
    $signedPath = Join-Path $fixture 'signed.exe'; [IO.File]::WriteAllBytes($signedPath,$signed)
    [PCSteward.Deployment.PayloadIntegrity]::AssertSignedImage($signedPath,$identity); $checks++
    # This synthetic certificate proves structure/byte identity only. It is never trusted/signed/executed.
    foreach ($offset in @(2,200,511,512,750,1023)) {
        $changed = [byte[]]$signed.Clone(); $changed[$offset] = $changed[$offset] -bxor 1
        [IO.File]::WriteAllBytes($signedPath,$changed)
        Reject { [PCSteward.Deployment.PayloadIntegrity]::AssertSignedImage($signedPath,$identity) } "signed original-byte tamper at $offset"
    }
    $overlay = [byte[]]::new(1049); $signed.CopyTo($overlay,0); [IO.File]::WriteAllBytes($signedPath,$overlay)
    Reject { [PCSteward.Deployment.PayloadIntegrity]::AssertSignedImage($signedPath,$identity) } 'unsigned appended overlay after certificate'
    $badCert = [byte[]]$signed.Clone(); [BitConverter]::GetBytes([uint32]4096).CopyTo($badCert,1024); [IO.File]::WriteAllBytes($signedPath,$badCert)
    Reject { [PCSteward.Deployment.PayloadIntegrity]::AssertSignedImage($signedPath,$identity) } 'malformed certificate bounds'
    [IO.File]::WriteAllBytes($signedPath,$signed)
    Reject { [PCSteward.Deployment.PayloadIntegrity]::GetUnsignedImage($signedPath) } 'already signed candidate rejected'
    $unsignedOverlay = [byte[]]::new(1025); $unsigned.CopyTo($unsignedOverlay,0); [IO.File]::WriteAllBytes($signedPath,$unsignedOverlay)
    Reject { [PCSteward.Deployment.PayloadIntegrity]::GetUnsignedImage($signedPath) } 'unsigned input overlay rejected'
    Add-Type -ReferencedAssemblies 'System.dll','System.Core.dll','System.Security.dll' -TypeDefinition (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'SyntheticCms.cs.txt') -Raw)
    $cms = [FixtureCms]::Make('good')
    $cmsSigned = With-Cms $unsigned $cms.Blob
    [IO.File]::WriteAllBytes($signedPath,$cmsSigned)
    [PCSteward.Deployment.PayloadIntegrity]::AssertSignedImage($signedPath,$identity)
    [PCSteward.Deployment.PayloadIntegrity]::AssertSignerBlob($signedPath,$cms.Thumbprint); $checks++
    foreach($mode in @('sha1-signer','sha1-pe','extra-certificate','unsigned-attribute','signed-attribute','counter-signature','wrong-image-type')) {
        $badCms=[FixtureCms]::Make($mode)
        [IO.File]::WriteAllBytes($signedPath,(With-Cms $unsigned $badCms.Blob))
        Reject { [PCSteward.Deployment.PayloadIntegrity]::AssertSignerBlob($signedPath,$badCms.Thumbprint) } "strict CMS rejects $mode"
    }
    $trailing=[byte[]]::new($cms.Blob.Length+1);$cms.Blob.CopyTo($trailing,0);$trailing[$trailing.Length-1]=1
    [IO.File]::WriteAllBytes($signedPath,(With-Cms $unsigned $trailing))
    Reject { [PCSteward.Deployment.PayloadIntegrity]::AssertSignerBlob($signedPath,$cms.Thumbprint) } 'nonzero trailing PKCS7 data'
    $nonminimal=[byte[]]::new($cms.Blob.Length+1);$nonminimal[0]=0x30;$nonminimal[1]=0x83;$nonminimal[2]=0;[Array]::Copy($cms.Blob,2,$nonminimal,3,$cms.Blob.Length-2)
    [IO.File]::WriteAllBytes($signedPath,(With-Cms $unsigned $nonminimal))
    Reject { [PCSteward.Deployment.PayloadIntegrity]::AssertSignerBlob($signedPath,$cms.Thumbprint) } 'nonminimal DER length'
    $indefinite=[byte[]]$cms.Blob.Clone();$indefinite[1]=0x80
    [IO.File]::WriteAllBytes($signedPath,(With-Cms $unsigned $indefinite))
    Reject { [PCSteward.Deployment.PayloadIntegrity]::AssertSignerBlob($signedPath,$cms.Thumbprint) } 'BER indefinite length'
    [IO.File]::WriteAllBytes($signedPath,$cmsSigned)
    # Only the external Windows trust query is mocked. All parsing, hashing and crypto run for real.
    $mockTrustStatus='Valid';$mockSignatureType='Authenticode'
    function Get-AuthenticodeSignature { param([string]$LiteralPath) [pscustomobject]@{Status=$script:mockTrustStatus;SignatureType=$script:mockSignatureType;SignerCertificate=[pscustomobject]@{Thumbprint=$cms.Thumbprint}} }
    $heldResult=Test-OwnerOnlySignedExecutable -Path $signedPath -Identity $identity -ExpectedThumbprint $cms.Thumbprint
    Check ($heldResult.sha256 -eq (Get-FileHash -LiteralPath $signedPath -Algorithm SHA256).Hash) 'signed hash returned from validation lease'
    $mockSignatureType='Catalog'
    Reject { Test-OwnerOnlySignedExecutable -Path $signedPath -Identity $identity -ExpectedThumbprint $cms.Thumbprint } 'Valid catalog signature rejected'
    $mockSignatureType='Authenticode';$mockTrustStatus='HashMismatch'
    Reject { Test-OwnerOnlySignedExecutable -Path $signedPath -Identity $identity -ExpectedThumbprint $cms.Thumbprint } 'Windows trust failure rejected'
    $mockTrustStatus='Valid'
    $stateRoot = Join-Path $fixture 'state'; [IO.Directory]::CreateDirectory($stateRoot) | Out-Null
    [IO.File]::Copy($manifestPath,(Join-Path $stateRoot 'candidate-manifest.json'))
    $imageRecords = @(foreach($leaf in @('PCSteward.Desktop.exe','PCSteward.ElevatedHelper.exe')) {
        $item = [PCSteward.Deployment.PayloadIntegrity]::GetUnsignedImage((Join-Path $copy $leaf))
        [IO.File]::WriteAllBytes((Join-Path $copy $leaf),$cmsSigned)
        [pscustomobject]@{path=$leaf;bytes=$item.bytes;originalChecksum=[long]$item.originalChecksum}
    })
    $ledgerFiles=@(Get-ChildItem -LiteralPath $copy -File -Recurse|ForEach-Object{[pscustomobject]@{path=$_.FullName.Substring($copy.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}})
    $state = [pscustomobject]@{payloadOrigin='pinned-unsigned-v1';candidateManifestSha256=$manifestHash;executableImages=$imageRecords;certificateThumbprint=$cms.Thumbprint;files=$ledgerFiles}
    Check ((Assert-OwnerOnlyPayloadProvenance -InstallRoot $copy -StateRoot $stateRoot -State $state) -eq 'pinned-unsigned-v1') 'frozen candidate provenance'
    $imageRecords[0].originalChecksum=1
    Reject { Assert-OwnerOnlyPayloadProvenance -InstallRoot $copy -StateRoot $stateRoot -State $state } 'wrong original checksum'
    $imageRecords[0].originalChecksum=0;$imageRecords[0].bytes=1025
    Reject { Assert-OwnerOnlyPayloadProvenance -InstallRoot $copy -StateRoot $stateRoot -State $state } 'wrong original byte length'
    $imageRecords[0].bytes=1024
    $ledgerImage=@($ledgerFiles|Where-Object{$_.path -ceq 'PCSteward.Desktop.exe'})[0];$originalLedgerHash=$ledgerImage.sha256;$ledgerImage.sha256='0'*64
    Reject { Assert-OwnerOnlyPayloadProvenance -InstallRoot $copy -StateRoot $stateRoot -State $state } 'signed hash must match protected ledger'
    $ledgerImage.sha256=$originalLedgerHash
    $legacyRoot = Join-Path $fixture 'legacy'; [IO.Directory]::CreateDirectory($legacyRoot) | Out-Null
    Check ((Assert-OwnerOnlyPayloadProvenance -InstallRoot $copy -StateRoot $legacyRoot -State ([pscustomobject]@{})) -eq 'legacy-v6') 'explicit legacy behavior'
    Reject { Assert-OwnerOnlyPayloadProvenance -InstallRoot $copy -StateRoot $stateRoot -State ([pscustomobject]@{}) } 'stripped provenance with retained manifest'
    Reject { Assert-OwnerOnlyPayloadProvenance -InstallRoot $copy -StateRoot $legacyRoot -State ([pscustomobject]@{candidateManifestSha256=$manifestHash}) } 'partial provenance'
    $state.executableImages = @($imageRecords[0],$imageRecords[0])
    Reject { Assert-OwnerOnlyPayloadProvenance -InstallRoot $copy -StateRoot $stateRoot -State $state } 'duplicate executable identity'
    $state.executableImages = $imageRecords
    [IO.File]::WriteAllText((Join-Path $copy 'App.xbf'),'changed')
    Reject { Assert-OwnerOnlyPayloadProvenance -InstallRoot $copy -StateRoot $stateRoot -State $state } 'non-signed installed drift'
    [IO.File]::WriteAllText((Join-Path $copy 'App.xbf'),'fixture')
    $state.payloadOrigin = 'unknown'
    Reject { Assert-OwnerOnlyPayloadProvenance -InstallRoot $copy -StateRoot $stateRoot -State $state } 'unknown origin'
    $installerAst = [Management.Automation.Language.Parser]::ParseFile((Join-Path $RepositoryRoot 'scripts\Install-OwnerOnlyProtectedTestDeployment.ps1'),[ref]$null,[ref]$null)
    $commands = @($installerAst.FindAll({param($node) $node -is [Management.Automation.Language.CommandAst]},$true) | ForEach-Object { $_.GetCommandName() })
    Check (@($commands | Where-Object { $_ -eq 'dotnet' }).Count -eq 0) 'privileged deployment never recompiles'
    $stageChecks=@($installerAst.FindAll({param($node) $node -is [Management.Automation.Language.InvokeMemberExpressionAst] -and $node.Member.Value -eq 'AssertTree' -and $node.Arguments[0].Extent.Text -eq '$payloadStaging'},$true))
    $imageCheck=@($installerAst.FindAll({param($node) $node -is [Management.Automation.Language.InvokeMemberExpressionAst] -and $node.Member.Value -eq 'GetUnsignedImage'},$true))[0]
    $certificateCall=@($installerAst.FindAll({param($node) $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'New-OwnerOnlyCertificate'},$true))[0]
    Check ($stageChecks.Count -gt 0 -and $stageChecks[0].Extent.StartOffset -lt $certificateCall.Extent.StartOffset -and $imageCheck.Extent.StartOffset -lt $certificateCall.Extent.StartOffset) 'staged bytes/image validated before certificate mutation'
    $cleanup=@($installerAst.FindAll({param($node) $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'Remove-Item' -and $node.Extent.Text -match '\$staging'},$true))[0]
    $complete=@($installerAst.FindAll({param($node) $node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -eq '$completedState'},$true))[0]
    Check ($cleanup.Extent.StartOffset -lt $complete.Extent.StartOffset) 'staging removed before completed ledger'
    $freshOnly=$false
    try { & (Join-Path $RepositoryRoot 'scripts\Install-OwnerOnlyProtectedTestDeployment.ps1') -Install -CandidateRoot $source -CandidateManifestPath $manifestPath -CandidateManifestSha256 $manifestHash -CertificateThumbprint ('A'*40) }
    catch { $freshOnly=$_.Exception.Message -like '*fresh owner-only certificate*' }
    Check $freshOnly 'pinned route refuses reused certificate before elevation/effects'
    Reject { Assert-OwnerOnlyPayloadProfile -Origin 'legacy-v6' } 'default pinned profile requirement'
    Assert-OwnerOnlyPayloadProfile -Origin 'legacy-v6' -AllowLegacyPayload; $checks++
    Assert-OwnerOnlyPayloadProfile -Origin 'pinned-unsigned-v1'; $checks++
    $compilerAcl=New-OwnerOnlyExecutionSecurity -Directory -Compiler
    Assert-OwnerOnlyExecutionAcl -Acl $compilerAcl -Directory -Compiler;$checks++
    Check ($compilerAcl.GetOwner([Security.Principal.SecurityIdentifier]).Value -ceq 'S-1-5-32-544') 'compiler owner is Administrators'
    $ownerRights=@($compilerAcl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])|Where-Object{$_.IdentityReference.Value -ceq 'S-1-3-4'})
    Check ($ownerRights.Count -eq 1 -and !($ownerRights[0].FileSystemRights -band [Security.AccessControl.FileSystemRights]::ChangePermissions)) 'compiler owner cannot regain WRITE_DAC'
    $compilerAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.WindowsIdentity]::GetCurrent().User,[Security.AccessControl.FileSystemRights]::FullControl,[Security.AccessControl.AccessControlType]::Allow))
    Reject {Assert-OwnerOnlyExecutionAcl -Acl $compilerAcl -Directory -Compiler} 'same-user write grant rejected'
    $codeAcl=New-OwnerOnlyExecutionSecurity -Directory;Assert-OwnerOnlyExecutionAcl -Acl $codeAcl -Directory;$checks++
    Reject {Assert-OwnerOnlyExecutionEnvironment -ScriptRoot $fixture} 'writable non-ProgramFiles execution root rejected'
    $integrityAst=[Management.Automation.Language.Parser]::ParseFile($helper,[ref]$null,[ref]$null)
    $guard=@($integrityAst.FindAll({param($node)$node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'Assert-OwnerOnlyExecutionEnvironment'},$true))[0]
    $compile=@($integrityAst.FindAll({param($node)$node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'Add-Type'},$true))[0]
    Check ($guard.Extent.StartOffset -lt $compile.Extent.StartOffset) 'elevated compiler context checked before Add-Type'
    $bootstrapAst=[Management.Automation.Language.Parser]::ParseFile((Join-Path $RepositoryRoot 'scripts\Invoke-OwnerOnlyApprovedRedeployment.ps1'),[ref]$null,[ref]$null)
    $privateTemp=@($bootstrapAst.FindAll({param($node)$node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -eq '$env:TEMP'},$true))[0]
    $load=@($bootstrapAst.FindAll({param($node)$node -is [Management.Automation.Language.CommandAst] -and $node.InvocationOperator -eq 'Dot'},$true))[0]
    Check ($privateTemp.Extent.StartOffset -lt $load.Extent.StartOffset) 'private compiler TEMP established before loading protected code'
    Check ($null -ne (Get-Command Wait-OwnerOnlyDeploymentProcess -ErrorAction SilentlyContinue)) 'native exit-code capture interface unavailable'
    foreach ($expectedCode in @(0,7)) {
        $child = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -ArgumentList @('-NoProfile','-Command',"exit $expectedCode") -WindowStyle Hidden -PassThru
        try { $actualCode = Wait-OwnerOnlyDeploymentProcess -Process $child -TimeoutMilliseconds 10000; Check ($actualCode -eq $expectedCode) "native child exit $expectedCode" } finally { $child.Dispose() }
    }
    Write-Output "PASS: payload integrity fixtures ($checks assertions; real junction; no signing/store/installed-root mutation)"
}
finally {
    $absolute = [IO.Path]::GetFullPath($fixture)
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$absolute.StartsWith($temporaryRoot,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($absolute) -notlike 'pc-steward-payload-fixture-*') { throw 'Fixture cleanup root mismatch.' }
    Remove-Item -LiteralPath $absolute -Recurse -Force
}
