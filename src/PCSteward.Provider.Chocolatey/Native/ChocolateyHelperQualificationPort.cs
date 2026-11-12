using PCSteward.Domain;

namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 design rev 6 §5.4 (M6, rev-3 M-A): the helper qualification port. The port owns the
/// item; the stage machine in Execution sees only these opaque interfaces and the plain value records below. No type with a
/// private constructor crosses into Execution. Exactly one production implementation of each interface exists (N4b):
/// <see cref="NativeChocolateyHelperQualification"/>, <see cref="NativeChocolateyPeer"/> and
/// <see cref="NativeChocolateyHelperItem"/> (architecture test).
/// </summary>
internal interface IChocolateyHelperQualificationPort
{
    /// <summary>Opens the held peer handle (H3) for the pipe server process; null on any failure.</summary>
    IChocolateyPeer? OpenPeer(int pipeServerProcessId);

    /// <summary>
    /// Stage A2: acquires the requester evaluation token under the §2.1 linked-token rules and checks that the peer has not
    /// exited. Null with a reason other than <see cref="ChocolateyItemRefusal.None"/> on refusal.
    /// </summary>
    IChocolateyHelperItem? BeginItem(IChocolateyPeer peer, string expectedSid, out ChocolateyItemRefusal refusal);
}

/// <summary>The held peer process handle (H3). Every peer fact comes from this handle.</summary>
internal interface IChocolateyPeer : IDisposable
{
    int ProcessId { get; }

    bool HasExited { get; }

    bool TryReadIdentity(out string imagePath, out int session, out string userSid);
}

/// <summary>
/// One qualification item: it owns the evaluation token, the held client binding and every launch job. The ordering rules
/// are enforced inside the native item, independent of the stage machine: <see cref="QueryCandidateAsync"/> needs an earlier
/// stage-B <c>Observed</c>; <see cref="RevalidateBeforeLaunch"/> needs a stage-C <c>Matched</c>; <see cref="UpgradeAsync"/>
/// needs the one-use launch permission minted by a stage-D <c>Observed</c> after a stage-C <c>Matched</c> (rev-3 L-C). An
/// out-of-order call returns its record's <c>OutOfOrder</c> arm without I/O or launch (rev-4 L5).
/// </summary>
internal interface IChocolateyHelperItem : IDisposable
{
    /// <summary>The G1 protection check (§6): ancestry on each root, then Node mode on the root and every file.</summary>
    bool RequesterCannotModify(string root, IReadOnlyList<string> files);

    /// <summary>Stage B with the frozen operation's profile digest; bounded by <paramref name="remaining"/>.</summary>
    ChocolateyStageValues ObserveProfile(ReviewedUpdateOperation operation, string expectedFingerprint, TimeSpan remaining,
        CancellationToken cancellationToken);

    /// <summary>Stage C: <c>list --exact</c>, then <c>outdated</c>, each capped by <paramref name="remaining"/>.</summary>
    Task<ChocolateyCandidateValues> QueryCandidateAsync(TimeSpan remaining, CancellationToken cancellationToken);

    /// <summary>Stage D: stage B in full with the same expected fingerprint, comparing identities with the held binding.</summary>
    ChocolateyStageValues RevalidateBeforeLaunch(TimeSpan remaining, CancellationToken cancellationToken);

    /// <summary>The single fixed upgrade through the stage-B binding and the N0 launcher, at most once.</summary>
    Task<ChocolateyUpgradeResult> UpgradeAsync(TimeSpan remaining, CancellationToken cancellationToken);

    /// <summary>True only when every launch job of the item settled.</summary>
    bool AggregateSettled { get; }
}

/// <summary>Why <see cref="IChocolateyHelperQualificationPort.BeginItem"/> refused. Every value except None maps to RequestNotAdmitted.</summary>
internal enum ChocolateyItemRefusal
{
    None,

    /// <summary>The requester evaluation token could not be acquired or failed the §2.1 token rules.</summary>
    RequesterTokenRefused,

    /// <summary>The held peer process exited.</summary>
    PeerExited,

