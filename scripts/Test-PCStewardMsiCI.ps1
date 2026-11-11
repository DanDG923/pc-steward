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
#   4. a higher version upgrades in place, a lower version is refused, and uninstall removes the program files and leaves
#      per-user data alone.
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
    $pinHash = [Security.Cryptography.SHA256]::HashData($certificate.PublicKey.ExportSubjectPublicKeyInfo())
    $pin = [Convert]::ToHexString($pinHash)
    $pinsText = [Text.Encoding]::UTF8.GetString($pinsOriginal)
    $placeholder = 'internal const string Primary = "";'
    if (([regex]::Matches($pinsText, [regex]::Escape($placeholder))).Count -ne 1) { throw 'ReleaseSigningPins.cs does not hold exactly one empty primary pin to replace in this CI checkout.' }
    [IO.File]::WriteAllText($pinsFile, $pinsText.Replace($placeholder, "internal const string Primary = `"$pin`";"), [Text.UTF8Encoding]::new($false))
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

    # Two signed payloads that differ only in version: the signing step a release script performs, with the throwaway key.
    $msis = [ordered]@{}
    foreach ($version in @('0.1.1', '0.1.2')) {
        $payload = Join-Path $work "payload-$version"
        Copy-Item -LiteralPath $candidatePayload -Destination $payload -Recurse
        foreach ($leaf in @('PCSteward.Desktop.exe', 'PCSteward.ElevatedHelper.exe')) {
            $target = Join-Path $payload $leaf
            $null = Set-AuthenticodeSignature -FilePath $target -Certificate $certificate -HashAlgorithm SHA256
            $signed = Get-AuthenticodeSignature -FilePath $target
            if ($null -eq $signed.SignerCertificate -or $signed.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) { throw "Signing $leaf did not embed the throwaway signer." }
        }
        $manifest = & (Join-Path $PSScriptRoot 'New-PCStewardReleaseManifest.ps1') -PayloadRoot $payload -ProductVersion $version | ConvertFrom-Json
        $msiPath = Join-Path $work "PCSteward-$version.msi"
        $msi = & (Join-Path $PSScriptRoot 'New-PCStewardMsi.ps1') -PayloadRoot $payload -MsiVersion $version -OutputPath $msiPath | Select-Object -Last 1 | ConvertFrom-Json
        $null = Set-AuthenticodeSignature -FilePath $msiPath -Certificate $certificate -HashAlgorithm SHA256
        if ((Get-AuthenticodeSignature -FilePath $msiPath).SignerCertificate.Thumbprint -ne $certificate.Thumbprint) { throw "Signing the $version MSI did not embed the throwaway signer." }
        $msis[$version] = [pscustomobject]@{ Path = $msiPath; Payload = $payload; ManifestSha256 = $manifest.manifestSha256; FileCount = $manifest.fileCount }
    }
    Set-Check 'msi-built-without-custom-actions' $true 'New-PCStewardMsi.ps1 checked the tables of both packages'

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
    $code = Invoke-Msi @('/i', ('"' + $msis['0.1.2'].Path + '"')) 'upgrade-0.1.2.log'
    $products = @(Get-InstalledProducts)
    Set-Check 'major-upgrade' (($code -eq 0) -and ($products.Count -eq 1) -and ($products[0].Version -eq '0.1.2') -and ($manifestJson = Get-Content -LiteralPath (Join-Path $installRoot 'PCSteward.ReleaseManifest.json') -Raw | ConvertFrom-Json).productVersion -eq '0.1.2') "exit $code; $(($products | ForEach-Object { $_.Version }) -join ',')"
    $code = Invoke-Msi @('/i', ('"' + $msis['0.1.1'].Path + '"')) 'downgrade-0.1.1.log'
    $products = @(Get-InstalledProducts)
    Set-Check 'downgrade-refused' (($code -ne 0) -and ($products.Count -eq 1) -and ($products[0].Version -eq '0.1.2')) "exit $code"
    $code = Invoke-Msi @('/x', $products[0].ProductCode) 'uninstall.log'
    Set-Check 'uninstall-removes-program-files' (($code -eq 0) -and !(Test-Path -LiteralPath $installRoot) -and (@(Get-InstalledProducts).Count -eq 0) -and !(Test-Path -LiteralPath (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonPrograms)) 'PC Steward.lnk'))) "exit $code"
    Set-Check 'uninstall-keeps-per-user-data' (Test-Path -LiteralPath $marker -PathType Leaf)

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
if ($exitCode -eq 0) { Write-Output 'PASS: signed MSI install, permissions, pinned trust, capability, upgrade, downgrade refusal and uninstall.' }
exit $exitCode
