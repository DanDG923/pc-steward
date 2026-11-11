using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.SelfUpdate;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// ADR-032 sections 2, 4 and 4b, TM-19 to TM-21, TM-26 and TM-28: the release manifest, its detached signature and the
/// candidate rules. Every rejection path has a fixture, and a manifest signed by a different ECDSA implementation proves the
/// signature format is not specific to .NET.
/// </summary>
[TestClass]
public sealed class SelfUpdateReleaseVerificationTests
{
    private const string Tag = "v0.2.0-beta.1";
    private static readonly RunningRelease Running = new(Version("0.1.0-private-beta"), 1);

    // ---- Happy paths ----

    [TestMethod]
    public void ManifestSignedByThePrimaryKeyIsANewerRelease()
    {
        using TestKey primary = new("primary");
        using TestKey backup = new("backup");
        ReleaseCheckResult result = Check(primary, Manifest(primary.Key.KeyId), [primary.Key, backup.Key]);

        Assert.AreEqual(ReleaseCheckOutcome.NewerRelease, result.Outcome);
        Assert.AreEqual(ReleaseRefusal.None, result.Refusal);
        Assert.AreEqual("0.2.0-beta.1", result.Manifest!.Version.Text);
        Assert.AreEqual(2, result.Manifest.Sequence);
        Assert.AreEqual(new string('a', 64), result.Manifest.AssetSha256);
    }

    [TestMethod]
    public void ManifestSignedByTheBackupKeyIsANewerRelease()
    {
        using TestKey primary = new("primary");
        using TestKey backup = new("backup");
        ReleaseCheckResult result = Check(backup, Manifest(backup.Key.KeyId), [primary.Key, backup.Key]);

        Assert.AreEqual(ReleaseCheckOutcome.NewerRelease, result.Outcome);
    }

    [TestMethod]
    public void ManifestSignedByAnotherEcdsaImplementationVerifies()
    {
        // Generated with Python "cryptography" (OpenSSL) from a fixed test-only private key: DER signature converted to r || s.
        ReleaseSigningKey key = ReleaseSigningKey.FromHex(
            "interop",
            "986A4D6D8E5A81E62A52ADDFC7062D2B2173388E7E5522CC8A6E7163A76B1DD5",
            "6684CC8908A110A9C3A87261ADC3929B28A81B4649C8597D571A284D81E006CC");
        Assert.AreEqual("b6d81b1cde1a0ba5", key.KeyId);
        byte[] manifest = Manifest("b6d81b1cde1a0ba5", assetSize: "12345678", schemaAfter: "7");
        byte[] signature = Encoding.ASCII.GetBytes(
            "pcsteward-release-signature:1\nkey:b6d81b1cde1a0ba5\nsignature:+CDMD62zdpalXqqlpQzeRI2lZ6RWUNJcUvT015Q/6KAtA72JP69WvlA7jEug9h0ieT279K18tM0ne2gN3UZUlA==\n");

        ReleaseCheckResult result = ReleaseCandidateVerifier.Verify(manifest, signature, Tag, Running, 0, [key]);

        Assert.AreEqual(ReleaseCheckOutcome.NewerRelease, result.Outcome);
        Assert.AreEqual(7, result.Manifest!.SchemaVersionAfter);
    }

    [TestMethod]
    public void KeyIdIsTheFirstEightBytesOfSha256OverTheRawPoint()
    {
        // NIST P-256 base point G, so the owner's signing script can be checked against the same known answer.
        ReleaseSigningKey key = ReleaseSigningKey.FromHex(
            "known-answer",
            "6B17D1F2E12C4247F8BCE6E563A440F277037D812DEB33A0F4A13945D898C296",
            "4FE342E2FE1A7F9B8EE7EB4A7C0F9E162BCE33576B315ECECBB6406837BF51F5");

        Assert.AreEqual("d875db7def232236", key.KeyId);
    }

    [TestMethod]
    public void APointThatIsNotOnTheCurveIsNotAKey()
    {
        Assert.Throws<CryptographicException>(() => ReleaseSigningKey.FromHex("bad", new string('1', 64), new string('2', 64)));
        Assert.ThrowsExactly<ArgumentException>(() => ReleaseSigningKey.FromHex("short", new string('1', 62), new string('2', 64)));
    }

