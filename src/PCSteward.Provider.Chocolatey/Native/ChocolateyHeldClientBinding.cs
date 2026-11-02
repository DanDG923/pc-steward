namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>ADR-031: what a held client binding may launch.</summary>
internal enum ChocolateyBindingPurpose
{
    /// <summary>The Task 50 package route: list, outdated and the fixed package upgrade; code holds released after resume.</summary>
    PackageUpdate,
    /// <summary>The client self-update: client list and the fixed client upgrade; every hold released after resume (design §1).</summary>
    ClientSelfUpdate
}

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 design rev 6 §4 (M4, L2, L6) and §4.1: the retained root-engine binding. Private
/// construction; produced only by <see cref="ChocolateyProfileObserver"/> (Helper role) from the sealed
/// <see cref="ChocolateyHeldProfileFiles"/> it filled, plus the launch paths it resolved natively. The binding captured in
/// stage B is the one that launches (L6).
/// <list type="bullet">
/// <item>The root engine and every release-tree file (<c>helpers</c>/<c>tools</c>) stay held, read-share only, until the
/// binding is disposed, which the owner does only after the last launch has exited (M4).</item>
/// <item>The <c>hooks</c>/<c>extensions</c> code files stay held until the <b>upgrade</b> launch's <c>CreateProcessW</c>,
/// <c>AssignProcessToJobObject</c> and <c>ResumeThread</c> have all succeeded; the launcher then calls
/// <see cref="ReleaseCodeAfterResume"/> (L2). Query launches never release them. After that release the binding launches
/// nothing more.</item>
/// <item>No <c>lib</c> or registration file is ever held (Profile V2); <see cref="Create"/> refuses a holder with one.</item>
/// </list>
/// <see cref="EnginePath"/> is the held engine's proven full path and <see cref="LaunchPaths"/> feeds only
/// <see cref="ChocolateyCompiledEnvironment"/>; no other string reaches the launcher (§4.1, rev-2 M4). A binding is
/// target-specific: a list or upgrade command for any other package ID is refused before launch.
/// </summary>
internal sealed class ChocolateyHeldClientBinding : IDisposable
{
    internal const string EngineName = "choco.exe";

    private readonly ChocolateyHeldProfileFiles files;

    private ChocolateyHeldClientBinding(ChocolateyHeldProfileFiles files, ChocolateyLaunchPaths launchPaths, string targetId,
        ChocolateyBindingPurpose purpose, ChocolateyReviewedRelease? installedRelease, RequesterToken? requester)
    {
        Purpose = purpose;
        InstalledRelease = installedRelease;
        Requester = requester;
        this.files = files;
        LaunchPaths = launchPaths;
        TargetId = targetId;
        EnginePath = files.Engine.Node.FullPath;
    }

    /// <summary>The held root engine's full path (<c>&lt;root&gt;\choco.exe</c>), never the <c>bin</c> shim.</summary>
    internal string EnginePath { get; }

    /// <summary>The natively resolved launch paths; <see cref="ChocolateyLaunchPaths.ChocolateyRoot"/> is the working directory.</summary>
    internal ChocolateyLaunchPaths LaunchPaths { get; }

    /// <summary>The package ID the observation was made for; list and upgrade commands must name it exactly.</summary>
    internal string TargetId { get; }

    /// <summary>
    /// ADR-031: a package binding accepts only the package commands; a client self-update binding accepts only the client
    /// commands for <see cref="InstalledRelease"/>, and its upgrade releases every hold after resume.
    /// </summary>
    internal ChocolateyBindingPurpose Purpose { get; }

    /// <summary>Client self-update only: the listed release the observer matched (the installed client). Null for a package binding.</summary>
    internal ChocolateyReviewedRelease? InstalledRelease { get; }

    /// <summary>
    /// Client self-update only (design §2.1 R5 watchdog): the requester token the observation that produced this binding
    /// evaluated with; the watchdog re-evaluates the requester's access with it while the upgrade runs. Not owned: the
    /// observer's caller disposes it after the binding. Null for a package binding.
    /// </summary>
    internal RequesterToken? Requester { get; }

    /// <summary>True once a client self-update's resumed upgrade released every hold.</summary>
    internal bool AllReleased => files.AllReleased;

    internal ChocolateyHeldFile Engine => files.Engine;

    internal IReadOnlyList<ChocolateyHeldFile> ReleaseFiles => files.ReleaseFiles;

