param([string]$ResultPath)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptRoot = $PSScriptRoot
$adminScript = Join-Path $scriptRoot 'Invoke-InstalledHelperEvidenceAdmin.ps1'
$programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$installedRoot = [IO.Path]::GetFullPath((Join-Path $programFiles 'PC Steward'))
$runId = [Guid]::NewGuid().ToString('N')
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "pcsteward-installed-trust-$runId"
$desktopStage = Join-Path $temporaryRoot 'desktop'
$helperStage = Join-Path $temporaryRoot 'helper'
$wrongSignerHelper = Join-Path $temporaryRoot 'wrong-signer-helper.exe'
$tamperedHelper = Join-Path $temporaryRoot 'tampered-helper.exe'
$certificateFile = Join-Path $temporaryRoot 'primary.cer'
$wrongCertificateFile = Join-Path $temporaryRoot 'wrong.cer'
$results = [Collections.Generic.List[object]]::new()
$certificates = [Collections.Generic.List[Security.Cryptography.X509Certificates.X509Certificate2]]::new()
$certificateThumbprints = [Collections.Generic.List[string]]::new()
$evidence = $null
$cleanLayout = $null

function Start-AdminStep {
    param(
        [Parameter(Mandatory)][string]$Action,
        [hashtable]$Additional = @{},
        [switch]$NoWait
    )

    $arguments = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', $adminScript,
        '-Action', $Action,
        '-RunId', $runId
    )
    foreach ($entry in $Additional.GetEnumerator()) {
        $arguments += "-$($entry.Key)"
        $arguments += [string]$entry.Value
    }

    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = (Get-Command powershell.exe -ErrorAction Stop).Source
    $start.UseShellExecute = $true
    $start.Verb = 'runas'
    foreach ($argument in $arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    if ($null -eq $process) { throw "The elevated $Action step did not start." }
    if ($NoWait) { return $process }
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "The elevated $Action step failed with exit $($process.ExitCode)." }
}

function Install-Layout {
    param(
        [Parameter(Mandatory)][string]$Action,
        [string]$ReplacementHelper
    )
    $additional = @{
        DesktopStage = $desktopStage
        HelperStage = $helperStage
    }
    if ($ReplacementHelper) { $additional.ReplacementHelper = $ReplacementHelper }
    Start-AdminStep -Action $Action -Additional $additional
}

function Invoke-DesktopFixture {
    param(
        [Parameter(Mandatory)][string]$Case,
        [string[]]$Arguments = @(),
        [bool]$ExpectStarted = $false
    )

    $desktop = Join-Path $installedRoot 'PCSteward.Desktop.exe'
    $started = Join-Path $installedRoot '.pcsteward-evidence-helper-started'
    $output = & $desktop @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "$Case failed with exit $exitCode. Output: $($output | Out-String)"
    }
    $startedObserved = Test-Path -LiteralPath $started
    if ($startedObserved -ne $ExpectStarted) {
        throw "$Case helper-start marker mismatch. Expected=$ExpectStarted Observed=$startedObserved"
    }
    $results.Add([pscustomobject]@{
        Case = $Case
        ExitCode = $exitCode
        HelperStarted = $startedObserved
        Output = ($output | Out-String).Trim()
    })
}

