using System.Security.Cryptography;
using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Provider.WinGet;

/// <summary>
/// D4b stage 2b (design §7; owner answers Q1 and Q3, 2026-11-12): the review-time installer evidence of a whole-PC WinGet update,
/// read without elevation. It checks that the effective <c>winget</c> source is the trusted default, runs one
/// <c>winget download</c> of the exact reviewed package and version (<see cref="WingetMachineReviewedArguments.BuildReviewDownload"/>)
/// into a fresh folder under PC Steward's own download cache, reads that folder with <see cref="WingetDownloadFolder"/> and deletes it.
/// Agreements stop the download itself (PC Steward never accepts them), and declared dependencies, an installer that does not say it
/// installs for every user, and installer types outside the route are refused with the reason. The result is comparison data only:
/// the elevated helper downloads and checks the same installer again before it runs anything (§5 H7b).
/// </summary>
public sealed class WingetMachineInstallerEvidenceSource : IWingetMachineInstallerEvidenceSource
{
    /// <summary>Shown when the update declares other packages it would also install (Q3). Nothing was started.</summary>
    public const string DependenciesMessage = "Not offered in this version: this update would also install other packages.";

    /// <summary>Shown when the update declares a licence agreement (Q3). Nothing was started.</summary>
    public const string AgreementsMessage = "Not offered in this version: this update asks you to accept a licence agreement.";

    /// <summary>Shown when the chosen installer does not say it installs for every user (Q1). Nothing was started.</summary>
    public const string NoMachineInstallerMessage =
        "Not offered in this version: this update has no installer that says it installs for every user on this PC.";

    /// <summary>Shown when the chosen installer's type is outside the route. Nothing was started.</summary>
    public const string InstallerTypeMessage = "Not offered in this version: this update's installer type can't be updated for every user here.";

    /// <summary>Shown when the installer could not be checked (download failed, timed out, or did not read). Nothing was started.</summary>
    public const string NotCheckedMessage =
        "PC Steward couldn't check this update's installer, so it can't be updated here. It may need a licence agreement, or the download " +
        "failed. Nothing was started.";

    /// <summary>The review download's budget.</summary>
    internal static readonly TimeSpan DownloadBudget = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan SourceBudget = TimeSpan.FromSeconds(30);
    private readonly IWingetProcess process;
    private readonly Func<string> root;

    public WingetMachineInstallerEvidenceSource() : this(new WindowsWingetProcess(), DefaultRoot) { }

    internal WingetMachineInstallerEvidenceSource(IWingetProcess process, Func<string> root)
    {
        this.process = process ?? throw new ArgumentNullException(nameof(process));
        this.root = root ?? throw new ArgumentNullException(nameof(root));
    }

    public async Task<WingetMachineInstallerCheck> CheckAsync(UpdateJobSelection selection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (!WingetMachineReviewedRoute.TryGetSelectionArchitecture(selection, out string architecture))
            return new(null, WingetMachineReviewedRoute.NotOnThisRouteMessage);
        string? folder = null;
        bool settled = true;
        try
        {
            WingetCliSourceObservation source = await new WingetCliSourceGuard(process).ObserveAsync(SourceBudget, cancellationToken).ConfigureAwait(false);
            if (!source.IsExpected) return new(null, NotCheckedMessage);
            folder = CreateFolder();
            if (folder is null) return new(null, NotCheckedMessage);
            WingetProcessResult result = await process.RunObservedQueryAsync(WingetMachineReviewedArguments.BuildReviewDownload(selection, folder),
                DownloadBudget, cancellationToken).ConfigureAwait(false);
            settled = !result.ExecutionUncertain && result.OwnedCompletion?.IsCompletedSuccessfully != false;
            cancellationToken.ThrowIfCancellationRequested();
            if (!settled || result.ExitCode != 0 || result.TimedOut || result.Cancelled || result.OutputLimitExceeded || result.OutputIncomplete)
                return new(null, NotCheckedMessage);
            (WingetManifestRefusal refusal, WingetManifestFacts? facts) = await WingetDownloadFolder.ReadAsync(folder,
                new(selection.PackageId, selection.CandidateVersion, architecture, null), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return refusal == WingetManifestRefusal.None && facts is not null
                ? new(new(facts.InstallerType, facts.ManifestDigest, facts.InstallerSha256), "The installer was checked for this review.")
                : new(null, Message(refusal));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or
            ArgumentException or PlatformNotSupportedException or System.ComponentModel.Win32Exception)
        {
            return new(null, NotCheckedMessage);
        }
        finally
        {
            // A download that did not settle may still be writing: its folder is left for Clear downloaded files.
            if (folder is not null && settled) TryDelete(folder);
        }
    }

    internal static string Message(WingetManifestRefusal refusal) => refusal switch
    {
        WingetManifestRefusal.DependenciesDeclared => DependenciesMessage,
        WingetManifestRefusal.AgreementsDeclared => AgreementsMessage,
        WingetManifestRefusal.ScopeNotMachine => NoMachineInstallerMessage,
        WingetManifestRefusal.InstallerMismatch or WingetManifestRefusal.NestedInstaller => InstallerTypeMessage,
        _ => NotCheckedMessage,
    };

    /// <summary>
    /// <c>%LOCALAPPDATA%\PCSteward\downloads\WinGetReview</c>, inside the download cache root, so Clear downloaded files removes
    /// anything a review left behind.
    /// </summary>
    private static string DefaultRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCSteward", "downloads", "WinGetReview");

    /// <summary>A new, empty folder with a random 128-bit name under the review root; null when the root is not a plain folder.</summary>
    private string? CreateFolder()
    {
        string parent = root();
        if (string.IsNullOrWhiteSpace(parent) || !Path.IsPathFullyQualified(parent)) return null;
        Directory.CreateDirectory(parent);
        if ((new DirectoryInfo(parent).Attributes & FileAttributes.ReparsePoint) != 0) return null;
        string folder = Path.Combine(parent, Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));
        if (Directory.Exists(folder) || File.Exists(folder)) return null;
        DirectoryInfo created = Directory.CreateDirectory(folder);
        return (created.Attributes & FileAttributes.ReparsePoint) == 0 && !created.EnumerateFileSystemInfos().Any() ? folder : null;
    }

    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left for Clear downloaded files; nothing reads it again.
        }
    }
}
