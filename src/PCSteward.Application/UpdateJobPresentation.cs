using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>The three steps of the update job (ADR-030 decision 3, concept D used only for the job).</summary>
public enum UpdateJobStep
{
    Review,
    Update,
    Verify
}

/// <summary>Where a step sits relative to the current one.</summary>
public enum UpdateJobStepState
{
    Completed,
    Current,
    Upcoming
}

/// <summary>One entry in the step indicator.</summary>
public sealed record UpdateJobStepPresentation(UpdateJobStep Step, int Number, string Label, UpdateJobStepState State)
{
    /// <summary>For example "Step 2 of 3: Update, current step".</summary>
    public string AccessibleName => $"Step {Number} of 3: {Label}, {State switch { UpdateJobStepState.Completed => "done", UpdateJobStepState.Current => "current step", _ => "not started" }}";
}

/// <summary>FR-UPD-01, FR-UI-03 (Task 73 Phase 2): the step indicator. Presentation only; it never advances the job itself.</summary>
public static class UpdateJobSteps
{
    public static IReadOnlyList<UpdateJobStepPresentation> For(UpdateJobStep current) =>
    [
        Entry(UpdateJobStep.Review, "Review", current),
        Entry(UpdateJobStep.Update, "Update", current),
        Entry(UpdateJobStep.Verify, "Verify", current)
    ];

    private static UpdateJobStepPresentation Entry(UpdateJobStep step, string label, UpdateJobStep current) =>
        new(step, (int)step + 1, label, step < current ? UpdateJobStepState.Completed : step == current ? UpdateJobStepState.Current : UpdateJobStepState.Upcoming);
}

/// <summary>One labelled fact in the review sheet.</summary>
public sealed record ReviewFact(string Label, string Value);

/// <summary>The kinds of risk callout on the review sheet.</summary>
public enum ReviewRiskKind
{
    RunningApplication,
    Restart,
    Administrator
}

/// <summary>A risk callout. Tone is never the only signal: each has a title and a message.</summary>
public sealed record ReviewRiskCallout(ReviewRiskKind Kind, string Title, string Message, StatusTone Tone);

/// <summary>
/// FR-UI-03/04, SPEC §17.2, FR-UPD-04–06 (Task 73 Phase 2, ADR-030 foundation 4; audit P-H6, P-M3): the structured review sheet
/// that replaces the wall-of-text confirmation. It is built only from the reviewed immutable selection (facts), the inventory row
/// (name, publisher), the already computed update detail (confidence and its evidence) and the existing running-app and
/// administrator presentations. It holds no authority: confirming still goes through the existing review and confirmation flow.
/// </summary>
public sealed class UpdateReviewSheet
{
    /// <summary>The §17.2 fact labels, in display order.</summary>
    public static IReadOnlyList<string> FactLabels { get; } =
        ["App", "Version", "Publisher", "Source", "Package ID", "Scope", "Architecture", "Channel"];

    private UpdateReviewSheet(string appName, IReadOnlyList<ReviewFact> facts, string confidenceSummary, IReadOnlyList<string> confidenceEvidence,
        IReadOnlyList<ReviewRiskCallout> risks, string coverageDisclosure, string attestation, string primaryButtonLabel, string primaryAutomationName,
        AdministratorRequirementPresentation administratorRequirement, bool appearsToBeRunning, int itemCount)
    {
        AppName = appName; Facts = facts; ConfidenceSummary = confidenceSummary; ConfidenceEvidence = confidenceEvidence; Risks = risks;
        CoverageDisclosure = coverageDisclosure; Attestation = attestation; PrimaryButtonLabel = primaryButtonLabel;
        PrimaryAutomationName = primaryAutomationName; AdministratorRequirement = administratorRequirement;
        AppearsToBeRunning = appearsToBeRunning; ItemCount = itemCount;
    }

