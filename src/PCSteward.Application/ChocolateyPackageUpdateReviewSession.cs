using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>
/// Checks one Chocolatey Community package for a review: the user-cache candidate is resolved inside the call and a fresh
/// Desktop-role observation of the installed version follows (N5 obligation (a)). Null on any refusal, fault or cancellation.
/// </summary>
public delegate Task<ChocolateyReviewIntentFacts?> ChocolateyPackageReviewIntentSource(string packageId, CancellationToken cancellationToken);

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, TM-03/04/07/13; Chocolatey package Confirm addendum §3 and §5: the one-use review session of one
/// Chocolatey Community package update on <see cref="UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3"/>. It is the only
/// <c>src</c> caller of <see cref="ReviewedUpdateReview.Confirm"/> for that route and the only caller of its workflow
/// (architecture-pinned). It shares the client session's stage, refusal, result and progress types; nothing else is shared.
/// <list type="bullet">
/// <item><see cref="PrepareAsync"/> checks the selected package now (the user-cache candidate and a fresh observation) and builds the
/// frozen operation only from that checked intent, whose package ID must equal the selected one exactly: no scan value becomes a
/// version or an argument. It saves the draft and starts the 30-minute review lifetime.</item>
/// <item><see cref="ConfirmAsync"/> re-checks on the press: owner admission (C1), the check's age (C2, 10 minutes), the split token
/// (C4), earlier Chocolatey updates needing attention (C6) and the review's own lifetime (C8). The gate (C7), the deployment
/// capability (C5), F′, the claim, the one-use permit, the helper's stages (stage C confirms the exact candidate) and the verifier
/// run inside the workflow. One accepted press gives at most one workflow run.</item>
/// </list>
/// The run is started off the UI thread and is not cancellable from the screen: closing the dialog cannot stop an elevated run
/// that has started, so the run always reaches its recorded result.
/// </summary>
public sealed class ChocolateyPackageUpdateReviewSession
{
    /// <summary>The check may be at most this old when Confirm is pressed (owner decision 1, unchanged).</summary>
    public static TimeSpan MaximumCheckAge => ChocolateyClientUpdateReviewSession.MaximumCheckAge;

    /// <summary>The review lifetime, for the expiry line only.</summary>
    public static TimeSpan ReviewLifetime => ChocolateyClientUpdateReviewSession.ReviewLifetime;

    /// <summary>Fixed identities of a package registration (the key and instance carry the checked package ID).</summary>
    internal const string StableProductKeyPrefix = "choco:";
    internal const string InstallationContextId = "machine:chocolatey";
    internal const string MappingEvidenceVersion = "chocolatey-package-v1";

    private readonly object sync = new();
    private readonly ChocolateyPackageReviewIntentSource checkIntent;
    private readonly Func<ReviewedUpdateOperation, CancellationToken, Task<ReviewedUpdateDraft>> saveDraft;
    private readonly Func<CancellationToken, Task<IReadOnlyList<ReviewedUpdateHistoryRecord>?>> readHistory;
    private readonly ReviewedUpdateDispatchWorkflow workflow;
    private readonly ChocolateyClientUpdatePhaseSignal phases;
    private readonly Func<ReviewedRouteSplitTokenState> probeSplitToken;
    private readonly TimeProvider clock;
    private readonly bool admitted;
    private ChocolateyClientUpdateReviewStage stage = ChocolateyClientUpdateReviewStage.NotPrepared;
    private ReviewedUpdateReview? review;
    private ReviewedUpdateOperation? operation;
    private ChocolateyReviewIntentFacts? intent;
    private DateTimeOffset? checkStartedAt;

    public ChocolateyPackageUpdateReviewSession(ChocolateyPackageReviewIntentSource checkIntent,
        Func<ReviewedUpdateOperation, CancellationToken, Task<ReviewedUpdateDraft>> saveDraft,
        Func<CancellationToken, Task<IReadOnlyList<ReviewedUpdateHistoryRecord>?>> readHistory, ReviewedUpdateDispatchWorkflow workflow,
        ChocolateyClientUpdatePhaseSignal phases, Func<ReviewedRouteSplitTokenState> probeSplitToken, TimeProvider? clock = null)
        : this(ChocolateyPackageUpdateAdmission.IsOwnerApproved, checkIntent, saveDraft, readHistory, workflow, phases, probeSplitToken, clock)
    {
    }

