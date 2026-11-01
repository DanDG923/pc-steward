namespace PCSteward.Application;

/// <summary>One labelled display line of a review or history view model, with its accessible name (ADR-021, best effort).</summary>
public sealed record ReviewedDisplayField(string Label, string Text)
{
    public string AutomationName => Label + ": " + Text;
}

/// <summary>
/// FR-UPD-15, FR-UPD-01, FR-UI-03/04, TM-03/04/07/13; Task 50 admission obligation 5 (N6 review M1): the portable view model
/// behind the Desktop's review of the runtime-limit Chocolatey route. It keeps the Desktop thin: every
/// <see cref="ChocolateyReviewDisclosureView"/> field is a bound property and an ordered <see cref="Fields"/> entry shown before
/// any confirmation.
/// <list type="bullet">
/// <item>The split-token state is probed through the injected function on every <see cref="Open"/>, never cached between reviews.</item>
/// <item><see cref="IsConfirmEnabled"/> is exactly <see cref="CanConfirm"/>, which is false today (the route is not admitted).</item>
/// <item><see cref="TryConfirm"/> goes only through <see cref="ReviewedUpdateReview.Confirm"/>, which rechecks the review gate; it
/// returns null whenever <see cref="CanConfirm"/> is false or no reviewed operation exists. It dispatches nothing.</item>
/// </list>
/// </summary>
public sealed class ChocolateyReviewedRouteReviewViewModel
{
    /// <summary>The Desktop entry point's text (only on Chocolatey Community rows).</summary>
    public const string EntryText = "Chocolatey reviewed update (unavailable)…";
    public const string EntryAutomationName = EntryText + " Opens the disclosure. This update route is unavailable.";
    public const string ConfirmText = "Confirm update";
    public const string CloseText = "Close";
    public const string UnavailableHeading = "Why this update cannot be confirmed";
    public const string NotStartedText = "No update was started. This Chocolatey update route is not available.";

    /// <summary>The Chocolatey Community source ID as the inventory reports it (case-insensitive, as the workflow parses it).</summary>
    internal const string ChocolateyCommunitySourceId = "chocolatey-community";

    private readonly ReviewedUpdateReview? review;

    private ChocolateyReviewedRouteReviewViewModel(ChocolateyReviewDisclosureView view, ReviewedRouteSplitTokenState splitToken, ReviewedUpdateReview? review)
    {
        View = view;
        SplitTokenState = splitToken;
        this.review = review;
        List<ReviewedDisplayField> fields =
        [
            new("Candidate", view.CandidateIntent),
            new("Candidate checked", view.IntentTime),
            new("Your user cache", view.UserCacheDisclosure),
            new("Installer mode", view.InstallerMode),
        ];
        if (view.RuntimeLimits is { } limits) fields.Add(new("Package scripts and downloads", limits));
        fields.Add(new("Running installers", view.LingeringProcesses));
        if (view.LibBadResidue is { } residue) fields.Add(new("Earlier failed install", residue));
        Fields = fields.AsReadOnly();
        UnavailableFields = view.UnavailableTexts.Select(text => new ReviewedDisplayField("Unavailable", text)).ToArray();
    }

