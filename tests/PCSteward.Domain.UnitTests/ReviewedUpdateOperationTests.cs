using System.Text;
using System.Security.Cryptography;
using PCSteward.Domain;

namespace PCSteward.Domain.UnitTests;

/// <summary>FR-UPD-15 / ADR-025: canonical integrity is complete, immutable data, never consent.</summary>
[TestClass]
public sealed class ReviewedUpdateOperationTests
{
    private static readonly DateTimeOffset ReviewedAt = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void EquivalentUtcEvidenceHasDeterministicCanonicalIdentity()
    {
        ReviewedUpdateOperation first = Operation();
        ReviewedUpdateOperation second = Operation(evidenceAt: ReviewedAt.AddSeconds(-1).ToOffset(TimeSpan.FromHours(2)));
        Assert.AreEqual(first.Digest, second.Digest);
        Assert.IsTrue(first.HasSameIdentity(second));
        StringAssert.StartsWith(Encoding.UTF8.GetString(first.GetCanonicalBytes()), "pc-steward-reviewed-update:2\nUpdate\n");
        Assert.AreEqual(64, first.Digest.Length);
    }

    [TestMethod]
    [DataRow("mode")]
    [DataRow("binding")]
    [DataRow("user")]
    [DataRow("instance")]
    [DataRow("context")]
    [DataRow("evidence")]
    [DataRow("observed")]
    [DataRow("candidate")]
    [DataRow("review-time")]
    [DataRow("job")]
    [DataRow("correlation")]
    [DataRow("provider")]
    [DataRow("source")]
    [DataRow("scope")]
    [DataRow("architecture")]
    [DataRow("channel")]
    [DataRow("mapping")]
    [DataRow("installed")]
    [DataRow("stable")]
    [DataRow("package")]
    [DataRow("route")]
    public void RecomputedDigestCannotHideAnIdentityChange(string changed)
    {
        ReviewedUpdateOperation original = Operation();
        ReviewedUpdateOperation substituted = Operation(changed);
        Assert.AreNotEqual(original.Digest, substituted.Digest);
        Assert.IsFalse(original.HasSameIdentity(substituted));
    }

    [TestMethod]
    public void CallerOwnedByteMutationDoesNotChangeTheReviewedIdentity()
    {
        ReviewedUpdateOperation operation = Operation();
        string digest = operation.Digest;
        byte[] bytes = operation.GetCanonicalBytes();
        bytes[0] = (byte)'x';
        Assert.AreEqual(digest, operation.Digest);
        Assert.IsTrue(operation.HasSameIdentity(Operation()));
        Assert.AreNotEqual(bytes[0], operation.GetCanonicalBytes()[0]);
    }

    [TestMethod]
    [DataRow("S-1-5")]
    [DataRow("S-1-5-01")]
    [DataRow("S-1-281474976710656-1")]
    [DataRow("S-1-5-4294967296")]
    [DataRow("S-1-5-1\nUpdate")]
    public void NoncanonicalOrOutOfRangeUserIdentityIsRejected(string sid)
    {
        Assert.ThrowsExactly<ArgumentException>(() => Operation(sid: sid));
    }

    [TestMethod]
    [DataRow("instance", "id\nSilent")]
    [DataRow("instance", "..\\other")]
    [DataRow("context", "C:\\other")]
    [DataRow("evidence", "evidence --force")]
    [DataRow("evidence", "evidence\n")]
    public void NativeEvidenceCannotInjectCanonicalFieldsOrPaths(string field, string value)
    {
        Assert.ThrowsExactly<ArgumentException>(() => new ReviewedUpdateOperation(Job(), "S-1-5-21-11-22-33-1001",
            field == "instance" ? value : "native:123", field == "context" ? value : "context:123",
            field == "evidence" ? value : "evidence:123", ReviewedAt.AddSeconds(-1), UpdateRouteContract.WinGetUserExactV2,
            ReviewedInstallerMode.Selected(InstallerMode.Silent)));
    }

