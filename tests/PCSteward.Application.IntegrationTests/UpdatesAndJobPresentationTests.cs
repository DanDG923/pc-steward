using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// Task 73 Phase 2 (ADR-030 decisions 2–3): the grouped Updates page, version-jump badges, the structured review sheet, the job
/// stepper and the honest outcome mapping. Portable presentation only; nothing here grants update authority.
/// </summary>
[TestClass]
public sealed class UpdatesAndJobPresentationTests
{
    private static readonly string[] ExpectedFactLabels = ["App", "Version", "Publisher", "Source", "Updated with", "Package ID", "Scope", "Architecture", "Channel"];
    private static readonly Dictionary<UpdateOutcomeKind, string> OutcomeLabels = new()
    {
        [UpdateOutcomeKind.Verified] = "Verified",
        [UpdateOutcomeKind.RestartRequired] = "Restart required",
        [UpdateOutcomeKind.InstalledNotVerified] = "Installed, not verified",
        [UpdateOutcomeKind.Failed] = "Failed",
        [UpdateOutcomeKind.NotStarted] = "Not started",
        [UpdateOutcomeKind.ResultNotVerified] = "Result not verified"
    };
    private static readonly string[] ReadyKeys = ["ready"];
    private static readonly string[] NeedsReviewKeys = ["review", "unavailable"];
    private static readonly string[] IgnoredKeys = ["ignored", "pinned"];
    private static readonly string[] OverflowLabels = ["Ignore updates for this app", "Pin current version", "Skip this version (2.0)", "Details"];
    private static readonly string[] StepLabels = ["Review", "Update", "Verify"];
    private static readonly int[] StepNumbers = [1, 2, 3];
    private static readonly ReviewRiskKind[] RiskOrder = [ReviewRiskKind.RunningApplication, ReviewRiskKind.Restart, ReviewRiskKind.Administrator];
    private static readonly string[] SevenZipRecordKeys = ["registry:7zip", "winget:7zip"];
    private static readonly string[] ShownSevenZipKeys = ["registry:7zip", "user:7zip", "fork:7zip"];
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    [Description("FR-UPD-01; SPEC §7.3; ADR-030 decision 2: Ready is exactly the verified status, review-only evidence is Needs review, pins and ignores are Ignored and skipped; nothing is preselected.")]
    public void UpdatesAreGroupedAndNothingIsPreselected()
    {
        InventoryRow[] rows =
        [
            Row("ready", "VerifiedUpdate", "1.2.3"),
            Row("review", "ReviewRequired", "4.0"),
            Row("unavailable", "FailedUnavailable", "1.0"),
            Row("ignored", "Ignored", "2.0"),
            Row("pinned", "Pinned", "3.0"),
            Row("current", "Current", "5.0"),
            Row("unmanaged", "Unmanaged", "6.0")
        ];
        Dictionary<string, UpdateDetail> details = new(StringComparer.Ordinal)
        {
            ["ready"] = Detail("ready", "1.2.3", "1.3.0"),
            ["review"] = Detail("review", "4.0", "5.0", confidence: "Low", reasons: ["ArchitectureConflict", "ChannelConflict"]),
            ["unavailable"] = Detail("unavailable", "1.0", null)
        };
        StableDecision[] decisions =
        [
            new(Guid.NewGuid(), "ignored", "Ignore", "Indefinitely", Now.AddDays(-1)),
            new(Guid.NewGuid(), "pinned", "PinCurrentVersion", "3.0", Now.AddDays(-2))
        ];

        UpdatesPagePresentation page = UpdatesPagePresenter.Build(rows, details, true, decisions, null, Now);

        CollectionAssert.AreEqual(ReadyKeys, page.Ready.Select(row => row.StableProductKey).ToArray());
        CollectionAssert.AreEquivalent(NeedsReviewKeys, page.NeedsReview.Select(row => row.StableProductKey).ToArray());
        CollectionAssert.AreEquivalent(IgnoredKeys, page.IgnoredAndSkipped.Select(row => row.StableProductKey).ToArray());
        Assert.IsEmpty(page.SelectedKeys, "nothing is preselected");
        Assert.IsFalse(page.CanReview);
        Assert.AreEqual("Review selected updates", page.ReviewButtonLabel);
        Assert.IsTrue(page.CanReviewAll);
        // UI polish phase 3 (approved target T-Updates-Ready): the review-all button names the one ready app, or counts them.
        Assert.AreEqual("Review update for App ready", page.ReviewAllLabel);
        CollectionAssert.AreEqual(ReadyKeys, page.ReadyKeys.ToArray(), "Review all ready chooses exactly the Ready group");
        Assert.IsTrue(page.HasAnyRow);
        Assert.AreEqual(decisions[0].Id, page.IgnoredAndSkipped.Single(row => row.StableProductKey == "ignored").DecisionId);
        Assert.AreEqual(decisions[1].Id, page.IgnoredAndSkipped.Single(row => row.StableProductKey == "pinned").DecisionId);
        StringAssert.Contains(page.IgnoredAndSkipped.Single(row => row.StableProductKey == "pinned").ReasonText, "version 3.0");
        Assert.IsTrue(page.IgnoredAndSkipped.All(row => row.Actions.Any(action => action.Action == UpdateRowAction.Reverse)), "ignored and pinned rows are reversible");

        UpdatesPagePresentation selected = UpdatesPagePresenter.Build(rows, details, true, decisions, ["ready"], Now);
        Assert.IsTrue(selected.CanReview);
        Assert.IsTrue(selected.AllReadySelected);
        Assert.AreEqual("Review update for App ready", selected.ReviewButtonLabel);
        Assert.IsEmpty(UpdatesPagePresenter.Build(rows, details, true, decisions, ["review"], Now).SelectedKeys, "a needs-review row can never be selected");
        Assert.IsEmpty(UpdatesPagePresenter.Build(rows, details, true, decisions, ["ignored"], Now).SelectedKeys);
        CollectionAssert.AreEqual(ReadyKeys, UpdatesPagePresenter.Build(rows, details, true, decisions, ["unmanaged", "ready", "pinned", "current", "unavailable"], Now).SelectedKeys.ToArray(),
            "only Ready keys survive a selection, whatever is passed in");
    }

