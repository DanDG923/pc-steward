using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01, FR-HIS-01, FR-UI-03/04, TM-03/04/07/13; Task 50 admission obligation 5 (N6 review M1), UI-level and
/// portable: the view models the Desktop binds for the runtime-limit Chocolatey review and for reviewed-update history.
/// Every disclosure and history field is a bound property and a display line; the split-token state is probed on every
/// review; Confirm is enabled only when <c>CanConfirm</c> holds (never today) and confirmation goes only through
/// <c>ReviewedUpdateReview.Confirm</c>.
/// </summary>
[TestClass]
public sealed class ReviewedRouteViewModelTests
{
    private const string User = "S-1-5-21-11-22-33-1001";
    private static readonly DateTimeOffset ReviewedAt = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ResolvedAt = new(2026, 10, 30, 13, 59, 15, TimeSpan.FromHours(2)); // 11:59:15 UTC
    private static readonly string Evidence = ReviewedUpdateOperation.ProfileEvidencePrefix + new string('A', 64);
    private static readonly string[] MinimalHistoryLabels = ["Outcome", "Route", "Installer mode", "Candidate evidence", "Helper report", "Other updates"];

    // ---- Review ---------------------------------------------------------------------------------------------------------

    [TestMethod]
    public void EveryDisclosureFieldIsBoundAndShownBeforeConfirmationForAMatchingIntent()
    {
        ReviewedUpdateDraft draft = Draft(Chocolatey());
        ChocolateyReviewedRouteReviewViewModel model = ChocolateyReviewedRouteReviewViewModel.Open(draft, Facts(libBad: true),
            () => ReviewedRouteSplitTokenState.SplitTokenLimited, new Clock(ReviewedAt.AddSeconds(10)));
        ChocolateyReviewDisclosureView expected = ChocolateyReviewDisclosurePresentation.Present(draft.Operation, Facts(libBad: true),
            ReviewedRouteSplitTokenState.SplitTokenLimited);

        // Every bound property is the presentation's field.
        Assert.AreEqual(expected.Title, model.Title);
        Assert.AreEqual(expected.CandidateIntent, model.CandidateIntent);
        Assert.AreEqual(ResolvedAt, model.IntentResolvedAtUtc);
        Assert.AreEqual(TimeSpan.Zero, model.IntentResolvedAtUtc!.Value.Offset, "the intent's own time, in UTC");
        Assert.AreEqual("Candidate checked in your user cache at 2026-10-30 11:59:15 UTC.", model.IntentTime);
        Assert.AreEqual(ChocolateyReviewDisclosurePresentation.UserCacheDisclosure, model.UserCacheDisclosure);
        Assert.AreEqual(expected.InstallerMode, model.InstallerMode);
        StringAssert.Contains(model.InstallerMode, "package's own scripts");
        Assert.AreEqual(ReviewedUpdateProviderDisclosure.ChocolateyCommunityScriptRuntimeLimits, model.RuntimeLimits);
        Assert.AreEqual(ReviewedUpdateLingeringProcesses.ReviewDisclosure, model.LingeringProcesses);
        Assert.AreEqual(ChocolateyReviewDisclosurePresentation.LibBadResidueText, model.LibBadResidue);
        // Task 50 activation (ADR-031 owner decision 7): the exact shape is admitted, so a split token and a matching intent
        // leave no unavailability reason.
        Assert.AreEqual(0, model.UnavailableReasons.Count);
        Assert.AreEqual(0, model.UnavailableTexts.Count);
        Assert.IsFalse(model.IsUnavailable);
        Assert.AreEqual(ReviewedRouteSplitTokenState.SplitTokenLimited, model.SplitTokenState);

        // Every field is rendered, in order, before the confirmation.
        CollectionAssert.AreEqual(new[]
        {
            expected.CandidateIntent, expected.IntentTime, expected.UserCacheDisclosure, expected.InstallerMode, expected.RuntimeLimits,
            expected.LingeringProcesses, expected.LibBadResidue,
        }, model.Fields.Select(field => field.Text).ToArray());
        CollectionAssert.AreEqual(expected.UnavailableTexts.ToArray(), model.UnavailableFields.Select(field => field.Text).ToArray());
        Assert.IsTrue(model.Fields.Concat(model.UnavailableFields).All(field =>
            field.Label.Length != 0 && field.AutomationName == field.Label + ": " + field.Text), "every line has an accessible name");

        // Confirm is enabled exactly when CanConfirm holds and goes only through ReviewedUpdateReview.Confirm, once; it
        // dispatches nothing (this view model has no dispatch path). The Desktop prepares no draft, so it never reaches this
        // state there (D4 = G-0: the owner confirms no Chocolatey review until a recorded probe PASS).
        Assert.IsTrue(ReviewedUpdateReview.HasSupportedMode(draft.Operation));
        Assert.IsTrue(model.CanConfirm);
        Assert.IsTrue(model.IsConfirmEnabled);
        LiveApprovedUpdate? approval = model.TryConfirm();
        Assert.IsNotNull(approval);
        Assert.AreSame(draft.Operation, approval.Operation);
        Assert.IsNull(model.TryConfirm(), "a review is confirmed at most once");
    }

