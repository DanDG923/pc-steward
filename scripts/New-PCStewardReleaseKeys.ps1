[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Primary', 'Backup', 'BackupCopy')][string]$Key,
    # Backup only: the one or two removable drives (for example E:\ and F:\) that each receive the password-protected PFX.
    [string[]]$BackupDrives,
    # BackupCopy only: the removable drive that holds the backup PFX, and the different removable drive that receives a copy.
    [string]$CopyFrom,
    [string]$CopyTo,
    # BackupCopy only: the backup pin the copy must carry, when ReleaseSigningPins.cs at this commit has none yet.
    [string]$BackupPin,
    # Primary only: ykman.exe, if it is not in one of the YubiKey Manager folders under Program Files. It must carry a valid
    # Yubico signature wherever it is.
    [string]$YkmanPath,
    # Primary only: if slot 9c holds only this script's own unfinished attempt (its one-day placeholder certificate for the
    # slot key, under the release subject), delete that attempt and start again. Anything else in the slot is still refused.
    [switch]$DiscardUnfinished
)

# ADR-034 §2 (FR-UPD-15, TM-04/13): create one of PC Steward's two release keys on the owner's PC, and print its pin (the
# SHA-256 of its public key) for ReleaseSigningPins.cs. Run it in Windows PowerShell 5.1 as the owner, not elevated.
#
#   -Key Primary  The key pair is generated inside the YubiKey's PIV slot 9c (Digital Signature), so the private key never
#                 leaves the device. PIN policy ALWAYS and touch policy ALWAYS: every signature needs the PIN and a touch.
#                 RSA 3072 on firmware 5.7 or later, otherwise RSA 2048. The certificate (CN=PC Steward Release Signing,
#                 code signing only, not a CA, ten years) is signed by the slot key itself and stored in the slot; Windows
#                 shows it in the personal store while the YubiKey is plugged in. Refuses if slot 9c already holds a key.
#   -Key Backup   An RSA 3072 key (CN=PC Steward Release Signing (Backup)) is created in the personal store, exported once
#                 to a password-protected PFX on each of one or two removable drives, checked from each drive, and
#                 deleted from the PC. Keep the drives apart from each other and from the YubiKey.
#   -Key BackupCopy  Copies the backup PFX from one removable drive to a second (owner, 2026-11-11: one drive now, the second
#                 copy later). The copy is written only if the source opens with the password and carries the backup pin
#                 (from ReleaseSigningPins.cs, or -BackupPin before the pins are committed); it is then read back, compared
#                 byte for byte, and checked to sign. The key never enters a certificate store.
#
# Neither key is added to Root, TrustedPublisher or any other trust store, and nothing is sent anywhere. The output holds
# only the pins and the certificate subjects, which contain no personal data.
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Run this script in Windows PowerShell 5.1 (powershell.exe).' }
foreach ($module in @('Management', 'Utility', 'Security')) { Import-Module ([IO.Path]::Combine($PSHOME, 'Modules', "Microsoft.PowerShell.$module", "Microsoft.PowerShell.$module.psd1")) -Force }
. (Join-Path $PSScriptRoot 'PCStewardReleaseSigning.ps1')
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this script from a normal (not elevated) PowerShell window.' }

