[CmdletBinding()]
param([string]$CandidateSearchRoot = $env:RUNNER_TEMP, [string]$ExpectedSourceCommit)

# Task 50 B-exec signing diagnosis (FR-UPD-15, TM-03/TM-13, ADR-022); informational Windows CI only.
# On the owner's host, Install-OwnerOnlyProtectedTestDeployment.ps1 got a non-Valid Set-AuthenticodeSignature result for
# the G3-built PCSteward.Desktop.exe and did not record Status/StatusMessage. This script reproduces the signing step on a
# disposable GitHub-hosted runner: a throwaway self-signed code-signing certificate with the installer's exact parameters,
# trusted in LocalMachine Root/TrustedPublisher (CurrentUser Root shows a UI prompt, so it is not used headless) and in
# CurrentUser TrustedPublisher; then Set-AuthenticodeSignature -HashAlgorithm SHA256 on COPIES of the candidate apphosts and
# of two controls, in a plain temp folder and in a Program Files folder with the installer's protected DACL. It prints the
# status, status message, signer, Get-AuthenticodeSignature, signtool verify and PE facts. The candidate is never modified.
# All certificates and folders it creates are removed at the end. Runs only elevated under Windows PowerShell 5.1.
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Run under Windows PowerShell 5.1, the installer runtime.' }
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or !$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'This diagnostic changes certificate stores; it runs only as an administrator on a disposable GitHub-hosted runner.'
}
if (!$CandidateSearchRoot -or !(Test-Path -LiteralPath $CandidateSearchRoot -PathType Container)) { throw 'CandidateSearchRoot (RUNNER_TEMP) is required.' }
$repository = Split-Path -Parent $PSScriptRoot
$evidenceRoot = Join-Path $repository 'artifacts\signing-diagnostic'
New-Item -ItemType Directory -Path $evidenceRoot -Force | Out-Null

# ---- Pure PE facts (bytes only) ----
$script:BundleSignature = [byte[]](0x8b,0x12,0x02,0xb9,0x6a,0x61,0x20,0x38,0x72,0x7b,0x93,0x02,0x14,0xd7,0xa0,0x32,0x13,0xf5,0xb9,0xe6,0xef,0xae,0x33,0x18,0xee,0x3b,0x2d,0xce,0x24,0xb3,0x6a,0xae)
$script:Latin1 = [Text.Encoding]::GetEncoding(28591)
function Find-Bytes([byte[]]$Data, [byte[]]$Pattern) { $script:Latin1.GetString($Data).IndexOf($script:Latin1.GetString($Pattern), [StringComparison]::Ordinal) }
function Get-PeFacts {
    param([Parameter(Mandatory)][byte[]]$Data)
    $facts = [ordered]@{ bytes = $Data.Length; pe = 'invalid' }
    if ($Data.Length -lt 512 -or [BitConverter]::ToUInt16($Data, 0) -ne 0x5A4D) { return $facts }
    $header = [BitConverter]::ToInt32($Data, 60)
    if ($header -lt 0 -or $header + 264 -gt $Data.Length -or [BitConverter]::ToUInt32($Data, $header) -ne 0x4550) { return $facts }
    $optional = $header + 24; $magic = [BitConverter]::ToUInt16($Data, $optional)
    $directories = if ($magic -eq 0x20b) { $optional + 112 } else { $optional + 96 }
    $sectionTable = $optional + [BitConverter]::ToUInt16($Data, $header + 20)
    $count = [BitConverter]::ToUInt16($Data, $header + 6)
    [long]$rawEnd = [BitConverter]::ToUInt32($Data, $optional + 60)
    $names = [Collections.Generic.List[string]]::new()
    for ($i = 0; $i -lt $count -and $sectionTable + $i * 40 + 40 -le $Data.Length; $i++) {
        $section = $sectionTable + $i * 40
        $names.Add($script:Latin1.GetString($Data, $section, 8).TrimEnd([char]0))
        [long]$stop = [long][BitConverter]::ToUInt32($Data, $section + 20) + [BitConverter]::ToUInt32($Data, $section + 16)
        if ($stop -gt $rawEnd) { $rawEnd = $stop }
    }
    [long]$certificateOffset = [BitConverter]::ToUInt32($Data, $directories + 32); [long]$certificateSize = [BitConverter]::ToUInt32($Data, $directories + 36)
    [long]$extra = if ($certificateSize -gt 0) { ($certificateOffset - $rawEnd) + ($Data.Length - ($certificateOffset + $certificateSize)) } else { $Data.Length - $rawEnd }
    $bundleAt = Find-Bytes $Data $script:BundleSignature
    $facts.pe = if ($magic -eq 0x20b) { 'PE32+' } else { 'PE32' }
    $facts.machine = '0x{0:X4}' -f [BitConverter]::ToUInt16($Data, $header + 4)
    $facts.sections = ($names -join ',')
    $facts.fileAlignment = [BitConverter]::ToUInt32($Data, $optional + 36)
    $facts.headerChecksum = '0x{0:X8}' -f [BitConverter]::ToUInt32($Data, $optional + 64)
    $facts.subsystem = [BitConverter]::ToUInt16($Data, $optional + 68)
    $facts.dllCharacteristics = '0x{0:X4}' -f [BitConverter]::ToUInt16($Data, $optional + 70)
    $facts.lastSectionEnd = $rawEnd
    $facts.certificateTable = if ($certificateSize -gt 0) { "offset $certificateOffset size $certificateSize" } else { 'none' }
    $facts.overlayBytes = $extra
    $facts.clrHeader = ([BitConverter]::ToUInt32($Data, $directories + 116) -ne 0)
    # .NET apphost: the bundle marker is preceded by an 8-byte bundle-header offset that is 0 unless the host is a single-file bundle.
    $facts.dotnetBundleMarker = ($bundleAt -ge 8)
    $facts.dotnetSingleFileBundle = ($bundleAt -ge 8 -and [BitConverter]::ToInt64($Data, $bundleAt - 8) -ne 0)
    # The unpatched apphost template still carries the SHA-256 of "foobar" where the app DLL name belongs.
    $facts.apphostPlaceholderUnpatched = ((Find-Bytes $Data ([Text.Encoding]::ASCII.GetBytes('c3ab8ff13720e8ad9047dd39466b3c8974e592c2fa383d4a3960714caef0c4f2'))) -ge 0)
    $facts
}

