using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// FR-UPD-15, FR-UI-03/04, FR-HIS-01; Chocolatey Confirm design §2 (C1–C8), §4.1–§4.3 and owner decisions 1–2 (§9): the admitted
/// review screen (whichever admission value is compiled in, through the test-only factory), the earlier-update attention rule
/// (C6) and the result texts of one Confirm press. Display only: the session re-checks every condition on the press
/// (<see cref="ChocolateyClientUpdateReviewSessionTests"/>).
/// </summary>
[TestClass]
public sealed class ChocolateyClientUpdateAdmittedViewModelTests
{
    private const string User = "S-1-5-21-11-22-33-1001";
    private static readonly ChocolateyClientReleaseCatalogFacts Fixture = new(["2.7.5", "2.8.0"], "Chocolatey Software, Inc");
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private static ChocolateyClientUpdateViewModel Admitted(string verified = "2.7.5",
        ReviewedRouteSplitTokenState token = ReviewedRouteSplitTokenState.SplitTokenLimited) =>
        ChocolateyClientUpdateViewModel.OpenWithAdmissionForTests(true, Fixture, ChocolateyInstalledClientVerification.Verified(verified), "2.7.5", () => token);

    private static ChocolateyClientUpdateReviewState Ready(bool live = true, bool fresh = true, string source = "2.7.5", string target = "2.8.0") =>
        new(ChocolateyClientUpdateReviewStage.Ready, live, fresh, false, false, source, target, Now + TimeSpan.FromMinutes(30));

    [TestMethod]
    public void ALiveReviewForExactlyTheOfferEnablesConfirmWithTheExactTarget()
    {
        ChocolateyClientUpdateViewModel model = Admitted().WithReview(Ready());
        Assert.IsTrue(model.Admitted);
        Assert.AreEqual(0, model.UnavailableReasons.Count, string.Join(", ", model.UnavailableReasons));
        Assert.IsTrue(model.CanConfirm);
        Assert.IsTrue(model.IsConfirmEnabled);
        Assert.IsTrue(model.IsPrimaryActionEnabled);
        Assert.IsFalse(model.OffersCheckAgain);
        Assert.AreEqual("Update Chocolatey to 2.8.0", model.PrimaryActionText);
        Assert.AreEqual("Update Chocolatey to 2.8.0", model.Title);
        Assert.AreEqual("Update Chocolatey to 2.8.0 after review", model.ConfirmAutomationName);
        Assert.AreEqual("Chocolatey 2.7.5 (verified) · 2.8.0 is a newer reviewed release", model.Summary);
        StringAssert.DoesNotMatch(model.OfferText, new System.Text.RegularExpressions.Regex("not (yet enabled|available yet)"));
        ReviewedDisplayField change = model.Fields.Single(field => field.Label == ChocolateyClientUpdateViewModel.ChangeLabel);
        Assert.AreEqual("Chocolatey 2.7.5 → 2.8.0", change.Text);
        Assert.AreEqual(3, model.Fields.ToList().IndexOf(change), "the change follows the offer");
        ReviewedDisplayField expires = model.Fields[^1];
        Assert.AreEqual(ChocolateyClientUpdateViewModel.ExpiresLabel, expires.Label);
        StringAssert.StartsWith(expires.Text, "This review expires at ");
        StringAssert.Contains(model.EntryAutomationName, ChocolateyClientUpdateViewModel.EntryAutomationDetailAdmitted);
    }

    [TestMethod]
    public void TheLockedViewNeverEnablesConfirmEvenWithALiveReview()
    {
        ChocolateyClientUpdateViewModel model = ChocolateyClientUpdateViewModel.OpenWithAdmissionForTests(false, Fixture,
            ChocolateyInstalledClientVerification.Verified("2.7.5"), "2.7.5", () => ReviewedRouteSplitTokenState.SplitTokenLimited).WithReview(Ready());
        Assert.IsFalse(model.Admitted);
        CollectionAssert.AreEqual(new[] { ChocolateyClientUpdateUnavailableReason.NotAdmittedPendingReview }, model.UnavailableReasons.ToArray());
        Assert.IsFalse(model.CanConfirm);
        Assert.IsFalse(model.IsPrimaryActionEnabled);
        Assert.IsFalse(model.OffersCheckAgain);
        Assert.IsFalse(model.Fields.Any(field => field.Label is ChocolateyClientUpdateViewModel.ChangeLabel or ChocolateyClientUpdateViewModel.ExpiresLabel));
        StringAssert.EndsWith(model.Title, ChocolateyClientUpdateViewModel.NotYetEnabledSuffix);
    }

