using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>
/// The review-time facts of the installed Chocolatey client, from a fresh Desktop-role, client-mode observation made when the
/// review is prepared (Chocolatey Confirm design §3): the observed listed release, the compiled list's offer for it, the
/// observing token's SID and the <c>choco-profile-v2:</c> identity of that observation. Review data only: it is not dispatch
/// evidence, and the workflow re-observes the client (F′) before any claim.
/// </summary>
public sealed record ChocolateyClientReviewEvidence(string SourceVersion, string TargetVersion, string RequestingUserSid,
    string ProfileEvidenceId, DateTimeOffset ObservedAt);

/// <summary>Observes the installed client for a review at the given verified release; null when it is not exactly that release.</summary>
public delegate Task<ChocolateyClientReviewEvidence?> ChocolateyClientReviewObservation(string verifiedInstalledRelease, CancellationToken cancellationToken);

/// <summary>The progress points of a confirmed client update (design §4.3), in order. Display only.</summary>
public enum ChocolateyClientUpdatePhase
{
    /// <summary>The gate, the fresh re-observation (F′) and the durable claim.</summary>
    CheckingAgain,
    /// <summary>The helper exchange has started: Windows asks for permission, then the update runs under the watchdog.</summary>
    WaitingForPermission,
    /// <summary>The helper reply arrived; settlement and verification of the target release run.</summary>
    Verifying,
}

/// <summary>
/// The relay from the composed dispatcher to the session's progress sink. The composition reports phases; only the session
/// attaches a sink. Reporting never throws and decides nothing.
/// </summary>
public sealed class ChocolateyClientUpdatePhaseSignal
{
    private IProgress<ChocolateyClientUpdatePhase>? target;

    public void Report(ChocolateyClientUpdatePhase phase)
    {
        try { Volatile.Read(ref target)?.Report(phase); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
    }

    internal void Attach(IProgress<ChocolateyClientUpdatePhase>? progress) => Volatile.Write(ref target, progress);
}

/// <summary>Where one review session is (design §4.2/§4.3).</summary>
public enum ChocolateyClientUpdateReviewStage
{
    /// <summary>No review has been prepared yet.</summary>
    NotPrepared,
    /// <summary>The fresh observation and the saved draft are being prepared.</summary>
    Preparing,
    /// <summary>A live review exists; Confirm may be pressed if every condition holds.</summary>
    Ready,
    /// <summary>The review could not be prepared; nothing was started.</summary>
    PrepareFailed,
    /// <summary>Confirm was accepted and the one workflow run is in progress.</summary>
    Running,
    /// <summary>The run ended (any result); the session is spent.</summary>
    Finished,
    /// <summary>Confirm was refused (expired, condition no longer held); the session is spent and nothing was started.</summary>
    Refused,
}

/// <summary>Why a Confirm press was refused before anything started (design §2, re-checked on press).</summary>
public enum ChocolateyClientUpdateRefusal
{
    None,
    NotAdmitted,
    NotReady,
    CheckTooOld,
    SplitTokenNotLimited,
    EarlierUpdateNeedsAttention,
    ReviewExpired,
}

/// <summary>The result of one Confirm press: either a refusal before anything started, or the one workflow result.</summary>
public sealed record ChocolateyClientUpdateRunResult(ChocolateyClientUpdateRefusal Refusal, ReviewedUpdateDispatchResult? Result)
{
    public bool Started => Result is not null;
}

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, TM-03/04/07/13; Chocolatey Confirm design §3 item 2 and §5 items 2–4: the one-use review session
/// of a Chocolatey client self-update. It is the only <c>src</c> caller of <see cref="ReviewedUpdateReview.Confirm"/> for
/// <see cref="UpdateRouteContract.ChocolateyClientSelfUpdateV1"/> and the only caller of its workflow (architecture-pinned).
/// <list type="bullet">
/// <item><see cref="PrepareAsync"/> builds the frozen operation only from the verified installed release, the compiled list's offer
/// for it and a fresh review observation of exactly that release (design §5 item 3): no scan, wire or database value becomes a
/// version, a package ID or an argument. It saves the draft and starts the 30-minute review lifetime.</item>
/// <item><see cref="ConfirmAsync"/> re-checks on the press: owner admission (C1), the check's age (C2, 10 minutes, owner decision 1),
/// the split token (C4), earlier client updates needing attention (C6), and the review's own lifetime (C8, through
/// <see cref="ReviewedUpdateReview.Confirm"/>). The gate (C7), the deployment capability (C5), F′, the claim, the one-use permit, the
/// helper's stages and the verifier run inside the workflow. One accepted press gives at most one workflow run; every later press
/// is refused, and a new attempt needs a new session.</item>
/// </list>
/// The run is started off the UI thread and is not cancellable from the screen: closing the dialog cannot stop an elevated run
/// that has started, so the run always reaches its recorded result.
/// </summary>
public sealed class ChocolateyClientUpdateReviewSession
{
    /// <summary>Owner decision 1 (design §9): the verified check may be at most this old when Confirm is pressed.</summary>
    public static readonly TimeSpan MaximumCheckAge = TimeSpan.FromMinutes(10);

