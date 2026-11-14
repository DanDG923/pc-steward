using System.Globalization;
using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>Why an "Explain with AI…" draft or review can't go further. None of these sends or costs anything.</summary>
public enum AdvisoryAiExplainBlock
{
    None,
    NotAvailable,
    NotConnected,
    SettingsUnavailable,
    PricesNeeded,
    DraftNotPossible,
    PreviewNotPossible
}

/// <summary>A draft made for the owner to check, or why there is none.</summary>
public sealed record AdvisoryAiDraftResult(AdvisoryAiEvidenceDraft? Draft, AdvisoryAiExplainBlock Block, AdvisoryAiDraftProblem DraftProblem);

/// <summary>
/// FR-AI-02/04 (ADR-028 consent items 4–5): what the owner reviews before approving. <see cref="Preview"/> holds the exact body and
/// headers; <see cref="Binding"/> is what an approval made now is bound to; <see cref="Reservation"/> is the most this request could
/// cost, reserved against the limits before it is sent.
/// </summary>
public sealed record AdvisoryAiReview(
    AdvisoryAiRequestPreview? Preview,
    AdvisoryAiApprovalBinding? Binding,
    AdvisoryAiTariff? Tariff,
    MicroUsd Reservation,
    AdvisoryAiExplainBlock Block,
    AdvisoryAiPreviewProblem PreviewProblem)
{
    public bool CanApprove => Block == AdvisoryAiExplainBlock.None && Preview is not null && Binding is not null && Tariff is not null;
}

/// <summary>
/// FR-AI-01..06, FR-BUD-01..03 (Task 67 stage 4c; ADR-028, ADR-029): "Explain with AI…" for a Diagnostics incident or a failed update.
/// It makes the default draft from PC Steward's own redacted records, builds the exact preview for the owner, and only after the
/// owner's explicit approval sends it once through <see cref="AdvisoryAiRequestWorkflow"/>. Approvals live in memory only. Callers
/// run <see cref="SendAsync"/> inside the exclusive data-operation lease, as the workflow requires; everything else is read-only
/// apart from deleting a saved answer.
/// </summary>
public sealed class AdvisoryAiExplainService
{
    private readonly IAdvisoryAiSettingsStore settings;
    private readonly IAdvisoryAiLedger ledger;
    private readonly IAdvisoryAiExplanationStore explanations;
    private readonly AdvisoryAiRequestWorkflow workflow;
    private readonly AdvisoryAiAvailability availability;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly DiagnosticRedactionContext? redaction;

    /// <summary>Makes the service. The optional redaction context holds the PC's account, domain and computer names, removed from drafts, edits and answers.</summary>
    public AdvisoryAiExplainService(IAdvisoryAiSettingsStore settings, IAdvisoryAiLedger ledger, IAdvisoryAiExplanationStore explanations,
        AdvisoryAiRequestWorkflow workflow, AdvisoryAiAvailability availability, Func<DateTimeOffset> utcNow, DiagnosticRedactionContext? redaction = null)
    {
        this.redaction = redaction;
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        this.explanations = explanations ?? throw new ArgumentNullException(nameof(explanations));
        this.workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        this.availability = availability ?? throw new ArgumentNullException(nameof(availability));
        this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
    }

    public AdvisoryAiAvailability Availability => availability;

    /// <summary>The default draft for one Diagnostics incident (FR-AI-02, ADR-028 R4).</summary>
    public Task<AdvisoryAiDraftResult> DraftForIncidentAsync(DiagnosticTimeline timeline, DiagnosticRecordKey incident, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        return DraftAsync((allowance) => (AdvisoryAiDiagnosticDraftBuilder.TryBuild(timeline, incident, allowance, redaction, out AdvisoryAiEvidenceDraft? draft), draft), cancellationToken);
    }