    [TestMethod]
    public void ThePublicFactoryReadsTheCompiledAdmission()
    {
        ChocolateyClientUpdateViewModel model = ChocolateyClientUpdateViewModel.Open(Fixture, ChocolateyInstalledClientVerification.Verified("2.7.5"), "2.7.5",
            () => ReviewedRouteSplitTokenState.SplitTokenLimited);
        Assert.AreEqual(ChocolateyClientUpdateAdmission.IsOwnerApproved, model.Admitted);
        Assert.AreEqual(model.Admitted, model.WithReview(Ready()).Admitted, "WithReview keeps the admission");
        Assert.AreEqual(model.Admitted, model.WithVerification(ChocolateyInstalledClientVerification.Checking).Admitted, "WithVerification keeps the admission");
        Assert.AreEqual(ChocolateyClientUpdateAdmission.IsOwnerApproved
            ? ChocolateyClientUpdateViewModel.DetailCopyAdmitted : ChocolateyClientUpdateViewModel.DetailCopy, ChocolateyClientUpdateViewModel.CurrentDetailCopy);
    }

    /// <summary>Design §4.2: each review state leaves exactly its one reason, and only an expired review offers "Check again".</summary>
    [TestMethod]
    [DataRow(ChocolateyClientUpdateReviewStage.NotPrepared, true, true, "2.7.5", "2.8.0", ChocolateyClientUpdateUnavailableReason.PreparingReview, false)]
    [DataRow(ChocolateyClientUpdateReviewStage.Preparing, true, true, "2.7.5", "2.8.0", ChocolateyClientUpdateUnavailableReason.PreparingReview, false)]
    [DataRow(ChocolateyClientUpdateReviewStage.PrepareFailed, false, true, null, null, ChocolateyClientUpdateUnavailableReason.ReviewNotPrepared, false)]
    [DataRow(ChocolateyClientUpdateReviewStage.Ready, true, true, "2.6.0", "2.8.0", ChocolateyClientUpdateUnavailableReason.ReviewNotPrepared, false)]
    [DataRow(ChocolateyClientUpdateReviewStage.Ready, true, true, "2.7.5", "2.9.0", ChocolateyClientUpdateUnavailableReason.ReviewNotPrepared, false)]
    [DataRow(ChocolateyClientUpdateReviewStage.Ready, false, true, "2.7.5", "2.8.0", ChocolateyClientUpdateUnavailableReason.ReviewExpired, true)]
    [DataRow(ChocolateyClientUpdateReviewStage.Ready, true, false, "2.7.5", "2.8.0", ChocolateyClientUpdateUnavailableReason.ReviewExpired, true)]
    [DataRow(ChocolateyClientUpdateReviewStage.Running, true, true, "2.7.5", "2.8.0", ChocolateyClientUpdateUnavailableReason.ReviewExpired, true)]
    [DataRow(ChocolateyClientUpdateReviewStage.Finished, false, true, "2.7.5", "2.8.0", ChocolateyClientUpdateUnavailableReason.ReviewExpired, true)]
    [DataRow(ChocolateyClientUpdateReviewStage.Refused, false, true, "2.7.5", "2.8.0", ChocolateyClientUpdateUnavailableReason.ReviewExpired, true)]
    public void EachReviewStateLeavesItsOneReason(ChocolateyClientUpdateReviewStage stage, bool live, bool fresh, string? source, string? target,
        ChocolateyClientUpdateUnavailableReason expected, bool checkAgain)
    {
        ChocolateyClientUpdateViewModel model = Admitted().WithReview(new(stage, live, fresh, false, false, source, target, null));
        CollectionAssert.AreEqual(new[] { expected }, model.UnavailableReasons.ToArray());
        Assert.IsFalse(model.CanConfirm);
        Assert.AreEqual(checkAgain, model.OffersCheckAgain);
        Assert.AreEqual(checkAgain, model.IsPrimaryActionEnabled);
        Assert.AreEqual(checkAgain ? ChocolateyClientUpdateViewModel.CheckAgainText : ChocolateyClientUpdateViewModel.ConfirmText, model.PrimaryActionText);
        Assert.AreEqual(ChocolateyClientUpdateViewModel.Text(expected), model.UnavailableFields.Single().Text);
        Assert.IsFalse(model.Fields.Any(field => field.Label == ChocolateyClientUpdateViewModel.ChangeLabel), "no change line without a matching live review");
    }