    /// <summary>The review lifetime (<c>ReviewedUpdateLifetime.MaximumReviewAge</c>), for the expiry line only.</summary>
    public static readonly TimeSpan ReviewLifetime = TimeSpan.FromMinutes(30);

    /// <summary>The review observation must be this fresh when the operation is frozen.</summary>
    internal static readonly TimeSpan MaximumReviewEvidenceAge = TimeSpan.FromMinutes(2);

    /// <summary>Fixed identities of the client registration (never a scan value).</summary>
    internal const string StableProductKey = "choco:chocolatey";
    internal const string NativeInstanceId = "choco:chocolatey";
    internal const string InstallationContextId = "machine:chocolatey";
    internal const string MappingEvidenceVersion = "chocolatey-client-v1";

    private readonly object sync = new();
    private readonly ChocolateyClientReviewObservation observeForReview;
    private readonly Func<ReviewedUpdateOperation, CancellationToken, Task<ReviewedUpdateDraft>> saveDraft;
    private readonly Func<CancellationToken, Task<IReadOnlyList<ReviewedUpdateHistoryRecord>?>> readHistory;
    private readonly ReviewedUpdateDispatchWorkflow workflow;
    private readonly ChocolateyClientUpdatePhaseSignal phases;
    private readonly Func<ReviewedRouteSplitTokenState> probeSplitToken;
    private readonly IReadOnlyList<string> listed;
    private readonly TimeProvider clock;
    private readonly bool admitted;
    private ChocolateyClientUpdateReviewStage stage = ChocolateyClientUpdateReviewStage.NotPrepared;
    private ReviewedUpdateReview? review;
    private ReviewedUpdateOperation? operation;

    public ChocolateyClientUpdateReviewSession(ChocolateyClientReviewObservation observeForReview,
        Func<ReviewedUpdateOperation, CancellationToken, Task<ReviewedUpdateDraft>> saveDraft,
        Func<CancellationToken, Task<IReadOnlyList<ReviewedUpdateHistoryRecord>?>> readHistory, ReviewedUpdateDispatchWorkflow workflow,
        ChocolateyClientUpdatePhaseSignal phases, Func<ReviewedRouteSplitTokenState> probeSplitToken, IReadOnlyList<string> listedVersions,
        TimeProvider? clock = null)
        : this(ChocolateyClientUpdateAdmission.IsOwnerApproved, observeForReview, saveDraft, readHistory, workflow, phases, probeSplitToken, listedVersions, clock)
    {
    }

    /// <summary>
    /// Tests only: a session with the admission given, so the admitted path stays under test whichever value is compiled in.
    /// Production code never calls it (architecture-pinned); every production session reads the compiled C1.
    /// </summary>
    internal static ChocolateyClientUpdateReviewSession CreateWithAdmissionForTests(bool admitted, ChocolateyClientReviewObservation observeForReview,
        Func<ReviewedUpdateOperation, CancellationToken, Task<ReviewedUpdateDraft>> saveDraft,
        Func<CancellationToken, Task<IReadOnlyList<ReviewedUpdateHistoryRecord>?>> readHistory, ReviewedUpdateDispatchWorkflow workflow,
        ChocolateyClientUpdatePhaseSignal phases, Func<ReviewedRouteSplitTokenState> probeSplitToken, IReadOnlyList<string> listedVersions,
        TimeProvider? clock = null) =>
        new(admitted, observeForReview, saveDraft, readHistory, workflow, phases, probeSplitToken, listedVersions, clock);

