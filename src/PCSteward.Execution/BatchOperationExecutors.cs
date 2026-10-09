using System.Diagnostics;
using System.Text.RegularExpressions;
using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Provider.Chocolatey;
using PCSteward.Provider.WinGet;

namespace PCSteward.Execution;

/// <summary>
/// FR-BAT-05 desktop phase: runs only unelevated items (user-scope WinGet installs and plain user-scope WinGet
/// upgrades). Anything that needs elevation is refused here and belongs to the helper session.
/// </summary>
public sealed class DesktopOperationExecutor : IOperationExecutor
{
    private readonly WingetUpdateExecutor upgrades;
    private readonly WingetInstallExecutor installs;

    public DesktopOperationExecutor() : this(new WingetUpdateExecutor(), new WingetInstallExecutor()) { }

    internal DesktopOperationExecutor(WingetUpdateExecutor upgrades, WingetInstallExecutor installs)
    {
        this.upgrades = upgrades ?? throw new ArgumentNullException(nameof(upgrades));
        this.installs = installs ?? throw new ArgumentNullException(nameof(installs));
    }

    public async Task<ProviderExitAssessment> ExecuteAsync(ImmutableUpdateJob job, UpdateJobExecutionEvidence freshEvidence, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(freshEvidence);
        if (!job.HasValidDigest || !job.Matches(freshEvidence)) return Refused("current-evidence-mismatch");
        UpdateJobSelection selection = job.Selection;
        if (selection.RunsElevated) return Refused("operation-requires-elevated-session");
        if (selection.Provider != UpdateProvider.WinGet) return Refused("unsupported-provider");
        if (selection.Operation == PackageOperationKind.Install) return await installs.ExecuteAsync(job, freshEvidence, cancellationToken).ConfigureAwait(false);
        if (selection.IsPlainUpgrade) return WingetExitCodes.Classify(await upgrades.ExecuteAsync(job, cancellationToken).ConfigureAwait(false));
        return Refused("operation-not-supported");
    }

    internal static ProviderExitAssessment Refused(string reason) => new(UpdateProviderAttemptStatus.ProductFailed, false, reason);
}

/// <summary>FR-BAT-05: one protected helper session (protocol v2) per approved batch. Requires the protected installation.</summary>
public sealed class ElevatedBatchExecutor : IElevatedBatchExecutor
{
    private readonly WindowsElevatedHelperTransport transport;

    public ElevatedBatchExecutor() : this(new WindowsElevatedHelperTransport()) { }

    internal ElevatedBatchExecutor(WindowsElevatedHelperTransport transport) => this.transport = transport ?? throw new ArgumentNullException(nameof(transport));

    public Task<ElevatedBatchOutcome> ExecuteAsync(ApprovedOperationBatch batch, IReadOnlyDictionary<Guid, string> skipReasons, Action<Guid>? itemStarted, CancellationToken cancelRemaining, CancellationToken abort) =>
        transport.ExecuteBatchAsync(batch, skipReasons, itemStarted, cancelRemaining, abort);
}

/// <summary>Helper phase: the same operations, elevated, built in code from the accepted job.</summary>
internal sealed class HelperOperationExecutor : IOperationExecutor
{
    public async Task<ProviderExitAssessment> ExecuteAsync(ImmutableUpdateJob job, UpdateJobExecutionEvidence freshEvidence, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(freshEvidence);
        if (!job.HasValidDigest || !job.Matches(freshEvidence)) return DesktopOperationExecutor.Refused("current-evidence-mismatch");
        UpdateJobSelection selection = job.Selection;
        if (!selection.RunsElevated) return DesktopOperationExecutor.Refused("unelevated-operation-in-helper");
        return (selection.Operation, selection.Provider) switch
        {
            (PackageOperationKind.Install, UpdateProvider.WinGet) => await new WingetInstallExecutor().ExecuteAsync(job, freshEvidence, cancellationToken).ConfigureAwait(false),
            (PackageOperationKind.Install, UpdateProvider.Chocolatey) => await new ChocolateyInstallExecutor().ExecuteAsync(job, freshEvidence, cancellationToken).ConfigureAwait(false),
            (PackageOperationKind.Upgrade, UpdateProvider.WinGet) when selection.IsPlainUpgrade => WingetExitCodes.Classify(await new WingetMachineUpdateExecutor().ExecuteAsync(job, cancellationToken).ConfigureAwait(false)),
            (PackageOperationKind.Upgrade, UpdateProvider.Chocolatey) when selection.IsPlainUpgrade => ChocolateyExitCodes.Classify(await new ChocolateyMachineUpdateExecutor().ExecuteAsync(job, cancellationToken).ConfigureAwait(false)),
            _ => DesktopOperationExecutor.Refused("operation-not-supported")
        };
    }
}

/// <summary>
/// Helper phase revalidation (ADR-018 v2): fresh structured provider evidence immediately before each item.
/// Installs repeat the exact install preflight; upgrades repeat the structured candidate resolution and must
/// still show the approved installed and target versions. The helper reads no per-user database.
/// </summary>
internal sealed class HelperOperationRevalidator(IInstallPreflightAuthority installs, IStructuredUpdateCandidateAuthority upgrades) : IOperationRevalidator
{
    private readonly IInstallPreflightAuthority installs = installs ?? throw new ArgumentNullException(nameof(installs));
    private readonly IStructuredUpdateCandidateAuthority upgrades = upgrades ?? throw new ArgumentNullException(nameof(upgrades));

