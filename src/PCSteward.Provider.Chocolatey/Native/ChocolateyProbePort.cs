namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 qualification probe spec rev 4 §3 (review M2) and §7 H1/H2/H4: the probe's own port.
/// It opens the held peer handle (H3) like the update port, and begins a <b>probe</b> item, never an update item: the probe
/// item has no stage-C, stage-D or launch member and no launch permission. Exactly one production implementation exists,
/// <see cref="NativeChocolateyProbePort"/> (architecture test). <see cref="IChocolateyPeer"/> is shared with the update port.
/// </summary>
internal interface IChocolateyProbePort
{
    /// <summary>Opens the held peer handle (H3) for the pipe server process; null on any failure.</summary>
    IChocolateyPeer? OpenPeer(int pipeServerProcessId);

    /// <summary>
    /// Stage A2: the peer-not-exited check, then the requester evaluation token under the §2.1 linked-token rules. Returns an
    /// item only when <paramref name="verdict"/> is <see cref="TokenVerdict.Accepted"/>; otherwise null with the closed verdict.
    /// </summary>
    IChocolateyProbeItem? BeginProbe(IChocolateyPeer peer, string expectedSid, out TokenVerdict verdict);
}

/// <summary>
/// One probe item: it owns the requester evaluation token. <see cref="RequesterCannotModify"/> is the G1 protection principal
/// (the same rule as the update item's); <see cref="ObserveClient"/> is the single Helper-role client-mode observation of
/// stage B′ with no expected fingerprint. The observation's held binding never leaves the item: it is disposed inside
/// <see cref="ObserveClient"/> before any value is returned (H2). <see cref="IDisposable.Dispose"/> may arrive while either
/// call is still in flight (N4b): it does not wait and does not throw, and the item admits nothing afterwards.
/// </summary>
internal interface IChocolateyProbeItem : IDisposable
{
    /// <summary>The G1 protection check (§6): ancestry on the root, then Node mode on the root and every file directly inside it.</summary>
    bool RequesterCannotModify(string root, IReadOnlyList<string> files);

    /// <summary>Stage B′: at most once, bounded by <paramref name="remaining"/>; the binding is disposed before this returns.</summary>
    ChocolateyProbeValues ObserveClient(TimeSpan remaining, CancellationToken cancellationToken);
}

/// <summary>
/// Probe spec §3 (review L5) and §4.1 field 7: which check at the failed step refused. The names and values equal the Execution
/// wire enum's exactly (pinned by test, §4.4).
/// </summary>
internal enum ChocolateyProbeFailedCheck { None, StepRule, InstalledVersion, SetupAccessGuard, ReleaseLeftover }

/// <summary>
/// Probe spec §3 (review L5; §10 answer 1) and §4.1 field 8: the access verdict behind a refusal. <see cref="Granted"/> is a
/// <c>RequesterModification.Granted</c> verdict (or the step-4 requester-owned check), <see cref="Unreadable"/> a
/// <c>RequesterModification.Unreadable</c> verdict, <see cref="Other"/> any refusal that was not an access verdict. The names and
/// values equal the Execution wire enum's exactly (pinned by test, §4.4).
/// </summary>
internal enum ChocolateyProbeFailedAccess { None, Granted, Unreadable, Other }

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; probe spec §3 B′ and §4.1 fields 5–15: the B′ result as plain values (H4). The kind is closed:
/// the base constructor is private and exactly these five arms derive from it. Each arm has non-private construction so a fake
/// item can build it, and carries no authority. The fingerprint crosses as its <b>length only</b>; no path, SID, handle or
/// binding is carried. The constructors enforce the per-arm shape, so an inconsistent value cannot be built.
/// <see cref="Cancelled"/> and <see cref="Faulted"/> are never written by the probe entry (no response).
/// </summary>
internal abstract record ChocolateyProbeValues
{
    /// <summary>§4.1 fields 11 and 12: the saturating maxima.</summary>
    internal const int MaximumNodesEvaluated = 1_048_576;
    internal const int MaximumAncestorsEvaluated = 65_536;

    private ChocolateyProbeValues() { }

