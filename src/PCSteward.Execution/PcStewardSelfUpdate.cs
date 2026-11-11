using System.Security.Cryptography;

namespace PCSteward.Execution;

/// <summary>The desktop's own first check of a downloaded MSI (ADR-032 revision 2 §5).</summary>
public enum PcStewardUpdatePrecheck
{
    Passed,
    NotPinned,
    Unreadable,
    StructureRefused,
    TablesDiffer,
}

/// <summary>How the owner's <b>Install</b> ended.</summary>
public enum PcStewardInstallOutcome
{
    /// <summary>The helper is installing after PC Steward closes: close now.</summary>
    Ready,
    /// <summary>The helper refused (see <see cref="PcStewardInstallResult.RefusalCode"/>); nothing was installed.</summary>
    Refused,
    /// <summary>The owner declined the Windows prompt.</summary>
    ElevationDeclined,
    /// <summary>A check before the prompt failed; no prompt was shown.</summary>
    NotStarted,
    /// <summary>The helper started but gave no valid answer.</summary>
    NoValidAnswer,
    /// <summary>The helper is ready but the desktop could not record the job; PC Steward must still close.</summary>
    ReadyJobNotSaved,
}

/// <param name="Outcome">How it ended.</param>
/// <param name="RefusalCode">For <see cref="PcStewardInstallOutcome.Refused"/>, the helper's fixed code (for example <c>msi-not-pinned</c>).</param>
public sealed record PcStewardInstallResult(PcStewardInstallOutcome Outcome, string? RefusalCode);

/// <summary>Where the recorded update stands (ADR-032 revision 2 §8).</summary>
public enum PcStewardUpdateState
{
    InProgress,
    Updated,
    RestartToFinish,
    Failed,
    Inconclusive,
}

/// <param name="State">The decision.</param>
/// <param name="Code">A fixed lowercase code naming why.</param>
/// <param name="FromVersion">The version the owner updated from.</param>
/// <param name="ToVersion">The version the owner approved.</param>
/// <param name="InstallLogPath">The administrator-only install log in the job's work folder.</param>
/// <param name="IsFinal">The job is decided and is forgotten once shown.</param>
public sealed record PcStewardUpdateStatus(PcStewardUpdateState State, string Code, string FromVersion, string ToVersion, string InstallLogPath, bool IsFinal);

/// <summary>The values the owner approved: the checked release and the downloaded files' SHA-256.</summary>
public sealed record PcStewardApprovedUpdate(string FromVersion, string ToVersion, long RunId, string SourceCommit, string CheckZipSha256, string MsiSha256);

/// <summary>A download in progress into the per-user folder. Disposing it without <see cref="Commit"/> removes the partial file.</summary>
public sealed class PcStewardDownload : IDisposable
{
    private readonly SelfUpdateDesktopFiles files;
    private readonly string partialPath;
    private bool committed;

    internal PcStewardDownload(SelfUpdateDesktopFiles files)
    {
        this.files = files;
        Stream = files.CreatePartial(out partialPath);
    }

    /// <summary>Where the download is written.</summary>
    public Stream Stream { get; }