    /// <summary>The default draft for one failed update, from that job's own recorded history only (FR-AI-02).</summary>
    public Task<AdvisoryAiDraftResult> DraftForFailedUpdateAsync(Guid jobId, AdvisoryAiFailedUpdateFacts facts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return DraftAsync((allowance) => (AdvisoryAiFailedUpdateDraftBuilder.TryBuild(jobId, facts, allowance, redaction, out AdvisoryAiEvidenceDraft? draft), draft), cancellationToken);
    }

    private async Task<AdvisoryAiDraftResult> DraftAsync(Func<AdvisoryAiInputAllowance, (AdvisoryAiDraftProblem Problem, AdvisoryAiEvidenceDraft? Draft)> build,
        CancellationToken cancellationToken)
    {
        if (!availability.CanSetUp || availability.Model is not AdvisoryAiModelProfile model) return new(null, AdvisoryAiExplainBlock.NotAvailable, AdvisoryAiDraftProblem.None);
        AdvisoryAiSettings current;
        try { current = await settings.GetSettingsAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
        {
            return new(null, AdvisoryAiExplainBlock.SettingsUnavailable, AdvisoryAiDraftProblem.None);
        }
        if (!current.Connected) return new(null, AdvisoryAiExplainBlock.NotConnected, AdvisoryAiDraftProblem.None);
        (AdvisoryAiDraftProblem problem, AdvisoryAiEvidenceDraft? draft) = build(AdvisoryAiInputAllowance.For(current.Limits, model, AdvisoryAiInstructions.Text));
        return draft is null || problem != AdvisoryAiDraftProblem.None
            ? new(null, AdvisoryAiExplainBlock.DraftNotPossible, problem == AdvisoryAiDraftProblem.None ? AdvisoryAiDraftProblem.NoEvidenceLeft : problem)
            : new(draft, AdvisoryAiExplainBlock.None, AdvisoryAiDraftProblem.None);
    }

    /// <summary>
    /// ADR-028 consent items 4–5: the exact request for <paramref name="draft"/> under the current settings and today's confirmed
    /// prices, and the most it could cost. Reads no key and sends nothing.
    /// </summary>
    public async Task<AdvisoryAiReview> ReviewAsync(AdvisoryAiEvidenceDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!availability.CanSetUp || availability.Model is not AdvisoryAiModelProfile model) return Blocked(AdvisoryAiExplainBlock.NotAvailable);
        AdvisoryAiSettings current;
        AdvisoryAiTariff? tariff;
        try
        {
            current = await settings.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
            tariff = await settings.GetLatestTariffAsync(model.ModelId, model.ServiceTier, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
        {
            return Blocked(AdvisoryAiExplainBlock.SettingsUnavailable);
        }
        if (!current.Connected) return Blocked(AdvisoryAiExplainBlock.NotConnected);
        DateTimeOffset now = utcNow();
        if (tariff is null || !tariff.IsCurrent(now) || tariff.Validate(model) != AdvisoryAiTariffProblem.None) return Blocked(AdvisoryAiExplainBlock.PricesNeeded);
        AdvisoryAiPreviewProblem problem = AdvisoryAiRequestBuilder.TryBuild(draft, model, current.Limits, out AdvisoryAiRequestPreview? preview);
        if (problem != AdvisoryAiPreviewProblem.None || preview is null) return Blocked(AdvisoryAiExplainBlock.PreviewNotPossible, problem);
        MicroUsd reservation;
        try { reservation = AdvisoryAiCost.Reservation(preview.InputTokenUpperBound, preview.Limits.MaximumOutputTokens, tariff); }
        catch (OverflowException) { return Blocked(AdvisoryAiExplainBlock.PreviewNotPossible, AdvisoryAiPreviewProblem.RequestTooLarge); }
        return new(preview, current.BindingFor(tariff), tariff, reservation, AdvisoryAiExplainBlock.None, AdvisoryAiPreviewProblem.None);
    }

    /// <summary>
    /// The owner approved <paramref name="review"/>: records that approval (in memory), finishes any request an earlier run left
    /// unfinished, rebuilds the preview from the same draft under the current limits and sends it once. Any change since the review
    /// (an edit, a limit, prices, consent or the key) refuses it before anything is reserved or sent.
    /// </summary>
    public async Task<AdvisoryAiSendOutcome> SendAsync(AdvisoryAiReview review, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(review);
        if (!review.CanApprove) return AdvisoryAiSendOutcome.Refused(AdvisoryAiSendRefusal.ApprovalStale);
        AdvisoryAiApproval approval = AdvisoryAiApproval.Approve(review.Preview!, review.Binding!, utcNow());
        _ = await ledger.RecoverInterruptedAsync(utcNow(), cancellationToken).ConfigureAwait(false);
        AdvisoryAiSettings current = await settings.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
        if (AdvisoryAiRequestBuilder.TryBuild(review.Preview!.Draft, review.Preview.Model, current.Limits, out AdvisoryAiRequestPreview? fresh) != AdvisoryAiPreviewProblem.None || fresh is null)
            return AdvisoryAiSendOutcome.Refused(AdvisoryAiSendRefusal.ApprovalStale);
        return await workflow.SendAsync(approval, fresh, redaction, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>FR-AI-06: the saved, unexpired answers for one incident or job, newest first.</summary>
    public Task<IReadOnlyList<AdvisoryAiSavedExplanation>> SavedAsync(AdvisoryAiSourceKind source, string originId, CancellationToken cancellationToken) =>
        explanations.ListExplanationsAsync(source, originId, utcNow(), cancellationToken);

    public Task<bool> DeleteSavedAsync(Guid id, CancellationToken cancellationToken) => explanations.DeleteExplanationAsync(id, cancellationToken);

    /// <summary>
    /// ADR-028 consent item 4: applies the owner's edits to <paramref name="draft"/>: <paramref name="texts"/> holds each item's text
    /// as it now stands (null for an item the owner removed). Edits are redacted again exactly as generated text is.
    /// </summary>
    public AdvisoryAiDraftProblem ApplyEdits(AdvisoryAiEvidenceDraft draft, IReadOnlyList<string?> texts, out AdvisoryAiEvidenceDraft edited)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(texts);
        edited = draft;
        if (texts.Count != draft.Items.Count) return AdvisoryAiDraftProblem.NoSuchItem;
        AdvisoryAiEvidenceDraft current = draft;
        for (int index = texts.Count - 1; index >= 0; index--)
        {
            AdvisoryAiDraftProblem problem;
            if (texts[index] is not string text) problem = current.TryRemove(index, out current);
            else if (!string.Equals(text, draft.Items[index].Text, StringComparison.Ordinal)) problem = current.TryEdit(index, text, redaction, out current);
            else continue;
            if (problem != AdvisoryAiDraftProblem.None) return problem;
        }
        edited = current;
        return AdvisoryAiDraftProblem.None;
    }

    private static AdvisoryAiReview Blocked(AdvisoryAiExplainBlock block, AdvisoryAiPreviewProblem problem = AdvisoryAiPreviewProblem.None) =>
        new(null, null, null, default, block, problem);
}

/// <summary>FR-AI-02/05, FR-BUD-02/03 (Task 67 stage 4c): the words of "Explain with AI…". Amounts are US dollars.</summary>
public static class AdvisoryAiExplainText
{
    public const string ActionLabel = "Explain with AI…";

    public const string DraftIntro = "This is everything that would be sent. PC Steward has already removed your account, domain and computer names. Edit or remove anything you don't want to share; nothing is sent until you approve the exact request.";

    public const string ApproveLabel = "Approve and send";

    /// <summary>Task 70 T70-06 (ADR-028 consent): shown beside the draft and the exact request.</summary>
    public const string ResidualSecrets = "Removing names is best effort. PC Steward can't recognise every password, key or personal detail, in its records or in what you type. Check every item before you send it.";

    public const string AdviceOnly = "An AI explanation is advice only. PC Steward never acts on it, and it can be wrong.";

    public static string Blocked(AdvisoryAiExplainBlock block, AdvisoryAiDraftProblem draftProblem = AdvisoryAiDraftProblem.None, AdvisoryAiPreviewProblem previewProblem = AdvisoryAiPreviewProblem.None) => block switch
    {
        AdvisoryAiExplainBlock.None => string.Empty,
        AdvisoryAiExplainBlock.NotAvailable => AdvisoryAiSetupText.NoReviewedModel,
        AdvisoryAiExplainBlock.NotConnected => "Connect an AI service in Settings first. Nothing has been sent.",
        AdvisoryAiExplainBlock.SettingsUnavailable => "PC Steward couldn't read its AI settings, so nothing was prepared or sent.",
        AdvisoryAiExplainBlock.PricesNeeded => "Confirm today's prices in Settings, AI explanations, first. Confirmed prices last 24 hours. Nothing has been sent.",
        AdvisoryAiExplainBlock.DraftNotPossible => draftProblem switch
        {
            AdvisoryAiDraftProblem.IncidentNotFound => "That record is no longer in this timeline, so there is nothing to explain.",
            AdvisoryAiDraftProblem.IncidentHasNoKnownTime => "That record has no reliable time, so PC Steward can't tell what happened near it. Nothing has been sent.",
            AdvisoryAiDraftProblem.RequiredEvidenceDoesNotFit => "The record itself is longer than your input limit allows. Raise the input limit in Settings, or explain a different record.",
            _ => "PC Steward couldn't prepare anything to send for this. Nothing has been sent."
        },
        _ => previewProblem switch
        {
            AdvisoryAiPreviewProblem.LimitsNotUsable => "Your AI limits can't be used for this model. Check them in Settings. Nothing has been sent.",
            AdvisoryAiPreviewProblem.DraftDoesNotFitInputLimit => "This is longer than your input limit allows. Remove some items, or raise the input limit in Settings.",
            AdvisoryAiPreviewProblem.RequestTooLarge => "This request would be larger than PC Steward allows. Remove some items and try again.",
            _ => AdvisoryAiSetupText.NoReviewedModel
        }
    };

    public static string RoleLabel(AdvisoryAiEvidenceRole role) => role switch
    {
        AdvisoryAiEvidenceRole.SelectedIncident => "The record you chose",
        AdvisoryAiEvidenceRole.NearbyPcStewardChange => "A PC Steward change nearby",
        AdvisoryAiEvidenceRole.OtherNearbyRecord => "Another record nearby",
        AdvisoryAiEvidenceRole.Coverage => "What each source returned",
        AdvisoryAiEvidenceRole.FailedUpdateSummary => "The update",
        _ => "A recorded step of the update"
    };

    /// <summary>What was left out to fit the input limit, by role, so the owner knows the draft is partial.</summary>
    public static string? Omitted(AdvisoryAiEvidenceDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        int count = draft.Omitted.Sum(item => item.Count);
        return count == 0 ? null : string.Create(CultureInfo.InvariantCulture, $"{count} more {(count == 1 ? "record was" : "records were")} left out to fit your input limit.");
    }

    public static string Cost(AdvisoryAiReview review, AdvisoryAiProvider provider)
    {
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(provider);
        return $"Before sending, PC Steward reserves up to {review.Reservation.ToDollarString()} against your limits: the most this request could cost. {provider.Name} charges your own account. If PC Steward can't tell what it cost, the whole reservation is kept. {AdvisoryAiSetupText.OutputDeadlineNote}";
    }

    public static string Route(AdvisoryAiRequestPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        return $"Sent once to {preview.Provider.Endpoint}, for {preview.Model.ModelId}. Your key goes in the {preview.Provider.AuthorizationName} header, which is shown here as {preview.Provider.AuthorizationPreview}.";
    }

    /// <summary>
    /// Task 70 T70-07: the reported counts in words, shown with the outcome so the owner can note them. Null when nothing was received.
    /// </summary>
    public static string? UsageCounts(AdvisoryAiUsageReport? report, CultureInfo? culture = null)
    {
        if (report is null) return null;
        CultureInfo format = culture ?? CultureInfo.CurrentCulture;
        string usage = report.Usage is AdvisoryAiUsage counts
            ? string.Format(format, "{0:N0} input tokens ({1:N0} cached), {2:N0} output tokens ({3:N0} reasoning), {4:N0} in total",
                counts.InputTokens, counts.CachedInputTokens, counts.OutputTokens, counts.ReasoningTokens, counts.TotalTokens)
            : "no complete usage";
        string cache = report.CacheWriteUnreadable ? "a cache-write figure that isn't a plain count"
            : report.CacheWriteTokens is long written ? string.Format(format, "{0:N0} cache-write tokens", written)
            : "no cache-write figure";
        return $"The provider reported {usage}, and {cache}." + (report.UsageReliable ? string.Empty : " PC Steward didn't trust this usage to settle the charge.");
    }

    /// <summary>
    /// Independent review F1/F3: the reply's HTTP status, service tier, the closed reason it wasn't trusted and anything that departed
    /// from the approved request, so a failed or unusual reply can be told apart and noted down. Numbers and fixed words only: no body,
    /// header or provider message. Null when nothing was received.
    /// </summary>
    public static string? ReplyDetails(AdvisoryAiUsageReport? report)
    {
        if (report is null) return null;
        List<string> parts = [];
        if (report.HttpStatus is int status) parts.Add(string.Create(CultureInfo.InvariantCulture, $"HTTP status {status}"));
        parts.Add(report.ServiceTier is string tier ? $"service tier \"{tier}\"" : "no service tier reported");
        string details = $"Reply details: {string.Join(", ", parts)}.";
        if (report.MalformedReason != AdvisoryAiMalformedReason.None) details += $" It wasn't trusted as an answer because {MalformedReasonText(report.MalformedReason)}.";
        List<string> departures = [];
        if (report.Departures.HasFlag(AdvisoryAiResponseDeparture.ServiceTier)) departures.Add("a service tier other than the one requested and priced");
        if (report.Departures.HasFlag(AdvisoryAiResponseDeparture.UnknownUsageField)) departures.Add("a usage figure PC Steward doesn't know how to price");
        if (report.Departures.HasFlag(AdvisoryAiResponseDeparture.PromptCacheUsed)) departures.Add("cached input although no prompt cache was asked for");
        if (report.Departures.HasFlag(AdvisoryAiResponseDeparture.ConversationState)) departures.Add("background or conversation state that wasn't asked for");
        if (departures.Count > 0) details += $" The reply reported {string.Join("; ", departures)}, so paid requests are paused until you review it in Settings.";
        return details;
    }

    private static string MalformedReasonText(AdvisoryAiMalformedReason reason) => reason switch
    {
        AdvisoryAiMalformedReason.TooLarge => "it was larger than this request allows",
        AdvisoryAiMalformedReason.NotJson => "it wasn't readable JSON",
        AdvisoryAiMalformedReason.NotAResponseObject => "it wasn't a response object (an error reply usually looks like this)",
        AdvisoryAiMalformedReason.DuplicateProperty => "a field appeared twice",
        AdvisoryAiMalformedReason.MissingOrInvalidId => "its response ID was missing or invalid",
        AdvisoryAiMalformedReason.ModelMismatch => "it named a different model",
        AdvisoryAiMalformedReason.UnknownStatus => "its status was missing or unknown",
        AdvisoryAiMalformedReason.StoredResponse => "it said the response was stored",
        AdvisoryAiMalformedReason.ToolsPresent => "it listed tools",
        AdvisoryAiMalformedReason.UnexpectedOutputItem => "it held output other than a message",
        AdvisoryAiMalformedReason.InvalidOutputText => "its message text wasn't plain text",
        AdvisoryAiMalformedReason.UnexpectedState => "it carried background or conversation state",
        _ => "of an unknown problem"
    };

    /// <summary>ADR-028 "Provider storage disclosure", repeated before the first egress (independent review I4).</summary>
    public static string Retention(AdvisoryAiProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return $"PC Steward asks {provider.Name} not to store this request or its answer, but {provider.Name} may keep data for abuse monitoring. Read {provider.Name}'s data controls at {provider.DataControlsPage}.";
    }

    /// <summary>FR-AI-05, FR-BUD-02/03: what happened, what it cost as far as PC Steward knows, and whether the answer was saved.</summary>
    public static string Outcome(AdvisoryAiSendOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (!outcome.Dispatched)
        {
            string why = outcome.Refusal switch
            {
                AdvisoryAiSendRefusal.NotConnected => "AI explanations are not connected.",
                AdvisoryAiSendRefusal.ModelNotReviewed => AdvisoryAiSetupText.NoReviewedModel,
                AdvisoryAiSendRefusal.PricesNotConfirmed => "Today's prices aren't confirmed.",
                AdvisoryAiSendRefusal.ApprovalStale => "Something changed after you approved it (the text, a limit, the prices, the connection or the key), so it needs a fresh review.",
                AdvisoryAiSendRefusal.ReservationRefused => AdvisoryAiReservationRule.Explain(outcome.ReservationProblem),
                AdvisoryAiSendRefusal.NoKey => "PC Steward couldn't read your key from Credential Manager. Replace the key in Settings.",
                AdvisoryAiSendRefusal.ProviderUnavailable => "This version of PC Steward can't reach that AI service.",
                _ => "PC Steward couldn't record the request safely."
            };
            return $"Nothing was sent and nothing was charged. {why}";
        }
        string cost = outcome.Settlement switch
        {
            { State: AdvisoryAiRequestState.Settled } settled when settled.AboveReservation =>
                $"It cost {settled.Committed.ToDollarString()}, more than PC Steward reserved, so paid requests are paused until you review it in Settings.",
            { State: AdvisoryAiRequestState.Settled } settled => $"It cost {settled.Committed.ToDollarString()}.",
            { } kept => $"PC Steward can't tell what it cost, so the whole reservation of {kept.Committed.ToDollarString()} is counted.",
            null => $"PC Steward can't tell what it cost, so the whole reservation of {outcome.Reservation.ToDollarString()} is counted."
        };
        if (outcome.SettlementWrite is not (AdvisoryAiLedgerWrite.Done or AdvisoryAiLedgerWrite.AlreadyDone))
            cost += " The spending record couldn't be updated, so this request stays counted at its whole reservation.";
        string answer = outcome.AnswerStatus switch
        {
            AdvisoryAiAnswerStatus.Completed => "The answer is below.",
            AdvisoryAiAnswerStatus.Incomplete => outcome.IncompleteReason == AdvisoryAiIncompleteReason.OutputLimit
                ? "The answer stopped at your output limit, so it is incomplete."
                : "The answer stopped early, so it is incomplete.",
            AdvisoryAiAnswerStatus.Refused => "The model declined to answer.",
            AdvisoryAiAnswerStatus.ProviderFailed => "The AI service reported a failure instead of an answer.",
            AdvisoryAiAnswerStatus.Malformed => "The reply wasn't a well-formed answer for this request, so none of it is shown.",
            _ => "No answer came back."
        };
        string saved = outcome.Saved switch
        {
            AdvisoryAiExplanationSave.Saved => " It is saved for 30 days.",
            null => string.Empty,
            _ => " It couldn't be saved, so copy anything you need now."
        };
        return $"Sent once. {cost} {answer}{saved}";
    }

    public static string SavedHeading(AdvisoryAiSavedExplanation saved, CultureInfo? culture = null, TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(saved);
        return $"{DisplayFormat.DateTime(saved.CreatedAtUtc, culture ?? CultureInfo.CurrentCulture, zone)}, {saved.ModelId}{(saved.Status == AdvisoryAiAnswerStatus.Completed ? string.Empty : " (incomplete)")}. Kept until {DisplayFormat.DateTime(saved.ExpiresAtUtc, culture ?? CultureInfo.CurrentCulture, zone)}.";
    }
}
