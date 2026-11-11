[CmdletBinding()]
param([switch]$StandardUser, [ValidateSet('Build', 'Check', 'SelfUpdateDecide')][string]$Step, [string]$WorkRoot)

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
#      and verifies the real signed MSI;
#   6. the self-update finish copy (ADR-032 revision 2 §7–§8), run as the real helper copy from a protected work folder that
#      the production Windows host laid out: it refuses to run from anywhere but that folder's bin or while the account's own
#      registry registers the product (security review R1), uninstalls an upgrade
#      whose files differ from the expected tree, installs and verifies one that matches, and the reopened desktop's read of
#      each job (as a standard user) reports Failed and Updated;
#   7. (evidence only, Mythos review S1) whether an elevated install of the per-machine package applies a transform that a
#      per-user registration of the new or the installed PC Steward product names.
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
    if ($Step -eq 'SelfUpdateDecide') {
        # ADR-032 revision 2 §8: the reopened desktop's read of the job the elevated part of this script prepared.
        $leaf = ([IO.File]::ReadAllText((Join-Path $WorkRoot 'self-update-leaf.txt'))).Trim()
        & (Join-Path $WorkRoot 'installcheck\PCSteward.InstallCheck.exe') --self-update-decide $leaf '0.1.1'
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
$selfUpdateLeaves = [Collections.Generic.List[string]]::new()
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
    # ADR-032 revision 2 §3: the release check record the release-payload workflow publishes, made from the same artefact
    # layout. Both PowerShell editions must write the same bytes, and the record must carry the run, the pins, the tables
    # digest, every candidate file and the two executables' PE checksums.
    $checkOutputs = @{}
    foreach ($edition in @(@{ Name = 'pwsh'; Path = (Get-Process -Id $PID).Path }, @{ Name = 'powershell'; Path = $windowsPowerShell })) {
        $checkOutput = Join-Path $work ('release-check-' + $edition.Name)
        $null = & $edition.Path -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'New-PCStewardReleaseCheck.ps1') -ReleasePayloadRoot $release -OutputRoot $checkOutput
        if ($LASTEXITCODE -ne 0) { throw "New-PCStewardReleaseCheck.ps1 failed under $($edition.Name)." }
        $checkOutputs[$edition.Name] = [IO.File]::ReadAllBytes((Join-Path $checkOutput 'release-check.json'))
    }
    Set-Check 'release-check-same-bytes-in-both-editions' ([Convert]::ToBase64String($checkOutputs['pwsh']) -ceq [Convert]::ToBase64String($checkOutputs['powershell']))
    $releaseCheck = [Text.Encoding]::ASCII.GetString($checkOutputs['pwsh']) | ConvertFrom-Json
    $candidateCount = @(Get-Content -LiteralPath (Join-Path $release 'candidate-manifest.json') -Raw | ConvertFrom-Json).Count
    Set-Check 'release-check-carries-the-ci-build' ($releaseCheck.schema -ceq 'pc-steward-release-check:1' -and [string]$releaseCheck.runId -ceq [string]$runId -and $releaseCheck.sourceCommit -ceq $sourceCommit -and $releaseCheck.msiVersion -ceq '0.1.1' -and $releaseCheck.pins.primary -ceq $pin -and $releaseCheck.pins.backup -ceq $backupPin -and $releaseCheck.msiTablesSha256 -ceq $reference.tablesSha256 -and @($releaseCheck.files).Count -eq $candidateCount -and @($releaseCheck.executables).Count -eq 2) "files $(@($releaseCheck.files).Count) of $candidateCount"
    # ADR-032 revision 2 §5–§6: the C# checks the desktop and the elevated helper run agree with the PowerShell ones above on
    # this job's real signed MSI and signed executables (MsiTableDigest, ReleaseCheckPolicy, ReleaseImageComparison).
    $parity = & (Join-Path $work 'installcheck\PCSteward.InstallCheck.exe') --release-check-parity $msis['0.1.1'].Path (Join-Path $work 'release-check-pwsh\release-check.json') $msis['0.1.1'].Payload
    Set-Check 'release-check-csharp-matches-powershell' ($LASTEXITCODE -eq 0) "$parity"
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

    # 6. The self-update finish copy (ADR-032 revision 2 §7–§8). The install check lays each job out through the production
    #    Windows host as the elevated session leaves it (work folder, MSI copy, image, expected tree, job, helper copy); the
    #    finish copy is then the real installed helper, copied into the work folder's bin and started there, with the .NET
    #    runtime names removed from its environment as the desktop's guard ensures for the session that starts it.
    $installCheck = Join-Path $work 'installcheck\PCSteward.InstallCheck.exe'
    function New-SelfUpdateJob([string]$Mode) {
        $products = @(Get-InstalledProducts); $code = 0
        if ($products.Count -eq 0) {
            $code = Invoke-Msi @('/i', ('"' + $msis['0.1.1'].Path + '"')) "self-update-$Mode-from-0.1.1.log"
            $products = @(Get-InstalledProducts)
        }
        if (($code -ne 0) -or ($products.Count -ne 1) -or ($products[0].Version -ne '0.1.1')) { throw "0.1.1 is not the one install before the $Mode self-update job (exit $code)." }
        $prepared = @(& $installCheck --self-update-prepare $msis['0.1.2'].Path $Mode)
        if ($LASTEXITCODE -ne 0) { throw "The $Mode self-update job was not prepared: $($prepared -join ' ')" }
        $leaf = ([string]$prepared[-1]).Trim()
        if ($leaf -notmatch '^PC Steward Update [0-9a-f]{32}$') { throw "Unexpected work folder name '$leaf'." }
        $selfUpdateLeaves.Add($leaf)
        $leaf
    }
    function Invoke-FinishCopy([string]$Helper, [string]$Leaf, [string]$Name) {
        $start = [Diagnostics.ProcessStartInfo]::new($Helper)
        $start.UseShellExecute = $false; $start.CreateNoWindow = $true; $start.WorkingDirectory = [Environment]::SystemDirectory
        $start.ArgumentList.Add('--finish-self-update'); $start.ArgumentList.Add($Leaf)
        foreach ($key in @($start.Environment.Keys)) { if ($key -match '^(DOTNET_|COMPlus_|CORECLR_|COR_|APPDOMAIN_MANAGER_)') { $null = $start.Environment.Remove($key) } }
        $process = [Diagnostics.Process]::Start($start)
        try {
            if (!$process.WaitForExit(2700000)) { $process.Kill($true); $process.WaitForExit(); throw "$Name exceeded forty-five minutes." }
            $folder = Join-Path $programFiles $Leaf
            foreach ($log in @('install.log', 'uninstall.log')) { if (Test-Path -LiteralPath (Join-Path $folder "private\$log")) { Copy-Item -LiteralPath (Join-Path $folder "private\$log") -Destination (Join-Path $logs "$Name-$log") } }
            $resultPath = Join-Path $folder 'result.json'
            $outcome = if (Test-Path -LiteralPath $resultPath) { Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json } else { $null }
            Write-Output "$Name exit $($process.ExitCode): $($outcome | ConvertTo-Json -Compress)"
            [pscustomobject]@{ ExitCode = $process.ExitCode; Result = $outcome }
        }
        finally { $process.Dispose() }
    }
    # $Label, not $Name: PowerShell names are case-insensitive and $name is the disposable account.
    function Invoke-SelfUpdateDecide([string]$Leaf, [string]$Label) {
        [IO.File]::WriteAllText((Join-Path $work 'self-update-leaf.txt'), $Leaf)
        $decided = Invoke-AsStandardUser 'SelfUpdateDecide' $name $password
        Write-Output "$Label exit $($decided.ExitCode): $($decided.Output.Trim())"
        $line = @($decided.Output -split "`r?`n" | Where-Object { $_ -match '^\{' })[-1]
        [pscustomobject]@{ ExitCode = $decided.ExitCode; Verdict = $(if ($line) { $line | ConvertFrom-Json } else { $null }) }
    }
    $selfUpdateHelper = { param([string]$Leaf) Join-Path $programFiles "$Leaf\bin\PCSteward.ElevatedHelper.exe" }

    # A job whose expected tree differs from what the MSI installs: the finish copy installs 0.1.2, finds the difference,
    # and uninstalls it, so nothing is left installed.
    $leaf = New-SelfUpdateJob 'mismatch'
    $finish = @(Invoke-FinishCopy (& $selfUpdateHelper $leaf) $leaf 'self-update-mismatch')[-1]
    Set-Check 'self-update-finish-uninstalls-an-install-unlike-the-expected-tree' (($finish.ExitCode -eq 0) -and ($finish.Result.outcome -ceq 'mismatch-uninstalled') -and ($finish.Result.detail -ceq 'installed-tree-differs') -and ($finish.Result.exitCode -eq 0) -and (Test-NothingInstalled)) "exit $($finish.ExitCode); $($finish.Result.outcome) $($finish.Result.detail)"
    $decide = @(Invoke-SelfUpdateDecide $leaf 'self-update-mismatch-decide')[-1]
    Set-Check 'self-update-desktop-reads-the-uninstalled-job-as-failed' (($decide.ExitCode -eq 0) -and ($decide.Verdict.state -ceq 'Failed') -and ($decide.Verdict.code -ceq 'mismatch-uninstalled')) "exit $($decide.ExitCode); $($decide.Verdict.state) $($decide.Verdict.code)"

    # A matching job. The installed helper itself, outside the work folder, is refused before it reads or writes anything;
    # the copy in bin installs 0.1.2, verifies it, and the reopened desktop (now 0.1.2) reads the job as Updated.
    $leaf = New-SelfUpdateJob 'match'
    $refused = @(Invoke-FinishCopy (Join-Path $installRoot 'PCSteward.ElevatedHelper.exe') $leaf 'self-update-wrong-path')[-1]
    $products = @(Get-InstalledProducts)
    Set-Check 'self-update-finish-refuses-outside-the-work-folder' (($refused.ExitCode -eq 20) -and ($null -eq $refused.Result) -and ($products.Count -eq 1) -and ($products[0].Version -eq '0.1.1')) "exit $($refused.ExitCode)"
    # Security review R1: what a same-user program can write, the owner's own Windows Installer registration of the product
    # (HKCU), stops the finish copy before msiexec runs; 0.1.1 stays installed. The job then has a result, so a new one follows.
    $productCode = [string](Get-Content -LiteralPath (Join-Path $programFiles "$leaf\job.json") -Raw | ConvertFrom-Json).productCode
    if ($productCode -cnotmatch '\A\{([0-9A-F]{8})-([0-9A-F]{4})-([0-9A-F]{4})-([0-9A-F]{4})-([0-9A-F]{12})\}\z') { throw "The job names no product code: $productCode" }
    $tail = $Matches[4] + $Matches[5]
    $packed = (-join @($Matches[1], $Matches[2], $Matches[3] | ForEach-Object { -join $_[($_.Length - 1)..0] })) + (-join @(0..7 | ForEach-Object { $tail[2 * $_ + 1]; $tail[2 * $_] }))
    $perUserKey = "HKCU:\Software\Microsoft\Installer\Products\$packed"
    $null = New-Item -Path $perUserKey -Force
    New-ItemProperty -LiteralPath $perUserKey -Name 'ProductName' -Value 'PC Steward' -PropertyType String -Force | Out-Null
    try { $perUser = @(Invoke-FinishCopy (& $selfUpdateHelper $leaf) $leaf 'self-update-per-user')[-1] }
    finally { Remove-Item -LiteralPath $perUserKey -Recurse -Force }
    $products = @(Get-InstalledProducts)
    Set-Check 'self-update-finish-refuses-a-per-user-registration' (($perUser.ExitCode -eq 0) -and ($perUser.Result.outcome -ceq 'refused-before-install') -and ($perUser.Result.detail -ceq 'per-user-registration') -and ($null -eq $perUser.Result.exitCode) -and ($products.Count -eq 1) -and ($products[0].Version -eq '0.1.1')) "exit $($perUser.ExitCode); $($perUser.Result.outcome) $($perUser.Result.detail)"
    $leaf = New-SelfUpdateJob 'match'
    $finish = @(Invoke-FinishCopy (& $selfUpdateHelper $leaf) $leaf 'self-update-match')[-1]
    $products = @(Get-InstalledProducts)
    $installedVersion = if (Test-Path -LiteralPath (Join-Path $installRoot 'PCSteward.ReleaseManifest.json')) { (Get-Content -LiteralPath (Join-Path $installRoot 'PCSteward.ReleaseManifest.json') -Raw | ConvertFrom-Json).productVersion } else { '' }
    Set-Check 'self-update-finish-installs-and-verifies-the-upgrade' (($finish.ExitCode -eq 0) -and ($finish.Result.outcome -ceq 'installed') -and ($finish.Result.exitCode -eq 0) -and ($products.Count -eq 1) -and ($products[0].Version -eq '0.1.2') -and ($installedVersion -eq '0.1.2')) "exit $($finish.ExitCode); $($finish.Result.outcome) $($finish.Result.detail); $(($products | ForEach-Object { $_.Version }) -join ',')"
    $decide = @(Invoke-SelfUpdateDecide $leaf 'self-update-match-decide')[-1]
    Set-Check 'self-update-private-folder-hidden-from-standard-users' (($decide.ExitCode -eq 0) -and ($decide.Verdict.privateHidden -ceq 'true')) "privateHidden $($decide.Verdict.privateHidden)"
    Set-Check 'self-update-desktop-reads-the-installed-job-as-updated' (($decide.ExitCode -eq 0) -and ($decide.Verdict.state -ceq 'Updated') -and ($decide.Verdict.code -ceq 'installed') -and ($decide.Verdict.running -ceq '0.1.2')) "exit $($decide.ExitCode); $($decide.Verdict.state) $($decide.Verdict.code) running $($decide.Verdict.running)"
    foreach ($product in @(Get-InstalledProducts)) { $null = Invoke-Msi @('/x', $product.ProductCode) 'self-update-uninstall.log' }
    Set-Check 'self-update-uninstalls-cleanly' (Test-NothingInstalled)

    # 7. Mythos review S1 experiment. Evidence only: its outcome never fails the job (a broken experiment is recorded, not
    #    hidden). Does an elevated `msiexec /i` of the per-machine package apply a transform that the owner's own (per-user)
    #    registration of a PC Steward product names? Any same-user program can write that registration; `msiexec /ju` writes
    #    the same HKCU keys here. The transform only adds a property, so the verbose logs show whether it was applied. Case
    #    'new-product' registers the 0.1.2 product the update installs; case 'installed-product' the installed 0.1.1 product
    #    that the upgrade removes. The helper itself never runs msiexec while such a registration exists; this measures how
    #    much a registration written in the moment between that check and msiexec reading it could do.
    $experiment = [ordered]@{}
    $evidence.perUserTransformExperiment = $experiment
    $loggingPolicy = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Installer'
    function Invoke-InstallerCom($Target, [string]$Member, [string]$Kind, [object[]]$Arguments) { $Target.GetType().InvokeMember($Member, $Kind, $null, $Target, $Arguments) }
    function Get-MsiPropertyValue([string]$Msi, [string]$Property) {
        $installer = New-Object -ComObject WindowsInstaller.Installer
        $database = Invoke-InstallerCom $installer 'OpenDatabase' 'InvokeMethod' @($Msi, 0)
        $view = Invoke-InstallerCom $database 'OpenView' 'InvokeMethod' @("SELECT ``Value`` FROM ``Property`` WHERE ``Property``='$Property'")
        try {
            $null = Invoke-InstallerCom $view 'Execute' 'InvokeMethod' $null
            $row = Invoke-InstallerCom $view 'Fetch' 'InvokeMethod' $null
            if ($null -eq $row) { throw "No $Property in $Msi." }
            [string](Invoke-InstallerCom $row 'StringData' 'GetProperty' @(1))
        }
        finally { $null = Invoke-InstallerCom $view 'Close' 'InvokeMethod' $null; $null = [Runtime.InteropServices.Marshal]::FinalReleaseComObject($view); $null = [Runtime.InteropServices.Marshal]::FinalReleaseComObject($database) }
    }
    # A transform of $Msi that adds one public property, PCSTEWARDPERUSERMARKER=<case>, made from a scratch copy through msi.dll
    # (the COM automation layer refuses PowerShell-wrapped database objects as arguments).
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class PCStewardMarkerTransform
{
    [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern uint MsiOpenDatabaseW(string path, IntPtr persist, out IntPtr database);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern uint MsiDatabaseOpenViewW(IntPtr database, string query, out IntPtr view);
    [DllImport("msi.dll")] private static extern uint MsiViewExecute(IntPtr view, IntPtr record);
    [DllImport("msi.dll")] private static extern uint MsiViewClose(IntPtr view);
    [DllImport("msi.dll")] private static extern uint MsiDatabaseCommit(IntPtr database);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern uint MsiDatabaseGenerateTransformW(IntPtr database, IntPtr reference, string transform, int reserved1, int reserved2);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern uint MsiCreateTransformSummaryInfoW(IntPtr database, IntPtr reference, string transform, int errorConditions, int validation);
    [DllImport("msi.dll")] private static extern uint MsiCloseHandle(IntPtr handle);
    private static void Check(uint status, string what) { if (status != 0) throw new InvalidOperationException(what + " failed with " + status + "."); }
    public static void Create(string original, string scratch, string transform, string marker)
    {
        IntPtr changed, reference, view;
        Check(MsiOpenDatabaseW(scratch, new IntPtr(1), out changed), "MsiOpenDatabase (transact)");
        try
        {
            Check(MsiOpenDatabaseW(original, IntPtr.Zero, out reference), "MsiOpenDatabase (read-only)");
            try
            {
                Check(MsiDatabaseOpenViewW(changed, "INSERT INTO `Property` (`Property`, `Value`) VALUES ('PCSTEWARDPERUSERMARKER', '" + marker + "')", out view), "MsiDatabaseOpenView");
                try { Check(MsiViewExecute(view, IntPtr.Zero), "MsiViewExecute"); }
                finally { MsiViewClose(view); MsiCloseHandle(view); }
                Check(MsiDatabaseCommit(changed), "MsiDatabaseCommit");
                Check(MsiDatabaseGenerateTransformW(changed, reference, transform, 0, 0), "MsiDatabaseGenerateTransform");
                Check(MsiCreateTransformSummaryInfoW(changed, reference, transform, 0, 0), "MsiCreateTransformSummaryInfo");
            }
            finally { MsiCloseHandle(reference); }
        }
        finally { MsiCloseHandle(changed); }
    }
}
'@
    function New-MarkerTransform([string]$Msi, [string]$Case) {
        $scratch = Join-Path $work "marker-$Case.msi"; $transform = Join-Path $work "marker-$Case.mst"
        Copy-Item -LiteralPath $Msi -Destination $scratch
        [PCStewardMarkerTransform]::Create($Msi, $scratch, $transform, $Case)
        if (!(Test-Path -LiteralPath $transform -PathType Leaf)) { throw "No transform was written for $Case." }
        $transform
    }
    function ConvertTo-PackedProductCode([string]$Guid) {
        if ($Guid -cnotmatch '\A\{([0-9A-F]{8})-([0-9A-F]{4})-([0-9A-F]{4})-([0-9A-F]{4})-([0-9A-F]{12})\}\z') { throw "Not an uppercase braced GUID: $Guid" }
        $tail = $Matches[4] + $Matches[5]
        (-join @($Matches[1], $Matches[2], $Matches[3] | ForEach-Object { -join $_[($_.Length - 1)..0] })) + (-join @(0..7 | ForEach-Object { $tail[2 * $_ + 1]; $tail[2 * $_] }))
    }
    $packedCodes = @((Get-MsiPropertyValue $msis['0.1.1'].Path 'ProductCode'), (Get-MsiPropertyValue $msis['0.1.2'].Path 'ProductCode') | ForEach-Object { ConvertTo-PackedProductCode $_ }) + @(ConvertTo-PackedProductCode $upgradeCode)
    function Get-PerUserInstallerKeys { @(foreach ($root in @('HKCU:\Software\Microsoft\Installer', 'HKCU:\Software\Classes\Installer')) { foreach ($kind in @('Products', 'Features', 'UpgradeCodes')) { foreach ($packedCode in $packedCodes) { if (Test-Path -LiteralPath "$root\$kind\$packedCode") { "$root\$kind\$packedCode" } } } }) }
    function Get-MachineRegisteredProducts {
        $key = 'HKLM:\SOFTWARE\Classes\Installer\UpgradeCodes\' + (ConvertTo-PackedProductCode $upgradeCode)
        if (Test-Path -LiteralPath $key) { @((Get-Item -LiteralPath $key).Property | Where-Object { $_ }) } else { @() }
    }
    function Clear-ExperimentState {
        foreach ($product in @(Get-InstalledProducts)) { $null = Invoke-Msi @('/x', $product.ProductCode) 'experiment-cleanup-uninstall.log' }
        foreach ($key in @(Get-PerUserInstallerKeys)) { Remove-Item -LiteralPath $key -Recurse -Force -ErrorAction SilentlyContinue }
    }
    try {
        # Windows Installer's logging policy also logs the nested removal of the old product, wherever the marker might show.
        $null = New-Item -Path $loggingPolicy -Force
        New-ItemProperty -LiteralPath $loggingPolicy -Name 'Logging' -Value 'voicewarmupx' -PropertyType String -Force | Out-Null
        foreach ($case in @(@{ Name = 'new-product'; Advertise = '0.1.2' }, @{ Name = 'installed-product'; Advertise = '0.1.1' })) {
            $record = [ordered]@{}
            $experiment[$case.Name] = $record
            try {
                Clear-ExperimentState
                $record.machineInstall011Exit = Invoke-Msi @('/i', ('"' + $msis['0.1.1'].Path + '"')) "experiment-$($case.Name)-install-0.1.1.log"
                $transform = New-MarkerTransform $msis[$case.Advertise].Path $case.Name
                $record.perUserAdvertiseExit = Invoke-Msi @('/ju', ('"' + $msis[$case.Advertise].Path + '"'), '/t', ('"' + $transform + '"')) "experiment-$($case.Name)-advertise.log"
                $record.perUserKeysAfterAdvertise = @(Get-PerUserInstallerKeys)
                # Only logs written from here on: the advertisement's own logs name the marker by design.
                $since = Get-Date
                $record.elevatedInstall012Exit = Invoke-Msi @('/i', ('"' + $msis['0.1.2'].Path + '"')) "experiment-$($case.Name)-install-0.1.2.log"
                $record.machineProductsAfter = @(Get-MachineRegisteredProducts)
                $record.perUserKeysAfterInstall = @(Get-PerUserInstallerKeys)
                $record.installedVersions = @(Get-InstalledProducts | ForEach-Object { $_.Version })
                $policyLogs = @(foreach ($folder in @($env:TEMP, (Join-Path $env:SystemRoot 'Temp'))) { Get-ChildItem -LiteralPath $folder -Filter 'MSI*.LOG' -File -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $since } })
                $searched = @((Join-Path $logs "experiment-$($case.Name)-install-0.1.2.log")) + @($policyLogs | ForEach-Object { $_.FullName })
                $hits = @(Select-String -LiteralPath $searched -Pattern 'PCSTEWARDPERUSERMARKER' -SimpleMatch -ErrorAction SilentlyContinue)
                $record.transformAppliedDuringElevatedInstall = $hits.Count -gt 0
                $record.markerLines = @($hits | Select-Object -First 10 | ForEach-Object { "$([IO.Path]::GetFileName($_.Path)): $($_.Line.Trim())" })
                $record.transformLines = @(Select-String -LiteralPath $searched -Pattern 'marker-.*\.mst|TRANSFORMS' -ErrorAction SilentlyContinue | Select-Object -First 10 | ForEach-Object { "$([IO.Path]::GetFileName($_.Path)): $($_.Line.Trim())" })
                foreach ($log in $policyLogs) { Copy-Item -LiteralPath $log.FullName -Destination (Join-Path $logs "experiment-$($case.Name)-policy-$($log.Name)") -ErrorAction SilentlyContinue }
            }
            catch { $record.error = $_.Exception.Message }
            Write-Output ("EXPERIMENT {0}: {1}" -f $case.Name, ($record | ConvertTo-Json -Depth 4 -Compress))
        }
    }
    catch { $experiment.error = $_.Exception.Message; Write-Output "EXPERIMENT error: $($_.Exception.Message)" }
    finally {
        Remove-ItemProperty -LiteralPath $loggingPolicy -Name 'Logging' -ErrorAction SilentlyContinue
        try { Clear-ExperimentState } catch { $experiment.cleanupError = $_.Exception.Message }
    }

    $exitCode = if ($failures.Count -eq 0) { 0 } else { 1 }
}
catch {
    $failures.Add("error: $($_.Exception.Message)")
    Write-Output "ERROR: $($_.Exception.Message)"
    $exitCode = 1
}
finally {
    foreach ($product in @(Get-InstalledProducts)) { $null = Invoke-Msi @('/x', $product.ProductCode) 'cleanup-uninstall.log' }
    foreach ($leaf in $selfUpdateLeaves) { Remove-Item -LiteralPath (Join-Path $programFiles $leaf) -Recurse -Force -ErrorAction SilentlyContinue }
    if ($created) { Remove-Item -LiteralPath ('Cert:\CurrentUser\My\' + $created.Thumbprint) -DeleteKey -ErrorAction SilentlyContinue }
    [IO.File]::WriteAllBytes($pinsFile, $pinsOriginal)
    if ($account) { Remove-LocalUser -SID $account.SID }
    if ($marker -and (Test-Path -LiteralPath $marker)) { Remove-Item -LiteralPath $marker -Force }
    $password.Dispose()
    $evidence.failures = @($failures)
    $evidence.result = if ($failures.Count -eq 0) { 'Pass' } else { 'Fail' }
    [IO.File]::WriteAllText((Join-Path $evidenceRoot 'msi-ci-evidence.json'), ($evidence | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
}
if ($exitCode -eq 0) { Write-Output 'PASS: signed MSI install, permissions, pinned trust, capability, failed-upgrade rollback, upgrade, downgrade refusal, uninstall, the verified release install step, and the self-update finish copy.' }
exit $exitCode