    /// <summary>The observed code files (identities stay available for the stage-D comparison after release).</summary>
    internal IReadOnlyList<ChocolateyHeldFile> CodeFiles => files.CodeFiles;

    /// <summary>The code holds are still in place.</summary>
    internal bool CodeHeld => !files.CodeReleased;

    internal bool IsDisposed => files.IsDisposed;

    /// <summary>Not disposed and the code holds still in place (no upgrade has been resumed through this binding).</summary>
    internal bool CanLaunch => !files.IsDisposed && !files.CodeReleased;

    /// <summary>
    /// The launch-path rule shared by the observer (step 6, which refuses on false) and <see cref="Create"/> (which throws):
    /// the Chocolatey root is the observed root and the compiled environment can be formatted from the paths.
    /// </summary>
    internal static bool IsLaunchable(ChocolateyLaunchPaths launchPaths, string observedRoot)
    {
        ArgumentNullException.ThrowIfNull(launchPaths);
        ArgumentNullException.ThrowIfNull(observedRoot);
        if (!ProtectedNodeReader.IsCanonicalLocalPath(launchPaths.ChocolateyRoot) ||
            !string.Equals(launchPaths.ChocolateyRoot, observedRoot, StringComparison.Ordinal))
            return false;
        try
        {
            IReadOnlyDictionary<string, string> environment = ChocolateyCompiledEnvironment.Create(launchPaths);
            return string.Equals(environment["ChocolateyInstall"], launchPaths.ChocolateyRoot, StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Observer only (an architecture test pins the call site): takes ownership of a sealed, undisposed holder whose engine
    /// is <c>&lt;ChocolateyRoot&gt;\choco.exe</c>, whose release files are the root files and <c>helpers</c>/<c>tools</c>
    /// only, and whose code files are under <c>hooks</c>/<c>extensions</c> only. Any violation throws; the observer has
    /// already established every condition, so a throw is an internal fault.
    /// </summary>
    internal static ChocolateyHeldClientBinding Create(ChocolateyHeldProfileFiles files, ChocolateyLaunchPaths launchPaths, string targetId)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(launchPaths);
        if (!ChocolateyNativePackageId.IsSupported(targetId) || ChocolateyClientReleasePolicy.IsClientTarget(targetId))
            throw new ArgumentException("A supported non-client target is required.", nameof(targetId));
        Validate(files, launchPaths);
        return new ChocolateyHeldClientBinding(files, launchPaths, targetId, ChocolateyBindingPurpose.PackageUpdate, null, null);
    }

    /// <summary>
    /// ADR-031 design §3.6, observer only (an architecture test pins the call site): the client self-update binding over the
    /// same sealed holder rules, for target <c>chocolatey</c> and the listed release the observer matched by engine digest.
    /// <paramref name="requester"/> is the observation's requester token, kept (not owned) for the R5 runtime watchdog.
    /// </summary>
    internal static ChocolateyHeldClientBinding CreateForClientUpdate(ChocolateyHeldProfileFiles files, ChocolateyLaunchPaths launchPaths,
        ChocolateyReviewedRelease installedRelease, RequesterToken requester)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(launchPaths);
        ArgumentNullException.ThrowIfNull(installedRelease);
        ArgumentNullException.ThrowIfNull(requester);
        if (!installedRelease.List.Contains(installedRelease))
            throw new ArgumentException("The installed release must belong to its reviewed list.", nameof(installedRelease));
        Validate(files, launchPaths);
        return new ChocolateyHeldClientBinding(files, launchPaths, PCSteward.Domain.ReviewedUpdateOperation.ChocolateyClientPackageId,
            ChocolateyBindingPurpose.ClientSelfUpdate, installedRelease, requester);
    }

    private static void Validate(ChocolateyHeldProfileFiles files, ChocolateyLaunchPaths launchPaths)
    {
        if (files.IsDisposed || !files.IsSealed || files.CodeReleased)
            throw new ArgumentException("A sealed, fully held file set is required.", nameof(files));
        if (!IsLaunchable(launchPaths, launchPaths.ChocolateyRoot))
            throw new ArgumentException("The launch paths cannot form the compiled environment.", nameof(launchPaths));
        ChocolateyHeldFile engine = files.Engine;
        if (engine.Handle is null || engine.RelativePath != EngineName || engine.Node.Directory ||
            // The walked path carries the on-disk case of the final path; Windows paths compare case-insensitively.
            !string.Equals(engine.Node.FullPath, launchPaths.ChocolateyRoot + '\\' + EngineName, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The held engine is not the root engine of the launch root.", nameof(files));
        foreach (ChocolateyHeldFile file in files.ReleaseFiles)
            if (!ChocolateyProfileRules.IsReleasePath(file.RelativePath) || file.Node.Directory)
                throw new ArgumentException("A held release file is outside the release tree.", nameof(files));
        foreach (ChocolateyHeldFile file in files.CodeFiles)
        {
            string top = file.RelativePath.Split('\\')[0];
            if (file.Node.Directory ||
                !(top.Equals("hooks", StringComparison.OrdinalIgnoreCase) || top.Equals("extensions", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("A held code file is outside hooks and extensions.", nameof(files));
        }
    }

    /// <summary>
    /// Launcher only (an architecture test pins the call site): called once the upgrade launch's <c>CreateProcessW</c>,
    /// assignment and resume have all succeeded. Closes the code handles; the engine and release tree stay held.
    /// </summary>
    internal void ReleaseCodeAfterResume() => files.ReleaseCode();

    /// <summary>
    /// ADR-031 design §1, launcher only (an architecture test pins the call site): a client self-update binding's upgrade
    /// launch, once <c>CreateProcessW</c>, assignment and resume have all succeeded, closes every hold (engine, release tree,
    /// code), because the client's setup renames or deletes each of them. Throws for a package binding.
    /// </summary>
    internal void ReleaseAllAfterResume()
    {
        if (Purpose != ChocolateyBindingPurpose.ClientSelfUpdate)
            throw new InvalidOperationException("Only a client self-update binding releases every hold.");
        files.ReleaseAll();
    }

    private int releaseIncomplete;
    private int setupAccessOverrideRefused;

    /// <summary>
    /// Design §2 R3 (client self-update only): the resumed upgrade's release of every hold failed part-way. The postcheck then
    /// reports <c>ReleaseIncomplete</c> (Unverified) whatever the re-observation shows. Never set for a package binding.
    /// </summary>
    internal bool ReleaseIncomplete => Volatile.Read(ref releaseIncomplete) != 0;

    /// <summary>
    /// Design §2 R5: the launcher refused to create the client upgrade process because a setup override name was set
    /// immediately before launch (the launch reports NotStarted; no process exists).
    /// </summary>
    internal bool SetupAccessOverrideRefused => Volatile.Read(ref setupAccessOverrideRefused) != 0;

    /// <summary>Launch spec only (an architecture test pins the call site): records a failed release after resume (client binding only).</summary>
    internal void RecordReleaseIncomplete()
    {
        if (Purpose == ChocolateyBindingPurpose.ClientSelfUpdate) Volatile.Write(ref releaseIncomplete, 1);
    }

    /// <summary>Launch spec only (an architecture test pins the call site): records the pre-creation setup-override refusal.</summary>
    internal void RecordSetupAccessOverrideRefused() => Volatile.Write(ref setupAccessOverrideRefused, 1);

    private int watchdogFault;

    /// <summary>
    /// Design §2.1 R5 (client self-update only): the runtime watchdog terminated the upgrade job while it ran. The postcheck
    /// then reports a watchdog verdict (never Verified) whatever the re-observation shows; Chocolatey may be partly updated.
    /// </summary>
    internal ChocolateyClientWatchdogFault WatchdogFault => (ChocolateyClientWatchdogFault)Volatile.Read(ref watchdogFault);

    /// <summary>Launch spec only (an architecture test pins the call site): records the first watchdog fault (client binding only).</summary>
    internal void RecordWatchdogFault(ChocolateyClientWatchdogFault fault)
    {
        if (Purpose == ChocolateyBindingPurpose.ClientSelfUpdate && fault != ChocolateyClientWatchdogFault.None && Enum.IsDefined(fault))
            Interlocked.CompareExchange(ref watchdogFault, (int)fault, (int)ChocolateyClientWatchdogFault.None);
    }

    private int upgradeClaimed;

    /// <summary>
    /// N3 review L2: an upgrade spec claims this one-shot flag atomically when it is built, so two overlapping upgrade
    /// launches on one binding cannot both pass <see cref="CanLaunch"/> before the code holds are released.
    /// </summary>
    internal bool TryClaimUpgradeLaunch() => Interlocked.Exchange(ref upgradeClaimed, 1) == 0;

    public void Dispose() => files.Dispose();
}