    /// <summary>
    /// Tests only: a session with the admission given, so the admitted path stays under test whichever value is compiled in.
    /// Production code never calls it (architecture-pinned); every production session reads the compiled C1.
    /// </summary>
    internal static ChocolateyPackageUpdateReviewSession CreateWithAdmissionForTests(bool admitted, ChocolateyPackageReviewIntentSource checkIntent,
        Func<ReviewedUpdateOperation, CancellationToken, Task<ReviewedUpdateDraft>> saveDraft,
        Func<CancellationToken, Task<IReadOnlyList<ReviewedUpdateHistoryRecord>?>> readHistory, ReviewedUpdateDispatchWorkflow workflow,
        ChocolateyClientUpdatePhaseSignal phases, Func<ReviewedRouteSplitTokenState> probeSplitToken, TimeProvider? clock = null) =>
        new(admitted, checkIntent, saveDraft, readHistory, workflow, phases, probeSplitToken, clock);

    private ChocolateyPackageUpdateReviewSession(bool admitted, ChocolateyPackageReviewIntentSource checkIntent,
        Func<ReviewedUpdateOperation, CancellationToken, Task<ReviewedUpdateDraft>> saveDraft,
        Func<CancellationToken, Task<IReadOnlyList<ReviewedUpdateHistoryRecord>?>> readHistory, ReviewedUpdateDispatchWorkflow workflow,
        ChocolateyClientUpdatePhaseSignal phases, Func<ReviewedRouteSplitTokenState> probeSplitToken, TimeProvider? clock)
    {
        this.admitted = admitted;
        this.checkIntent = checkIntent ?? throw new ArgumentNullException(nameof(checkIntent));
        this.saveDraft = saveDraft ?? throw new ArgumentNullException(nameof(saveDraft));
        this.readHistory = readHistory ?? throw new ArgumentNullException(nameof(readHistory));
        this.workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        this.phases = phases ?? throw new ArgumentNullException(nameof(phases));
        this.probeSplitToken = probeSplitToken ?? throw new ArgumentNullException(nameof(probeSplitToken));
        this.clock = clock ?? TimeProvider.System;
    }

    public ChocolateyClientUpdateReviewStage Stage { get { lock (sync) return stage; } }

    /// <summary>The frozen operation once prepared; display only (title, versions, expiry).</summary>
    public ReviewedUpdateOperation? Operation { get { lock (sync) return operation; } }

    /// <summary>The checked intent the operation was frozen from; display only (the Profile V2 disclosure).</summary>
    public ChocolateyReviewIntentFacts? Intent { get { lock (sync) return intent; } }

    /// <summary>When this session's check started (C2 and C6 are measured from it); null before it is prepared.</summary>
    public DateTimeOffset? CheckStartedAt { get { lock (sync) return checkStartedAt; } }

    /// <summary>When the prepared review expires (its 30-minute lifetime); null before it is prepared.</summary>
    public DateTimeOffset? ExpiresAt { get { lock (sync) return operation is null ? null : operation.ReviewedAt + ReviewLifetime; } }

    /// <summary>True while a prepared, unconfirmed review is inside its lifetime by the wall clock (display only; Confirm re-checks).</summary>
    public bool IsReviewLive
    {
        get
        {
            lock (sync)
            {
                if (stage != ChocolateyClientUpdateReviewStage.Ready || operation is null) return false;
                TimeSpan age = clock.GetUtcNow() - operation.ReviewedAt;
                return age >= TimeSpan.Zero && age < ReviewLifetime;
            }
        }
    }

    /// <summary>C2: true while this session's check is less than <see cref="MaximumCheckAge"/> old (and not in the future).</summary>
    public bool IsCheckFresh
    {
        get
        {
            DateTimeOffset? started = CheckStartedAt;
            if (started is not { } at) return false;
            TimeSpan age = clock.GetUtcNow() - at;
            return age >= TimeSpan.Zero && age < MaximumCheckAge;
        }
    }

