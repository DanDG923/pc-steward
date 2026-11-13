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

/// <summary>One app's input to the review sheet: its reviewed immutable selection plus display-only inventory and evidence.</summary>
/// <summary>
/// One app for a review sheet. <see cref="UserInstaller"/> (D7 design §3) is the vendor installer a per-user review bound, or null.
/// </summary>
public sealed record UpdateReviewSheetInput(UpdateJobSelection Selection, string AppName, string Publisher, UpdateDetail? Detail, RunningApplicationDetection Running,
    WinGetUserInstaller? UserInstaller = null);

/// <summary>One app on the review sheet: the §17.2 facts, the confidence in words and this app's own risks.</summary>
public sealed record UpdateReviewSheetItem(
    string StableProductKey,
    string AppName,
    IReadOnlyList<ReviewFact> Facts,
    string ConfidenceSummary,
    IReadOnlyList<string> ConfidenceEvidence,
    AdministratorRequirementPresentation AdministratorRequirement,
    bool AppearsToBeRunning,
    IReadOnlyList<string> RunningProcessNames)
{
    /// <summary>The value of one of this app's §17.2 facts (<see cref="UpdateReviewSheet.FactLabels"/>), or empty.</summary>
    public string Fact(string label) => Facts.FirstOrDefault(fact => string.Equals(fact.Label, label, StringComparison.Ordinal))?.Value ?? string.Empty;

    /// <summary>
    /// UI polish phase 3 (approved target T-Review): the "What will change" row's identity line under the app name — the exact package
    /// ID, architecture and channel, then the source and the provider that runs the update — so the row shows every protected fact.
    /// </summary>
    public string IdentityLine => $"{Fact("Package ID")} · {Fact("Architecture")} · {Fact("Channel")} · {Fact("Source")} source, updated with {Fact("Updated with")}";

    /// <summary>The scope in the short words of the "Installed for" column ("All users" or "Current user").</summary>
    public string InstalledFor => Fact("Scope").StartsWith("All users", StringComparison.Ordinal) ? "All users" : Fact("Scope");
}

/// <summary>A selected app that isn't in the frozen set, with the reason in words (SPEC §9.13: excluded rows are explained).</summary>
public sealed record UpdateReviewExclusion(string AppName, string Reason);

/// <summary>
/// FR-UI-03/04, SPEC §17.2, FR-UPD-04–06 (Task 73 Phase 2, ADR-030 foundation 4; audit P-H6, P-M3): the structured review sheet
/// that replaces the wall-of-text confirmation. It is built only from the reviewed immutable selection (facts), the inventory row
/// (name, publisher), the already computed update detail (confidence and its evidence) and the existing running-app and
/// administrator presentations. It holds no authority: confirming still goes through the existing review and confirmation flow.
/// Task 73 Phase 6 (SPEC §9.13 G4): the same sheet reviews a frozen set of apps — every app's facts, the excluded apps with their
/// reasons, and aggregated risks. A one-app sheet is exactly the Phase 2 sheet.
/// </summary>
public sealed class UpdateReviewSheet
{
    /// <summary>The §17.2 fact labels plus the provider (a protected UI behaviour), in display order.</summary>
    public static IReadOnlyList<string> FactLabels { get; } =
        ["App", "Version", "Publisher", "Source", "Updated with", "Package ID", "Scope", "Architecture", "Channel"];

    /// <summary>
    /// Shown when more than one app needs administrator permission: the one-shot helper accepts one approved job per prompt, so the
    /// batch is approved and elevated one app at a time (Task 73 Phase 6 design note).
    /// </summary>
    public const string PerAppElevationNote =
        "Windows may ask for permission once for each app that needs it, when that app's update starts. If you decline, only that app is skipped.";

    private UpdateReviewSheet(IReadOnlyList<UpdateReviewSheetItem> items, IReadOnlyList<UpdateReviewExclusion> excluded, IReadOnlyList<ReviewRiskCallout> risks,
        string coverageDisclosure, string attestation, string primaryButtonLabel, string primaryAutomationName,
        AdministratorRequirementPresentation administratorRequirement, bool appearsToBeRunning)
    {
        Items = items; Excluded = excluded; Risks = risks; CoverageDisclosure = coverageDisclosure; Attestation = attestation;
        PrimaryButtonLabel = primaryButtonLabel; PrimaryAutomationName = primaryAutomationName; AdministratorRequirement = administratorRequirement;
        AppearsToBeRunning = appearsToBeRunning;
    }

    /// <summary>"Review update" — never "approved" before approval (audit P-M3).</summary>
    public string Title => ItemCount == 1 ? "Review update" : "Review updates";
    public const string Intro = "Check these details. Nothing changes until you choose the update button below, and you can cancel now.";