    [TestMethod]
    [Description("SPEC §7.3; audit §6.4; ADR-030 decision 2: needs-review and ignored rows have no update control at all, and every needs-review row carries a reason in words.")]
    public void NeedsReviewRowsHaveAReasonInWordsAndNoUpdateControl()
    {
        InventoryRow[] rows =
        [
            Row("low", "ReviewRequired", "1.0"),
            Row("arch", "ReviewRequired", "1.0"),
            Row("stale", "ReviewRequired", "1.0"),
            Row("unavailable", "FailedUnavailable", "1.0"),
            Row("incomparable", "ReviewRequired", "1.0"),
            Row("incomplete", "VerifiedUpdate", "1.0"),
            Row("nodetail", "ReviewRequired", "1.0")
        ];
        Dictionary<string, UpdateDetail> details = new(StringComparer.Ordinal)
        {
            ["low"] = Detail("low", "1.0", "2.0", confidence: "Low"),
            ["arch"] = Detail("arch", "1.0", "2.0", reasons: ["ArchitectureConflict"]),
            ["stale"] = Detail("stale", "1.0", "2.0", freshness: "winget: cached result from yesterday; source data may be stale."),
            ["unavailable"] = Detail("unavailable", "1.0", null),
            ["incomparable"] = Detail("incomparable", "1.0", "2.0-vendor", reasons: ["IncomparableVersion", "ChannelConflict", "SomethingNew"]),
            ["incomplete"] = Detail("incomplete", "1.0", null)
        };

        UpdatesPagePresentation page = UpdatesPagePresenter.Build(rows, details, true, [], ["incomplete"], Now);

        Assert.IsEmpty(page.Ready, "a verified row without complete evidence fails closed into Needs review");
        Assert.HasCount(rows.Length, page.NeedsReview);
        foreach (UpdateRowPresentation row in page.NeedsReview.Concat(page.IgnoredAndSkipped))
        {
            Assert.IsFalse(row.HasUpdateControl, row.StableProductKey);
            Assert.IsFalse(row.CanSelect, row.StableProductKey);
            Assert.IsFalse(string.IsNullOrWhiteSpace(row.ReasonText), row.StableProductKey);
            Assert.IsNull(row.Badge, "badges are shown only on Ready rows");
            Assert.IsFalse(row.ReasonText.Contains("Conflict", StringComparison.Ordinal), "reasons are words, not enum names");
        }
        StringAssert.Contains(Reason(page, "low"), "low confidence");
        StringAssert.Contains(Reason(page, "arch"), "different architecture");
        StringAssert.Contains(Reason(page, "stale"), "cached and may be out of date");
        StringAssert.Contains(Reason(page, "unavailable"), "couldn't confirm whether an update is available");
        StringAssert.Contains(Reason(page, "incomparable"), "can't be compared reliably");
        StringAssert.Contains(Reason(page, "incomparable"), "different release channel");
        StringAssert.Contains(Reason(page, "incomparable"), "Another evidence check needs your review.");
        StringAssert.Contains(Reason(page, "incomplete"), "incomplete");
        StringAssert.Contains(Reason(page, "nodetail"), "needs your review");
        Assert.IsEmpty(page.SelectedKeys);
        Assert.IsFalse(page.CanReviewAll);
        Assert.IsFalse(page.NeedsReview.Single(row => row.StableProductKey == "unavailable").Actions.Any(action => action.Action is UpdateRowAction.IgnoreApp or UpdateRowAction.PinCurrentVersion),
            "unavailable evidence offers no decision");

        UpdatesPagePresentation sourcesOff = UpdatesPagePresenter.Build([Row("ready", "VerifiedUpdate", "1.0")],
            new Dictionary<string, UpdateDetail>(StringComparer.Ordinal) { ["ready"] = Detail("ready", "1.0", "2.0") }, false, [], ["ready"], Now);
        Assert.IsEmpty(sourcesOff.Ready, "with sources off nothing is ready");
        StringAssert.Contains(sourcesOff.NeedsReview.Single().ReasonText, "Package sources are off");
    }

