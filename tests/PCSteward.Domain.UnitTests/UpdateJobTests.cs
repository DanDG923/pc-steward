using PCSteward.Domain;

namespace PCSteward.Domain.UnitTests;

[TestClass]
public sealed class UpdateJobTests
{
    [TestMethod]
    public void ReviewRequiresExplicitConfirmationBeforeAnApprovedJobExists()
    {
        Guid jobId = Guid.NewGuid();
        Guid correlationId = Guid.NewGuid();
        UpdateJobReview review = new(jobId, correlationId, Selection());

        Assert.AreEqual(UpdateJobReviewState.AwaitingConfirmation, review.State);
        Assert.IsFalse(typeof(UpdateJobReview).GetProperties().Any(property => property.PropertyType == typeof(ImmutableUpdateJob)));

        DateTimeOffset approvedAt = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);
        ImmutableUpdateJob job = review.Confirm(approvedAt);
        Assert.AreEqual(jobId, job.Id);
        Assert.AreEqual(correlationId, job.CorrelationId);
        Assert.AreEqual(approvedAt, job.ApprovedAt);
        Assert.IsTrue(job.HasValidDigest);
    }

    [TestMethod]
    public void PersistedFieldMutationFailsTheCanonicalDigestCheck()
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), Selection()).Confirm(DateTimeOffset.UtcNow);
        UpdateJobSelection substituted = Selection(candidateVersion: "2.0.0");

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ImmutableUpdateJob.Restore(job.Id, job.CorrelationId, substituted, job.ApprovedAt, job.Digest));
    }

    [TestMethod]
    [DataRow("package", "Contoso.Widget & calc.exe")]
    [DataRow("package", "..\\evil.exe")]
    [DataRow("installed", "1.0 --source evil")]
    [DataRow("candidate", "2.0;shutdown")]
    [DataRow("stable", "contoso\r\ncommand")]
    [DataRow("mapping", "evidence value with spaces")]
    public void CommandPathAndArgumentShapedSelectionValuesAreRejected(string field, string maliciousValue)
    {
        Assert.ThrowsExactly<ArgumentException>(() => new UpdateJobSelection(
            field == "stable" ? maliciousValue : "winget:contoso.widget",
            UpdateProvider.WinGet,
            UpdateSource.WinGet,
            field == "package" ? maliciousValue : "Contoso.Widget",
            field == "installed" ? maliciousValue : "1.0.0",
            field == "candidate" ? maliciousValue : "1.1.0",
            UpdateScope.User,
            UpdateArchitecture.X64,
            UpdateChannel.Stable,
            field == "mapping" ? maliciousValue : "mapping-v1"));
    }

    private static UpdateJobSelection Selection(string candidateVersion = "1.1.0") =>
        new(
            "winget:contoso.widget",
            UpdateProvider.WinGet,
            UpdateSource.WinGet,
            "Contoso.Widget",
            "1.0.0",
            candidateVersion,
            UpdateScope.User,
            UpdateArchitecture.X64,
            UpdateChannel.Stable,
            "mapping-v1");
}