    [TestMethod]
    public void WithoutAReviewTheFixedDisclosuresAndUnavailabilityAreStillShown()
    {
        ChocolateyReviewedRouteReviewViewModel model = ChocolateyReviewedRouteReviewViewModel.Open(null, Facts(),
            () => ReviewedRouteSplitTokenState.SplitTokenLimited);

        Assert.AreEqual(ChocolateyReviewDisclosurePresentation.NoReviewTitle, model.Title);
        Assert.AreEqual(ChocolateyReviewDisclosurePresentation.NoReviewCandidateText, model.CandidateIntent);
        Assert.IsNull(model.IntentResolvedAtUtc, "no reviewed intent, so no intent time even when facts are supplied");
        Assert.AreEqual(ChocolateyReviewDisclosurePresentation.IntentTimeUnavailableText, model.IntentTime);
        Assert.AreEqual(ChocolateyReviewDisclosurePresentation.UserCacheDisclosure, model.UserCacheDisclosure);
        Assert.AreEqual(ReviewedUpdateHistoryPresentation.Mode(ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled)),
            model.InstallerMode);
        Assert.AreEqual(ReviewedUpdateProviderDisclosure.ChocolateyCommunityScriptRuntimeLimits, model.RuntimeLimits);
        Assert.AreEqual(ReviewedUpdateLingeringProcesses.ReviewDisclosure, model.LingeringProcesses);
        Assert.IsNull(model.LibBadResidue);
        CollectionAssert.AreEqual(new[] { ReviewedRouteUnavailableReason.NotAdmittedPendingHostEvidence }, model.UnavailableReasons.ToArray());
        Assert.AreEqual(6, model.Fields.Count, "candidate, time, user cache, mode, runtime limits, running installers");
        AssertConfirmDisabled(model);
    }

    [TestMethod]
    [DataRow(ReviewedRouteSplitTokenState.NoSplitToken, ReviewedRouteUnavailableReason.SplitTokenRequired)]
    [DataRow(ReviewedRouteSplitTokenState.DesktopTokenUnsupported, ReviewedRouteUnavailableReason.DesktopTokenUnsupported)]
    [DataRow(ReviewedRouteSplitTokenState.Unknown, ReviewedRouteUnavailableReason.DesktopTokenUnknown)]
    [DataRow((ReviewedRouteSplitTokenState)99, ReviewedRouteUnavailableReason.DesktopTokenUnknown)]
    public void TheProbedSplitTokenStateAddsItsReasonWithOrWithoutAReview(ReviewedRouteSplitTokenState state, ReviewedRouteUnavailableReason reason)
    {
        foreach (ReviewedUpdateDraft? draft in new[] { null, Draft(Chocolatey()) })
        {
            ChocolateyReviewedRouteReviewViewModel model = ChocolateyReviewedRouteReviewViewModel.Open(draft, Facts(), () => state);
            // Task 50 activation: an admitted draft carries only the token reason; the no-review disclosure keeps its
            // not-admitted reason first.
            ReviewedRouteUnavailableReason[] expected = draft is null ? [ReviewedRouteUnavailableReason.NotAdmittedPendingHostEvidence, reason] : [reason];
            CollectionAssert.AreEqual(expected, model.UnavailableReasons.ToArray());
            Assert.AreEqual(ChocolateyReviewDisclosurePresentation.Text(reason), model.UnavailableFields[^1].Text);
            Assert.AreEqual(Enum.IsDefined(state) ? state : ReviewedRouteSplitTokenState.Unknown, model.SplitTokenState);
            AssertConfirmDisabled(model);
        }
    }

    [TestMethod]
    public void TheSplitTokenStateIsProbedFreshAtEveryReviewAndAProbeFaultIsUnknown()
    {
        Queue<ReviewedRouteSplitTokenState> states = new([ReviewedRouteSplitTokenState.SplitTokenLimited, ReviewedRouteSplitTokenState.NoSplitToken]);
        int probes = 0;
        ReviewedRouteSplitTokenState Probe() { probes++; return states.Dequeue(); }

        ChocolateyReviewedRouteReviewViewModel first = ChocolateyReviewedRouteReviewViewModel.Open(null, null, Probe);
        ChocolateyReviewedRouteReviewViewModel second = ChocolateyReviewedRouteReviewViewModel.Open(null, null, Probe);
        Assert.AreEqual(2, probes, "one probe per review, never cached");
        Assert.AreEqual(ReviewedRouteSplitTokenState.SplitTokenLimited, first.SplitTokenState);
        Assert.AreEqual(ReviewedRouteSplitTokenState.NoSplitToken, second.SplitTokenState);
        CollectionAssert.Contains(second.UnavailableReasons.ToArray(), ReviewedRouteUnavailableReason.SplitTokenRequired);
        CollectionAssert.DoesNotContain(first.UnavailableReasons.ToArray(), ReviewedRouteUnavailableReason.SplitTokenRequired);

        ChocolateyReviewedRouteReviewViewModel faulted = ChocolateyReviewedRouteReviewViewModel.Open(null, null,
            () => throw new InvalidOperationException("token unreadable"));
        Assert.AreEqual(ReviewedRouteSplitTokenState.Unknown, faulted.SplitTokenState);
        CollectionAssert.Contains(faulted.UnavailableReasons.ToArray(), ReviewedRouteUnavailableReason.DesktopTokenUnknown);
        AssertConfirmDisabled(faulted);
        Assert.ThrowsExactly<ArgumentNullException>(() => ChocolateyReviewedRouteReviewViewModel.Open(null, null, null!));
    }

    [TestMethod]
    public void AMismatchedIntentOrAnotherRouteIsShownAsUnavailableAndNeverConfirmable()
    {
        ChocolateyReviewedRouteReviewViewModel mismatch = ChocolateyReviewedRouteReviewViewModel.Open(Draft(Chocolatey()),
            Facts() with { CandidateVersion = "3.0.0" }, () => ReviewedRouteSplitTokenState.SplitTokenLimited);
        CollectionAssert.Contains(mismatch.UnavailableReasons.ToArray(), ReviewedRouteUnavailableReason.ReviewDataMismatch);
        Assert.IsNull(mismatch.IntentResolvedAtUtc);
        AssertConfirmDisabled(mismatch);

        // A WinGet draft passes the real review gate, yet this review is never confirmable for it.
        ReviewedUpdateDraft winget = Draft(WinGet());
        Assert.IsTrue(ReviewedUpdateReview.HasSupportedMode(winget.Operation));
        ChocolateyReviewedRouteReviewViewModel other = ChocolateyReviewedRouteReviewViewModel.Open(winget, Facts(),
            () => ReviewedRouteSplitTokenState.SplitTokenLimited, new Clock(ReviewedAt.AddSeconds(10)));
        CollectionAssert.AreEqual(new[] { ReviewedRouteUnavailableReason.ReviewDataMismatch }, other.UnavailableReasons.ToArray());
        Assert.IsNull(other.RuntimeLimits);
        Assert.AreEqual(5, other.Fields.Count, "no runtime-limit or residue line for another route");
        AssertConfirmDisabled(other);
    }

    [TestMethod]
    public void TheEntryPointAppliesOnlyToChocolateyCommunityRows()
    {
        Assert.IsTrue(ChocolateyReviewedRouteReviewViewModel.AppliesTo("chocolatey-community"));
        Assert.IsTrue(ChocolateyReviewedRouteReviewViewModel.AppliesTo("Chocolatey-Community"));
        foreach (string? other in new[] { null, "", "winget", "msstore", "chocolatey", "chocolatey-community ", "chocolatey-other" })
            Assert.IsFalse(ChocolateyReviewedRouteReviewViewModel.AppliesTo(other), other ?? "null");
        StringAssert.Contains(ChocolateyReviewedRouteReviewViewModel.EntryText, "(unavailable)");
        StringAssert.Contains(ChocolateyReviewedRouteReviewViewModel.EntryAutomationName, "unavailable");
        // WCAG 2.5.3 (ADR-021 best effort): the accessible name starts with the visible label.
        StringAssert.StartsWith(ChocolateyReviewedRouteReviewViewModel.EntryAutomationName, ChocolateyReviewedRouteReviewViewModel.EntryText);
        StringAssert.StartsWith(ChocolateyReviewedRouteReviewViewModel.NotStartedText, "No update was started.");
    }

    // ---- History --------------------------------------------------------------------------------------------------------

    [TestMethod]
    public void EveryHistoryFieldIsBoundAndShownForAReviewedOutcome()
    {
        ReviewedUpdateOperation operation = Chocolatey();
        ReviewedUpdateHistoryRecord record = new(operation.JobId, operation, UpdateDispatchState.Final, ReviewedAt,
            new(UpdateDispatchOutcome.Unverified, ReviewedAt.AddMinutes(1), 0, UpdateDispatchSettlementState.OwnedWorkStopped,
                ReviewedUpdateLingeringProcesses.Token + " " + ReviewedUpdateDispatchWorkflow.TargetNotDetectedMessage), true);
        ReviewedUpdateHistoryView view = ReviewedUpdateHistoryPresentation.Present(record);
        ReviewedUpdateHistoryItemViewModel model = ReviewedUpdateHistoryItemViewModel.From(view);

        Assert.AreEqual(view.Title, model.Title);
        Assert.AreEqual(view.Status, model.Status);
        Assert.AreEqual(ReviewedUpdateHistoryPresentation.Route(UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3), model.Route);
        Assert.AreEqual(ReviewedUpdateHistoryPresentation.TargetNotDetectedText, model.VerificationDetail);
        Assert.AreEqual(ReviewedUpdateLingeringProcesses.HistoryNote, model.LingeringProcessesNote);
        Assert.AreEqual(view.HelperReport, model.HelperReport);
        Assert.AreEqual(view.InstallerMode, model.InstallerMode);
        Assert.AreEqual(view.CandidateEvidence, model.CandidateEvidence);
        Assert.AreEqual(ReviewedUpdateProviderDisclosure.ChocolateyCommunityScriptRuntimeLimits, model.Disclosure);
        Assert.AreEqual(view.Diagnostic, model.Diagnostic);
        Assert.IsFalse(model.BlocksOtherUpdates);
        Assert.AreEqual(view.Title + ": " + view.Status, model.AutomationName);

        string[] texts = model.Fields.Select(field => field.Text).ToArray();
        foreach (string? text in new[] { view.Status, view.Route, view.VerificationDetail, view.LingeringProcessesNote, view.InstallerMode,
                     view.CandidateEvidence, view.Disclosure, view.Diagnostic })
            CollectionAssert.Contains(texts, text);
        Assert.AreEqual("Outcome", model.Fields[0].Label);
        Assert.AreEqual("Running installers", model.Fields[1].Label, "the lingering note sits beside the outcome");
    }

    [TestMethod]
    public void TheHelperReportAndBlockingStateAreShownWhenRecorded()
    {
        ReviewedUpdateOperation operation = Chocolatey();
        ReviewedUpdateHistoryView view = new("Title", "Status", "Mode", "Evidence", null, true, string.Empty, "Helper report text", "Route text",
            null, null);
        ReviewedUpdateHistoryItemViewModel model = ReviewedUpdateHistoryItemViewModel.From(view);
        Assert.AreEqual("Helper report text", model.HelperReport);
        Assert.IsTrue(model.BlocksOtherUpdates);
        CollectionAssert.AreEqual(MinimalHistoryLabels, model.Fields.Select(field => field.Label).ToArray(),
            "absent optional fields and an empty diagnostic are omitted");
        Assert.AreEqual(ReviewedUpdateHistoryItemViewModel.BlocksOtherUpdatesText, model.Fields[^1].Text);

        // A real uncertain record blocks other updates.
        ReviewedUpdateHistoryItemViewModel uncertain = ReviewedUpdateHistoryItemViewModel.From(ReviewedUpdateHistoryPresentation.Present(
            new(operation.JobId, operation, UpdateDispatchState.Uncertain, ReviewedAt, null, true)));
        Assert.IsTrue(uncertain.BlocksOtherUpdates);
        Assert.IsNull(uncertain.VerificationDetail);
        Assert.ThrowsExactly<ArgumentNullException>(() => ReviewedUpdateHistoryItemViewModel.From(null!));
    }

    [TestMethod]
    public async Task TheHistoryListLoadsThroughTheReaderAndFailsClosed()
    {
        ReviewedUpdateOperation operation = Chocolatey();
        ReviewedUpdateHistoryRecord record = new(operation.JobId, operation, UpdateDispatchState.Final, ReviewedAt,
            new(UpdateDispatchOutcome.Success, ReviewedAt.AddMinutes(1), 0, UpdateDispatchSettlementState.OwnedWorkStopped,
                ReviewedUpdateDispatchWorkflow.TargetVerifiedMessage), true);
        Reader loaded = new(_ => [record]);
        ReviewedUpdateHistoryListViewModel list = await ReviewedUpdateHistoryListViewModel.LoadAsync(loaded, CancellationToken.None);
        Assert.IsTrue(list.IsAvailable);
        Assert.AreEqual(1, list.Items.Count);
        Assert.AreEqual(ReviewedUpdateHistoryPresentation.TargetVerifiedText, list.Items[0].VerificationDetail);
        Assert.AreEqual(ReviewedUpdateHistoryListViewModel.MaximumRecords, loaded.RequestedMaximum);
        StringAssert.Contains(list.Summary, "1 reviewed update record");
        // UI review 2026-11-13 (item 17, FR-HIS-01): the record sits closed on Activity and opens by itself only when it needs attention.
        Assert.IsFalse(list.NeedsAttention, "a finished, verified record needs nothing");
        ReviewedUpdateHistoryListViewModel blocking = await ReviewedUpdateHistoryListViewModel.LoadAsync(new Reader(_ => [record with { State = UpdateDispatchState.Uncertain }]), CancellationToken.None);
        Assert.IsTrue(blocking.NeedsAttention, "a record that still blocks other updates is shown without being asked");
        Assert.IsTrue(ReviewedUpdateHistoryListViewModel.Unavailable.NeedsAttention, "a record that could not be read says so");
        Assert.AreEqual("Technical record of updates", ReviewedUpdateHistoryListViewModel.RecordTitle);
        Assert.AreEqual("How each update was run and checked, as PC Steward recorded it.", ReviewedUpdateHistoryListViewModel.RecordDescription);

        ReviewedUpdateHistoryListViewModel empty = await ReviewedUpdateHistoryListViewModel.LoadAsync(new Reader(_ => []), CancellationToken.None);
        Assert.IsTrue(empty.IsAvailable);
        Assert.AreEqual(ReviewedUpdateHistoryListViewModel.EmptyText, empty.Summary);
        Assert.IsFalse(empty.NeedsAttention);

        foreach (Reader failing in new[]
                 {
                     new Reader(_ => throw new InvalidOperationException("future schema")),
                     new Reader(token => { token.ThrowIfCancellationRequested(); return []; }),
                     new Reader(_ => Enumerable.Repeat(record, ReviewedUpdateHistoryListViewModel.MaximumRecords + 1).ToArray()),
                 })
        {
            using CancellationTokenSource cancelled = new();
            cancelled.Cancel();
            ReviewedUpdateHistoryListViewModel unavailable = await ReviewedUpdateHistoryListViewModel.LoadAsync(failing, cancelled.Token);
            Assert.IsFalse(unavailable.IsAvailable);
            Assert.AreEqual(0, unavailable.Items.Count, "never partial or invented rows");
            Assert.AreEqual(ReviewedUpdateHistoryListViewModel.UnavailableText, unavailable.Summary);
        }
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => ReviewedUpdateHistoryListViewModel.LoadAsync(null!, CancellationToken.None));
    }

    // ---- Helpers --------------------------------------------------------------------------------------------------------

    private static void AssertConfirmDisabled(ChocolateyReviewedRouteReviewViewModel model)
    {
        Assert.IsFalse(model.CanConfirm);
        Assert.AreEqual(model.CanConfirm, model.IsConfirmEnabled, "Confirm is enabled exactly when CanConfirm holds");
        Assert.IsFalse(model.IsConfirmEnabled);
        Assert.IsNull(model.TryConfirm(), "no confirmation while CanConfirm is false");
        Assert.IsNull(model.TryConfirm(), "and none on a retry");
        StringAssert.Contains(model.ConfirmAutomationName, "unavailable");
    }

    private static ReviewedUpdateDraft Draft(ReviewedUpdateOperation operation) => new(operation, Guid.NewGuid());

    private static ChocolateyReviewIntentFacts Facts(bool libBad = false) =>
        new("git.install", "1.0.0", "2.0.0", User, Evidence, ReviewedCandidateProvenance.UserCacheIntent, libBad, ResolvedAt, ReviewedAt.AddSeconds(-30));

    private static ReviewedUpdateOperation Chocolatey()
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection("choco:git.install",
            UpdateProvider.Chocolatey, UpdateSource.ChocolateyCommunity, "git.install", "1.0.0", "2.0.0", UpdateScope.Machine,
            UpdateArchitecture.Neutral, UpdateChannel.Stable, "mapping-v2")).Confirm(ReviewedAt);
        return new(job, User, "choco:git.install", "machine:choco", Evidence, ReviewedAt, UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3,
            ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled), ReviewedUpdateArchitecture.ProviderScriptSelected());
    }

    private static ReviewedUpdateOperation WinGet()
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection("winget:contoso.widget", UpdateProvider.WinGet,
            UpdateSource.WinGet, "Contoso.Widget", "1.0", "1.1", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "mapping-v1")).Confirm(ReviewedAt);
        return new(job, User, "native:1", "context:1", "review:1", ReviewedAt, UpdateRouteContract.WinGetUserExactV3,
            ReviewedInstallerMode.Selected(InstallerMode.Silent));
    }

    private sealed class Reader(Func<CancellationToken, IReadOnlyList<ReviewedUpdateHistoryRecord>> read) : IReviewedUpdateHistoryReader
    {
        internal int RequestedMaximum { get; private set; }

        public Task<IReadOnlyList<ReviewedUpdateHistoryRecord>> GetReviewedUpdateHistoryAsync(int maximumRecords, CancellationToken cancellationToken)
        {
            RequestedMaximum = maximumRecords;
            return Task.FromResult(read(cancellationToken));
        }
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