    [TestMethod]
    [Description("FR-POL-01–05; owner decision 2026-11-02: the overflow offers Ignore, Pin, Skip this version (V) and Details, each worded distinctly; Skip names the exact version.")]
    public void OverflowActionsKeepIgnorePinAndSkipDistinct()
    {
        UpdatesPagePresentation page = UpdatesPagePresenter.Build([Row("ready", "VerifiedUpdate", "1.0"), Row("review", "ReviewRequired", "1.0"), Row("unavailable", "FailedUnavailable", "1.0")],
            new Dictionary<string, UpdateDetail>(StringComparer.Ordinal)
            {
                ["ready"] = Detail("ready", "1.0", "2.0"),
                ["review"] = Detail("review", "1.0", "3.0", confidence: "Low"),
                ["unavailable"] = Detail("unavailable", "1.0", null)
            }, true, [], null, Now);
        UpdateRowActionPresentation[] actions = [.. page.Ready.Single().Actions];
        CollectionAssert.AreEqual(OverflowLabels, actions.Select(action => action.Label).ToArray());
        UpdateRowActionPresentation skip = actions.Single(action => action.Action == UpdateRowAction.SkipVersion);
        Assert.AreEqual("2.0", skip.Version, "the exact candidate version is what is skipped");
        Assert.IsTrue(actions.Where(action => action.Action != UpdateRowAction.SkipVersion).All(action => action.Version is null));
        Assert.AreEqual("3.0", page.NeedsReview.Single(row => row.StableProductKey == "review").Actions.Single(action => action.Action == UpdateRowAction.SkipVersion).Version,
            "a review-only candidate can be skipped too; skipping never makes it actionable");
        Assert.IsFalse(page.NeedsReview.Single(row => row.StableProductKey == "unavailable").Actions.Any(action => action.Action == UpdateRowAction.SkipVersion),
            "no version is invented when none is known");
        Assert.AreEqual(actions.Length, actions.Select(action => action.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [TestMethod]
    [Description("FR-POL-01/05; owner decision 2026-11-02: a skipped version sits under Ignored and skipped with its version named and a Stop skipping reversal; it is never selectable.")]
    public void SkippedRowsAreGroupedWithIgnoredNamedAndReversible()
    {
        StableDecision skip = new(Guid.NewGuid(), "skipped", UserDecisionService.SkipVersionKind, "2.0", Now.AddDays(-1));
        UpdatesPagePresentation page = UpdatesPagePresenter.Build([Row("skipped", "Skipped", "1.0")],
            new Dictionary<string, UpdateDetail>(StringComparer.Ordinal) { ["skipped"] = Detail("skipped", "1.0", "2.0") }, true, [skip], null, Now);

        UpdateRowPresentation row = page.IgnoredAndSkipped.Single();
        Assert.IsEmpty(page.Ready);
        Assert.IsFalse(row.CanSelect);
        Assert.IsFalse(row.HasUpdateControl);
        StringAssert.Contains(row.ReasonText, "Skipped version 2.0");
        Assert.AreEqual(skip.Id, row.DecisionId);
        Assert.AreEqual("Stop skipping 2.0", row.Actions.Single(action => action.Action == UpdateRowAction.Reverse).Label);
        Assert.IsFalse(row.Actions.Any(action => action.Label.Contains("Ignore", StringComparison.OrdinalIgnoreCase)), "distinct from ignore");
    }

    [TestMethod]
    [Description("FR-UPD-01; ADR-030 decision 2: version-jump badges — patch, minor, major (highlighted) — and no badge when unknown.")]
    public void VersionJumpBadgesClassifyNumericVersionsOnly()
    {
        Assert.AreEqual(VersionJump.Patch, VersionJumpPresenter.Classify("1.2.3", "1.2.4"));
        Assert.AreEqual(VersionJump.Patch, VersionJumpPresenter.Classify("1.2.3.4", "1.2.3.5"));
        Assert.AreEqual(VersionJump.Minor, VersionJumpPresenter.Classify("1.2.3", "1.3.0"));
        Assert.AreEqual(VersionJump.Minor, VersionJumpPresenter.Classify("1.2", "1.10"));
        Assert.AreEqual(VersionJump.Major, VersionJumpPresenter.Classify("1.9.9", "2.0.0"));
        Assert.AreEqual(VersionJump.Major, VersionJumpPresenter.Classify("9", "10"));
        Assert.AreEqual(VersionJump.Patch, VersionJumpPresenter.Classify("1.2", "1.2.1"), "missing components count as zero");
        foreach ((string? installed, string? candidate) in new (string?, string?)[]
        {
            ("1.2.3", "1.2.3"), ("2.0", "1.9"), ("1.0", null), (null, "2.0"), ("", "2.0"), ("1.0", "2.0-beta"), ("1.0-beta", "1.0"),
            ("1.0", "2.0+build"), ("v1.0", "v2.0"), ("1..0", "2.0"), ("1.0", "1.0.0"), ("unknown", "2.0"),
            ("1.2.3.4.5.6.7.8.9", "2.0")
        })
            Assert.AreEqual(VersionJump.Unknown, VersionJumpPresenter.Classify(installed, candidate), $"{installed} → {candidate}");

        Assert.IsNull(VersionJumpPresenter.Badge(VersionJump.Unknown), "unknown shows no badge");
        foreach (VersionJump jump in new[] { VersionJump.Patch, VersionJump.Minor, VersionJump.Major })
        {
            VersionJumpBadge badge = VersionJumpPresenter.Badge(jump)!;
            Assert.IsFalse(string.IsNullOrEmpty(badge.Glyph), "icon plus text");
            Assert.AreEqual(jump.ToString(), badge.Label);
            StringAssert.Contains(badge.AccessibleName, badge.Label);
            Assert.AreEqual(jump == VersionJump.Major, badge.IsHighlighted, "only major is highlighted");
        }

        UpdatesPagePresentation page = UpdatesPagePresenter.Build([Row("a", "VerifiedUpdate", "1.0.0"), Row("b", "VerifiedUpdate", "1.0")],
            new Dictionary<string, UpdateDetail>(StringComparer.Ordinal) { ["a"] = Detail("a", "1.0.0", "2.0.0"), ["b"] = Detail("b", "1.0", "1.0-rc1") }, true, [], null, Now);
        Assert.AreEqual(VersionJump.Major, page.Ready.Single(row => row.StableProductKey == "a").Badge!.Jump);
        Assert.IsNull(page.Ready.Single(row => row.StableProductKey == "b").Badge);
        Assert.AreEqual("1.0.0 → 2.0.0", page.Ready.Single(row => row.StableProductKey == "a").VersionText);
    }

    [TestMethod]
    [Description("FR-UI-03/04; SPEC §17.2; FR-UPD-04–06 (audit P-H6, P-M3): the review sheet has every §17.2 fact, confidence in words with evidence, risk callouts, one specific button and the shield only when administrator approval is required.")]
    public void ReviewSheetCarriesEverySpecFactConfidenceRisksAndASpecificButton()
    {
        UpdateJobSelection machine = Selection(UpdateScope.Machine, UpdateChannel.Stable);
        UpdateDetail detail = Detail("winget:contoso.widget", "1.0", "2.0", packageId: "Contoso.Widget", confidence: "High",
            explanation: "Exact package ID and publisher match.");
        UpdateReviewSheet sheet = UpdateReviewSheet.Create(machine, "Contoso Widget", "Contoso Ltd", detail, RunningApplicationDetection.NotDetected,
            AdministratorRequirementPresenter.ForSelection(machine));

        CollectionAssert.AreEqual(ExpectedFactLabels, sheet.Facts.Select(fact => fact.Label).ToArray());
        CollectionAssert.AreEqual(ExpectedFactLabels, UpdateReviewSheet.FactLabels.ToArray());
        Assert.IsTrue(sheet.Facts.All(fact => !string.IsNullOrWhiteSpace(fact.Value)), "every fact has a value");
        Assert.AreEqual("Contoso Widget", Fact(sheet, "App"));
        Assert.AreEqual("1.0 → 2.0", Fact(sheet, "Version"));
        Assert.AreEqual("Contoso Ltd", Fact(sheet, "Publisher"));
        Assert.AreEqual("WinGet", Fact(sheet, "Source"));
        Assert.AreEqual("WinGet", Fact(sheet, "Updated with"), "the exact provider is named (protected UI behaviour)");
        Assert.AreEqual("Contoso.Widget", Fact(sheet, "Package ID"));
        Assert.AreEqual("All users (machine-wide)", Fact(sheet, "Scope"));
        Assert.AreEqual("x64", Fact(sheet, "Architecture"));
        Assert.AreEqual("Stable", Fact(sheet, "Channel"));
        Assert.AreEqual("High confidence", sheet.ConfidenceSummary);
        CollectionAssert.Contains(sheet.ConfidenceEvidence.ToArray(), "Exact package ID and publisher match.");
        Assert.IsTrue(sheet.ConfidenceEvidence.Any(line => line.Contains("fixture-evidence", StringComparison.Ordinal)));
        CollectionAssert.AreEqual(RiskOrder, sheet.Risks.Select(risk => risk.Kind).ToArray());
        StringAssert.Contains(sheet.Risks.Single(risk => risk.Kind == ReviewRiskKind.RunningApplication).Message, "never closes apps");
        StringAssert.Contains(sheet.Risks.Single(risk => risk.Kind == ReviewRiskKind.Restart).Message, "never restarts your PC");
        Assert.AreEqual(AdministratorRequirementPresenter.ForSelection(machine).Label, sheet.Risks.Single(risk => risk.Kind == ReviewRiskKind.Administrator).Title);
        Assert.IsTrue(sheet.ShowsAdministratorShield, "machine-wide: the shield is shown");
        Assert.AreEqual("Update 1 app", sheet.PrimaryButtonLabel);
        StringAssert.Contains(sheet.PrimaryAutomationName, sheet.PrimaryButtonLabel);
        StringAssert.StartsWith(sheet.PrimaryAutomationName, sheet.PrimaryButtonLabel, "label-in-name (WCAG 2.5.3, Task 73 Phase 5)");
        Assert.AreEqual("Cancel", UpdateReviewSheet.CancelButtonLabel);
        Assert.AreEqual(RunningApplicationDetection.NotDetected.CoverageDisclosure, sheet.CoverageDisclosure, "the existing coverage disclosure is kept");
        StringAssert.Contains(sheet.Attestation, "By choosing Update 1 app");
        StringAssert.Contains(sheet.Attestation, "doesn't record the update as successful until it checks the installed version");
        Assert.AreEqual("Review update", sheet.Title);
        foreach (string text in new[] { sheet.Title, UpdateReviewSheet.Intro, sheet.PrimaryButtonLabel, sheet.Attestation })
            Assert.IsFalse(text.Contains("approved", StringComparison.OrdinalIgnoreCase), $"never 'approved' before approval: {text}");

        UpdateJobSelection user = Selection(UpdateScope.User, UpdateChannel.Unknown);
        RunningApplicationDetection running = new(true, ["widget.exe"]);
        UpdateReviewSheet userSheet = UpdateReviewSheet.Create(user, "Contoso Widget", "", null, running, AdministratorRequirementPresenter.ForSelection(user));
        Assert.IsFalse(userSheet.ShowsAdministratorShield, "current-user: no shield");
        Assert.AreEqual("Update 1 app anyway", userSheet.PrimaryButtonLabel, "a running app needs an explicit 'anyway'");
        StringAssert.Contains(userSheet.Risks.Single(risk => risk.Kind == ReviewRiskKind.RunningApplication).Message, "widget.exe");
        StringAssert.Contains(userSheet.Risks.Single(risk => risk.Kind == ReviewRiskKind.RunningApplication).Message, "will not close it");
        Assert.AreEqual(StatusTone.Attention, userSheet.Risks.Single(risk => risk.Kind == ReviewRiskKind.RunningApplication).Tone);
        Assert.AreEqual("Current user", Fact(userSheet, "Scope"));
        Assert.AreEqual("Not stated by the source", Fact(userSheet, "Channel"));
        Assert.AreEqual("Not recorded", Fact(userSheet, "Publisher"));
        Assert.AreEqual("Confidence not available for this exact update", userSheet.ConfidenceSummary);

        UpdateDetail other = Detail("winget:contoso.widget", "1.0", "3.0", packageId: "Contoso.Widget", confidence: "High");
        Assert.AreEqual("Confidence not available for this exact update",
            UpdateReviewSheet.Create(user, "Contoso Widget", "Contoso", other, RunningApplicationDetection.NotDetected, AdministratorRequirementPresenter.ForSelection(user)).ConfidenceSummary,
            "confidence for a different target version is not borrowed");
    }

    [TestMethod]
    [Description("FR-UI-04: confidence levels are shown in words.")]
    public void ConfidenceIsShownInWords()
    {
        Assert.AreEqual("High confidence", ReviewReasonText.Confidence("High"));
        Assert.AreEqual("Medium confidence", ReviewReasonText.Confidence("Medium"));
        Assert.AreEqual("Low confidence", ReviewReasonText.Confidence("Low"));
        Assert.AreEqual("No confident match", ReviewReasonText.Confidence("None"));
        Assert.AreEqual("Confidence not available", ReviewReasonText.Confidence(null));
        foreach (MappingReviewReason reason in Enum.GetValues<MappingReviewReason>())
        {
            string? words = ReviewReasonText.Describe(reason.ToString());
            if (reason == MappingReviewReason.None) { Assert.IsNull(words); continue; }
            Assert.IsFalse(string.IsNullOrWhiteSpace(words), reason.ToString());
            Assert.AreNotEqual("Another evidence check needs your review.", words, $"{reason} has its own words");
        }
    }

    [TestMethod]
    [Description("FR-UPD-01; ADR-030 decision 3: the job has three steps, Review → Update → Verify, with one current step.")]
    public void StepperHasThreeStepsWithOneCurrent()
    {
        foreach (UpdateJobStep current in Enum.GetValues<UpdateJobStep>())
        {
            IReadOnlyList<UpdateJobStepPresentation> steps = UpdateJobSteps.For(current);
            CollectionAssert.AreEqual(StepLabels, steps.Select(step => step.Label).ToArray());
            CollectionAssert.AreEqual(StepNumbers, steps.Select(step => step.Number).ToArray());
            Assert.AreEqual(1, steps.Count(step => step.State == UpdateJobStepState.Current));
            Assert.IsTrue(steps.Where(step => step.Step < current).All(step => step.State == UpdateJobStepState.Completed));
            Assert.IsTrue(steps.Where(step => step.Step > current).All(step => step.State == UpdateJobStepState.Upcoming));
            StringAssert.Contains(steps[(int)current].AccessibleName, "current step");
        }
    }

    [TestMethod]
    [Description("FR-UPD-14; SPEC §17.4: per-item progress words come from the existing review-bound progress presentation and stay indeterminate.")]
    public void ProgressWordsFollowTheExistingProgressPresentation()
    {
        Guid job = Guid.NewGuid(), correlation = Guid.NewGuid();
        UpdateProgressPresentation presentation = new(job, correlation, UpdateScope.Machine);
        Assert.AreEqual("Waiting to start", UpdateJobProgressText.ItemState(null));
        Assert.AreEqual("Waiting to start", UpdateJobProgressText.ItemState(presentation));
        Assert.IsTrue(presentation.Consume(new(job, correlation, 1, UpdateProgressPhase.Queued, null, null, false)));
        Assert.IsTrue(presentation.Consume(new(job, correlation, 2, UpdateProgressPhase.ElevationRequested, null, null, false)));
        Assert.AreEqual(UpdateProgressPhase.ElevationRequested, presentation.Phase);
        StringAssert.Contains(UpdateJobProgressText.ItemState(presentation), "UAC");
        Assert.IsTrue(presentation.Consume(new(job, correlation, 3, UpdateProgressPhase.HandedToHelper, null, null, false)));
        Assert.IsTrue(presentation.Consume(new(job, correlation, 4, UpdateProgressPhase.Verifying, null, null, false)));
        Assert.AreEqual("Checking the installed version", UpdateJobProgressText.ItemState(presentation));
        presentation.MarkUnavailable();
        StringAssert.Contains(UpdateJobProgressText.ItemState(presentation), "aren't available");
        Assert.IsTrue(presentation.IsIndeterminate);
        Assert.AreEqual("Updating 1 app", UpdateJobProgressText.Heading(1));
        Assert.IsFalse(UpdateJobProgressText.LeaveNote.Contains("Cancel update", StringComparison.Ordinal));
    }

    [TestMethod]
    [Description("FR-UPD-07–09; FR-HIS-03; UJ-04 step 8 (audit P-H3; independent review): every recorded status maps to Verified, Restart required, Installed, not verified, Result not verified, Failed or Not started; only Success is Verified; a timeout is never Failed or Review again.")]
    public void EveryRecordedStatusMapsToAnHonestOutcome()
    {
        Dictionary<UpdateHistoryStatus, UpdateOutcomeKind> expected = new()
        {
            [UpdateHistoryStatus.Success] = UpdateOutcomeKind.Verified,
            [UpdateHistoryStatus.RestartRequired] = UpdateOutcomeKind.RestartRequired,
            [UpdateHistoryStatus.Inconclusive] = UpdateOutcomeKind.InstalledNotVerified,
            [UpdateHistoryStatus.VerificationRequired] = UpdateOutcomeKind.ResultNotVerified,
            [UpdateHistoryStatus.VerificationFailed] = UpdateOutcomeKind.InstalledNotVerified,
            [UpdateHistoryStatus.Running] = UpdateOutcomeKind.ResultNotVerified,
            [UpdateHistoryStatus.ElevationDenied] = UpdateOutcomeKind.NotStarted,
            [UpdateHistoryStatus.Cancelled] = UpdateOutcomeKind.NotStarted,
            [UpdateHistoryStatus.Queued] = UpdateOutcomeKind.NotStarted,
            [UpdateHistoryStatus.RunningApplicationWarning] = UpdateOutcomeKind.NotStarted,
            [UpdateHistoryStatus.SourceFailed] = UpdateOutcomeKind.Failed,
            [UpdateHistoryStatus.DownloadFailed] = UpdateOutcomeKind.Failed,
            [UpdateHistoryStatus.InstallerFailed] = UpdateOutcomeKind.Failed,
            [UpdateHistoryStatus.TimedOut] = UpdateOutcomeKind.ResultNotVerified,
            [UpdateHistoryStatus.ProductFailed] = UpdateOutcomeKind.Failed
        };
        CollectionAssert.AreEquivalent(Enum.GetValues<UpdateHistoryStatus>(), expected.Keys.ToArray(), "every enum value is covered");
        CollectionAssert.AreEquivalent(Enum.GetValues<UpdateOutcomeKind>(), OutcomeLabels.Keys.ToArray(), "every display state has a label");
        foreach (UpdateHistoryStatus status in Enum.GetValues<UpdateHistoryStatus>())
        {
            UpdateOutcomePresentation outcome = UpdateOutcomePresenter.ForStatus(status, "diag", 1, "2.0");
            Assert.AreEqual(expected[status], outcome.Kind, status.ToString());
            Assert.AreEqual(OutcomeLabels[outcome.Kind], outcome.Label, status.ToString());
            Assert.IsFalse(string.IsNullOrWhiteSpace(outcome.Summary), status.ToString());
            Assert.IsFalse(string.IsNullOrWhiteSpace(outcome.NextStep), status.ToString());
            Assert.IsFalse(string.IsNullOrEmpty(outcome.Glyph), "icon plus text");
            StringAssert.Contains(outcome.TechnicalDetail, status.ToString());
            Assert.AreEqual(status == UpdateHistoryStatus.ElevationDenied, outcome.ReturnsToReview, status.ToString());
            Assert.IsFalse(outcome.Summary.Contains("Update failed", StringComparison.Ordinal), "no flattened failure title");
            if (outcome.Kind == UpdateOutcomeKind.Verified) Assert.AreEqual(UpdateHistoryStatus.Success, status, "only verified re-inventory reads as Verified");
            if (outcome.Kind == UpdateOutcomeKind.RestartRequired) Assert.AreEqual(UpdateHistoryStatus.RestartRequired, status, "only a reliable restart-pending result reads as Restart required");
            if (outcome.Kind == UpdateOutcomeKind.NotStarted) Assert.AreNotEqual(StatusTone.Critical, outcome.Tone, "not started is not shown as a failure");
            if (outcome.Kind is UpdateOutcomeKind.InstalledNotVerified or UpdateOutcomeKind.ResultNotVerified)
            {
                Assert.AreEqual(StatusTone.Attention, outcome.Tone, status.ToString());
                Assert.IsTrue(outcome.NextStep.StartsWith("Scan again", StringComparison.Ordinal), $"{status}: the next step is to scan again");
            }
        }

        // A timeout: "Result not verified" with timeout wording, Scan again, never installed, a failure or a new review.
        UpdateOutcomePresentation timedOut = UpdateOutcomePresenter.ForStatus(UpdateHistoryStatus.TimedOut, null, null, "2.0");
        Assert.AreEqual(UpdateOutcomeKind.ResultNotVerified, timedOut.Kind);
        Assert.AreEqual("Result not verified", timedOut.Label);
        Assert.AreNotEqual(StatusTone.Critical, timedOut.Tone, "a timeout is not shown as a failure");
        Assert.IsFalse(timedOut.Label.Contains("Installed", StringComparison.Ordinal), "a timeout is not shown as installed");
        Assert.IsFalse(timedOut.Label.Contains("Failed", StringComparison.Ordinal), "a timeout is not shown as failed");
        StringAssert.Contains(timedOut.Summary, "took too long and PC Steward stopped waiting");
        StringAssert.Contains(timedOut.Summary, "may have partly applied");
        StringAssert.StartsWith(timedOut.NextStep, "Scan again");
        Assert.IsFalse(timedOut.NextStep.Contains("review the update again", StringComparison.OrdinalIgnoreCase), "no Review again after a timeout");
        Assert.IsFalse(timedOut.ReturnsToReview);

        // Verification required reads as installed only when the provider reported finishing; otherwise nothing shows it ran.
        Assert.AreEqual(UpdateOutcomeKind.InstalledNotVerified,
            UpdateOutcomePresenter.ForStatus(UpdateHistoryStatus.VerificationRequired, UpdateOutcomePresenter.ProviderCompletedCheckInterruptedDiagnostic, null, "2.0").Kind);
        Assert.AreEqual(UpdateOutcomeKind.InstalledNotVerified, UpdateOutcomePresenter.ForStatus(UpdateHistoryStatus.VerificationRequired, "diag", 0, "2.0").Kind);
        Assert.AreEqual(UpdateOutcomeKind.ResultNotVerified,
            UpdateOutcomePresenter.ForStatus(UpdateHistoryStatus.VerificationRequired, "recovered-after-interrupted-started-update; verification-required", null, "2.0").Kind);
        Assert.AreEqual(UpdateOutcomeKind.ResultNotVerified, UpdateOutcomePresenter.ForStatus(UpdateHistoryStatus.VerificationRequired, null, null, "2.0").Kind);
        Assert.AreEqual(UpdateOutcomeKind.ResultNotVerified, UpdateOutcomePresenter.ForStatus((UpdateHistoryStatus)999, null, null, "2.0").Kind,
            "an unreadable recorded outcome is not described as installed");
        StringAssert.Contains(UpdateOutcomePresenter.ForStatus(UpdateHistoryStatus.ElevationDenied, "user-declined-elevation", null, "2.0").Summary, "cancelled the Windows permission prompt");
        StringAssert.Contains(UpdateOutcomePresenter.ForStatus(UpdateHistoryStatus.InstallerFailed, null, 1603, "2.0").Summary, "exit code 1603");
        Assert.AreEqual(ElevationStory.HelperMissingOutcome,
            UpdateOutcomePresenter.ForStatus(UpdateHistoryStatus.ProductFailed, UpdateOutcomePresenter.HelperMissingDiagnostic, null, "2.0").Summary);
        StringAssert.Contains(UpdateOutcomePresenter.ForStatus(UpdateHistoryStatus.Success, null, 0, "2.0").Summary, "2.0");
    }

    [TestMethod]
    [Description("FR-UPD-08/09; AC-UPD-05; UJ-04 (audit P-H3): the completion uses the recorded batch; a refused UAC prompt returns to Review; not-started results are never Failed; an unrecorded started run is never Verified.")]
    public void CompletionUsesTheRecordedBatchAndKeepsRefusalsAndNotStartedHonest()
    {
        UpdateJobSelection selection = Selection(UpdateScope.Machine, UpdateChannel.Stable);
        UpdateJobCompletion verified = UpdateOutcomePresenter.ForResult(Result(true, selection, UpdateHistoryStatus.Success), selection, "Widget");
        Assert.AreEqual(UpdateOutcomeKind.Verified, verified.Items.Single().Outcome.Kind);
        Assert.AreEqual("Update verified", verified.Headline);
        Assert.IsFalse(verified.ReturnsToReview);
        StringAssert.Contains(verified.RestartGuidance, "never restarts your PC");
        Assert.AreEqual("Widget", verified.Items.Single().AppName);
        Assert.AreEqual("1.0", verified.Items.Single().InstalledVersion);
        Assert.AreEqual("2.0", verified.Items.Single().TargetVersion);

        UpdateJobCompletion restart = UpdateOutcomePresenter.ForResult(Result(true, selection, UpdateHistoryStatus.RestartRequired), selection, "Widget");
        Assert.IsTrue(restart.RestartRequired);
        StringAssert.Contains(restart.RestartGuidance, "never restarts your PC");
        StringAssert.Contains(restart.RestartGuidance, "Restart Windows");

        UpdateJobCompletion denied = UpdateOutcomePresenter.ForResult(Result(true, selection, UpdateHistoryStatus.ElevationDenied), selection, "Widget");
        Assert.IsTrue(denied.ReturnsToReview, "a refused UAC prompt returns quietly to Review");
        Assert.AreEqual(UpdateOutcomeKind.NotStarted, denied.Items.Single().Outcome.Kind);
        Assert.AreNotEqual("Update failed", denied.Headline);

        UpdateJobCompletion inconclusive = UpdateOutcomePresenter.ForResult(Result(true, selection, UpdateHistoryStatus.Inconclusive), selection, "Widget");
        Assert.AreEqual("Installed, not verified", inconclusive.Headline);

        UpdateJobCompletion failed = UpdateOutcomePresenter.ForResult(Result(true, selection, UpdateHistoryStatus.InstallerFailed), selection, "Widget");
        Assert.AreEqual("Update failed", failed.Headline, "Failed is used only for a real failure");

        ReviewedUpdateExecutionResult changed = new(false, "The approved update evidence changed or is no longer fresh. No update was started.", null);
        UpdateJobCompletion notStarted = UpdateOutcomePresenter.ForResult(changed, selection, "Widget");
        Assert.AreEqual(UpdateOutcomeKind.NotStarted, notStarted.Items.Single().Outcome.Kind);
        StringAssert.Contains(notStarted.Items.Single().Outcome.Summary, "evidence changed");
        Assert.IsFalse(notStarted.ReturnsToReview);

        UpdateJobCompletion busy = UpdateOutcomePresenter.ForResult(new(false, "busy", new UpdateBatchResult([Entry(selection, UpdateHistoryStatus.ProductFailed)], AdmissionRefused: true)), selection, "Widget");
        Assert.AreEqual(UpdateOutcomeKind.NotStarted, busy.Items.Single().Outcome.Kind, "admission refusal never started anything");

        UpdateJobCompletion late = UpdateOutcomePresenter.ForResult(new(false, "started running after confirmation",
            new UpdateBatchResult([Entry(selection, UpdateHistoryStatus.RunningApplicationWarning)], new RunningApplicationDetection(true, ["widget.exe"]))), selection, "Widget");
        Assert.AreEqual(UpdateOutcomeKind.NotStarted, late.Items.Single().Outcome.Kind);

        // Final review L2: a data-lease refusal before the workflow runs is Not started, with honest text, never "no recorded outcome".
        UpdateJobCompletion leaseRefused = UpdateOutcomePresenter.ForResult(new(false, UpdateOutcomePresenter.DataUnavailableNotStartedMessage, null), selection, "Widget");
        Assert.AreEqual(UpdateOutcomeKind.NotStarted, leaseRefused.Items.Single().Outcome.Kind);
        Assert.AreEqual("Update not started", leaseRefused.Headline);
        StringAssert.Contains(leaseRefused.Items.Single().Outcome.Summary, "Nothing was changed.");
        Assert.IsFalse(leaseRefused.Items.Single().Outcome.Summary.Contains("recorded outcome", StringComparison.Ordinal));
        Assert.AreNotEqual(StatusTone.Critical, leaseRefused.Items.Single().Outcome.Tone);

        UpdateJobCompletion unrecorded = UpdateOutcomePresenter.ForResult(new(true, "no recorded outcome", null), selection, "Widget");
        Assert.AreEqual(UpdateOutcomeKind.ResultNotVerified, unrecorded.Items.Single().Outcome.Kind, "an unrecorded run is never Verified or described as installed");
        Assert.AreEqual("Result not verified", unrecorded.Headline);
        Assert.AreEqual("Result not verified", unrecorded.Items.Single().Outcome.Label);
        Assert.IsFalse(unrecorded.ReturnsToReview);
        StringAssert.StartsWith(unrecorded.Items.Single().Outcome.NextStep, "Scan again");

        UpdateJobCompletion interrupted = UpdateOutcomePresenter.ForResult(Result(true, selection, UpdateHistoryStatus.VerificationRequired), selection, "Widget");
        Assert.AreEqual("Result not verified", interrupted.Headline, "verification required with no evidence of installation");

        UpdateJobCompletion timedOut = UpdateOutcomePresenter.ForResult(Result(true, selection, UpdateHistoryStatus.TimedOut), selection, "Widget");
        Assert.AreEqual("Result not verified", timedOut.Headline, "a timeout is neither a failure nor shown as installed");
        StringAssert.StartsWith(timedOut.Items.Single().Outcome.NextStep, "Scan again");
        Assert.AreEqual("No outcome recorded", new UpdateJobCompletion([]).Headline);
    }

    [TestMethod]
    [Description("FR-DAT-09; UJ-04 step 9 (audit P-H4, P-M6): the completion view shows the recoverable download size in human-readable words.")]
    public void CleanupTextShowsAHumanReadableRecoverableSize()
    {
        Assert.AreEqual("0 bytes", UpdateJobCleanupText.Size(0));
        Assert.AreEqual("1 byte", UpdateJobCleanupText.Size(1));
        Assert.AreEqual("0 bytes", UpdateJobCleanupText.Size(-5));
        StringAssert.Contains(UpdateJobCleanupText.Size(1536), "KB");
        StringAssert.Contains(UpdateJobCleanupText.Size(5L * 1024 * 1024), "5 MB");
        StringAssert.Contains(UpdateJobCleanupText.Size(3L * 1024 * 1024 * 1024), "3 GB");
        StringAssert.Contains(UpdateJobCleanupText.Recoverable(5L * 1024 * 1024), "5 MB of downloaded update files can be deleted");
        Assert.AreEqual("There are no downloaded update files to delete.", UpdateJobCleanupText.Recoverable(0));
        Assert.AreEqual("Delete downloaded files", UpdateJobCleanupText.Heading, "the exact data-control label is kept");
    }

    [TestMethod]
    [Description("FR-UI-01/02: the update job is a contextual page under Updates and is not returned to with Back once left.")]
    public void UpdateJobPageSitsUnderUpdatesAndIsTransient()
    {
        Assert.AreEqual(ShellPage.Updates, ShellNavigation.ParentOf(ShellPage.UpdateJob));
        Assert.IsTrue(ShellNavigation.IsTransient(ShellPage.UpdateJob));
        ShellNavigation navigation = new();
        navigation.Navigate(ShellPage.Updates);
        navigation.Navigate(ShellPage.UpdateJob);
        Assert.AreEqual(ShellPage.Updates, navigation.SelectedItem);
        navigation.Navigate(ShellPage.Home);
        Assert.IsTrue(navigation.GoBack());
        Assert.AreEqual(ShellPage.Updates, navigation.Current, "Back skips the left update job page");
    }

    [TestMethod]
    [Description("UI polish phase 3 (approved target T-Updates-Ready; owner approved 2026-11-11): records of the same app are one row; only its own key can be chosen; anything that differs stays apart.")]
    public void RecordsOfTheSameAppAreOneRowAndOnlyItsOwnKeyCanBeChosen()
    {
        InventoryRow windows = new(Guid.NewGuid(), "7-Zip", "Igor Pavlov", "registry", "Machine", "23.01", "VerifiedUpdate", "Application", [], "registry:7zip");
        InventoryRow winget = new(Guid.NewGuid(), "7-Zip", "Igor Pavlov", "winget", "Machine", "23.01", "VerifiedUpdate", "Application", [], "winget:7zip");
        InventoryRow userCopy = new(Guid.NewGuid(), "7-Zip", "Igor Pavlov", "winget", "User", "23.01", "VerifiedUpdate", "Application", [], "user:7zip");
        InventoryRow otherPackage = new(Guid.NewGuid(), "7-Zip", "Igor Pavlov", "winget", "Machine", "23.01", "VerifiedUpdate", "Application", [], "fork:7zip");
        Dictionary<string, UpdateDetail> details = new(StringComparer.Ordinal)
        {
            ["registry:7zip"] = Detail("registry:7zip", "23.01", "24.08", packageId: "7zip.7zip"),
            ["winget:7zip"] = Detail("winget:7zip", "23.01", "24.08", packageId: "7zip.7zip"),
            ["user:7zip"] = Detail("user:7zip", "23.01", "24.08", packageId: "7zip.7zip"),
            ["fork:7zip"] = Detail("fork:7zip", "23.01", "24.08", packageId: "Fork.7zip")
        };

        UpdatesPagePresentation page = UpdatesPagePresenter.Build([windows, winget, userCopy, otherPackage], details, true, [], null, Now);

        Assert.HasCount(3, page.Ready, "only the Windows and WinGet records of the same machine-wide package become one row");
        UpdateRowPresentation sevenZip = page.Ready[0];
        Assert.AreEqual("registry:7zip", sevenZip.StableProductKey, "the first record keeps its place and its key");
        CollectionAssert.AreEqual(SevenZipRecordKeys, sevenZip.AllKeys.ToArray());
        Assert.AreEqual("Found twice (Windows and WinGet)", sevenZip.FoundText);
        Assert.AreEqual("Igor Pavlov · High confidence match · bigger changes are possible · Found twice (Windows and WinGet)", sevenZip.Summary, "23 to 24 is a major version");
        Assert.IsNull(page.Ready[1].FoundText, "a different scope is a different install");
        Assert.IsNull(page.Ready[2].FoundText, "a different package is a different app");
        CollectionAssert.AreEqual(ShownSevenZipKeys, page.ReadyKeys.ToArray(),
            "Review all chooses each shown row once, so a duplicate is never reviewed or updated twice");
        Assert.IsEmpty(UpdatesPagePresenter.Build([windows, winget], details, true, [], ["winget:7zip"], Now).SelectedKeys,
            "a record shown inside another row can't be chosen on its own");

        // An unknown scope can't bridge two records whose known scopes differ (independent review).
        InventoryRow unknownScope = windows with { Scope = "Unknown" };
        InventoryRow machineCopy = winget;
        InventoryRow userOnly = userCopy with { StableProductKey = "user2:7zip" };
        Dictionary<string, UpdateDetail> bridged = new(details, StringComparer.Ordinal) { ["user2:7zip"] = Detail("user2:7zip", "23.01", "24.08", packageId: "7zip.7zip") };
        UpdatesPagePresentation noBridge = UpdatesPagePresenter.Build([unknownScope, machineCopy, userOnly], bridged, true, [], null, Now);
        Assert.HasCount(2, noBridge.Ready, "the unknown-scope record joins one known install, never both");
        Assert.IsTrue(noBridge.Ready.All(row => row.AllKeys.Count <= 2));
        Assert.IsFalse(noBridge.Ready.Any(row => row.AllKeys.Contains("winget:7zip") && row.AllKeys.Contains("user2:7zip")), "Machine and User stay apart");

        // Ignored rows join only when their decisions read the same; Needs review rows only when their reasons match.
        StableDecision ignoreWindows = new(Guid.NewGuid(), "registry:7zip", "Ignore", "Indefinitely", Now.AddDays(-1));
        StableDecision ignoreWinget = new(Guid.NewGuid(), "winget:7zip", "Ignore", "Indefinitely", Now.AddDays(-1));
        UpdatesPagePresentation ignored = UpdatesPagePresenter.Build([windows with { Status = "Ignored" }, winget with { Status = "Ignored" }], details, true, [ignoreWindows, ignoreWinget], null, Now);
        UpdateRowPresentation ignoredRow = ignored.IgnoredAndSkipped.Single();
        Assert.AreEqual(ignoreWindows.Id, ignoredRow.DecisionId);
        Assert.AreEqual(ignoreWinget.Id, ignoredRow.AlsoFound.Single().DecisionId, "Stop ignoring reverses the decision for both records");
        UpdatesPagePresentation reviews = UpdatesPagePresenter.Build([windows with { Status = "ReviewRequired" }, winget with { Status = "ReviewRequired" }],
            new Dictionary<string, UpdateDetail>(StringComparer.Ordinal)
            {
                ["registry:7zip"] = Detail("registry:7zip", "23.01", "24.08", confidence: "Low"),
                ["winget:7zip"] = Detail("winget:7zip", "23.01", "24.08", reasons: ["ArchitectureConflict"])
            }, true, [], null, Now);
        Assert.HasCount(2, reviews.NeedsReview, "different reasons are never hidden inside one row");
        StringAssert.StartsWith(reviews.NeedsReview[0].Summary, "Installed 23.01 · WinGet offers 24.08 · ");
    }

    [TestMethod]
    [Description("UI polish phase 3 (approved targets T-Updates and T-Updates-Ready; FR-UI-05): the header counts rows and states freshness in words; with checks off it offers the existing source consent.")]
    public void UpdatesHeaderStatesCountsFreshnessAndOffersTurningOnChecks()
    {
        InventoryRow[] rows = [Row("ready", "VerifiedUpdate", "1.0"), Row("other", "VerifiedUpdate", "1.0"), Row("review", "ReviewRequired", "1.0")];
        Dictionary<string, UpdateDetail> details = new(StringComparer.Ordinal)
        {
            ["ready"] = Detail("ready", "1.0", "2.0"),
            ["other"] = Detail("other", "1.0", "1.1", packageId: "Contoso.Other"),
            ["review"] = Detail("review", "1.0", "2.0", confidence: "Low")
        };

        UpdatesPagePresentation off = UpdatesPagePresenter.Build(rows, details, false, [], null, Now);
        UpdatesPageHeader offHeader = UpdatesPagePresenter.Header(off, false, [], Now);
        Assert.IsNotNull(offHeader.Notice);
        Assert.AreEqual("Update checks are off.", offHeader.Notice.Title);
        Assert.IsTrue(offHeader.Notice.OffersTurnOnUpdateChecks, "the info bar offers the existing source consent");
        Assert.IsFalse(offHeader.ShowsCheckForUpdates, "nothing to check while sources are off");
        Assert.IsEmpty(off.Ready, "with checks off nothing is ready");

        UpdatesPagePresentation page = UpdatesPagePresenter.Build(rows, details, true, [], ["ready"], Now);
        SourceSnapshot live = new("winget", "1.9", Now.AddMinutes(-5), SourceSnapshotState.Live, [], []);
        UpdatesPageHeader header = UpdatesPagePresenter.Header(page, true, [live], Now, System.Globalization.CultureInfo.InvariantCulture, TimeZoneInfo.Utc);
        Assert.AreEqual("2 ready · 1 needs review · live results, checked 5 minutes ago", header.Description);
        Assert.IsNull(header.Notice, "live evidence needs no info bar");
        Assert.IsTrue(header.ShowsCheckForUpdates);
        Assert.AreEqual(UpdatesPagePresenter.ReviewFooter, header.Footer);
        Assert.AreEqual("1 of 2 ready updates selected", page.SelectionText);
        Assert.AreEqual("Review update for App ready", page.ReviewButtonLabel);
        Assert.AreEqual("Review all 2 updates", page.ReviewAllLabel);
        Assert.AreEqual("2 updates are ready. Select the ones to review, or review all of them.", UpdatesPagePresenter.Build(rows, details, true, [], null, Now).SelectionText);

        SourceSnapshot stale = new("winget", "1.9", Now.AddDays(-1), SourceSnapshotState.Cached, [], []);
        UpdatesPageHeader cached = UpdatesPagePresenter.Header(page, true, [stale], Now, System.Globalization.CultureInfo.InvariantCulture, TimeZoneInfo.Utc);
        StringAssert.EndsWith(cached.Description, "cached results, last checked yesterday");
        Assert.IsNotNull(cached.Notice, "anything less than live is called out");
        Assert.AreEqual(StatusTone.Attention, cached.Notice.Tone);
        Assert.IsFalse(cached.Notice.OffersTurnOnUpdateChecks);

        UpdatesPagePresentation empty = UpdatesPagePresenter.Build([], details, true, [], null, Now);
        UpdatesPageHeader none = UpdatesPagePresenter.Header(empty, true, [live], Now);
        Assert.AreEqual("No updates ready", none.EmptyTitle);
        Assert.AreEqual(UpdatesPagePresenter.EmptyFooter, none.Footer);
    }

    [TestMethod]
    [Description("UI polish phase 3 (approved targets T-Review and T-Verify): the review row shows every protected fact on one identity line; the titles name what is reviewed and what happened.")]
    public void ReviewAndVerifyTitlesAndTheIdentityLineKeepEveryProtectedFact()
    {
        UpdateJobSelection machine = Selection(UpdateScope.Machine, UpdateChannel.Stable);
        UpdateDetail detail = Detail("winget:contoso.widget", "1.0", "2.0", packageId: "Contoso.Widget");
        UpdateReviewSheet sheet = UpdateReviewSheet.Create(machine, "Contoso Widget", "Contoso Ltd", detail, RunningApplicationDetection.NotDetected,
            AdministratorRequirementPresenter.ForSelection(machine));
        UpdateReviewSheetItem item = sheet.Items.Single();
        Assert.AreEqual("Contoso.Widget · x64 · Stable · WinGet source, updated with WinGet", item.IdentityLine);
        Assert.AreEqual("All users", item.InstalledFor);
        Assert.AreEqual("1.0 → 2.0", item.Fact("Version"));
        Assert.AreEqual(string.Empty, item.Fact("Not a fact"));
        Assert.AreEqual("Review the update for Contoso Widget", sheet.PageTitle);
        Assert.AreEqual("Nothing changes until you select Update 1 app.", sheet.ConfirmNote);
        foreach (string text in new[] { sheet.PageTitle, UpdateReviewSheet.PageDescription, sheet.ConfirmNote })
            Assert.IsFalse(text.Contains("approved", StringComparison.OrdinalIgnoreCase), $"never 'approved' before approval: {text}");

        UpdateJobSelection user = Selection(UpdateScope.User, UpdateChannel.Stable);
        Assert.AreEqual("Current user", UpdateReviewSheet.Create(user, "Contoso Widget", "Contoso Ltd", detail, RunningApplicationDetection.NotDetected,
            AdministratorRequirementPresenter.ForSelection(user)).Items.Single().InstalledFor);

        UpdateJobCompletion verified = UpdateOutcomePresenter.ForResult(Result(true, machine, UpdateHistoryStatus.Success), machine, "Widget");
        Assert.AreEqual("Here's what happened", UpdateJobCompletion.PageTitle);
        Assert.AreEqual("Widget: update verified.", verified.Description);
        Assert.AreEqual("No outcome was recorded for this update.", new UpdateJobCompletion([]).Description);
        StringAssert.Contains(UpdateJobCompletion.OutcomesNote, "Verified only when it finds the new version");
        Assert.AreEqual("Delete downloaded files", UpdateJobCleanupText.Heading, "the cleanup button keeps its exact label under the Tidy up row");
    }

    private static string Reason(UpdatesPagePresentation page, string key) => page.NeedsReview.Single(row => row.StableProductKey == key).ReasonText;

    private static string Fact(UpdateReviewSheet sheet, string label) => sheet.Facts.Single(fact => fact.Label == label).Value;

    private static InventoryRow Row(string key, string status, string version) =>
        new(Guid.NewGuid(), $"App {key}", "Contoso", "winget", "User", version, status, "Application", [], key);

    private static UpdateDetail Detail(string key, string installed, string? candidate, string? packageId = "Contoso.App", string confidence = "High",
        string[]? reasons = null, string freshness = "winget: live result from today.", string explanation = "Exact match.") =>
        new(key, installed, candidate, candidate is null && packageId is null ? null : "winget", packageId, confidence, explanation, freshness, "X64", null,
            "Review the update.", reasons ?? [], [], []);

    private static UpdateJobSelection Selection(UpdateScope scope, UpdateChannel channel) =>
        new("winget:contoso.widget", UpdateProvider.WinGet, UpdateSource.WinGet, "Contoso.Widget", "1.0", "2.0", scope, UpdateArchitecture.X64, channel, "fixture-evidence");

    private static UpdateHistoryEntry Entry(UpdateJobSelection selection, UpdateHistoryStatus status) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), selection.StableProductKey, selection.Provider, selection.PackageId, selection.InstalledVersion,
            selection.CandidateVersion, Now, status, null, "diag");

    private static ReviewedUpdateExecutionResult Result(bool started, UpdateJobSelection selection, UpdateHistoryStatus status) =>
        new(started, $"Update result: {status}.", new UpdateBatchResult([Entry(selection, UpdateHistoryStatus.Queued), Entry(selection, UpdateHistoryStatus.Running), Entry(selection, status)]));
}
