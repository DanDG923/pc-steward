using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Provider.WinGet;

/// <summary>
/// D4b stage 2b (design §7; owner answers Q1 and Q3, 2026-11-12): the review-time installer evidence of a whole-PC WinGet update,
/// read without elevation. It checks that the effective <c>winget</c> source is the trusted default, runs one
/// <c>winget download</c> of the exact reviewed package and version (<see cref="WingetMachineReviewedArguments.BuildReviewDownload(UpdateJobSelection, string)"/>)
/// into a fresh folder under PC Steward's own download cache, reads that folder with <see cref="WingetDownloadFolder"/> and has it
/// deleted. Storage creates and deletes the folder (<see cref="IReviewDownloadFolders"/>): this class never deletes a path itself.
/// Agreements stop the download itself (PC Steward never accepts them), and declared dependencies, an installer that does not say it
/// installs for every user, and installer types outside the route are refused with the reason. The result is comparison data only:
/// the elevated helper downloads and checks the same installer again before it runs anything (§5 H7b).
/// <para>D4b 32-bit-registered apps (design note §3.3, owner accepted P1 and P2 on 2026-11-13): it also proves an x64 selection's
/// architecture for an app whose uninstall entry is in the 32-bit view. P1 downloads the installed version's x64 manifest the same way
/// and finds the entry's key and DisplayVersion among what that installer declares it writes. P2 asks WinGet's structured API whether
/// either version has any machine installer for x86; it must have none, and an x64 one. Both are comparison only, and both refuse on a
/// PC that isn't x64.</para>
/// </summary>
public sealed class WingetMachineInstallerEvidenceSource : IWingetMachineInstallerEvidenceSource, IWingetArchitectureProofSource
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

    /// <summary>Shown when neither proof holds (design note §3.3). Nothing was started.</summary>
    public const string ArchitectureNotConfirmedMessage = InventoryWorkflow.ArchitectureNotConfirmedMessage;

    private static readonly TimeSpan SourceBudget = TimeSpan.FromSeconds(30);
    private readonly IWingetProcess process;
    private readonly IReviewDownloadFolders folders;
    private readonly IWinGetInstallerArchitectureQuery architectures;
    private readonly Func<bool> isX64Windows;

    public WingetMachineInstallerEvidenceSource(IReviewDownloadFolders folders) : this(new WindowsWingetProcess(), folders) { }

    internal WingetMachineInstallerEvidenceSource(IWingetProcess process, IReviewDownloadFolders folders,
        IWinGetInstallerArchitectureQuery? architectures = null, Func<bool>? isX64Windows = null)
    {
        this.process = process ?? throw new ArgumentNullException(nameof(process));
        this.folders = folders ?? throw new ArgumentNullException(nameof(folders));
        this.architectures = architectures ?? new WinGetComInstallerArchitectureQuery();
        this.isX64Windows = isX64Windows ?? (static () => System.Runtime.InteropServices.RuntimeInformation.OSArchitecture ==
            System.Runtime.InteropServices.Architecture.X64);
    }

    public async Task<WingetMachineInstallerCheck> CheckAsync(UpdateJobSelection selection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (!WingetMachineReviewedRoute.TryGetSelectionArchitecture(selection, out string architecture))
            return new(null, WingetMachineReviewedRoute.NotOnThisRouteMessage);
        (bool read, WingetManifestRefusal refusal, WingetManifestFacts? facts) =
            await DownloadAndReadAsync(selection.PackageId, selection.CandidateVersion, architecture, cancellationToken).ConfigureAwait(false);
        if (!read) return new(null, NotCheckedMessage);
        return refusal == WingetManifestRefusal.None && facts is not null
            ? new(new(facts.InstallerType, facts.ManifestDigest, facts.InstallerSha256) { DeclaredEntry = WingetArchitectureProofRules.DeclaredEntry(facts) },
                "The installer was checked for this review.")
            : new(null, Message(refusal));
    }

    public async Task<WingetArchitectureProofResult> ProveAsync(UpdateJobSelection selection, WingetArchitectureProofRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(request);
        WingetArchitectureProofResult notConfirmed = new(false, ArchitectureNotConfirmedMessage);
        if (!WingetMachineReviewedRoute.TryGetSelectionArchitecture(selection, out string architecture) || architecture != "x64" ||
            !WingetArchitectureProofRules.IsBoundedVersion(request.ProviderInstalledVersion) || !IsX64Windows())
            return notConfirmed;

        // P1: the installed version's x64 installer declares the entry this PC has, by key and DisplayVersion.
        (bool read, WingetManifestRefusal refusal, WingetManifestFacts? installed) =
            await DownloadAndReadAsync(selection.PackageId, request.ProviderInstalledVersion, architecture, cancellationToken).ConfigureAwait(false);
        if (read && refusal == WingetManifestRefusal.None && installed is not null && WingetArchitectureProofRules.ProvesProductCode(installed, request))
            return new(true, "The app's architecture was confirmed from its installed version's own installer.");

        // P2: without P1 nothing ties WinGet's installed version to the entry, so they must be equal; then neither version may offer
        // a machine installer for x86, and both must offer one for x64.
        if (!string.Equals(request.ProviderInstalledVersion, request.RegistrationVersion, StringComparison.Ordinal)) return notConfirmed;
        string[] versions = [request.ProviderInstalledVersion, selection.CandidateVersion];
        WinGetInstallerArchitectureObservation? observation;
        try
        {
            observation = await architectures.QueryAsync(selection.PackageId, versions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return notConfirmed;
        }
        return WingetArchitectureProofRules.ProvesSingleArchitecture(observation, selection.PackageId, versions)
            ? new(true, "The app's architecture was confirmed: this package has no installer for any other architecture.")
            : notConfirmed;
    }

    private bool IsX64Windows()
    {
        try { return isX64Windows(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
    }

    /// <summary>
    /// One <c>winget download</c> of <paramref name="packageId"/> at <paramref name="version"/> into a fresh folder, read with the strict
    /// reader and then deleted. <c>Read</c> is false when the source isn't the trusted default or the download didn't complete cleanly.
    /// </summary>
    private async Task<(bool Read, WingetManifestRefusal Refusal, WingetManifestFacts? Facts)> DownloadAndReadAsync(string packageId, string version,
        string architecture, CancellationToken cancellationToken)
    {
        string? folder = null;
        bool settled = true;
        try
        {
            WingetCliSourceObservation source = await new WingetCliSourceGuard(process).ObserveAsync(SourceBudget, cancellationToken).ConfigureAwait(false);
            if (!source.IsExpected) return (false, WingetManifestRefusal.Malformed, null);
            folder = folders.CreateFolder();
            if (folder is null) return (false, WingetManifestRefusal.Malformed, null);
            WingetProcessResult result = await process.RunObservedQueryAsync(
                WingetMachineReviewedArguments.BuildReviewDownload(packageId, version, architecture, folder), DownloadBudget, cancellationToken).ConfigureAwait(false);
            settled = !result.ExecutionUncertain && result.OwnedCompletion?.IsCompletedSuccessfully != false;
            cancellationToken.ThrowIfCancellationRequested();
            if (!settled || result.ExitCode != 0 || result.TimedOut || result.Cancelled || result.OutputLimitExceeded || result.OutputIncomplete)
                return (false, WingetManifestRefusal.Malformed, null);
            (WingetManifestRefusal refusal, WingetManifestFacts? facts) = await WingetDownloadFolder.ReadAsync(folder,
                new(packageId, version, architecture, null), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return (true, refusal, facts);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or
            ArgumentException or PlatformNotSupportedException or System.ComponentModel.Win32Exception)
        {
            return (false, WingetManifestRefusal.Malformed, null);
        }
        finally
        {
            // A download that did not settle may still be writing: its folder is left for Clear downloaded files.
            if (folder is not null && settled) _ = folders.DeleteFolder(folder);
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
}