    /// <summary>A call arrived out of order and was refused before any I/O or launch (rev-4 L5).</summary>
    OutOfOrder
}

/// <summary>The settled stage-B/D refusal reasons, in rev 3 §3.1 precedence order (the first is the strongest).</summary>
internal enum ChocolateyStageRefusal
{
    MachineInputsUnsupported,
    SourceNotCommunity,
    InstalledMismatch,
    ProfileMismatch
}

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 design rev 6 §3 and §5.4 (rev-4 L3, rev-5 F1): the stage-B or stage-D result as plain
/// values. The kind is closed (the base constructor is private; exactly these five arms derive from it). Each arm has
/// non-private construction so a fake item can build it, and carries no authority. The constructors enforce the rev 3
/// structural predicates that apply to stages B and D, so an inconsistent value cannot be built:
/// <list type="bullet">
/// <item>versions are supported native versions in normalized form and a fingerprint is 64 uppercase hex (S12);</item>
/// <item><c>SourceNotCommunity</c> carries neither an installed value nor a fingerprint (S8, design §3 step 8);</item>
/// <item><c>ProfileMismatch</c> carries both (S9).</item>
/// </list>
/// <see cref="TimedOut"/> and <see cref="Cancelled"/> are interrupts and never a settled refusal (N2).
/// </summary>
internal abstract record ChocolateyStageValues
{
    private ChocolateyStageValues() { }

    /// <summary>Every step passed; the installed value and fingerprint observed in this stage.</summary>
    internal sealed record Observed : ChocolateyStageValues
    {
        internal Observed(string installed, string fingerprint)
            : this(installed, fingerprint, wingetMachine: false)
        {
        }

        private Observed(string installed, string fingerprint, bool wingetMachine)
        {
            // D4b design §5.1: the machine WinGet route's installed value is reviewed version text, not a Chocolatey native version.
            Installed = wingetMachine
                ? UpdateJobSelection.IsVersionText(installed) ? installed : throw new ArgumentException("Reviewed version text is required.", nameof(installed))
                : RequireVersion(installed, nameof(installed));
            Fingerprint = RequireFingerprint(fingerprint, nameof(fingerprint));
        }

        /// <summary>D4b design §5.1: stage B or D passed for the machine WinGet route (the frozen installed text and registration digest).</summary>
        internal static Observed ForWinGetMachine(string installed, string fingerprint) => new(installed, fingerprint, wingetMachine: true);

        internal string Installed { get; }

        internal string Fingerprint { get; }
    }

    /// <summary>A settled refusal with the values "as far as observed" (rev-5 F1; fields 16 and 18).</summary>
    internal sealed record Refused : ChocolateyStageValues
    {
        internal Refused(ChocolateyStageRefusal reason, string? installed, string? fingerprint)
        {
            if (!Enum.IsDefined(reason)) throw new ArgumentOutOfRangeException(nameof(reason));
            if (installed is not null) RequireVersion(installed, nameof(installed));
            if (fingerprint is not null) RequireFingerprint(fingerprint, nameof(fingerprint));
            bool consistent = reason switch
            {
                ChocolateyStageRefusal.SourceNotCommunity => installed is null && fingerprint is null,
                ChocolateyStageRefusal.ProfileMismatch => installed is not null && fingerprint is not null,
                _ => true,
            };
            if (!consistent) throw new ArgumentException("The values do not satisfy the stage-B predicates for the reason.", nameof(reason));
            Reason = reason;
            Installed = installed;
            Fingerprint = fingerprint;
        }

        internal ChocolateyStageRefusal Reason { get; }

        internal string? Installed { get; }

        internal string? Fingerprint { get; }
    }

    /// <summary>The <c>remaining</c> bound was exceeded.</summary>
    internal sealed record TimedOut : ChocolateyStageValues
    {
        internal TimedOut() { }
    }

    /// <summary>The work was cancelled.</summary>
    internal sealed record Cancelled : ChocolateyStageValues
    {
        internal Cancelled() { }
    }