    /// <summary>UI polish phase 3 (approved target T-Review): the page title names what is reviewed, for example "Review 2 updates".</summary>
    public string PageTitle => ItemCount == 1 ? $"Review the update for {AppName}" : $"Review {ItemCount.ToString(System.Globalization.CultureInfo.CurrentCulture)} updates";

    /// <summary>The line under the review title (approved target T-Review). The frozen plan is the existing immutable reviewed selection.</summary>
    public const string PageDescription = "Check what will change, then choose whether to go ahead. This plan is fixed: if anything changes, you'll review it again.";

    /// <summary>The note beside the review's buttons: nothing changes until the specific update button is chosen.</summary>
    public string ConfirmNote => $"Nothing changes until you select {PrimaryButtonLabel}.";
    /// <summary>Every app in the frozen set, in the order it will run.</summary>
    public IReadOnlyList<UpdateReviewSheetItem> Items { get; }
    /// <summary>Selected apps that couldn't be reviewed and aren't included, each with its reason.</summary>
    public IReadOnlyList<UpdateReviewExclusion> Excluded { get; }
    /// <summary>The app's name, or "N apps" for a batch.</summary>
    public string AppName => ItemCount == 1 ? Items[0].AppName : $"{ItemCount} apps";
    /// <summary>The single app's facts (a one-app sheet), or empty for a batch: a batch shows each item's facts.</summary>
    public IReadOnlyList<ReviewFact> Facts => ItemCount == 1 ? Items[0].Facts : [];
    public string ConfidenceSummary => ItemCount == 1 ? Items[0].ConfidenceSummary : string.Empty;

    /// <summary>
    /// UI review 2026-11-13 (item 13): how sure the matches are, in the words every app on the sheet shares, or null when the
    /// apps differ. A shared answer is said once above the list instead of in a column that reads the same on every row.
    /// </summary>
    public string? SharedConfidence => Items.Select(item => item.ConfidenceSummary).Distinct(StringComparer.Ordinal).Take(2).ToArray() is [var only] ? only : null;

    /// <summary>The line above the list for <see cref="SharedConfidence"/>, or null when each row shows its own.</summary>
    public string? SharedConfidenceLine => SharedConfidence is not { } shared ? null : ItemCount == 1 ? $"How sure: {shared}." : $"How sure: {shared}, for every app.";
    public IReadOnlyList<string> ConfidenceEvidence => ItemCount == 1 ? Items[0].ConfidenceEvidence : [];
    public IReadOnlyList<ReviewRiskCallout> Risks { get; }
    /// <summary>The existing running-app coverage disclosure; required content, shown de-emphasised.</summary>
    public string CoverageDisclosure { get; }
    /// <summary>The existing verification attestation and the "not successful until verified" note; shown de-emphasised.</summary>
    public string Attestation { get; }
    public string PrimaryButtonLabel { get; }
    public string PrimaryAutomationName { get; }
    public const string CancelButtonLabel = "Cancel";
    public AdministratorRequirementPresentation AdministratorRequirement { get; }
    /// <summary>The UAC shield appears on the primary button only when at least one app needs administrator approval.</summary>
    public bool ShowsAdministratorShield => AdministratorRequirement.Status == AdministratorRequirementStatus.Required;
    /// <summary>How many apps in the frozen set need administrator approval (machine scope).</summary>
    public int AdministratorRequiredCount => Items.Count(item => item.AdministratorRequirement.Status == AdministratorRequirementStatus.Required);
    /// <summary>The per-app elevation note, shown only for a batch with an app that needs administrator approval.</summary>
    public string? ElevationNote => ItemCount > 1 && AdministratorRequiredCount > 0 ? PerAppElevationNote : null;
    public bool AppearsToBeRunning { get; }
    public int ItemCount => Items.Count;

    /// <summary>Review L2: when this review stops being startable (the 30-minute dispatch ceiling), in the time shown to the user.</summary>
    public DateTimeOffset? ReviewExpiresAt { get; private set; }

    /// <summary>The review's expiry in words, shown on the sheet; null when no expiry was supplied.</summary>
    public string? ExpiryNote => ReviewExpiresAt is { } expiresAt ? ExpiryText(expiresAt, ItemCount) : null;

    public static string ExpiryText(DateTimeOffset expiresAt, int itemCount)
    {
        string time = expiresAt.ToString("t", System.Globalization.CultureInfo.CurrentCulture);
        return itemCount == 1
            ? $"This review is valid until {time}. If the update hasn't started by then, review again to update."
            : $"This review is valid until {time}. An app that hasn't started by then won't start; review it again.";
    }