    private ChocolateyClientUpdateReviewSession(bool admitted, ChocolateyClientReviewObservation observeForReview,
        Func<ReviewedUpdateOperation, CancellationToken, Task<ReviewedUpdateDraft>> saveDraft,
        Func<CancellationToken, Task<IReadOnlyList<ReviewedUpdateHistoryRecord>?>> readHistory, ReviewedUpdateDispatchWorkflow workflow,
        ChocolateyClientUpdatePhaseSignal phases, Func<ReviewedRouteSplitTokenState> probeSplitToken, IReadOnlyList<string> listedVersions,
        TimeProvider? clock)
    {
        this.admitted = admitted;
        this.observeForReview = observeForReview ?? throw new ArgumentNullException(nameof(observeForReview));
        this.saveDraft = saveDraft ?? throw new ArgumentNullException(nameof(saveDraft));
        this.readHistory = readHistory ?? throw new ArgumentNullException(nameof(readHistory));
        this.workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        this.phases = phases ?? throw new ArgumentNullException(nameof(phases));
        this.probeSplitToken = probeSplitToken ?? throw new ArgumentNullException(nameof(probeSplitToken));
        ArgumentNullException.ThrowIfNull(listedVersions);
        listed = Array.AsReadOnly(listedVersions.ToArray());
        this.clock = clock ?? TimeProvider.System;
    }

    public ChocolateyClientUpdateReviewStage Stage { get { lock (sync) return stage; } }

    /// <summary>The frozen operation once prepared; display only (title, versions, expiry).</summary>
    public ReviewedUpdateOperation? Operation { get { lock (sync) return operation; } }

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

    /// <summary>
    /// C2: true while a verified check that started at <paramref name="checkStartedAt"/> is less than <see cref="MaximumCheckAge"/>
    /// old (and not in the future).
    /// </summary>
    public bool IsCheckFresh(DateTimeOffset checkStartedAt)
    {
        TimeSpan age = clock.GetUtcNow() - checkStartedAt;
        return age >= TimeSpan.Zero && age < MaximumCheckAge;
    }