    /// <summary>"Review update" — never "approved" before approval (audit P-M3).</summary>
    public string Title => ItemCount == 1 ? "Review update" : "Review updates";
    public const string Intro = "Check these details. Nothing changes until you choose the update button below, and you can cancel now.";
    public string AppName { get; }
    public IReadOnlyList<ReviewFact> Facts { get; }
    public string ConfidenceSummary { get; }
    public IReadOnlyList<string> ConfidenceEvidence { get; }
    public IReadOnlyList<ReviewRiskCallout> Risks { get; }
    /// <summary>The existing running-app coverage disclosure; required content, shown de-emphasised.</summary>
    public string CoverageDisclosure { get; }
    /// <summary>The existing verification attestation and the "not successful until verified" note; shown de-emphasised.</summary>
    public string Attestation { get; }
    public string PrimaryButtonLabel { get; }
    public string PrimaryAutomationName { get; }
    public const string CancelButtonLabel = "Cancel";
    public AdministratorRequirementPresentation AdministratorRequirement { get; }
    /// <summary>The UAC shield appears on the primary button only when the existing presentation says administrator approval is required.</summary>
    public bool ShowsAdministratorShield => AdministratorRequirement.Status == AdministratorRequirementStatus.Required;
    public bool AppearsToBeRunning { get; }
    public int ItemCount { get; }

    public static UpdateReviewSheet Create(UpdateJobSelection selection, string appName, string publisher, UpdateDetail? detail,
        RunningApplicationDetection running, AdministratorRequirementPresentation administratorRequirement)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(running);
        ArgumentNullException.ThrowIfNull(administratorRequirement);
        string name = string.IsNullOrWhiteSpace(appName) ? selection.PackageId : appName.Trim();
        ReviewFact[] facts =
        [
            new("App", name),
            new("Version", $"{selection.InstalledVersion} → {selection.CandidateVersion}"),
            new("Publisher", string.IsNullOrWhiteSpace(publisher) ? "Not recorded" : publisher.Trim()),
            new("Source", Source(selection.Source)),
            new("Package ID", selection.PackageId),
            new("Scope", selection.Scope == UpdateScope.Machine ? "All users (machine-wide)" : "Current user"),
            new("Architecture", Architecture(selection.Architecture)),
            new("Channel", selection.Channel switch { UpdateChannel.Stable => "Stable", UpdateChannel.Preview => "Preview", _ => "Not stated by the source" })
        ];

        // Confidence was computed for this product but never shown (FR-UI-04, audit P-H4). It is shown only when the computed
        // detail describes this exact reviewed package and version; otherwise it is reported as unavailable, never guessed.
        bool detailMatches = detail is not null &&
            string.Equals(detail.PackageId, selection.PackageId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(detail.CandidateVersion, selection.CandidateVersion, StringComparison.OrdinalIgnoreCase);
        string confidence = detailMatches ? ReviewReasonText.Confidence(detail!.Confidence) : "Confidence not available for this exact update";
        List<string> evidence = [];
        if (detailMatches)
        {
            if (!string.IsNullOrWhiteSpace(detail!.Explanation)) evidence.Add(detail.Explanation.Trim());
            evidence.AddRange(ReviewReasonText.DescribeAll(detail.ReviewReasons));
            if (!string.IsNullOrWhiteSpace(detail.Freshness)) evidence.Add(detail.Freshness.Trim());
        }
        evidence.Add($"Evidence version: {selection.MappingEvidenceVersion}.");

        List<ReviewRiskCallout> risks = [];
        risks.Add(running.AppearsToBeRunning
            ? new(ReviewRiskKind.RunningApplication, $"{name} appears to be running",
                $"Detected process: {string.Join(", ", running.ProcessNames)}. PC Steward will not close it. Close the app before you continue, or choose to update anyway.",
                StatusTone.Attention)
            : new(ReviewRiskKind.RunningApplication, "The app doesn't appear to be running",
                $"No running process was matched for {selection.PackageId}. If you know it's open, close it first. PC Steward never closes apps.",
                StatusTone.Neutral));
        risks.Add(new(ReviewRiskKind.Restart, "A restart might be needed",
            "Some installers need Windows to restart to finish. PC Steward never restarts your PC; if a restart is needed, it tells you afterwards.",
            StatusTone.Neutral));
        risks.Add(new(ReviewRiskKind.Administrator, administratorRequirement.Label, administratorRequirement.Explanation,
            administratorRequirement.Status == AdministratorRequirementStatus.NotExpected ? StatusTone.Neutral : StatusTone.Attention));

        const int itemCount = 1;
        string primary = $"Update {itemCount} app{(itemCount == 1 ? string.Empty : "s")}{(running.AppearsToBeRunning ? " anyway" : string.Empty)}";
        string attestation =
            $"By choosing {primary}, you confirm that the exact source, package ID, installed version and target version shown here are the app and update you intend. " +
            "PC Steward doesn't record the update as successful until it checks the installed version again afterwards.";
        string automation = $"{primary}: {name} {selection.InstalledVersion} to {selection.CandidateVersion}. {administratorRequirement.Label}";
        return new(name, facts, confidence, evidence, risks, running.CoverageDisclosure, attestation, primary, automation,
            administratorRequirement, running.AppearsToBeRunning, itemCount);
    }

