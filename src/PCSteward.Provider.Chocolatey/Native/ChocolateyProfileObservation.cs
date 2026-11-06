using Microsoft.Win32.SafeHandles;

namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>Which side observes. Only <see cref="Helper"/> runs the §3 step 4 HTTP-cache check and keeps file handles.</summary>
internal enum ChocolateyObserverRole
{
    Desktop,
    Helper
}

/// <summary>Settled refusal reasons, in rev 3 §3.1 precedence order (the first is the strongest).</summary>
internal enum ChocolateyProfileRefusal
{
    MachineInputsUnsupported,
    SourceNotCommunity,
    InstalledMismatch,
    ProfileMismatch
}

/// <summary>One file the helper keeps open (<c>FILE_SHARE_READ</c> only) from observation onwards, with the node it was proven as.</summary>
internal readonly record struct ChocolateyHeldFile(string RelativePath, NativeNode Node, SafeFileHandle Handle);

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 design rev 6 §3 and §4 (M4, L2): the read-share-only handles the helper keeps on the
/// root engine, every release-tree file and every <c>hooks</c>/<c>extensions</c> code file it observed, so none of them can
/// be written, renamed, deleted or replaced while held. Private construction; only <see cref="ChocolateyProfileObserver"/>
/// creates and fills one, and it is sealed before <see cref="ChocolateyHeldClientBinding"/> takes it over (N3). The binding
/// releases only the code holds, once, after the upgrade's process creation, assignment and resume all succeeded
/// (<see cref="ReleaseCode"/>); disposing closes every remaining handle.
/// </summary>
internal sealed class ChocolateyHeldProfileFiles : IDisposable
{
    private readonly List<ChocolateyHeldFile> release = [];
    private readonly List<ChocolateyHeldFile> code = [];
    private readonly Lock gate = new();
    private bool sealedForUse;
    private bool codeReleased;
    private bool allReleased;
    private bool disposed;

    private ChocolateyHeldProfileFiles() { }

    /// <summary>The held root engine (<c>choco.exe</c>); its handle is also in <see cref="ReleaseFiles"/>.</summary>
    internal ChocolateyHeldFile Engine { get; private set; }

    /// <summary>Every compiled release-tree file, in manifest order.</summary>
    internal IReadOnlyList<ChocolateyHeldFile> ReleaseFiles => release.AsReadOnly();

    /// <summary>Every regular file under <c>hooks</c> and <c>extensions</c>, in walk order.</summary>
    internal IReadOnlyList<ChocolateyHeldFile> CodeFiles => code.AsReadOnly();

    internal bool IsDisposed
    {
        get { lock (gate) return disposed; }
    }

    internal bool IsSealed
    {
        get { lock (gate) return sealedForUse; }
    }

    /// <summary>True once <see cref="ReleaseCode"/> closed the code handles (or the holder was disposed).</summary>
    internal bool CodeReleased
    {
        get { lock (gate) return codeReleased || disposed; }
    }

    /// <summary>Observer only (an architecture test pins the call site).</summary>
    internal static ChocolateyHeldProfileFiles Begin() => new();

    internal void AddRelease(ChocolateyHeldFile file, bool engine)
    {
        Require(file);
        if (engine)
        {
            if (Engine.Handle is not null) throw new InvalidOperationException("The engine is already held.");
            Engine = file;
        }
        release.Add(file);
    }

    internal void AddCode(ChocolateyHeldFile file)
    {
        Require(file);
        code.Add(file);
    }

