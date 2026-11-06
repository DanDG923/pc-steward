using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// FR-UPD-15, FR-UI-03/04, TM-03/04/07/13; Chocolatey package Confirm addendum §3–§4: the admitted package review's view model.
/// Every row runs through the test-only factory, so both admission values stay pinned whichever is compiled in.
/// </summary>
[TestClass]
public sealed class ChocolateyPackageUpdateViewModelTests
{
    private const string User = "S-1-5-21-11-22-33-1001";
    private const string Package = "ripgrep";
    private static readonly DateTimeOffset ReviewedAt = new(2026, 11, 6, 12, 0, 30, TimeSpan.Zero);
    private static readonly DateTimeOffset ResolvedAt = ReviewedAt - TimeSpan.FromSeconds(30);
    private static readonly string Evidence = ReviewedUpdateOperation.ProfileEvidencePrefix + new string('D', 64);

    [TestMethod]
    public void AReadyLiveFreshReviewShowsEveryDisclosureLineTheChangeAndTheExpiryAndCanConfirm()
    {
        ChocolateyPackageUpdateViewModel model = Open().WithReview(Ready());
        Assert.IsTrue(model.CanConfirm, string.Join(" | ", model.UnavailableTexts));
        Assert.IsTrue(model.IsPrimaryActionEnabled);
        Assert.AreEqual("Update ripgrep", model.PrimaryText);
        Assert.AreEqual(0, model.UnavailableFields.Count);
        Assert.AreEqual("ripgrep 14.1.0 → 15.2.0 (Chocolatey, Machine)", model.Title);
        string[] labels = model.Fields.Select(field => field.Label).ToArray();
        CollectionAssert.AreEqual(new[] { "Candidate", "Candidate checked", "Your user cache", "Installer mode", "Package scripts and downloads",
            "Running installers", ChocolateyPackageUpdateViewModel.ChangeLabel, ChocolateyPackageUpdateViewModel.ExpiresLabel }, labels);
        Assert.AreEqual("ripgrep 14.1.0 → 15.2.0", model.Fields.Single(field => field.Label == ChocolateyPackageUpdateViewModel.ChangeLabel).Text);
        Assert.AreEqual("2026-11-06 12:30 UTC", model.Fields.Single(field => field.Label == ChocolateyPackageUpdateViewModel.ExpiresLabel).Text);
        Assert.AreEqual(ChocolateyReviewDisclosurePresentation.UserCacheDisclosure, model.Fields.Single(field => field.Label == "Your user cache").Text);
        StringAssert.StartsWith(model.Summary, "Ready to review: ripgrep 14.1.0 → 15.2.0.");
    }

    [TestMethod]
    public void ALibBadResidueIsDisclosed()
    {
        ChocolateyPackageUpdateViewModel model = Open().WithReview(Ready(libBad: true));
        Assert.AreEqual(ChocolateyReviewDisclosurePresentation.LibBadResidueText, model.Fields.Single(field => field.Label == "Earlier failed install").Text);
        Assert.IsTrue(model.CanConfirm);
    }

    [TestMethod]
    public void ALockedViewNeverConfirmsOrChecksAgain()
    {
        ChocolateyPackageUpdateViewModel model = Open(admitted: false).WithReview(Ready());
        Assert.IsFalse(model.CanConfirm);
        Assert.IsFalse(model.OffersCheckAgain);
        Assert.IsFalse(model.ShouldPrepare);
        Assert.AreEqual(string.Empty, model.PrimaryText);
        CollectionAssert.AreEqual(new[] { ChocolateyPackageUpdateUnavailableReason.NotAdmitted }, model.UnavailableReasons.ToArray());
    }

