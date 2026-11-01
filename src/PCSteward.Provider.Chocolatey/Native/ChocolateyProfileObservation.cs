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
/// be written, renamed, deleted or replaced while held. This is an interim internal holder: N3 owns
/// <c>ChocolateyHeldClientBinding</c>, which will take these handles over (and release the code holds after process
/// creation). Private construction; only <see cref="ChocolateyProfileObserver"/> creates and fills one, and it is sealed
/// before an observation carries it. Disposing it closes every handle.
/// </summary>
internal sealed class ChocolateyHeldProfileFiles : IDisposable
{
    private readonly List<ChocolateyHeldFile> release = [];
    private readonly List<ChocolateyHeldFile> code = [];
    private bool sealedForUse;
    private bool disposed;

    private ChocolateyHeldProfileFiles() { }

    /// <summary>The held root engine (<c>choco.exe</c>); its handle is also in <see cref="ReleaseFiles"/>.</summary>
    internal ChocolateyHeldFile Engine { get; private set; }

    /// <summary>Every compiled release-tree file, in manifest order.</summary>
    internal IReadOnlyList<ChocolateyHeldFile> ReleaseFiles => release.AsReadOnly();

    /// <summary>Every regular file under <c>hooks</c> and <c>extensions</c>, in walk order.</summary>
    internal IReadOnlyList<ChocolateyHeldFile> CodeFiles => code.AsReadOnly();

    internal bool IsDisposed => disposed;

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
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Engine.Handle is null) throw new InvalidOperationException("No engine is held.");
        sealedForUse = true;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (ChocolateyHeldFile file in release) file.Handle.Dispose();
        foreach (ChocolateyHeldFile file in code) file.Handle.Dispose();
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
/// is the Task 90 M2 file-first baseline from the same reads. <see cref="HeldFiles"/> is non-null only for the Helper role.
/// This is comparison data, not a dispatch permit.
/// </summary>
internal sealed class ChocolateyProfileObservation : IDisposable
{
    private ChocolateyProfileObservation(ChocolateyCommonProfileData common, string fingerprint, ChocolateyClientStateData clientState,
        ChocolateyNativeVersion installedVersion, bool targetLibBadResidue, ChocolateyHeldProfileFiles? heldFiles)
    {
        Common = common;
        Fingerprint = fingerprint;
        ClientState = clientState;
        InstalledVersion = installedVersion;
        TargetLibBadResidue = targetLibBadResidue;
        HeldFiles = heldFiles;
    }

    internal ChocolateyCommonProfileData Common { get; }

    internal string Fingerprint { get; }

    internal ChocolateyClientStateData ClientState { get; }

    internal ChocolateyNativeVersion InstalledVersion { get; }

    /// <summary>Disclosure only: <c>lib-bad</c> holds an entry starting with the target ID. It never establishes installed state.</summary>
    internal bool TargetLibBadResidue { get; }

    /// <summary>Helper role only (§4 M4, L2): the held engine, release-tree and code files. Null for the Desktop role.</summary>
    internal ChocolateyHeldProfileFiles? HeldFiles { get; }

    /// <summary>Observer only (an architecture test pins the call site).</summary>
    internal static ChocolateyProfileObservation Create(ChocolateyCommonProfileData common, string fingerprint, ChocolateyClientStateData clientState,
        ChocolateyNativeVersion installedVersion, bool targetLibBadResidue, ChocolateyHeldProfileFiles? heldFiles)
    {
        ArgumentNullException.ThrowIfNull(common);
        ArgumentException.ThrowIfNullOrEmpty(fingerprint);
        ArgumentNullException.ThrowIfNull(clientState);
        ArgumentNullException.ThrowIfNull(installedVersion);
        return new ChocolateyProfileObservation(common, fingerprint, clientState, installedVersion, targetLibBadResidue, heldFiles);
    }

    public void Dispose() => HeldFiles?.Dispose();
}

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
        private Refused(ChocolateyProfileRefusal reason, string? observedInstalled, string? observedFingerprint)
        {
            Reason = reason;
            ObservedInstalled = observedInstalled;
            ObservedFingerprint = observedFingerprint;
        }

        internal ChocolateyProfileRefusal Reason { get; }

        internal string? ObservedInstalled { get; }

        internal string? ObservedFingerprint { get; }

        internal static Refused Create(ChocolateyProfileRefusal reason, string? observedInstalled, string? observedFingerprint)
        {
            bool valid = reason switch
            {
                ChocolateyProfileRefusal.MachineInputsUnsupported or ChocolateyProfileRefusal.SourceNotCommunity =>
                    observedInstalled is null && observedFingerprint is null,
                ChocolateyProfileRefusal.InstalledMismatch => !string.IsNullOrEmpty(observedInstalled) && observedFingerprint is null,
                ChocolateyProfileRefusal.ProfileMismatch => !string.IsNullOrEmpty(observedInstalled) && !string.IsNullOrEmpty(observedFingerprint),
                _ => false,
            };
            if (!valid) throw new ArgumentException("The observed values do not match the refusal reason.", nameof(reason));
            return new Refused(reason, observedInstalled, observedFingerprint);
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