    /// <summary>Ends filling; requires the engine to be held.</summary>
    internal void Seal()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (Engine.Handle is null) throw new InvalidOperationException("No engine is held.");
            sealedForUse = true;
        }
    }

    /// <summary>
    /// Closes the <c>hooks</c>/<c>extensions</c> handles only (§4 L2); the engine and release-tree holds stay until
    /// <see cref="Dispose"/>. Binding only (an architecture test pins the call site). Idempotent.
    /// </summary>
    internal void ReleaseCode()
    {
        lock (gate)
        {
            if (!sealedForUse) throw new InvalidOperationException("The held file set is not sealed.");
            if (codeReleased || disposed) return;
            codeReleased = true;
            foreach (ChocolateyHeldFile file in code) file.Handle.Dispose();
        }
    }

    /// <summary>
    /// ADR-031 design §1 (client self-update only): closes <b>every</b> handle — engine, release tree and code — because the
    /// client's own setup must rename or delete each of them. Binding only (an architecture test pins the call site), and
    /// only after the upgrade's process creation, assignment and resume all succeeded. Identities stay readable; nothing
    /// launches afterwards (<see cref="CodeReleased"/> is true). Idempotent. Every handle is attempted; <see cref="AllReleased"/>
    /// becomes true only when each close succeeded, otherwise this throws after the attempt (design §2 R3: the caller records
    /// the partial release as a fault).
    /// </summary>
    internal void ReleaseAll()
    {
        lock (gate)
        {
            if (!sealedForUse) throw new InvalidOperationException("The held file set is not sealed.");
            if (disposed || allReleased) return;
            codeReleased = true;
            bool complete = true;
            foreach (ChocolateyHeldFile file in code.Concat(release))
            {
                try { file.Handle.Dispose(); }
                catch (Exception exception) when (exception is not OutOfMemoryException) { complete = false; }
            }
            allReleased = complete;
            if (!complete) throw new InvalidOperationException("Not every held client file could be released.");
        }
    }

    /// <summary>True once <see cref="ReleaseAll"/> closed every handle (client self-update only).</summary>
    internal bool AllReleased
    {
        get { lock (gate) return allReleased; }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            foreach (ChocolateyHeldFile file in release) file.Handle.Dispose();
            foreach (ChocolateyHeldFile file in code) file.Handle.Dispose();
        }
    }

    private void Require(ChocolateyHeldFile file)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (sealedForUse) throw new InvalidOperationException("The held file set is sealed.");
        ArgumentNullException.ThrowIfNull(file.Handle);
        if (file.Handle.IsInvalid || file.Handle.IsClosed || file.Node.Directory) throw new ArgumentException("A held regular-file handle is required.", nameof(file));
    }
}

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 design rev 6 §3: one complete static profile observation. Private construction;
/// produced only by <see cref="ChocolateyProfileObserver"/> after every §3 step passed. <see cref="Common"/> feeds
/// <see cref="ChocolateyCommonProfileFingerprint.Compute"/>, whose result is <see cref="Fingerprint"/>; <see cref="ClientState"/>
/// is the Task 90 M2 file-first baseline from the same reads. <see cref="HeldClient"/> is non-null only for the Helper role.
/// This is comparison data, not a dispatch permit.
/// </summary>
internal sealed class ChocolateyProfileObservation : IDisposable
{
    private ChocolateyProfileObservation(ChocolateyCommonProfileData common, string fingerprint, ChocolateyClientStateData clientState,
        ChocolateyNativeVersion installedVersion, bool targetLibBadResidue, ChocolateyHeldClientBinding? heldClient,
        ChocolateyReviewedRelease? release, ChocolateyReleaseResidue? releaseResidue)
    {
        Release = release;
        ReleaseResidue = releaseResidue;
        Common = common;
        Fingerprint = fingerprint;
        ClientState = clientState;
        InstalledVersion = installedVersion;
        TargetLibBadResidue = targetLibBadResidue;
        HeldClient = heldClient;
    }

    internal ChocolateyCommonProfileData Common { get; }

    internal string Fingerprint { get; }

    internal ChocolateyClientStateData ClientState { get; }

    internal ChocolateyNativeVersion InstalledVersion { get; }

    /// <summary>Disclosure only: <c>lib-bad</c> holds an entry starting with the target ID. It never establishes installed state.</summary>
    internal bool TargetLibBadResidue { get; }

    /// <summary>
    /// Helper role only (§4 M4, L2, L6): the retained root-engine binding over the held engine, release-tree and code files.
    /// Null for the Desktop role. Disposing the observation disposes the binding.
    /// </summary>
    internal ChocolateyHeldClientBinding? HeldClient { get; }

    /// <summary>
    /// ADR-031 design §3.2: the listed release whose complete manifest the installed client matched (selected by engine digest).
    /// The observer always sets it; null only for an observation a test built directly.
    /// </summary>
    internal ChocolateyReviewedRelease? Release { get; }

    /// <summary>
    /// ADR-031 design §3a: the disclosed finding for the one reviewed self-upgrade leftover, accepted because its bytes equal a
    /// listed release's file. Never held, fingerprinted or part of the stage-D identity; null when absent.
    /// </summary>
    internal ChocolateyReleaseResidue? ReleaseResidue { get; }

    /// <summary>Observer only (an architecture test pins the call site).</summary>
    internal static ChocolateyProfileObservation Create(ChocolateyCommonProfileData common, string fingerprint, ChocolateyClientStateData clientState,
        ChocolateyNativeVersion installedVersion, bool targetLibBadResidue, ChocolateyHeldClientBinding? heldClient,
        ChocolateyReviewedRelease? release = null, ChocolateyReleaseResidue? releaseResidue = null)
    {
        ArgumentNullException.ThrowIfNull(common);
        ArgumentException.ThrowIfNullOrEmpty(fingerprint);
        ArgumentNullException.ThrowIfNull(clientState);
        ArgumentNullException.ThrowIfNull(installedVersion);
        if (releaseResidue is not null && release is null)
            throw new ArgumentException("A leftover finding needs the matched installed release.", nameof(releaseResidue));
        return new ChocolateyProfileObservation(common, fingerprint, clientState, installedVersion, targetLibBadResidue, heldClient, release,
            releaseResidue);
    }

