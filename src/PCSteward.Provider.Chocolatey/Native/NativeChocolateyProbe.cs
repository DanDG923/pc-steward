namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>
/// The Helper-role client-mode observation the probe item runs at stage B′, with the per-observation report. Production is
/// exactly <see cref="ChocolateyProfileObserver.ObserveClientWithReport"/> with <see cref="ChocolateyObserverRole.Helper"/>
/// and no expected fingerprint; only the test-only <see cref="NativeChocolateyProbeItem.CreateOverFixtureForTests"/> supplies
/// another (an observation over fixture inputs).
/// </summary>
internal delegate ChocolateyProfileResult ChocolateyProbeObserve(RequesterToken token, ChocolateyNativeVersion frozenInstalled, TimeSpan remaining,
    ChocolateyObservationReport report, CancellationToken cancellationToken);

/// <summary>
/// FR-UPD-15, FR-UPD-01, TM-03/04/07/13; Task 50 qualification probe spec rev 4 §3 (review M2) and §7 H1/H2/H5: the single
/// native implementation of the probe port. <see cref="OpenPeer"/> opens the held peer handle as the update port does;
/// <see cref="BeginProbe"/> checks that the peer has not exited and acquires the requester evaluation token under the §2.1
/// linked-token rules from that same handle (as the update port's <c>BeginItemWithVerdict</c> does, without calling it), and
/// hands it to a new <see cref="NativeChocolateyProbeItem"/>, which owns it. A peer this port did not open, a missing SID, an
/// exited peer or any token refusal returns no item with the closed verdict. It constructs no update item and references no
/// launch, query or update-port type (architecture test).
/// </summary>
internal sealed class NativeChocolateyProbePort : IChocolateyProbePort
{
    public IChocolateyPeer? OpenPeer(int pipeServerProcessId) => NativeChocolateyPeer.TryOpen(pipeServerProcessId);

    public IChocolateyProbeItem? BeginProbe(IChocolateyPeer peer, string expectedSid, out TokenVerdict verdict)
    {
        verdict = TokenVerdict.EvaluationTokenUnreadable;
        if (peer is not NativeChocolateyPeer native || string.IsNullOrEmpty(expectedSid)) return null;
        if (native.HasExited)
        {
            verdict = TokenVerdict.PeerExited;
            return null;
        }
        RequesterToken? token = native.TryAcquireRequesterToken(expectedSid, out verdict);
        if (token is null)
        {
            if (verdict == TokenVerdict.Accepted) verdict = TokenVerdict.EvaluationTokenUnreadable;
            return null;
        }
        if (verdict != TokenVerdict.Accepted)
        {
            token.Dispose();
            return null;
        }
        return NativeChocolateyProbeItem.Create(token);
    }
}

/// <summary>
/// FR-UPD-15, FR-UPD-01, TM-03/04/07/13; probe spec rev 4 §3 (A3, B′, Stop, in-flight disposal) and §7 H1/H2/H10: the single
/// native probe item. It owns the requester evaluation token and has no stage-C, stage-D or launch member and no launch
/// permission; it never constructs or references the update item. The order is enforced here:
/// <list type="number">
/// <item><see cref="RequesterCannotModify"/> (G1, §6; the shared <see cref="ChocolateyRequesterProtection.CannotModify"/>
/// rule) only before stage B′;</item>
/// <item><see cref="ObserveClient"/> once, and only with no G1 call in flight. It observes the newest compiled listed release
/// (<c>frozenInstalled</c>) in client mode with no expected fingerprint and its own report. When the observation is
/// <c>Observed</c>, its held binding is disposed in this same method and must then report <c>IsDisposed</c>; nothing that holds
/// the binding is stored or returned (H2), and only the fingerprint's length leaves the method.</item>
/// </list>
/// Any other call, a concurrent call or a call after <see cref="Dispose"/> returns <c>false</c> or
/// <see cref="ChocolateyProbeValues.Faulted"/> without any I/O. Dispose may arrive while either call is in flight (the probe
/// entry's real-time caps, N4b): it neither waits nor throws, cancels the in-flight work through the item's lifetime token and
/// admits nothing new; the token and the lifetime token source are released only once the last in-flight call has returned.
/// </summary>
internal sealed class NativeChocolateyProbeItem : IChocolateyProbeItem
{
    private enum Phase
    {
        Begun,
        Busy,
        Closed
    }

    private readonly IWork work;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Lock gate = new();
    private Phase phase = Phase.Begun;
    private int inFlight;
    private bool disposed;
    private bool lifetimeCancelled;
    private bool released;

    private NativeChocolateyProbeItem(IWork work) => this.work = work;

    /// <summary>Native probe port only (architecture test): the production probe item over an accepted requester token, which it owns.</summary>
    internal static NativeChocolateyProbeItem Create(RequesterToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return new NativeChocolateyProbeItem(new NativeWork(token, ProductionObserve, ancestryBase: null));
    }

