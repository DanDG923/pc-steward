using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using PCSteward.Domain;

namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>
/// The Helper-role static observation the native item runs at stages B and D. Production is exactly
/// <see cref="ChocolateyProfileObserver.Observe"/> with <see cref="ChocolateyObserverRole.Helper"/>; only the test-only
/// <see cref="NativeChocolateyHelperItem.CreateOverFixtureForTests"/> supplies another (an observation over fixture inputs).
/// </summary>
internal delegate ChocolateyProfileResult ChocolateyHelperObserve(RequesterToken token, string targetId, ChocolateyNativeVersion frozenInstalled,
    string expectedFingerprint, TimeSpan remaining, CancellationToken cancellationToken);

/// <summary>
/// FR-UPD-15, FR-UPD-01, TM-03/04/07/13; Task 50 design rev 6 §4, §5.2 and §5.4 (rev-3 L-C, rev-4 L5, N4b): the single native
/// qualification item. It owns the requester evaluation token, the stage-B observation (and with it the held root-engine
/// binding, §4 M4/L2/L6) and every launch job, and enforces the stage order itself, independent of the stage machine:
/// <list type="number">
/// <item><see cref="RequesterCannotModify"/> (G1, §6) only before stage B;</item>
/// <item><see cref="ObserveProfile"/> once, first;</item>
/// <item><see cref="QueryCandidateAsync"/> once, only after a stage-B <c>Observed</c>;</item>
/// <item><see cref="RevalidateBeforeLaunch"/> once, only after a stage-C <c>Matched</c>; its <c>Observed</c> mints the one-use
/// launch permission;</item>
/// <item><see cref="UpgradeAsync"/> once, only with that permission, which it consumes atomically before any I/O.</item>
/// </list>
/// Any other call, a concurrent call, or a call after <see cref="Dispose"/> returns its record's <c>OutOfOrder</c> arm (or
/// <c>false</c> for the G1 check) without any I/O or launch. A non-passing result ends the sequence. Dispose may arrive while a
/// call is in flight (the stage machine's real-time caps): it cancels the in-flight work through the item's lifetime token
/// and admits nothing new; the token, the observation and the held binding are released only when the last in-flight call
/// has returned, and the binding only when every launch job settled (otherwise it stays held until process exit, M4).
/// <see cref="AggregateSettled"/> is true only with no call in flight and every launch job of the item settled.
/// </summary>
internal sealed class NativeChocolateyHelperItem : IChocolateyHelperItem
{
    private enum Phase
    {
        Begun,
        Observed,
        Matched,
        Permitted,
        Busy,
        Closed
    }

    private readonly IWork work;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Lock gate = new();
    private Phase phase = Phase.Begun;
    private int inFlight;
    private bool disposed;
    private bool released;

    private NativeChocolateyHelperItem(IWork work) => this.work = work;

    /// <summary>Native port only (architecture test): the production item over an accepted requester token, which it owns.</summary>
    internal static NativeChocolateyHelperItem Create(RequesterToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return new NativeChocolateyHelperItem(new NativeWork(token, ProductionObserve, ancestryBase: null, stageCForTests: null));
    }

    /// <summary>
    /// Test-only (an architecture test bans every <c>src</c> caller): the item's ordering, permission, settlement and disposal
    /// rules over scripted stage values instead of native work.
    /// </summary>
    internal static NativeChocolateyHelperItem CreateForTests(ChocolateyHelperItemTestSeam seam)
    {
        ArgumentNullException.ThrowIfNull(seam);
        return new NativeChocolateyHelperItem(new ScriptedWork(seam));
    }

