using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>
/// FR-AI-01/03, FR-BUD-01 (ADR-028/029): the owner's AI settings as stored. <see cref="Connected"/> is true only after the deliberate
/// connect flow recorded consent; every version rises on its own kind of change, so an approval bound to an older one is stale.
/// </summary>
public sealed record AdvisoryAiSettings(
    bool Connected,
    long SettingsVersion,
    long ConsentVersion,
    long CredentialGeneration,
    AdvisoryAiLimits Limits)
{
    /// <summary>A fresh app state: not connected, the accepted default limits, every version at zero.</summary>
    public static AdvisoryAiSettings Initial { get; } = new(false, 0, 0, 0, AdvisoryAiLimits.Defaults);

    public AdvisoryAiApprovalBinding BindingFor(AdvisoryAiTariff tariff)
    {
        ArgumentNullException.ThrowIfNull(tariff);
        return new(SettingsVersion, ConsentVersion, CredentialGeneration, tariff.Version);
    }
}

public enum AdvisoryAiSettingsWrite
{
    Saved,

    /// <summary>Someone saved first: the caller's view is stale and nothing was written.</summary>
    VersionConflict,
    Invalid,
    StorageFailed
}

/// <summary>
/// FR-AI-01/03, FR-BUD-01 (ADR-029 Q7 and F6): AI settings and owner-confirmed tariffs. A tariff is never edited in place: each
/// confirmation is a new immutable version, so a reservation keeps the exact rates it was made with.
/// </summary>
public interface IAdvisoryAiSettingsStore
{
    Task<AdvisoryAiSettings> GetSettingsAsync(CancellationToken cancellationToken);

    /// <summary>Saves new limits when <paramref name="expectedSettingsVersion"/> is still current, and raises the settings version.</summary>
    Task<AdvisoryAiSettingsWrite> SaveLimitsAsync(AdvisoryAiLimits limits, long expectedSettingsVersion, CancellationToken cancellationToken);

    /// <summary>Records the connect flow's consent (raises the consent version) or a disconnect (also raises it, so approvals fail).</summary>
    Task<AdvisoryAiSettingsWrite> SetConnectedAsync(bool connected, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    /// <summary>Raises the credential generation after a key is saved, replaced or removed, so no approval outlives the key it saw.</summary>
    Task<AdvisoryAiSettingsWrite> AdvanceCredentialGenerationAsync(CancellationToken cancellationToken);

    /// <summary>Records one owner confirmation of prices. The version passed in is ignored: the store assigns the next one.</summary>
    Task<AdvisoryAiTariff?> ConfirmTariffAsync(AdvisoryAiTariff tariff, CancellationToken cancellationToken);

    /// <summary>The newest confirmed tariff for exactly this model and tier, current or not; null when none was ever confirmed.</summary>
    Task<AdvisoryAiTariff?> GetLatestTariffAsync(string modelId, string serviceTier, CancellationToken cancellationToken);
}

/// <summary>ADR-029 "Minimal records": what one reservation stores. No prompt, evidence, body hash, key or identity.</summary>
public sealed record AdvisoryAiReservationRequest(
    Guid RequestId,
    string ModelId,
    int ModelProfileVersion,
    long TariffVersion,
    long SettingsVersion,
    int InputTokenUpperBound,
    int MaximumOutputTokens,
    MicroUsd Reservation,
    bool FirstLiveTest,
    DateTimeOffset ReservedAtUtc)
{
    public AdvisoryAiPeriod Period => AdvisoryAiPeriod.Of(ReservedAtUtc);
}

public sealed record AdvisoryAiReserveResult(AdvisoryAiReservationProblem Problem, AdvisoryAiLedgerSnapshot? Snapshot, bool StorageFailed = false)
{
    public bool Reserved => Problem == AdvisoryAiReservationProblem.None && !StorageFailed;
}

/// <summary>The result of one compare-and-set write to a request row.</summary>
public enum AdvisoryAiLedgerWrite
{
    Done,

    /// <summary>The identical change was already recorded; nothing was written again.</summary>
    AlreadyDone,

    /// <summary>The row is not in the state this change starts from (for example a claim after a release). Nothing was written.</summary>
    WrongState,
    NotFound,

    /// <summary>A settlement conflicted with one already recorded, or reused a provider response ID: paid requests now need review.</summary>
    Conflict,
    StorageFailed
}

/// <summary>What a restart did to requests a previous process left unfinished.</summary>
public sealed record AdvisoryAiRecovery(int ReleasedUnclaimed, int MarkedUncertain);

/// <summary>ADR-029 Q7 and "Reset, restore and missing accounting continuity": the ledger-wide accounting state.</summary>
public sealed record AdvisoryAiAccountingState(
    AdvisoryAiContinuity Continuity,
    DateTimeOffset? LastAcceptedUtc,
    AdvisoryAiPeriod? UnknownPriorUseSince,
    bool ReviewBlock);

/// <summary>Spending for one UTC month and the separate first-live-test envelope, for the AI limits page. All amounts in micro-USD.</summary>
public sealed record AdvisoryAiSpendingSummary(
    AdvisoryAiPeriod Period,
    MicroUsd Settled,
    MicroUsd Outstanding,
    MicroUsd UncertainInEarlierMonths,
    MicroUsd TestSettled,
    MicroUsd TestOutstanding,
    int DetailedRows,
    bool ReviewBlock);

public enum AdvisoryAiAccountingWrite
{
    Done,