    /// <summary>Closes the file and moves it to the name its SHA-256 gives. The hash is checked again from the file.</summary>
    public bool Commit(string msiSha256)
    {
        Stream.Dispose();
        if (committed || !SelfUpdateHelperProtocol.IsUpperHex64(msiSha256)) return false;
        try
        {
            using (FileStream read = new(partialPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                if (!string.Equals(Convert.ToHexString(SHA256.HashData(read)), msiSha256, StringComparison.Ordinal)) return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return false; }
        committed = files.CommitDownload(partialPath, files.MsiPath(msiSha256));
        return committed;
    }

    public void Dispose()
    {
        Stream.Dispose();
        if (!committed) files.Discard(partialPath);
    }
}

/// <summary>
/// FR-UPD-04/15, TM-03/04/12/13/26; ADR-032 revision 2 §5, §6 (desktop side) and §8: the desktop's façade for PC Steward's
/// own update. It owns the per-user folder, the first MSI check, the one elevated launch and the job record; the release
/// check and the download come from <c>PCSteward.SelfUpdate</c>. Every member fails closed and never throws for expected
/// failures. Nothing here installs: the elevated helper and its finish copy do, after their own checks.
/// </summary>
public sealed class PcStewardSelfUpdate
{
    private readonly SelfUpdateDesktopFiles files;
    private readonly SelfUpdateDesktopTransport transport;

    private PcStewardSelfUpdate(SelfUpdateDesktopFiles files, SelfUpdateDesktopTransport transport)
    {
        this.files = files;
        this.transport = transport;
    }

    /// <summary>The façade over this user's folder and the installed helper, or null off Windows.</summary>
    public static PcStewardSelfUpdate? TryCreate() =>
        SelfUpdateDesktopFiles.TryCreate() is { } files ? new(files, new SelfUpdateDesktopTransport()) : null;

    /// <summary>A new download of the MSI. Null when the folder cannot be written.</summary>
    public PcStewardDownload? BeginMsiDownload()
    {
        try { return new PcStewardDownload(files); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Stores the release's check zip under its SHA-256 name.</summary>
    public bool SaveCheckZip(byte[] bytes, string checkZipSha256) =>
        bytes is not null && SelfUpdateHelperProtocol.IsUpperHex64(checkZipSha256) && files.WriteCheckZip(bytes, checkZipSha256);

    /// <summary>§5: the downloaded MSI's pin, structure and tables digest, before any prompt.</summary>
    public PcStewardUpdatePrecheck Precheck(string msiSha256, string msiTablesSha256)
    {
        if (!SelfUpdateHelperProtocol.IsUpperHex64(msiSha256)) return PcStewardUpdatePrecheck.Unreadable;
        try { return files.Precheck(files.MsiPath(msiSha256), msiTablesSha256); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return PcStewardUpdatePrecheck.Unreadable; }
    }

    /// <summary>Removes the downloaded MSI and check zip.</summary>
    public void DiscardDownloads(string msiSha256, string checkZipSha256)
    {
        if (SelfUpdateHelperProtocol.IsUpperHex64(msiSha256)) files.Discard(files.MsiPath(msiSha256));
        if (SelfUpdateHelperProtocol.IsUpperHex64(checkZipSha256)) files.Discard(files.CheckZipPath(checkZipSha256));
    }

    /// <summary>
    /// §6: one Windows prompt for the elevated session with a new job id and nonce. On Ready the job is recorded; the caller
    /// then closes PC Steward so the finish copy can install.
    /// </summary>
    public async Task<PcStewardInstallResult> InstallAsync(PcStewardApprovedUpdate update, DateTimeOffset approvedAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (!ReleaseCheckPolicy.IsMsiVersion(update.FromVersion)) return new(PcStewardInstallOutcome.NotStarted, null);
        SelfUpdateRequest request = new(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), Guid.NewGuid(), update.ToVersion, update.RunId,
            update.SourceCommit, update.CheckZipSha256, update.MsiSha256);
        SelfUpdateLaunchResult launched;
        try { launched = await transport.InstallAsync(request, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return new(PcStewardInstallOutcome.NoValidAnswer, null); }
        switch (launched.Outcome)
        {
            case SelfUpdateLaunchOutcome.Ready when launched.Response is { WorkFolder: { } folder, ManifestSha256: { } manifest }:
                SelfUpdateDesktopJob job = new(request.JobId, update.FromVersion, update.ToVersion, update.RunId, update.SourceCommit,
                    update.CheckZipSha256, update.MsiSha256, approvedAt, folder, manifest);
                return new(files.WriteJob(job) ? PcStewardInstallOutcome.Ready : PcStewardInstallOutcome.ReadyJobNotSaved, null);
            case SelfUpdateLaunchOutcome.Refused when launched.Response?.Reason is { } reason:
                return new(PcStewardInstallOutcome.Refused, SelfUpdateHelperProtocol.Code(reason));
            case SelfUpdateLaunchOutcome.ElevationDeclined:
                return new(PcStewardInstallOutcome.ElevationDeclined, null);
            case SelfUpdateLaunchOutcome.NotStarted:
                return new(PcStewardInstallOutcome.NotStarted, null);
            default:
                return new(PcStewardInstallOutcome.NoValidAnswer, null);
        }
    }

    /// <summary>§8: the recorded job's state for this launch, or null when no job is recorded.</summary>
    /// <param name="runningVersion">This process's MSI version (three-part assembly version).</param>
    /// <param name="now">The current time.</param>
    public PcStewardUpdateStatus? ReadJob(string runningVersion, DateTimeOffset now)
    {
        try
        {
            if (files.ReadJob() is not { } job) return null;
            SelfUpdateJobVerdict verdict = files.Evaluate(job, runningVersion ?? string.Empty, now);
            return new((PcStewardUpdateState)verdict.State, verdict.Code, job.FromVersion, job.ToVersion, files.InstallLogPath(job), verdict.IsFinal);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>Forgets the recorded job and its downloads once its final state has been shown.</summary>
    public void ForgetJob()
    {
        try
        {
            if (files.ReadJob() is { } job) files.Forget(job);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
    }
}
