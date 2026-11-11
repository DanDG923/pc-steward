using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using PCSteward.Execution;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// ADR-034 §3 (amends ADR-018), TM-04/TM-13: the pinned release-signature check. Pure tests cover the compiled pins and the
/// empty-pin rule. Windows-native tests sign real PE copies with two throwaway self-signed code-signing keys that are never
/// added to any trust store, then prove the cases §3 names before the pinned check may replace the current rule: a correctly
/// pinned file is accepted, a tampered file, a file signed by another self-signed key, an unsigned file and a file signed by
/// a publicly trusted but unpinned certificate are rejected, and only the same pin on both files counts as the same signer.
/// On other platforms the Windows tests report <see cref="PlatformNotSupportedException"/> (never silently skipped).
/// </summary>
[TestClass]
public sealed class PinnedReleaseSignatureTests
{
    private static readonly string[] SignedRuntimeImages = ["System.Private.CoreLib.dll", "coreclr.dll", "clrjit.dll", "hostpolicy.dll"];
    private static readonly object FixtureLock = new();
    private static SignedFixtures? fixtures;

    [TestMethod]
    public void CompiledPinsAreWellFormedAndDistinctAndEmptyPinsAcceptNothing()
    {
        foreach (string compiled in ReleaseSigningPins.Compiled)
            Assert.IsTrue(ReleaseSigningPins.IsWellFormed(compiled), compiled);
        Assert.AreEqual(ReleaseSigningPins.Compiled.Count, ReleaseSigningPins.Compiled.Distinct(StringComparer.Ordinal).Count());
        // Until the owner's key session commits the public-key hashes, no key is pinned and the pinned check accepts no file.
        Assert.IsEmpty(ReleaseSigningPins.Primary);
        Assert.IsEmpty(ReleaseSigningPins.Backup);
        Assert.IsEmpty(ReleaseSigningPins.Compiled);
        Assert.IsFalse(AuthenticodeIdentity.TryReadPinnedSigner(typeof(object).Assembly.Location, [], out string? pin));
        Assert.IsNull(pin);
    }

    [TestMethod]
    [DataRow("", false)]
    [DataRow("ABC", false)]
    [DataRow("0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF", true)]
    [DataRow("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", false)]
    [DataRow("0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEG", false)]
    [DataRow("0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0", false)]
    public void OnlySixtyFourUppercaseHexCharactersAreAPin(string value, bool expected) =>
        Assert.AreEqual(expected, ReleaseSigningPins.IsWellFormed(value));

    [TestMethod]
    public void ACorrectlyPinnedFileIsAcceptedThoughWindowsDoesNotTrustItsKey()
    {
        SignedFixtures signed = Fixtures();
        Assert.IsFalse(AuthenticodeIdentity.IsTrusted(signed.PinnedA), "the throwaway key is in no trust store");
        Assert.IsTrue(AuthenticodeIdentity.TryReadPinnedSigner(signed.PinnedA, [signed.PinA], out string? pin));
        Assert.AreEqual(signed.PinA, pin);
        // The backup position matches as well as the primary.
        Assert.IsTrue(AuthenticodeIdentity.TryReadPinnedSigner(signed.PinnedA, [signed.PinB, signed.PinA], out pin));
        Assert.AreEqual(signed.PinA, pin);
    }

    [TestMethod]
    public void ATamperedFileIsRejected()
    {
        SignedFixtures signed = Fixtures();
        Assert.IsFalse(AuthenticodeIdentity.TryReadPinnedSigner(signed.TamperedA, [signed.PinA, signed.PinB], out string? pin));
        Assert.IsNull(pin);
    }

    [TestMethod]
    public void AFileSignedByAnotherSelfSignedKeyIsRejectedUntilThatKeyIsPinned()
    {
        SignedFixtures signed = Fixtures();
        Assert.IsFalse(AuthenticodeIdentity.TryReadPinnedSigner(signed.SignedB, [signed.PinA], out string? pin));
        Assert.IsNull(pin);
        Assert.IsTrue(AuthenticodeIdentity.TryReadPinnedSigner(signed.SignedB, [signed.PinA, signed.PinB], out pin));
        Assert.AreEqual(signed.PinB, pin);
    }

    [TestMethod]
    public void AnUnsignedFileIsRejected()
    {
        SignedFixtures signed = Fixtures();
        Assert.IsFalse(AuthenticodeIdentity.TryReadPinnedSigner(signed.Unsigned, [signed.PinA, signed.PinB], out _));
        Assert.IsFalse(AuthenticodeIdentity.TryReadPinnedSigner(Path.Combine(signed.Directory, "missing.dll"), [signed.PinA], out _));
    }