    /// <summary>
    /// Test-only (an architecture test bans every <c>src</c> caller): the native work over a fixture. <paramref name="observe"/>
    /// replaces the production observer call (a test passes the N2 fixture seam); <paramref name="ancestryBase"/>, when set,
    /// starts ancestry evaluation for the G1 check at that directory instead of the drive root. The token is owned by the item.
    /// </summary>
    internal static NativeChocolateyProbeItem CreateOverFixtureForTests(RequesterToken token, ChocolateyProbeObserve observe, string? ancestryBase)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(observe);
        if (ancestryBase is not null && (!ProtectedNodeReader.IsCanonicalLocalPath(ancestryBase) || ancestryBase.Length <= 3))
            throw new ArgumentException("A canonical ancestry base below a drive root is required.", nameof(ancestryBase));
        return new NativeChocolateyProbeItem(new NativeWork(token, observe, ancestryBase));
    }

    /// <summary>
    /// Test-only (an architecture test bans every <c>src</c> caller): the item's ordering and in-flight disposal rules over
    /// scripted values instead of native work.
    /// </summary>
    internal static NativeChocolateyProbeItem CreateForTests(ChocolateyProbeItemTestSeam seam)
    {
        ArgumentNullException.ThrowIfNull(seam);
        return new NativeChocolateyProbeItem(new ScriptedWork(seam));
    }

    public bool RequesterCannotModify(string root, IReadOnlyList<string> files)
    {
        if (root is null || files is null || !TryEnter(exclusive: false)) return false;
        try { return work.RequesterCannotModify(root, [.. files]); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
        finally { Exit(exclusive: false); }
    }

    public ChocolateyProbeValues ObserveClient(TimeSpan remaining, CancellationToken cancellationToken)
    {
        if (!TryEnter(exclusive: true)) return new ChocolateyProbeValues.Faulted();
        try
        {
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
            return work.ObserveClient(remaining, linked.Token);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new ChocolateyProbeValues.Faulted();
        }
        finally { Exit(exclusive: true); }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            phase = Phase.Closed;
        }
        // In-flight work observes the lifetime token (the observer's walk); nothing new is admitted. The cancellation runs
        // outside the gate (its callbacks are the in-flight work's), and before any release.
        try { lifetime.Cancel(); }
        catch (AggregateException) { }
        bool release;
        lock (gate)
        {
            lifetimeCancelled = true;
            release = ShouldRelease();
        }
        if (release) Release();
    }

    /// <summary>
    /// Admits one call. The observation is exclusive and runs once, with no G1 check in flight; the G1 check is non-exclusive
    /// and runs only before the observation.
    /// </summary>
    private bool TryEnter(bool exclusive)
    {
        lock (gate)
        {
            if (disposed || phase != Phase.Begun || (exclusive && inFlight != 0)) return false;
            if (exclusive) phase = Phase.Busy;
            inFlight++;
            return true;
        }
    }

    private void Exit(bool exclusive)
    {
        bool release;
        lock (gate)
        {
            if (exclusive) phase = Phase.Closed;
            inFlight--;
            release = ShouldRelease();
        }
        if (release) Release();
    }

    /// <summary>Under the gate: true exactly once, when Dispose has cancelled the lifetime token and no call is in flight.</summary>
    private bool ShouldRelease()
    {
        if (!disposed || !lifetimeCancelled || inFlight != 0 || released) return false;
        released = true;
        return true;
    }

    private void Release()
    {
        try { work.Dispose(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
        lifetime.Dispose();
    }

    /// <summary>Probe spec §3 B′: the client-mode Helper-role observation with no expected fingerprint.</summary>
    private static ChocolateyProfileResult ProductionObserve(RequesterToken token, ChocolateyNativeVersion frozenInstalled, TimeSpan remaining,
        ChocolateyObservationReport report, CancellationToken cancellationToken) =>
        ChocolateyProfileObserver.ObserveClientWithReport(token, ChocolateyObserverRole.Helper, frozenInstalled, expectedFingerprint: null, remaining,
            report, cancellationToken);

    /// <summary>The work behind the item's ordering rules: native in production, scripted only under the test seam.</summary>
    private interface IWork : IDisposable
    {
        bool RequesterCannotModify(string root, IReadOnlyList<string> files);

        ChocolateyProbeValues ObserveClient(TimeSpan remaining, CancellationToken cancellationToken);
    }

    /// <summary>
    /// The native work. B′ observes the newest compiled listed release (<c>ChocolateyReleaseList.Listed.Releases[^1]</c>; the
    /// spec §3 re-spec trigger pins the list at one entry) and maps the result and the report to plain values. The binding of an
    /// <c>Observed</c> result is disposed here, in this method, before anything is returned.
    /// </summary>
    private sealed class NativeWork(RequesterToken token, ChocolateyProbeObserve observe, string? ancestryBase) : IWork
    {
        public bool RequesterCannotModify(string root, IReadOnlyList<string> files) =>
            ChocolateyRequesterProtection.CannotModify(token, root, files, ancestryBase);

        public ChocolateyProbeValues ObserveClient(TimeSpan remaining, CancellationToken cancellationToken)
        {
            ChocolateyNativeVersion frozenInstalled = ChocolateyReleaseList.Listed.Releases[^1].NativeVersion;
            ChocolateyObservationReport report = new();
            ChocolateyProfileResult result = observe(token, frozenInstalled, remaining, report, cancellationToken);
            switch (result)
            {
                case ChocolateyProfileResult.Observed observed:
                {
                    ChocolateyProfileObservation observation = observed.Observation;
                    string version = observation.InstalledVersion.Normalized;
                    string fingerprint = observation.Fingerprint;
                    bool releasedBinding;
                    ChocolateyHeldClientBinding? binding = observation.HeldClient;
                    try
                    {
                        binding?.Dispose();
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException)
                    {
                        // A close that failed leaves the binding not disposed, which refuses below.
                    }
                    releasedBinding = binding is not null && binding.IsDisposed;
                    // Stop (§3): a binding that is not disposed, or an observation that is not of the newest listed release with a
                    // 64-character fingerprint, yields no response.
                    if (!releasedBinding || !string.Equals(version, frozenInstalled.Normalized, StringComparison.Ordinal) ||
                        !ChocolateyCandidateValues.IsFingerprint(fingerprint))
                        return new ChocolateyProbeValues.Faulted();
                    return Try(() => new ChocolateyProbeValues.Observed(version, fingerprint.Length, report.NodesEvaluated, report.AncestorsEvaluated));
                }
                case ChocolateyProfileResult.Refused refused:
                {
                    ChocolateyStageRefusal? reason = refused.Reason switch
                    {
                        ChocolateyProfileRefusal.MachineInputsUnsupported => ChocolateyStageRefusal.MachineInputsUnsupported,
                        ChocolateyProfileRefusal.SourceNotCommunity => ChocolateyStageRefusal.SourceNotCommunity,
                        ChocolateyProfileRefusal.InstalledMismatch => ChocolateyStageRefusal.InstalledMismatch,
                        // No expected fingerprint: a profile mismatch cannot be a probe result.
                        _ => null,
                    };
                    if (reason is null || report.FailedStep is not int step) return new ChocolateyProbeValues.Faulted();
                    string? installed = reason == ChocolateyStageRefusal.InstalledMismatch ? refused.ObservedInstalled : null;
                    return Try(() => new ChocolateyProbeValues.Refused(reason.Value, step, report.FailedCheck, report.FailedAccess, installed,
                        report.NodesEvaluated, report.AncestorsEvaluated));
                }
                case ChocolateyProfileResult.TimedOut:
                    return Try(() => new ChocolateyProbeValues.TimedOut(report.NodesEvaluated, report.AncestorsEvaluated));
                case ChocolateyProfileResult.Cancelled:
                    return new ChocolateyProbeValues.Cancelled();
                default:
                    return new ChocolateyProbeValues.Faulted();
            }
        }

        public void Dispose() => token.Dispose();

        private static ChocolateyProbeValues Try(Func<ChocolateyProbeValues> create)
        {
            try { return create(); }
            catch (ArgumentException) { return new ChocolateyProbeValues.Faulted(); }
        }
    }

    /// <summary>Test seam work: every call is scripted; the item's ordering and disposal rules are unchanged.</summary>
    private sealed class ScriptedWork(ChocolateyProbeItemTestSeam seam) : IWork
    {
        public bool RequesterCannotModify(string root, IReadOnlyList<string> files) => seam.RequesterCannotModify(root, files);

        public ChocolateyProbeValues ObserveClient(TimeSpan remaining, CancellationToken cancellationToken) => seam.ObserveClient(remaining, cancellationToken);

        public void Dispose() => seam.Released();
    }
}

/// <summary>
/// Test-only (an architecture test bans every <c>src</c> reference to <see cref="CreateForTests"/> and to
/// <see cref="NativeChocolateyProbeItem.CreateForTests"/>): scripted values for the native probe item, so its ordering and
/// in-flight disposal rules can be exercised without native inputs. It carries no authority: the item decides every transition.
/// </summary>
internal sealed class ChocolateyProbeItemTestSeam
{
    private ChocolateyProbeItemTestSeam(Func<string, IReadOnlyList<string>, bool> requesterCannotModify,
        Func<TimeSpan, CancellationToken, ChocolateyProbeValues> observeClient, Action released)
    {
        RequesterCannotModify = requesterCannotModify;
        ObserveClient = observeClient;
        Released = released;
    }

    internal Func<string, IReadOnlyList<string>, bool> RequesterCannotModify { get; }
    internal Func<TimeSpan, CancellationToken, ChocolateyProbeValues> ObserveClient { get; }
    internal Action Released { get; }

    internal static ChocolateyProbeItemTestSeam CreateForTests(Func<string, IReadOnlyList<string>, bool> requesterCannotModify,
        Func<TimeSpan, CancellationToken, ChocolateyProbeValues> observeClient, Action released)
    {
        ArgumentNullException.ThrowIfNull(requesterCannotModify);
        ArgumentNullException.ThrowIfNull(observeClient);
        ArgumentNullException.ThrowIfNull(released);
        return new ChocolateyProbeItemTestSeam(requesterCannotModify, observeClient, released);
    }
}
