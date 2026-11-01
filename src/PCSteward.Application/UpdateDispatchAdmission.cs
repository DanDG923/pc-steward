using System.Globalization;
using System.Text;
using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>Dispatch-time observation from trusted preparation, separate from the frozen review. Data alone grants no authority.</summary>
public sealed class FreshUpdateDispatchEvidence
{
    public FreshUpdateDispatchEvidence(ReviewedUpdateSelection selection, string requestingUserSid, string nativeInstanceId,
        string installationContextId, string evidenceId, DateTimeOffset observedAt, UpdateRouteContract route,
        ReviewedInstallerMode mode, bool actionable, bool sourceIsFresh)
    {
        Selection = selection ?? throw new ArgumentNullException(nameof(selection));
        RequestingUserSid = Identity(requestingUserSid);
        NativeInstanceId = Identity(nativeInstanceId);
        InstallationContextId = Identity(installationContextId);
        EvidenceId = Identity(evidenceId);
        if (observedAt == default || !Enum.IsDefined(route)) throw new ArgumentException("Valid dispatch observation required.");
        ObservedAt = observedAt.ToUniversalTime();
        Route = route;
        Mode = mode ?? throw new ArgumentNullException(nameof(mode));
        Actionable = actionable;
        SourceIsFresh = sourceIsFresh;
    }

    public ReviewedUpdateSelection Selection { get; }
    public string RequestingUserSid { get; }
    public string NativeInstanceId { get; }
    public string InstallationContextId { get; }
    public string EvidenceId { get; }
    public DateTimeOffset ObservedAt { get; }
    public UpdateRouteContract Route { get; }
    public ReviewedInstallerMode Mode { get; }
    public bool Actionable { get; }
    public bool SourceIsFresh { get; }

    public bool Matches(ReviewedUpdateOperation operation, DateTimeOffset now) => operation is not null &&
        Actionable && SourceIsFresh && ObservedAt >= operation.ReviewedAt && ObservedAt <= now && now - ObservedAt <= TimeSpan.FromMinutes(2) &&
        now >= operation.ReviewedAt && now - operation.ReviewedAt <= TimeSpan.FromMinutes(30) &&
        RequestingUserSid == operation.RequestingUserSid && NativeInstanceId == operation.NativeInstanceId &&
        InstallationContextId == operation.InstallationContextId && Selection.EqualsEvidence(operation.Selection) &&
        Route == operation.RouteContract && Mode == operation.Mode && ReviewedUpdateReview.HasSupportedMode(operation);

    public byte[] GetCanonicalBytes() => Encoding.UTF8.GetBytes(string.Join('\n', "pc-steward-dispatch-evidence:2",
        Selection.StableProductKey, Selection.Provider.ToString(), Selection.Source.ToString(), Selection.PackageId,
        Selection.InstalledVersion, Selection.CandidateVersion, Selection.Scope.ToString(), Selection.Architecture.Binding.ToString(), Selection.Architecture.Value?.ToString() ?? "None",
        Selection.Channel.ToString(), Selection.MappingEvidenceVersion, RequestingUserSid, NativeInstanceId, InstallationContextId, EvidenceId,
        ObservedAt.ToString("O", CultureInfo.InvariantCulture), Route.ToString(), Mode.Binding.ToString(),
        Mode.Mode?.ToString() ?? "None", Mode.Reason?.ToString() ?? "None", Actionable ? "True" : "False", SourceIsFresh ? "True" : "False"));

    private static string Identity(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 256 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or ':' or '_' or '-')))
            throw new ArgumentException("Bounded canonical evidence identity required.");
        return value;
    }
}

public enum UpdateDispatchState { DispatchClaimed, Final, Uncertain }
/// <summary>
/// Append only. <see cref="Unverified"/>: owned work positively settled but post-verification proved neither target nor
/// restart state; never Success. <see cref="ElevationDenied"/>: the user declined UAC (AC-UPD-05, distinct from failure).
/// <see cref="Refused"/>: the helper/runner refused before updating (for example, exact-candidate qualification mismatch).
/// </summary>
public enum UpdateDispatchOutcome { Success, RestartRequired, Failed, Cancelled, Uncertain, Unverified, ElevationDenied, Refused }

/// <summary>Passive committed identity. Restoring or constructing a receipt never produces a dispatch permit.</summary>
public sealed record UpdateDispatchClaim(Guid ClaimId, Guid TerminalEventId, ReviewedUpdateDraft Draft,
    FreshUpdateDispatchEvidence Evidence, DateTimeOffset ClaimedAt);