    // ---- Build identity and trust anchors ----

    [TestMethod]
    public void DevelopmentBuildNeverSelfUpdates()
    {
        using TestKey primary = new("primary");
        ReleaseCheckResult result = ReleaseCandidateVerifier.Verify(
            Manifest(primary.Key.KeyId), primary.Sign(Manifest(primary.Key.KeyId)), Tag, new RunningRelease(Version("0.1.0-private-beta"), 0), 0, [primary.Key]);

        AssertRefused(result, ReleaseRefusal.DevelopmentBuild);
    }

    [TestMethod]
    public void NoCompiledKeyRefusesEverything()
    {
        using TestKey primary = new("primary");
        AssertRefused(Check(primary, Manifest(primary.Key.KeyId), []), ReleaseRefusal.NoReleaseKey);
    }

    [TestMethod]
    public void ProductionBuildCompilesFixedRepositoryAndChannel()
    {
        Assert.AreEqual("PCSteward", ReleaseTrustAnchors.Product);
        Assert.AreEqual("beta", ReleaseTrustAnchors.Channel);
        Assert.AreEqual("DanDG923", ReleaseTrustAnchors.RepositoryOwner);
        Assert.AreEqual("pc-steward", ReleaseTrustAnchors.RepositoryName);
        Assert.IsTrue(ReleaseTrustAnchors.ReleaseKeys.Select(key => key.KeyId).Distinct(StringComparer.Ordinal).Count() == ReleaseTrustAnchors.ReleaseKeys.Count);
    }

    // ---- Signature file ----

    [TestMethod]
    public void MalformedSignatureFilesAreUnreadable()
    {
        using TestKey primary = new("primary");
        byte[] manifest = Manifest(primary.Key.KeyId);
        string good = Encoding.ASCII.GetString(primary.Sign(manifest));
        string[] lines = good.TrimEnd('\n').Split('\n');
        string[] variants =
        [
            "",
            good.TrimEnd('\n'),
            good.Replace("\n", "\r\n", StringComparison.Ordinal),
            good + "\n",
            good + "extra:1\n",
            "pcsteward-release-signature:2\n" + lines[1] + "\n" + lines[2] + "\n",
            lines[0] + "\nkey:" + primary.Key.KeyId.ToUpperInvariant() + "\n" + lines[2] + "\n",
            lines[0] + "\nkey:" + primary.Key.KeyId[..15] + "\n" + lines[2] + "\n",
            lines[0] + "\n" + lines[1] + "\nsignature:" + Convert.ToBase64String(new byte[63]) + "\n",
            lines[0] + "\n" + lines[1] + "\nsignature:" + Convert.ToBase64String(new byte[65]) + "\n",
            lines[0] + "\n" + lines[1] + "\nsignature:" + new string('*', 88) + "\n",
            lines[0] + "\n" + lines[1] + "\nsignature: " + lines[2]["signature:".Length..] + "\n",
            lines[0] + "\n" + lines[2] + "\n" + lines[1] + "\n",
            good + new string(' ', ReleaseSignature.MaximumBytes),
        ];

        foreach (string variant in variants)
        {
            AssertRefused(ReleaseCandidateVerifier.Verify(manifest, Encoding.ASCII.GetBytes(variant), Tag, Running, 0, [primary.Key]), ReleaseRefusal.SignatureUnreadable);
        }
        byte[] nonAscii = [.. Encoding.ASCII.GetBytes(good)];
        nonAscii[5] = 0xC3;
        AssertRefused(ReleaseCandidateVerifier.Verify(manifest, nonAscii, Tag, Running, 0, [primary.Key]), ReleaseRefusal.SignatureUnreadable);
    }

    [TestMethod]
    public void NonCanonicalBase64IsUnreadable()
    {
        using TestKey primary = new("primary");
        byte[] manifest = Manifest(primary.Key.KeyId);
        string good = Encoding.ASCII.GetString(primary.Sign(manifest));
        // The last base64 character of a 64-byte value carries 2 unused bits; setting one gives the same bytes, another text.
        int index = good.LastIndexOf("==", StringComparison.Ordinal) - 1;
        const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        char altered = Alphabet[Alphabet.IndexOf(good[index], StringComparison.Ordinal) ^ 1];
        string variant = good[..index] + altered + good[(index + 1)..];

        AssertRefused(ReleaseCandidateVerifier.Verify(manifest, Encoding.ASCII.GetBytes(variant), Tag, Running, 0, [primary.Key]), ReleaseRefusal.SignatureUnreadable);
    }

