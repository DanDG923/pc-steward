using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// SPEC §9.13 G4, FR-UI-03/04, FR-UPD-04–06 (Task 73 Phase 6): one review sheet for a frozen set, aggregated risks, the shield only
/// when an app needs administrator approval, the per-app elevation note, excluded apps explained and grouped per-item outcomes.
/// </summary>
[TestClass]
public sealed class UpdateBatchPresentationTests
{
    private static readonly string[] AppNames = ["Alpha", "Beta", "Gamma"];
    private static readonly UpdateOutcomeKind[] GroupOrder = [UpdateOutcomeKind.Verified, UpdateOutcomeKind.RestartRequired, UpdateOutcomeKind.Failed];

    [TestMethod]
    [TestCategory("FR-UI-03")]
    public void AOneAppBatchIsExactlyTheSingleAppSheet()
    {
        UpdateReviewSheetInput input = Input("alpha", UpdateScope.Machine, running: true);
        UpdateReviewSheet single = UpdateReviewSheet.Create(input.Selection, input.AppName, input.Publisher, input.Detail, input.Running,
            AdministratorRequirementPresenter.ForSelection(input.Selection));
        UpdateReviewSheet batch = UpdateReviewSheet.CreateBatch([input]);

        Assert.AreEqual(1, batch.ItemCount);
        Assert.AreEqual(single.Title, batch.Title);
        Assert.AreEqual(single.PrimaryButtonLabel, batch.PrimaryButtonLabel);
        Assert.AreEqual("Update 1 app anyway", batch.PrimaryButtonLabel);
        Assert.AreEqual(single.PrimaryAutomationName, batch.PrimaryAutomationName);
        Assert.AreEqual(single.Attestation, batch.Attestation);
        CollectionAssert.AreEqual(single.Facts.ToArray(), batch.Facts.ToArray());
        CollectionAssert.AreEqual(single.Risks.ToArray(), batch.Risks.ToArray());
        Assert.IsTrue(batch.ShowsAdministratorShield);
        Assert.IsNull(batch.ElevationNote, "a single app keeps the existing administrator explanation");
    }

    [TestMethod]
    [TestCategory("FR-UI-04")]
    [TestCategory("FR-UPD-04")]
    public void ABatchSheetShowsEveryAppsFactsAggregatesRisksAndNamesTheCount()
    {
        UpdateReviewSheet sheet = UpdateReviewSheet.CreateBatch(
            [Input("alpha", UpdateScope.User), Input("beta", UpdateScope.Machine, running: true), Input("gamma", UpdateScope.User)],
            [new UpdateReviewExclusion("Delta", "The update evidence is no longer fresh.")]);

        Assert.AreEqual("Review updates", sheet.Title);
        Assert.AreEqual("3 apps", sheet.AppName);
        CollectionAssert.AreEqual(AppNames, sheet.Items.Select(item => item.AppName).ToArray());
        foreach (UpdateReviewSheetItem item in sheet.Items)
        {
            CollectionAssert.AreEqual(UpdateReviewSheet.FactLabels.ToArray(), item.Facts.Select(fact => fact.Label).ToArray(), "every app keeps the §17.2 facts");
            Assert.AreEqual("High confidence", item.ConfidenceSummary, "confidence in words per app");
        }
        Assert.IsEmpty(sheet.Facts, "a batch shows each app's facts, not one merged set");
        Assert.AreEqual("Update 3 apps anyway", sheet.PrimaryButtonLabel);
        StringAssert.StartsWith(sheet.PrimaryAutomationName, sheet.PrimaryButtonLabel, "label-in-name");
        StringAssert.Contains(sheet.PrimaryAutomationName, "Alpha, Beta, Gamma");
        Assert.IsTrue(sheet.ShowsAdministratorShield, "the shield appears because one app is machine-wide");
        Assert.AreEqual(1, sheet.AdministratorRequiredCount);
        Assert.AreEqual(UpdateReviewSheet.PerAppElevationNote, sheet.ElevationNote);
        StringAssert.Contains(sheet.ElevationNote!, "once for each app that needs it");
        Assert.AreEqual("Administrator approval required for 1 of 3 apps", sheet.AdministratorRequirement.Label);

        ReviewRiskCallout running = sheet.Risks.Single(risk => risk.Kind == ReviewRiskKind.RunningApplication);
        Assert.AreEqual("Beta appears to be running", running.Title);
        StringAssert.Contains(running.Message, "Beta (Contoso.BETA)");
        StringAssert.Contains(running.Message, "Close Beta for me");
        StringAssert.Contains(running.Message, "never force-quits");
        Assert.AreEqual("Close Beta for me", sheet.CloseForMeLabel);
        Assert.IsTrue(sheet.CanConfirm);
        Assert.AreEqual(StatusTone.Attention, running.Tone);
        Assert.HasCount(3, sheet.Risks);
        StringAssert.Contains(sheet.Attestation, "one at a time");
        StringAssert.Contains(sheet.Attestation, "doesn't record an update as successful until it checks");
        Assert.AreEqual("Delta", sheet.Excluded.Single().AppName);
        StringAssert.Contains(sheet.Excluded.Single().Reason, "no longer fresh");
        Assert.IsFalse(string.IsNullOrWhiteSpace(sheet.CoverageDisclosure));
    }