function Get-Sha256([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }

function New-ProtectedSecurity([switch]$Directory) {
    # The installer's protected DACL (Set-OwnerOnlyAcl / PayloadIntegrity.Protect): owner Administrators, no inheritance,
    # SYSTEM and Administrators FullControl, Users ReadAndExecute+Synchronize.
    $security = if ($Directory) { [Security.AccessControl.DirectorySecurity]::new() } else { [Security.AccessControl.FileSecurity]::new() }
    $security.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
    $security.SetAccessRuleProtection($true, $false)
    foreach ($sid in @('S-1-5-18', 'S-1-5-32-544')) { [void]$security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($sid), [Security.AccessControl.FileSystemRights]::FullControl, [Security.AccessControl.AccessControlType]::Allow)) }
    [void]$security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new('S-1-5-32-545'), ([Security.AccessControl.FileSystemRights]::ReadAndExecute -bor [Security.AccessControl.FileSystemRights]::Synchronize), [Security.AccessControl.AccessControlType]::Allow))
    $security
}

function Copy-Plain([string]$Source, [string]$Directory, [string]$Leaf) {
    $target = Join-Path $Directory $Leaf
    [IO.File]::Copy($Source, $target, $false)
    [IO.File]::SetAttributes($target, [IO.FileAttributes]::Normal)
    $target
}

function Copy-Protected([string]$Source, [string]$Directory, [string]$Leaf) {
    # Same creation route as PayloadIntegrity.CopyTreeProtected: CreateNew with an explicit protected FileSecurity and
    # FileSystemRights.Write, then the installer's later Set-Acl pass.
    $target = Join-Path $Directory $Leaf
    $reader = [IO.File]::OpenRead($Source)
    try {
        $writer = [IO.FileStream]::new($target, [IO.FileMode]::CreateNew, [Security.AccessControl.FileSystemRights]::Write, [IO.FileShare]::None, 4096, [IO.FileOptions]::None, (New-ProtectedSecurity))
        try { $reader.CopyTo($writer); $writer.Flush($true) } finally { $writer.Dispose() }
    } finally { $reader.Dispose() }
    Set-Acl -LiteralPath $target -AclObject (New-ProtectedSecurity)
    $target
}