    internal static HelperOperationRevalidator CreateDefault() => new(
        new CompositeInstallPreflightAuthority(new Dictionary<UpdateProvider, IInstallPreflightAuthority>
        {
            [UpdateProvider.WinGet] = new WingetInstallPreflightAuthority(),
            [UpdateProvider.Chocolatey] = new ChocolateyInstallPreflightAuthority()
        }),
        new CompositeStructuredUpdateCandidateAuthority([new WingetStructuredUpdateCandidateAuthority(), new ChocolateyUpdateSourceConnector()]));

    public async Task<UpdateJobExecutionEvidence?> RevalidateAsync(ImmutableUpdateJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        UpdateJobSelection selection = job.Selection;
        if (selection.Operation == PackageOperationKind.Install)
        {
            InstallPreflightResult result = await installs.PreflightAsync(InstallPreflightRequest.ForApprovedJob(job), cancellationToken).ConfigureAwait(false);
            return result.AsExecutionEvidence();
        }

        string sourceId = selection.Source switch
        {
            UpdateSource.WinGet => "winget",
            UpdateSource.MicrosoftStore => "msstore",
            _ => PersonalCatalogMatching.ChocolateySourceId
        };
        StructuredUpdateCandidateEvidence? candidate = await upgrades.ResolveAsync(sourceId, selection.PackageId, cancellationToken).ConfigureAwait(false);
        if (candidate is null || !string.Equals(candidate.PackageId, selection.PackageId, StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            UpdateJobSelection fresh = UpdateJobSelection.Restore(
                selection.Operation, selection.StableProductKey, selection.Provider, selection.Source, selection.PackageId,
                candidate.InstalledVersion, candidate.CandidateVersion, selection.Scope, selection.Architecture, selection.Channel,
                selection.MappingEvidenceVersion, selection.AgreementHash, selection.RunsElevated);
            return new(fresh, true, true);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}

/// <summary>A process the closer may ask to exit. Abstracted so the policy is testable without real processes.</summary>
internal interface IClosableProcess : IDisposable
{
    bool HasExited { get; }
    bool CloseMainWindow();
    Task WaitForExitAsync(CancellationToken cancellationToken);
    void Kill();
}

/// <summary>
/// FR-BAT-08: asks the current session's matching processes to close through their main window, waits a
/// bounded time, and terminates them only when the caller passes the second per-run force consent.
/// </summary>
public sealed partial class WindowsRunningApplicationCloser : IRunningApplicationCloser
{
    private static readonly TimeSpan MaximumGracefulWait = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ForceWait = TimeSpan.FromSeconds(10);
    private readonly Func<string, IReadOnlyList<IClosableProcess>> findProcesses;

    public WindowsRunningApplicationCloser() : this(FindCurrentSessionProcesses) { }

    internal WindowsRunningApplicationCloser(Func<string, IReadOnlyList<IClosableProcess>> findProcesses) =>
        this.findProcesses = findProcesses ?? throw new ArgumentNullException(nameof(findProcesses));

    public async Task<bool> CloseAsync(IReadOnlyCollection<string> processNames, bool allowForce, TimeSpan gracefulWait, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(processNames);
        if (processNames.Count == 0 || processNames.Count > 16 || processNames.Any(name => name is null || !ProcessName().IsMatch(name))) return false;
        if (gracefulWait <= TimeSpan.Zero || gracefulWait > MaximumGracefulWait) throw new ArgumentOutOfRangeException(nameof(gracefulWait));
        List<IClosableProcess> targets = [.. processNames.Distinct(StringComparer.OrdinalIgnoreCase).SelectMany(findProcesses)];
        try
        {
            foreach (IClosableProcess process in targets.Where(process => !process.HasExited)) process.CloseMainWindow();
            if (await WaitAllAsync(targets, gracefulWait, cancellationToken).ConfigureAwait(false)) return true;
            if (!allowForce) return false;
            foreach (IClosableProcess process in targets.Where(process => !process.HasExited)) process.Kill();
            return await WaitAllAsync(targets, ForceWait, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            foreach (IClosableProcess process in targets) process.Dispose();
        }
    }

    private static async Task<bool> WaitAllAsync(IReadOnlyCollection<IClosableProcess> targets, TimeSpan wait, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(wait);
        try
        {
            await Task.WhenAll(targets.Where(process => !process.HasExited).Select(process => process.WaitForExitAsync(timeout.Token))).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        return targets.All(process => process.HasExited);
    }

    private static IReadOnlyList<IClosableProcess> FindCurrentSessionProcesses(string name)
    {
        int session = Process.GetCurrentProcess().SessionId;
        List<IClosableProcess> found = [];
        foreach (Process process in Process.GetProcessesByName(name))
        {
            bool keep;
            try
            {
                keep = process.SessionId == session && process.Id != Environment.ProcessId;
            }
            catch (InvalidOperationException)
            {
                keep = false;
            }

            if (keep) found.Add(new ProcessAdapter(process));
            else process.Dispose();
        }

        return found;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._+-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProcessName();

    private sealed class ProcessAdapter(Process process) : IClosableProcess
    {
        public bool HasExited
        {
            get
            {
                try { return process.HasExited; }
                catch (InvalidOperationException) { return true; }
            }
        }

        public bool CloseMainWindow()
        {
            try { return process.CloseMainWindow(); }
            catch (InvalidOperationException) { return false; }
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken) => process.WaitForExitAsync(cancellationToken);

        public void Kill()
        {
            try { process.Kill(entireProcessTree: false); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        public void Dispose() => process.Dispose();
    }
}