    /// <summary>
    /// Test-only (an architecture test bans every <c>src</c> caller): the native work over a fixture. <paramref name="observe"/>
    /// replaces the production observer call (a test passes the N2 fixture seam); <paramref name="ancestryBase"/>, when set,
    /// starts ancestry evaluation for the G1 check at that directory instead of the drive root (as the observer's own test seam
    /// does); <paramref name="stageCForTests"/>, when set, replaces the two client queries with a fixed stage-C value (a
    /// stand-in engine prints no Chocolatey records). The token is owned by the item.
    /// </summary>
    internal static NativeChocolateyHelperItem CreateOverFixtureForTests(RequesterToken token, ChocolateyHelperObserve observe, string? ancestryBase,
        Func<ChocolateyCandidateValues>? stageCForTests)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(observe);
        if (ancestryBase is not null && (!ProtectedNodeReader.IsCanonicalLocalPath(ancestryBase) || ancestryBase.Length <= 3))
            throw new ArgumentException("A canonical ancestry base below a drive root is required.", nameof(ancestryBase));
        return new NativeChocolateyHelperItem(new NativeWork(token, observe, ancestryBase, stageCForTests));
    }

    public bool AggregateSettled
    {
        get
        {
            lock (gate)
                if (inFlight > 0) return false;
            try { return work.LaunchesSettled; }
            catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
        }
    }

    public bool RequesterCannotModify(string root, IReadOnlyList<string> files)
    {
        if (root is null || files is null || !TryEnter(Phase.Begun, exclusive: false)) return false;
        try { return work.RequesterCannotModify(root, [.. files]); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
        finally { Exit(Phase.Begun, exclusive: false); }
    }

    public ChocolateyStageValues ObserveProfile(ReviewedUpdateOperation operation, string expectedFingerprint, TimeSpan remaining,
        CancellationToken cancellationToken)
    {
        if (operation is null || expectedFingerprint is null || !TryEnter(Phase.Begun, exclusive: true)) return new ChocolateyStageValues.OutOfOrder();
        Phase next = Phase.Closed;
        try
        {
            using CancellationTokenSource linked = Link(cancellationToken);
            ChocolateyStageValues values = work.ObserveProfile(operation, expectedFingerprint, remaining, linked.Token);
            if (values is ChocolateyStageValues.Observed) next = Phase.Observed;
            return values;
        }
        finally { Exit(next, exclusive: true); }
    }

    public async Task<ChocolateyCandidateValues> QueryCandidateAsync(TimeSpan remaining, CancellationToken cancellationToken)
    {
        if (!TryEnter(Phase.Observed, exclusive: true)) return new ChocolateyCandidateValues.OutOfOrder();
        Phase next = Phase.Closed;
        try
        {
            using CancellationTokenSource linked = Link(cancellationToken);
            ChocolateyCandidateValues values = await work.QueryCandidateAsync(remaining, linked.Token).ConfigureAwait(false);
            if (values is ChocolateyCandidateValues.Matched) next = Phase.Matched;
            return values;
        }
        finally { Exit(next, exclusive: true); }
    }

    public ChocolateyStageValues RevalidateBeforeLaunch(TimeSpan remaining, CancellationToken cancellationToken)
    {
        if (!TryEnter(Phase.Matched, exclusive: true)) return new ChocolateyStageValues.OutOfOrder();
        Phase next = Phase.Closed;
        try
        {
            using CancellationTokenSource linked = Link(cancellationToken);
            ChocolateyStageValues values = work.RevalidateBeforeLaunch(remaining, linked.Token);
            // rev-3 L-C: the one-use launch permission exists only after a stage-C Matched and this stage-D Observed.
            if (values is ChocolateyStageValues.Observed) next = Phase.Permitted;
            return values;
        }
        finally { Exit(next, exclusive: true); }
    }

    public async Task<ChocolateyUpgradeResult> UpgradeAsync(TimeSpan remaining, CancellationToken cancellationToken)
    {
        // The permission is consumed here, before any I/O; whatever happens next, the item never launches again.
        if (!TryEnter(Phase.Permitted, exclusive: true)) return new ChocolateyUpgradeResult.OutOfOrder();
        try
        {
            using CancellationTokenSource linked = Link(cancellationToken);
            NativeProcessObservation observation = await work.UpgradeAsync(remaining, linked.Token).ConfigureAwait(false);
            return new ChocolateyUpgradeResult.Observed(observation);
        }
        finally { Exit(Phase.Closed, exclusive: true); }
    }

    public void Dispose()
    {
        bool release;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            phase = Phase.Closed;
            release = inFlight == 0 && !released;
            if (release) released = true;
        }
        // In-flight work observes the lifetime token (the observer's walk, the launcher's teardown); nothing new is admitted.
        try { lifetime.Cancel(); }
        catch (AggregateException) { }
        if (release) Release();
    }

    private bool TryEnter(Phase expected, bool exclusive)
    {
        lock (gate)
        {
            if (disposed || phase != expected) return false;
            if (exclusive) phase = Phase.Busy;
            inFlight++;
            return true;
        }
    }

    private void Exit(Phase next, bool exclusive)
    {
        bool release;
        lock (gate)
        {
            if (exclusive) phase = disposed ? Phase.Closed : next;
            inFlight--;
            release = disposed && inFlight == 0 && !released;
            if (release) released = true;
        }
        if (release) Release();
    }

    private CancellationTokenSource Link(CancellationToken cancellationToken) =>
        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);

    private void Release()
    {
        try { work.Dispose(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
    }

    private static ChocolateyProfileResult ProductionObserve(RequesterToken token, string targetId, ChocolateyNativeVersion frozenInstalled,
        string expectedFingerprint, TimeSpan remaining, CancellationToken cancellationToken) =>
        ChocolateyProfileObserver.Observe(token, ChocolateyObserverRole.Helper, targetId, frozenInstalled, expectedFingerprint, remaining,
            cancellationToken);

    /// <summary>The stage work behind the item's ordering rules: native in production, scripted only under the test seam.</summary>
    private interface IWork : IDisposable
    {
        bool LaunchesSettled { get; }

        bool RequesterCannotModify(string root, IReadOnlyList<string> files);

        ChocolateyStageValues ObserveProfile(ReviewedUpdateOperation operation, string expectedFingerprint, TimeSpan remaining, CancellationToken cancellationToken);

        Task<ChocolateyCandidateValues> QueryCandidateAsync(TimeSpan remaining, CancellationToken cancellationToken);

        ChocolateyStageValues RevalidateBeforeLaunch(TimeSpan remaining, CancellationToken cancellationToken);

        Task<NativeProcessObservation> UpgradeAsync(TimeSpan remaining, CancellationToken cancellationToken);
    }

    /// <summary>
    /// The native stage work. Stage B observes in the Helper role with the frozen installed value and F and keeps the
    /// observation (its held binding is the one that launches, L6). Stage C runs <c>list --exact</c> then <c>outdated</c>
    /// through <see cref="ChocolateyClientQueries"/> on that binding, each capped by what is left of <c>remaining</c>, and
    /// decides through <see cref="ChocolateyStageCMapping"/> with a <see cref="ChocolateyStageCBaseline"/> that includes F.
    /// Stage D is a full re-observation compared against the held stage-B identities (<see cref="ChocolateyBindingIdentity"/>);
    /// any difference is drift (MachineInputsUnsupported, values null). The upgrade runs the sealed command through the
    /// stage-B binding. Every launch's settlement is recorded; a launch that throws is recorded unsettled.
    /// </summary>
    private sealed class NativeWork(RequesterToken token, ChocolateyHelperObserve observe, string? ancestryBase,
        Func<ChocolateyCandidateValues>? stageCForTests) : IWork
    {
        private readonly Lock launches = new();
        private bool unsettledLaunch;
        private ReviewedUpdateOperation? operation;
        private string fingerprint = string.Empty;
        private ChocolateyNativeVersion? frozenInstalled;
        private ChocolateyCompiledCommand.ListExact? listCommand;
        private ChocolateyCompiledCommand.Upgrade? upgradeCommand;
        private ChocolateyProfileObservation? stageB;
        private ChocolateyBindingIdentity? stageBIdentity;

        public bool LaunchesSettled
        {
            get
            {
                lock (launches) return !unsettledLaunch;
            }
        }

        /// <summary>§6 (rev-4 L1): ancestry on the root, then Node mode on the root and on every file directly inside it.</summary>
        public bool RequesterCannotModify(string root, IReadOnlyList<string> files)
        {
            if (!OperatingSystem.IsWindows() || !ProtectedNodeReader.IsCanonicalLocalPath(root) || root.Length <= 3) return false;
            if (Ancestry(root) != RequesterModification.Denied) return false;
            using SafeFileHandle? directory = ProtectedNodeReader.TryOpen(root, directory: true, out _);
            if (directory is null ||
                RequesterAccess.Evaluate(token, directory, directory: true, parentDirectory: null, AccessMode.Node) != RequesterModification.Denied)
                return false;
            foreach (string file in files)
            {
                if (!ProtectedNodeReader.IsCanonicalLocalPath(file) || !file.StartsWith(root + '\\', StringComparison.OrdinalIgnoreCase) ||
                    file.IndexOf('\\', root.Length + 1) >= 0)
                    return false;
                using SafeFileHandle? handle = ProtectedNodeReader.TryOpen(file, directory: false, out _);
                if (handle is null ||
                    RequesterAccess.Evaluate(token, handle, directory: false, directory, AccessMode.Node) != RequesterModification.Denied)
                    return false;
            }
            return true;
        }

        public ChocolateyStageValues ObserveProfile(ReviewedUpdateOperation operation, string expectedFingerprint, TimeSpan remaining,
            CancellationToken cancellationToken)
        {
            string installed = operation.Selection.InstalledVersion;
            if (!ChocolateyCandidateValues.IsFingerprint(expectedFingerprint) ||
                !ChocolateyNativeVersion.TryParse(installed, out ChocolateyNativeVersion? frozen) || frozen is null ||
                !string.Equals(frozen.Normalized, installed, StringComparison.Ordinal))
                return Unsupported();
            // The only command factory call sites (architecture test): both refuse anything but the exact route shape.
            ChocolateyCompiledCommand.ListExact? list = ChocolateyCompiledCommand.TryListExact(operation);
            ChocolateyCompiledCommand.Upgrade? upgrade = ChocolateyCompiledCommand.TryUpgrade(operation);
            if (list is null || upgrade is null) return Unsupported();
            this.operation = operation;
            fingerprint = expectedFingerprint;
            frozenInstalled = frozen;
            listCommand = list;
            upgradeCommand = upgrade;

            ChocolateyProfileResult result = observe(token, operation.Selection.PackageId, frozen, expectedFingerprint, remaining, cancellationToken);
            if (result is not ChocolateyProfileResult.Observed observed) return Values(result);
            ChocolateyProfileObservation observation = observed.Observation;
            bool keep = false;
            try
            {
                ChocolateyBindingIdentity? identity = ChocolateyBindingIdentity.From(observation);
                if (identity is null || !Consistent(observation) ||
                    !string.Equals(identity.TargetId, operation.Selection.PackageId, StringComparison.Ordinal))
                    return Unsupported();
                ChocolateyStageValues.Observed values = new(observation.InstalledVersion.Normalized, observation.Fingerprint);
                stageB = observation;
                stageBIdentity = identity;
                keep = true;
                return values;
            }
            catch (ArgumentException)
            {
                return Unsupported();
            }
            finally
            {
                if (!keep) observation.Dispose();
            }
        }

        public async Task<ChocolateyCandidateValues> QueryCandidateAsync(TimeSpan remaining, CancellationToken cancellationToken)
        {
            ChocolateyHeldClientBinding binding = RequireBinding();
            if (stageCForTests is not null) return stageCForTests();
            ReviewedUpdateOperation frozen = operation!;
            ChocolateyStageCBaseline baseline = new(frozen.Selection.PackageId, stageB!.InstalledVersion.Normalized, stageB.Fingerprint, fingerprint,
                frozen.Selection.InstalledVersion, frozen.Selection.CandidateVersion);
            Stopwatch clock = Stopwatch.StartNew();
            if (Interrupted(remaining, clock, cancellationToken) is { } before) return before;
            ChocolateyQueryOutcome<ChocolateyInstalledRecord> list = await Launch(
                () => ChocolateyClientQueries.ListExactAsync(binding, listCommand!, remaining, cancellationToken),
                static outcome => outcome.Observation).ConfigureAwait(false);
            if (ChocolateyStageCMapping.AfterList(baseline, list) is { } decided) return decided;
            // Nothing more starts after an interrupt or once the budget is spent (the item's lifetime token covers Dispose).
            if (Interrupted(remaining, clock, cancellationToken) is { } between) return between;
            TimeSpan left = remaining - clock.Elapsed;
            ChocolateyQueryOutcome<ChocolateyNativeOutdatedRecord> outdated = await Launch(
                () => ChocolateyClientQueries.OutdatedAsync(binding, ChocolateyCompiledCommand.CreateOutdated(), left, cancellationToken),
                static outcome => outcome.Observation).ConfigureAwait(false);
            return ChocolateyStageCMapping.Map(baseline, list, outdated);
        }

        public ChocolateyStageValues RevalidateBeforeLaunch(TimeSpan remaining, CancellationToken cancellationToken)
        {
            ChocolateyHeldClientBinding binding = RequireBinding();
            ChocolateyProfileResult result = observe(token, operation!.Selection.PackageId, frozenInstalled!, fingerprint, remaining, cancellationToken);
            if (result is not ChocolateyProfileResult.Observed observed) return Values(result);
            using ChocolateyProfileObservation observation = observed.Observation;
            try
            {
                // L6: the stage-B binding launches; the fresh observation must prove the same identities, else drift.
                ChocolateyBindingIdentity? identity = ChocolateyBindingIdentity.From(observation);
                if (identity is null || !Consistent(observation) || !binding.CanLaunch || !identity.Matches(stageBIdentity!))
                    return Unsupported();
                return new ChocolateyStageValues.Observed(observation.InstalledVersion.Normalized, observation.Fingerprint);
            }
            catch (ArgumentException)
            {
                return Unsupported();
            }
        }

        public Task<NativeProcessObservation> UpgradeAsync(TimeSpan remaining, CancellationToken cancellationToken)
        {
            ChocolateyHeldClientBinding binding = RequireBinding();
            if (!binding.CanLaunch) throw new InvalidOperationException("The stage-B binding can no longer launch.");
            return Launch(() => ChocolateyClientQueries.UpgradeAsync(binding, upgradeCommand!, remaining, cancellationToken), static observation => observation);
        }

        public void Dispose()
        {
            // M4: the engine and release-tree holds stay until every launch job settled; otherwise process exit releases them.
            if (LaunchesSettled) stageB?.Dispose();
            token.Dispose();
        }

        private ChocolateyHeldClientBinding RequireBinding() =>
            stageB?.HeldClient is { } binding && operation is not null && listCommand is not null && upgradeCommand is not null && frozenInstalled is not null
                ? binding
                : throw new InvalidOperationException("No stage-B binding is held.");

        /// <summary>The observation is the Helper-role one for the frozen installed value and F.</summary>
        private bool Consistent(ChocolateyProfileObservation observation) =>
            observation.HeldClient is not null && observation.HeldClient.CanLaunch &&
            string.Equals(observation.InstalledVersion.Normalized, frozenInstalled!.Normalized, StringComparison.Ordinal) &&
            ChocolateyCandidateValues.IsFingerprint(observation.Fingerprint) &&
            CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(observation.Fingerprint), Encoding.ASCII.GetBytes(fingerprint));

        private async Task<T> Launch<T>(Func<Task<T>> run, Func<T, NativeProcessObservation> observationOf)
        {
            T outcome;
            try { outcome = await run().ConfigureAwait(false); }
            catch
            {
                lock (launches) unsettledLaunch = true;
                throw;
            }
            NativeProcessObservation observation = observationOf(outcome);
            if (!observation.AggregateSettled || observation.Invocation == NativeInvocation.Unknown || observation.TerminationFailed)
                lock (launches) unsettledLaunch = true;
            return outcome;
        }

        private ChocolateyCandidateValues.Undetermined? Interrupted(TimeSpan remaining, Stopwatch clock, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return new ChocolateyCandidateValues.Undetermined(true, false, LaunchesSettled, false);
            if (remaining - clock.Elapsed <= TimeSpan.Zero) return new ChocolateyCandidateValues.Undetermined(false, true, LaunchesSettled, false);
            return null;
        }

        /// <summary>
        /// Production: <see cref="RequesterAccess.EvaluateAncestry"/> from the lexical drive root. Test fixture only: below the
        /// ancestry base, Ancestor mode on every directory from the base down to the parent (as the observer's test seam).
        /// </summary>
        private RequesterModification Ancestry(string path)
        {
            if (ancestryBase is null || !path.StartsWith(ancestryBase + '\\', StringComparison.OrdinalIgnoreCase))
                return RequesterAccess.EvaluateAncestry(token, path);
            string[] segments = path[(ancestryBase.Length + 1)..].Split('\\');
            string directory = ancestryBase;
            ulong? volume = null;
            for (int index = 0; ; index++)
            {
                using (SafeFileHandle? handle = ProtectedNodeReader.TryOpen(directory, directory: true, out NativeNode node))
                {
                    if (handle is null || (volume is not null && volume != node.Identity.VolumeSerial)) return RequesterModification.Unreadable;
                    volume = node.Identity.VolumeSerial;
                    RequesterModification verdict = RequesterAccess.Evaluate(token, handle, directory: true, parentDirectory: null, AccessMode.Ancestor);
                    if (verdict != RequesterModification.Denied) return verdict;
                }
                if (index == segments.Length - 1) return RequesterModification.Denied;
                directory += '\\' + segments[index];
            }
        }

        private static ChocolateyStageValues Values(ChocolateyProfileResult result)
        {
            switch (result)
            {
                case ChocolateyProfileResult.Refused refused:
                    try { return new ChocolateyStageValues.Refused(Reason(refused.Reason), refused.ObservedInstalled, refused.ObservedFingerprint); }
                    catch (ArgumentException) { return Unsupported(); }
                case ChocolateyProfileResult.TimedOut:
                    return new ChocolateyStageValues.TimedOut();
                case ChocolateyProfileResult.Cancelled:
                    return new ChocolateyStageValues.Cancelled();
                default:
                    return Unsupported();
            }
        }

        private static ChocolateyStageRefusal Reason(ChocolateyProfileRefusal reason) => reason switch
        {
            ChocolateyProfileRefusal.SourceNotCommunity => ChocolateyStageRefusal.SourceNotCommunity,
            ChocolateyProfileRefusal.InstalledMismatch => ChocolateyStageRefusal.InstalledMismatch,
            ChocolateyProfileRefusal.ProfileMismatch => ChocolateyStageRefusal.ProfileMismatch,
            _ => ChocolateyStageRefusal.MachineInputsUnsupported,
        };

        private static ChocolateyStageValues.Refused Unsupported() => new(ChocolateyStageRefusal.MachineInputsUnsupported, null, null);
    }

    /// <summary>Test seam work: every stage is a scripted value; the item's ordering and disposal rules are unchanged.</summary>
    private sealed class ScriptedWork(ChocolateyHelperItemTestSeam seam) : IWork
    {
        public bool LaunchesSettled => seam.LaunchesSettled();

        public bool RequesterCannotModify(string root, IReadOnlyList<string> files) => seam.RequesterCannotModify(root, files);

        public ChocolateyStageValues ObserveProfile(ReviewedUpdateOperation operation, string expectedFingerprint, TimeSpan remaining,
            CancellationToken cancellationToken) => seam.ObserveProfile(operation, expectedFingerprint, remaining, cancellationToken);

        public Task<ChocolateyCandidateValues> QueryCandidateAsync(TimeSpan remaining, CancellationToken cancellationToken) =>
            seam.QueryCandidate(remaining, cancellationToken);

        public ChocolateyStageValues RevalidateBeforeLaunch(TimeSpan remaining, CancellationToken cancellationToken) =>
            seam.Revalidate(remaining, cancellationToken);

        public Task<NativeProcessObservation> UpgradeAsync(TimeSpan remaining, CancellationToken cancellationToken) =>
            seam.Upgrade(remaining, cancellationToken);

        public void Dispose() => seam.Released();
    }
}

