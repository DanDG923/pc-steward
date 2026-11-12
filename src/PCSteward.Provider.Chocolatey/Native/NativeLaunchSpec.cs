using System.Collections.ObjectModel;

namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>
/// Immutable, validated input to the N0 launcher (Task 50 design rev 6 §4.1, rev-4 M1, rev-5 F3). Construction is
/// private. The only production factory is <see cref="FromBinding"/>, whose only call site is
/// <see cref="LaunchJob.RunAsync"/>; the test-only factory is banned from every <c>src</c> caller (both
/// checked on compiled IL). The environment is the complete child environment; it is never merged with the current
/// process environment. A spec from <see cref="FromBinding"/> also carries its binding, so the launcher refuses to create
/// a process once the binding no longer holds the engine, and releases the code holds after an upgrade's resume (§4 L2).
/// </summary>
internal sealed class NativeLaunchSpec
{
    private readonly ChocolateyHeldClientBinding? client;
    private readonly bool releaseCodeAfterResume;
    private readonly bool releaseAllAfterResume;

    private NativeLaunchSpec(string imagePath, IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment, string workingDirectory, ChocolateyHeldClientBinding? client = null,
        bool releaseCodeAfterResume = false, bool releaseAllAfterResume = false)
    {
        this.client = client;
        this.releaseCodeAfterResume = releaseCodeAfterResume;
        this.releaseAllAfterResume = releaseAllAfterResume;
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(environment);
        ImagePath = NativeCommandLine.ValidateImagePath(imagePath, nameof(imagePath));
        WorkingDirectory = NativeCommandLine.ValidateLocalPath(workingDirectory, nameof(workingDirectory));
        string[] copiedArguments = [.. arguments];
        if (copiedArguments.Any(static argument => argument is null || argument.Any(char.IsControl)))
            throw new ArgumentException("Arguments must be non-null and free of control characters.", nameof(arguments));
        Dictionary<string, string> copiedEnvironment = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, string value) in environment)
        {
            NativeCommandLine.ValidateVariable(name, value);
            if (!copiedEnvironment.TryAdd(name, value))
                throw new ArgumentException("Environment names must be unique ignoring case.", nameof(environment));
        }
        Arguments = new ReadOnlyCollection<string>(copiedArguments);
        Environment = new ReadOnlyDictionary<string, string>(copiedEnvironment);
        // Both native forms are checked once here so the launcher cannot meet an unformattable spec.
        _ = NativeCommandLine.Build(ImagePath, Arguments);
        _ = NativeCommandLine.BuildEnvironmentBlock(Environment);
    }

    internal string ImagePath { get; }
    internal IReadOnlyList<string> Arguments { get; }
    internal IReadOnlyDictionary<string, string> Environment { get; }
    internal string WorkingDirectory { get; }

    /// <summary>
    /// The launcher's pre-creation check: a binding spec may create a process only while its binding is undisposed and its
    /// code holds are in place. A test-only spec has no binding.
    /// </summary>
    internal bool MayCreateProcess => client is null || client.CanLaunch;

    /// <summary>
    /// Called by the launcher once <c>CreateProcessW</c>, <c>AssignProcessToJobObject</c> and <c>ResumeThread</c> have all
    /// succeeded: an upgrade spec releases its binding's code holds (§4 L2); every other spec does nothing.
    /// </summary>
    internal void OnResumed()
    {
        // ADR-031 design §1: a client self-update upgrade releases every hold; a package upgrade only the code holds.
        if (releaseAllAfterResume) client!.ReleaseAllAfterResume();
        else if (releaseCodeAfterResume) client!.ReleaseCodeAfterResume();
    }

    /// <summary>
    /// Called by the launcher when <see cref="OnResumed"/> threw. Design §2 R3: a client self-update records the partial
    /// release on its binding, which forces the postcheck to Unverified; the package route's outcome is unchanged (its code
    /// holds only stay until the binding is disposed).
    /// </summary>
    internal void OnReleaseFaulted()
    {
        if (releaseAllAfterResume) client!.RecordReleaseIncomplete();
    }

    /// <summary>
    /// ADR-031 design §2 R5, called by the launcher inside its handle gate immediately before process creation: a client
    /// self-update upgrade may create its process only while <see cref="ChocolateySetupEnvironmentGuard.Permits"/> holds for
    /// this spec's complete environment and a fresh <see cref="ChocolateySetupEnvironmentReader.Read"/>; a refusal is
    /// recorded on the binding and nothing is created. Every other spec is unaffected. This narrows, but cannot close,
    /// the race (the value can be written after this check).
    /// </summary>
    internal bool SetupEnvironmentPermitsLaunch()
    {
        if (!releaseAllAfterResume) return true;
        if (ChocolateySetupEnvironmentGuard.Permits(Environment, client!.LaunchPaths.ChocolateyRoot, ChocolateySetupEnvironmentReader.Read()))
            return true;
        client.RecordSetupAccessOverrideRefused();
        return false;
    }

    /// <summary>
    /// ADR-031 design §2.1 R5, called by the launcher before its last pre-creation checks: a client self-update upgrade
    /// spec is watched (<see cref="ChocolateyClientWatchTargets.FromBinding"/> over its binding); every other spec, the
    /// package route included, is not (null).
    /// </summary>
    internal ChocolateyClientWatchTargets? WatchTargets() =>
        releaseAllAfterResume ? ChocolateyClientWatchTargets.FromBinding(client!) : null;

    /// <summary>
    /// Called by the launcher after the watchdog of a client self-update launch was stopped: a fault is recorded on the
    /// binding (design §2.1 R5), which forces the postcheck to a watchdog verdict. Every other spec records nothing.
    /// </summary>
    internal void OnWatchdogStopped(ChocolateyClientWatchdogFault fault)
    {
        if (releaseAllAfterResume && fault != ChocolateyClientWatchdogFault.None) client!.RecordWatchdogFault(fault);
    }

    /// <summary>
    /// N3 (rev-5 F3): the only production factory; its only call site is <see cref="LaunchJob.RunAsync"/> (architecture
    /// test). No string comes from the caller: the image is the held engine, argv[1..] is the sealed command's fixed list,
    /// the environment is <see cref="ChocolateyCompiledEnvironment.Create"/> over the binding's launch paths, and the working
    /// directory is the Chocolatey root. A list or upgrade command must name the binding's target exactly.
    /// </summary>
    internal static NativeLaunchSpec FromBinding(ChocolateyHeldClientBinding client, ChocolateyCompiledCommand command)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(command);
        if (!client.CanLaunch) throw new InvalidOperationException("The held client binding can no longer launch.");
        IReadOnlyList<string> arguments = command.Arguments;
        bool package = client.Purpose == ChocolateyBindingPurpose.PackageUpdate;
        bool clientUpdate = client.Purpose == ChocolateyBindingPurpose.ClientSelfUpdate && client.InstalledRelease is not null;
        bool upgrade = command switch
        {
            ChocolateyCompiledCommand.ListExact list when package && IsTarget(client, list.PackageId) && arguments.Count == 5 &&
                arguments[0] == "list" && arguments[1] == list.PackageId => false,
            ChocolateyCompiledCommand.Outdated when package && arguments.Count > 0 && arguments[0] == "outdated" => false,
            ChocolateyCompiledCommand.Upgrade update when package && IsTarget(client, update.PackageId) && arguments.Count > 3 &&
                arguments[0] == "upgrade" && arguments[1] == update.PackageId && arguments[3] == update.CandidateVersion => true,
            // ADR-031 design §3.6: a client binding launches only the client commands for the release it holds.
            ChocolateyCompiledCommand.ListClient list when clientUpdate && ReferenceEquals(list.InstalledRelease, client.InstalledRelease) &&
                arguments.Count == 5 && arguments[0] == "list" && IsTarget(client, arguments[1]) => false,
            ChocolateyCompiledCommand.UpgradeClient update when clientUpdate && ReferenceEquals(update.InstalledRelease, client.InstalledRelease) &&
                ReferenceEquals(client.InstalledRelease!.List.OfferTarget(client.InstalledRelease), update.TargetRelease) &&
                arguments.Count > 3 && arguments[0] == "upgrade" && IsTarget(client, arguments[1]) &&
                arguments[3] == update.TargetRelease.Version => true,
            _ => throw new ArgumentException("The command is not a compiled command for this binding.", nameof(command)),
        };
        // N3 review L2: at most one upgrade launch per binding, claimed atomically before any process can exist.
        if (upgrade && !client.TryClaimUpgradeLaunch())
            throw new InvalidOperationException("The held client binding has already launched its upgrade.");
        ChocolateyLaunchPaths paths = client.LaunchPaths;
        return new NativeLaunchSpec(client.EnginePath, arguments, ChocolateyCompiledEnvironment.Create(paths), paths.ChocolateyRoot,
            client, releaseCodeAfterResume: upgrade && package, releaseAllAfterResume: upgrade && clientUpdate);
    }

    /// <summary>
    /// D4b design §5.1: the machine WinGet route's factory; its only call site is <see cref="WingetMachineHelperWork"/> (architecture
    /// test). The image must be the trusted App Installer <c>winget.exe</c> resolved again here, immediately before launch, and equal
    /// to the one the item's stages bound; the first argument is one of the route's three fixed verbs; the environment is the
    /// complete fixed block with <c>TEMP</c> and <c>TMP</c> naming <paramref name="runFolder"/>, which must still be a fresh
    /// administrator-only folder and is the working directory. No other spec carries a binding, so the Chocolatey hooks never run.
    /// </summary>
    internal static NativeLaunchSpec ForWingetMachine(string boundWinget, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment,
        string runFolder)
    {
        ArgumentNullException.ThrowIfNull(boundWinget);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(runFolder);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The machine WinGet route runs only on Windows.");
        string? trusted = PCSteward.Provider.WinGet.HelperTrustedWinget.TryResolve();
        if (trusted is null || !string.Equals(trusted, boundWinget, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The trusted WinGet client changed or is unavailable.");
        if (arguments.Count == 0 || arguments[0] is not ("upgrade" or "download" or "source"))
            throw new ArgumentException("The command is not a machine WinGet route command.", nameof(arguments));
        if (!environment.TryGetValue("TEMP", out string? temp) || !environment.TryGetValue("TMP", out string? tmp) ||
            !string.Equals(temp, runFolder, StringComparison.Ordinal) || !string.Equals(tmp, runFolder, StringComparison.Ordinal) ||
            !PCSteward.Provider.WinGet.HelperProtectedRunFolder.IsFreshProtected(runFolder, expectEmpty: false))
            throw new ArgumentException("The run folder is not the protected TEMP of this launch.", nameof(runFolder));
        return new NativeLaunchSpec(trusted, arguments, environment, runFolder);
    }

    /// <summary>Test-only factory (rev-4 M1). Any <c>src</c> caller fails the architecture tests.</summary>
    internal static NativeLaunchSpec ForTests(string imagePath, IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment, string workingDirectory) =>
        new(imagePath, arguments, environment, workingDirectory);

    private static bool IsTarget(ChocolateyHeldClientBinding client, string packageId) =>
        string.Equals(client.TargetId, packageId, StringComparison.Ordinal);
}
