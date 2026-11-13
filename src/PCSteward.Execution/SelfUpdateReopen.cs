namespace PCSteward.Execution;

/// <summary>
/// The machine side of the reopen watcher (ADR-032 revision 2, reopen amendment). Every member is a bounded, read-only or
/// fixed-path Windows operation that needs no elevation; the Windows implementation is <see cref="WindowsSelfUpdateHost"/>.
/// </summary>
internal interface ISelfUpdateReopenHost
{
    string InstallRoot { get; }

    string ProgramFiles { get; }

    /// <summary>The running process's own image path.</summary>
    string? ProcessPath { get; }

    /// <summary>As <see cref="ISelfUpdateFinishHost.IsProtectedWorkFolder"/>; a read of the descriptor only.</summary>
    bool IsProtectedWorkFolder(string path);

    byte[]? ReadFile(string path, int maximumBytes);

    bool BothCarrySameReleasePin(string first, string second);

    /// <summary>A process runs the installed desktop image (any user), so a second window is not opened.</summary>
    bool DesktopIsOpen();

    /// <summary>Starts the installed desktop as this process's own (unelevated) user, with no arguments and no shell.</summary>
    bool StartDesktop();

    /// <summary>Waits <paramref name="delay"/> before the next look at the work folder.</summary>
    Task PauseAsync(TimeSpan delay, CancellationToken cancellationToken);
}

/// <summary>
/// FR-UPD-15; ADR-032 revision 2, reopen amendment (2026-11-13): the reopen watcher. The desktop starts the job's protected
/// helper copy <b>unelevated</b> with <c>--reopen-after-self-update &lt;leaf&gt;</c> just before it closes for the install. The
/// watcher:
/// <list type="number">
/// <item>refuses (exit <see cref="Refused"/>) when it runs elevated, or not as exactly
/// <c>%ProgramFiles%\&lt;leaf&gt;\bin\PCSteward.ElevatedHelper.exe</c>, or when the work folder or <c>bin</c> is not protected;</item>
/// <item>looks for the finish copy's <c>result.json</c> every <see cref="PollInterval"/>, at most for <see cref="Window"/>, and
/// accepts only a canonical result naming this job;</item>
/// <item>opens nothing when Windows asked for a restart or the finish copy found an install unlike the release (the next
/// launch decides those), when PC Steward is already open, or when the installed desktop does not carry the same release pin
/// as this copy;</item>
/// <item>otherwise starts <c>%ProgramFiles%\PC Steward\PCSteward.Desktop.exe</c> as itself and exits. The reopened desktop
/// decides the job exactly as a hand-opened one does (§8): this watcher shows and records nothing.</item>
/// </list>
/// It installs, writes and changes nothing, and nothing elevated ever starts the desktop.
/// </summary>
internal static class SelfUpdateReopen
{
    internal const int Opened = 0;
    internal const int Refused = 20;
    internal const int NoResult = 40;
    internal const int LeftForNextLaunch = 41;
    internal const int AlreadyOpen = 42;
    internal const int DesktopNotPinned = 43;
    internal const int StartFailed = 44;

    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    /// <summary>As long as the desktop counts a job without a result as still installing.</summary>
    internal static readonly TimeSpan Window = SelfUpdateDesktop.InstallWindow;

    internal static async Task<int> RunAsync(string workFolderLeaf, bool elevated, ISelfUpdateReopenHost host, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (elevated || !SelfUpdateInstallPolicy.IsWorkFolderName(workFolderLeaf)) return Refused;
        string workFolder;
        string self;
        try
        {
            if (string.IsNullOrEmpty(host.ProgramFiles) || !Path.IsPathFullyQualified(host.ProgramFiles)) return Refused;
            workFolder = Path.Combine(host.ProgramFiles, workFolderLeaf);
            string bin = Path.Combine(workFolder, SelfUpdateHelperProtocol.HelperCopyFolder);
            self = Path.Combine(bin, InstalledHelperLaunchTrust.HelperExecutableName);
            if (host.ProcessPath is not { } path || !string.Equals(Path.GetFullPath(path), self, StringComparison.OrdinalIgnoreCase) ||
                !host.IsProtectedWorkFolder(workFolder) || !host.IsProtectedWorkFolder(bin))
                return Refused;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Refused;
        }

        try
        {
            SelfUpdateResultFile? result = null;
            long polls = Window.Ticks / PollInterval.Ticks;
            for (long poll = 0; poll <= polls; poll++)
            {
                result = ReadResult(workFolder, workFolderLeaf, host);
                if (result is not null) break;
                if (poll < polls) await host.PauseAsync(PollInterval, cancellationToken).ConfigureAwait(false);
            }
            if (result is null) return NoResult;
            if (!Reopens(result.Outcome)) return LeftForNextLaunch;
            if (host.DesktopIsOpen()) return AlreadyOpen;
            if (!host.BothCarrySameReleasePin(Path.Combine(host.InstallRoot, InstalledHelperLaunchTrust.DesktopExecutableName), self)) return DesktopNotPinned;
            return host.StartDesktop() ? Opened : StartFailed;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return StartFailed;
        }
    }

    /// <summary>
    /// The outcomes after which the installed desktop is the new release or the untouched old one. A needed restart and an
    /// install unlike the release are left to the next launch, which decides them (§8).
    /// </summary>
    internal static bool Reopens(SelfUpdateFinishOutcome outcome) => outcome is SelfUpdateFinishOutcome.Installed or SelfUpdateFinishOutcome.InstallFailed or
        SelfUpdateFinishOutcome.PcStewardDidNotClose or SelfUpdateFinishOutcome.RefusedBeforeInstall;

    private static SelfUpdateResultFile? ReadResult(string workFolder, string workFolderLeaf, ISelfUpdateReopenHost host)
    {
        // A result still being written reads as unreadable and is looked at again on the next poll.
        if (host.ReadFile(Path.Combine(workFolder, SelfUpdateHelperProtocol.ResultFileName), SelfUpdateHelperProtocol.MaximumFileBytes) is not { } bytes ||
            !SelfUpdateHelperProtocol.TryReadResult(bytes, out SelfUpdateResultFile? result) || result is null ||
            !string.Equals(SelfUpdateInstallPolicy.WorkFolderName(result.JobId), workFolderLeaf, StringComparison.Ordinal))
            return null;
        return result;
    }
}