    [TestMethod]
    public void AnEarlierUpdateNeedingAttentionOrAnotherTaskBlocksAndHidesTheReviewState()
    {
        ChocolateyClientUpdateViewModel attention = Admitted().WithReview(Ready() with { EarlierUpdateNeedsAttention = true });
        CollectionAssert.AreEqual(new[] { ChocolateyClientUpdateUnavailableReason.EarlierUpdateNeedsAttention }, attention.UnavailableReasons.ToArray());
        Assert.IsTrue(attention.OffersCheckAgain, "a fresh check is what clears it");
        Assert.AreEqual(ChocolateyClientUpdateViewModel.EarlierUpdateNeedsAttentionText, attention.UnavailableFields.Single().Text);

        ChocolateyClientUpdateViewModel busy = Admitted().WithReview(Ready() with { AnotherTaskRunning = true });
        CollectionAssert.AreEqual(new[] { ChocolateyClientUpdateUnavailableReason.AnotherTaskRunning }, busy.UnavailableReasons.ToArray());
        Assert.IsFalse(busy.OffersCheckAgain);
        Assert.IsFalse(busy.CanConfirm);

        ChocolateyClientUpdateViewModel both = Admitted().WithReview(Ready(live: false) with { EarlierUpdateNeedsAttention = true, AnotherTaskRunning = true });
        CollectionAssert.AreEqual(new[] { ChocolateyClientUpdateUnavailableReason.EarlierUpdateNeedsAttention, ChocolateyClientUpdateUnavailableReason.AnotherTaskRunning },
            both.UnavailableReasons.ToArray());
        Assert.IsFalse(both.OffersCheckAgain);
    }

    [TestMethod]
    [DataRow(ReviewedRouteSplitTokenState.NoSplitToken, ChocolateyClientUpdateUnavailableReason.SplitTokenRequired)]
    [DataRow(ReviewedRouteSplitTokenState.DesktopTokenUnsupported, ChocolateyClientUpdateUnavailableReason.DesktopTokenUnsupported)]
    [DataRow(ReviewedRouteSplitTokenState.Unknown, ChocolateyClientUpdateUnavailableReason.DesktopTokenUnknown)]
    public void ATokenThatIsNotLimitedBlocksEvenALiveReview(ReviewedRouteSplitTokenState token, ChocolateyClientUpdateUnavailableReason expected)
    {
        ChocolateyClientUpdateViewModel model = Admitted(token: token).WithReview(Ready());
        CollectionAssert.AreEqual(new[] { expected }, model.UnavailableReasons.ToArray());
        Assert.IsFalse(model.CanConfirm);
        Assert.IsFalse(model.IsPrimaryActionEnabled);
    }

    [TestMethod]
    public void NoOfferHidesThePrimaryButtonAndNeverShowsAReviewReason()
    {
        ChocolateyClientUpdateViewModel newest = Admitted("2.8.0").WithReview(Ready(source: "2.8.0"));
        CollectionAssert.AreEqual(new[] { ChocolateyClientUpdateUnavailableReason.NoNewerReviewedRelease }, newest.UnavailableReasons.ToArray());
        Assert.AreEqual(string.Empty, newest.PrimaryActionText, "verified and nothing offered: no primary button");
        Assert.IsFalse(newest.IsPrimaryActionEnabled);

        ChocolateyClientUpdateViewModel checking = ChocolateyClientUpdateViewModel.OpenWithAdmissionForTests(true, Fixture, ChocolateyInstalledClientVerification.Checking,
            "2.7.5", () => ReviewedRouteSplitTokenState.SplitTokenLimited).WithReview(Ready());
        CollectionAssert.AreEqual(new[] { ChocolateyClientUpdateUnavailableReason.InstalledReleaseChecking }, checking.UnavailableReasons.ToArray());
        Assert.AreEqual(ChocolateyClientUpdateViewModel.ConfirmText, checking.PrimaryActionText);
        Assert.IsFalse(checking.IsPrimaryActionEnabled);
    }

    // ---- C6: the earlier-update attention rule (owner decision 2). ----