public interface IUpdateDispatchStore
{
    Task<UpdateDispatchClaim?> TryClaimAsync(ReviewedUpdateDraft draft, FreshUpdateDispatchEvidence evidence,
        OperationOwnership ownership, DateTimeOffset claimedAt, CancellationToken cancellationToken);
    Task RecordFinalAsync(UpdateDispatchClaim claim, OperationOwnership ownership, UpdateDispatchTerminal terminal, UpdateDispatchSettlement settlement, CancellationToken cancellationToken);
    Task<int> RecoverUnfinishedDispatchesAsync(OperationOwnership ownership, DateTimeOffset now, CancellationToken cancellationToken);
    Task<UpdateDispatchState?> GetDispatchStateAsync(Guid jobId, CancellationToken cancellationToken);
    Task<bool> HasUnresolvedDispatchesAsync(OperationOwnership ownership, CancellationToken cancellationToken);
}

public sealed class UpdateDispatchAdmission
{
    private readonly IUpdateDispatchStore store;
    private readonly TimeProvider clock;
    public UpdateDispatchAdmission(IUpdateDispatchStore store, TimeProvider? clock = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.clock = clock ?? TimeProvider.System;
    }
    public async Task<UpdateDispatchPermit?> TryClaimAsync(LiveApprovedUpdate approval, FreshUpdateDispatchEvidence evidence,
        OperationOwnership ownership, CancellationToken cancellationToken)
    {
        UpdateDispatchAdmissionResult result = await ClaimAsync(approval, evidence, ownership, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return result.Permit;
    }

    /// <summary>
    /// Like <see cref="TryClaimAsync"/>, but also returns a claim that committed without a permit (post-commit expiry,
    /// stale evidence, lost ownership or cancellation) so the caller can finalize it as never invoked instead of leaving
    /// a false "invocation uncertain" barrier. A committed claim without a permit is never dispatch authority.
    /// </summary>
    public async Task<UpdateDispatchAdmissionResult> ClaimAsync(LiveApprovedUpdate approval, FreshUpdateDispatchEvidence evidence,
        OperationOwnership ownership, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(ownership);
        cancellationToken.ThrowIfCancellationRequested();
        if (!approval.TryConsumeClaimAttempt(approval.Operation, ownership) || !evidence.Matches(approval.Operation, clock.GetUtcNow())) return new(null, null);
        UpdateDispatchClaim? claim = await store.TryClaimAsync(approval.Draft, evidence, ownership, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        if (claim is null) return new(null, null);
        // A claim that differs from the reviewed identity is never finalized or used here; its committed row stays a barrier.
        if (claim.ClaimId == Guid.Empty || claim.TerminalEventId == Guid.Empty ||
            !approval.Draft.HasSameDraftIdentity(claim.Draft) || !ReferenceEquals(claim.Evidence, evidence)) return new(null, null);
        if (cancellationToken.IsCancellationRequested || !approval.IsCurrent || !ownership.CanDispatchFor(approval.Operation) ||
            !evidence.Matches(approval.Operation, clock.GetUtcNow())) return new(null, claim);
        return new(new(claim, approval, ownership, clock), claim);
    }
}

/// <summary>Admission outcome. <see cref="CommittedClaim"/> without <see cref="Permit"/> is a barrier to finalize as never invoked, never authority.</summary>
public sealed record UpdateDispatchAdmissionResult(UpdateDispatchPermit? Permit, UpdateDispatchClaim? CommittedClaim);

/// <summary>One-use, process-local dispatch authorization; no public construction or restore path.</summary>
public sealed class UpdateDispatchPermit
{
    private readonly LiveApprovedUpdate approval;
    private readonly OperationOwnership ownership;
    private readonly TimeProvider clock;
    private int consumed;
    internal UpdateDispatchPermit(UpdateDispatchClaim claim, LiveApprovedUpdate approval, OperationOwnership ownership, TimeProvider clock)
    {
        Claim = claim;
        this.approval = approval;
        this.ownership = ownership;
        this.clock = clock;
    }
    public UpdateDispatchClaim Claim { get; }
    public bool TryConsume(LiveApprovedUpdate originalApproval, OperationOwnership originalOwnership) =>
        Interlocked.Exchange(ref consumed, 1) == 0 && ReferenceEquals(approval, originalApproval) && ReferenceEquals(ownership, originalOwnership) &&
        approval.IsCurrent && ownership.CanDispatchFor(approval.Operation) && Claim.Evidence.Matches(approval.Operation, clock.GetUtcNow());
}
