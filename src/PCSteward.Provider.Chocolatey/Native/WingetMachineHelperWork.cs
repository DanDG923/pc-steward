using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Provider.WinGet;

namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>
/// Phase D stage D4b design §5 and §5.1 (FR-UPD-15, FR-UPD-01/03, TM-03/04/07/13): the native stage work of the machine WinGet route
/// inside the single helper item (<see cref="NativeChocolateyHelperItem"/>), which still enforces the stage order, the one-use launch
/// permission, settlement and disposal. It handles one reviewed <see cref="UpdateRouteContract.WinGetMachineSilentV1"/> operation:
/// <list type="bullet">
/// <item><b>B</b> (static): the route shape (H2), the trusted <c>winget.exe</c> found without WinRT and its C++ runtime folder (H4),
/// the requester's WinGet settings on the allow-list (H7) and the one HKLM registration whose digest equals the reviewed one (H6).
/// The fingerprint is that registration digest;</item>
/// <item><b>C</b>: <c>winget source export --name winget</c> through the launcher with a fresh protected <c>TEMP</c>, which must be
/// exactly the trusted default source (H5). Then H7b: <c>winget download</c> of the reviewed installer into a second fresh protected
/// folder, whose exported manifest (<see cref="WingetExportedManifest"/>) and installer must equal the reviewed manifest digest and
/// installer SHA-256, with machine scope, no dependencies and no agreements;</item>
/// <item><b>D</b>: stage B again, which must find the same client and the same registration;</item>
/// <item><b>Launch</b>: the fixed <c>upgrade</c> line (§4) with its own fresh protected <c>TEMP</c> (§6 S1) and the fixed environment.</item>
/// </list>
/// Refusals use the existing closed reasons: a registration that is gone or changed is <c>InstalledMismatch</c>; a source query that
/// fails or shows anything but the trusted source, and an H7b download that fails or does not match, is <c>PreflightQueryFailed</c>;
/// everything else is <c>MachineInputsUnsupported</c>.
/// Version fields carry the frozen operation's reviewed text, checked by the reply's own grammar (§5.1). Nothing here admits the
/// route: the review gate and the helper's A3 capability check admit it from stage 3.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WingetMachineHelperWork : IDisposable
{
    /// <summary>The <c>source export</c> query's own budget, capped by what is left of the endpoint's.</summary>
    internal static readonly TimeSpan SourceQueryBudget = TimeSpan.FromSeconds(60);

    /// <summary>The upgrade's own budget, capped by what is left of the endpoint's route budget (27 min, D4b design §5.5).</summary>
    internal static readonly TimeSpan UpgradeBudget = TimeSpan.FromMinutes(15);

    /// <summary>The H7b download's own budget, capped by what is left of the endpoint's.</summary>
    internal static readonly TimeSpan DownloadBudget = TimeSpan.FromMinutes(10);

    private readonly Lock gate = new();
    private ReviewedUpdateOperation? operation;
    private WinGetMachineEvidence? evidence;
    private string? winget;
    private string? runtimeFolder;
    private string? registrationKey;
    private WinGetMachineRegistrationView registrationView;
    private bool unsettledLaunch;

    /// <summary>True only when every launch of this work settled.</summary>
    internal bool LaunchesSettled
    {
        get
        {
            lock (gate) return !unsettledLaunch;
        }
    }

    /// <summary>Stage B. <paramref name="expectedFingerprint"/> is the reviewed registration digest from the route's profile digest.</summary>
    internal ChocolateyStageValues Observe(ReviewedUpdateOperation reviewed, string expectedFingerprint, TimeSpan remaining,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        ArgumentNullException.ThrowIfNull(expectedFingerprint);
        if (cancellationToken.IsCancellationRequested) return new ChocolateyStageValues.Cancelled();
        if (remaining <= TimeSpan.Zero) return new ChocolateyStageValues.TimedOut();
        if (!WingetMachineReviewedRoute.TryGetShape(reviewed, out WinGetMachineEvidence? parsed, out _, out _) || parsed is null ||
            !FixedEquals(parsed.RegistrationDigest, expectedFingerprint))
            return Unsupported();
        operation = reviewed;
        evidence = parsed;
        return ObserveMachine(cancellationToken, out string? client, out string? runtime, out WinGetMachineRegistrationView view, out string? key) is { } refused
            ? refused
            : Bind(client!, runtime, view, key!);
    }

    /// <summary>
    /// Stage C: the source export (H5), then H7b: a <c>download</c> of the reviewed installer into its own fresh protected folder, whose
    /// exported manifest and installer must match the review exactly (§5.1). Matched carries the frozen candidate.
    /// </summary>
    internal async Task<ChocolateyCandidateValues> QueryCandidateAsync(TimeSpan remaining, CancellationToken cancellationToken)
    {
        (ReviewedUpdateOperation reviewed, WinGetMachineEvidence bound, string client) = RequireBinding();
        if (cancellationToken.IsCancellationRequested) return new ChocolateyCandidateValues.Undetermined(true, false, LaunchesSettled, false);
        if (remaining <= TimeSpan.Zero) return new ChocolateyCandidateValues.Undetermined(false, true, LaunchesSettled, false);
        long started = Stopwatch.GetTimestamp();
        (NativeProcessObservation source, _) = await LaunchAsync(client, static _ => WingetCliSourceGuard.Arguments, SourceQueryBudget,
            remaining, null, cancellationToken).ConfigureAwait(false);
        if (Unfinished(source) is { } sourceUnfinished) return sourceUnfinished;
        if (!Completed(source) || !string.IsNullOrWhiteSpace(source.StandardError) || !WingetCliSourceGuard.MatchesExport(source.StandardOutput))
            return PreflightRefused(bound, source.OutputLimitExceeded);

        // H7b: the same reviewed installer, downloaded again by the elevated helper and read before its folder is deleted.
        TimeSpan left = remaining - Stopwatch.GetElapsedTime(started);
        if (cancellationToken.IsCancellationRequested) return new ChocolateyCandidateValues.Undetermined(true, false, LaunchesSettled, false);
        if (left <= TimeSpan.Zero) return new ChocolateyCandidateValues.Undetermined(false, true, LaunchesSettled, false);
        (ReviewedUpdateSelection selection, string architecture, string installerType) = WingetMachineReviewedArguments.Admit(reviewed);
        WingetManifestExpectation expectation = new(selection.PackageId, selection.CandidateVersion, architecture, installerType);
        (NativeProcessObservation download, (WingetManifestRefusal Refusal, WingetManifestFacts? Facts)? read) = await LaunchAsync(client,
            folder => WingetMachineReviewedArguments.BuildDownload(reviewed, folder.Downloads), DownloadBudget, left,
            folder => WingetDownloadFolder.ReadAsync(folder.Downloads, expectation, cancellationToken), cancellationToken).ConfigureAwait(false);
        if (Unfinished(download) is { } downloadUnfinished) return downloadUnfinished;
        if (cancellationToken.IsCancellationRequested) return new ChocolateyCandidateValues.Undetermined(true, false, LaunchesSettled, false);
        bool matches = Completed(download) && read is { Refusal: WingetManifestRefusal.None, Facts: { } facts } &&
            FixedEquals(facts.ManifestDigest, bound.ManifestDigest) && FixedEquals(facts.InstallerSha256, bound.InstallerSha256);
        return matches
            ? ChocolateyCandidateValues.Matched.ForWinGetMachine(reviewed.Selection.CandidateVersion)
            : PreflightRefused(bound, download.OutputLimitExceeded);
    }

    /// <summary>Stage D: stage B again; the client and the registration must be the ones stage B bound.</summary>
    internal ChocolateyStageValues Revalidate(TimeSpan remaining, CancellationToken cancellationToken)
    {
        (ReviewedUpdateOperation reviewed, WinGetMachineEvidence bound, string client) = RequireBinding();
        if (cancellationToken.IsCancellationRequested) return new ChocolateyStageValues.Cancelled();
        if (remaining <= TimeSpan.Zero) return new ChocolateyStageValues.TimedOut();
        if (ObserveMachine(cancellationToken, out string? fresh, out _, out WinGetMachineRegistrationView view, out string? key) is { } refused)
            return refused;
        if (!string.Equals(fresh, client, StringComparison.OrdinalIgnoreCase) || view != registrationView ||
            !string.Equals(key, registrationKey, StringComparison.Ordinal))
            return Unsupported();
        return ChocolateyStageValues.Observed.ForWinGetMachine(reviewed.Selection.InstalledVersion, bound.RegistrationDigest);
    }

    /// <summary>The single fixed upgrade (§4) with its own fresh protected <c>TEMP</c>.</summary>
    internal Task<NativeProcessObservation> UpgradeAsync(TimeSpan remaining, CancellationToken cancellationToken)
    {
        (ReviewedUpdateOperation reviewed, _, string client) = RequireBinding();
        return UpgradeCoreAsync(client, WingetMachineReviewedArguments.BuildUpgrade(reviewed), remaining, cancellationToken);
    }

    private async Task<NativeProcessObservation> UpgradeCoreAsync(string client, IReadOnlyList<string> arguments, TimeSpan remaining,
        CancellationToken cancellationToken) =>
        (await LaunchAsync(client, _ => arguments, UpgradeBudget, remaining, null, cancellationToken).ConfigureAwait(false)).Observation;

    /// <summary>Nothing to release: each launch's folder is deleted once that launch settled, and kept (administrator-only) otherwise.</summary>
    public void Dispose()
    {
    }

    /// <summary>
    /// One launch with a fresh protected folder as <c>TEMP</c>, deleted once the launch settled. A settled launch's folder is first given
    /// to <paramref name="inspect"/> (H7b reads the download there). A launch that is not known settled keeps its folder
    /// (administrator-only, unfindable), is not inspected, and marks the work unsettled; a launch that throws is recorded unsettled first.
    /// </summary>
    private async Task<(NativeProcessObservation Observation, (WingetManifestRefusal Refusal, WingetManifestFacts? Facts)? Inspected)> LaunchAsync(
        string client, Func<HelperProtectedRunFolder, IReadOnlyList<string>> arguments, TimeSpan budget, TimeSpan remaining,
        Func<HelperProtectedRunFolder, Task<(WingetManifestRefusal Refusal, WingetManifestFacts? Facts)>>? inspect,
        CancellationToken cancellationToken)
    {
        HelperProtectedRunFolder? folder = HelperProtectedRunFolder.TryCreate();
        if (folder is null) return (NotStarted(), null);
        NativeLaunchSpec spec;
        try
        {
            spec = NativeLaunchSpec.ForWingetMachine(client, arguments(folder), HelperWingetEnvironment.Create(folder.Path, runtimeFolder), folder.Path);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or PlatformNotSupportedException)
        {
            // Nothing was created: the client changed, or the folder or environment failed its checks.
            folder.Dispose();
            return (NotStarted(), null);
        }
        NativeProcessObservation observation;
        try
        {
            observation = await LaunchJob.RunWingetMachineAsync(spec, budget, remaining, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            lock (gate) unsettledLaunch = true;
            throw;
        }
        bool settled = observation.AggregateSettled && observation.Invocation != NativeInvocation.Unknown && !observation.TerminationFailed;
        if (!settled)
        {
            lock (gate) unsettledLaunch = true;
            return (observation, null);
        }
        try
        {
            return (observation, inspect is null || !Completed(observation) ? null : await inspect(folder).ConfigureAwait(false));
        }
        finally
        {
            folder.Dispose();
        }
    }

    /// <summary>The stage-C value for a launch that was cancelled, timed out or did not settle; null for a launch that finished.</summary>
    private ChocolateyCandidateValues.Undetermined? Unfinished(NativeProcessObservation observation)
    {
        if (observation.Cancelled || observation.TimedOut)
            return new ChocolateyCandidateValues.Undetermined(observation.Cancelled, observation.TimedOut,
                observation.AggregateSettled && LaunchesSettled, observation.OutputLimitExceeded);
        if (!observation.AggregateSettled || observation.Invocation == NativeInvocation.Unknown || observation.TerminationFailed)
            return new ChocolateyCandidateValues.Undetermined(false, false, false, observation.OutputLimitExceeded);
        return null;
    }

    /// <summary>A launch that started, exited 0 on its own and whose output was read completely and within its limit.</summary>
    private static bool Completed(NativeProcessObservation observation) =>
        observation.Invocation == NativeInvocation.Started && observation.DirectExitObserved && observation.ExitCode == 0 &&
        observation.OwnedIoCompleted && observation.PipeEofObserved && !observation.OutputLimitExceeded;

    private static ChocolateyCandidateValues.Refused PreflightRefused(WinGetMachineEvidence bound, bool outputLimitExceeded) =>
        new(ChocolateyCandidateRefusal.PreflightQueryFailed, null, null, bound.RegistrationDigest, outputLimitExceeded);

    /// <summary>H4, H7 and H6 in that order; null when all pass, else the stage value to return.</summary>
    private ChocolateyStageValues? ObserveMachine(CancellationToken cancellationToken, out string? client, out string? runtime,
        out WinGetMachineRegistrationView view, out string? key)
    {
        client = runtime = key = null;
        view = default;
        string? resolved = HelperTrustedWinget.TryResolve();
        if (resolved is null) return Unsupported();
        if (cancellationToken.IsCancellationRequested) return new ChocolateyStageValues.Cancelled();
        if (!HelperWingetSettings.Allows(operation!.RequestingUserSid)) return Unsupported();
        if (cancellationToken.IsCancellationRequested) return new ChocolateyStageValues.Cancelled();
        if (!HelperMachineRegistration.TryFind(evidence!.RegistrationDigest, out view, out string found, out _))
            return new ChocolateyStageValues.Refused(ChocolateyStageRefusal.InstalledMismatch, null, null);
        client = resolved;
        runtime = HelperTrustedWinget.TryResolveRuntimeFolder(resolved);
        key = found;
        return null;
    }

    private ChocolateyStageValues.Observed Bind(string client, string? runtime, WinGetMachineRegistrationView view, string key)
    {
        winget = client;
        runtimeFolder = runtime;
        registrationView = view;
        registrationKey = key;
        return ChocolateyStageValues.Observed.ForWinGetMachine(operation!.Selection.InstalledVersion, evidence!.RegistrationDigest);
    }

    private (ReviewedUpdateOperation Operation, WinGetMachineEvidence Evidence, string Client) RequireBinding() =>
        operation is not null && evidence is not null && winget is not null && registrationKey is not null
            ? (operation, evidence, winget)
            : throw new InvalidOperationException("No stage-B binding is held.");

    /// <summary>A definitive, settled launch failure before any process existed.</summary>
    private static NativeProcessObservation NotStarted() =>
        new(NativeInvocation.NotStarted, ExitCode: null, DirectExitObserved: false, OwnedIoCompleted: false, PipeEofObserved: false,
            TerminationFailed: false, AggregateSettled: true, Cancelled: false, TimedOut: false, OutputLimitExceeded: false,
            LingeringTerminated: false, string.Empty, string.Empty);

    private static ChocolateyStageValues.Refused Unsupported() => new(ChocolateyStageRefusal.MachineInputsUnsupported, null, null);

    private static bool FixedEquals(string value, string expected) =>
        value.Length == expected.Length && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(value), Encoding.ASCII.GetBytes(expected));
}
