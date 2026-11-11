# ADR-034 §2, §3 and §5 (FR-UPD-15, TM-04/13): functions shared by the release key script, the release signing script and
# the CI MSI job. Dot-source only. Windows PowerShell 5.1 and pwsh 7 both run them; nothing here touches a trust store.
#   Get-PCStewardKeyPin             the ADR-034 pin of a certificate: SHA-256 of its DER SubjectPublicKeyInfo, uppercase hex
#   Test-PCStewardPinnedSignature   the script-side form of the §3 check for one signed file
#   Invoke-PCStewardReleaseSigning  sign both executables, write the release manifest, build and sign the MSI, check it all

# Reads one DER tag-length header at $Offset; returns the header length and the content length. Definite lengths only.
function Read-PCStewardDerHeader([byte[]]$Bytes, [int]$Offset) {
    if ($Offset + 2 -gt $Bytes.Length) { throw 'The certificate DER is truncated.' }
    $first = [int]$Bytes[$Offset + 1]
    if ($first -lt 0x80) { return [pscustomobject]@{ Tag = [int]$Bytes[$Offset]; Header = 2; Length = $first } }
    $count = $first -band 0x7F
    if ($count -lt 1 -or $count -gt 3 -or $Offset + 2 + $count -gt $Bytes.Length) { throw 'The certificate DER has an unsupported length.' }
    $length = 0
    for ($index = 0; $index -lt $count; $index++) { $length = ($length -shl 8) -bor [int]$Bytes[$Offset + 2 + $index] }
    [pscustomobject]@{ Tag = [int]$Bytes[$Offset]; Header = 2 + $count; Length = $length }
}

# Pure: SHA-256 of the DER SubjectPublicKeyInfo of an X.509 certificate, as 64 uppercase hex characters (the value
# ReleaseSigningPins holds). Walks Certificate -> TBSCertificate -> [version] serial signature issuer validity subject SPKI.
function Get-PCStewardKeyPin {
    param([Parameter(Mandatory)][byte[]]$CertificateDer)
    $certificate = Read-PCStewardDerHeader $CertificateDer 0
    if ($certificate.Tag -ne 0x30 -or $certificate.Header + $certificate.Length -ne $CertificateDer.Length) { throw 'Not one DER certificate.' }
    $tbsOffset = $certificate.Header
    $tbs = Read-PCStewardDerHeader $CertificateDer $tbsOffset
    if ($tbs.Tag -ne 0x30) { throw 'The certificate has no TBSCertificate.' }
    $offset = $tbsOffset + $tbs.Header
    $end = $offset + $tbs.Length
    $element = Read-PCStewardDerHeader $CertificateDer $offset
    if ($element.Tag -eq 0xA0) { $offset += $element.Header + $element.Length }
    foreach ($expected in @(0x02, 0x30, 0x30, 0x30, 0x30)) {
        $element = Read-PCStewardDerHeader $CertificateDer $offset
        if ($element.Tag -ne $expected) { throw 'The TBSCertificate fields are not in X.509 order.' }
        $offset += $element.Header + $element.Length
    }
    $spki = Read-PCStewardDerHeader $CertificateDer $offset
    if ($spki.Tag -ne 0x30 -or $offset + $spki.Header + $spki.Length -gt $end) { throw 'The certificate has no SubjectPublicKeyInfo.' }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { [BitConverter]::ToString($sha.ComputeHash($CertificateDer, $offset, $spki.Header + $spki.Length)).Replace('-', '') } finally { $sha.Dispose() }
}