    [TestMethod]
    public void TrustedSourcesOffMeansNothingIsCheckedOrConfirmed()
    {
        ChocolateyPackageUpdateViewModel model = Open(trustedSources: false);
        Assert.IsFalse(model.ShouldPrepare);
        Assert.IsFalse(model.CanConfirm);
        Assert.IsFalse(model.OffersCheckAgain);
        CollectionAssert.AreEqual(new[] { ChocolateyPackageUpdateUnavailableReason.TrustedSourcesOff }, model.UnavailableReasons.ToArray());
        Assert.AreEqual(ChocolateyPackageUpdateViewModel.TrustedSourcesOffText, model.UnavailableTexts.Single());
    }

    [TestMethod]
    [DataRow(ReviewedRouteSplitTokenState.NoSplitToken, ChocolateyReviewDisclosurePresentation.SplitTokenRequiredText)]
    [DataRow(ReviewedRouteSplitTokenState.DesktopTokenUnsupported, ChocolateyReviewDisclosurePresentation.DesktopTokenUnsupportedText)]
    [DataRow(ReviewedRouteSplitTokenState.Unknown, ChocolateyReviewDisclosurePresentation.DesktopTokenUnknownText)]
    public void WithoutALimitedSplitTokenNothingIsPreparedAndTheReasonIsShown(ReviewedRouteSplitTokenState token, string text)
    {
        ChocolateyPackageUpdateViewModel model = Open(token: () => token);
        Assert.IsFalse(model.ShouldPrepare);
        Assert.IsFalse(model.CanConfirm);
        Assert.AreEqual(text, model.UnavailableTexts.Single());
        Assert.AreEqual(ChocolateyPackageUpdateViewModel.NotConfirmableSummary, model.Summary);
    }

    [TestMethod]
    public void AProbeFaultIsUnknownAndUnavailable()
    {
        ChocolateyPackageUpdateViewModel model = Open(token: () => throw new InvalidOperationException("probe"));
        Assert.AreEqual(ReviewedRouteSplitTokenState.Unknown, model.SplitTokenState);
        Assert.IsFalse(model.ShouldPrepare);
    }

    [TestMethod]
    public void TheOpenedViewIsCheckingAndPreparesOnlyWhenEverythingElseHolds()
    {
        ChocolateyPackageUpdateViewModel model = Open();
        Assert.IsTrue(model.ShouldPrepare);
        Assert.IsFalse(model.CanConfirm);
        Assert.IsFalse(model.OffersCheckAgain);
        Assert.AreEqual(string.Empty, model.PrimaryText);
        CollectionAssert.AreEqual(new[] { ChocolateyPackageUpdateUnavailableReason.Checking }, model.UnavailableReasons.ToArray());
        Assert.AreEqual(ChocolateyPackageUpdateViewModel.CheckingText, model.Summary);
        Assert.AreEqual(0, model.Fields.Count);
    }

    [TestMethod]
    public void AFailedCheckOffersCheckAgain()
    {
        ChocolateyPackageUpdateViewModel model = Open().WithReview(ChocolateyPackageUpdateReviewState.Checking with { Stage = ChocolateyClientUpdateReviewStage.PrepareFailed });
        Assert.IsFalse(model.CanConfirm);
        Assert.IsTrue(model.OffersCheckAgain);
        Assert.AreEqual(ChocolateyPackageUpdateViewModel.CheckAgainText, model.PrimaryText);
        CollectionAssert.AreEqual(new[] { ChocolateyPackageUpdateUnavailableReason.NoCheckedUpdate }, model.UnavailableReasons.ToArray());
    }

    [TestMethod]
    [DataRow("stale-check", ChocolateyPackageUpdateUnavailableReason.CheckTooOld)]
    [DataRow("expired", ChocolateyPackageUpdateUnavailableReason.ReviewExpired)]
    [DataRow("attention", ChocolateyPackageUpdateUnavailableReason.EarlierUpdateNeedsAttention)]
    public void EachAgedOrBlockedReviewOffersCheckAgainAndHidesTheChangeLines(string change, ChocolateyPackageUpdateUnavailableReason expected)
    {
        ChocolateyPackageUpdateReviewState state = change switch
        {
            "stale-check" => Ready() with { CheckFresh = false },
            "expired" => Ready() with { ReviewLive = false },
            _ => Ready() with { EarlierUpdateNeedsAttention = true },
        };
        ChocolateyPackageUpdateViewModel model = Open().WithReview(state);
        Assert.IsFalse(model.CanConfirm);
        Assert.IsTrue(model.OffersCheckAgain);
        Assert.AreEqual(ChocolateyPackageUpdateViewModel.CheckAgainText, model.PrimaryText);
        CollectionAssert.Contains(model.UnavailableReasons.ToArray(), expected);
        if (change != "attention")
            Assert.IsFalse(model.Fields.Any(field => field.Label is ChocolateyPackageUpdateViewModel.ChangeLabel or ChocolateyPackageUpdateViewModel.ExpiresLabel));
    }