    [TestMethod]
    public void MissingFutureOrUndefinedEvidenceCannotBeCanonicalized()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Operation(evidenceAt: default(DateTimeOffset)));
        Assert.ThrowsExactly<ArgumentException>(() => Operation(evidenceAt: ReviewedAt.AddTicks(1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ReviewedUpdateOperation(Job(), "S-1-5-21-11-22-33-1001",
            "native:123", "context:123", "evidence:123", ReviewedAt.AddSeconds(-1), (UpdateRouteContract)99,
            ReviewedInstallerMode.Selected(InstallerMode.Silent)));
    }

    [TestMethod]
    public void EvidenceIdentityLengthHasAnExactBoundary()
    {
        ReviewedUpdateOperation operation = new(Job(), "S-1-5-21-11-22-33-1001", new string('a', 256),
            "context:123", "evidence:123", ReviewedAt, UpdateRouteContract.WinGetUserExactV2,
            ReviewedInstallerMode.Selected(InstallerMode.Silent));
        Assert.AreEqual(256, operation.NativeInstanceId.Length);
        Assert.ThrowsExactly<ArgumentException>(() => new ReviewedUpdateOperation(Job(), "S-1-5-21-11-22-33-1001", new string('a', 257),
            "context:123", "evidence:123", ReviewedAt, UpdateRouteContract.WinGetUserExactV2,
            ReviewedInstallerMode.Selected(InstallerMode.Silent)));
    }

    [TestMethod]
    [DataRow("selected")]
    [DataRow("binding")]
    [DataRow("mode")]
    public void StrictRestorePreservesDataWithoutCreatingApproval(string variant)
    {
        ReviewedUpdateOperation original = Operation(variant);
        Assert.IsTrue(ReviewedUpdateOperation.TryRestore(original.GetCanonicalBytes(), original.Digest, out ReviewedUpdateOperation? restored));
        Assert.IsNotNull(restored);
        Assert.IsTrue(original.HasSameIdentity(restored));
        Assert.AreEqual(original.Mode, restored.Mode);
        Assert.AreEqual(original.NativeInstanceId, restored.NativeInstanceId);
    }

    [TestMethod]
    [DataRow(0, "pc-steward-reviewed-update:1")]
    [DataRow(1, "Install")]
    [DataRow(2, "00000000-0000-0000-0000-000000000000")]
    [DataRow(7, "UnknownProvider")]
    [DataRow(7, "0")]
    [DataRow(6, " winget:contoso.widget")]
    [DataRow(20, "UnknownRoute")]
    [DataRow(21, "Unsupported")]
    [DataRow(22, "UnknownMode")]
    [DataRow(22, "None")]
    [DataRow(23, "ProviderScriptControlled")]
    public void RecomputedHashDoesNotPermitMalformedOrNoncanonicalFields(int field, string value)
    {
        string[] lines = Encoding.UTF8.GetString(Operation().GetCanonicalBytes()).Split('\n');
        Assert.HasCount(24, lines);
        lines[field] = value;
        byte[] mutated = Encoding.UTF8.GetBytes(string.Join('\n', lines));
        Assert.IsFalse(RestoreWithRecomputedDigest(mutated));
    }

    [TestMethod]
    public void RestoreRejectsMissingExtraInvalidUtf8AndOversizePayloads()
    {
        byte[] original = Operation().GetCanonicalBytes();
        Assert.IsFalse(RestoreWithRecomputedDigest(original[..^5]));
        Assert.IsFalse(RestoreWithRecomputedDigest(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(original) + "\nextra")));
        byte[] invalidUtf8 = (byte[])original.Clone();
        invalidUtf8[0] = 0xFF;
        Assert.IsFalse(RestoreWithRecomputedDigest(invalidUtf8));
        Assert.IsFalse(RestoreWithRecomputedDigest(new byte[16 * 1024 + 1]));
        Assert.IsFalse(ReviewedUpdateOperation.TryRestore(original, new string('0', 64), out _));
        Assert.IsFalse(ReviewedUpdateOperation.TryRestore(original, "invalid-digest", out _));
    }

    [TestMethod]
    public void RestoredValidMutationStillDoesNotMatchTheOriginalReview()
    {
        ReviewedUpdateOperation review = Operation();
        ReviewedUpdateOperation substituted = Operation("mode");
        Assert.IsTrue(ReviewedUpdateOperation.TryRestore(substituted.GetCanonicalBytes(), substituted.Digest, out ReviewedUpdateOperation? restored));
        Assert.IsFalse(review.HasSameIdentity(restored));
    }

    private static bool RestoreWithRecomputedDigest(byte[] bytes) =>
        ReviewedUpdateOperation.TryRestore(bytes, Convert.ToHexString(SHA256.HashData(bytes)), out _);

    private static ImmutableUpdateJob Job(string? change = null) => new UpdateJobReview(
        change == "job" ? Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa") : Guid.Parse("11111111-1111-1111-1111-111111111111"),
        change == "correlation" ? Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb") : Guid.Parse("22222222-2222-2222-2222-222222222222"),
        new UpdateJobSelection(change == "stable" ? "winget:other" : "winget:contoso.widget",
            change == "provider" ? UpdateProvider.Chocolatey : UpdateProvider.WinGet,
            change == "source" ? UpdateSource.MicrosoftStore : UpdateSource.WinGet,
            change == "package" ? "Contoso.Other" : "Contoso.Widget", change == "installed" ? "0.9" : "1.0",
            change == "candidate" ? "1.2" : "1.1", change is "scope" or "provider" ? UpdateScope.Machine : UpdateScope.User,
            change == "architecture" ? UpdateArchitecture.X86 : UpdateArchitecture.X64,
            change == "channel" ? UpdateChannel.Preview : UpdateChannel.Stable, change == "mapping" ? "mapping-v2" : "mapping-v1"))
        .Confirm(change == "review-time" ? ReviewedAt.AddTicks(1) : ReviewedAt);

    private static ReviewedUpdateOperation Operation(string? change = null, string? sid = null, DateTimeOffset? evidenceAt = null) => new(
        Job(change), sid ?? (change == "user" ? "S-1-5-21-11-22-33-1002" : "S-1-5-21-11-22-33-1001"),
        change == "instance" ? "native:456" : "native:123", change == "context" ? "context:456" : "context:123",
        change == "evidence" ? "evidence:456" : "evidence:123", evidenceAt ?? ReviewedAt.AddSeconds(change == "observed" ? -2 : -1),
        change == "route" ? UpdateRouteContract.ChocolateyMachineRegistrationV2 : UpdateRouteContract.WinGetUserExactV2, change == "binding"
            ? ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled)
            : ReviewedInstallerMode.Selected(change == "mode" ? InstallerMode.Interactive : InstallerMode.Silent));
}