    [TestMethod]
    public void APubliclyTrustedButUnpinnedFileIsRejectedEvenWhenItsLeafKeyIsListed()
    {
        SignedFixtures signed = Fixtures();
        string image = PubliclySignedRuntimeImage(out string leafPin);
        Assert.IsTrue(AuthenticodeIdentity.IsTrusted(image), "fixture: Windows trusts the runtime image");
        Assert.IsFalse(AuthenticodeIdentity.TryReadPinnedSigner(image, [signed.PinA, signed.PinB], out _));
        // Step 3: the chain must be the single self-signed certificate, so a CA-issued leaf never passes, even if listed.
        Assert.IsFalse(AuthenticodeIdentity.TryReadPinnedSigner(image, [leafPin], out _));
    }

    [TestMethod]
    public void OnlyTheSamePinOnBothFilesIsTheSameSigner()
    {
        SignedFixtures signed = Fixtures();
        Assert.IsTrue(AuthenticodeIdentity.HaveSamePinnedSigner(signed.PinnedA, signed.SecondPinnedA, [signed.PinA]));
        Assert.IsFalse(AuthenticodeIdentity.HaveSamePinnedSigner(signed.PinnedA, signed.SignedB, [signed.PinA, signed.PinB]));
        Assert.IsFalse(AuthenticodeIdentity.HaveSamePinnedSigner(signed.PinnedA, signed.TamperedA, [signed.PinA]));
        Assert.IsFalse(AuthenticodeIdentity.HaveSamePinnedSigner(signed.PinnedA, signed.Unsigned, [signed.PinA]));
        Assert.IsFalse(AuthenticodeIdentity.HaveSamePinnedSigner(signed.PinnedA, signed.SecondPinnedA, []));
    }

    [TestMethod]
    public void ProductionAcceptsNoThrowawayKeyAndTheAdr022RuleStillNeedsWindowsTrust()
    {
        // The compiled pins are empty, so the production pair check falls back to the ADR-022 rule, which needs Windows trust.
        SignedFixtures signed = Fixtures();
        Assert.IsFalse(AuthenticodeIdentity.IsTrustedAndSameSigner(signed.PinnedA, signed.SecondPinnedA));
        Assert.IsFalse(AuthenticodeIdentity.HaveSamePinnedSigner(signed.PinnedA, signed.SecondPinnedA, ReleaseSigningPins.Compiled));
    }

    [ClassCleanup]
    public static void RemoveFixtures()
    {
        lock (FixtureLock)
        {
            if (fixtures is not null && Directory.Exists(fixtures.Directory)) Directory.Delete(fixtures.Directory, recursive: true);
            fixtures = null;
        }
    }

    private static SignedFixtures Fixtures()
    {
        RequireWindows("ADR-034 §3 pinned signature fixtures");
        lock (FixtureLock)
        {
            return fixtures ??= SignedFixtures.Create();
        }
    }

    /// <summary>A .NET runtime image carrying an embedded Microsoft Authenticode signature, and its leaf key's pin.</summary>
    private static string PubliclySignedRuntimeImage(out string leafPin)
    {
        string runtime = RuntimeEnvironment.GetRuntimeDirectory();
        foreach (string candidate in SignedRuntimeImages.Select(name => Path.Combine(runtime, name)))
        {
            if (!File.Exists(candidate)) continue;
            try
            {
#pragma warning disable SYSLIB0057
                using X509Certificate signer = X509Certificate.CreateFromSignedFile(candidate);
                using X509Certificate2 leaf = new(signer);
#pragma warning restore SYSLIB0057
                leafPin = SignedFixtures.PinOf(leaf);
                return candidate;
            }
            catch (CryptographicException)
            {
                // Not embedded-signed; try the next image.
            }
        }
        Assert.Inconclusive("No embedded-signed runtime image is available on this host.");
        throw new InvalidOperationException("unreachable");
    }

