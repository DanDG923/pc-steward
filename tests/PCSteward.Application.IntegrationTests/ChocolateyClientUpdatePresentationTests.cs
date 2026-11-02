using System.Security.Cryptography;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, FR-HIS-01, TM-07/13; ADR-031 design mapping §2 (R4), §3.6, §3.7 and §3a: the client self-update
/// review sheet, Verify and history texts, and the workflow rules that apply to the route. The route stays unavailable
/// (not admitted); restart-pending and every non-None detail are Unverified; the profile evidence equality applies.
/// </summary>
[TestClass]
public sealed class ChocolateyClientUpdatePresentationTests
{
    [TestMethod]
    public void TheReviewSheetSaysUpdateChocolateyToXAndDisclosesTheSideEffects()
    {
        ChocolateyClientUpdateReviewView view = ChocolateyClientUpdatePresentation.Review(Operation("2.7.5", "2.8.0"))
            ?? throw new AssertFailedException("no review sheet");
        Assert.AreEqual("Update Chocolatey to 2.8.0", view.Title);
        Assert.AreEqual("Chocolatey 2.7.5 → 2.8.0", view.Versions);
        Assert.AreEqual(ChocolateyClientUpdatePresentation.WhatHappensText, view.WhatHappens);
        Assert.AreEqual(ChocolateyClientUpdatePresentation.VerificationText, view.Verification);
        CollectionAssert.Contains(view.Disclosures.ToArray(), ReviewedUpdateLingeringProcesses.ReviewDisclosure);
        Assert.IsTrue(view.Disclosures.Any(text => text.Contains("PATH", StringComparison.Ordinal)));
        Assert.IsTrue(view.Disclosures.Any(text => text.Contains(".old", StringComparison.Ordinal)));
        Assert.AreEqual(ChocolateyClientUpdatePresentation.UnavailableText, view.UnavailableReason, "the route is not admitted");
        Assert.IsFalse(ReviewedUpdateReview.HasSupportedMode(Operation("2.7.5", "2.8.0")));
    }

    [TestMethod]
    public void NoSheetForAnotherRouteADowngradeOrAMalformedVersion()
    {
        Assert.IsNull(ChocolateyClientUpdatePresentation.Review(Operation("2.8.0", "2.7.5")));
        Assert.IsNull(ChocolateyClientUpdatePresentation.Review(Operation("2.7.5", "2.7.5")));
        Assert.IsNull(ChocolateyClientUpdatePresentation.Review(Operation("2.7.5", "2.8.0-beta")));
        Assert.IsNull(ChocolateyClientUpdatePresentation.Review(Operation("2.7.5", "02.8.0")));
        Assert.IsNull(ChocolateyClientUpdatePresentation.Review(PackageOperation()));
        Assert.AreEqual("Update Chocolatey", ChocolateyClientUpdatePresentation.Title("2.8.0<script>"));
    }