    /// <summary>True only for a Chocolatey Community source row: the only rows that show the entry point.</summary>
    public static bool AppliesTo(string? sourceId) => string.Equals(sourceId, ChocolateyCommunitySourceId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Opens a review: probes the split-token state now, then presents the frozen <paramref name="draft"/> with the checked
    /// <paramref name="intent"/>, or the no-review disclosure when there is no draft (always today). A probe fault is Unknown
    /// (unavailable).
    /// </summary>
    public static ChocolateyReviewedRouteReviewViewModel Open(ReviewedUpdateDraft? draft, ChocolateyReviewIntentFacts? intent,
        Func<ReviewedRouteSplitTokenState> probeSplitToken, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(probeSplitToken);
        ReviewedRouteSplitTokenState splitToken;
        try { splitToken = probeSplitToken(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { splitToken = ReviewedRouteSplitTokenState.Unknown; }
        if (!Enum.IsDefined(splitToken)) splitToken = ReviewedRouteSplitTokenState.Unknown;
        return draft is null
            ? new(ChocolateyReviewDisclosurePresentation.PresentWithoutReview(splitToken), splitToken, null)
            : new(ChocolateyReviewDisclosurePresentation.Present(draft.Operation, intent, splitToken), splitToken, new ReviewedUpdateReview(draft, clock));
    }

    /// <summary>The presentation this view model binds.</summary>
    public ChocolateyReviewDisclosureView View { get; }

    /// <summary>The split-token state probed when this review was opened.</summary>
    public ReviewedRouteSplitTokenState SplitTokenState { get; }

    public string Title => View.Title;
    public string CandidateIntent => View.CandidateIntent;
    public DateTimeOffset? IntentResolvedAtUtc => View.IntentResolvedAt;
    public string IntentTime => View.IntentTime;
    public string UserCacheDisclosure => View.UserCacheDisclosure;
    public string InstallerMode => View.InstallerMode;
    public string? RuntimeLimits => View.RuntimeLimits;
    public string LingeringProcesses => View.LingeringProcesses;
    public string? LibBadResidue => View.LibBadResidue;
    public IReadOnlyList<ReviewedRouteUnavailableReason> UnavailableReasons => View.UnavailableReasons;
    public IReadOnlyList<string> UnavailableTexts => View.UnavailableTexts;
    public bool IsUnavailable => View.UnavailableReasons.Count != 0;
    public bool CanConfirm => View.CanConfirm;

    /// <summary>Bound to the Confirm button's enabled state: exactly <see cref="CanConfirm"/>.</summary>
    public bool IsConfirmEnabled => CanConfirm;

    public string ConfirmAutomationName => CanConfirm ? "Confirm this reviewed Chocolatey update" : "Confirm update is unavailable for this Chocolatey update route";

    /// <summary>The disclosure lines in display order (every field except the title and the unavailability reasons).</summary>
    public IReadOnlyList<ReviewedDisplayField> Fields { get; }

    /// <summary>One line per unavailability reason, in display order.</summary>
    public IReadOnlyList<ReviewedDisplayField> UnavailableFields { get; }

    /// <summary>
    /// The only confirmation path: refused unless <see cref="CanConfirm"/>, then <see cref="ReviewedUpdateReview.Confirm"/>,
    /// which rechecks the review gate and refuses an expired, cancelled, repeated or unadmitted review. Null on any refusal.
    /// </summary>
    public LiveApprovedUpdate? TryConfirm()
    {
        if (!CanConfirm || review is null) return null;
        try { return review.Confirm(); }
        catch (InvalidOperationException) { return null; }
    }
}

/// <summary>
/// FR-HIS-01, FR-UPD-15, TM-07/13; admission obligation 5 (N6 review M1): one reviewed-update outcome for the Desktop history,
/// binding every <see cref="ReviewedUpdateHistoryView"/> field, including <see cref="Route"/>, <see cref="VerificationDetail"/>,
/// <see cref="LingeringProcessesNote"/> and <see cref="HelperReport"/>. Display only.
/// </summary>
public sealed class ReviewedUpdateHistoryItemViewModel
{
    private ReviewedUpdateHistoryItemViewModel(ReviewedUpdateHistoryView view)
    {
        View = view;
        List<ReviewedDisplayField> fields = [new("Outcome", view.Status)];
        if (view.LingeringProcessesNote is { } lingering) fields.Add(new("Running installers", lingering));
        if (view.VerificationDetail is { } verification) fields.Add(new("Verification", verification));
        fields.Add(new("Route", view.Route));
        fields.Add(new("Installer mode", view.InstallerMode));
        fields.Add(new("Candidate evidence", view.CandidateEvidence));
        if (view.HelperReport is { } helper) fields.Add(new("Helper report", helper));
        if (view.Disclosure is { } disclosure) fields.Add(new("Disclosure", disclosure));
        if (view.BlocksOtherUpdates) fields.Add(new("Other updates", BlocksOtherUpdatesText));
        if (view.Diagnostic.Length != 0) fields.Add(new("Diagnostic", view.Diagnostic));
        Fields = fields.AsReadOnly();
    }

    public const string BlocksOtherUpdatesText = "Other updates stay blocked until this record is safely reconciled.";

    public static ReviewedUpdateHistoryItemViewModel From(ReviewedUpdateHistoryView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new(view);
    }

    public ReviewedUpdateHistoryView View { get; }
    public string Title => View.Title;
    public string Status => View.Status;
    public string Route => View.Route;
    public string InstallerMode => View.InstallerMode;
    public string CandidateEvidence => View.CandidateEvidence;
    public string? Disclosure => View.Disclosure;
    public bool BlocksOtherUpdates => View.BlocksOtherUpdates;
    public string Diagnostic => View.Diagnostic;
    public string? HelperReport => View.HelperReport;
    public string? VerificationDetail => View.VerificationDetail;
    public string? LingeringProcessesNote => View.LingeringProcessesNote;
    public string AutomationName => Title + ": " + Status;

    /// <summary>The outcome lines in display order; absent optional fields are omitted.</summary>
    public IReadOnlyList<ReviewedDisplayField> Fields { get; }
}

/// <summary>
/// FR-HIS-01; admission obligation 5: the Desktop's read-only list of reviewed-update outcomes, loaded through
/// <see cref="IReviewedUpdateHistoryReader"/>. Any read fault gives the unavailable state, never partial or invented rows.
/// </summary>
public sealed class ReviewedUpdateHistoryListViewModel
{
    public const int MaximumRecords = 50;
    public const string Heading = "Reviewed update outcomes";
    public const string EmptyText = "No reviewed update outcomes are recorded.";
    public const string UnavailableText = "Reviewed update history is unavailable right now. Nothing was started or changed.";
    public const string LoadingText = "Loading reviewed update outcomes…";

    private ReviewedUpdateHistoryListViewModel(bool isAvailable, IReadOnlyList<ReviewedUpdateHistoryItemViewModel> items)
    {
        IsAvailable = isAvailable;
        Items = items;
    }

    public static ReviewedUpdateHistoryListViewModel Unavailable { get; } = new(false, []);

    public bool IsAvailable { get; }
    public IReadOnlyList<ReviewedUpdateHistoryItemViewModel> Items { get; }
    public string Summary => !IsAvailable ? UnavailableText : Items.Count == 0 ? EmptyText :
        $"Showing {Items.Count} reviewed update record{(Items.Count == 1 ? string.Empty : "s")}, newest unresolved first.";

    public static async Task<ReviewedUpdateHistoryListViewModel> LoadAsync(IReviewedUpdateHistoryReader reader, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        try
        {
            IReadOnlyList<ReviewedUpdateHistoryRecord> records = await reader.GetReviewedUpdateHistoryAsync(MaximumRecords, cancellationToken).ConfigureAwait(false);
            if (records is null || records.Count > MaximumRecords) return Unavailable;
            return new(true, records.Select(record => ReviewedUpdateHistoryItemViewModel.From(ReviewedUpdateHistoryPresentation.Present(record))).ToArray());
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Unavailable;
        }
    }
}
