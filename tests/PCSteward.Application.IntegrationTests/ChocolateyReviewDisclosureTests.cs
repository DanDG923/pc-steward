using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01, FR-HIS-01, TM-03/04/07/13; Task 50 N6: the runtime-limit Chocolatey review disclosure (Profile V2
/// user-cache text, the intent's own time, design rev 6 §(L6) split-token reason and rev-4 L4 lingering-process
/// disclosure) and the history route, verification and lingering-process texts. The route stays non-admitted: no review
/// view can be confirmable while <c>HasSupportedMode</c> refuses it.
/// </summary>
[TestClass]
public sealed class ChocolateyReviewDisclosureTests
{
    private const string User = "S-1-5-21-11-22-33-1001";
    private static readonly DateTimeOffset ReviewedAt = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ResolvedAt = new(2026, 10, 30, 13, 59, 15, TimeSpan.FromHours(2)); // 11:59:15 UTC
    private static readonly string Evidence = ReviewedUpdateOperation.ProfileEvidencePrefix + new string('A', 64);

    // ---- Review disclosure ------------------------------------------------------------------------------------------------

    [TestMethod]
    public void TheReviewShowsTheSpecifiedDisclosuresAndTheIntentsOwnTimeAndCannotBeConfirmed()
    {
        ReviewedUpdateOperation operation = Chocolatey(UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3);
        ChocolateyReviewDisclosureView view = ChocolateyReviewDisclosurePresentation.Present(operation, Facts(), ReviewedRouteSplitTokenState.SplitTokenLimited);

        // Profile V2 "UI must disclose before review" and design rev 6 rev-4 L4, verbatim.
        Assert.AreEqual("Candidate shown from your user cache; PC Steward confirms the exact version with protected machine data before updating.",
            view.UserCacheDisclosure);
        Assert.AreEqual("Processes started by the update, including installers still running, may be closed when the update finishes.", view.LingeringProcesses);
        Assert.AreEqual(ReviewedUpdateProviderDisclosure.ChocolateyCommunityScriptRuntimeLimits, view.RuntimeLimits);
        StringAssert.Contains(view.InstallerMode, "package's own scripts");
        StringAssert.Contains(view.CandidateIntent, "provisional intent from your user cache");
        StringAssert.Contains(view.Title, "git.install 1.0.0 → 2.0.0");
        // N5 obligation (a): the intent's own resolved time, not the observation or review time.
        Assert.AreEqual(ResolvedAt, view.IntentResolvedAt);
        Assert.AreEqual(TimeSpan.Zero, view.IntentResolvedAt!.Value.Offset);
        Assert.AreEqual("Candidate checked in your user cache at 2026-10-30 11:59:15 UTC.", view.IntentTime);
        Assert.IsNull(view.LibBadResidue);
        // Not admitted pending owner host evidence: even a split token cannot make it confirmable.
        CollectionAssert.AreEqual(new[] { ReviewedRouteUnavailableReason.NotAdmittedPendingHostEvidence }, view.UnavailableReasons.ToArray());
        CollectionAssert.AreEqual(new[] { ChocolateyReviewDisclosurePresentation.NotAdmittedText }, view.UnavailableTexts.ToArray());
        Assert.IsFalse(view.CanConfirm);
        Assert.IsFalse(ReviewedUpdateReview.HasSupportedMode(operation));
        AssertNoQualificationClaim(view);
    }

    [TestMethod]
    [DataRow(ReviewedRouteSplitTokenState.NoSplitToken, ReviewedRouteUnavailableReason.SplitTokenRequired)]
    [DataRow(ReviewedRouteSplitTokenState.DesktopTokenUnsupported, ReviewedRouteUnavailableReason.DesktopTokenUnsupported)]
    [DataRow(ReviewedRouteSplitTokenState.Unknown, ReviewedRouteUnavailableReason.DesktopTokenUnknown)]
    [DataRow((ReviewedRouteSplitTokenState)99, ReviewedRouteUnavailableReason.DesktopTokenUnknown)]
    public void EveryTokenStateOtherThanASplitTokenAddsItsUnavailabilityReason(ReviewedRouteSplitTokenState state, ReviewedRouteUnavailableReason reason)
    {
        ChocolateyReviewDisclosureView view = ChocolateyReviewDisclosurePresentation.Present(Chocolatey(UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3), Facts(), state);
        CollectionAssert.AreEqual(new[] { ReviewedRouteUnavailableReason.NotAdmittedPendingHostEvidence, reason }, view.UnavailableReasons.ToArray());
        Assert.AreEqual(ChocolateyReviewDisclosurePresentation.Text(reason), view.UnavailableTexts[1]);
        Assert.IsFalse(view.CanConfirm);
    }

    [TestMethod]
    public void TheSplitTokenReasonStatesTheL6Constraint()
    {
        string text = ChocolateyReviewDisclosurePresentation.SplitTokenRequiredText;
        StringAssert.Contains(text, "administrator account with User Account Control turned on (a split token)");
        StringAssert.Contains(text, "unavailable when User Account Control is off");
        StringAssert.Contains(text, "standard-user desktops");
        StringAssert.Contains(ChocolateyReviewDisclosurePresentation.NotAdmittedText, "does not allow this Chocolatey update route yet");
        string[] texts = Enum.GetValues<ReviewedRouteUnavailableReason>().Select(ChocolateyReviewDisclosurePresentation.Text).ToArray();
        Assert.AreEqual(texts.Length, texts.Distinct(StringComparer.Ordinal).Count());
        Assert.IsTrue(texts.All(item => item.StartsWith("Unavailable: ", StringComparison.Ordinal)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ChocolateyReviewDisclosurePresentation.Text((ReviewedRouteUnavailableReason)99));
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("package")]
    [DataRow("installed")]
    [DataRow("candidate")]
    [DataRow("sid")]
    [DataRow("evidence")]
    [DataRow("provenance")]
    [DataRow("resolved-after-observed")]
    [DataRow("observed-after-review")]
    [DataRow("default-resolved")]
    [DataRow("older-route")]
    [DataRow("resolved-too-long-before-review")]
    public void AnIntentThatDoesNotMatchTheFrozenReviewIsAMismatchWithoutItsTime(string change)
    {
        ReviewedUpdateOperation operation = Chocolatey(change == "older-route" ? UpdateRouteContract.ChocolateyMachineProvisionalV3 : UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3);
        ChocolateyReviewIntentFacts facts = Facts(libBad: true);
        ChocolateyReviewIntentFacts? changed = change switch
        {
            "null" => null,
            "package" => facts with { PackageId = "other.package" },
            "installed" => facts with { InstalledVersion = "0.9.0" },
            "candidate" => facts with { CandidateVersion = "3.0.0" },
            "sid" => facts with { RequestingUserSid = "S-1-5-21-11-22-33-1002" },
            "evidence" => facts with { ProfileEvidenceId = ReviewedUpdateOperation.ProfileEvidencePrefix + new string('B', 64) },
            "provenance" => facts with { CandidateProvenance = ReviewedCandidateProvenance.TrustedDesktopEvidenceRequired },
            "resolved-after-observed" => facts with { IntentResolvedAt = facts.ObservedAt.AddTicks(1) },
            "observed-after-review" => facts with { ObservedAt = ReviewedAt.AddTicks(1) },
            "default-resolved" => facts with { IntentResolvedAt = default },
            // L4: observed promptly but reviewed more than IntentMaxAge after the intent was resolved.
            "resolved-too-long-before-review" => facts with
            {
                IntentResolvedAt = ReviewedAt - ChocolateyReviewDisclosurePresentation.IntentMaxAge - TimeSpan.FromTicks(1),
                ObservedAt = ReviewedAt - ChocolateyReviewDisclosurePresentation.IntentMaxAge,
            },
            _ => facts,
        };
        ChocolateyReviewDisclosureView view = ChocolateyReviewDisclosurePresentation.Present(operation, changed, ReviewedRouteSplitTokenState.SplitTokenLimited);
        CollectionAssert.Contains(view.UnavailableReasons.ToArray(), ReviewedRouteUnavailableReason.ReviewDataMismatch);
        Assert.IsNull(view.IntentResolvedAt);
        Assert.AreEqual(ChocolateyReviewDisclosurePresentation.IntentTimeUnavailableText, view.IntentTime);
        Assert.IsNull(view.LibBadResidue, "residue is shown only for the matching intent");
        Assert.IsFalse(view.CanConfirm);
        // The fixed pre-review disclosures are still shown.
        Assert.AreEqual(ChocolateyReviewDisclosurePresentation.UserCacheDisclosure, view.UserCacheDisclosure);
        Assert.AreEqual(ReviewedUpdateLingeringProcesses.ReviewDisclosure, view.LingeringProcesses);
    }

    /// <summary>L4: the review may follow the intent's own resolved time by at most IntentMaxAge, the producer's bound.</summary>
    [TestMethod]
    public void TheReviewMayFollowTheResolvedIntentByAtMostTheProducersIntentMaxAge()
    {
        Assert.AreEqual(PCSteward.Provider.Chocolatey.Native.ChocolateyProfileEvidenceSource.IntentMaxAge, ChocolateyReviewDisclosurePresentation.IntentMaxAge);
        ReviewedUpdateOperation operation = Chocolatey(UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3);
        DateTimeOffset oldest = ReviewedAt - ChocolateyReviewDisclosurePresentation.IntentMaxAge;
        foreach ((DateTimeOffset resolved, DateTimeOffset observed, bool matches) in new[]
                 {
                     (oldest, oldest, true),
                     (oldest, ReviewedAt, true),
                     (ReviewedAt, ReviewedAt, true),
                     (oldest.AddTicks(-1), oldest, false),
                     (oldest.AddTicks(-1), ReviewedAt, false),
                 })
        {
            ChocolateyReviewDisclosureView view = ChocolateyReviewDisclosurePresentation.Present(operation,
                Facts() with { IntentResolvedAt = resolved, ObservedAt = observed }, ReviewedRouteSplitTokenState.SplitTokenLimited);
            Assert.AreEqual(!matches, view.UnavailableReasons.Contains(ReviewedRouteUnavailableReason.ReviewDataMismatch), $"{resolved:O} {observed:O}");
            Assert.AreEqual(matches ? resolved : null, view.IntentResolvedAt);
        }
        // Resolved after the review (negative age) never matches.
        ChocolateyReviewDisclosureView future = ChocolateyReviewDisclosurePresentation.Present(operation,
            Facts() with { IntentResolvedAt = ReviewedAt.AddTicks(1), ObservedAt = ReviewedAt.AddTicks(1) }, ReviewedRouteSplitTokenState.SplitTokenLimited);
        CollectionAssert.Contains(future.UnavailableReasons.ToArray(), ReviewedRouteUnavailableReason.ReviewDataMismatch);
    }

    [TestMethod]
    public void LibBadResidueIsDisclosedForTheMatchingIntent()
    {
        ChocolateyReviewDisclosureView view = ChocolateyReviewDisclosurePresentation.Present(Chocolatey(UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3),
            Facts(libBad: true), ReviewedRouteSplitTokenState.SplitTokenLimited);
        Assert.AreEqual(ChocolateyReviewDisclosurePresentation.LibBadResidueText, view.LibBadResidue);
        StringAssert.Contains(view.LibBadResidue, "never count as the installed version");
    }

    [TestMethod]
    public void AWinGetOperationIsNeverPresentedAsAChocolateyReview()
    {
        ChocolateyReviewDisclosureView view = ChocolateyReviewDisclosurePresentation.Present(WinGet(), Facts(), ReviewedRouteSplitTokenState.SplitTokenLimited);
        CollectionAssert.AreEqual(new[] { ReviewedRouteUnavailableReason.ReviewDataMismatch }, view.UnavailableReasons.ToArray());
        Assert.IsFalse(view.CanConfirm);
        Assert.IsNull(view.RuntimeLimits);
    }

    // ---- History ------------------------------------------------------------------------------------------------------

    [TestMethod]
    public void HistoryNamesTheFrozenRouteAndNeverCarriesTheCurrentRouteMeaningToOlderRoutes()
    {
        Assert.AreEqual("WinGet exact package update for your account (user scope).", Present(WinGet(), UpdateDispatchOutcome.Success, "x").Route);
        StringAssert.Contains(Present(Chocolatey(UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3), UpdateDispatchOutcome.Unverified, "x").Route,
            "administrator helper");
        foreach (UpdateRouteContract older in new[] { UpdateRouteContract.ChocolateyMachineProvisionalV3, UpdateRouteContract.ChocolateyMachineRegistrationV3,
                     UpdateRouteContract.ChocolateyMachineRegistrationV2 })
            Assert.AreEqual("Earlier Chocolatey machine route (historical record).", ReviewedUpdateHistoryPresentation.Route(older));
        // L3: anything else, including an undefined value, is an unknown route, never an earlier Chocolatey route.
        foreach (UpdateRouteContract undefined in new[] { (UpdateRouteContract)(-1), (UpdateRouteContract)6, (UpdateRouteContract)99 })
            Assert.AreEqual("Unknown route", ReviewedUpdateHistoryPresentation.Route(undefined));
        foreach (UpdateRouteContract defined in Enum.GetValues<UpdateRouteContract>())
            Assert.AreNotEqual("Unknown route", ReviewedUpdateHistoryPresentation.Route(defined), defined.ToString());
        ReviewedUpdateHistoryView unreadable = ReviewedUpdateHistoryPresentation.Present(new(Guid.NewGuid(), null, UpdateDispatchState.Final, ReviewedAt,
            Terminal(UpdateDispatchOutcome.Success, ReviewedUpdateDispatchWorkflow.TargetVerifiedMessage), false));
        Assert.AreEqual("Unknown", unreadable.Route);
        Assert.IsNull(unreadable.VerificationDetail, "an integrity failure has no verification text");
    }

    [TestMethod]
    [DataRow(UpdateDispatchOutcome.Success, ReviewedUpdateDispatchWorkflow.TargetVerifiedMessage, ReviewedUpdateHistoryPresentation.TargetVerifiedText)]
    [DataRow(UpdateDispatchOutcome.Unverified, ReviewedUpdateDispatchWorkflow.TargetNotDetectedMessage, ReviewedUpdateHistoryPresentation.TargetNotDetectedText)]
    [DataRow(UpdateDispatchOutcome.Unverified, ReviewedUpdateDispatchWorkflow.VerificationDidNotFinishMessage, ReviewedUpdateHistoryPresentation.VerificationDidNotFinishText)]
    [DataRow(UpdateDispatchOutcome.Unverified, ReviewedUpdateDispatchWorkflow.ClientNotVerifiedMessage, ReviewedUpdateDispatchWorkflow.ClientNotVerifiedMessage)]
    [DataRow(UpdateDispatchOutcome.Unverified, ReviewedUpdateDispatchWorkflow.RestartContractViolationMessage, ReviewedUpdateDispatchWorkflow.RestartContractViolationMessage)]
    [DataRow(UpdateDispatchOutcome.Unverified, ReviewedUpdateDispatchWorkflow.ExitCodeNotObservedMessage + " fixture", ReviewedUpdateHistoryPresentation.ExitCodeNotObservedText)]
    [DataRow(UpdateDispatchOutcome.Unverified, "TimedOut after the provider started; the resulting version is unverified. fixture", ReviewedUpdateHistoryPresentation.StoppedAfterStartText)]
    [DataRow(UpdateDispatchOutcome.Unverified, "Cancelled after the provider started; the resulting version is unverified.", ReviewedUpdateHistoryPresentation.StoppedAfterStartText)]
    [DataRow(UpdateDispatchOutcome.Unverified, "Failed after the provider started; the resulting version is unverified. x", ReviewedUpdateHistoryPresentation.StoppedAfterStartText)]
    [DataRow(UpdateDispatchOutcome.Unverified, "helper-check-not-recorded lingering-descendants-terminated " + ReviewedUpdateDispatchWorkflow.TargetNotDetectedMessage, ReviewedUpdateHistoryPresentation.TargetNotDetectedText)]
    [DataRow(UpdateDispatchOutcome.Unverified, "lingering-descendants-terminated helper-check-not-recorded " + ReviewedUpdateDispatchWorkflow.ClientNotVerifiedMessage, ReviewedUpdateDispatchWorkflow.ClientNotVerifiedMessage)]
    // Inconsistent with the recorded outcome, or not the workflow's own message: no verification text.
    [DataRow(UpdateDispatchOutcome.Success, ReviewedUpdateDispatchWorkflow.TargetNotDetectedMessage, null)]
    [DataRow(UpdateDispatchOutcome.Unverified, ReviewedUpdateDispatchWorkflow.TargetVerifiedMessage, null)]
    [DataRow(UpdateDispatchOutcome.Failed, ReviewedUpdateDispatchWorkflow.VerificationDidNotFinishMessage, null)]
    [DataRow(UpdateDispatchOutcome.Unverified, "fixture " + ReviewedUpdateDispatchWorkflow.TargetNotDetectedMessage, null)]
    [DataRow(UpdateDispatchOutcome.Unverified, ReviewedUpdateDispatchWorkflow.TargetNotDetectedMessage + " extra", null)]
    [DataRow(UpdateDispatchOutcome.Unverified, "Completed after the provider started; the resulting version is unverified.", null)]
    public void HistoryShowsFixedVerificationTextOnlyForTheWorkflowsOwnConsistentMessage(UpdateDispatchOutcome outcome, string diagnostic, string? expected)
    {
        Assert.AreEqual(expected, Present(Chocolatey(UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3), outcome, diagnostic).VerificationDetail);
        Assert.AreEqual(expected, Present(WinGet(), outcome, diagnostic).VerificationDetail);
    }

    [TestMethod]
    [DataRow(UpdateDispatchOutcome.Success)]
    [DataRow(UpdateDispatchOutcome.Unverified)]
    [DataRow(UpdateDispatchOutcome.Failed)]
    [DataRow(UpdateDispatchOutcome.Refused)]
    public void TheLingeringProcessNoteIsShownBesideEveryOutcomeWhenRecorded(UpdateDispatchOutcome outcome)
    {
        ReviewedUpdateOperation operation = Chocolatey(UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3);
        ReviewedUpdateHistoryView shown = Present(operation, outcome, "lingering-descendants-terminated Target version verified by post-update inventory.");
        Assert.AreEqual(ReviewedUpdateLingeringProcesses.HistoryNote, shown.LingeringProcessesNote);
        StringAssert.StartsWith(shown.LingeringProcessesNote, "Recorded at the time:");
        Assert.IsFalse(shown.Status.Contains("closed", StringComparison.Ordinal), "the note is separate from the status line");
        Assert.IsNull(Present(operation, outcome, "fixture").LingeringProcessesNote);
        Assert.IsNull(Present(operation, outcome, "xlingering-descendants-terminated").LingeringProcessesNote, "only the whole token");
        Assert.IsNull(Present(operation, outcome, "lingering-descendants-terminated-extra").LingeringProcessesNote, "only the whole token");
        // L2: only the position the workflow writes (leading, after an optional leading helper-check marker).
        Assert.IsNull(Present(operation, outcome, "helper: settle; lingering-descendants-terminated; done").LingeringProcessesNote, "an embedded token");
        Assert.IsNull(Present(operation, outcome, "Nothing was invoked. lingering-descendants-terminated").LingeringProcessesNote, "an embedded token");
        Assert.IsNull(Present(operation, outcome, " lingering-descendants-terminated x").LingeringProcessesNote, "not leading");
        Assert.IsNull(Present(operation, outcome, "lingering-descendants-terminated;x").LingeringProcessesNote, "not the marker's separator");
        Assert.IsNull(Present(operation, outcome, "helper-check-not-recorded helper-check-not-recorded lingering-descendants-terminated x").LingeringProcessesNote);
        Assert.IsNotNull(Present(operation, outcome, "helper-check-not-recorded lingering-descendants-terminated x").LingeringProcessesNote);
        Assert.IsNotNull(Present(operation, outcome, "lingering-descendants-terminated").LingeringProcessesNote);
        // An integrity failure shows nothing recorded.
        Assert.IsNull(ReviewedUpdateHistoryPresentation.Present(new(operation.JobId, operation, UpdateDispatchState.Final, ReviewedAt,
            Terminal(outcome, ReviewedUpdateLingeringProcesses.Token), false)).LingeringProcessesNote);
    }

    /// <summary>Info: the note already shows the marker, so the visible diagnostic drops it; the helper-check marker (no note) stays.</summary>
    [TestMethod]
    [DataRow("lingering-descendants-terminated Target version verified by post-update inventory.", "Target version verified by post-update inventory.")]
    [DataRow("helper-check-not-recorded lingering-descendants-terminated Nothing new.", "helper-check-not-recorded Nothing new.")]
    [DataRow("lingering-descendants-terminated", "")]
    [DataRow("helper-check-not-recorded Nothing new.", "helper-check-not-recorded Nothing new.")]
    [DataRow("helper: settle; lingering-descendants-terminated; done", "helper: settle; lingering-descendants-terminated; done")]
    public void TheVisibleDiagnosticDropsOnlyTheLeadingLingeringMarker(string recorded, string shown)
    {
        ReviewedUpdateOperation operation = Chocolatey(UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3);
        ReviewedUpdateHistoryRecord record = new(operation.JobId, operation, UpdateDispatchState.Final, ReviewedAt,
            Terminal(UpdateDispatchOutcome.Unverified, recorded), true);
        ReviewedUpdateHistoryView view = ReviewedUpdateHistoryPresentation.Present(record);
        Assert.AreEqual(shown, view.Diagnostic);
        Assert.AreEqual(recorded, record.Terminal!.Diagnostic, "persisted data is unchanged");
        // An integrity failure shows the recorded text unchanged (no note either).
        ReviewedUpdateHistoryView untrusted = ReviewedUpdateHistoryPresentation.Present(record with { IntegrityVerified = false });
        Assert.AreEqual(recorded, untrusted.Diagnostic);
        Assert.IsNull(untrusted.LingeringProcessesNote);
    }

    [TestMethod]
    public void AnUncertainRowKeepsTheLingeringNoteButHasNoVerificationText()
    {
        ReviewedUpdateOperation operation = Chocolatey(UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3);
        ReviewedUpdateHistoryView view = ReviewedUpdateHistoryPresentation.Present(new(operation.JobId, operation, UpdateDispatchState.Uncertain, ReviewedAt,
            new(UpdateDispatchOutcome.Uncertain, ReviewedAt.AddMinutes(1), null, UpdateDispatchSettlementState.OwnedWorkUnsettled,
                "lingering-descendants-terminated " + ReviewedUpdateDispatchWorkflow.VerificationDidNotFinishMessage), true));
        Assert.IsTrue(view.BlocksOtherUpdates);
        Assert.IsNull(view.VerificationDetail);
        Assert.AreEqual(ReviewedUpdateLingeringProcesses.HistoryNote, view.LingeringProcessesNote);
    }

    // ---- Helpers ------------------------------------------------------------------------------------------------------

    private static ReviewedUpdateHistoryView Present(ReviewedUpdateOperation operation, UpdateDispatchOutcome outcome, string diagnostic) =>
        ReviewedUpdateHistoryPresentation.Present(new(operation.JobId, operation, UpdateDispatchState.Final, ReviewedAt, Terminal(outcome, diagnostic), true));

    private static ReviewedUpdateTerminalRecord Terminal(UpdateDispatchOutcome outcome, string diagnostic) =>
        new(outcome, ReviewedAt.AddMinutes(1), outcome is UpdateDispatchOutcome.Refused ? null : 0,
            outcome is UpdateDispatchOutcome.Refused ? UpdateDispatchSettlementState.NotInvoked : UpdateDispatchSettlementState.OwnedWorkStopped, diagnostic);

    private static void AssertNoQualificationClaim(ChocolateyReviewDisclosureView view)
    {
        foreach (string text in new[] { view.Title, view.CandidateIntent, view.IntentTime, view.UserCacheDisclosure, view.InstallerMode, view.LingeringProcesses }
                     .Concat(view.UnavailableTexts))
            Assert.IsFalse(text.Contains("qualified", StringComparison.OrdinalIgnoreCase) || text.Contains("verified", StringComparison.OrdinalIgnoreCase), text);
    }

    private static ChocolateyReviewIntentFacts Facts(bool libBad = false) =>
        new("git.install", "1.0.0", "2.0.0", User, Evidence, ReviewedCandidateProvenance.UserCacheIntent, libBad, ResolvedAt, ReviewedAt.AddSeconds(-30));

    private static ReviewedUpdateOperation Chocolatey(UpdateRouteContract route)
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection("choco:git.install",
            UpdateProvider.Chocolatey, UpdateSource.ChocolateyCommunity, "git.install", "1.0.0", "2.0.0", UpdateScope.Machine,
            UpdateArchitecture.Neutral, UpdateChannel.Stable, "mapping-v2")).Confirm(ReviewedAt);
        return new(job, User, "choco:git.install", "machine:choco",
            route == UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3 ? Evidence : "evidence:1", ReviewedAt, route,
            ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled), ReviewedUpdateArchitecture.ProviderScriptSelected());
    }

    private static ReviewedUpdateOperation WinGet()
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection("winget:contoso.widget", UpdateProvider.WinGet,
            UpdateSource.WinGet, "Contoso.Widget", "1.0", "1.1", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "mapping-v1")).Confirm(ReviewedAt);
        return new(job, User, "native:1", "context:1", "review:1", ReviewedAt, UpdateRouteContract.WinGetUserExactV3,
            ReviewedInstallerMode.Selected(InstallerMode.Silent));
    }
}
