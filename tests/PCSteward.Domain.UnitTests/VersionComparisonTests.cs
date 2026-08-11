using PCSteward.Domain;

namespace PCSteward.Domain.UnitTests;

[TestClass]
public sealed class VersionComparisonTests
{
    [TestMethod]
    [DataRow("1.2", "1.2.0", VersionComparisonOutcome.Equal)]
    [DataRow("01.002.0003", "1.2.4", VersionComparisonOutcome.Newer)]
    [DataRow("1.2.4", "1.2.03", VersionComparisonOutcome.Older)]
    [DataRow("1.2.3.4.5", "1.2.3.4.6", VersionComparisonOutcome.Newer)]
    public void ComparesNumericDottedVersionsWithoutLexicalFallback(string installed, string candidate, VersionComparisonOutcome expected)
    {
        VersionComparisonResult result = VersionComparison.Compare(new(installed, candidate));
        Assert.AreEqual(expected, result.Outcome);
        Assert.AreEqual(expected == VersionComparisonOutcome.Older ? VersionComparisonExplanation.CandidateIsOlderDowngradeBlocked : VersionComparisonExplanation.NumericDotted, result.Explanation);
    }

    [TestMethod]
    [DataRow("1.0.0-beta.2", "1.0.0-beta.3", VersionComparisonOutcome.Newer)]
    [DataRow("1.0.0-preview.1", "1.0.0-rc.1", VersionComparisonOutcome.Newer)]
    [DataRow("1.0.0-rc.1", "1.0.0", VersionComparisonOutcome.Newer)]
    public void ComparesRecognizedPrereleaseLabelsWhenAllowedByInstalledChannel(string installed, string candidate, VersionComparisonOutcome expected)
    {
        VersionComparisonResult result = VersionComparison.Compare(new(installed, candidate));
        Assert.AreEqual(expected, result.Outcome);
        Assert.AreEqual(VersionComparisonExplanation.PrereleaseNumericDotted, result.Explanation);
    }

    [TestMethod]
    public void DoesNotOfferPrereleaseCandidateToStableInstallationByDefault()
    {
        VersionComparisonResult result = VersionComparison.Compare(new("1.0.0", "1.1.0-preview.1"));
        Assert.AreEqual(VersionComparisonOutcome.Incomparable, result.Outcome);
        Assert.AreEqual(VersionComparisonExplanation.CandidatePrereleaseDisallowed, result.Explanation);
    }

    [TestMethod]
    public void CanComparePrereleaseCandidateWhenCallerExplicitlyAllowsIt()
    {
        VersionComparisonResult result = VersionComparison.Compare(new("1.0.0", "1.1.0-preview.1", AllowPrereleaseCandidate: true));
        Assert.AreEqual(VersionComparisonOutcome.Newer, result.Outcome);
        Assert.AreEqual(VersionComparisonExplanation.PrereleaseNumericDotted, result.Explanation);
    }

    [TestMethod]
    public void DefersConnectorSpecificSemanticsWithoutParsingVersions()
    {
        VersionComparisonResult result = VersionComparison.Compare(new("not-a-version", "also-not-a-version", RequiresConnectorSpecificSemantics: true));
        Assert.AreEqual(VersionComparisonOutcome.ConnectorDeferred, result.Outcome);
        Assert.AreEqual(VersionComparisonExplanation.ConnectorSpecificSemanticsRequired, result.Explanation);
    }

    [TestMethod]
    [DataRow(null, "1.0.0", VersionComparisonExplanation.MissingInstalledVersion)]
    [DataRow("1.0.0", null, VersionComparisonExplanation.MissingCandidateVersion)]
    [DataRow("1.0 Contoso", "1.0.1", VersionComparisonExplanation.VendorSuffixUnsupported)]
    [DataRow("1.0.0", "1.0.1+vendor", VersionComparisonExplanation.VendorSuffixUnsupported)]
    [DataRow("unknown", "1.0.1", VersionComparisonExplanation.MalformedInstalledVersion)]
    [DataRow("1.0.0", "2024-R2", VersionComparisonExplanation.MalformedCandidateVersion)]
    [DataRow("1.0.0", "1.0.0-nightly.2", VersionComparisonExplanation.MalformedCandidateVersion)]
    [DataRow("1.0.0", "1.2.3.4.5.6.7.8.9", VersionComparisonExplanation.MalformedCandidateVersion)]
    public void TreatsMissingVendorSuffixedAndUnknownVersionsAsIncomparable(string? installed, string? candidate, VersionComparisonExplanation expected)
    {
        VersionComparisonResult result = VersionComparison.Compare(new(installed, candidate));
        Assert.AreEqual(VersionComparisonOutcome.Incomparable, result.Outcome);
        Assert.AreEqual(expected, result.Explanation);
    }

    [TestMethod]
    public void OversizedPrereleaseNumberIsIncomparableInsteadOfThrowing()
    {
        string oversized = new('9', 256);
        VersionComparisonResult result = VersionComparison.Compare(new("1.0.0-preview.1", $"1.0.0-preview.{oversized}"));

        Assert.AreEqual(VersionComparisonOutcome.Incomparable, result.Outcome);
        Assert.AreEqual(VersionComparisonExplanation.MalformedCandidateVersion, result.Explanation);
    }
}