    private static void RequireWindows(string evidence)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException($"Windows-native test ({evidence}): it runs on Windows CI and is reported as failed on other platforms.");
    }

    /// <summary>
    /// Copies of this test assembly (a real PE image): two signed with key A, one signed with key B, one tampered after
    /// signing with A, and one left unsigned. Windows PowerShell 5.1 creates both keys in <c>CurrentUser\My</c> (non-exportable,
    /// RSA 3072, code-signing EKU), signs with <c>Set-AuthenticodeSignature -HashAlgorithm SHA256</c> as the release script
    /// will, exports the public certificates and removes both keys before returning. No trust store is touched.
    /// </summary>
    internal sealed record SignedFixtures(
        string Directory, string PinnedA, string SecondPinnedA, string SignedB, string TamperedA, string Unsigned, string PinA, string PinB)
    {
        internal static SignedFixtures Create()
        {
            string directory = Path.Combine(Path.GetTempPath(), $"pcsteward-pinned-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            string source = typeof(PinnedReleaseSignatureTests).Assembly.Location;
            string pinnedA = Copy(source, directory, "pinned-a.dll");
            string secondA = Copy(source, directory, "pinned-a-2.dll");
            string signedB = Copy(source, directory, "signed-b.dll");
            string tampered = Copy(source, directory, "tampered-a.dll");
            string unsigned = Copy(source, directory, "unsigned.dll");
            string certA = Path.Combine(directory, "a.cer");
            string certB = Path.Combine(directory, "b.cer");
            RunSigningScript(directory, [pinnedA, secondA, tampered], [signedB], certA, certB);

            // Change one byte inside the image, well before the appended signature, so only the digest no longer matches.
            byte[] bytes = File.ReadAllBytes(tampered);
            bytes[bytes.Length / 4] ^= 0x5A;
            File.WriteAllBytes(tampered, bytes);

            using X509Certificate2 a = X509CertificateLoader.LoadCertificateFromFile(certA);
            using X509Certificate2 b = X509CertificateLoader.LoadCertificateFromFile(certB);
            return new(directory, pinnedA, secondA, signedB, tampered, unsigned, PinOf(a), PinOf(b));
        }

        internal static string PinOf(X509Certificate2 certificate) =>
            Convert.ToHexString(SHA256.HashData(certificate.PublicKey.ExportSubjectPublicKeyInfo()));

        private static string Copy(string source, string directory, string leaf)
        {
            string target = Path.Combine(directory, leaf);
            File.Copy(source, target);
            return target;
        }

        private static void RunSigningScript(string directory, string[] signWithA, string[] signWithB, string certA, string certB)
        {
            static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
            static string List(IEnumerable<string> values) => "@(" + string.Join(",", values.Select(Quote)) + ")";
            string script = $$"""
                $ErrorActionPreference = 'Stop'
                # Started from pwsh 7, the inherited module path would load PowerShell 7 modules: use only this host's own.
                $env:PSModulePath = Join-Path $PSHOME 'Modules'
                $made = @()
                try {
                    foreach ($pair in @(@({{List(signWithA)}}, {{Quote(certA)}}), @({{List(signWithB)}}, {{Quote(certB)}}))) {
                        $certificate = New-SelfSignedCertificate -Type CodeSigningCert -Subject ('CN=PC Steward pinned-check fixture ' + [Guid]::NewGuid().ToString('N')) -CertStoreLocation 'Cert:\CurrentUser\My' -KeyExportPolicy NonExportable -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -NotAfter (Get-Date).AddDays(1)
                        $made += $certificate
                        foreach ($file in $pair[0]) {
                            $null = Set-AuthenticodeSignature -FilePath $file -Certificate $certificate -HashAlgorithm SHA256
                            $observed = Get-AuthenticodeSignature -FilePath $file
                            if ($null -eq $observed.SignerCertificate -or $observed.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) {
                                throw "fixture signing did not embed the expected signer in $file (status $($observed.Status))"
                            }
                        }
                        $null = Export-Certificate -Cert $certificate -FilePath $pair[1] -Type CERT
                    }
                }
                finally {
                    foreach ($certificate in $made) { Remove-Item -LiteralPath ('Cert:\CurrentUser\My\' + $certificate.Thumbprint) -DeleteKey -ErrorAction SilentlyContinue }
                }
                """;
            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            ProcessStartInfo start = new(powershell)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = directory,
            };
            foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand",
                Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
                start.ArgumentList.Add(argument);
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("Windows PowerShell did not start.");
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(120_000))
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail("Fixture signing exceeded two minutes.");
            }
            process.WaitForExit();
            Assert.AreEqual(0, process.ExitCode, $"Fixture signing failed.{Environment.NewLine}{output.Result}{Environment.NewLine}{error.Result}");
        }
    }
}
