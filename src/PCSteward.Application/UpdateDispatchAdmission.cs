using System.Globalization;
using System.Text;
using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>Dispatch-time observation from trusted preparation, separate from the frozen review. Data alone grants no authority.</summary>
public sealed class FreshUpdateDispatchEvidence
{
    public FreshUpdateDispatchEvidence(UpdateJobSelection selection, string requestingUserSid, string nativeInstanceId,
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

    public UpdateJobSelection Selection { get; }
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

    public byte[] GetCanonicalBytes() => Encoding.UTF8.GetBytes(string.Join('\n', "pc-steward-dispatch-evidence:1",
        Selection.StableProductKey, Selection.Provider.ToString(), Selection.Source.ToString(), Selection.PackageId,
        Selection.InstalledVersion, Selection.CandidateVersion, Selection.Scope.ToString(), Selection.Architecture.ToString(),
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
public enum UpdateDispatchOutcome { Success, RestartRequired, Failed, Cancelled, Uncertain }

public sealed class UpdateDispatchTerminal
{
    public const int ReservedBytes = 16 * 1024;
    public UpdateDispatchTerminal(UpdateDispatchOutcome outcome, DateTimeOffset occurredAt, int? exitCode, string diagnostic)
    {
        if (!Enum.IsDefined(outcome) || occurredAt == default || diagnostic is null || diagnostic.Length > 4096)
            throw new ArgumentException("A bounded terminal outcome is required.");
        Outcome = outcome;
        OccurredAt = occurredAt.ToUniversalTime();
        ExitCode = exitCode;
        Diagnostic = SanitizedDiagnosticBundle.Sanitize(diagnostic);
        if (Diagnostic.Length > 4096) throw new ArgumentException("Sanitized diagnostic exceeds the terminal reservation.");
    }
    public UpdateDispatchOutcome Outcome { get; }
    public DateTimeOffset OccurredAt { get; }
    public int? ExitCode { get; }
    public string Diagnostic { get; }
    public byte[] GetReservedPayload()
    {
        byte[] diagnostic = new UTF8Encoding(false, true).GetBytes(Diagnostic);
        byte[] header = Encoding.ASCII.GetBytes(string.Join('\n', "pc-steward-update-terminal:1", Outcome.ToString(),
            OccurredAt.ToString("O", CultureInfo.InvariantCulture), ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "None",
            diagnostic.Length.ToString(CultureInfo.InvariantCulture)) + "\n");
        if (header.Length + diagnostic.Length > ReservedBytes) throw new InvalidOperationException("Terminal representation exceeds its reservation.");
        byte[] reserved = new byte[ReservedBytes];
        header.CopyTo(reserved, 0);
        diagnostic.CopyTo(reserved, header.Length);
        return reserved;
    }
}

/// <summary>Passive committed identity. Restoring or constructing a receipt never produces a dispatch permit.</summary>
public sealed record UpdateDispatchClaim(Guid ClaimId, Guid TerminalEventId, ReviewedUpdateDraft Draft,
    FreshUpdateDispatchEvidence Evidence, DateTimeOffset ClaimedAt);

public interface IUpdateDispatchStore
{
    Task<UpdateDispatchClaim?> TryClaimAsync(ReviewedUpdateDraft draft, FreshUpdateDispatchEvidence evidence,
        OperationOwnership ownership, DateTimeOffset claimedAt, CancellationToken cancellationToken);
    Task RecordFinalAsync(UpdateDispatchClaim claim, OperationOwnership ownership, UpdateDispatchTerminal terminal, CancellationToken cancellationToken);
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
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(ownership);
        cancellationToken.ThrowIfCancellationRequested();
        if (!approval.TryConsumeClaimAttempt(approval.Operation, ownership) || !evidence.Matches(approval.Operation, clock.GetUtcNow())) return null;
        UpdateDispatchClaim? claim = await store.TryClaimAsync(approval.Draft, evidence, ownership, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (claim is null || claim.ClaimId == Guid.Empty || claim.TerminalEventId == Guid.Empty ||
            !approval.Draft.HasSameDraftIdentity(claim.Draft) || !ReferenceEquals(claim.Evidence, evidence) ||
            !approval.IsCurrent || !ownership.CanDispatchFor(approval.Operation) || !evidence.Matches(approval.Operation, clock.GetUtcNow())) return null;
        return new(claim, approval, ownership, clock);
    }
}

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
