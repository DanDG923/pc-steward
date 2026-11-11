[CmdletBinding()]
param([switch]$StandardUser, [ValidateSet('Build', 'Check')][string]$Step, [string]$WorkRoot)

# ADR-034 §5 Windows CI evidence (FR-UPD-04/15, FR-DAT-12, TM-04/13): build the MSI exactly as a release would be built, but
# signed with a throwaway self-signed key whose pin is written into this job's own checkout only (never committed, never in a
# release build), then prove on the hosted runner:
#   1. the MSI refuses to install beside the ADR-022 owner-only state folder, and over a PC Steward folder no MSI owns;
#   2. it installs to %ProgramFiles%\PC Steward with the protected DACL (SYSTEM/Administrators full, Users read+execute) and
#      exactly the files the release manifest lists;
#   3. as a standard user, ADR-018 pre-launch trust passes, both executables carry the same compiled pin, Windows does not
#      trust the key, and the capability reader reports every allow-listed route Supported in both roles;
#   4. an upgrade that fails part-way rolls back to the installed version, a higher version upgrades in place, a lower
#      version is refused, and uninstall removes the program files and leaves per-user data alone;
#   5. the elevated release install step (Invoke-PCStewardReleaseInstall.ps1, security review H1) refuses a wrong artefact
#      digest, an MSI whose tables differ from the CI reference and an MSI whose files differ (uninstalling it), and installs
#      and verifies the real signed MSI.
# The key is created in the runner account's CurrentUser\My, never added to a trust store, and deleted at the end.
$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
$isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$repository = Split-Path -Parent $PSScriptRoot
$windowsPowerShell = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::System)) 'WindowsPowerShell\v1.0\powershell.exe'

if ($StandardUser) {
    if ($isAdministrator) { throw 'This step requires a standard-user token.' }
    if (!$WorkRoot -or !(Test-Path -LiteralPath $WorkRoot -PathType Container)) { throw 'A prepared WorkRoot is required.' }
    if ($Step -eq 'Check') {
        $check = Join-Path $WorkRoot 'installcheck\PCSteward.InstallCheck.exe'
        & $check
        exit $LASTEXITCODE
    }
    # Step Build: the candidate builder refuses an elevated token. Same per-user environment as Test-OwnerOnlyCandidateBuilderCI.ps1.
    Set-Location -LiteralPath $repository
    $env:TEMP = Join-Path $WorkRoot 'temp'; $env:TMP = $env:TEMP
    $env:DOTNET_CLI_HOME = Join-Path $WorkRoot 'dotnet-home'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'; $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:APPDATA = Join-Path $WorkRoot 'appdata'; $env:LOCALAPPDATA = Join-Path $WorkRoot 'localappdata'
    $userHome = Join-Path $WorkRoot 'home'
    New-Item -ItemType Directory -Path $env:TEMP, $env:DOTNET_CLI_HOME, $env:APPDATA, $env:LOCALAPPDATA, $userHome -Force | Out-Null
    $env:HOME = $userHome; $env:USERPROFILE = $userHome
    $env:HOMEDRIVE = Split-Path -Qualifier $userHome; $env:HOMEPATH = $userHome.Substring($env:HOMEDRIVE.Length)
    $env:GIT_CONFIG_COUNT = '1'; $env:GIT_CONFIG_KEY_0 = 'safe.directory'; $env:GIT_CONFIG_VALUE_0 = $repository.Replace('\', '/')
    # -AllowDirty: this job's checkout carries the throwaway pin, so the source is deliberately not the committed tree.
    & $windowsPowerShell @('-NoProfile', '-NonInteractive', '-File', (Join-Path $PSScriptRoot 'New-OwnerOnlyCandidate.ps1'), '-OutputRoot', (Join-Path $WorkRoot 'candidate'), '-AllowDirty')
    exit $LASTEXITCODE
}

if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or !$isAdministrator) {
    throw 'This job installs and removes software machine-wide; it runs only as an administrator on a disposable GitHub-hosted runner.'
}
if (!$env:RUNNER_TEMP) { throw 'RUNNER_TEMP is required.' }