function Find-SignTool {
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (!(Test-Path -LiteralPath $kits)) { return $null }
    $found = @(Get-ChildItem -LiteralPath $kits -Directory -Filter '10.*' | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'x64\signtool.exe') } | Sort-Object { [version]$_.Name })
    if ($found.Count -eq 0) { return $null }
    Join-Path $found[-1].FullName 'x64\signtool.exe'
}

function Invoke-Native([string]$Executable, [string[]]$Arguments) {
    # Function-local: Windows PowerShell 5.1 can turn native stderr into terminating errors under 'Stop'.
    $ErrorActionPreference = 'Continue'
    $output = @(& $Executable @Arguments 2>&1 | ForEach-Object { [string]$_ })
    [pscustomobject]@{ exitCode = $LASTEXITCODE; output = @($output | Where-Object { $_.Trim() } | Select-Object -Last 25) }
}

function Test-ExclusiveOpen([string]$Path) {
    try { $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None); $stream.Dispose(); 'ok' } catch { $_.Exception.Message }
}

$script:Results = [Collections.Generic.List[object]]::new()
function Invoke-SigningAttempt {
    param([string]$Item, [string]$Variant, [string]$Path, $Certificate, [string]$SignTool, [switch]$UseSignToolToSign)
    $before = Get-PeFacts -Data ([IO.File]::ReadAllBytes($Path))
    $record = [ordered]@{ item = $Item; variant = $Variant; path = $Path; sha256Before = (Get-Sha256 $Path); peBefore = $before; exclusiveOpenBefore = (Test-ExclusiveOpen $Path); readOnly = [bool]((Get-Item -LiteralPath $Path).Attributes -band [IO.FileAttributes]::ReadOnly) }
    $record.getBefore = [string](Get-AuthenticodeSignature -LiteralPath $Path).Status
    if ($UseSignToolToSign) {
        if (!$SignTool) { $record.signError = 'signtool.exe not found' }
        else { $sign = Invoke-Native $SignTool @('sign', '/fd', 'SHA256', '/s', 'My', '/sha1', $Certificate.Thumbprint, $Path); $record.signtoolSignExit = $sign.exitCode; $record.signtoolSignOutput = $sign.output }
    } else {
        try {
            # The installer's exact call; the RETURNED object is what its line-157 check reads (not a later Get-AuthenticodeSignature).
            $set = Set-AuthenticodeSignature -FilePath $Path -Certificate $Certificate -HashAlgorithm SHA256 -ErrorAction Stop
            $record.setReturnedObjects = @($set).Count
            $record.setStatus = [string]$set.Status
            $record.setStatusMessage = [string]$set.StatusMessage
            $record.setSignatureType = [string]$set.SignatureType
            $record.setPath = [string]$set.Path
            $record.setSignerThumbprint = if ($set.SignerCertificate) { $set.SignerCertificate.Thumbprint } else { $null }
            $record.setSignerSubject = if ($set.SignerCertificate) { $set.SignerCertificate.Subject } else { $null }
            # Exactly the installer's line-157 acceptance predicate.
            $record.installerWouldAccept = ($set.Status -eq 'Valid' -and $null -ne $set.SignerCertificate -and $set.SignerCertificate.Thumbprint -eq $Certificate.Thumbprint)
        } catch { $record.signError = $_.Exception.GetType().FullName + ': ' + $_.Exception.Message }
    }
    $get = Get-AuthenticodeSignature -LiteralPath $Path
    $record.getStatus = [string]$get.Status
    $record.getStatusMessage = [string]$get.StatusMessage
    $record.getSignatureType = [string]$get.SignatureType
    $record.getSignerThumbprint = if ($get.SignerCertificate) { $get.SignerCertificate.Thumbprint } else { $null }
    $record.getTimeStamper = if ($get.TimeStamperCertificate) { $get.TimeStamperCertificate.Thumbprint } else { $null }
    $record.sha256After = Get-Sha256 $Path
    $record.peAfter = Get-PeFacts -Data ([IO.File]::ReadAllBytes($Path))
    if ($SignTool) { $verify = Invoke-Native $SignTool @('verify', '/pa', '/v', $Path); $record.signtoolVerifyExit = $verify.exitCode; $record.signtoolVerifyOutput = $verify.output }
    $script:Results.Add([pscustomobject]$record)
    Write-Host ("== {0} [{1}]: set={2} setSigner={3} get={4} getSigner={5} installerAccepts={6} signtoolVerify={7} overlayBefore={8} bundle={9}" -f $Item, $Variant, $record.setStatus, $record.setSignerThumbprint, $record.getStatus, $record.getSignerThumbprint, $record.installerWouldAccept, $record.signtoolVerifyExit, $before.overlayBytes, $before.dotnetSingleFileBundle)
    if ($record.setStatusMessage) { Write-Host "   set message: $($record.setStatusMessage)" }
    if ($record.getStatusMessage) { Write-Host "   get message: $($record.getStatusMessage)" }
    if ($record.signError) { Write-Host "   sign error: $($record.signError)" }
}