function New-EvidenceCertificate {
    param([Parameter(Mandatory)][string]$Subject, [Parameter(Mandatory)][string]$ExportPath)
    $certificate = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $Subject `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -HashAlgorithm SHA256 `
        -KeyExportPolicy Exportable `
        -NotAfter ([DateTime]::UtcNow.AddDays(2))
    $certificates.Add($certificate)
    $certificateThumbprints.Add($certificate.Thumbprint)
    Export-Certificate -Cert $certificate -FilePath $ExportPath -Force | Out-Null
    Import-Certificate -FilePath $ExportPath -CertStoreLocation 'Cert:\CurrentUser\Root' | Out-Null
    Import-Certificate -FilePath $ExportPath -CertStoreLocation 'Cert:\CurrentUser\TrustedPublisher' | Out-Null
    return $certificate
}

function Remove-EvidenceCertificate {
    param([Parameter(Mandatory)][string]$Thumbprint)
    foreach ($storeName in @('My', 'Root', 'TrustedPublisher')) {
        $store = [Security.Cryptography.X509Certificates.X509Store]::new(
            $storeName,
            [Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
        $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        try {
            $matches = $store.Certificates.Find(
                [Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint,
                $Thumbprint,
                $false)
            foreach ($match in $matches) { $store.Remove($match) }
        }
        finally {
            $store.Close()
        }
    }
}

function Count-EvidenceCertificate {
    param([Parameter(Mandatory)][string]$Thumbprint)
    $count = 0
    foreach ($storeName in @('My', 'Root', 'TrustedPublisher')) {
        $store = [Security.Cryptography.X509Certificates.X509Store]::new(
            $storeName,
            [Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
        $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
        try {
            $count += $store.Certificates.Find(
                [Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint,
                $Thumbprint,
                $false).Count
        }
        finally {
            $store.Close()
        }
    }
    return $count
}

if (Test-Path -LiteralPath $installedRoot) {
    throw "Refusing to run because the fixed installed root already exists: $installedRoot"
}

[IO.Directory]::CreateDirectory($desktopStage) | Out-Null
[IO.Directory]::CreateDirectory($helperStage) | Out-Null

try {
    & dotnet build (Join-Path $scriptRoot 'desktop\PCSteward.ProductionTrustDesktop.csproj') --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'The desktop evidence fixture build failed.' }
    & dotnet build (Join-Path $scriptRoot 'helper\PCSteward.ProductionTrustHelper.csproj') --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'The helper evidence fixture build failed.' }

    $desktopOutput = Join-Path $scriptRoot 'desktop\bin\Release\net10.0-windows10.0.22000.0'
    $helperOutput = Join-Path $scriptRoot 'helper\bin\Release\net10.0-windows10.0.22000.0'
    Copy-Item -Path (Join-Path $desktopOutput '*') -Destination $desktopStage -Recurse -Force
    Copy-Item -Path (Join-Path $helperOutput '*') -Destination $helperStage -Recurse -Force
    Move-Item -LiteralPath (Join-Path $desktopStage 'PCSteward.ProductionTrustDesktop.exe') -Destination (Join-Path $desktopStage 'PCSteward.Desktop.exe')
    Move-Item -LiteralPath (Join-Path $helperStage 'PCSteward.ProductionTrustHelper.exe') -Destination (Join-Path $helperStage 'PCSteward.ElevatedHelper.exe')

    $primary = New-EvidenceCertificate "CN=PC Steward installed-helper evidence $runId" $certificateFile
    $wrong = New-EvidenceCertificate "CN=PC Steward wrong-signer evidence $runId" $wrongCertificateFile
    $desktopExecutable = Join-Path $desktopStage 'PCSteward.Desktop.exe'
    $helperExecutable = Join-Path $helperStage 'PCSteward.ElevatedHelper.exe'
    Set-AuthenticodeSignature -FilePath $desktopExecutable -Certificate $primary -HashAlgorithm SHA256 | Out-Null
    Set-AuthenticodeSignature -FilePath $helperExecutable -Certificate $primary -HashAlgorithm SHA256 | Out-Null
    Copy-Item -LiteralPath $helperExecutable -Destination $wrongSignerHelper
    Set-AuthenticodeSignature -FilePath $wrongSignerHelper -Certificate $wrong -HashAlgorithm SHA256 | Out-Null
    Copy-Item -LiteralPath $helperExecutable -Destination $tamperedHelper
    $tamperStream = [IO.File]::Open($tamperedHelper, [IO.FileMode]::Append, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $tamperStream.WriteByte(0x5A) } finally { $tamperStream.Dispose() }

    foreach ($path in @($desktopExecutable, $helperExecutable, $wrongSignerHelper)) {
        $signature = Get-AuthenticodeSignature -FilePath $path
        if ($signature.Status -ne 'Valid') { throw "Fixture signature is not valid: $path ($($signature.Status))" }
    }
    if ((Get-AuthenticodeSignature -FilePath $tamperedHelper).Status -eq 'Valid') {
        throw 'The tampered helper unexpectedly retained a valid signature.'
    }

    Install-Layout -Action InstallClean
    $installedDesktop = Join-Path $installedRoot 'PCSteward.Desktop.exe'
    $installedHelper = Join-Path $installedRoot 'PCSteward.ElevatedHelper.exe'
    $volumeRoot = [IO.Path]::GetPathRoot($installedRoot)
    $installedDesktopSignature = Get-AuthenticodeSignature $installedDesktop
    $installedHelperSignature = Get-AuthenticodeSignature $installedHelper
    $cleanLayout = [pscustomobject]@{
        RootOwner = (Get-Acl -LiteralPath $installedRoot).Owner
        RootSddl = (Get-Acl -LiteralPath $installedRoot).Sddl
        ProgramFilesSddl = (Get-Acl -LiteralPath $programFiles).Sddl
        VolumeRootSddl = (Get-Acl -LiteralPath $volumeRoot).Sddl
        DesktopSddl = (Get-Acl -LiteralPath $installedDesktop).Sddl
        HelperSddl = (Get-Acl -LiteralPath $installedHelper).Sddl
        RootAttributes = (Get-Item -LiteralPath $installedRoot -Force).Attributes.ToString()
        DesktopAttributes = (Get-Item -LiteralPath $installedDesktop -Force).Attributes.ToString()
        HelperAttributes = (Get-Item -LiteralPath $installedHelper -Force).Attributes.ToString()
        DesktopSignature = $installedDesktopSignature.Status.ToString()
        HelperSignature = $installedHelperSignature.Status.ToString()
        DesktopSigner = $installedDesktopSignature.SignerCertificate.Thumbprint
        HelperSigner = $installedHelperSignature.SignerCertificate.Thumbprint
        RootIcacls = ((& icacls.exe $installedRoot) | Out-String).Trim()
        ProgramFilesIcacls = ((& icacls.exe $programFiles) | Out-String).Trim()
        VolumeRootIcacls = ((& icacls.exe $volumeRoot) | Out-String).Trim()
    }
    Invoke-DesktopFixture -Case 'clean-signed-protected-uac-no-op' -ExpectStarted $true

    Install-Layout -Action InstallTampered -ReplacementHelper $tamperedHelper
    Invoke-DesktopFixture -Case 'tampered-signature-zero-launch' -Arguments @('--expect', 'elevated-helper-signature-untrusted')

    Install-Layout -Action InstallWrongSigner -ReplacementHelper $wrongSignerHelper
    Invoke-DesktopFixture -Case 'wrong-signer-zero-launch' -Arguments @('--expect', 'elevated-helper-signer-mismatch')

    Install-Layout -Action InstallWritable
    Invoke-DesktopFixture -Case 'caller-writable-zero-launch' -Arguments @('--expect', 'elevated-helper-caller-writable-layout-rejected')

    Install-Layout -Action InstallReparse
    Invoke-DesktopFixture -Case 'helper-reparse-zero-launch' -Arguments @('--expect', 'elevated-helper-reparse-layout-rejected')

    Install-Layout -Action InstallClean
    $replacementRequest = Join-Path $temporaryRoot 'replacement.request'
    $replacementCompleted = Join-Path $temporaryRoot 'replacement.completed'
    $coordinator = Start-AdminStep -Action WaitReplace -Additional @{
        ReplacementHelper = $tamperedHelper
        RequestPath = $replacementRequest
        CompletedPath = $replacementCompleted
    } -NoWait
    try {
        Invoke-DesktopFixture -Case 'between-validations-replacement-zero-launch' -Arguments @('--replacement', $replacementRequest, $replacementCompleted)
        $coordinator.WaitForExit()
        if ($coordinator.ExitCode -ne 0) { throw "The replacement coordinator failed with exit $($coordinator.ExitCode)." }
    }
    finally {
        $coordinator.Dispose()
    }

    $desktopSignature = Get-AuthenticodeSignature (Join-Path $desktopStage 'PCSteward.Desktop.exe')
    $helperSignature = Get-AuthenticodeSignature (Join-Path $helperStage 'PCSteward.ElevatedHelper.exe')
    $evidence = [pscustomobject]@{
        Outcome = 'PASS'
        RunId = $runId
        InstalledRoot = $installedRoot
        PrimarySigner = $primary.Thumbprint
        WrongSigner = $wrong.Thumbprint
        SameSignerFixture = $desktopSignature.SignerCertificate.Thumbprint -eq $helperSignature.SignerCertificate.Thumbprint
        CleanLayout = $cleanLayout
        Cases = $results
    }
}
finally {
    try {
        Start-AdminStep -Action Teardown
    }
    catch {
        Write-Warning "Evidence-root teardown failed: $_"
    }

    foreach ($certificate in $certificates) {
        try {
            Remove-EvidenceCertificate -Thumbprint $certificate.Thumbprint
        }
        catch {
            Write-Warning "Certificate teardown failed for $($certificate.Thumbprint): $_"
        }
        $certificate.Dispose()
    }

    if ([IO.Directory]::Exists($temporaryRoot)) {
        $resolvedTemporary = [IO.Path]::GetFullPath($temporaryRoot)
        $resolvedSystemTemporary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if ($resolvedTemporary.StartsWith($resolvedSystemTemporary, [StringComparison]::OrdinalIgnoreCase) -and
            -not $resolvedTemporary.Equals($resolvedSystemTemporary, [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
        }
    }
}

if ($null -eq $evidence) { throw 'The evidence run did not produce a result.' }
$certificateRemainders = @{}
foreach ($thumbprint in $certificateThumbprints) {
    $certificateRemainders[$thumbprint] = Count-EvidenceCertificate -Thumbprint $thumbprint
}
$teardown = [pscustomobject]@{
    InstalledRootAbsent = -not (Test-Path -LiteralPath $installedRoot)
    TemporaryRootAbsent = -not (Test-Path -LiteralPath $temporaryRoot)
    CertificateEntriesRemaining = $certificateRemainders
}
$evidence | Add-Member -NotePropertyName Teardown -NotePropertyValue $teardown
if (-not $teardown.InstalledRootAbsent -or
    -not $teardown.TemporaryRootAbsent -or
    @($certificateRemainders.Values | Where-Object { $_ -ne 0 }).Count -ne 0) {
    throw 'Evidence teardown verification failed.'
}

$evidence | ConvertTo-Json -Depth 7
if ($ResultPath) {
    $resolvedResult = [IO.Path]::GetFullPath($ResultPath)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resolvedResult)) | Out-Null
    [IO.File]::WriteAllText($resolvedResult, ($evidence | ConvertTo-Json -Depth 7))
}
