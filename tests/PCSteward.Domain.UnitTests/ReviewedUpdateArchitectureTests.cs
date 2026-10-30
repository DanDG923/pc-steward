using System.Security.Cryptography;
using System.Text;
using PCSteward.Domain;

namespace PCSteward.Domain.UnitTests;

/// <summary>FR-UPD-15; TM-03/04/13: script-selected is explicit data and never an observed Neutral payload.</summary>
[TestClass]
public sealed class ReviewedUpdateArchitectureTests
{
    [TestMethod]
    public void ShapesAreClosedAndScriptSelectionDoesNotExposeANeutralArchitecture()
    {
        ReviewedUpdateArchitecture script = ReviewedUpdateArchitecture.ProviderScriptSelected();
        Assert.AreEqual(ReviewedArchitectureBinding.ProviderScriptSelected, script.Binding);
        Assert.IsNull(script.Value);
        Assert.AreEqual(UpdateArchitecture.X64, ReviewedUpdateArchitecture.Observed(UpdateArchitecture.X64).Value);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ReviewedUpdateArchitecture.Observed((UpdateArchitecture)99));
        Assert.IsEmpty(typeof(ReviewedUpdateArchitecture).GetConstructors());
        Assert.IsNull(typeof(ReviewedUpdateArchitecture).GetProperty(nameof(ReviewedUpdateArchitecture.Binding))!.SetMethod);
        Assert.IsNull(typeof(ReviewedUpdateArchitecture).GetProperty(nameof(ReviewedUpdateArchitecture.Value))!.SetMethod);
        Assert.ThrowsExactly<ArgumentException>(() => new ReviewedUpdateSelection(Job(UpdateArchitecture.X64).Selection, script));
    }

    [TestMethod]
    public void ScriptAndObservedNeutralHaveDifferentCanonicalIdentityAndSurviveRestore()
    {
        ImmutableUpdateJob job = Job(UpdateArchitecture.Neutral);
        ReviewedUpdateOperation script = Operation(job, ReviewedUpdateArchitecture.ProviderScriptSelected());
        ReviewedUpdateOperation observed = Operation(job, ReviewedUpdateArchitecture.Observed(UpdateArchitecture.Neutral));
        Assert.AreNotEqual(script.Digest, observed.Digest);
        Assert.IsFalse(script.Selection.EqualsEvidence(observed.Selection));
        string[] lines = Encoding.UTF8.GetString(script.GetCanonicalBytes()).Split('\n');
        Assert.HasCount(25, lines);
        Assert.AreEqual("ProviderScriptSelected", lines[13]);
        Assert.AreEqual("None", lines[14]);
        Assert.IsFalse(lines.Contains("Neutral"));
        Assert.AreEqual(job.Digest, script.LegacyJobDigest);
        Assert.IsTrue(ReviewedUpdateOperation.TryRestore(script.GetCanonicalBytes(), script.Digest, out ReviewedUpdateOperation? restored));
        Assert.AreEqual(script.Selection.Architecture, restored!.Selection.Architecture);
        Assert.IsTrue(script.HasSameIdentity(restored));
        Assert.AreEqual(script.LegacyJobDigest, restored.LegacyJobDigest);
    }

    [TestMethod]
    [DataRow("ProviderScriptSelected", "X64")]
    [DataRow("Observed", "None")]
    [DataRow("Unknown", "None")]
    [DataRow("0", "X64")]
    [DataRow("Observed", "0")]
    public void RecomputedDigestDoesNotPermitMalformedArchitectureShape(string binding, string value)
    {
        ReviewedUpdateOperation operation = Operation(Job(UpdateArchitecture.Neutral), ReviewedUpdateArchitecture.ProviderScriptSelected());
        string[] lines = Encoding.UTF8.GetString(operation.GetCanonicalBytes()).Split('\n');
        lines[13] = binding;
        lines[14] = value;
        byte[] bytes = Encoding.UTF8.GetBytes(string.Join('\n', lines));
        Assert.IsFalse(ReviewedUpdateOperation.TryRestore(bytes, Convert.ToHexString(SHA256.HashData(bytes)), out _));
    }

    [TestMethod]
    public void SchemaTwoRetainsExactBytesAndDigestWithoutAcquiringTheNewContract()
    {
        ReviewedUpdateOperation current = Operation(Job(UpdateArchitecture.Neutral), ReviewedUpdateArchitecture.Observed(UpdateArchitecture.Neutral));
        List<string> lines = Encoding.UTF8.GetString(current.GetCanonicalBytes()).Split('\n').ToList();
        lines[0] = "pc-steward-reviewed-update:2";
        lines.RemoveAt(13);
        lines[20] = "ChocolateyMachineRegistrationV2";
        byte[] bytes = Encoding.UTF8.GetBytes(string.Join('\n', lines));
        string digest = Convert.ToHexString(SHA256.HashData(bytes));
        Assert.IsTrue(ReviewedUpdateOperation.TryRestore(bytes, digest, out ReviewedUpdateOperation? restored));
        Assert.IsFalse(restored!.HasCurrentSchema);
        Assert.AreEqual(2, restored.EncodingVersion);
        Assert.AreEqual(digest, restored.Digest);
        CollectionAssert.AreEqual(bytes, restored.GetCanonicalBytes());
    }

    private static ImmutableUpdateJob Job(UpdateArchitecture architecture) => new UpdateJobReview(
        Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("22222222-2222-2222-2222-222222222222"),
        new("chocolatey:widget", UpdateProvider.Chocolatey, UpdateSource.ChocolateyCommunity, "widget", "1.0", "1.1",
            UpdateScope.Machine, architecture, UpdateChannel.Stable, "mapping-v1"))
        .Confirm(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));
    private static ReviewedUpdateOperation Operation(ImmutableUpdateJob job, ReviewedUpdateArchitecture architecture) =>
        new(job, "S-1-5-21-1", "native:1", "context:1", "evidence:1", job.ApprovedAt,
            UpdateRouteContract.ChocolateyMachineRegistrationV3,
            ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled), architecture);
}