    [TestMethod]
    [TestCategory("FR-UPD-04")]
    public void NoShieldOrElevationNoteWhenEveryAppIsCurrentUser()
    {
        UpdateReviewSheet sheet = UpdateReviewSheet.CreateBatch([Input("alpha", UpdateScope.User), Input("beta", UpdateScope.User)]);

        Assert.IsFalse(sheet.ShowsAdministratorShield);
        Assert.IsNull(sheet.ElevationNote);
        Assert.AreEqual("Update 2 apps", sheet.PrimaryButtonLabel);
        Assert.AreEqual(AdministratorRequirementStatus.NotExpected, sheet.AdministratorRequirement.Status);
        Assert.AreEqual("None of these apps appear to be running", sheet.Risks.Single(risk => risk.Kind == ReviewRiskKind.RunningApplication).Title);
        Assert.IsFalse(sheet.PrimaryButtonLabel.Contains("anyway", StringComparison.Ordinal));
    }

    [TestMethod]
    [TestCategory("FR-UPD-04")]
    public void TheBatchAdministratorRequirementCountsMachineWideApps()
    {
        UpdateJobSelection user = Input("u", UpdateScope.User).Selection;
        UpdateJobSelection machine = Input("m", UpdateScope.Machine).Selection;

        Assert.AreEqual(AdministratorRequirementPresenter.ForSelection(machine), AdministratorRequirementPresenter.ForBatch([machine]));
        Assert.AreEqual("Administrator approval required for 2 of 3 apps", AdministratorRequirementPresenter.ForBatch([machine, user, machine]).Label);
        Assert.AreEqual(AdministratorRequirementStatus.NotExpected, AdministratorRequirementPresenter.ForBatch([user, user]).Status);
        Assert.ThrowsExactly<ArgumentException>(() => AdministratorRequirementPresenter.ForBatch([]));
    }

    [TestMethod]
    [TestCategory("AC-UPD-09")]
    public void BatchOutcomesAreGroupedWithoutMergingOrUpgradingAny()
    {
        UpdateJobCompletion completion = new(
        [
            Outcome("a", UpdateHistoryStatus.Success),
            Outcome("b", UpdateHistoryStatus.InstallerFailed),
            Outcome("c", UpdateHistoryStatus.Success),
            Outcome("d", UpdateHistoryStatus.RestartRequired)
        ]);

        CollectionAssert.AreEqual(GroupOrder, completion.Groups.Select(group => group.Kind).ToArray());
        Assert.AreEqual("Verified (2)", completion.Groups[0].Heading);
        Assert.AreEqual("2 verified · 1 restart required · 1 failed", completion.Headline);
        Assert.IsTrue(completion.RestartRequired);
        Assert.IsFalse(completion.ReturnsToReview);
        Assert.AreEqual(4, completion.Groups.Sum(group => group.Items.Count), "every item appears exactly once");
        // Interleaved outcomes (a verified, b failed, c verified): the Verify step shows each group's items together.
        CollectionAssert.AreEqual(new[] { completion.Items[0], completion.Items[2], completion.Items[3], completion.Items[1] },
            completion.DisplayItems.ToArray(), "display order follows the groups, batch order within a group");
        CollectionAssert.AreEqual(completion.Groups.SelectMany(group => group.Items).ToArray(), completion.DisplayItems.ToArray());
        UpdateJobCompletion single = new([Outcome("a", UpdateHistoryStatus.InstallerFailed)]);
        CollectionAssert.AreEqual(single.Items.ToArray(), single.DisplayItems.ToArray(), "a single item is unchanged");
        Assert.AreEqual("Update verified", new UpdateJobCompletion([Outcome("a", UpdateHistoryStatus.Success)]).Headline, "a one-app headline is unchanged");
    }