$certificate = $null; $trusted = [Collections.Generic.List[object]]::new(); $protectedRoot = $null; $exitCode = 1
$work = Join-Path $CandidateSearchRoot ('pcsteward-signing-diagnostic-' + [Guid]::NewGuid().ToString('N'))
try {
    # ---- Candidate built by the G3 builder job step (disposable standard user) ----
    $summaries = @(Get-ChildItem -LiteralPath $CandidateSearchRoot -Directory -Filter 'pcsteward-candidate-*' | ForEach-Object { Join-Path $_.FullName 'candidate\candidate-build.json' } | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
    if ($summaries.Count -ne 1) { throw "Expected exactly one built candidate under $CandidateSearchRoot; found $($summaries.Count)." }
    $summary = Get-Content -LiteralPath $summaries[0] -Raw | ConvertFrom-Json
    $payload = Join-Path (Split-Path -Parent $summaries[0]) 'payload'
    if ($ExpectedSourceCommit -and $summary.sourceCommit -cne $ExpectedSourceCommit) { throw "The candidate source commit $($summary.sourceCommit) is not the expected $ExpectedSourceCommit." }
    $os = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    $environment = [ordered]@{
        os = "$([Environment]::OSVersion.Version) UBR $($os.UBR) ($($os.ProductName) $($os.DisplayVersion))"; powershell = $PSVersionTable.PSVersion.ToString(); clr = $PSVersionTable.CLRVersion.ToString()
        candidateSourceCommit = $summary.sourceCommit; candidateManifestSha256 = $summary.manifestSha256; candidateFiles = $summary.fileCount; candidateBytes = $summary.totalBytes; sdk = $summary.sdkVersion
        unsignedExecutables = @($summary.unsignedExecutables)
    }
    # Packaged (MSIX) context of this process: 15700 APPMODEL_ERROR_NO_PACKAGE means unpackaged. Host hypothesis H1 needs a packaged ancestor.
    try {
        Add-Type -Namespace PCStewardSigningDiagnostic -Name Package -MemberDefinition '[DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern int GetCurrentPackageFullName(ref int length, System.Text.StringBuilder name);'
        $length = 0; $environment.packagedProcess = 'GetCurrentPackageFullName=' + [PCStewardSigningDiagnostic.Package]::GetCurrentPackageFullName([ref]$length, $null)
    } catch { $environment.packagedProcess = 'unavailable: ' + $_.Exception.Message }
    try { $mp = Get-MpComputerStatus; $environment.defender = "AMRunningMode=$($mp.AMRunningMode) RealTime=$($mp.RealTimeProtectionEnabled) SmartAppControl=$($mp.SmartAppControlState)" } catch { $environment.defender = 'unavailable: ' + $_.Exception.Message }
    $environment | ConvertTo-Json -Depth 4 | Write-Host

    New-Item -ItemType Directory -Path $work | Out-Null
    $plain = Join-Path $work 'plain'; $untrusted = Join-Path $work 'untrusted'; $signToolCopies = Join-Path $work 'signtool'
    New-Item -ItemType Directory -Path $plain, $untrusted, $signToolCopies | Out-Null
    $programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
    $protectedRoot = Join-Path $programFiles ('PCStewardSigningDiagnostic-' + [Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($protectedRoot, (New-ProtectedSecurity -Directory))
    $protectedPayload = Join-Path $protectedRoot 'payload'
    [void][IO.Directory]::CreateDirectory($protectedPayload, (New-ProtectedSecurity -Directory))

    # ---- Controls: a freshly built tiny framework-dependent console apphost and a copy of the (signed) dotnet.exe ----
    $dotnet = (Get-Command -Name 'dotnet' -CommandType Application | Select-Object -First 1).Source
    $controlSource = Join-Path $work 'control-console'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'
    $new = Invoke-Native $dotnet @('new', 'console', '--name', 'SignControl', '--output', $controlSource)
    $build = if ($new.exitCode -eq 0) { Invoke-Native $dotnet @('build', (Join-Path $controlSource 'SignControl.csproj'), '--configuration', 'Release', '--runtime', 'win-x64', '--self-contained', 'false', '--output', (Join-Path $controlSource 'out')) } else { $new }
    $sources = [ordered]@{ 'PCSteward.Desktop.exe' = (Join-Path $payload 'PCSteward.Desktop.exe'); 'PCSteward.ElevatedHelper.exe' = (Join-Path $payload 'PCSteward.ElevatedHelper.exe') }
    $consoleExe = Join-Path $controlSource 'out\SignControl.exe'
    if ($build.exitCode -eq 0 -and (Test-Path -LiteralPath $consoleExe)) { $sources['control-console-apphost.exe'] = $consoleExe } else { Write-Host "Control console build failed (exit $($build.exitCode)): $($build.output -join ' | ')" }
    $sources['control-dotnet.exe'] = $dotnet
    $sourceHashes = @{}
    foreach ($key in @($sources.Keys)) { $sourceHashes[$key] = Get-Sha256 $sources[$key] }

    # ---- The installer's certificate: New-OwnerOnlyCertificate parameters, unique test subject ----
    $subject = "CN=PC Steward Owner-Only Test Signing Diagnostic $(New-Guid)"
    $certificate = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject -CertStoreLocation 'Cert:\CurrentUser\My' -KeyExportPolicy NonExportable -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -NotAfter (Get-Date).AddDays(14)
    Write-Host ("Certificate {0}: NotBefore {1:o} NotAfter {2:o} HasPrivateKey {3} EKU {4} KeyProvider {5}" -f $certificate.Thumbprint, $certificate.NotBefore.ToUniversalTime(), $certificate.NotAfter.ToUniversalTime(), $certificate.HasPrivateKey, (($certificate.EnhancedKeyUsageList | ForEach-Object { $_.ObjectId }) -join ','), $(try { [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($certificate).Key.Provider } catch { 'n/a' }))
    $signTool = Find-SignTool
    Write-Host "signtool: $signTool"

    # Baseline before any trust: what a chain that does not reach a trusted root looks like.
    foreach ($key in @('PCSteward.Desktop.exe', 'control-dotnet.exe')) { Invoke-SigningAttempt -Item $key -Variant 'temp-untrusted' -Path (Copy-Plain $sources[$key] $untrusted $key) -Certificate $certificate -SignTool $signTool }

    foreach ($store in @(@('Root', 'LocalMachine'), @('TrustedPublisher', 'LocalMachine'), @('TrustedPublisher', 'CurrentUser'))) {
        $x509 = [Security.Cryptography.X509Certificates.X509Store]::new($store[0], [Security.Cryptography.X509Certificates.StoreLocation]($store[1]))
        $x509.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        try { $x509.Add($certificate); $trusted.Add($store) } finally { $x509.Close() }
    }
    $chain = [Security.Cryptography.X509Certificates.X509Chain]::new(); $chain.ChainPolicy.RevocationMode = 'NoCheck'
    Write-Host "Chain build after trust: $($chain.Build($certificate)) $((@($chain.ChainStatus | ForEach-Object { $_.Status })) -join ',')"

    foreach ($key in @($sources.Keys)) {
        Invoke-SigningAttempt -Item $key -Variant 'temp-trusted' -Path (Copy-Plain $sources[$key] $plain $key) -Certificate $certificate -SignTool $signTool
        Invoke-SigningAttempt -Item $key -Variant 'programfiles-protected' -Path (Copy-Protected $sources[$key] $protectedPayload $key) -Certificate $certificate -SignTool $signTool
    }
    Invoke-SigningAttempt -Item 'PCSteward.Desktop.exe' -Variant 'signtool-sign' -Path (Copy-Plain $sources['PCSteward.Desktop.exe'] $signToolCopies 'PCSteward.Desktop.exe') -Certificate $certificate -SignTool $signTool -UseSignToolToSign

    foreach ($key in @($sources.Keys)) { if ((Get-Sha256 $sources[$key]) -cne $sourceHashes[$key]) { throw "A diagnostic source changed: $key" } }
    try { $detections = @(Get-MpThreatDetection -ErrorAction Stop | Where-Object { $_.InitialDetectionTime -ge (Get-Date).AddHours(-2) } | ForEach-Object { "$($_.InitialDetectionTime) $($_.Resources -join ';')" }) } catch { $detections = @('unavailable: ' + $_.Exception.Message) }
    Write-Host "Defender detections in the last two hours: $(if ($detections.Count) { $detections -join ' | ' } else { 'none' })"

    $evidence = [ordered]@{ schema = 'pc-steward-owner-only-signing-diagnostic-v1'; environment = $environment; certificate = [ordered]@{ thumbprint = $certificate.Thumbprint; subject = $subject; trustedIn = @($trusted | ForEach-Object { $_[1] + '\' + $_[0] }) }; signtool = $signTool; defenderDetections = $detections; attempts = @($script:Results) }
    [IO.File]::WriteAllText((Join-Path $evidenceRoot 'signing-diagnostic.json'), (ConvertTo-Json -InputObject $evidence -Depth 6), [Text.UTF8Encoding]::new($false))
    $table = @($script:Results | Select-Object item, variant, setStatus, getStatus, installerWouldAccept, signtoolVerifyExit, @{ n = 'overlay'; e = { $_.peBefore.overlayBytes } }, @{ n = 'bundle'; e = { $_.peBefore.dotnetSingleFileBundle } }, @{ n = 'message'; e = { if ($_.setStatusMessage) { $_.setStatusMessage } elseif ($_.signError) { $_.signError } else { $_.getStatusMessage } } })
    Write-Host '==== SUMMARY ===='
    $table | Format-Table -AutoSize -Wrap | Out-String -Width 220 | Write-Host
    if ($env:GITHUB_STEP_SUMMARY) {
        $lines = @('### Owner-only signing diagnostic (informational)', '', "Windows $($environment.os); Windows PowerShell $($environment.powershell); candidate $($environment.candidateSourceCommit) manifest $($environment.candidateManifestSha256)", '', '| item | variant | Set status | Get status | installer accepts | signtool verify | overlay | bundle | message |', '|---|---|---|---|---|---|---|---|---|')
        foreach ($row in $table) { $lines += "| $($row.item) | $($row.variant) | $($row.setStatus) | $($row.getStatus) | $($row.installerWouldAccept) | $($row.signtoolVerifyExit) | $($row.overlay) | $($row.bundle) | $(([string]$row.message).Replace('|', '/').Replace("`r", ' ').Replace("`n", ' ')) |" }
        Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $lines -Encoding UTF8
    }
    $exitCode = 0
}
finally {
    $cleanup = [Collections.Generic.List[string]]::new()
    if ($certificate) {
        foreach ($store in @(@('Root', 'LocalMachine'), @('TrustedPublisher', 'LocalMachine'), @('TrustedPublisher', 'CurrentUser'))) {
            try {
                $x509 = [Security.Cryptography.X509Certificates.X509Store]::new($store[0], [Security.Cryptography.X509Certificates.StoreLocation]($store[1]))
                $x509.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
                try { foreach ($found in @($x509.Certificates.Find([Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint, $certificate.Thumbprint, $false))) { $x509.Remove($found) } } finally { $x509.Close() }
            } catch { $cleanup.Add("$($store[1])\$($store[0]): $($_.Exception.Message)") }
        }
        try { Remove-Item -LiteralPath ('Cert:\CurrentUser\My\' + $certificate.Thumbprint) -DeleteKey -ErrorAction Stop } catch { $cleanup.Add("CurrentUser\My: $($_.Exception.Message)") }
        foreach ($path in @('Cert:\LocalMachine\Root', 'Cert:\LocalMachine\TrustedPublisher', 'Cert:\CurrentUser\TrustedPublisher', 'Cert:\CurrentUser\My')) {
            if (Test-Path -LiteralPath (Join-Path $path $certificate.Thumbprint)) { $cleanup.Add("certificate remains in $path") }
        }
    }
    foreach ($directory in @($protectedRoot, $work)) {
        if ($directory -and (Test-Path -LiteralPath $directory)) { try { Remove-Item -LiteralPath $directory -Recurse -Force -ErrorAction Stop } catch { $cleanup.Add("$directory`: $($_.Exception.Message)") } }
    }
    if ($cleanup.Count) { Write-Host "CLEANUP INCOMPLETE: $($cleanup -join '; ')"; $exitCode = 1 } else { Write-Host 'Cleanup: diagnostic certificate and folders removed.' }
}
exit $exitCode