    public static UpdateReviewSheet Create(UpdateJobSelection selection, string appName, string publisher, UpdateDetail? detail,
        RunningApplicationDetection running, AdministratorRequirementPresentation administratorRequirement, WinGetUserInstaller? userInstaller = null)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(running);
        ArgumentNullException.ThrowIfNull(administratorRequirement);
        UpdateReviewSheetItem item = Item(new(selection, appName, publisher, detail, running, userInstaller), administratorRequirement);
        string name = item.AppName;

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
        return new([item], [], risks, running.CoverageDisclosure, attestation, primary, automation, administratorRequirement, running.AppearsToBeRunning);
    }

    /// <summary>
    /// SPEC §9.13 G4 (Task 73 Phase 6): one sheet for a frozen set. One app gives exactly <see cref="Create"/>. For several apps every
    /// app keeps its own §17.2 facts and confidence; risks are aggregated (running apps named, restart, administrator count) and
    /// excluded apps are listed with their reasons. The commit button names the count and shows the shield only if an app needs it.
    /// </summary>
    public static UpdateReviewSheet CreateBatch(IReadOnlyList<UpdateReviewSheetInput> inputs, IReadOnlyList<UpdateReviewExclusion>? excluded = null,
        DateTimeOffset? reviewExpiresAt = null)
    {
        UpdateReviewSheet sheet = BuildBatch(inputs, excluded);
        sheet.ReviewExpiresAt = reviewExpiresAt;
        return sheet;
    }

    private static UpdateReviewSheet BuildBatch(IReadOnlyList<UpdateReviewSheetInput> inputs, IReadOnlyList<UpdateReviewExclusion>? excluded)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0) throw new ArgumentException("A review sheet needs at least one app.", nameof(inputs));
        if (inputs.Any(input => input is null || input.Selection is null || input.Running is null)) throw new ArgumentException("Every app needs its reviewed selection and running-app detection.", nameof(inputs));
        IReadOnlyList<UpdateReviewExclusion> exclusions = excluded ?? [];
        if (inputs.Count == 1)
        {
            UpdateReviewSheetInput only = inputs[0];
            UpdateReviewSheet single = Create(only.Selection, only.AppName, only.Publisher, only.Detail, only.Running,
                AdministratorRequirementPresenter.ForSelection(only.Selection, only.UserInstaller), only.UserInstaller);
            return exclusions.Count == 0 ? single : new(single.Items, exclusions, single.Risks, single.CoverageDisclosure, single.Attestation,
                single.PrimaryButtonLabel, single.PrimaryAutomationName, single.AdministratorRequirement, single.AppearsToBeRunning);
        }

        UpdateReviewSheetItem[] items = [.. inputs.Select(input => Item(input, AdministratorRequirementPresenter.ForSelection(input.Selection, input.UserInstaller)))];
        AdministratorRequirementPresentation administrator = AdministratorRequirementPresenter.ForBatch(inputs.Select(input => input.Selection).ToArray(),
            inputs.Select(input => input.UserInstaller).ToArray());
        UpdateReviewSheetItem[] running = [.. items.Where(item => item.AppearsToBeRunning)];
        List<ReviewRiskCallout> risks = [];
        risks.Add(running.Length == 0
            ? new(ReviewRiskKind.RunningApplication, "None of these apps appear to be running",
                "No running process was matched for these apps. If you know one is open, close it first. PC Steward never closes apps.", StatusTone.Neutral)
            : new(ReviewRiskKind.RunningApplication, running.Length == 1 ? $"{running[0].AppName} appears to be running" : $"{running.Length} apps appear to be running",
                $"Detected: {string.Join("; ", running.Select(item => $"{item.AppName} ({string.Join(", ", item.RunningProcessNames)})"))}. " +
                "PC Steward will not close them. Close them before you continue, or choose to update anyway. An app that starts running later is not updated without a new review.",
                StatusTone.Attention));
        risks.Add(new(ReviewRiskKind.Restart, "A restart might be needed",
            "Some installers need Windows to restart to finish. PC Steward never restarts your PC; if a restart is needed, it tells you afterwards.",
            StatusTone.Neutral));
        risks.Add(new(ReviewRiskKind.Administrator, administrator.Label, administrator.Explanation,
            administrator.Status == AdministratorRequirementStatus.NotExpected ? StatusTone.Neutral : StatusTone.Attention));

        string primary = $"Update {items.Length} apps{(running.Length > 0 ? " anyway" : string.Empty)}";
        string attestation =
            $"By choosing {primary}, you confirm that the exact source, package ID, installed version and target version shown for each app are the apps and updates you intend. " +
            "PC Steward updates them one at a time, checks each app's details again just before it starts, and doesn't start an app whose details changed. " +
            "It doesn't record an update as successful until it checks the installed version again afterwards.";
        string automation = $"{primary}: {string.Join(", ", items.Select(item => item.AppName))}. {administrator.Label}";
        string coverage = string.Join(" ", inputs.Select(input => input.Running.CoverageDisclosure).Distinct(StringComparer.Ordinal));
        return new(items, exclusions, risks, coverage, attestation, primary, automation, administrator, running.Length > 0);
    }

    private static UpdateReviewSheetItem Item(UpdateReviewSheetInput input, AdministratorRequirementPresentation administratorRequirement)
    {
        UpdateJobSelection selection = input.Selection;
        UpdateDetail? detail = input.Detail;
        string name = string.IsNullOrWhiteSpace(input.AppName) ? selection.PackageId : input.AppName.Trim();
        ReviewFact[] facts =
        [
            new("App", name),
            new("Version", $"{selection.InstalledVersion} → {selection.CandidateVersion}"),
            new("Publisher", string.IsNullOrWhiteSpace(input.Publisher) ? "Not recorded" : input.Publisher.Trim()),
            new("Source", Source(selection.Source)),
            // Protected UI behaviour: the final confirmation names the exact provider that runs the update.
            new("Updated with", selection.Provider == UpdateProvider.Chocolatey ? "Chocolatey" : "WinGet"),
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
        // D7 design §3 item 1: the vendor installer the review bound, in words.
        if (input.UserInstaller is { } installer) evidence.Add($"Installer: {WingetUserInstallerText.TypeInWords(installer.Type)}.");
        evidence.Add($"Evidence version: {selection.MappingEvidenceVersion}.");
        return new(selection.StableProductKey, name, facts, confidence, evidence, administratorRequirement, input.Running.AppearsToBeRunning,
            [.. input.Running.ProcessNames]);
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

/// <summary>
/// FR-DAT-09, UJ-04 step 9 (Task 73 Phase 2; audit P-H4): words for the completion view's "Delete downloaded files" card. It
/// describes the existing download-cache summary only; deletion still goes through the existing confirmation and cleanup.
/// </summary>
public static class UpdateJobCleanupText
{
    public const string Heading = "Delete downloaded files";

    /// <summary>UI polish phase 3 (approved target T-Verify): the cleanup row's title. The button keeps the exact label <see cref="Heading"/>.</summary>
    public const string TidyUpTitle = "Tidy up";

    /// <summary>What deleting the downloaded files keeps, shown after the recoverable size.</summary>
    public const string KeptNote = "Your scan data and Activity are kept.";

    /// <summary>A human-readable size (audit P-M6: no raw byte counts).</summary>
    public static string Size(long bytes) => DisplayFormat.Size(bytes);

    public static string Recoverable(long recoverableBytes) => recoverableBytes > 0
        ? $"{Size(recoverableBytes)} of downloaded update files can be deleted. This doesn't affect installed apps or the update you just ran."
        : "There are no downloaded update files to delete.";
}

/// <summary>Per-item progress words for the Update step. Progress stays indeterminate (SPEC §17.4); there is no cancel.</summary>
public static class UpdateJobProgressText
{
    public const string LeaveNote = "You can leave this page; the update keeps going and its result is recorded. An update can't be cancelled once it has started.";

    /// <summary>A defensible count only: the apps in the reviewed frozen set.</summary>
    public static string Heading(int itemCount) => itemCount == 1 ? "Updating 1 app" : $"Updating {itemCount} apps, one at a time";

    /// <summary>FR-UPD-11, AC-UPD-10 (Task 73 Phase 6): the batch stop control. It stops before the next app; it never interrupts one.</summary>
    public const string StopAfterCurrentLabel = "Stop after this app";
    public const string StopAfterCurrentHelp = "The app that is updating now finishes and its result is recorded. The apps after it don't start.";
    public const string StopRequestedText = "Stopping after this app. The apps after it won't start.";

    /// <summary>The state of one batch item that isn't the one updating now.</summary>
    public static string QueuedItemState(int index, int currentIndex, string? finishedLabel) =>
        finishedLabel is not null ? finishedLabel : index < currentIndex ? "Finished" : "Waiting to start";

    /// <summary>Review L1: shown on the next app while the batch waits for the previous app's owned work to settle.</summary>
    public const string WaitingForPreviousApp = "Waiting for the previous app to finish…";

    /// <summary>
    /// Review L1 (Task 73 Phase 6): how one line of the Update step is shown. A finished app always shows its recorded outcome label,
    /// never a progress bar or "Waiting to start", even while it is still the current app. While the batch waits for a finished app's
    /// owned work to settle, the next app (unless a stop was requested) carries the live state "Waiting for the previous app to finish…".
    /// </summary>
    public static UpdateJobItemLine Line(int index, int currentIndex, IReadOnlyList<string?> finishedLabels, bool stopRequested)
    {
        ArgumentNullException.ThrowIfNull(finishedLabels);
        if (index < 0 || index >= finishedLabels.Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (finishedLabels[index] is { } finished) return new(false, finished);
        if (finishedLabels.Count == 1 || index == currentIndex) return new(true, null);
        bool currentFinished = currentIndex >= 0 && currentIndex < finishedLabels.Count && finishedLabels[currentIndex] is not null;
        if (currentFinished && index == currentIndex + 1 && !stopRequested) return new(true, WaitingForPreviousApp);
        return new(false, QueuedItemState(index, currentIndex, null));
    }

    /// <summary>The live state text for a live line: the waiting text, or the polled progress of the app updating now.</summary>
    public static string LiveState(UpdateJobItemLine line, UpdateProgressPresentation? presentation)
    {
        ArgumentNullException.ThrowIfNull(line);
        return line.Text ?? ItemState(presentation);
    }

    /// <summary>Review M2: the band while a review is prepared — "Preparing review… 2 of 5" for several apps, the plain label for one.</summary>
    public static string PreparingReview(string label, UpdateBatchReviewProgress? progress) =>
        progress is { Total: > 1 } known ? $"{label} {known.Prepared} of {known.Total}" : label;

    /// <summary>Info (review): the operation band names the app updating now in a batch, so a UAC prompt has context.</summary>
    public static string BatchSubject(string appName, int index, int itemCount) => $"Updating {appName} ({index + 1} of {itemCount})";

    /// <summary>The band text: the batch subject (if any) followed by the polled progress label.</summary>
    public static string BandLabel(string? subject, string progressLabel) =>
        string.IsNullOrWhiteSpace(subject) ? progressLabel : $"{subject} · {progressLabel}";

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

/// <summary>
/// One line of the Update step. <see cref="IsLive"/> lines carry the polite live region and an indeterminate progress bar; their
/// <see cref="Text"/> is null when they follow the polled progress. Other lines show <see cref="Text"/> only.
/// </summary>
public sealed record UpdateJobItemLine(bool IsLive, string? Text);

/// <summary>
/// The outcome shown on the Verify step. The first four are the ADR-030 outcomes; <see cref="NotStarted"/> is not a failure.
/// <see cref="ResultNotVerified"/> is the honest reading when nothing shows that the update was installed and nothing checked it
/// (independent review: "Installed, not verified" would overstate what is known).
/// </summary>
public enum UpdateOutcomeKind
{
    Verified,
    RestartRequired,
    InstalledNotVerified,
    Failed,
    NotStarted,
    ResultNotVerified
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

/// <summary>One app's line on the Verify step. <see cref="StableProductKey"/> lets "Review again" open a fresh review of that app only.</summary>
public sealed record UpdateJobItemOutcome(string AppName, string PackageId, string InstalledVersion, string TargetVersion, UpdateOutcomePresentation Outcome,
    string? StableProductKey = null);

/// <summary>One outcome group on the Verify step, for example "Verified (2)".</summary>
public sealed record UpdateOutcomeGroup(UpdateOutcomeKind Kind, string Label, IReadOnlyList<UpdateJobItemOutcome> Items)
{
    public string Heading => $"{Label} ({Items.Count})";
}

/// <summary>The completion view: per-item outcomes plus restart guidance.</summary>
public sealed record UpdateJobCompletion(IReadOnlyList<UpdateJobItemOutcome> Items)
{
    public const string NeverRestartsNote = "PC Steward never restarts your PC.";

    /// <summary>UI polish phase 3 (approved target T-Verify): the Verify step's page title.</summary>
    public const string PageTitle = "Here's what happened";

    /// <summary>The Verify step's footer: the outcomes PC Steward uses, and that Verified needs the new version to be found.</summary>
    public const string OutcomesNote =
        "Outcomes PC Steward uses: Verified, Restart required, Installed, not verified, and Failed. It says Verified only when it finds the new version.";

    /// <summary>The line under the Verify title: how many apps, then the outcome counts in words (the recorded results, never upgraded).</summary>
    public string Description => Items.Count switch
    {
        0 => "No outcome was recorded for this update.",
        1 => $"{Items[0].AppName}: {Headline.ToLowerInvariant()}.",
        _ => $"{DisplayFormat.Plural(Items.Count, "app")}: {Headline}."
    };

    public bool ReturnsToReview => Items.Count != 0 && Items.All(item => item.Outcome.ReturnsToReview);
    public bool RestartRequired => Items.Any(item => item.Outcome.Kind == UpdateOutcomeKind.RestartRequired);

    public string RestartGuidance => RestartRequired
        ? $"Restart Windows when it suits you to finish the update. {NeverRestartsNote} The new version is checked again on your next scan after the restart."
        : $"{NeverRestartsNote} No restart was reported for this job.";

    /// <summary>
    /// SPEC §9.13 G4 (Task 73 Phase 6): the outcomes grouped by kind, in the order Verified, Restart required, Installed, not verified,
    /// Failed, Not started, Result not verified. Each item keeps its own recorded outcome; grouping never merges or upgrades one.
    /// </summary>
    public IReadOnlyList<UpdateOutcomeGroup> Groups => [.. Items.GroupBy(item => item.Outcome.Kind).OrderBy(group => group.Key)
        .Select(group => new UpdateOutcomeGroup(group.Key, group.First().Outcome.Label, [.. group]))];

    /// <summary>
    /// AC-UPD-09: the order the Verify step shows the items in. A batch follows <see cref="Groups"/> (each group's items together,
    /// in batch order within the group), so every item appears under its own outcome heading; a single item is unchanged.
    /// </summary>
    public IReadOnlyList<UpdateJobItemOutcome> DisplayItems => Items.Count > 1 ? [.. Groups.SelectMany(group => group.Items)] : Items;

    public string Headline => Items.Count == 0 ? "No outcome recorded"
        // A batch headline counts each outcome group, for example "2 verified · 1 not started"; the subject ("3 apps") is shown beside it.
        : Items.Count > 1 ? string.Join(" · ", Groups.Select(group => $"{group.Items.Count} {group.Label.ToLowerInvariant()}"))
        : Items[0].Outcome.Kind switch
    {
        UpdateOutcomeKind.Verified => "Update verified",
        UpdateOutcomeKind.RestartRequired => "Restart required to finish",
        UpdateOutcomeKind.InstalledNotVerified => "Installed, not verified",
        UpdateOutcomeKind.Failed => "Update failed",
        UpdateOutcomeKind.ResultNotVerified => "Result not verified",
        _ => "Update not started"
    };
}

/// <summary>
/// FR-UPD-07–09, FR-HIS-03, UJ-04 step 8 (Task 73 Phase 2, ADR-030 foundation 3; audit P-H3): maps every recorded outcome to one
/// of four honest labels — Verified, Restart required, Installed, not verified and Failed — or to "Not started" when nothing ran,
/// or to "Result not verified" when nothing shows the update was installed and nothing checked it. "Installed, not verified" is
/// kept for results where the installer reported finishing but the check failed or was inconclusive; a timeout reads as
/// "Result not verified".
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
    public const string ResultNotVerifiedLabel = "Result not verified";

    /// <summary>The diagnostic recorded when the machine-wide update helper is missing (existing special case).</summary>
    public const string HelperMissingDiagnostic = "elevated-helper-fixed-installed-path-invalid";

    /// <summary>The diagnostic recorded when the provider finished but the post-update check was interrupted (existing workflow text).</summary>
    public const string ProviderCompletedCheckInterruptedDiagnostic = "verification-interrupted-after-provider-completed";

    /// <summary>
    /// The diagnostic the coordinator records (as <see cref="UpdateHistoryStatus.ProductFailed"/>) for an app refused execution admission
    /// because another update or its owned work hadn't finished — for example after the batch settle wait. Nothing ran (review L5).
    /// </summary>
    public const string AdmissionRefusedDiagnostic = "not-started-update-execution-busy-or-owned-work-unresolved";

    /// <summary>
    /// Task 51: the diagnostic prefix a provider records (as <see cref="UpdateHistoryStatus.InstallerFailed"/>) when its typed exit code
    /// says the installer needs a restart <b>before</b> it can install. The update was not applied, so it stays Failed, never Restart required.
    /// </summary>
    public const string RestartBeforeInstallDiagnostic = "restart-required-before-install; update-not-applied";

    private const string AdmissionRefusedSummary = "Not started — another update is still running or its work isn't finished, so nothing was changed.";

    /// <summary>
    /// The workflow message when the data lease refuses the confirmed update before the workflow runs (another data task, a pending
    /// reset or an unresolved earlier update). Nothing was started; it reads as "Not started", never as an unrecorded outcome.
    /// </summary>
    public const string DataUnavailableNotStartedMessage =
        "PC Steward couldn't open its data for this update because another task with its data, such as a reset or an earlier update, isn't finished. Nothing was changed.";

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
            // D7 design §3 item 4: a per-user update that also changed whole-PC uninstall entries, or whose comparison couldn't be made.
            UpdateHistoryStatus.VerificationFailed when diagnostic?.StartsWith(SequentialUpdateBatchCoordinator.UnexpectedWholePcChangesDiagnostic, StringComparison.Ordinal) == true =>
                NotVerified(WholePcChangesSummary(diagnostic, targetVersion), technical),
            UpdateHistoryStatus.VerificationFailed when diagnostic?.StartsWith(SequentialUpdateBatchCoordinator.WholePcCheckUnavailableDiagnostic, StringComparison.Ordinal) == true =>
                NotVerified("The installer finished, but PC Steward couldn't check whether it also changed apps installed for every user of this PC.", technical),
            UpdateHistoryStatus.VerificationFailed => NotVerified("The installer finished, but PC Steward couldn't complete its check of installed apps.", technical),
            // Verification required is "installed" only when the provider reported finishing; otherwise nothing shows it ran.
            UpdateHistoryStatus.VerificationRequired when ProviderReportedFinishing(diagnostic, exitCode) => NotVerified(
                "The installer finished, but PC Steward was interrupted before it could check the installed version.", technical),
            UpdateHistoryStatus.VerificationRequired or UpdateHistoryStatus.Running => ResultNotVerified(
                "The update may have started, but it was interrupted before PC Steward could check the installed version.", technical),
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
            UpdateHistoryStatus.InstallerFailed when diagnostic?.StartsWith(RestartBeforeInstallDiagnostic, StringComparison.Ordinal) == true => Failed(
                "The installer needs Windows to restart before it can install this update, so nothing was updated. Restart when it suits you, then review the update again.", technical),
            UpdateHistoryStatus.InstallerFailed => Failed(exitCode is { } code ? $"The installer reported an error (exit code {code})." : "The installer reported an error.", technical),
            // A timeout ends the attempt before any verification runs. The recorded status stays TimedOut (FR-HIS-03); the display
            // is "Result not verified": nothing shows the installer finished, it may have partly applied, and nothing checked it.
            // It is neither "Installed" nor "Failed". The next step is to scan again, not to review again (independent review).
            UpdateHistoryStatus.TimedOut => ResultNotVerified(
                "The update took too long and PC Steward stopped waiting. It may have partly applied — scan again to check.", technical),
            UpdateHistoryStatus.ProductFailed when string.Equals(diagnostic, HelperMissingDiagnostic, StringComparison.Ordinal) =>
                Failed(ElevationStory.HelperMissingOutcome, technical),
            // Review L5: an admission refusal is recorded as ProductFailed, but nothing ran. Presentation only; the record is unchanged.
            UpdateHistoryStatus.ProductFailed when string.Equals(diagnostic, AdmissionRefusedDiagnostic, StringComparison.Ordinal) =>
                new(UpdateOutcomeKind.NotStarted, NotStartedLabel, "", StatusTone.Neutral, AdmissionRefusedSummary, TryAgain, technical),
            UpdateHistoryStatus.ProductFailed => Failed("PC Steward couldn't run the update.", technical),
            _ => ResultNotVerified("PC Steward couldn't read the recorded outcome.", technical)
        };
    }

    /// <summary>The completion for one reviewed job, from the existing workflow result. The recorded batch is authoritative.</summary>
    public static UpdateJobCompletion ForResult(ReviewedUpdateExecutionResult result, UpdateJobSelection selection, string appName)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(selection);
        string name = string.IsNullOrWhiteSpace(appName) ? selection.PackageId : appName;
        UpdateJobItemOutcome Item(UpdateOutcomePresentation outcome) =>
            new(name, selection.PackageId, selection.InstalledVersion, selection.CandidateVersion, outcome, selection.StableProductKey);

        if (result.Batch is { AdmissionRefused: true })
            return new([Item(new(UpdateOutcomeKind.NotStarted, NotStartedLabel, "", StatusTone.Neutral,
                AdmissionRefusedSummary, TryAgain, AdmissionRefusedDiagnostic))]);
        UpdateHistoryEntry[] finals = result.Batch?.FinalEntries.ToArray() ?? [];
        if (finals.Length != 0)
            return new(finals.Select(entry => Item(ForStatus(entry.Status, entry.Diagnostic, entry.ExitCode, entry.TargetVersion))).ToArray());
        if (!result.Started)
            return new([Item(new(UpdateOutcomeKind.NotStarted, NotStartedLabel, "", StatusTone.Neutral,
                $"Not started — {result.Message}", TryAgain, "not-started"))]);
        // Started but nothing was recorded: execution or verification may have been interrupted. Never Verified.
        return new([Item(ResultNotVerified(
            "PC Steward didn't record an outcome for this update. It may or may not have run, and the installed version wasn't checked.",
            "no-recorded-outcome"))]);
    }

    /// <summary>
    /// SPEC §9.13 G4, AC-UPD-09/10 (Task 73 Phase 6): one completion for a batch, in batch order. Every item that ran is mapped by
    /// <see cref="ForResult"/> from its own recorded result, so Verified still comes only from Success. Items that never reached
    /// confirm-and-run (stopped, review expired, stopped after an earlier problem) read as "Not started", never as failures.
    /// </summary>
    public static UpdateJobCompletion ForBatch(UpdateBatchRunResult run, Func<string, string?> appName)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(appName);
        List<UpdateJobItemOutcome> items = [];
        foreach (UpdateBatchItemResult item in run.Items.OrderBy(item => item.Index))
        {
            string name = appName(item.StableProductKey) is { Length: > 0 } known ? known : item.Selection.PackageId;
            if (item.Disposition == UpdateBatchItemDisposition.Ran)
            {
                items.AddRange(ForResult(item.Result, item.Selection, name).Items);
                continue;
            }
            string summary = item.Disposition switch
            {
                // Review L2: a one-app job has no "turn"; it reads as the single-app expiry.
                UpdateBatchItemDisposition.ReviewExpired when run.Items.Count == 1 => $"Not started — {ReviewedUpdateExecutionWorkflow.SingleReviewExpiredMessage} Nothing was changed.",
                UpdateBatchItemDisposition.ReviewExpired => "Not started — the review was more than 30 minutes old when this app's turn came, so nothing was changed.",
                UpdateBatchItemDisposition.StoppedAfterEarlierProblem => "Not started — an earlier update in this batch didn't finish normally, so PC Steward stopped before this app. Nothing was changed.",
                _ => "Not started — you stopped the remaining updates before this app started, so nothing was changed."
            };
            items.Add(new(name, item.Selection.PackageId, item.Selection.InstalledVersion, item.Selection.CandidateVersion,
                new(UpdateOutcomeKind.NotStarted, NotStartedLabel, "\uE711", StatusTone.Neutral, summary, TryAgain, $"not-started; {item.Disposition}"),
                item.StableProductKey));
        }
        return new(items);
    }

    /// <summary>D7 design §3 item 4: the summary of a per-user update after which whole-PC entries had changed.</summary>
    private static string WholePcChangesSummary(string diagnostic, string targetVersion)
    {
        int at = diagnostic.IndexOf("changed: ", StringComparison.Ordinal);
        string changed = at < 0 ? string.Empty : $" ({diagnostic[(at + "changed: ".Length)..]})";
        string found = diagnostic.Contains("; target-version-detected", StringComparison.Ordinal)
            ? $"PC Steward found version {targetVersion}, but the update" : "The update";
        return $"{found} also changed apps installed for every user of this PC{changed}. Your account's update shouldn't do that, so " +
            "PC Steward hasn't recorded it as successful. Check those apps in Installed apps.";
    }

    private static UpdateOutcomePresentation NotVerified(string summary, string technical) =>
        new(UpdateOutcomeKind.InstalledNotVerified, InstalledNotVerifiedLabel, "", StatusTone.Attention, summary, CheckAgain, technical);

    private static UpdateOutcomePresentation ResultNotVerified(string summary, string technical) =>
        new(UpdateOutcomeKind.ResultNotVerified, ResultNotVerifiedLabel, "\uE9CE", StatusTone.Attention, summary, CheckAgain, technical);

    private static bool ProviderReportedFinishing(string? diagnostic, int? exitCode) =>
        exitCode == 0 || string.Equals(diagnostic, ProviderCompletedCheckInterruptedDiagnostic, StringComparison.Ordinal);

    private static UpdateOutcomePresentation Failed(string summary, string technical) =>
        new(UpdateOutcomeKind.Failed, FailedLabel, "", StatusTone.Critical, summary, TryAgain, technical);

    private static string Technical(UpdateHistoryStatus status, string? diagnostic, int? exitCode) =>
        $"Recorded status: {status}. Exit code: {(exitCode is { } code ? code.ToString(System.Globalization.CultureInfo.InvariantCulture) : "none")}. Diagnostic: {(string.IsNullOrWhiteSpace(diagnostic) ? "none" : diagnostic)}.";
}