    [TestMethod]
    [TestCategory("FR-UPD-11")]
    public void ProgressWordsForABatchNeverOfferACancel()
    {
        Assert.AreEqual("Updating 3 apps, one at a time", UpdateJobProgressText.Heading(3));
        Assert.AreEqual("Updating 1 app", UpdateJobProgressText.Heading(1));
        Assert.AreEqual("Stop after this app", UpdateJobProgressText.StopAfterCurrentLabel);
        StringAssert.Contains(UpdateJobProgressText.StopAfterCurrentHelp, "finishes and its result is recorded");
        Assert.AreEqual("Waiting to start", UpdateJobProgressText.QueuedItemState(2, 1, null));
        Assert.AreEqual("Verified", UpdateJobProgressText.QueuedItemState(0, 1, "Verified"));
        foreach (string text in new[] { UpdateJobProgressText.StopAfterCurrentLabel, UpdateJobProgressText.StopAfterCurrentHelp, UpdateJobProgressText.StopRequestedText })
            Assert.IsFalse(text.Contains("Cancel", StringComparison.OrdinalIgnoreCase), "stopping the batch is never a cancel of a running update");
    }

    [TestMethod]
    [TestCategory("AC-UPD-09")]
    public void AFinishedAppShowsItsOutcomeAndTheNextAppWaitsForThePreviousOneDuringTheSettleWait()
    {
        // Review L1: during the settle wait the finished current app shows its outcome (no progress bar, no "Waiting to start").
        string?[] labels = ["Verified", null, null];
        UpdateJobItemLine finished = UpdateJobProgressText.Line(0, 0, labels, stopRequested: false);
        Assert.IsFalse(finished.IsLive);
        Assert.AreEqual("Verified", finished.Text);
        UpdateJobItemLine next = UpdateJobProgressText.Line(1, 0, labels, stopRequested: false);
        Assert.IsTrue(next.IsLive, "the next app carries the live state while the batch waits");
        Assert.AreEqual(UpdateJobProgressText.WaitingForPreviousApp, next.Text);
        Assert.AreEqual("Waiting for the previous app to finish…", UpdateJobProgressText.LiveState(next, null));
        Assert.AreEqual("Waiting to start", UpdateJobProgressText.Line(2, 0, labels, stopRequested: false).Text);
        Assert.IsFalse(UpdateJobProgressText.Line(1, 0, labels, stopRequested: true).IsLive, "after a stop request the next app won't start, so it doesn't wait");

        // While the current app runs it is the only live line and follows the polled progress.
        string?[] running = ["Verified", null, null];
        UpdateJobItemLine current = UpdateJobProgressText.Line(1, 1, running, stopRequested: false);
        Assert.IsTrue(current.IsLive);
        Assert.IsNull(current.Text);
        Assert.AreEqual("Waiting to start", UpdateJobProgressText.LiveState(current, null));
        Assert.IsFalse(UpdateJobProgressText.Line(2, 1, running, stopRequested: false).IsLive);
        Assert.IsTrue(UpdateJobProgressText.Line(0, -1, [null], stopRequested: false).IsLive, "a one-app job is always live until it finishes");
        Assert.IsFalse(UpdateJobProgressText.Line(0, 0, ["Failed"], stopRequested: false).IsLive);
        Assert.IsFalse(UpdateJobProgressText.Line(0, -1, new string?[2], stopRequested: false).IsLive, "nothing is live before the first app starts");
    }

    [TestMethod]
    [TestCategory("FR-UPD-10")]
    public void TheBandNamesTheAppUpdatingNowAndCountsReviewPreparation()
    {
        Assert.AreEqual("Updating Firefox (2 of 4)", UpdateJobProgressText.BatchSubject("Firefox", 1, 4));
        Assert.AreEqual("Updating Firefox (2 of 4) · Waiting for Windows administrator permission (UAC); installer progress not observed",
            UpdateJobProgressText.BandLabel(UpdateJobProgressText.BatchSubject("Firefox", 1, 4), "Waiting for Windows administrator permission (UAC); installer progress not observed"));
        Assert.AreEqual("Verifying installed version", UpdateJobProgressText.BandLabel(null, "Verifying installed version"));
        Assert.AreEqual("Preparing review… 2 of 5", UpdateJobProgressText.PreparingReview("Preparing review…", new(2, 5)));
        Assert.AreEqual("Preparing review…", UpdateJobProgressText.PreparingReview("Preparing review…", new(0, 1)));
        Assert.AreEqual("Preparing review…", UpdateJobProgressText.PreparingReview("Preparing review…", null));
    }