$primarySubject = 'CN=PC Steward Release Signing'
$backupSubject = 'CN=PC Steward Release Signing (Backup)'
$codeSigning = '1.3.6.1.5.5.7.3.3'
$backupFileName = 'PCSteward-Release-Backup-Key.pfx'
$work = Join-Path ([IO.Path]::GetTempPath()) ('pcsteward-keys-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null

# Signs a small script with $Certificate and checks it with the §3 rules, so the key is proven to sign before its pin is used.
function Test-PCStewardKeySigns([Security.Cryptography.X509Certificates.X509Certificate2]$Certificate, [string]$Pin) {
    $probe = Join-Path $work 'signing-probe.ps1'
    [IO.File]::WriteAllText($probe, "# PC Steward release key signing probe`r`n", [Text.UTF8Encoding]::new($true))
    $null = Set-AuthenticodeSignature -FilePath $probe -Certificate $Certificate -HashAlgorithm SHA256
    $null = Test-PCStewardPinnedSignature -Path $probe -Pins @($Pin)
    Remove-Item -LiteralPath $probe -Force
}

# The root of a ready removable drive (for example E:\), or a refusal.
function Get-PCStewardRemovableRoot([string]$Drive) {
    if (!$Drive) { throw 'Name a removable drive, for example E:\' }
    $root = [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($Drive))
    $info = [IO.DriveInfo]::new($root)
    if (!$info.IsReady -or $info.DriveType -ne [IO.DriveType]::Removable) { throw "$root is not a ready removable drive." }
    $root
}

# A backup PFX: the password opens it in memory only (never a store), it holds the private key, its certificate carries
# $Pin, and the private key signs data that the certificate's public key verifies.
function Test-PCStewardBackupPfx([byte[]]$Bytes, [Security.SecureString]$Password, [string]$Pin, [string]$Name) {
    $copy = [Security.Cryptography.X509Certificates.X509Certificate2]::new($Bytes, $Password, [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
    try {
        if ($copy.Subject -cne $backupSubject -or !$copy.HasPrivateKey -or (Get-PCStewardKeyPin -CertificateDer $copy.RawData) -cne $Pin) { throw "The backup on $Name does not hold the backup key." }
        $data = [Text.Encoding]::UTF8.GetBytes('PC Steward backup key check')
        $private = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($copy)
        $public = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey($copy)
        $signature = $private.SignData($data, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        if (!$public.VerifyData($data, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)) { throw "The backup on $Name does not sign." }
    }
    finally { $copy.Dispose() }
}

function Get-PCStewardStoreCertificates([string]$Subject) {
    @(Get-ChildItem -LiteralPath 'Cert:\CurrentUser\My' | Where-Object { $_.Subject -ceq $Subject })
}

try {
    if ($Key -eq 'Primary') {
        if ($BackupDrives -or $CopyFrom -or $CopyTo -or $BackupPin) { throw '-BackupDrives, -CopyFrom, -CopyTo and -BackupPin are not for -Key Primary.' }
        # Never from PATH, which any program running as the owner can change: an administrator-only install folder, or the
        # path the owner names, and in both cases a Yubico signature that Windows trusts.
        $programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
        $ykman = $YkmanPath
        if (!$ykman) { $ykman = @(@('Yubico\YubiKey Manager CLI\ykman.exe', 'Yubico\YubiKey Manager\ykman.exe') | ForEach-Object { Join-Path $programFiles $_ } | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })[0] }
        if (!$ykman -or !(Test-Path -LiteralPath $ykman -PathType Leaf)) { throw 'ykman.exe was not found. Install the YubiKey Manager CLI from Yubico, or pass -YkmanPath.' }
        $ykman = [IO.Path]::GetFullPath($ykman)
        $ykmanSignature = Get-AuthenticodeSignature -LiteralPath $ykman
        if ($ykmanSignature.Status -ne 'Valid' -or $null -eq $ykmanSignature.SignerCertificate -or $ykmanSignature.SignerCertificate.Subject -notmatch '(^|, )O=Yubico AB(,|$)') { throw "$ykman does not carry a valid Yubico signature." }
        # Windows PowerShell 5.1 turns a native command's stderr into a terminating error under 'Stop', so capture it as text.
        function Invoke-YkmanRaw([string[]]$Arguments) {
            $previous = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
            try { $output = @(& $ykman @Arguments 2>&1 | ForEach-Object { "$_" }); [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output } }
            finally { $ErrorActionPreference = $previous }
        }
        function Invoke-Ykman([string[]]$Arguments) {
            $result = Invoke-YkmanRaw $Arguments
            if ($result.ExitCode -ne 0) { throw "ykman $($Arguments -join ' ') failed: $($result.Output -join ' ')" }
            $result.Output
        }
        $serials = @(Invoke-Ykman @('list', '--serials') | Where-Object { $_ -match '^\d+$' })
        if ($serials.Count -ne 1) { throw "Plug in exactly one YubiKey (found $($serials.Count))." }
        $device = @('--device', $serials[0])
        $firmware = [string](@(Invoke-Ykman ($device + @('info'))) | Where-Object { $_ -match '^Firmware version:\s*(\d+)\.(\d+)\.(\d+)' } | Select-Object -First 1)
        if ($firmware -notmatch '^Firmware version:\s*(\d+)\.(\d+)\.(\d+)') { throw 'Could not read the YubiKey firmware version.' }
        $firmware57 = [int]$Matches[1] -gt 5 -or ([int]$Matches[1] -eq 5 -and [int]$Matches[2] -ge 7)
        $algorithm = if ($firmware57) { 'RSA3072' } else { 'RSA2048' }

        # The factory PIN, PUK and management key are public; they are changed before the key exists. ykman asks for each.
        $info = @(Invoke-Ykman ($device + @('piv', 'info')))
        if ($info -match 'default PIN') { Write-Output 'Choose a new YubiKey PIV PIN (6-8 characters).'; & $ykman @($device + @('piv', 'access', 'change-pin')); if ($LASTEXITCODE -ne 0) { throw 'The PIN was not changed.' } }
        if ($info -match 'default PUK') { Write-Output 'Choose a new YubiKey PIV PUK (6-8 characters).'; & $ykman @($device + @('piv', 'access', 'change-puk')); if ($LASTEXITCODE -ne 0) { throw 'The PUK was not changed.' } }
        if ($info -match 'default management key') {
            Write-Output 'Replacing the default management key with a random one kept on the YubiKey behind your PIN.'
            & $ykman @($device + @('piv', 'access', 'change-management-key', '--generate', '--protect'))
            if ($LASTEXITCODE -ne 0) { throw 'The management key was not changed.' }
        }
        $info = @(Invoke-Ykman ($device + @('piv', 'info')))
        if ($info -match 'default (PIN|PUK|management key)') { throw 'The YubiKey still uses a factory PIN, PUK or management key.' }
        # Never overwrite slot 9c: no certificate listed there, and (firmware 5.3 and later) no key metadata either.
        $keyPresent = (Invoke-YkmanRaw ($device + @('piv', 'keys', 'info', '9c'))).ExitCode -eq 0
        if ($DiscardUnfinished -and $keyPresent) {
            # Only this script's own unfinished attempt: the slot's certificate is the one-day placeholder under the release
            # subject, and it carries the slot's own public key. A real release certificate (ten years) is never touched.
            $slotCertificateFile = Join-Path $work 'unfinished-certificate.pem'
            $slotPublicFile = Join-Path $work 'unfinished-public.pem'
            $haveCertificate = (Invoke-YkmanRaw ($device + @('piv', 'certificates', 'export', '--format', 'PEM', '9c', $slotCertificateFile))).ExitCode -eq 0
            $havePublic = (Invoke-YkmanRaw ($device + @('piv', 'keys', 'export', '--format', 'PEM', '9c', $slotPublicFile))).ExitCode -eq 0
            if ($haveCertificate -and $havePublic) {
                $unfinished = [Security.Cryptography.X509Certificates.X509Certificate2]::new($slotCertificateFile)
                try {
                    $slotSpki = [Convert]::FromBase64String(((Get-Content -LiteralPath $slotPublicFile) | Where-Object { $_ -notmatch '^-----' }) -join '')
                    $slotSha = [Security.Cryptography.SHA256]::Create()
                    try { $slotPin = [BitConverter]::ToString($slotSha.ComputeHash($slotSpki)).Replace('-', '') } finally { $slotSha.Dispose() }
                    $isPlaceholder = $unfinished.Subject -ceq $primarySubject -and ($unfinished.NotAfter - $unfinished.NotBefore).TotalDays -le 2 -and (Get-PCStewardKeyPin -CertificateDer $unfinished.RawData) -ceq $slotPin
                }
                finally { $unfinished.Dispose() }
                if ($isPlaceholder) {
                    Write-Output 'Slot 9c holds the unfinished attempt from an earlier run (nothing uses it). Deleting it to start again: enter your PIN if asked.'
                    # Not captured: ykman asks for the PIN, which unlocks the management key kept on the YubiKey.
                    & $ykman @($device + @('piv', 'certificates', 'delete', '9c'))
                    if ($LASTEXITCODE -ne 0) { throw 'The unfinished certificate was not deleted.' }
                    # Firmware 5.7 and later can delete the key itself; older firmware replaces it when the new key is generated.
                    if ($firmware57) { & $ykman @($device + @('piv', 'keys', 'delete', '9c')); if ($LASTEXITCODE -ne 0) { throw 'The unfinished key was not deleted.' } }
                    foreach ($stale in @(Get-PCStewardStoreCertificates $primarySubject | Where-Object { (Get-PCStewardKeyPin -CertificateDer $_.RawData) -ceq $slotPin })) { Remove-Item -LiteralPath ('Cert:\CurrentUser\My\' + $stale.Thumbprint) }
                    $keyPresent = $firmware57 -and (Invoke-YkmanRaw ($device + @('piv', 'keys', 'info', '9c'))).ExitCode -eq 0
                    $info = @(Invoke-Ykman ($device + @('piv', 'info')))
                }
            }
        }
        if ($keyPresent -or ($info -join "`n") -match '(?m)^Slot 9C') { throw 'Slot 9c already holds a key or certificate. This script never replaces one; stop and ask.' }

        Write-Output "Generating the $algorithm key inside the YubiKey (slot 9c). Enter your PIN, then touch the YubiKey when it blinks."
        $publicPem = Join-Path $work 'primary-public.pem'
        & $ykman @($device + @('piv', 'keys', 'generate', '--algorithm', $algorithm, '--pin-policy', 'ALWAYS', '--touch-policy', 'ALWAYS', '9c', $publicPem))
        if ($LASTEXITCODE -ne 0) { throw 'Key generation failed.' }
        $spki = [Convert]::FromBase64String(((Get-Content -LiteralPath $publicPem) | Where-Object { $_ -notmatch '^-----' }) -join '')
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $pin = [BitConverter]::ToString($sha.ComputeHash($spki)).Replace('-', '') } finally { $sha.Dispose() }

        # A short-lived placeholder certificate lets Windows reach the slot key; the slot key then signs the real one.
        Write-Output 'Creating a placeholder certificate. Enter your PIN and touch the YubiKey.'
        & $ykman @($device + @('piv', 'certificates', 'generate', '--subject', $primarySubject, '--valid-days', '1', '9c', $publicPem))
        if ($LASTEXITCODE -ne 0) { throw 'The placeholder certificate was not created.' }
        function Wait-PCStewardCardCertificate([string]$ExpectedPin, [string]$CertificateFile) {
            for ($attempt = 0; $attempt -lt 30; $attempt++) {
                $found = @(Get-PCStewardStoreCertificates $primarySubject | Where-Object { $_.HasPrivateKey -and (Get-PCStewardKeyPin -CertificateDer $_.RawData) -ceq $ExpectedPin })
                if ($found.Count -ge 1) { return $found }
                Start-Sleep -Seconds 1
            }
            # Certificate propagation did not run: add the certificate and link it to the card key.
            $imported = Import-Certificate -FilePath $CertificateFile -CertStoreLocation 'Cert:\CurrentUser\My'
            & certutil.exe -user -repairstore My $imported.SerialNumber | Out-Null
            if ($LASTEXITCODE -ne 0) { Remove-Item -LiteralPath ('Cert:\CurrentUser\My\' + $imported.Thumbprint); return @() }
            @(Get-PCStewardStoreCertificates $primarySubject | Where-Object { $_.HasPrivateKey -and (Get-PCStewardKeyPin -CertificateDer $_.RawData) -ceq $ExpectedPin })
        }
        $placeholderFile = Join-Path $work 'placeholder.pem'
        Invoke-Ykman ($device + @('piv', 'certificates', 'export', '--format', 'PEM', '9c', $placeholderFile)) | Out-Null
        $placeholder = @(Wait-PCStewardCardCertificate $pin $placeholderFile)
        if ($placeholder.Count -lt 1) { throw 'Windows does not show the YubiKey key. Install the YubiKey Minidriver from Yubico, replug the YubiKey and run this again.' }

        # The real certificate: code signing only, not a CA, ten years, signed by the slot key (PIN and touch).
        Write-Output 'Signing the release certificate with the YubiKey key. Enter your PIN and touch the YubiKey.'
        $rsa = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($placeholder[0])
        $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new($primarySubject, $rsa, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true))
        $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new([Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature, $true))
        $usages = [Security.Cryptography.OidCollection]::new(); [void]$usages.Add([Security.Cryptography.Oid]::new($codeSigning))
        $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($usages, $false))
        $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension]::new($request.PublicKey, $false))
        $now = [DateTimeOffset]::UtcNow
        # Create with a signature generator: the certificate only needs the slot key's signature. The self-signed shortcut
        # also attaches the private key to the result (CopyWithPrivateKey), which Windows refuses for a smart-card key
        # (0x80090027 on the owner's PC, 2026-11-11, while the same key signed data and a certificate without it).
        $serial = [byte[]]::new(16)
        $random = [Security.Cryptography.RandomNumberGenerator]::Create()
        try { $random.GetBytes($serial) } finally { $random.Dispose() }
        $serial[0] = $serial[0] -band 0x7F
        if ($serial[0] -eq 0) { $serial[0] = 1 }
        $generator = [Security.Cryptography.X509Certificates.X509SignatureGenerator]::CreateForRSA($rsa, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $signed = $request.Create([Security.Cryptography.X509Certificates.X500DistinguishedName]::new($primarySubject), $generator, $now.AddMinutes(-5), $now.AddYears(10), $serial)
        $certificateFile = Join-Path $work 'primary.cer'
        [IO.File]::WriteAllBytes($certificateFile, $signed.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
        if ((Get-PCStewardKeyPin -CertificateDer $signed.RawData) -cne $pin) { throw 'The new certificate does not carry the slot key.' }

        Write-Output 'Storing the release certificate in slot 9c. Enter your PIN if asked.'
        & $ykman @($device + @('piv', 'certificates', 'import', '9c', $certificateFile))
        if ($LASTEXITCODE -ne 0) { throw 'The release certificate was not stored on the YubiKey.' }
        # The placeholder leaves the personal store (the certificate only; the key stays on the YubiKey).
        foreach ($old in @(Get-PCStewardStoreCertificates $primarySubject | Where-Object { $_.Thumbprint -ne $signed.Thumbprint })) { Remove-Item -LiteralPath ('Cert:\CurrentUser\My\' + $old.Thumbprint) }
        $final = @(Wait-PCStewardCardCertificate $pin $certificateFile | Where-Object { $_.Thumbprint -eq $signed.Thumbprint })
        if ($final.Count -ne 1) { throw 'Windows does not show the release certificate with the YubiKey key.' }
        Write-Output 'Proving the key signs: enter your PIN and touch the YubiKey once more.'
        Test-PCStewardKeySigns $final[0] $pin
        # Yubico's attestation of slot 9c, signed by the YubiKey's factory key (slot f9), which Yubico's own CA certifies.
        # It lets the pin be checked away from this PC before it is committed: the key was generated inside a YubiKey, with
        # PIN and touch policies ALWAYS. It names the YubiKey's serial number, so it is for that check only, never the repo.
        $attestationFile = Join-Path $work 'attestation.pem'
        $intermediateFile = Join-Path $work 'attestation-intermediate.pem'
        Invoke-Ykman ($device + @('piv', 'keys', 'attest', '--format', 'PEM', '9c', $attestationFile)) | Out-Null
        Invoke-Ykman ($device + @('piv', 'certificates', 'export', '--format', 'PEM', 'f9', $intermediateFile)) | Out-Null
        $attestation = [Security.Cryptography.X509Certificates.X509Certificate2]::new($attestationFile)
        try { if ((Get-PCStewardKeyPin -CertificateDer $attestation.RawData) -cne $pin) { throw 'The attestation is not for the new key.' } } finally { $attestation.Dispose() }
        [pscustomobject]@{
            key = 'Primary'; subject = $primarySubject; algorithm = $algorithm; pin = $pin; notAfter = $signed.NotAfter.ToString('yyyy-MM-dd'); pinLine = "internal const string Primary = `"$pin`";"
            attestation = [IO.File]::ReadAllText($attestationFile).Trim(); attestationIntermediate = [IO.File]::ReadAllText($intermediateFile).Trim()
        } | ConvertTo-Json
    }
    elseif ($Key -eq 'BackupCopy') {
        if ($YkmanPath -or $BackupDrives -or $DiscardUnfinished) { throw '-YkmanPath, -BackupDrives and -DiscardUnfinished are not for -Key BackupCopy.' }
        $from = Get-PCStewardRemovableRoot $CopyFrom
        $to = Get-PCStewardRemovableRoot $CopyTo
        if ($from -ieq $to) { throw '-CopyFrom and -CopyTo must be two different removable drives.' }
        $source = Join-Path $from $backupFileName
        $target = Join-Path $to $backupFileName
        if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "$source was not found." }
        if (Test-Path -LiteralPath $target) { throw "$target already exists. This script never overwrites a backup key." }
        # The pin the copy must carry: the committed one, or the owner's before the pins are committed; never a different one.
        $pinsSource = [IO.File]::ReadAllText([IO.Path]::Combine($PSScriptRoot, '..', 'src', 'PCSteward.Execution', 'ReleaseSigningPins.cs'))
        $committed = [regex]::Matches($pinsSource, 'internal const string Backup = "([0-9A-F]{64})";')
        $expected = if ($committed.Count -eq 1) { $committed[0].Groups[1].Value } else { '' }
        if ($BackupPin) {
            if ($BackupPin -cnotmatch '\A[0-9A-F]{64}\z') { throw '-BackupPin must be 64 uppercase hex characters.' }
            if ($expected -and $expected -cne $BackupPin) { throw '-BackupPin differs from the backup pin in ReleaseSigningPins.cs.' }
            $expected = $BackupPin
        }
        if (!$expected) { throw 'ReleaseSigningPins.cs at this commit has no backup pin; pass the pin the backup step printed with -BackupPin.' }

        $bytes = [IO.File]::ReadAllBytes($source)
        $password = Read-Host -AsSecureString 'Enter the backup key password'
        try {
            Test-PCStewardBackupPfx $bytes $password $expected $source
            # CreateNew: never overwrites, even if the file appeared since the check above.
            $stream = [IO.FileStream]::new($target, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
            $written = [IO.File]::ReadAllBytes($target)
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $same = [BitConverter]::ToString($sha.ComputeHash($bytes)) -ceq [BitConverter]::ToString($sha.ComputeHash($written)) } finally { $sha.Dispose() }
            if (!$same) { throw "The copy on $target differs from $source." }
            Test-PCStewardBackupPfx $written $password $expected $target
        }
        finally { $password.Dispose() }
        [pscustomobject]@{ key = 'BackupCopy'; subject = $backupSubject; pin = $expected; copiedTo = $to; result = 'The backup key is now on two drives. Keep them apart from each other and from the YubiKey.' } | ConvertTo-Json
    }
    else {
        if ($YkmanPath -or $CopyFrom -or $CopyTo -or $BackupPin -or $DiscardUnfinished) { throw '-YkmanPath, -CopyFrom, -CopyTo, -BackupPin and -DiscardUnfinished are not for -Key Backup.' }
        # One or two drives (owner, 2026-11-11: one drive now; -Key BackupCopy adds the second copy later).
        $drives = @($BackupDrives | ForEach-Object { Get-PCStewardRemovableRoot $_ } | Select-Object -Unique)
        if ($drives.Count -lt 1 -or $drives.Count -gt 2 -or $drives.Count -ne @($BackupDrives).Count) { throw 'Name one or two different removable drives with -BackupDrives, for example -BackupDrives E:\ or -BackupDrives E:\, F:\' }
        $targets = @($drives | ForEach-Object { Join-Path $_ $backupFileName })
        foreach ($target in $targets) { if (Test-Path -LiteralPath $target) { throw "$target already exists. This script never overwrites a backup key." } }
        if (@(Get-PCStewardStoreCertificates $backupSubject).Count -ne 0) { throw 'A backup key is already in the personal store. Stop and ask before going on.' }

        $password = Read-Host -AsSecureString 'Choose a password for the backup key (at least 12 characters)'
        $again = Read-Host -AsSecureString 'Type the password again'
        # Compared in unmanaged memory that is zeroed afterwards; the password never becomes a managed string.
        $first = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($password)
        $second = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($again)
        try {
            $length = $password.Length
            $same = $length -eq $again.Length
            for ($index = 0; $same -and $index -lt $length; $index++) { $same = [Runtime.InteropServices.Marshal]::ReadInt16($first, 2 * $index) -eq [Runtime.InteropServices.Marshal]::ReadInt16($second, 2 * $index) }
        }
        finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($first); [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($second) }
        if (!$same) { throw 'The passwords differ.' }
        if ($length -lt 12) { throw 'The password is shorter than 12 characters.' }

        $created = New-SelfSignedCertificate -Type CodeSigningCert -Subject $backupSubject -CertStoreLocation 'Cert:\CurrentUser\My' -KeyExportPolicy Exportable -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyUsage DigitalSignature -NotAfter (Get-Date).AddYears(10)
        try {
            $pin = Get-PCStewardKeyPin -CertificateDer $created.RawData
            Test-PCStewardKeySigns $created $pin
            foreach ($target in $targets) {
                $null = Export-PfxCertificate -Cert ('Cert:\CurrentUser\My\' + $created.Thumbprint) -FilePath $target -Password $password -CryptoAlgorithmOption AES256_SHA256 -NoProperties
                # Read back from the drive: the password opens it, the key is there, and it is the same key.
                $copy = [Security.Cryptography.X509Certificates.X509Certificate2]::new($target, $password, [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
                try {
                    if (!$copy.HasPrivateKey -or (Get-PCStewardKeyPin -CertificateDer $copy.RawData) -cne $pin) { throw "The backup on $target does not hold the key." }
                    $data = [Text.Encoding]::UTF8.GetBytes('PC Steward backup key check')
                    $private = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($copy)
                    $public = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey($created)
                    $signature = $private.SignData($data, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
                    if (!$public.VerifyData($data, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)) { throw "The backup on $target does not sign." }
                }
                finally { $copy.Dispose() }
            }
        }
        finally {
            # The key leaves the PC: only the two drives keep it.
            Remove-Item -LiteralPath ('Cert:\CurrentUser\My\' + $created.Thumbprint) -DeleteKey
            $password.Dispose(); $again.Dispose()
        }
        if (@(Get-PCStewardStoreCertificates $backupSubject).Count -ne 0) { throw 'The backup key is still in the personal store.' }
        $note = if ($targets.Count -eq 1) { 'Only one copy so far: run -Key BackupCopy when a second removable drive is available.' } else { 'Two copies. Keep the drives apart from each other and from the YubiKey.' }
        [pscustomobject]@{ key = 'Backup'; subject = $backupSubject; algorithm = 'RSA3072'; pin = $pin; notAfter = $created.NotAfter.ToString('yyyy-MM-dd'); copies = $targets.Count; note = $note; pinLine = "internal const string Backup = `"$pin`";" } | ConvertTo-Json
    }
}
finally {
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}