    /// <summary>
    /// Prepares the one review: checks <paramref name="packageId"/> now, freezes the package operation from the checked intent,
    /// saves the draft and starts the review lifetime. Only once, only while owner admission is compiled in, and never for a
    /// Chocolatey client identity; any refusal, mismatch, fault or cancellation is <see cref="ChocolateyClientUpdateReviewStage.PrepareFailed"/>,
    /// and nothing was started.
    /// </summary>
    public async Task<ChocolateyClientUpdateReviewStage> PrepareAsync(string packageId, CancellationToken cancellationToken)
    {
        DateTimeOffset started;
        lock (sync)
        {
            if (stage != ChocolateyClientUpdateReviewStage.NotPrepared) return stage;
            stage = ChocolateyClientUpdateReviewStage.Preparing;
            started = clock.GetUtcNow();
            checkStartedAt = started;
        }
        ReviewedUpdateReview? prepared = null;
        ReviewedUpdateOperation? frozen = null;
        ChocolateyReviewIntentFacts? checkedIntent = null;
        try
        {
            if (admitted && IsSelectablePackage(packageId))
            {
                ChocolateyReviewIntentFacts? facts = await checkIntent(packageId, cancellationToken).ConfigureAwait(false);
                if (!cancellationToken.IsCancellationRequested && facts is not null && IsThisCheck(facts, packageId, started) &&
                    Freeze(facts) is { } candidate)
                {
                    ReviewedUpdateDraft? draft = await saveDraft(candidate, cancellationToken).ConfigureAwait(false);
                    // The review runs on exactly the operation that was saved, which must be the one frozen here.
                    if (draft is not null && !cancellationToken.IsCancellationRequested && SameOperation(draft.Operation, candidate))
                    {
                        frozen = draft.Operation;
                        checkedIntent = facts;
                        prepared = new ReviewedUpdateReview(draft, clock);
                    }
                }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            prepared = null;
        }
        lock (sync)
        {
            if (prepared is null || frozen is null || checkedIntent is null)
            {
                stage = ChocolateyClientUpdateReviewStage.PrepareFailed;
                return stage;
            }
            review = prepared;
            operation = frozen;
            intent = checkedIntent;
            stage = ChocolateyClientUpdateReviewStage.Ready;
            return stage;
        }
    }

    /// <summary>
    /// One Confirm press. Re-checks C1, C2, C4, C6 and C8; then confirms the review (once) and runs the workflow once, off the UI
    /// thread and to its recorded result. Any refusal spends the session before anything is claimed. Never throws for a refusal.
    /// </summary>
    public async Task<ChocolateyClientUpdateRunResult> ConfirmAsync(IProgress<ChocolateyClientUpdatePhase>? progress, CancellationToken cancellationToken)
    {
        ReviewedUpdateReview current;
        DateTimeOffset started;
        lock (sync)
        {
            if (stage != ChocolateyClientUpdateReviewStage.Ready || review is null || checkStartedAt is not { } at)
                return new(ChocolateyClientUpdateRefusal.NotReady, null);
            // Claimed for this press: a second press while the checks below run is refused.
            stage = ChocolateyClientUpdateReviewStage.Running;
            current = review;
            started = at;
        }
        ChocolateyClientUpdateRefusal refusal = await PressRefusalAsync(started, cancellationToken).ConfigureAwait(false);
        LiveApprovedUpdate? approval = null;
        if (refusal == ChocolateyClientUpdateRefusal.None)
        {
            try { approval = current.Confirm(); }
            catch (InvalidOperationException) { refusal = ChocolateyClientUpdateRefusal.ReviewExpired; }
        }
        if (approval is null)
        {
            lock (sync) stage = ChocolateyClientUpdateReviewStage.Refused;
            return new(refusal, null);
        }
        phases.Attach(progress);
        try
        {
            phases.Report(ChocolateyClientUpdatePhase.CheckingAgain);
            // The workflow may do bounded synchronous I/O, so it runs off the caller's (UI) thread. It is never cancelled from the
            // screen: an elevated run that has started always reaches its recorded result.
            ReviewedUpdateDispatchResult result = await Task.Run(() => workflow.RunAsync(approval, CancellationToken.None), CancellationToken.None)
                .ConfigureAwait(false);
            return new(ChocolateyClientUpdateRefusal.None, result);
        }
        finally
        {
            phases.Attach(null);
            lock (sync) stage = ChocolateyClientUpdateReviewStage.Finished;
        }
    }

    /// <summary>The press-time conditions other than the review lifetime, in display order.</summary>
    private async Task<ChocolateyClientUpdateRefusal> PressRefusalAsync(DateTimeOffset started, CancellationToken cancellationToken)
    {
        if (!admitted) return ChocolateyClientUpdateRefusal.NotAdmitted;
        if (!IsCheckFresh) return ChocolateyClientUpdateRefusal.CheckTooOld;
        ReviewedRouteSplitTokenState token;
        try { token = probeSplitToken(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { token = ReviewedRouteSplitTokenState.Unknown; }
        if (token != ReviewedRouteSplitTokenState.SplitTokenLimited) return ChocolateyClientUpdateRefusal.SplitTokenNotLimited;
        IReadOnlyList<ReviewedUpdateHistoryRecord>? history;
        try { history = await readHistory(cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { history = null; }
        if (cancellationToken.IsCancellationRequested) return ChocolateyClientUpdateRefusal.ReviewExpired;
        return ChocolateyPackageUpdateAttention.EarlierUpdateNeedsAttention(history, started)
            ? ChocolateyClientUpdateRefusal.EarlierUpdateNeedsAttention
            : ChocolateyClientUpdateRefusal.None;
    }

    /// <summary>
    /// A package ID the row may select: present, never a Chocolatey client identity (in any case), and the compiled admitted
    /// package (C1a).
    /// </summary>
    public static bool IsSelectablePackage(string? packageId) =>
        !string.IsNullOrWhiteSpace(packageId) && !ReviewedUpdateOperation.IsChocolateyClientIdentity(packageId) &&
        ChocolateyPackageUpdateAdmission.AdmitsPackage(packageId);

    /// <summary>
    /// The intent of exactly this package, resolved during this check, as provisional user-cache intent with a strictly newer
    /// candidate (the producer already refuses anything else; this is checked again before anything is saved).
    /// </summary>
    private static bool IsThisCheck(ChocolateyReviewIntentFacts facts, string packageId, DateTimeOffset started) =>
        string.Equals(facts.PackageId, packageId, StringComparison.Ordinal) &&
        facts.CandidateProvenance == ReviewedCandidateProvenance.UserCacheIntent &&
        facts.IntentResolvedAt >= started && facts.ObservedAt >= facts.IntentResolvedAt &&
        VersionComparison.Compare(new(facts.InstalledVersion, facts.CandidateVersion)).Outcome == VersionComparisonOutcome.Newer;

    /// <summary>
    /// The frozen package operation (schema 3, Chocolatey Community, Machine, provider-script controlled, the checked intent's
    /// <c>choco-profile-v2:</c> identity), only when the review gate admits it and the Profile V2 disclosure matches it (the intent's
    /// time rules included); null otherwise.
    /// </summary>
    private ReviewedUpdateOperation? Freeze(ChocolateyReviewIntentFacts facts)
    {
        DateTimeOffset reviewedAt = clock.GetUtcNow();
        if (!ReviewedUpdateOperation.TryGetProfileEvidenceDigest(facts.ProfileEvidenceId, out _) || facts.ObservedAt > reviewedAt) return null;
        try
        {
            string key = StableProductKeyPrefix + facts.PackageId;
            ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection(key, UpdateProvider.Chocolatey,
                UpdateSource.ChocolateyCommunity, facts.PackageId, facts.InstalledVersion, facts.CandidateVersion, UpdateScope.Machine,
                UpdateArchitecture.Neutral, UpdateChannel.Stable, MappingEvidenceVersion)).Confirm(reviewedAt);
            ReviewedUpdateOperation frozen = new(job, facts.RequestingUserSid, key, InstallationContextId, facts.ProfileEvidenceId, facts.ObservedAt,
                UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3,
                ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled), ReviewedUpdateArchitecture.ProviderScriptSelected());
            // Only the exact package shape the review gate admits; a client identity is never prepared here.
            return frozen.RouteContract == UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3 &&
                frozen.ProviderGuardContract == ReviewedProviderGuardContract.CommunityScriptRuntimeLimits &&
                !ReviewedUpdateOperation.IsChocolateyClientIdentity(frozen.Selection.PackageId) &&
                ChocolateyPackageUpdateAdmission.AdmitsPackage(frozen.Selection.PackageId) && ReviewedUpdateReview.HasSupportedMode(frozen) &&
                ChocolateyReviewDisclosurePresentation.Present(frozen, facts, ReviewedRouteSplitTokenState.SplitTokenLimited).CanConfirm
                    ? frozen
                    : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool SameOperation(ReviewedUpdateOperation saved, ReviewedUpdateOperation frozen) =>
        saved.JobId == frozen.JobId && string.Equals(saved.Digest, frozen.Digest, StringComparison.Ordinal);
}
