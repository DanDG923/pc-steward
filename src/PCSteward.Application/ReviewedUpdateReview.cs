using PCSteward.Domain;

namespace PCSteward.Application;

public sealed class ReviewedUpdateReview
{
    private readonly object sync = new();
    private readonly ReviewedUpdateOperation operation;
    private readonly ReviewedUpdateDraft draft;
    private readonly ReviewedUpdateLifetime lifetime;
    private bool confirmed;

    public ReviewedUpdateReview(ReviewedUpdateDraft draft, TimeProvider? clock = null, CancellationToken cancellationToken = default)
    {
        this.draft = draft ?? throw new ArgumentNullException(nameof(draft));
        operation = draft.Operation;
        lifetime = new(operation.ReviewedAt, clock ?? TimeProvider.System, cancellationToken);
    }

    public LiveApprovedUpdate Confirm()
    {
        lock (sync)
        {
            if (confirmed || !lifetime.IsCurrent || !HasSupportedMode(operation))
                throw new InvalidOperationException("Review is expired, cancelled, already confirmed or its mode/route is unavailable.");
            confirmed = true;
            return new(draft, lifetime);
        }
    }

    /// <summary>
    /// The compiled route admission: only <see cref="UpdateRouteContract.WinGetUserExactV3"/>. The trailing term is the
    /// ADR-031 design §8.6 gate-1 test seam (<see cref="ReviewedRouteTestAdmission"/>), always false in production because
    /// no <c>src</c> code can enter its scope (architecture test).
    /// </summary>
    internal static bool HasSupportedMode(ReviewedUpdateOperation operation) =>
        operation.HasCurrentSchema && operation.RouteContract == UpdateRouteContract.WinGetUserExactV3 &&
        operation.Selection.Architecture.Binding == ReviewedArchitectureBinding.Observed &&
        operation.Selection.Architecture.Value is UpdateArchitecture.X86 or UpdateArchitecture.X64 or UpdateArchitecture.Arm64 &&
        UpdateInstallerModeCapabilities.Resolve(operation.Selection.Provider, operation.Selection.Source, operation.Selection.Scope) == operation.Mode ||
        ReviewedRouteTestAdmission.Admits(operation);
}

/// <summary>Explicit process-local confirmation. It is not a persisted job or a dispatch permit.</summary>
public sealed class LiveApprovedUpdate
{
    private readonly object sync = new();
    private readonly ReviewedUpdateLifetime lifetime;
    private bool claimAttempted;

    internal LiveApprovedUpdate(ReviewedUpdateDraft draft, ReviewedUpdateLifetime lifetime)
    {
        Draft = draft;
        this.lifetime = lifetime;
    }

    public ReviewedUpdateDraft Draft { get; }
    public ReviewedUpdateOperation Operation => Draft.Operation;
    public bool IsCurrent => lifetime.IsCurrent;

    internal bool TryConsumeClaimAttempt(ReviewedUpdateOperation original, OperationOwnership ownership)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(ownership);
        lock (sync)
        {
            if (claimAttempted || !ReferenceEquals(original, Operation) || !IsCurrent ||
                !ownership.CanDispatchFor(Operation) || !ReviewedUpdateReview.HasSupportedMode(Operation)) return false;
            claimAttempted = true;
            return true;
        }
    }
}

internal sealed class ReviewedUpdateLifetime
{
    private static readonly TimeSpan MaximumReviewAge = TimeSpan.FromMinutes(30);
    private readonly object sync = new();
    private readonly DateTimeOffset reviewedAt;
    private readonly TimeProvider clock;
    private readonly CancellationToken cancellationToken;
    private readonly long started;
    private readonly TimeSpan initialAge;
    private DateTimeOffset lastUtc;
    private long lastTimestamp;
    private bool invalid;

    internal ReviewedUpdateLifetime(DateTimeOffset reviewedAt, TimeProvider clock, CancellationToken cancellationToken)
    {
        this.reviewedAt = reviewedAt;
        this.clock = clock;
        this.cancellationToken = cancellationToken;
        started = clock.GetTimestamp();
        lastTimestamp = started;
        lastUtc = clock.GetUtcNow();
        initialAge = lastUtc - reviewedAt;
        invalid = initialAge < TimeSpan.Zero || initialAge > MaximumReviewAge;
    }

    internal bool IsCurrent
    {
        get
        {
            lock (sync)
            {
                if (invalid || cancellationToken.IsCancellationRequested) return false;
                DateTimeOffset now = clock.GetUtcNow();
                long timestamp = clock.GetTimestamp();
                TimeSpan monotonicAge = initialAge + clock.GetElapsedTime(started, timestamp);
                TimeSpan utcAge = now - reviewedAt;
                invalid = now < lastUtc || timestamp < lastTimestamp || monotonicAge < TimeSpan.Zero ||
                    utcAge < TimeSpan.Zero || monotonicAge > MaximumReviewAge || utcAge > MaximumReviewAge;
                lastUtc = now;
                lastTimestamp = timestamp;
                return !invalid;
            }
        }
    }
}