/// <summary>
/// Test-only (an architecture test bans every <c>src</c> reference to <see cref="CreateForTests"/> and to
/// <see cref="NativeChocolateyHelperItem.CreateForTests"/>): scripted stage values for the native item, so its ordering, one-use
/// permission, settlement and in-flight disposal rules can be exercised without native inputs. It carries no authority: the
/// item still decides every transition.
/// </summary>
internal sealed class ChocolateyHelperItemTestSeam
{
    private ChocolateyHelperItemTestSeam(Func<string, IReadOnlyList<string>, bool> requesterCannotModify,
        Func<ReviewedUpdateOperation, string, TimeSpan, CancellationToken, ChocolateyStageValues> observeProfile,
        Func<TimeSpan, CancellationToken, Task<ChocolateyCandidateValues>> queryCandidate,
        Func<TimeSpan, CancellationToken, ChocolateyStageValues> revalidate,
        Func<TimeSpan, CancellationToken, Task<NativeProcessObservation>> upgrade, Func<bool> launchesSettled, Action released)
    {
        RequesterCannotModify = requesterCannotModify;
        ObserveProfile = observeProfile;
        QueryCandidate = queryCandidate;
        Revalidate = revalidate;
        Upgrade = upgrade;
        LaunchesSettled = launchesSettled;
        Released = released;
    }