    [TestMethod]
    [TestCategory("FR-UPD-10")]
    [TestCategory("FR-UI-03")]
    public void TheSheetShowsWhenTheReviewExpires()
    {
        // Review L2.
        DateTimeOffset expires = new(2026, 11, 2, 14, 32, 0, TimeSpan.Zero);
        string time = expires.ToString("t", System.Globalization.CultureInfo.CurrentCulture);
        UpdateReviewSheet single = UpdateReviewSheet.CreateBatch([Input("alpha", UpdateScope.User)], null, expires);
        Assert.AreEqual(expires, single.ReviewExpiresAt);
        Assert.AreEqual($"This review is valid until {time}. If the update hasn't started by then, review again to update.", single.ExpiryNote);
        UpdateReviewSheet batch = UpdateReviewSheet.CreateBatch([Input("alpha", UpdateScope.User), Input("beta", UpdateScope.User)], null, expires);
        StringAssert.Contains(batch.ExpiryNote, time);
        StringAssert.Contains(batch.ExpiryNote, "An app that hasn't started by then won't start");
        Assert.IsNull(UpdateReviewSheet.CreateBatch([Input("alpha", UpdateScope.User)]).ExpiryNote);
    }

    [TestMethod]
    [TestCategory("FR-POL-01")]
    [TestCategory("FR-UI-05")]
    public void EveryInventoryStatusHasAChipSoSkippedRowsNeverReadAsStatusUnavailable()
    {
        // Review L3: Results and Applications rows use StewardStatus.From → StatusVocabulary.For(row.Status).
        foreach (UpdateIntelligenceStatus status in Enum.GetValues<UpdateIntelligenceStatus>())
            Assert.AreNotEqual("Status unavailable", StatusVocabulary.For(status.ToString()).Label, status.ToString());
        Assert.AreEqual("Skipped", StatusVocabulary.For(nameof(UpdateIntelligenceStatus.Skipped)).Label);
        CollectionAssert.Contains(StatusVocabulary.FilterLabels.ToArray(), "Skipped");
    }

    private static UpdateJobItemOutcome Outcome(string key, UpdateHistoryStatus status) =>
        new($"App {key}", $"Contoso.{key}", "1.0", "2.0", UpdateOutcomePresenter.ForStatus(status, "diag", status == UpdateHistoryStatus.InstallerFailed ? 1 : 0, "2.0"), key);

    [TestMethod]
    [TestCategory("FR-UI-04")]
    [Description("UI review 2026-11-13 (item 13): how sure the matches are is said once when every app shares the answer, and per app when they differ.")]
    public void HowSureIsSaidOnceWhenEveryAppSharesIt()
    {
        UpdateReviewSheet one = UpdateReviewSheet.CreateBatch([Input("alpha", UpdateScope.User)]);
        Assert.AreEqual("High confidence", one.SharedConfidence);
        Assert.AreEqual("How sure: High confidence.", one.SharedConfidenceLine);

        UpdateReviewSheet same = UpdateReviewSheet.CreateBatch([Input("alpha", UpdateScope.User), Input("beta", UpdateScope.Machine)]);
        Assert.AreEqual("How sure: High confidence, for every app.", same.SharedConfidenceLine);

        UpdateReviewSheetInput medium = Input("beta", UpdateScope.User);
        UpdateReviewSheet mixed = UpdateReviewSheet.CreateBatch([Input("alpha", UpdateScope.User), medium with { Detail = medium.Detail! with { Confidence = "Medium" } }]);
        Assert.IsNull(mixed.SharedConfidence, "when the apps differ nothing is summarised, and each row shows its own");
        Assert.IsNull(mixed.SharedConfidenceLine);
        Assert.AreEqual("Medium confidence", mixed.Items[1].ConfidenceSummary);
    }

    private static UpdateReviewSheetInput Input(string key, UpdateScope scope, bool running = false)
    {
        UpdateJobSelection selection = new(key, UpdateProvider.WinGet, UpdateSource.WinGet, $"Contoso.{key.ToUpperInvariant()}", "1.0", "2.0", scope,
            UpdateArchitecture.X64, UpdateChannel.Stable, "fixture-evidence");
        UpdateDetail detail = new(key, "1.0", "2.0", "winget", selection.PackageId, "High", "Exact match.", "winget: live result from today.", "X64", null,
            "Review the update.", [], [], []);
        string name = char.ToUpperInvariant(key[0]) + key[1..];
        return new(selection, name, "Contoso", detail, running ? new RunningApplicationDetection(true, [selection.PackageId]) : RunningApplicationDetection.NotDetected);
    }
}
