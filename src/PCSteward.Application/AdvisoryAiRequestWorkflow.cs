using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>Why nothing was sent. Every refusal happens before the dispatch claim, so none of them costs anything.</summary>
public enum AdvisoryAiSendRefusal
{
    None,
    NotConnected,
    ModelNotReviewed,
    PricesNotConfirmed,
    ApprovalStale,
    ReservationRefused,
    LedgerUnavailable,
    NoKey,
    ClaimNotRecorded
}

/// <summary>
/// The result of one approved request. When <see cref="Dispatched"/> is false nothing left the PC and nothing is owed. Otherwise the
/// settlement says what stays committed, and the answer (already redacted), its status and whether it was saved are independent of it.
/// </summary>
public sealed record AdvisoryAiSendOutcome(
    AdvisoryAiSendRefusal Refusal,
    AdvisoryAiReservationProblem ReservationProblem,
    bool Dispatched,
    MicroUsd Reservation,
    AdvisoryAiSettlement? Settlement,
    AdvisoryAiLedgerWrite? SettlementWrite,
    AdvisoryAiAnswerStatus? AnswerStatus,
    AdvisoryAiIncompleteReason IncompleteReason,
    string? AnswerText,
    bool AnswerTruncated,
    AdvisoryAiExplanationSave? Saved)
{
    internal static AdvisoryAiSendOutcome Refused(AdvisoryAiSendRefusal refusal, AdvisoryAiReservationProblem problem = AdvisoryAiReservationProblem.None, MicroUsd reservation = default) =>
        new(refusal, problem, false, reservation, null, null, null, AdvisoryAiIncompleteReason.None, null, false, null);
}

/// <summary>
/// FR-AI-01..05, FR-BUD-01..03 (ADR-028 and ADR-029; Task 67/68): one approved request from approval to saved answer, in the only
/// order the ADRs allow: check the approval against fresh settings, prices and preview; reserve the worst case; read the key; record
/// the one dispatch claim; send once; read the response strictly; settle or keep the reservation; redact the answer; save it.
/// <para>
/// The caller runs this inside the exclusive data-operation lease (ADR-025 gate) after <see cref="IAdvisoryAiLedger.RecoverInterruptedAsync"/>,
/// so a reset can never interleave. Nothing before the claim reaches the transport, and a failed claim never sends. After the claim,
/// ledger writes ignore cancellation so the outcome is always recorded; there is no retry of any kind.
/// </para>
/// </summary>
public sealed class AdvisoryAiRequestWorkflow
{
    private readonly IAdvisoryAiSettingsStore settings;
    private readonly IAdvisoryAiLedger ledger;
    private readonly IAdvisoryAiCredentialStore credentials;
    private readonly IAdvisoryAiTransport transport;
    private readonly IAdvisoryAiExplanationStore explanations;
    private readonly IReadOnlyList<AdvisoryAiModelProfile> reviewedModels;
    private readonly AdvisoryAiPaidRequestMode mode;
    private readonly Func<DateTimeOffset> utcNow;