    internal Func<string, IReadOnlyList<string>, bool> RequesterCannotModify { get; }
    internal Func<ReviewedUpdateOperation, string, TimeSpan, CancellationToken, ChocolateyStageValues> ObserveProfile { get; }
    internal Func<TimeSpan, CancellationToken, Task<ChocolateyCandidateValues>> QueryCandidate { get; }
    internal Func<TimeSpan, CancellationToken, ChocolateyStageValues> Revalidate { get; }
    internal Func<TimeSpan, CancellationToken, Task<NativeProcessObservation>> Upgrade { get; }
    internal Func<bool> LaunchesSettled { get; }
    internal Action Released { get; }

    internal static ChocolateyHelperItemTestSeam CreateForTests(Func<string, IReadOnlyList<string>, bool> requesterCannotModify,
        Func<ReviewedUpdateOperation, string, TimeSpan, CancellationToken, ChocolateyStageValues> observeProfile,
        Func<TimeSpan, CancellationToken, Task<ChocolateyCandidateValues>> queryCandidate,
        Func<TimeSpan, CancellationToken, ChocolateyStageValues> revalidate,
        Func<TimeSpan, CancellationToken, Task<NativeProcessObservation>> upgrade, Func<bool> launchesSettled, Action released)
    {
        ArgumentNullException.ThrowIfNull(requesterCannotModify);
        ArgumentNullException.ThrowIfNull(observeProfile);
        ArgumentNullException.ThrowIfNull(queryCandidate);
        ArgumentNullException.ThrowIfNull(revalidate);
        ArgumentNullException.ThrowIfNull(upgrade);
        ArgumentNullException.ThrowIfNull(launchesSettled);
        ArgumentNullException.ThrowIfNull(released);
        return new ChocolateyHelperItemTestSeam(requesterCannotModify, observeProfile, queryCandidate, revalidate, upgrade, launchesSettled, released);
    }
}