    private static string Source(UpdateSource source) => source switch
    {
        UpdateSource.WinGet => "WinGet",
        UpdateSource.MicrosoftStore => "Microsoft Store (through WinGet)",
        UpdateSource.ChocolateyCommunity => "Chocolatey Community",
        _ => "Unknown source"
    };

    private static string Architecture(UpdateArchitecture architecture) => architecture switch
    {
        UpdateArchitecture.X64 => "x64",
        UpdateArchitecture.X86 => "x86",
        UpdateArchitecture.Arm64 => "Arm64",
        UpdateArchitecture.Neutral => "Any (architecture-neutral)",
        _ => "Unknown"
    };
}

/// <summary>Per-item progress words for the Update step. Progress stays indeterminate (SPEC §17.4); there is no cancel.</summary>
public static class UpdateJobProgressText
{
    public const string LeaveNote = "You can leave this page; the update keeps going and its result is recorded. An update can't be cancelled once it has started.";

    /// <summary>A defensible count only: one reviewed app per job.</summary>
    public static string Heading(int itemCount) => itemCount == 1 ? "Updating 1 app" : $"Updating {itemCount} apps";

    public static string ItemState(UpdateProgressPresentation? presentation)
    {
        if (presentation is null) return "Waiting to start";
        if (presentation.IsUnavailable) return "Progress details aren't available. The update continues and its result will be shown here.";
        return presentation.Phase switch
        {
            UpdateProgressPhase.Preparing => "Checking the update evidence again",
            UpdateProgressPhase.ElevationRequested => "Waiting for Windows administrator permission (UAC)",
            UpdateProgressPhase.HandedToHelper => "Handed to PC Steward's protected update helper",
            UpdateProgressPhase.ProcessStarted => "Installing",
            UpdateProgressPhase.Verifying => "Checking the installed version",
            UpdateProgressPhase.Final => "Finishing",
            _ => "Waiting to start"
        };
    }
}

/// <summary>The outcome shown on the Verify step. The first four are the ADR-030 outcomes; <see cref="NotStarted"/> is not a failure.</summary>
public enum UpdateOutcomeKind
{
    Verified,
    RestartRequired,
    InstalledNotVerified,
    Failed,
    NotStarted
}

/// <summary>One per-item outcome on the Verify step.</summary>
public sealed record UpdateOutcomePresentation(
    UpdateOutcomeKind Kind,
    string Label,
    string Glyph,
    StatusTone Tone,
    string Summary,
    string NextStep,
    string TechnicalDetail,
    bool ReturnsToReview = false)
{
    public string AccessibleName(string subject) => $"{subject}: {Label}";
}

/// <summary>One app's line on the Verify step.</summary>
public sealed record UpdateJobItemOutcome(string AppName, string PackageId, string InstalledVersion, string TargetVersion, UpdateOutcomePresentation Outcome);

/// <summary>The completion view: per-item outcomes plus restart guidance.</summary>
public sealed record UpdateJobCompletion(IReadOnlyList<UpdateJobItemOutcome> Items)
{
    public const string NeverRestartsNote = "PC Steward never restarts your PC.";