$work = Join-Path $env:RUNNER_TEMP ('pcsteward-msi-' + [Guid]::NewGuid().ToString('N'))
$evidenceRoot = Join-Path $repository 'artifacts\msi-ci-evidence'
$logs = Join-Path $evidenceRoot 'logs'
New-Item -ItemType Directory -Path $work, $logs -Force | Out-Null
$programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$installRoot = Join-Path $programFiles 'PC Steward'
$stateRoot = Join-Path $programFiles 'PC Steward OwnerOnlyTest State'
$upgradeCode = '{2AD17330-BAD5-46BE-A409-20814B26673C}'
$pinsFile = Join-Path $repository 'src\PCSteward.Execution\ReleaseSigningPins.cs'
$evidence = [ordered]@{ schema = 'pc-steward-msi-ci-evidence:1'; checks = [ordered]@{} }
$failures = [Collections.Generic.List[string]]::new()
function Set-Check([string]$Name, [bool]$Passed, [string]$Detail = '') {
    $evidence.checks[$Name] = [ordered]@{ passed = $Passed; detail = $Detail }
    if (!$Passed) { $failures.Add("$Name $Detail") }
    Write-Output ("{0}: {1} {2}" -f $(if ($Passed) { 'PASS' } else { 'FAIL' }), $Name, $Detail)
}
function Get-FileSha256([string]$Path) {
    $sha = [Security.Cryptography.SHA256]::Create(); $stream = [IO.File]::OpenRead($Path)
    try { [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') } finally { $stream.Dispose(); $sha.Dispose() }
}
function Invoke-Msi([string[]]$Arguments, [string]$LogName) {
    $process = Start-Process -FilePath (Join-Path ([Environment]::SystemDirectory) 'msiexec.exe') -ArgumentList (@($Arguments) + @('/qn', '/norestart', '/l*v', ('"' + (Join-Path $logs $LogName) + '"'))) -Wait -PassThru
    $process.ExitCode
}
function Get-InstalledProducts {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    @($installer.GetType().InvokeMember('RelatedProducts', 'GetProperty', $null, $installer, @($upgradeCode)) | ForEach-Object {
        [pscustomobject]@{ ProductCode = [string]$_; Version = [string]$installer.GetType().InvokeMember('ProductInfo', 'GetProperty', $null, $installer, @([string]$_, 'VersionString')) }
    })
}
function Invoke-AsStandardUser([string]$StepName, [string]$Name, [Security.SecureString]$Password) {
    $stdout = Join-Path $logs "$StepName-stdout.log"; $stderr = Join-Path $logs "$StepName-stderr.log"
    $credential = [PSCredential]::new("$env:COMPUTERNAME\$Name", $Password)
    $arguments = '-NoProfile -File "' + $PSCommandPath + '" -StandardUser -Step ' + $StepName + ' -WorkRoot "' + $work + '"'
    $process = Start-Process -FilePath (Get-Command pwsh.exe).Source -ArgumentList $arguments -Credential $credential -LoadUserProfile -WorkingDirectory $repository -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    $null = $process.Handle
    try {
        if (!$process.WaitForExit(2400000)) { $process.Kill($true); $process.WaitForExit(); throw "$StepName exceeded forty minutes." }
        [pscustomobject]@{ ExitCode = $process.ExitCode; Output = ((Get-Content -LiteralPath $stdout -Raw) + (Get-Content -LiteralPath $stderr -Raw)) }
    }
    finally { $process.Dispose() }
}

$created = $null
$certificate = $null
$account = $null
$password = ConvertTo-SecureString ('Aa1!' + [Guid]::NewGuid().ToString('N')) -AsPlainText -Force
$pinsOriginal = [IO.File]::ReadAllBytes($pinsFile)
$marker = $null
$exitCode = 1
try {
    if ((Test-Path -LiteralPath $installRoot) -or (Test-Path -LiteralPath $stateRoot)) { throw 'The runner already has a PC Steward folder in Program Files.' }

    # Throwaway key and its pin, written into this checkout only.
    $created = New-SelfSignedCertificate -Type CodeSigningCert -Subject ('CN=PC Steward CI throwaway release key ' + [Guid]::NewGuid().ToString('N')) -CertStoreLocation 'Cert:\CurrentUser\My' -KeyExportPolicy NonExportable -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -NotAfter (Get-Date).AddDays(1)
    # Re-read from the store: a live certificate with its private key, whichever PowerShell edition created it.
    $certificate = Get-Item -LiteralPath ('Cert:\CurrentUser\My\' + $created.Thumbprint)
    if (!$certificate.HasPrivateKey) { throw 'The throwaway release key has no private key in CurrentUser\My.' }
    $pin = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($certificate.PublicKey.ExportSubjectPublicKeyInfo()))
    # The script-side pin function must agree with .NET's SubjectPublicKeyInfo export.
    . (Join-Path $PSScriptRoot 'PCStewardReleaseSigning.ps1')
    if ((Get-PCStewardKeyPin -CertificateDer $certificate.RawData) -cne $pin) { throw 'Get-PCStewardKeyPin disagrees with ExportSubjectPublicKeyInfo.' }
    $pinsText = [Text.Encoding]::UTF8.GetString($pinsOriginal)
    # The committed primary pin (the owner's YubiKey key since step 6) is replaced by the throwaway pin in this checkout only.
    $primaryLine = [regex]'internal const string Primary = "(?:[0-9A-F]{64})?";'
    if ($primaryLine.Matches($pinsText).Count -ne 1) { throw 'ReleaseSigningPins.cs does not hold exactly one primary pin to replace in this CI checkout.' }
    [IO.File]::WriteAllText($pinsFile, $primaryLine.Replace($pinsText, "internal const string Primary = `"$pin`";", 1), [Text.UTF8Encoding]::new($false))
    $evidence.throwawayPin = $pin

    # Disposable standard user for the builder and the install check.
    $name = 'pcsm' + [Guid]::NewGuid().ToString('N').Substring(0, 12)
    $account = New-LocalUser -Name $name -Password $password -Description 'Disposable PC Steward MSI CI user'
    Add-LocalGroupMember -SID 'S-1-5-32-545' -Member $account
    if ($env:NUGET_PACKAGES) { New-Item -ItemType Directory -Path $env:NUGET_PACKAGES -Force | Out-Null }
    foreach ($writable in @($repository, $work)) {
        & icacls.exe $writable /grant "*$($account.SID.Value):(OI)(CI)M" /T /Q | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Could not grant the CI user access to $writable." }
    }

    $build = Invoke-AsStandardUser 'Build' $name $password
    Write-Output $build.Output
    if ($build.ExitCode -ne 0) { throw "The candidate builder failed with exit code $($build.ExitCode)." }
    $candidatePayload = Join-Path $work 'candidate\payload'

    # Signed payloads that differ only in version, through the same signing function the release script uses.
    # 0.1.3 also carries one extra file, which the failed-upgrade check blocks with a folder of the same name.
    $msis = [ordered]@{}
    foreach ($version in @('0.1.1', '0.1.2', '0.1.3')) {
        $payload = Join-Path $work "payload-$version"
        Copy-Item -LiteralPath $candidatePayload -Destination $payload -Recurse
        if ($version -eq '0.1.3') { [IO.File]::WriteAllText((Join-Path $payload 'rollback-probe.txt'), 'blocked in CI to force a failed upgrade') }
        $signedRelease = Invoke-PCStewardReleaseSigning -PayloadRoot $payload -Certificate $certificate -Pin $pin -ProductVersion $version -MsiPath (Join-Path $work "PCSteward-$version.msi")
        $msis[$version] = [pscustomobject]@{ Path = $signedRelease.msiPath; Payload = $payload; ManifestSha256 = $signedRelease.manifestSha256; FileCount = $signedRelease.fileCount }
    }
    Set-Check 'msi-built-signed-and-checked' $true 'Invoke-PCStewardReleaseSigning signed both executables and every MSI with the pinned key, and New-PCStewardMsi.ps1 checked the tables'

    # The install check runs the production checks compiled with the throwaway pin.
    & dotnet build (Join-Path $repository 'tests\PCSteward.InstallCheck\PCSteward.InstallCheck.csproj') --configuration Release --output (Join-Path $work 'installcheck') -nologo
    if ($LASTEXITCODE -ne 0) { throw 'The install check did not build.' }

    # 1. Refusals.
    New-Item -ItemType Directory -Path $stateRoot | Out-Null
    $code = Invoke-Msi @('/i', ('"' + $msis['0.1.1'].Path + '"')) 'refuse-beside-owner-only.log'
    Set-Check 'refused-beside-owner-only-state' (($code -ne 0) -and !(Test-Path -LiteralPath $installRoot)) "exit $code"
    Remove-Item -LiteralPath $stateRoot -Force
    New-Item -ItemType Directory -Path $installRoot | Out-Null
    $code = Invoke-Msi @('/i', ('"' + $msis['0.1.1'].Path + '"')) 'refuse-over-unowned-folder.log'
    Set-Check 'refused-over-unowned-folder' (($code -ne 0) -and (@(Get-ChildItem -LiteralPath $installRoot -Force).Count -eq 0)) "exit $code"
    Remove-Item -LiteralPath $installRoot -Force

    # 2. Install, permissions and files.
    $code = Invoke-Msi @('/i', ('"' + $msis['0.1.1'].Path + '"')) 'install-0.1.1.log'
    Set-Check 'install' ($code -eq 0) "exit $code"
    $products = @(Get-InstalledProducts)
    Set-Check 'one-product-0.1.1' (($products.Count -eq 1) -and ($products[0].Version -eq '0.1.1')) (($products | ForEach-Object { $_.Version }) -join ',')
    $acl = Get-Acl -LiteralPath $installRoot
    $rules = @($acl.Access | ForEach-Object { '{0}|{1}|{2}|{3}' -f $_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value, [int]$_.FileSystemRights, $_.AccessControlType, $_.IsInherited })
    $expectedRules = @("S-1-5-18|$([int][Security.AccessControl.FileSystemRights]::FullControl)|Allow|False", "S-1-5-32-544|$([int][Security.AccessControl.FileSystemRights]::FullControl)|Allow|False", "S-1-5-32-545|$(0x1200a9)|Allow|False")
    Set-Check 'folder-dacl-protected-and-exact' ($acl.AreAccessRulesProtected -and (@(Compare-Object ($rules | Sort-Object) ($expectedRules | Sort-Object)).Count -eq 0)) ($rules -join '; ')
    $installedFiles = @(Get-ChildItem -LiteralPath $installRoot -Recurse -File -Force)
    $badFileAcl = @($installedFiles | Where-Object {
        $fileRules = @((Get-Acl -LiteralPath $_.FullName).Access | Where-Object { $_.AccessControlType -eq 'Allow' } | ForEach-Object { $_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value + '|' + [int]$_.FileSystemRights })
        @(Compare-Object ($fileRules | Sort-Object) (@('S-1-5-18|2032127', 'S-1-5-32-544|2032127', 'S-1-5-32-545|1179817') | Sort-Object)).Count -ne 0
    })
    Set-Check 'files-inherit-the-folder-dacl' ($badFileAcl.Count -eq 0) (($badFileAcl | Select-Object -First 5 | ForEach-Object { $_.FullName }) -join '; ')
    $manifestJson = Get-Content -LiteralPath (Join-Path $installRoot 'PCSteward.ReleaseManifest.json') -Raw | ConvertFrom-Json
    $listed = @{}; foreach ($file in $manifestJson.files) { $listed[$file.path] = $file }
    $mismatched = @($installedFiles | Where-Object {
        $relative = $_.FullName.Substring($installRoot.Length + 1)
        ($relative -ne 'PCSteward.ReleaseManifest.json') -and (!$listed.ContainsKey($relative) -or $listed[$relative].sha256 -ne (Get-FileSha256 $_.FullName) -or [long]$listed[$relative].bytes -ne $_.Length)
    })
    Set-Check 'installed-files-equal-the-manifest' (($mismatched.Count -eq 0) -and ($installedFiles.Count -eq $listed.Count + 1)) "installed $($installedFiles.Count), listed $($listed.Count)"
    Set-Check 'start-menu-shortcut' (Test-Path -LiteralPath (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonPrograms)) 'PC Steward.lnk'))

    # 3. Production checks as a standard user on the real install.
    & icacls.exe (Join-Path $work 'installcheck') /grant "*$($account.SID.Value):(OI)(CI)RX" /T /Q | Out-Null
    $check = Invoke-AsStandardUser 'Check' $name $password
    Write-Output $check.Output
    $evidence.installCheck = $check.Output.Trim()
    Set-Check 'standard-user-trust-and-capability' ($check.ExitCode -eq 0) "exit $($check.ExitCode)"

    # 4. Upgrade, downgrade refusal, uninstall.
    $marker = Join-Path $env:LOCALAPPDATA 'PC Steward\msi-ci-marker.txt'
    New-Item -ItemType Directory -Path (Split-Path -Parent $marker) -Force | Out-Null
    [IO.File]::WriteAllText($marker, 'per-user data must survive uninstall')
    # A folder where 0.1.3 must write a file makes that upgrade fail after the old version's removal has been scheduled.
    # MajorUpgrade runs that removal inside the install transaction, so the failure must leave 0.1.1 installed and intact.
    $probe = Join-Path $installRoot 'rollback-probe.txt'
    New-Item -ItemType Directory -Path $probe | Out-Null
    $code = Invoke-Msi @('/i', ('"' + $msis['0.1.3'].Path + '"')) 'failed-upgrade-0.1.3.log'
    $products = @(Get-InstalledProducts)
    $afterRollback = @(Get-ChildItem -LiteralPath $installRoot -Recurse -File -Force | Where-Object {
        $relative = $_.FullName.Substring($installRoot.Length + 1)
        ($relative -ne 'PCSteward.ReleaseManifest.json') -and (!$listed.ContainsKey($relative) -or $listed[$relative].sha256 -ne (Get-FileSha256 $_.FullName))
    })
    # The log must show the old version's removal was scheduled into the install script (RemoveExistingProducts returned
    # 1) and that the script then failed (InstallFinalize returned 3), so the rollback is what restored 0.1.1.
    $failedLog = @(Get-Content -LiteralPath (Join-Path $logs 'failed-upgrade-0.1.3.log') | Where-Object { $_ -match 'RemoveExistingProducts|Error \d{4}\.|Return value 3' })
    $removalScheduled = @($failedLog | Where-Object { $_ -match 'Action ended [0-9:]+: RemoveExistingProducts\. Return value 1\.' }).Count -gt 0
    $scriptFailed = @($failedLog | Where-Object { $_ -match 'Action ended [0-9:]+: InstallFinalize\. Return value 3\.' }).Count -gt 0
    Write-Output ($failedLog | Select-Object -First 25)
    Set-Check 'failed-upgrade-failed-after-removal-began' ($removalScheduled -and $scriptFailed) "removal scheduled $removalScheduled; install script failed $scriptFailed"
    Set-Check 'failed-upgrade-rolls-back' (($code -ne 0) -and ($products.Count -eq 1) -and ($products[0].Version -eq '0.1.1') -and ($afterRollback.Count -eq 0) -and ((Get-Content -LiteralPath (Join-Path $installRoot 'PCSteward.ReleaseManifest.json') -Raw | ConvertFrom-Json).productVersion -eq '0.1.1') -and (Test-Path -LiteralPath (Join-Path $installRoot 'PCSteward.Desktop.exe') -PathType Leaf)) "exit $code; $(($products | ForEach-Object { $_.Version }) -join ','); changed $($afterRollback.Count)"
    Remove-Item -LiteralPath $probe -Force
    $code = Invoke-Msi @('/i', ('"' + $msis['0.1.2'].Path + '"')) 'upgrade-0.1.2.log'
    $products = @(Get-InstalledProducts)
    Set-Check 'major-upgrade' (($code -eq 0) -and ($products.Count -eq 1) -and ($products[0].Version -eq '0.1.2') -and ($manifestJson = Get-Content -LiteralPath (Join-Path $installRoot 'PCSteward.ReleaseManifest.json') -Raw | ConvertFrom-Json).productVersion -eq '0.1.2') "exit $code; $(($products | ForEach-Object { $_.Version }) -join ',')"
    $code = Invoke-Msi @('/i', ('"' + $msis['0.1.1'].Path + '"')) 'downgrade-0.1.1.log'
    $products = @(Get-InstalledProducts)
    Set-Check 'downgrade-refused' (($code -ne 0) -and ($products.Count -eq 1) -and ($products[0].Version -eq '0.1.2')) "exit $code"
    $code = Invoke-Msi @('/x', $products[0].ProductCode) 'uninstall.log'
    Set-Check 'uninstall-removes-program-files' (($code -eq 0) -and !(Test-Path -LiteralPath $installRoot) -and (@(Get-InstalledProducts).Count -eq 0) -and !(Test-Path -LiteralPath (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonPrograms)) 'PC Steward.lnk'))) "exit $code"
    Set-Check 'uninstall-keeps-per-user-data' (Test-Path -LiteralPath $marker -PathType Leaf)

    # 5. The elevated release install step (ADR-034 security review H1), run exactly as the owner's paste runs it apart from
    #    the UAC prompt: a release artefact made from this job's CI payload, its MSI reference, the composed command, and the
    #    protected execution bundle. A wrong artefact digest, an MSI whose tables differ, and an MSI whose files differ are
    #    each refused with nothing left installed; the real signed MSI installs and verifies.
    . (Join-Path $PSScriptRoot 'PCStewardMsiTables.ps1')
    $release = Join-Path $work 'release-artefact'
    New-Item -ItemType Directory -Path $release | Out-Null
    Copy-Item -LiteralPath $candidatePayload -Destination (Join-Path $release 'payload') -Recurse
    Copy-Item -LiteralPath (Join-Path $work 'candidate\candidate-manifest.json') -Destination $release
    $backupPin = 'B' * 64
    $sourceCommit = ([string](& git -C $repository rev-parse HEAD)).Trim()
    $runId = 4242
    $releaseRecord = [ordered]@{
        schema = 'pc-steward-release-payload:1'; repository = 'DanDG923/pc-steward'; runId = [string]$runId; sourceCommit = $sourceCommit; productVersion = '0.1.1'
        manifestSha256 = (Get-FileSha256 (Join-Path $release 'candidate-manifest.json')); fileCount = 0; pins = [ordered]@{ primary = $pin; backup = $backupPin }
    }
    [IO.File]::WriteAllText((Join-Path $release 'release-payload.json'), ($releaseRecord | ConvertTo-Json -Depth 3), [Text.UTF8Encoding]::new($false))
    $reference = & (Join-Path $PSScriptRoot 'New-PCStewardMsiReference.ps1') -ReleasePayloadRoot $release | Select-Object -Last 1 | ConvertFrom-Json
    $signedTables = Get-PCStewardMsiTables -Path $msis['0.1.1'].Path
    $referenceLines = @((Get-Content -LiteralPath (Join-Path $release 'msi-reference.json') -Raw | ConvertFrom-Json).tables)
    $tableDifferences = @(Compare-Object $referenceLines @($signedTables.Lines) -CaseSensitive | ForEach-Object { $_.SideIndicator + ' ' + $_.InputObject })
    Write-Output ($tableDifferences | Select-Object -First 20)
    Set-Check 'signed-msi-tables-equal-the-unsigned-ci-reference' ($signedTables.Sha256 -ceq $reference.tablesSha256) "reference $($reference.tablesSha256); signed $($signedTables.Sha256); $($tableDifferences.Count) differing lines"
    Set-Check 'msi-tables-detect-a-different-package' ((Get-PCStewardMsiTables -Path $msis['0.1.3'].Path).Sha256 -cne $reference.tablesSha256)
    $artefactZip = Join-Path $work 'pc-steward-release-payload.zip'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($release, $artefactZip)
    $artefactSha256 = Get-FileSha256 $artefactZip
    # Like the owner's browser download, the zip carries Mark of the Web, which the elevated step refuses; every run below
    # therefore also proves the caller hands it a stream-free copy.
    Set-Content -LiteralPath $artefactZip -Stream 'Zone.Identifier' -Value "[ZoneTransfer]`r`nZoneId=3"
    Set-Check 'artefact-zip-carries-mark-of-the-web' (@(Get-Item -LiteralPath $artefactZip -Stream 'Zone.Identifier' -ErrorAction SilentlyContinue).Count -eq 1)
    $scriptPins = @{}
    foreach ($leaf in @('Invoke-PCStewardReleaseInstallBootstrap.ps1', 'Install-PCStewardRelease.ps1', 'OwnerOnlyExecution.ps1', 'OwnerOnlyPayloadIntegrity.ps1', 'PCStewardMsiTables.ps1', 'PCStewardReleaseSigning.ps1', 'Remove-OwnerOnlyProtectedTestDeployment.ps1')) { $scriptPins[$leaf] = Get-FileSha256 (Join-Path $PSScriptRoot $leaf) }
    function Invoke-ReleaseInstall([string]$Msi, [string]$Digest, [string]$Name) {
        $composed = & (Join-Path $PSScriptRoot 'Invoke-PCStewardReleaseInstall.ps1') -ArtefactZip $artefactZip -MsiPath $Msi -RunId $runId -SourceCommit $sourceCommit -ArtefactSha256 $Digest -PrimaryPin $pin -BackupPin $backupPin -ScriptPins $scriptPins -ComposeOnly | ConvertFrom-Json
        $process = Start-Process -FilePath $windowsPowerShell -ArgumentList @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $composed.encodedCommand) -WorkingDirectory ([Environment]::SystemDirectory) -WindowStyle Hidden -Wait -PassThru
        $resultPath = Join-Path $composed.executionRoot 'result.json'
        $outcome = if (Test-Path -LiteralPath $resultPath) { Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json } else { $null }
        foreach ($log in @(Get-ChildItem -LiteralPath (Join-Path $composed.executionRoot 'release') -Filter '*.log' -File -ErrorAction SilentlyContinue)) { Copy-Item -LiteralPath $log.FullName -Destination (Join-Path $logs "$Name-$($log.Name)") }
        if ($outcome) { [IO.File]::WriteAllText((Join-Path $logs "$Name-result.json"), ($outcome | ConvertTo-Json -Depth 5)) }
        Write-Output "$Name exit $($process.ExitCode): $($outcome | ConvertTo-Json -Depth 5 -Compress)"
        [pscustomobject]@{ ExitCode = $process.ExitCode; Result = $outcome }
    }
    function Test-NothingInstalled { (@(Get-InstalledProducts).Count -eq 0) -and !(Test-Path -LiteralPath $installRoot) }
    $run = @(Invoke-ReleaseInstall $msis['0.1.1'].Path ('A' * 64) 'release-wrong-digest')[-1]
    Set-Check 'release-install-refuses-a-wrong-artefact-digest' (($run.ExitCode -ne 0) -and ($run.Result.failure -like 'inputs:*') -and (Test-NothingInstalled)) "$($run.Result.failure)"
    $run = @(Invoke-ReleaseInstall $msis['0.1.3'].Path $artefactSha256 'release-other-tables')[-1]
    Set-Check 'release-install-refuses-msi-tables-unlike-ci' (($run.ExitCode -ne 0) -and ($run.Result.failure -like 'msi:*tables*') -and (Test-NothingInstalled)) "$($run.Result.failure)"
    # Same tables, one byte different in a versioned file the package does not hash: caught only after install, so the
    # product must be uninstalled again.
    $tamperedPayload = Join-Path $work 'payload-tampered'
    Copy-Item -LiteralPath $candidatePayload -Destination $tamperedPayload -Recurse
    $tamperedFile = Join-Path $tamperedPayload 'PCSteward.Execution.dll'
    $bytes = [IO.File]::ReadAllBytes($tamperedFile); $bytes[$bytes.Length - 1] = $bytes[$bytes.Length - 1] -bxor 1; [IO.File]::WriteAllBytes($tamperedFile, $bytes)
    $tampered = Invoke-PCStewardReleaseSigning -PayloadRoot $tamperedPayload -Certificate $certificate -Pin $pin -ProductVersion '0.1.1' -MsiPath (Join-Path $work 'PCSteward-0.1.1-tampered.msi')
    Set-Check 'tampered-msi-has-the-reference-tables' ((Get-PCStewardMsiTables -Path $tampered.msiPath).Sha256 -ceq $reference.tablesSha256)
    $run = @(Invoke-ReleaseInstall $tampered.msiPath $artefactSha256 'release-tampered-file')[-1]
    Set-Check 'release-install-uninstalls-files-unlike-ci' (($run.ExitCode -ne 0) -and ($run.Result.failure -like 'verify:*uninstalled*') -and (Test-NothingInstalled)) "$($run.Result.failure)"
    $run = @(Invoke-ReleaseInstall $msis['0.1.1'].Path $artefactSha256 'release-install')[-1]
    $products = @(Get-InstalledProducts)
    Set-Check 'release-install-installs-and-verifies-the-ci-build' (($run.ExitCode -eq 0) -and ($run.Result.validation -ceq 'Pass') -and ($run.Result.tablesSha256 -ceq $reference.tablesSha256) -and ($products.Count -eq 1) -and ($products[0].Version -eq '0.1.1')) "exit $($run.ExitCode); $($run.Result.phase) $($run.Result.failure)"
    foreach ($product in $products) { $null = Invoke-Msi @('/x', $product.ProductCode) 'release-uninstall.log' }
    Set-Check 'release-install-uninstalls-cleanly' (Test-NothingInstalled)

    $exitCode = if ($failures.Count -eq 0) { 0 } else { 1 }
}
catch {
    $failures.Add("error: $($_.Exception.Message)")
    Write-Output "ERROR: $($_.Exception.Message)"
    $exitCode = 1
}
finally {
    foreach ($product in @(Get-InstalledProducts)) { $null = Invoke-Msi @('/x', $product.ProductCode) 'cleanup-uninstall.log' }
    if ($created) { Remove-Item -LiteralPath ('Cert:\CurrentUser\My\' + $created.Thumbprint) -DeleteKey -ErrorAction SilentlyContinue }
    [IO.File]::WriteAllBytes($pinsFile, $pinsOriginal)
    if ($account) { Remove-LocalUser -SID $account.SID }
    if ($marker -and (Test-Path -LiteralPath $marker)) { Remove-Item -LiteralPath $marker -Force }
    $password.Dispose()
    $evidence.failures = @($failures)
    $evidence.result = if ($failures.Count -eq 0) { 'Pass' } else { 'Fail' }
    [IO.File]::WriteAllText((Join-Path $evidenceRoot 'msi-ci-evidence.json'), ($evidence | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
}
if ($exitCode -eq 0) { Write-Output 'PASS: signed MSI install, permissions, pinned trust, capability, failed-upgrade rollback, upgrade, downgrade refusal, uninstall, and the verified release install step.' }
exit $exitCode