    /// <summary>
    /// Prepares the one review: a fresh review observation of exactly <paramref name="verifiedInstalledRelease"/>, the frozen
    /// client operation for the compiled list's offer, the saved draft and the review lifetime. Only once, only while owner
    /// admission is compiled in; any refusal, mismatch, fault or cancellation is <see cref="ChocolateyClientUpdateReviewStage.PrepareFailed"/>,
    /// and nothing was started.
    /// </summary>
    public async Task<ChocolateyClientUpdateReviewStage> PrepareAsync(string verifiedInstalledRelease, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            if (stage != ChocolateyClientUpdateReviewStage.NotPrepared) return stage;
            stage = ChocolateyClientUpdateReviewStage.Preparing;
        }
        ReviewedUpdateReview? prepared = null;
        ReviewedUpdateOperation? frozen = null;
        try
        {
            if (admitted && OfferFor(verifiedInstalledRelease) is { } target)
            {
                ChocolateyClientReviewEvidence? evidence = await observeForReview(verifiedInstalledRelease, cancellationToken).ConfigureAwait(false);
                if (!cancellationToken.IsCancellationRequested && evidence is not null &&
                    string.Equals(evidence.SourceVersion, verifiedInstalledRelease, StringComparison.Ordinal) &&
                    string.Equals(evidence.TargetVersion, target, StringComparison.Ordinal) &&
                    Freeze(evidence) is { } candidate)
                {
                    ReviewedUpdateDraft? draft = await saveDraft(candidate, cancellationToken).ConfigureAwait(false);
                    // The review runs on exactly the operation that was saved, which must be the one frozen here.
                    if (draft is not null && !cancellationToken.IsCancellationRequested && SameOperation(draft.Operation, candidate))
                    {
                        frozen = draft.Operation;
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
            if (prepared is null || frozen is null)
            {
                stage = ChocolateyClientUpdateReviewStage.PrepareFailed;
                return stage;
            }
            review = prepared;
            operation = frozen;
            stage = ChocolateyClientUpdateReviewStage.Ready;
            return stage;
        }
    }

    /// <summary>
    /// One Confirm press. Re-checks C1, C2, C4, C6 and C8; then confirms the review (once) and runs the workflow once, off the UI
    /// thread and to its recorded result. Any refusal spends the session before anything is claimed. Never throws for a refusal.
    /// </summary>
    public async Task<ChocolateyClientUpdateRunResult> ConfirmAsync(DateTimeOffset verifiedCheckStartedAt, IProgress<ChocolateyClientUpdatePhase>? progress,
        CancellationToken cancellationToken)
    {
        ReviewedUpdateReview current;
        lock (sync)
        {
            if (stage != ChocolateyClientUpdateReviewStage.Ready || review is null) return new(ChocolateyClientUpdateRefusal.NotReady, null);
            // Claimed for this press: a second press while the checks below run is refused.
            stage = ChocolateyClientUpdateReviewStage.Running;
            current = review;
        }
        ChocolateyClientUpdateRefusal refusal = await PressRefusalAsync(verifiedCheckStartedAt, cancellationToken).ConfigureAwait(false);
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
            // G3: the workflow may do bounded synchronous I/O, so it runs off the caller's (UI) thread. It is never cancelled
            // from the screen: an elevated run that has started always reaches its recorded result.
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
    private async Task<ChocolateyClientUpdateRefusal> PressRefusalAsync(DateTimeOffset verifiedCheckStartedAt, CancellationToken cancellationToken)
    {
        if (!admitted) return ChocolateyClientUpdateRefusal.NotAdmitted;
        if (!IsCheckFresh(verifiedCheckStartedAt)) return ChocolateyClientUpdateRefusal.CheckTooOld;
        ReviewedRouteSplitTokenState token;
        try { token = probeSplitToken(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { token = ReviewedRouteSplitTokenState.Unknown; }
        if (token != ReviewedRouteSplitTokenState.SplitTokenLimited) return ChocolateyClientUpdateRefusal.SplitTokenNotLimited;
        IReadOnlyList<ReviewedUpdateHistoryRecord>? history;
        try { history = await readHistory(cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { history = null; }
        if (cancellationToken.IsCancellationRequested) return ChocolateyClientUpdateRefusal.ReviewExpired;
        return ChocolateyClientUpdateAttention.EarlierUpdateNeedsAttention(history, verifiedCheckStartedAt)
            ? ChocolateyClientUpdateRefusal.EarlierUpdateNeedsAttention
            : ChocolateyClientUpdateRefusal.None;
    }

    /// <summary>The compiled list's offer for the verified release (design §3.3), or null.</summary>
    private string? OfferFor(string? verifiedInstalledRelease) =>
        ChocolateyClientUpdatePresentation.IsReleaseVersion(verifiedInstalledRelease)
            ? ChocolateyClientUpdateViewModel.OfferTarget(verifiedInstalledRelease, listed)
            : null;

    /// <summary>
    /// The frozen client operation (schema 3, Chocolatey Community, Machine, provider-script controlled, the review observation's
    /// <c>choco-profile-v2:</c> identity), only when it is the exact admitted client shape; null otherwise.
    /// </summary>
    private ReviewedUpdateOperation? Freeze(ChocolateyClientReviewEvidence evidence)
    {
        DateTimeOffset reviewedAt = clock.GetUtcNow();
        TimeSpan evidenceAge = reviewedAt - evidence.ObservedAt;
        if (evidenceAge < TimeSpan.Zero || evidenceAge > MaximumReviewEvidenceAge ||
            !ReviewedUpdateOperation.TryGetProfileEvidenceDigest(evidence.ProfileEvidenceId, out _))
            return null;
        try
        {
            ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection(StableProductKey, UpdateProvider.Chocolatey,
                UpdateSource.ChocolateyCommunity, ReviewedUpdateOperation.ChocolateyClientPackageId, evidence.SourceVersion, evidence.TargetVersion,
                UpdateScope.Machine, UpdateArchitecture.Neutral, UpdateChannel.Stable, MappingEvidenceVersion)).Confirm(reviewedAt);
            ReviewedUpdateOperation frozen = new(job, evidence.RequestingUserSid, NativeInstanceId, InstallationContextId, evidence.ProfileEvidenceId,
                evidence.ObservedAt, UpdateRouteContract.ChocolateyClientSelfUpdateV1,
                ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled), ReviewedUpdateArchitecture.ProviderScriptSelected());
            // Design §5 item 6: only the exact client shape the review gate admits; the package route is never prepared here.
            return frozen.RouteContract == UpdateRouteContract.ChocolateyClientSelfUpdateV1 &&
                frozen.ProviderGuardContract == ReviewedProviderGuardContract.ChocolateyClientSelfUpdate && ReviewedUpdateReview.HasSupportedMode(frozen)
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