# The §3 check as a script can make it before anything is installed: Windows verified the signature's digest (status Valid,
# or UnknownError, which is how an untrusted root is reported), the signer chain is the one self-signed certificate, valid
# for code signing and failing only because Windows does not trust it, and its key pin is one of $Pins. Returns the pin.
function Test-PCStewardPinnedSignature {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string[]]$Pins)
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ([string]$signature.Status -notin @('Valid', 'UnknownError')) { throw "The signature on $Path is not intact: $($signature.Status) $($signature.StatusMessage)" }
    $signer = $signature.SignerCertificate
    if ($null -eq $signer) { throw "$Path has no signer certificate." }
    $chain = New-Object Security.Cryptography.X509Certificates.X509Chain
    try {
        $chain.ChainPolicy.RevocationMode = [Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
        $chain.ChainPolicy.VerificationTime = $signer.NotBefore.AddMinutes(1)
        [void]$chain.ChainPolicy.ApplicationPolicy.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.3'))
        [void]$chain.Build($signer)
        $problems = @($chain.ChainStatus | Where-Object { $_.Status -ne [Security.Cryptography.X509Certificates.X509ChainStatusFlags]::UntrustedRoot -and $_.Status -ne [Security.Cryptography.X509Certificates.X509ChainStatusFlags]::NoError })
        if ($chain.ChainElements.Count -ne 1 -or $problems.Count -ne 0) { throw "The signer of $Path is not one self-signed code-signing certificate: $(($problems | ForEach-Object { $_.Status }) -join ', ')" }
        # UnknownError is accepted only when the chain's one problem is the untrusted root.
        $untrustedRoot = @($chain.ChainStatus | Where-Object { $_.Status -eq [Security.Cryptography.X509Certificates.X509ChainStatusFlags]::UntrustedRoot }).Count -eq 1
    }
    finally { $chain.Dispose() }
    if ([string]$signature.Status -eq 'UnknownError' -and !$untrustedRoot) { throw "Windows reported $($signature.StatusMessage) for $Path." }
    $pin = Get-PCStewardKeyPin -CertificateDer $signer.RawData
    if (@($Pins | Where-Object { $_ -ceq $pin }).Count -ne 1) { throw "The signer of $Path carries pin $pin, which is not a release pin." }
    $pin
}

# ADR-034 §5: everything after the payload is in hand and checked. Signs the two executables with $Certificate (the
# YubiKey key, the backup key, or the CI job's throwaway key), writes the release manifest, builds the MSI with the pinned
# WiX SDK, signs the MSI, and checks all three signatures carry $Pin. A timestamp is added only when $TimestampServer is
# given and reachable; signing does not depend on it. Returns the MSI path and hashes.
function Invoke-PCStewardReleaseSigning {
    param(
        [Parameter(Mandatory)][string]$PayloadRoot,
        [Parameter(Mandatory)][Security.Cryptography.X509Certificates.X509Certificate2]$Certificate,
        [Parameter(Mandatory)][string]$Pin,
        [Parameter(Mandatory)][string]$ProductVersion,
        [Parameter(Mandatory)][string]$MsiPath,
        [string]$TimestampServer
    )
    $scripts = $PSScriptRoot
    if ((Get-PCStewardKeyPin -CertificateDer $Certificate.RawData) -cne $Pin) { throw 'The signing certificate does not carry the expected pin.' }
    $msiVersion = ($ProductVersion -split '-', 2)[0]
    function Set-PCStewardSignature([string]$Target) {
        $arguments = @{ FilePath = $Target; Certificate = $Certificate; HashAlgorithm = 'SHA256' }
        if ($TimestampServer) { $arguments.TimestampServer = $TimestampServer }
        $result = Set-AuthenticodeSignature @arguments
        if ($null -eq $result.SignerCertificate -or $result.SignerCertificate.Thumbprint -ne $Certificate.Thumbprint) {
            if ($TimestampServer) {
                Write-Warning "Signing $Target with a timestamp failed ($($result.StatusMessage)); signing without one."
                $result = Set-AuthenticodeSignature -FilePath $Target -Certificate $Certificate -HashAlgorithm SHA256
            }
            if ($null -eq $result.SignerCertificate -or $result.SignerCertificate.Thumbprint -ne $Certificate.Thumbprint) { throw "Signing $Target failed: $($result.StatusMessage)" }
        }
        $null = Test-PCStewardPinnedSignature -Path $Target -Pins @($Pin)
    }
    foreach ($leaf in @('PCSteward.Desktop.exe', 'PCSteward.ElevatedHelper.exe')) { Set-PCStewardSignature (Join-Path $PayloadRoot $leaf) }
    $manifest = & (Join-Path $scripts 'New-PCStewardReleaseManifest.ps1') -PayloadRoot $PayloadRoot -ProductVersion $ProductVersion | ConvertFrom-Json
    $msi = & (Join-Path $scripts 'New-PCStewardMsi.ps1') -PayloadRoot $PayloadRoot -MsiVersion $msiVersion -OutputPath $MsiPath | Select-Object -Last 1 | ConvertFrom-Json
    Set-PCStewardSignature $MsiPath
    $sha = [Security.Cryptography.SHA256]::Create(); $stream = [IO.File]::OpenRead($MsiPath)
    try { $signedHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') } finally { $stream.Dispose(); $sha.Dispose() }
    [pscustomobject]@{
        msiPath = $MsiPath; msiSha256 = $signedHash; msiVersion = $msi.msiVersion; productVersion = $ProductVersion
        manifestSha256 = $manifest.manifestSha256; fileCount = $manifest.fileCount; pin = $Pin
    }
}