    [TestMethod]
    public void SignatureFromAKeyThatIsNotCompiledIsRefused()
    {
        using TestKey primary = new("primary");
        using TestKey stranger = new("stranger");
        AssertRefused(Check(stranger, Manifest(stranger.Key.KeyId), [primary.Key]), ReleaseRefusal.UnknownKey);
    }

    [TestMethod]
    public void TamperedManifestFailsTheSignature()
    {
        using TestKey primary = new("primary");
        byte[] manifest = Manifest(primary.Key.KeyId);
        byte[] signature = primary.Sign(manifest);
        byte[] tampered = Manifest(primary.Key.KeyId, sequence: "3");
        byte[] flipped = [.. manifest];
        flipped[^2] ^= 0x01;

        AssertRefused(ReleaseCandidateVerifier.Verify(tampered, signature, Tag, Running, 0, [primary.Key]), ReleaseRefusal.SignatureInvalid);
        AssertRefused(ReleaseCandidateVerifier.Verify(flipped, signature, Tag, Running, 0, [primary.Key]), ReleaseRefusal.SignatureInvalid);
        AssertRefused(ReleaseCandidateVerifier.Verify([.. manifest, (byte)' '], signature, Tag, Running, 0, [primary.Key]), ReleaseRefusal.SignatureInvalid);
    }

    [TestMethod]
    public void SignatureFileNamingTheOtherCompiledKeyFailsTheSignature()
    {
        using TestKey primary = new("primary");
        using TestKey backup = new("backup");
        byte[] manifest = Manifest(primary.Key.KeyId);
        byte[] signedByPrimary = primary.Sign(manifest);
        string relabelled = Encoding.ASCII.GetString(signedByPrimary).Replace("key:" + primary.Key.KeyId, "key:" + backup.Key.KeyId, StringComparison.Ordinal);

        AssertRefused(ReleaseCandidateVerifier.Verify(manifest, Encoding.ASCII.GetBytes(relabelled), Tag, Running, 0, [primary.Key, backup.Key]), ReleaseRefusal.SignatureInvalid);
    }

    [TestMethod]
    public void ManifestNamingADifferentKeyThanItsSignatureIsRefused()
    {
        using TestKey primary = new("primary");
        using TestKey backup = new("backup");
        AssertRefused(Check(primary, Manifest(backup.Key.KeyId), [primary.Key, backup.Key]), ReleaseRefusal.KeyMismatch);
    }

    // ---- Manifest shape (each variant is genuinely signed, so only the reader can refuse it) ----