/// <summary>One held file's proven identity: walk-relative path plus the node (final path, kind, file ID and length).</summary>
internal readonly record struct ChocolateyBoundFile(string RelativePath, NativeNode Node);

/// <summary>
/// FR-UPD-15, TM-03/04/13; Task 50 design rev 6 §4 (L6) and §5.2 stage D: the identities of one Helper-role observation and
/// its held binding, as plain comparison data (no handle, no authority). Stage D re-observes in full and requires
/// <see cref="Matches"/> against the stage-B identity: the target, engine path, launch paths, fingerprint, installed value,
/// client-state baseline, and every held engine, release-tree and code file's relative path, final path, kind, file ID and
/// length (ordered by relative path). Any difference is drift.
/// </summary>
internal sealed record ChocolateyBindingIdentity(string TargetId, string EnginePath, ChocolateyLaunchPaths LaunchPaths, string Fingerprint,
    string Installed, ChocolateyClientStateData ClientState, ChocolateyBoundFile Engine, IReadOnlyList<ChocolateyBoundFile> ReleaseFiles,
    IReadOnlyList<ChocolateyBoundFile> CodeFiles)
{
    /// <summary>The identity of a Helper-role observation; null when it holds no binding.</summary>
    internal static ChocolateyBindingIdentity? From(ChocolateyProfileObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.HeldClient is not { } binding) return null;
        return new ChocolateyBindingIdentity(binding.TargetId, binding.EnginePath, binding.LaunchPaths, observation.Fingerprint,
            observation.InstalledVersion.Normalized, observation.ClientState, Bound(binding.Engine), Ordered(binding.ReleaseFiles),
            Ordered(binding.CodeFiles));
    }

    /// <summary>Ordinal equality of every member; the file lists are compared element by element in relative-path order.</summary>
    internal bool Matches(ChocolateyBindingIdentity other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return string.Equals(TargetId, other.TargetId, StringComparison.Ordinal) &&
            string.Equals(EnginePath, other.EnginePath, StringComparison.Ordinal) &&
            LaunchPaths == other.LaunchPaths &&
            string.Equals(Fingerprint, other.Fingerprint, StringComparison.Ordinal) &&
            string.Equals(Installed, other.Installed, StringComparison.Ordinal) &&
            ClientState == other.ClientState &&
            Engine == other.Engine &&
            ReleaseFiles.SequenceEqual(other.ReleaseFiles) &&
            CodeFiles.SequenceEqual(other.CodeFiles);
    }

    private static ChocolateyBoundFile Bound(ChocolateyHeldFile file) => new(file.RelativePath, file.Node);

    private static ChocolateyBoundFile[] Ordered(IReadOnlyList<ChocolateyHeldFile> files) =>
        [.. files.Select(Bound).OrderBy(file => file.RelativePath, StringComparer.Ordinal)];
}
