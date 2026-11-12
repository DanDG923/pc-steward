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
    /// The compiled route admission (the single review-gate admission point). The first term is the reviewed WinGet rule
    /// (<see cref="UpdateRouteContract.WinGetUserExactV3"/>), unchanged in meaning: stage C4 states its <c>winget</c> source
    /// explicitly, because the compiled mode table now also gives <c>msstore</c> user scope a mode (for the C4 route only). The second term is the Task 50 activation (ADR-031
    /// owner decision 7, step C-A; gate inventory in <c>docs/review/TASK_50_ACTIVATION_CHANGE.md</c>): the exact shapes of
    /// <see cref="UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3"/> and
    /// <see cref="UpdateRouteContract.ChocolateyClientSelfUpdateV1"/> (<see cref="ReviewedChocolateyRouteAdmission"/>). Admission
    /// is shape only: it never qualifies a candidate, and the fresh evidence, the deployment capability, the dispatcher and the
    /// elevated helper's own checks still decide whether anything runs. Phase D stage D4b, stage 3 (design note §10): the last term
    /// admits the whole-PC WinGet route (<see cref="UpdateRouteContract.WinGetMachineSilentV1"/>) by its exact shape
    /// (<see cref="WingetMachineReviewedRoute.TryGetShape"/>), in the same change that retires helper protocol 1.
    /// </summary>
    internal static bool HasSupportedMode(ReviewedUpdateOperation operation) =>
        operation.HasCurrentSchema && operation.RouteContract == UpdateRouteContract.WinGetUserExactV3 &&
        operation.Selection.Source == UpdateSource.WinGet &&
        operation.Selection.Architecture.Binding == ReviewedArchitectureBinding.Observed &&
        operation.Selection.Architecture.Value is UpdateArchitecture.X86 or UpdateArchitecture.X64 or UpdateArchitecture.Arm64 &&
        UpdateInstallerModeCapabilities.Resolve(operation.Selection.Provider, operation.Selection.Source, operation.Selection.Scope) == operation.Mode ||
        HasWinGetUserSilentMode(operation) ||
        ReviewedChocolateyRouteAdmission.Admits(operation) ||
        WingetMachineReviewedRoute.TryGetShape(operation, out _, out _, out _);

    /// <summary>
    /// Stage C4 (Task 54 R1, owner decision 2026-11-12): the per-user WinGet route <see cref="UpdateRouteContract.WinGetUserSilentV1"/>
    /// is admitted when its frozen mode is the compiled one for its selection (Silent, for WinGet user scope from <c>winget</c> or
    /// <c>msstore</c>) and its architecture is observed. Shape only, like the other terms.
    /// </summary>
    private static bool HasWinGetUserSilentMode(ReviewedUpdateOperation operation) =>
        operation.HasCurrentSchema && operation.RouteContract == UpdateRouteContract.WinGetUserSilentV1 &&
        operation.Selection.Provider == UpdateProvider.WinGet && operation.Selection.Scope == UpdateScope.User &&
        operation.Selection.Architecture.Binding == ReviewedArchitectureBinding.Observed &&
        operation.Mode == ReviewedInstallerMode.Selected(InstallerMode.Silent) &&
        UpdateInstallerModeCapabilities.Resolve(operation.Selection.Provider, operation.Selection.Source, operation.Selection.Scope) == operation.Mode;
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