    /// <summary>The item refused the call before any I/O (§5.4, rev-4 L5).</summary>
    internal sealed record OutOfOrder : ChocolateyStageValues
    {
        internal OutOfOrder() { }
    }

    private static string RequireVersion(string? value, string name) =>
        ChocolateyQueryParser.IsNormalizedVersion(value) ? value! : throw new ArgumentException("A normalized native version is required.", name);

    private static string RequireFingerprint(string? value, string name) =>
        ChocolateyCandidateValues.IsFingerprint(value) ? value! : throw new ArgumentException("A 64-character uppercase hex fingerprint is required.", name);
}

/// <summary>
/// ADR-031 design §8.1: a client self-update launch's settled binding state as a plain closed value (the rows of the
/// allowed-combination table) for the stage machine; no watchdog type crosses into Execution.
/// </summary>
internal enum ChocolateyClientRunFault
{
    None, SetupOverrideDuringRun, RootWritableDuringRun, WatchFailedDuringRun, ReleaseIncomplete,
    SetupOverrideDuringRunAndReleaseIncomplete, RootWritableDuringRunAndReleaseIncomplete, WatchFailedDuringRunAndReleaseIncomplete
}

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 design rev 6 §5.4 (rev-5 F1): the upgrade result, a closed record with exactly two
/// arms. <see cref="Observed"/> carries the launcher's wire facts; <see cref="OutOfOrder"/> is the dedicated pre-I/O value
/// returned without any launch when the one-use launch permission is absent (rev-4 L5).
/// </summary>
internal abstract record ChocolateyUpgradeResult
{
    private ChocolateyUpgradeResult() { }

    internal sealed record Observed : ChocolateyUpgradeResult
    {
        internal Observed(NativeProcessObservation observation) =>
            Observation = observation ?? throw new ArgumentNullException(nameof(observation));

        /// <summary>
        /// ADR-031 design §8.1: a client self-update launch also carries its binding's run state, read after the launch settled
        /// (the watchdog is joined and its first fault recorded before the launch task completes).
        /// </summary>
        internal Observed(NativeProcessObservation observation, ChocolateyClientWatchdogFault watchdogFault, bool releaseIncomplete)
            : this(observation) => RunFault = ToRunFault(watchdogFault, releaseIncomplete);

        internal NativeProcessObservation Observation { get; }

        /// <summary>The binding's run state as a plain closed value (None for the package route); the only one Execution reads.</summary>
        internal ChocolateyClientRunFault RunFault { get; }

        /// <summary>
        /// ADR-031 design §8.1 allowed-combination table. An undefined watchdog value is a failed watch (fail closed, as in the
        /// postcheck).
        /// </summary>
        internal static ChocolateyClientRunFault ToRunFault(ChocolateyClientWatchdogFault watchdogFault, bool releaseIncomplete) =>
            (watchdogFault, releaseIncomplete) switch
            {
                (ChocolateyClientWatchdogFault.None, false) => ChocolateyClientRunFault.None,
                (ChocolateyClientWatchdogFault.None, true) => ChocolateyClientRunFault.ReleaseIncomplete,
                (ChocolateyClientWatchdogFault.SetupOverrideDuringRun, false) => ChocolateyClientRunFault.SetupOverrideDuringRun,
                (ChocolateyClientWatchdogFault.SetupOverrideDuringRun, true) => ChocolateyClientRunFault.SetupOverrideDuringRunAndReleaseIncomplete,
                (ChocolateyClientWatchdogFault.RootWritableDuringRun, false) => ChocolateyClientRunFault.RootWritableDuringRun,
                (ChocolateyClientWatchdogFault.RootWritableDuringRun, true) => ChocolateyClientRunFault.RootWritableDuringRunAndReleaseIncomplete,
                (_, false) => ChocolateyClientRunFault.WatchFailedDuringRun,
                (_, true) => ChocolateyClientRunFault.WatchFailedDuringRunAndReleaseIncomplete,
            };
    }

    internal sealed record OutOfOrder : ChocolateyUpgradeResult
    {
        internal OutOfOrder() { }
    }
}