    /// <summary>The change does not apply to the current state (for example confirming a month that has not started).</summary>
    NotApplicable,
    StorageFailed
}

/// <summary>
/// FR-BUD-01/02/03 (ADR-029, ADR-015): the transactional spending ledger. Every write is one SQLite transaction; a reservation reads
/// the commitments and inserts its row in the same serialized write transaction, so two processes can never both fit under a cap.
/// Rows read back are untrusted and never grant send authority: only a fresh in-memory approval can send.
/// </summary>
public interface IAdvisoryAiLedger
{
    /// <summary>Soft ceiling on detailed rows (ADR-029): reaching it blocks new requests instead of deleting any commitment.</summary>
    public const int MaximumDetailedRows = 10_000;

    Task<AdvisoryAiReserveResult> ReserveAsync(AdvisoryAiReservationRequest request, AdvisoryAiLimits limits, AdvisoryAiPaidRequestMode mode,
        AdvisoryAiTariff tariff, AdvisoryAiModelProfile model, CancellationToken cancellationToken);

    /// <summary>Reserved → DispatchClaimed: the one durable claim to make the one network attempt.</summary>
    Task<AdvisoryAiLedgerWrite> ClaimDispatchAsync(Guid requestId, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    /// <summary>Reserved → NotSent, releasing the reservation. Fails once a claim exists: a claimed request is never released.</summary>
    Task<AdvisoryAiLedgerWrite> ReleaseUnclaimedAsync(Guid requestId, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    /// <summary>
    /// DispatchClaimed → Settled or Uncertain. A settlement above the reservation, a duplicate provider response ID or a conflicting
    /// repeat sets the review block. An identical repeat is <see cref="AdvisoryAiLedgerWrite.AlreadyDone"/>.
    /// </summary>
    Task<AdvisoryAiLedgerWrite> SettleAsync(Guid requestId, AdvisoryAiSettlement settlement, AdvisoryAiUsage? usage, string? providerResponseId,
        DateTimeOffset nowUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Run under the exclusive operation gate before any new request: Reserved rows (no claim was ever committed) become NotSent and
    /// DispatchClaimed rows become Uncertain. Nothing is resent.
    /// </summary>
    Task<AdvisoryAiRecovery> RecoverInterruptedAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken);

    Task<AdvisoryAiAccountingState> GetAccountingStateAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The owner's answer to "has PC Steward made AI requests on this PC before?", recorded once: <see cref="AdvisoryAiContinuity.DeclaredNoPriorUse"/>
    /// or <see cref="AdvisoryAiContinuity.PriorUseUnknown"/> (yes or unsure). It also accepts the current time as the accounting start.
    /// </summary>
    Task<AdvisoryAiAccountingWrite> DeclareContinuityAsync(AdvisoryAiContinuity answer, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    /// <summary>
    /// The owner confirms a later UTC month. When prior use was unknown since an earlier month, this starts a fresh period for new
    /// requests only (<see cref="AdvisoryAiContinuity.FreshPeriodConfirmed"/>); nothing earlier is refunded or reopened.
    /// </summary>
    Task<AdvisoryAiAccountingWrite> ConfirmNewPeriodAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken);

    /// <summary>
    /// The owner reviewed a clock that moved backwards inside the same UTC month and accepts the current time. Refused when the clock
    /// is now in an earlier month: an old period is never reopened.
    /// </summary>
    Task<AdvisoryAiAccountingWrite> AcceptClockAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken);

    /// <summary>The owner reviewed a recorded breach. The recorded amounts stay exactly as they are.</summary>
    Task<AdvisoryAiAccountingWrite> AcknowledgeReviewAsync(CancellationToken cancellationToken);

    Task<AdvisoryAiSpendingSummary> GetSummaryAsync(AdvisoryAiPeriod period, CancellationToken cancellationToken);
}

/// <summary>FR-AI-05 (ADR-028 D4, ADR-027 F5): one saved explanation. Only the redacted answer is saved: never the prompt or evidence.</summary>
public sealed record AdvisoryAiSavedExplanation(
    Guid Id,
    Guid RequestId,
    AdvisoryAiSourceKind Source,
    string OriginId,
    string ModelId,
    AdvisoryAiAnswerStatus Status,
    AdvisoryAiIncompleteReason IncompleteReason,
    string? Text,
    bool TextTruncated,
    DateTimeOffset CreatedAtUtc)
{
    public static TimeSpan Retention => AdvisoryAiResourcePolicy.SavedExplanationRetention;

    public DateTimeOffset ExpiresAtUtc => CreatedAtUtc + Retention;
}

public enum AdvisoryAiExplanationSave
{
    Saved,
    TooLarge,
    Invalid,
    StorageFailed
}

/// <summary>
/// FR-AI-05 (ADR-028 D4 and the 4 MiB / 8 MiB bounds; ADR-027 F5): saved explanations, kept 30 days. Clear scan data removes the
/// diagnostic-timeline kind and Clear update history removes the failed-update kind; factory reset removes both. Removing an
/// explanation never touches the spending ledger.
/// </summary>
public interface IAdvisoryAiExplanationStore
{
    /// <summary>Saves one explanation, removing expired ones and then the oldest others until the total fits 8 MiB.</summary>
    Task<AdvisoryAiExplanationSave> SaveExplanationAsync(AdvisoryAiSavedExplanation explanation, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    /// <summary>Unexpired explanations for one origin, newest first. Rows that fail validation are left out.</summary>
    Task<IReadOnlyList<AdvisoryAiSavedExplanation>> ListExplanationsAsync(AdvisoryAiSourceKind source, string originId, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    Task<bool> DeleteExplanationAsync(Guid id, CancellationToken cancellationToken);
}