    [TestMethod]
    public void UnreadableHistoryFailsClosed()
    {
        Assert.IsTrue(ChocolateyClientUpdateAttention.EarlierUpdateNeedsAttention(null, Now));
        Assert.IsFalse(ChocolateyClientUpdateAttention.EarlierUpdateNeedsAttention([], Now));
        Assert.IsTrue(ChocolateyClientUpdateAttention.EarlierUpdateNeedsAttention([null!], Now), "a null row");
        ReviewedUpdateHistoryRecord success = Record(UpdateRouteContract.ChocolateyClientSelfUpdateV1, UpdateDispatchOutcome.Success, UpdateDispatchSettlementState.OwnedWorkStopped, Now);
        Assert.IsTrue(ChocolateyClientUpdateAttention.EarlierUpdateNeedsAttention([success with { IntegrityVerified = false }], Now), "an unverified row");
        Assert.IsTrue(ChocolateyClientUpdateAttention.EarlierUpdateNeedsAttention([success with { Operation = null }], Now), "a row without its operation");
        Assert.AreEqual(50, ChocolateyClientUpdateAttention.HistoryWindow);
    }

    [TestMethod]
    [DataRow(UpdateDispatchOutcome.Success, UpdateDispatchSettlementState.OwnedWorkStopped, false)]
    [DataRow(UpdateDispatchOutcome.ElevationDenied, UpdateDispatchSettlementState.InvocationUnknown, false)]
    [DataRow(UpdateDispatchOutcome.Refused, UpdateDispatchSettlementState.NotInvoked, false)]
    [DataRow(UpdateDispatchOutcome.Failed, UpdateDispatchSettlementState.NotInvoked, false)]
    [DataRow(UpdateDispatchOutcome.Unverified, UpdateDispatchSettlementState.OwnedWorkStopped, true)]
    [DataRow(UpdateDispatchOutcome.Failed, UpdateDispatchSettlementState.OwnedWorkStopped, true)]
    [DataRow(UpdateDispatchOutcome.Refused, UpdateDispatchSettlementState.OwnedWorkStopped, true)]
    [DataRow(UpdateDispatchOutcome.Cancelled, UpdateDispatchSettlementState.OwnedWorkUnsettled, true)]
    [DataRow(UpdateDispatchOutcome.Uncertain, UpdateDispatchSettlementState.InvocationUnknown, true)]
    [DataRow(UpdateDispatchOutcome.RestartRequired, UpdateDispatchSettlementState.OwnedWorkStopped, true)]
    public void AnyNonSuccessAfterTheHelperMayHaveStartedNeedsAttention(UpdateDispatchOutcome outcome, UpdateDispatchSettlementState settlement, bool blocks)
    {
        ReviewedUpdateHistoryRecord client = Record(UpdateRouteContract.ChocolateyClientSelfUpdateV1, outcome, settlement, Now);
        Assert.AreEqual(blocks, ChocolateyClientUpdateAttention.EarlierUpdateNeedsAttention([client], Now), "recorded at the check start");
        Assert.AreEqual(blocks, ChocolateyClientUpdateAttention.EarlierUpdateNeedsAttention([client], Now - TimeSpan.FromMinutes(5)), "recorded after the check start");
        Assert.IsFalse(ChocolateyClientUpdateAttention.EarlierUpdateNeedsAttention([client], Now + TimeSpan.FromTicks(1)), "a check started after it clears it");
        ReviewedUpdateHistoryRecord package = Record(UpdateRouteContract.WinGetUserExactV3, outcome, settlement, Now);
        Assert.IsFalse(ChocolateyClientUpdateAttention.EarlierUpdateNeedsAttention([package], Now), "other routes are not client updates");
    }

    [TestMethod]
    public void AnUnfinishedClientJobNeedsAttentionFromItsClaimTime()
    {
        ReviewedUpdateHistoryRecord claimed = Record(UpdateRouteContract.ChocolateyClientSelfUpdateV1, UpdateDispatchOutcome.Success,
            UpdateDispatchSettlementState.OwnedWorkStopped, Now) with { State = UpdateDispatchState.DispatchClaimed, Terminal = null, ClaimedAt = Now };
        Assert.IsTrue(ChocolateyClientUpdateAttention.EarlierUpdateNeedsAttention([claimed], Now));
        Assert.IsFalse(ChocolateyClientUpdateAttention.EarlierUpdateNeedsAttention([claimed], Now + TimeSpan.FromTicks(1)));
        ReviewedUpdateHistoryRecord uncertain = claimed with { State = UpdateDispatchState.Uncertain };
        Assert.IsTrue(ChocolateyClientUpdateAttention.EarlierUpdateNeedsAttention([uncertain], Now));
        ReviewedUpdateHistoryRecord noTerminal = claimed with { State = UpdateDispatchState.Final };
        Assert.IsTrue(ChocolateyClientUpdateAttention.EarlierUpdateNeedsAttention([noTerminal], Now), "a final row without a readable result");
    }