    /// <remarks>
    /// Production passes <see cref="AdvisoryAiModelProfiles.Reviewed"/> as the reviewed models and <see cref="AdvisoryAiBuildPolicy.PaidRequests"/>
    /// as the mode; tests pass synthetic ones.
    /// </remarks>
    public AdvisoryAiRequestWorkflow(IAdvisoryAiSettingsStore settings, IAdvisoryAiLedger ledger, IAdvisoryAiCredentialStore credentials,
        IAdvisoryAiTransport transport, IAdvisoryAiExplanationStore explanations, IReadOnlyList<AdvisoryAiModelProfile> reviewedModels,
        AdvisoryAiPaidRequestMode mode, Func<DateTimeOffset> utcNow)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        this.credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        this.explanations = explanations ?? throw new ArgumentNullException(nameof(explanations));
        this.reviewedModels = reviewedModels ?? throw new ArgumentNullException(nameof(reviewedModels));
        this.mode = mode;
        this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
    }

    /// <summary>
    /// Sends the request <c>approval</c> approved, once, if <c>currentPreview</c> (rebuilt just now from the current draft, model and
    /// limits) still matches it. <c>redaction</c> carries the PC's account, domain and computer names, removed from the answer too.
    /// </summary>
    public async Task<AdvisoryAiSendOutcome> SendAsync(AdvisoryAiApproval approval, AdvisoryAiRequestPreview currentPreview,
        DiagnosticRedactionContext? redaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(currentPreview);
        AdvisoryAiRequestPreview preview = approval.Preview;
        AdvisoryAiModelProfile model = preview.Model;

        AdvisoryAiSettings current = await settings.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
        if (!current.Connected) return AdvisoryAiSendOutcome.Refused(AdvisoryAiSendRefusal.NotConnected);
        if (!reviewedModels.Contains(model)) return AdvisoryAiSendOutcome.Refused(AdvisoryAiSendRefusal.ModelNotReviewed);
        AdvisoryAiTariff? tariff = await settings.GetLatestTariffAsync(model.ModelId, model.ServiceTier, cancellationToken).ConfigureAwait(false);
        if (tariff is null) return AdvisoryAiSendOutcome.Refused(AdvisoryAiSendRefusal.PricesNotConfirmed);
        if (preview.Limits != current.Limits || !approval.IsStillValid(currentPreview, current.BindingFor(tariff)))
            return AdvisoryAiSendOutcome.Refused(AdvisoryAiSendRefusal.ApprovalStale);

        MicroUsd reservation;
        try
        {
            reservation = AdvisoryAiCost.Reservation(preview.InputTokenUpperBound, preview.Limits.MaximumOutputTokens, tariff);
        }
        catch (OverflowException)
        {
            return AdvisoryAiSendOutcome.Refused(AdvisoryAiSendRefusal.ReservationRefused, AdvisoryAiReservationProblem.AmountTooLarge);
        }

        AdvisoryAiReservationRequest request = new(approval.RequestId, model.ModelId, model.ProfileVersion, tariff.Version, current.SettingsVersion,
            preview.InputTokenUpperBound, preview.Limits.MaximumOutputTokens, reservation, mode == AdvisoryAiPaidRequestMode.FirstLiveTest, utcNow());
        AdvisoryAiReserveResult reserved = await ledger.ReserveAsync(request, current.Limits, mode, tariff, model, cancellationToken).ConfigureAwait(false);
        if (reserved.StorageFailed) return AdvisoryAiSendOutcome.Refused(AdvisoryAiSendRefusal.LedgerUnavailable, reservation: reservation);
        if (!reserved.Reserved) return AdvisoryAiSendOutcome.Refused(AdvisoryAiSendRefusal.ReservationRefused, reserved.Problem, reservation);

        AdvisoryAiApiKey? key = null;
        try
        {
            key = cancellationToken.IsCancellationRequested ? null : await credentials.ReadForDispatchAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        if (key is null)
        {
            await ledger.ReleaseUnclaimedAsync(request.RequestId, utcNow(), CancellationToken.None).ConfigureAwait(false);
            return AdvisoryAiSendOutcome.Refused(AdvisoryAiSendRefusal.NoKey, reservation: reservation);
        }

        using (key)
        {
            AdvisoryAiLedgerWrite claim = cancellationToken.IsCancellationRequested
                ? AdvisoryAiLedgerWrite.WrongState
                : await ledger.ClaimDispatchAsync(request.RequestId, utcNow(), CancellationToken.None).ConfigureAwait(false);
            if (claim != AdvisoryAiLedgerWrite.Done)
            {
                // Fail closed: no claim, no send. The release only succeeds while the row is still Reserved; if the claim did commit
                // despite the error, the row stays claimed and recovery records it as uncertain.
                await ledger.ReleaseUnclaimedAsync(request.RequestId, utcNow(), CancellationToken.None).ConfigureAwait(false);
                return AdvisoryAiSendOutcome.Refused(AdvisoryAiSendRefusal.ClaimNotRecorded, reservation: reservation);
            }

            AdvisoryAiTransportResult sent;
            try
            {
                sent = await transport.SendOnceAsync(approval, key, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                sent = AdvisoryAiTransportResult.Of(AdvisoryAiTransportOutcome.Cancelled);
            }
#pragma warning disable CA1031 // Any transport failure after the claim must still be recorded as uncertain, never lost.
            catch (Exception)
#pragma warning restore CA1031
            {
                sent = AdvisoryAiTransportResult.Of(AdvisoryAiTransportOutcome.Failed);
            }
            return await FinishAsync(approval, request, tariff, sent, redaction).ConfigureAwait(false);
        }
    }

    private async Task<AdvisoryAiSendOutcome> FinishAsync(AdvisoryAiApproval approval, AdvisoryAiReservationRequest request, AdvisoryAiTariff tariff,
        AdvisoryAiTransportResult sent, DiagnosticRedactionContext? redaction)
    {
        AdvisoryAiDispatchOutcome outcome = Map(sent.Outcome);
        AdvisoryAiResponse? response = outcome == AdvisoryAiDispatchOutcome.ResponseReceived ? AdvisoryAiResponseReader.Read(sent.Body.AsSpan(), approval) : null;
        AdvisoryAiSettlement settlement = AdvisoryAiSettlementRule.Decide(outcome, response, request.Reservation, tariff);
        AdvisoryAiLedgerWrite written = await ledger.SettleAsync(request.RequestId, settlement, response?.Usage, response?.ResponseId, utcNow(), CancellationToken.None)
            .ConfigureAwait(false);

        if (response is null || response.Status == AdvisoryAiAnswerStatus.Malformed)
            return new(AdvisoryAiSendRefusal.None, AdvisoryAiReservationProblem.None, true, request.Reservation, settlement, written,
                response?.Status, AdvisoryAiIncompleteReason.None, null, false, null);

        AdvisoryAiModelProfile model = approval.Preview.Model;
        int answerScalars = (int)Math.Min(int.MaxValue, (long)request.MaximumOutputTokens * model.MaximumTokenUtf8Bytes);
        DiagnosticSanitizedText answer = response.Text is null ? new(null, false) : AdvisoryAiRedaction.SanitizeAnswer(response.Text, Math.Max(2, answerScalars), redaction);
        AdvisoryAiSavedExplanation saved = new(Guid.NewGuid(), request.RequestId, approval.Preview.Draft.Source, approval.Preview.Draft.OriginId, model.ModelId,
            response.Status, response.IncompleteReason, answer.Value, answer.Truncated, utcNow());
        AdvisoryAiExplanationSave save;
        try
        {
            save = await explanations.SaveExplanationAsync(saved, saved.CreatedAtUtc, CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Saving is independent of the charge: a failure is reported, never allowed to hide the answer.
        catch (Exception)
#pragma warning restore CA1031
        {
            save = AdvisoryAiExplanationSave.StorageFailed;
        }
        return new(AdvisoryAiSendRefusal.None, AdvisoryAiReservationProblem.None, true, request.Reservation, settlement, written,
            response.Status, response.IncompleteReason, answer.Value, answer.Truncated, save);
    }

    private static AdvisoryAiDispatchOutcome Map(AdvisoryAiTransportOutcome outcome) => outcome switch
    {
        AdvisoryAiTransportOutcome.ResponseReceived => AdvisoryAiDispatchOutcome.ResponseReceived,
        AdvisoryAiTransportOutcome.NotSent => AdvisoryAiDispatchOutcome.NotSent,
        AdvisoryAiTransportOutcome.ConnectFailed => AdvisoryAiDispatchOutcome.ConnectFailed,
        AdvisoryAiTransportOutcome.TimedOut => AdvisoryAiDispatchOutcome.TimedOut,
        AdvisoryAiTransportOutcome.Cancelled => AdvisoryAiDispatchOutcome.Cancelled,
        AdvisoryAiTransportOutcome.Redirected => AdvisoryAiDispatchOutcome.Redirected,
        AdvisoryAiTransportOutcome.ResponseTooLarge => AdvisoryAiDispatchOutcome.ResponseTooLarge,
        _ => AdvisoryAiDispatchOutcome.Failed
    };
}