    public bool ReturnsToReview => Items.Count != 0 && Items.All(item => item.Outcome.ReturnsToReview);
    public bool RestartRequired => Items.Any(item => item.Outcome.Kind == UpdateOutcomeKind.RestartRequired);

    public string RestartGuidance => RestartRequired
        ? $"Restart Windows when it suits you to finish the update. {NeverRestartsNote} The new version is checked again on your next scan after the restart."
        : $"{NeverRestartsNote} No restart was reported for this job.";

    public string Headline => Items.Count == 0 ? "No outcome recorded" : Items[0].Outcome.Kind switch
    {
        UpdateOutcomeKind.Verified => "Update verified",
        UpdateOutcomeKind.RestartRequired => "Restart required to finish",
        UpdateOutcomeKind.InstalledNotVerified => "Installed, not verified",
        UpdateOutcomeKind.Failed => "Update failed",
        _ => "Update not started"
    };
}

/// <summary>
/// FR-UPD-07–09, FR-HIS-03, UJ-04 step 8 (Task 73 Phase 2, ADR-030 foundation 3; audit P-H3): maps every recorded outcome to one
/// of four honest labels — Verified, Restart required, Installed, not verified and Failed — or to "Not started" when nothing ran.
/// Only <see cref="UpdateHistoryStatus.Success"/> (post-update inventory found the target version) reads as Verified, and only
/// <see cref="UpdateHistoryStatus.RestartRequired"/> (a reliable restart-pending result) reads as Restart required. Presentation
/// only: it reads recorded results and never changes them.
/// </summary>
public static class UpdateOutcomePresenter
{
    public const string VerifiedLabel = "Verified";
    public const string RestartRequiredLabel = "Restart required";
    public const string InstalledNotVerifiedLabel = "Installed, not verified";
    public const string FailedLabel = "Failed";
    public const string NotStartedLabel = "Not started";

    /// <summary>The diagnostic recorded when the machine-wide update helper is missing (existing special case).</summary>
    public const string HelperMissingDiagnostic = "elevated-helper-fixed-installed-path-invalid";

    private const string CheckAgain = "Scan again to check the installed version. PC Steward hasn't recorded this update as successful.";
    private const string TryAgain = "Check the app in Installed apps, then review the update again from Updates.";

    public static UpdateOutcomePresentation ForStatus(UpdateHistoryStatus status, string? diagnostic, int? exitCode, string targetVersion)
    {
        string technical = Technical(status, diagnostic, exitCode);
        return status switch
        {
            UpdateHistoryStatus.Success => new(UpdateOutcomeKind.Verified, VerifiedLabel, "", StatusTone.Success,
                $"PC Steward checked installed apps again after the update and found version {targetVersion}.", "Nothing else to do.", technical),
            UpdateHistoryStatus.RestartRequired => new(UpdateOutcomeKind.RestartRequired, RestartRequiredLabel, "", StatusTone.Information,
                "The installer reported that Windows needs to restart to finish this update.",
                "Restart when it suits you. PC Steward never restarts your PC.", technical),
            UpdateHistoryStatus.Inconclusive => NotVerified($"The installer finished, but PC Steward didn't find version {targetVersion} when it checked again.", technical),
            UpdateHistoryStatus.VerificationFailed => NotVerified("The installer finished, but PC Steward couldn't complete its check of installed apps.", technical),
            UpdateHistoryStatus.VerificationRequired or UpdateHistoryStatus.Running => NotVerified(
                "The update may have run, but it was interrupted before PC Steward could check the installed version.", technical),
            UpdateHistoryStatus.ElevationDenied => new(UpdateOutcomeKind.NotStarted, NotStartedLabel, "", StatusTone.Neutral,
                "Not started — you cancelled the Windows permission prompt, so nothing was changed.",
                "Your review is still here. Start a new review when you want to try again.", technical, ReturnsToReview: true),
            UpdateHistoryStatus.Cancelled => new(UpdateOutcomeKind.NotStarted, NotStartedLabel, "", StatusTone.Neutral,
                "Not started — the update stopped before the installer ran, so nothing was changed.", TryAgain, technical),
            UpdateHistoryStatus.RunningApplicationWarning => new(UpdateOutcomeKind.NotStarted, NotStartedLabel, "", StatusTone.Neutral,
                "Not started — the app was running, so PC Steward stopped before the installer. PC Steward didn't close it.", TryAgain, technical),
            UpdateHistoryStatus.Queued => new(UpdateOutcomeKind.NotStarted, NotStartedLabel, "", StatusTone.Neutral,
                "Not started — nothing was changed.", TryAgain, technical),
            UpdateHistoryStatus.SourceFailed => Failed("The package source couldn't provide the update.", technical),
            UpdateHistoryStatus.DownloadFailed => Failed("The update couldn't be downloaded.", technical),
            UpdateHistoryStatus.InstallerFailed => Failed(exitCode is { } code ? $"The installer reported an error (exit code {code})." : "The installer reported an error.", technical),
            // A timeout ends the attempt before any verification runs, so the installed version is unknown: it is a failure
            // class (FR-HIS-03), never "installed". The copy says it may have partly run.
            UpdateHistoryStatus.TimedOut => Failed("The update took too long and was stopped before PC Steward could check it. It may have partly run.", technical),
            UpdateHistoryStatus.ProductFailed when string.Equals(diagnostic, HelperMissingDiagnostic, StringComparison.Ordinal) =>
                Failed(ElevationStory.HelperMissingOutcome, technical),
            UpdateHistoryStatus.ProductFailed => Failed("PC Steward couldn't run the update.", technical),
            _ => NotVerified("PC Steward couldn't read the recorded outcome.", technical)
        };
    }