    // ---- The result of one press (design §4.3). ----

    [TestMethod]
    public void RefusalsSayNothingWasStarted()
    {
        foreach (ChocolateyClientUpdateRefusal refusal in Enum.GetValues<ChocolateyClientUpdateRefusal>())
        {
            ChocolateyClientUpdateRunView view = ChocolateyClientUpdateRunPresentation.Present(new(refusal, null), null);
            Assert.AreEqual(ChocolateyClientUpdateRunPresentation.NotStartedHeading, view.Heading);
            Assert.IsFalse(view.Succeeded);
            StringAssert.StartsWith(view.Lines.Single(), ChocolateyClientUpdateRunPresentation.NothingStartedPrefix + " ");
        }
        Assert.AreEqual("Nothing was started. " + ChocolateyReviewDisclosurePresentation.SplitTokenRequiredText,
            ChocolateyClientUpdateRunPresentation.RefusalText(ChocolateyClientUpdateRefusal.SplitTokenNotLimited));
        Assert.AreEqual("Nothing was started. " + ChocolateyClientUpdateViewModel.EarlierUpdateNeedsAttentionText,
            ChocolateyClientUpdateRunPresentation.RefusalText(ChocolateyClientUpdateRefusal.EarlierUpdateNeedsAttention));
        Assert.AreEqual("Nothing was started. " + ChocolateyClientUpdateViewModel.NotAdmittedText,
            ChocolateyClientUpdateRunPresentation.RefusalText(ChocolateyClientUpdateRefusal.NotAdmitted));
        Assert.AreEqual("Nothing was started. " + ChocolateyClientUpdateViewModel.ReviewExpiredText,
            ChocolateyClientUpdateRunPresentation.RefusalText(ChocolateyClientUpdateRefusal.CheckTooOld));
    }

