using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32.SafeHandles;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/13; Task 50 design rev 6 §3 step 2 (L3), Windows-only: Authenticode trust of a held handle. An
/// unsigned file, a closed handle and a non-canonical path are untrusted. A copy of this test host's own (Microsoft-signed)
/// image keeps its embedded signature: when it verifies, its signer is read from the verified chain and is not the
/// Chocolatey signer, so the engine check still refuses; flipping one byte of that copy breaks verification.
/// </summary>
[TestClass]
public sealed class ChocolateyEngineTrustTests
{
    [TestMethod]
    public void AnUnsignedFileAClosedHandleOrANonCanonicalPathIsUntrusted()
    {
        using N1NativeFixture fixture = new();
        string path = fixture.File("unsigned.exe", "MZ not a signed image");
        using (SafeFileHandle handle = N1NativeFixture.Open(path, directory: false, out _))
        {
            Assert.IsFalse(ChocolateyEngineTrust.TryVerifyEmbeddedSignature(handle, path, out string? signer));
            Assert.IsNull(signer);
            Assert.IsFalse(ChocolateyEngineTrust.IsTrustedEngine(handle, path));
            Assert.IsFalse(ChocolateyEngineTrust.TryVerifyEmbeddedSignature(handle, path.ToLowerInvariant().Replace('\\', '/'), out _));
        }
        SafeFileHandle closed = N1NativeFixture.Open(path, directory: false, out _);
        closed.Dispose();
        Assert.IsFalse(ChocolateyEngineTrust.TryVerifyEmbeddedSignature(closed, path, out _));
        Assert.IsFalse(ChocolateyEngineTrust.TryReadFileVersion(closed, path, out _));
    }

    [TestMethod]
    public void ASignedNonChocolateyImageIsReadFromTheVerifiedChainAndStillRefusedAndATamperedCopyFails()
    {
        using N1NativeFixture fixture = new();
        string source = Environment.ProcessPath ?? throw new AssertFailedException("no process path");
        string copy = fixture.PathOf("signed-copy.exe");
        File.Copy(source, copy);
        bool verified;
        using (SafeFileHandle handle = N1NativeFixture.Open(copy, directory: false, out _))
        {
            verified = ChocolateyEngineTrust.TryVerifyEmbeddedSignature(handle, copy, out string? signer);
            if (verified) Assert.IsFalse(string.IsNullOrEmpty(signer));
            Assert.AreNotEqual(ChocolateyEngineTrust.ExpectedSignerSimpleName, signer);
            Assert.IsFalse(ChocolateyEngineTrust.IsTrustedEngine(handle, copy), "only the Chocolatey signer at 2.7.5.0 is trusted");
            Assert.IsTrue(ChocolateyEngineTrust.TryReadFileVersion(handle, copy, out string? version));
            Assert.AreEqual(4, version!.Split('.').Length);
        }
        if (!verified) return;
        byte[] bytes = File.ReadAllBytes(copy);
        bytes[bytes.Length / 2] ^= 0xFF;
        File.WriteAllBytes(copy, bytes);
        using SafeFileHandle tampered = N1NativeFixture.Open(copy, directory: false, out _);
        Assert.IsFalse(ChocolateyEngineTrust.TryVerifyEmbeddedSignature(tampered, copy, out _), "the signature binds the held bytes");
    }
}