    /// <summary>The completion for one reviewed job, from the existing workflow result. The recorded batch is authoritative.</summary>
    public static UpdateJobCompletion ForResult(ReviewedUpdateExecutionResult result, UpdateJobSelection selection, string appName)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(selection);
        string name = string.IsNullOrWhiteSpace(appName) ? selection.PackageId : appName;
        UpdateJobItemOutcome Item(UpdateOutcomePresentation outcome) =>
            new(name, selection.PackageId, selection.InstalledVersion, selection.CandidateVersion, outcome);

        if (result.Batch is { AdmissionRefused: true })
            return new([Item(new(UpdateOutcomeKind.NotStarted, NotStartedLabel, "", StatusTone.Neutral,
                "Not started — another update is still running or its work isn't finished, so nothing was changed.", TryAgain,
                "not-started-update-execution-busy-or-owned-work-unresolved"))]);
        UpdateHistoryEntry[] finals = result.Batch?.FinalEntries.ToArray() ?? [];
        if (finals.Length != 0)
            return new(finals.Select(entry => Item(ForStatus(entry.Status, entry.Diagnostic, entry.ExitCode, entry.TargetVersion))).ToArray());
        if (!result.Started)
            return new([Item(new(UpdateOutcomeKind.NotStarted, NotStartedLabel, "", StatusTone.Neutral,
                $"Not started — {result.Message}", TryAgain, "not-started"))]);
        // Started but nothing was recorded: execution or verification may have been interrupted. Never Verified.
        return new([Item(NotVerified(
            "PC Steward didn't record an outcome for this update. It may or may not have run, and the installed version wasn't checked.",
            "no-recorded-outcome"))]);
    }

    private static UpdateOutcomePresentation NotVerified(string summary, string technical) =>
        new(UpdateOutcomeKind.InstalledNotVerified, InstalledNotVerifiedLabel, "", StatusTone.Attention, summary, CheckAgain, technical);

    private static UpdateOutcomePresentation Failed(string summary, string technical) =>
        new(UpdateOutcomeKind.Failed, FailedLabel, "", StatusTone.Critical, summary, TryAgain, technical);

    private static string Technical(UpdateHistoryStatus status, string? diagnostic, int? exitCode) =>
        $"Recorded status: {status}. Exit code: {(exitCode is { } code ? code.ToString(System.Globalization.CultureInfo.InvariantCulture) : "none")}. Diagnostic: {(string.IsNullOrWhiteSpace(diagnostic) ? "none" : diagnostic)}.";
}