    [TestMethod]
    public void VerificationRejectsRestartPendingAndNamesTheUnlistedLeftover()
    {
        ReviewedUpdateOperation operation = Operation("2.7.5", "2.8.0");
        Assert.AreEqual((UpdateDispatchOutcome.Unverified, ReviewedUpdateDispatchWorkflow.RestartContractViolationMessage),
            ReviewedUpdateDispatchWorkflow.RejectVerification(operation, new PostUpdateInventoryResult(true, true)));
        Assert.AreEqual((UpdateDispatchOutcome.Unverified, ChocolateyClientUpdatePresentation.NotVerifiedLeftoverMessage),
            ReviewedUpdateDispatchWorkflow.RejectVerification(operation,
                new PostUpdateInventoryResult(false, false, PostUpdateVerificationDetail.ClientLeftoverNotListed)));
        Assert.AreEqual((UpdateDispatchOutcome.Unverified, ChocolateyClientUpdatePresentation.NotVerifiedReleaseMessage),
            ReviewedUpdateDispatchWorkflow.RejectVerification(operation,
                new PostUpdateInventoryResult(false, false, PostUpdateVerificationDetail.ClientChanged)));
        Assert.AreEqual((UpdateDispatchOutcome.Unverified, ChocolateyClientUpdatePresentation.NotVerifiedUnreadableMessage),
            ReviewedUpdateDispatchWorkflow.RejectVerification(operation,
                new PostUpdateInventoryResult(false, false, PostUpdateVerificationDetail.ClientUnverifiable)));
        Assert.IsNull(ReviewedUpdateDispatchWorkflow.RejectVerification(operation, new PostUpdateInventoryResult(true, false)));
        StringAssert.Contains(ChocolateyClientUpdatePresentation.NotVerifiedLeftoverMessage, @"helpers\Chocolatey.PowerShell.dll.old");
        StringAssert.Contains(ChocolateyClientUpdatePresentation.NotVerifiedLeftoverMessage, "never deletes");
        // The package route keeps its own message.
        Assert.AreEqual((UpdateDispatchOutcome.Unverified, ReviewedUpdateDispatchWorkflow.ClientNotVerifiedMessage),
            ReviewedUpdateDispatchWorkflow.RejectVerification(PackageOperation(),
                new PostUpdateInventoryResult(false, false, PostUpdateVerificationDetail.ClientLeftoverNotListed)));
    }

    [TestMethod]
    public void HistoryNamesTheClientRouteAndFreshEvidenceMustEqualTheFrozenProfile()
    {
        Assert.AreEqual(ChocolateyClientUpdatePresentation.HistoryRouteText,
            ReviewedUpdateHistoryPresentation.Route(UpdateRouteContract.ChocolateyClientSelfUpdateV1));
        ReviewedUpdateOperation operation = Operation("2.7.5", "2.8.0");
        Assert.IsTrue(Evidence(operation, operation.EvidenceId).ProfileEvidenceMatches(operation));
        Assert.IsFalse(Evidence(operation, ReviewedUpdateOperation.ProfileEvidencePrefix + new string('0', 64)).ProfileEvidenceMatches(operation));
        Assert.IsFalse(Evidence(operation, "fresh:1").ProfileEvidenceMatches(operation));
    }

    private static FreshUpdateDispatchEvidence Evidence(ReviewedUpdateOperation operation, string evidenceId) =>
        new(operation.Selection, operation.RequestingUserSid, operation.NativeInstanceId, operation.InstallationContextId, evidenceId,
            operation.ReviewedAt, operation.RouteContract, operation.Mode, true, true);

    private static ReviewedUpdateOperation Operation(string installed, string candidate) => Build("chocolatey", installed, candidate,
        UpdateRouteContract.ChocolateyClientSelfUpdateV1);

    private static ReviewedUpdateOperation PackageOperation() => Build("git", "1.0.0", "2.0.0", UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3);

    private static ReviewedUpdateOperation Build(string packageId, string installed, string candidate, UpdateRouteContract route)
    {
        DateTimeOffset at = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        ImmutableUpdateJob job = new UpdateJobReview(Guid.Parse("73000000-0000-0000-0000-000000000001"),
            Guid.Parse("73000000-0000-0000-0000-000000000002"), new UpdateJobSelection("choco:" + packageId,
                UpdateProvider.Chocolatey, UpdateSource.ChocolateyCommunity, packageId, installed, candidate, UpdateScope.Machine,
                UpdateArchitecture.Neutral, UpdateChannel.Stable, "mapping-v2")).Confirm(at);
        return new(job, "S-1-5-21-11-22-33-1001", "choco:" + packageId, "machine:choco",
            ReviewedUpdateOperation.ProfileEvidencePrefix + Convert.ToHexString(SHA256.HashData("client profile"u8)), at, route,
            ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled), ReviewedUpdateArchitecture.ProviderScriptSelected());
    }
}