    [TestMethod]
    public void AnIntentThatDoesNotMatchTheFrozenReviewIsAMismatch()
    {
        ChocolateyPackageUpdateReviewState state = Ready() with { Intent = Facts() with { CandidateVersion = "15.3.0" } };
        ChocolateyPackageUpdateViewModel model = Open().WithReview(state);
        Assert.IsFalse(model.CanConfirm);
        CollectionAssert.Contains(model.UnavailableTexts.ToArray(), ChocolateyReviewDisclosurePresentation.ReviewDataMismatchText);
    }

    [TestMethod]
    public void ThePublicFactoryFollowsTheCompiledAdmission()
    {
        ChocolateyPackageUpdateViewModel model = ChocolateyPackageUpdateViewModel.Open(Package, true, () => ReviewedRouteSplitTokenState.SplitTokenLimited);
        Assert.AreEqual(ChocolateyPackageUpdateAdmission.IsOwnerApproved, model.Admitted);
        Assert.AreEqual(ChocolateyPackageUpdateAdmission.IsOwnerApproved, model.ShouldPrepare);
    }

    [TestMethod]
    public void RefusalTextsSayNothingWasStarted()
    {
        foreach (ChocolateyClientUpdateRefusal refusal in Enum.GetValues<ChocolateyClientUpdateRefusal>())
            StringAssert.StartsWith(ChocolateyPackageUpdateRunPresentation.RefusalText(refusal), ChocolateyPackageUpdateRunPresentation.NothingStartedPrefix);
        StringAssert.Contains(ChocolateyPackageUpdateRunPresentation.RefusalText(ChocolateyClientUpdateRefusal.CheckTooOld), "10 minutes");
    }

    private static ChocolateyPackageUpdateViewModel Open(bool admitted = true, bool trustedSources = true, Func<ReviewedRouteSplitTokenState>? token = null) =>
        ChocolateyPackageUpdateViewModel.OpenWithAdmissionForTests(admitted, Package, trustedSources, token ?? (() => ReviewedRouteSplitTokenState.SplitTokenLimited));

    private static ChocolateyPackageUpdateReviewState Ready(bool libBad = false)
    {
        ReviewedUpdateOperation operation = Operation();
        return new(ChocolateyClientUpdateReviewStage.Ready, true, true, false, operation, Facts(libBad), operation.ReviewedAt + TimeSpan.FromMinutes(30));
    }

    private static ChocolateyReviewIntentFacts Facts(bool libBad = false) =>
        new(Package, "14.1.0", "15.2.0", User, Evidence, ReviewedCandidateProvenance.UserCacheIntent, libBad, ResolvedAt, ResolvedAt + TimeSpan.FromSeconds(1));

    private static ReviewedUpdateOperation Operation()
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection("choco:" + Package, UpdateProvider.Chocolatey,
            UpdateSource.ChocolateyCommunity, Package, "14.1.0", "15.2.0", UpdateScope.Machine, UpdateArchitecture.Neutral, UpdateChannel.Stable,
            "chocolatey-package-v1")).Confirm(ReviewedAt);
        return new(job, User, "choco:" + Package, "machine:chocolatey", Evidence, ResolvedAt + TimeSpan.FromSeconds(1),
            UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3, ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled),
            ReviewedUpdateArchitecture.ProviderScriptSelected());
    }
}