    [TestMethod]
    public void WorkflowResultsAreShownWithTheRecordedHistoryText()
    {
        ReviewedUpdateDispatchResult notStarted = Result(ReviewedUpdateDispatchDisposition.NotStarted, null, "The review changed.");
        ChocolateyClientUpdateRunView view = ChocolateyClientUpdateRunPresentation.Present(new(ChocolateyClientUpdateRefusal.None, notStarted), null);
        Assert.AreEqual(ChocolateyClientUpdateRunPresentation.NotStartedHeading, view.Heading);
        Assert.AreEqual("Nothing was started. The review changed.", view.Lines.Single());

        ReviewedUpdateDispatchResult denied = Result(ReviewedUpdateDispatchDisposition.Finalized, UpdateDispatchOutcome.ElevationDenied, "declined");
        view = ChocolateyClientUpdateRunPresentation.Present(new(ChocolateyClientUpdateRefusal.None, denied), null);
        Assert.AreEqual(ChocolateyClientUpdateRunPresentation.NotStartedHeading, view.Heading);
        Assert.AreEqual(ChocolateyClientUpdateRunPresentation.ElevationDeniedText, view.Lines.Single());

        ReviewedUpdateDispatchResult uncertain = Result(ReviewedUpdateDispatchDisposition.Finalized, UpdateDispatchOutcome.Uncertain, "lost track");
        view = ChocolateyClientUpdateRunPresentation.Present(new(ChocolateyClientUpdateRefusal.None, uncertain), null);
        Assert.AreEqual(ChocolateyClientUpdateRunPresentation.UncertainHeading, view.Heading);
        Assert.AreEqual("lost track", view.Lines.Single(), "the workflow's recorded message when the record cannot be read back");

        ReviewedUpdateHistoryRecord record = Record(UpdateRouteContract.ChocolateyClientSelfUpdateV1, UpdateDispatchOutcome.Unverified, UpdateDispatchSettlementState.OwnedWorkStopped, Now);
        ReviewedUpdateHistoryView recorded = ChocolateyClientUpdateRunPresentation.RecordedView([Record(UpdateRouteContract.WinGetUserExactV3, UpdateDispatchOutcome.Success,
            UpdateDispatchSettlementState.OwnedWorkStopped, Now), record], record.JobId)!;
        Assert.AreEqual(ReviewedUpdateHistoryPresentation.Present(record).Status, recorded.Status);
        ReviewedUpdateDispatchResult unverified = Result(ReviewedUpdateDispatchDisposition.Finalized, UpdateDispatchOutcome.Unverified, "not verified");
        view = ChocolateyClientUpdateRunPresentation.Present(new(ChocolateyClientUpdateRefusal.None, unverified), recorded);
        Assert.AreEqual(ChocolateyClientUpdateRunPresentation.NotVerifiedHeading, view.Heading);
        Assert.AreEqual(recorded.Status, view.Lines[0]);
        Assert.IsFalse(view.Succeeded);
        Assert.IsNull(ChocolateyClientUpdateRunPresentation.RecordedView(null, record.JobId));
        Assert.IsNull(ChocolateyClientUpdateRunPresentation.RecordedView([record], Guid.NewGuid()));

        Assert.AreEqual(ChocolateyClientUpdateRunPresentation.CheckingAgainText, ChocolateyClientUpdateRunPresentation.PhaseText(ChocolateyClientUpdatePhase.CheckingAgain));
        Assert.AreEqual(ChocolateyClientUpdateRunPresentation.WaitingForPermissionText, ChocolateyClientUpdateRunPresentation.PhaseText(ChocolateyClientUpdatePhase.WaitingForPermission));
        Assert.AreEqual(ChocolateyClientUpdateRunPresentation.VerifyingText, ChocolateyClientUpdateRunPresentation.PhaseText(ChocolateyClientUpdatePhase.Verifying));
        Assert.AreEqual(ChocolateyClientUpdateRunPresentation.CheckingAgainText, ChocolateyClientUpdateRunPresentation.PhaseText((ChocolateyClientUpdatePhase)99));
    }

    private static ReviewedUpdateDispatchResult Result(ReviewedUpdateDispatchDisposition disposition, UpdateDispatchOutcome? outcome, string message) =>
        new(disposition, outcome, message, ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled),
            UpdateRouteContract.ChocolateyClientSelfUpdateV1, ReviewedProviderGuardContract.ChocolateyClientSelfUpdate, null);

    private static ReviewedUpdateHistoryRecord Record(UpdateRouteContract route, UpdateDispatchOutcome outcome, UpdateDispatchSettlementState settlement, DateTimeOffset occurredAt)
    {
        DateTimeOffset reviewedAt = occurredAt - TimeSpan.FromMinutes(2);
        ReviewedUpdateOperation operation;
        if (route == UpdateRouteContract.ChocolateyClientSelfUpdateV1)
        {
            ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection("choco:chocolatey", UpdateProvider.Chocolatey,
                UpdateSource.ChocolateyCommunity, ReviewedUpdateOperation.ChocolateyClientPackageId, "2.7.5", "2.8.0", UpdateScope.Machine,
                UpdateArchitecture.Neutral, UpdateChannel.Stable, "chocolatey-client-v1")).Confirm(reviewedAt);
            operation = new(job, User, "choco:chocolatey", "machine:chocolatey", ReviewedUpdateOperation.ProfileEvidencePrefix + new string('C', 64),
                reviewedAt - TimeSpan.FromSeconds(5), route, ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled),
                ReviewedUpdateArchitecture.ProviderScriptSelected());
        }
        else
        {
            ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new("winget:contoso.widget", UpdateProvider.WinGet, UpdateSource.WinGet,
                "Contoso.Widget", "1.0", "1.1", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "mapping-v1")).Confirm(reviewedAt);
            operation = new(job, User, "native:1", "context:1", "review:1", reviewedAt - TimeSpan.FromSeconds(1), route, ReviewedInstallerMode.Selected(InstallerMode.Silent));
        }
        return new(operation.JobId, operation, UpdateDispatchState.Final, occurredAt - TimeSpan.FromMinutes(1),
            new ReviewedUpdateTerminalRecord(outcome, occurredAt, 0, settlement, "fixture"), true);
    }
}