    /// <summary>Every step passed: the registered client version, the fingerprint length and the instance's counts.</summary>
    internal sealed record Observed : ChocolateyProbeValues
    {
        internal Observed(string clientVersion, int fingerprintLength, int nodesEvaluated, int ancestorsEvaluated)
        {
            ClientVersion = RequireVersion(clientVersion, nameof(clientVersion));
            ArgumentOutOfRangeException.ThrowIfNotEqual(fingerprintLength, 64);
            FingerprintLength = fingerprintLength;
            (NodesEvaluated, AncestorsEvaluated) = RequireCounts(nodesEvaluated, ancestorsEvaluated);
        }

        internal string ClientVersion { get; }

        internal int FingerprintLength { get; }

        internal int NodesEvaluated { get; }

        internal int AncestorsEvaluated { get; }
    }

    /// <summary>
    /// A settled refusal: the reason, the failed step (1–9), the check and the access verdict behind it, the registered version
    /// only for <c>InstalledMismatch</c>, and the counts the instance reached.
    /// </summary>
    internal sealed record Refused : ChocolateyProbeValues
    {
        internal Refused(ChocolateyStageRefusal reason, int failedStep, ChocolateyProbeFailedCheck failedCheck, ChocolateyProbeFailedAccess failedAccess,
            string? observedInstalled, int nodesEvaluated, int ancestorsEvaluated)
        {
            if (!Enum.IsDefined(reason) || reason == ChocolateyStageRefusal.ProfileMismatch) throw new ArgumentOutOfRangeException(nameof(reason));
            if (failedStep is < 1 or > 9) throw new ArgumentOutOfRangeException(nameof(failedStep));
            if (!Enum.IsDefined(failedCheck) || failedCheck == ChocolateyProbeFailedCheck.None) throw new ArgumentOutOfRangeException(nameof(failedCheck));
            if (!Enum.IsDefined(failedAccess) || failedAccess == ChocolateyProbeFailedAccess.None) throw new ArgumentOutOfRangeException(nameof(failedAccess));
            if ((reason == ChocolateyStageRefusal.InstalledMismatch) != (observedInstalled is not null)) throw new ArgumentException("Only InstalledMismatch carries the installed value.", nameof(observedInstalled));
            if (observedInstalled is not null) RequireVersion(observedInstalled, nameof(observedInstalled));
            Reason = reason;
            FailedStep = failedStep;
            FailedCheck = failedCheck;
            FailedAccess = failedAccess;
            ObservedInstalled = observedInstalled;
            (NodesEvaluated, AncestorsEvaluated) = RequireCounts(nodesEvaluated, ancestorsEvaluated);
        }

        internal ChocolateyStageRefusal Reason { get; }

        internal int FailedStep { get; }

        internal ChocolateyProbeFailedCheck FailedCheck { get; }

        internal ChocolateyProbeFailedAccess FailedAccess { get; }

        internal string? ObservedInstalled { get; }

        internal int NodesEvaluated { get; }

        internal int AncestorsEvaluated { get; }
    }

    /// <summary>The <c>remaining</c> bound was exceeded; the counts are what the instance reached.</summary>
    internal sealed record TimedOut : ChocolateyProbeValues
    {
        internal TimedOut(int nodesEvaluated, int ancestorsEvaluated) =>
            (NodesEvaluated, AncestorsEvaluated) = RequireCounts(nodesEvaluated, ancestorsEvaluated);

        internal int NodesEvaluated { get; }

        internal int AncestorsEvaluated { get; }
    }

    /// <summary>The caller's token was cancelled (EOF, an inbound byte or the item's disposal). Never written.</summary>
    internal sealed record Cancelled : ChocolateyProbeValues
    {
        internal Cancelled() { }
    }

    /// <summary>
    /// The item could not produce a consistent value: a call out of order or after disposal, a binding that is not disposed, an
    /// observation inconsistent with the compiled release, or any internal fault. Never written (no response).
    /// </summary>
    internal sealed record Faulted : ChocolateyProbeValues
    {
        internal Faulted() { }
    }

    private static string RequireVersion(string? value, string name) =>
        ChocolateyQueryParser.IsNormalizedVersion(value) ? value! : throw new ArgumentException("A normalized native version is required.", name);

    private static (int Nodes, int Ancestors) RequireCounts(int nodes, int ancestors) =>
        nodes is < 0 or > MaximumNodesEvaluated ? throw new ArgumentOutOfRangeException(nameof(nodes))
            : ancestors is < 0 or > MaximumAncestorsEvaluated ? throw new ArgumentOutOfRangeException(nameof(ancestors))
            : (nodes, ancestors);
}