    [TestMethod]
    public void ManifestsWithTheWrongShapeAreUnreadableEvenWhenSigned()
    {
        using TestKey primary = new("primary");
        string id = primary.Key.KeyId;
        string good = Encoding.UTF8.GetString(Manifest(id));
        string[] variants =
        [
            good.Replace("\"schema\": 1,", "\"schema\": 1, \"url\": \"https://example.invalid/x.zip\",", StringComparison.Ordinal),
            good.Replace("\"schema\": 1,", "\"schema\": 1, \"schema\": 1,", StringComparison.Ordinal),
            good.Replace("\"schema\": 1,", "\"schema\": 1, \"Schema\": 1,", StringComparison.Ordinal),
            good.Replace("  \"sbomSha256\": \"" + new string('c', 64) + "\",\n", string.Empty, StringComparison.Ordinal),
            good.Replace("\"sequence\": 2", "\"sequence\": \"2\"", StringComparison.Ordinal),
            good.Replace("\"sequence\": 2", "\"sequence\": 2.0", StringComparison.Ordinal),
            good.Replace("\"sequence\": 2", "\"sequence\": 2e0", StringComparison.Ordinal),
            good.Replace("\"sequence\": 2", "\"sequence\": -2", StringComparison.Ordinal),
            good.Replace("\"sequence\": 2", "\"sequence\": 0", StringComparison.Ordinal),
            good.Replace("\"assetSize\": 1048576", "\"assetSize\": 0", StringComparison.Ordinal),
            good.Replace("\"assetSize\": 1048576", "\"assetSize\": 536870913", StringComparison.Ordinal),
            good.Replace("\"schemaVersionAfter\": 1", "\"schemaVersionAfter\": 0", StringComparison.Ordinal),
            good.Replace(new string('a', 64), new string('A', 64), StringComparison.Ordinal),
            good.Replace(new string('a', 64), new string('a', 63), StringComparison.Ordinal),
            good.Replace("2026-11-11T12:00:00Z", "2026-11-11T12:00:00+00:00", StringComparison.Ordinal),
            good.Replace("2026-11-11T12:00:00Z", "2026-13-11T12:00:00Z", StringComparison.Ordinal),
            good.Replace("\"version\": \"0.2.0-beta.1\"", "\"version\": \"0.2.0-beta.1+build\"", StringComparison.Ordinal),
            good.Replace("\"version\": \"0.2.0-beta.1\"", "\"version\": \"0.2\"", StringComparison.Ordinal),
            good.Replace("\"minimumUpdaterVersion\": \"0.1.0-private-beta\"", "\"minimumUpdaterVersion\": \"01.0.0\"", StringComparison.Ordinal),
            good.Replace("\"keyId\": \"" + id + "\"", "\"keyId\": \"" + id.ToUpperInvariant() + "\"", StringComparison.Ordinal),
            good.Replace("\"product\": \"PCSteward\"", "\"product\": null", StringComparison.Ordinal),
            good.Replace("\"product\": \"PCSteward\"", "\"product\": \"PC\\u0007Steward\"", StringComparison.Ordinal),
            good.Replace("\n}", ",\n}", StringComparison.Ordinal),
            good.Replace("{\n", "{\n  // comment\n", StringComparison.Ordinal),
            "[" + good + "]",
            good + good,
            good + new string(' ', ReleaseManifest.MaximumBytes),
        ];

        foreach (string variant in variants)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(variant);
            AssertRefused(ReleaseCandidateVerifier.Verify(bytes, primary.Sign(bytes), Tag, Running, 0, [primary.Key]), ReleaseRefusal.ManifestUnreadable);
        }
        byte[] withBom = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(good)];
        AssertRefused(ReleaseCandidateVerifier.Verify(withBom, primary.Sign(withBom), Tag, Running, 0, [primary.Key]), ReleaseRefusal.ManifestUnreadable);
        byte[] invalidUtf8 = [.. Encoding.UTF8.GetBytes(good)];
        invalidUtf8[Array.IndexOf(invalidUtf8, (byte)'P')] = 0xC3;
        AssertRefused(ReleaseCandidateVerifier.Verify(invalidUtf8, primary.Sign(invalidUtf8), Tag, Running, 0, [primary.Key]), ReleaseRefusal.ManifestUnreadable);
    }

    [TestMethod]
    public void WrongProductChannelOrSchemaIsRefused()
    {
        using TestKey primary = new("primary");
        string id = primary.Key.KeyId;
        AssertRefused(Check(primary, Manifest(id, product: "PCStewardX"), [primary.Key]), ReleaseRefusal.WrongProduct);
        AssertRefused(Check(primary, Manifest(id, channel: "stable"), [primary.Key]), ReleaseRefusal.WrongChannel);
        AssertRefused(Check(primary, Manifest(id, schema: "2"), [primary.Key]), ReleaseRefusal.WrongSchema);
    }

    [TestMethod]
    public void TagMustBeVPlusTheSignedVersion()
    {
        using TestKey primary = new("primary");
        byte[] manifest = Manifest(primary.Key.KeyId);
        byte[] signature = primary.Sign(manifest);
        foreach (string? tag in new[] { null, "", "0.2.0-beta.1", "V0.2.0-beta.1", "v0.2.0-beta.2", "v0.2.0-beta.1 " })
        {
            AssertRefused(ReleaseCandidateVerifier.Verify(manifest, signature, tag, Running, 0, [primary.Key]), ReleaseRefusal.TagMismatch);
        }
    }

    [TestMethod]
    public void AssetNameMustMatchTheCompiledPattern()
    {
        using TestKey primary = new("primary");
        foreach (string name in new[] { "PCSteward-0.2.0-beta.2-win-x64.zip", "..\\PCSteward-0.2.0-beta.1-win-x64.zip", "PCSteward-0.2.0-beta.1-win-x64.exe", "pcsteward-0.2.0-beta.1-win-x64.zip" })
        {
            AssertRefused(Check(primary, Manifest(primary.Key.KeyId, assetName: name), [primary.Key]), ReleaseRefusal.AssetNameMismatch);
        }
    }

    // ---- Ordering: downgrade, replay and "nothing newer" ----

    [TestMethod]
    public void SameOrOlderGenuineReleaseIsNotNewer()
    {
        using TestKey primary = new("primary");
        RunningRelease running = new(Version("0.2.0-beta.1"), 2);
        foreach ((string version, string sequence) in new[] { ("0.2.0-beta.1", "2"), ("0.1.0-private-beta", "1"), ("0.2.0-alpha", "1") })
        {
            byte[] manifest = Manifest(primary.Key.KeyId, version: version, sequence: sequence);
            ReleaseCheckResult result = ReleaseCandidateVerifier.Verify(manifest, primary.Sign(manifest), "v" + version, running, 2, [primary.Key]);

            Assert.AreEqual(ReleaseCheckOutcome.NoNewerRelease, result.Outcome, version);
            Assert.IsNotNull(result.Manifest);
        }
    }

    [TestMethod]
    public void NewerVersionWithAnOldOrRepeatedSequenceIsRefused()
    {
        using TestKey primary = new("primary");
        RunningRelease running = new(Version("0.1.0-private-beta"), 5);
        AssertRefused(Check(primary, Manifest(primary.Key.KeyId, sequence: "5"), [primary.Key], running), ReleaseRefusal.OlderOrRepeatedRelease);
        AssertRefused(Check(primary, Manifest(primary.Key.KeyId, sequence: "4"), [primary.Key], running), ReleaseRefusal.OlderOrRepeatedRelease);
        // After an owner rollback, the machine remembers the higher sequence it already accepted.
        AssertRefused(Check(primary, Manifest(primary.Key.KeyId, sequence: "6"), [primary.Key], running, highestAccepted: 6), ReleaseRefusal.OlderOrRepeatedRelease);
        Assert.AreEqual(ReleaseCheckOutcome.NewerRelease, Check(primary, Manifest(primary.Key.KeyId, sequence: "7"), [primary.Key], running, highestAccepted: 6).Outcome);
    }

    [TestMethod]
    public void OlderVersionWithAHigherSequenceIsRefused()
    {
        using TestKey primary = new("primary");
        RunningRelease running = new(Version("0.3.0"), 3);
        byte[] manifest = Manifest(primary.Key.KeyId, sequence: "9");

        AssertRefused(ReleaseCandidateVerifier.Verify(manifest, primary.Sign(manifest), Tag, running, 0, [primary.Key]), ReleaseRefusal.OlderOrRepeatedRelease);
    }

    [TestMethod]
    public void ReleaseNeedingANewerUpdaterIsRefused()
    {
        using TestKey primary = new("primary");
        AssertRefused(Check(primary, Manifest(primary.Key.KeyId, minimum: "0.1.1"), [primary.Key]), ReleaseRefusal.UpdaterTooOld);
        Assert.AreEqual(ReleaseCheckOutcome.NewerRelease, Check(primary, Manifest(primary.Key.KeyId, minimum: "0.1.0-private-beta"), [primary.Key]).Outcome);
    }

    // ---- Versions ----

    [TestMethod]
    public void VersionsFollowSemanticVersioningPrecedence()
    {
        string[] ordered = ["0.1.0-alpha", "0.1.0-alpha.1", "0.1.0-alpha.beta", "0.1.0-beta", "0.1.0-beta.2", "0.1.0-beta.11", "0.1.0-private-beta", "0.1.0", "0.1.1", "0.2.0-beta.1", "1.0.0", "10.0.0"];
        for (int index = 1; index < ordered.Length; index++)
        {
            Assert.IsTrue(Version(ordered[index - 1]) < Version(ordered[index]), ordered[index - 1] + " < " + ordered[index]);
        }
        Assert.IsTrue(Version("1.2.3") == Version("1.2.3"));
    }

    [TestMethod]
    public void VersionParsingIsStrict()
    {
        foreach (string? text in new[] { null, "", "1", "1.2", "1.2.3.4", "01.2.3", "1.02.3", "1.2.3-", "1.2.3-Beta", "1.2.3-beta..1", "1.2.3-beta.01", "1.2.3+build", " 1.2.3", "v1.2.3", "1.2.3-é", "9999999.0.0", new string('1', 65) })
        {
            Assert.IsFalse(ReleaseVersion.TryParse(text, out _), text ?? "null");
        }
    }

    // ---- Fail closed ----

    [TestMethod]
    public void UnexpectedExceptionsAreRefusalsNeverPasses()
    {
        using TestKey primary = new("primary");
        byte[] manifest = Manifest(primary.Key.KeyId);
        AssertRefused(ReleaseCandidateVerifier.Verify(manifest, primary.Sign(manifest), Tag, null!, 0, [primary.Key]), ReleaseRefusal.UnexpectedError);
        AssertRefused(ReleaseCandidateVerifier.Verify(manifest, primary.Sign(manifest), Tag, Running, 0, null!), ReleaseRefusal.UnexpectedError);
    }

    // ---- Helpers ----

    private static ReleaseVersion Version(string text)
    {
        Assert.IsTrue(ReleaseVersion.TryParse(text, out ReleaseVersion? version), text);
        return version!;
    }

    private static ReleaseCheckResult Check(TestKey signer, byte[] manifest, IReadOnlyList<ReleaseSigningKey> keys, RunningRelease? running = null, long highestAccepted = 0) =>
        ReleaseCandidateVerifier.Verify(manifest, signer.Sign(manifest), Tag, running ?? Running, highestAccepted, keys);

    private static void AssertRefused(ReleaseCheckResult result, ReleaseRefusal refusal)
    {
        Assert.AreEqual(ReleaseCheckOutcome.NotTrusted, result.Outcome);
        Assert.AreEqual(refusal, result.Refusal);
        Assert.IsNull(result.Manifest);
    }

    private static byte[] Manifest(
        string keyId,
        string version = "0.2.0-beta.1",
        string sequence = "2",
        string product = "PCSteward",
        string channel = "beta",
        string schema = "1",
        string minimum = "0.1.0-private-beta",
        string? assetName = null,
        string assetSize = "1048576",
        string schemaAfter = "1")
    {
        string text = "{\n" +
            "  \"schema\": " + schema + ",\n" +
            "  \"product\": \"" + product + "\",\n" +
            "  \"channel\": \"" + channel + "\",\n" +
            "  \"version\": \"" + version + "\",\n" +
            "  \"sequence\": " + sequence + ",\n" +
            "  \"publishedUtc\": \"2026-11-11T12:00:00Z\",\n" +
            "  \"minimumUpdaterVersion\": \"" + minimum + "\",\n" +
            "  \"assetName\": \"" + (assetName ?? "PCSteward-" + version + "-win-x64.zip").Replace("\\", "\\\\", StringComparison.Ordinal) + "\",\n" +
            "  \"assetSize\": " + assetSize + ",\n" +
            "  \"assetSha256\": \"" + new string('a', 64) + "\",\n" +
            "  \"payloadManifestSha256\": \"" + new string('b', 64) + "\",\n" +
            "  \"sbomSha256\": \"" + new string('c', 64) + "\",\n" +
            "  \"schemaVersionAfter\": " + schemaAfter + ",\n" +
            "  \"keyId\": \"" + keyId + "\"\n" +
            "}\n";
        return Encoding.UTF8.GetBytes(text);
    }

    private sealed class TestKey : IDisposable
    {
        private readonly ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public TestKey(string name)
        {
            ECParameters parameters = ecdsa.ExportParameters(false);
            Key = ReleaseSigningKey.FromHex(name, Convert.ToHexString(parameters.Q.X!), Convert.ToHexString(parameters.Q.Y!));
        }

        public ReleaseSigningKey Key { get; }

        public byte[] Sign(byte[] manifest)
        {
            byte[] signature = ecdsa.SignData(manifest, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            return Encoding.ASCII.GetBytes(new ReleaseSignature(Key.KeyId, signature).ToFileText());
        }

        public void Dispose() => ecdsa.Dispose();
    }
}
