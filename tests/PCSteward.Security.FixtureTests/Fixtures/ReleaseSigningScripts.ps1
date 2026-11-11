[CmdletBinding()]
param([Parameter(Mandatory)][string]$RepositoryRoot, [Parameter(Mandatory)][string]$WorkRoot)

# ADR-034 §3 and §5 fixture for scripts/PCStewardReleaseSigning.ps1, run under Windows PowerShell 5.1 and pwsh 7 by
# ReleaseSigningScriptTests. Creates throwaway self-signed certificates in CurrentUser\My (never trusted, deleted at the
# end), and writes results.json: each certificate's DER and the script's pin for it, and what Test-PCStewardPinnedSignature
# says about signed, tampered, wrongly pinned, unsigned and non-code-signing files.
$ErrorActionPreference = 'Stop'
# The edition's own modules plus the system ones, where New-SelfSignedCertificate (PKI) lives for both editions.
$env:PSModulePath = (Join-Path $PSHOME 'Modules') + ';' + (Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell\v1.0\Modules')
Import-Module PKI
. (Join-Path $RepositoryRoot 'scripts\PCStewardReleaseSigning.ps1')

function Get-Outcome([scriptblock]$Action) {
    try { [pscustomobject]@{ accepted = $true; value = [string](& $Action) } }
    catch { [pscustomobject]@{ accepted = $false; value = $_.Exception.Message } }
}

$created = [Collections.Generic.List[string]]::new()
try {
    $certificates = [ordered]@{}
    foreach ($spec in @(
            @{ Name = 'rsa3072'; Arguments = @{ Type = 'CodeSigningCert'; KeyAlgorithm = 'RSA'; KeyLength = 3072 } },
            @{ Name = 'rsa2048'; Arguments = @{ Type = 'CodeSigningCert'; KeyAlgorithm = 'RSA'; KeyLength = 2048 } },
            @{ Name = 'ecdsa256'; Arguments = @{ Type = 'CodeSigningCert'; KeyAlgorithm = 'ECDSA_nistP256' } },
            @{ Name = 'notCodeSigning'; Arguments = @{ Type = 'Custom'; KeyAlgorithm = 'RSA'; KeyLength = 2048; TextExtension = @('2.5.29.37={text}1.3.6.1.5.5.7.3.1') } })) {
        $arguments = $spec.Arguments
        $certificate = New-SelfSignedCertificate @arguments -Subject ('CN=PC Steward fixture ' + $spec.Name + ' ' + [Guid]::NewGuid().ToString('N')) -CertStoreLocation 'Cert:\CurrentUser\My' -KeyExportPolicy NonExportable -HashAlgorithm SHA256 -NotAfter (Get-Date).AddDays(1)
        $created.Add($certificate.Thumbprint)
        $certificates[$spec.Name] = Get-Item -LiteralPath ('Cert:\CurrentUser\My\' + $certificate.Thumbprint)
    }
    $pins = [ordered]@{}
    foreach ($name in $certificates.Keys) { $pins[$name] = [ordered]@{ der = [Convert]::ToBase64String($certificates[$name].RawData); pin = Get-PCStewardKeyPin -CertificateDer $certificates[$name].RawData } }

    $primary = $certificates['rsa3072']
    $primaryPin = $pins['rsa3072'].pin
    function New-SignedProbe([string]$Name, $Certificate) {
        $path = Join-Path $WorkRoot "$Name.ps1"
        [IO.File]::WriteAllText($path, "# probe $Name`r`n'probe'`r`n", [Text.UTF8Encoding]::new($true))
        $null = Set-AuthenticodeSignature -FilePath $path -Certificate $Certificate -HashAlgorithm SHA256
        $path
    }
    $signed = New-SignedProbe 'signed' $primary
    $tampered = New-SignedProbe 'tampered' $primary
    $content = [IO.File]::ReadAllText($tampered)
    [IO.File]::WriteAllText($tampered, $content.Replace("'probe'", "'other'"), [Text.UTF8Encoding]::new($true))
    $otherKey = New-SignedProbe 'other-key' $certificates['rsa2048']
    $unsigned = Join-Path $WorkRoot 'unsigned.ps1'
    [IO.File]::WriteAllText($unsigned, "'probe'`r`n", [Text.UTF8Encoding]::new($true))
    $notCodeSigning = Join-Path $WorkRoot 'not-code-signing.ps1'
    [IO.File]::WriteAllText($notCodeSigning, "'probe'`r`n", [Text.UTF8Encoding]::new($true))
    # PowerShell may refuse to sign with a server-auth-only certificate; the file is then unsigned, which is also a refusal.
    $serverSigned = $null
    try { $serverSigned = Set-AuthenticodeSignature -FilePath $notCodeSigning -Certificate $certificates['notCodeSigning'] -HashAlgorithm SHA256 } catch { $serverSigned = $null }

    $checks = [ordered]@{
        signed = Get-Outcome { Test-PCStewardPinnedSignature -Path $signed -Pins @($primaryPin) }
        signedEitherPin = Get-Outcome { Test-PCStewardPinnedSignature -Path $signed -Pins @($pins['ecdsa256'].pin, $primaryPin) }
        tampered = Get-Outcome { Test-PCStewardPinnedSignature -Path $tampered -Pins @($primaryPin) }
        otherKey = Get-Outcome { Test-PCStewardPinnedSignature -Path $otherKey -Pins @($primaryPin) }
        unsigned = Get-Outcome { Test-PCStewardPinnedSignature -Path $unsigned -Pins @($primaryPin) }
        notCodeSigning = Get-Outcome { Test-PCStewardPinnedSignature -Path $notCodeSigning -Pins @($pins['notCodeSigning'].pin) }
        wrongCertificatePin = Get-Outcome { Invoke-PCStewardReleaseSigning -PayloadRoot $WorkRoot -Certificate $primary -Pin $pins['rsa2048'].pin -ProductVersion '0.1.0' -MsiPath (Join-Path $WorkRoot 'never.msi') }
    }
    $badDer = Get-Outcome { Get-PCStewardKeyPin -CertificateDer ([byte[]](0x30, 0x03, 0x02, 0x01, 0x01)) }
    $results = [ordered]@{ edition = $PSVersionTable.PSEdition; pins = $pins; checks = $checks; badDer = $badDer; notCodeSigningSigned = [bool]($serverSigned -and $serverSigned.SignerCertificate) }
    [IO.File]::WriteAllText((Join-Path $WorkRoot 'results.json'), ($results | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
    Write-Output 'PASS: release signing fixtures ran'
}
finally {
    foreach ($thumbprint in $created) { Remove-Item -LiteralPath ('Cert:\CurrentUser\My\' + $thumbprint) -DeleteKey -ErrorAction SilentlyContinue }
}