    public void Dispose() => HeldClient?.Dispose();
}

/// <summary>
/// ADR-031 design §3a fallback: the disclosed cause of a MachineInputsUnsupported refusal. <see cref="UnlistedReleaseLeftover"/>
/// means the only failure was <c>helpers\Chocolatey.PowerShell.dll.old</c> whose bytes match no listed release; PC Steward never
/// deletes it and reports it as "Installed, not verified" with a manual next step. <see cref="SetupAccessOverride"/> (design
/// §2 R5, client mode, Helper role only): a name pinned setup.psm1 uses to weaken or skip the root ACL is set, so the client
/// self-update does not launch.
/// </summary>
internal enum ChocolateyProfileRefusalDetail { None, UnlistedReleaseLeftover, SetupAccessOverride }

/// <summary>
/// Closed result of <see cref="ChocolateyProfileObserver.Observe"/> (§3, rev-4 L3): Observed, a settled Refused with the
/// values "as far as observed", or one of the two interrupt arms. <see cref="TimedOut"/> and <see cref="Cancelled"/> map to
/// Undetermined, never to a settled refusal. Every arm has private construction; each factory is called only by the observer.
/// </summary>
internal abstract record ChocolateyProfileResult
{
    private ChocolateyProfileResult() { }

    internal sealed record Observed : ChocolateyProfileResult
    {
        private Observed(ChocolateyProfileObservation observation) => Observation = observation;

        internal ChocolateyProfileObservation Observation { get; }

        internal static Observed Create(ChocolateyProfileObservation observation)
        {
            ArgumentNullException.ThrowIfNull(observation);
            return new Observed(observation);
        }
    }

    /// <summary>
    /// MachineInputsUnsupported and SourceNotCommunity carry no values (S8); InstalledMismatch carries the observed installed
    /// version; ProfileMismatch carries the observed installed version and fingerprint.
    /// </summary>
    internal sealed record Refused : ChocolateyProfileResult
    {
        private Refused(ChocolateyProfileRefusal reason, string? observedInstalled, string? observedFingerprint, ChocolateyProfileRefusalDetail detail)
        {
            Detail = detail;
            Reason = reason;
            ObservedInstalled = observedInstalled;
            ObservedFingerprint = observedFingerprint;
        }

        internal ChocolateyProfileRefusal Reason { get; }

        internal string? ObservedInstalled { get; }

        internal string? ObservedFingerprint { get; }

        /// <summary>ADR-031 design §3a fallback: why a MachineInputsUnsupported refusal happened, when it has a disclosed cause.</summary>
        internal ChocolateyProfileRefusalDetail Detail { get; }

        internal static Refused Create(ChocolateyProfileRefusal reason, string? observedInstalled, string? observedFingerprint,
            ChocolateyProfileRefusalDetail detail = ChocolateyProfileRefusalDetail.None)
        {
            if (!Enum.IsDefined(detail) || detail != ChocolateyProfileRefusalDetail.None && reason != ChocolateyProfileRefusal.MachineInputsUnsupported)
                throw new ArgumentException("A refusal detail applies only to MachineInputsUnsupported.", nameof(detail));
            bool valid = reason switch
            {
                ChocolateyProfileRefusal.MachineInputsUnsupported or ChocolateyProfileRefusal.SourceNotCommunity =>
                    observedInstalled is null && observedFingerprint is null,
                ChocolateyProfileRefusal.InstalledMismatch => !string.IsNullOrEmpty(observedInstalled) && observedFingerprint is null,
                ChocolateyProfileRefusal.ProfileMismatch => !string.IsNullOrEmpty(observedInstalled) && !string.IsNullOrEmpty(observedFingerprint),
                _ => false,
            };
            if (!valid) throw new ArgumentException("The observed values do not match the refusal reason.", nameof(reason));
            return new Refused(reason, observedInstalled, observedFingerprint, detail);
        }
    }

    /// <summary>The <c>remaining</c> budget was exceeded before the observation settled.</summary>
    internal sealed record TimedOut : ChocolateyProfileResult
    {
        private TimedOut() { }

        internal static TimedOut Create() => new();
    }

    /// <summary>The caller's cancellation token was signalled before the observation settled.</summary>
    internal sealed record Cancelled : ChocolateyProfileResult
    {
        private Cancelled() { }

        internal static Cancelled Create() => new();
    }
}
