using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>
/// Phase D stage D4b (design note <c>docs/review/PHASE_D4B_MACHINE_WINGET_UPDATE_DESIGN.md</c> §4 and §5.1; FR-UPD-15, FR-UPD-01/03,
/// TM-03/04/07/13): the reviewed route of a WinGet update of an app installed for every user on this PC, run by the elevated helper on
/// <see cref="UpdateRouteContract.WinGetMachineSilentV1"/>. <see cref="TryGetShape"/> is the one shape check every layer uses (the fixed
/// command lines, the helper's A1 and the desktop's reply reader); <see cref="Admits"/> adds the review gate, which admits the route
/// from stage 3. Nothing here is native qualification, consent or a dispatch permit.
/// </summary>
public static class WingetMachineReviewedRoute
{
    /// <summary>The fixed installation context of a machine WinGet operation (data only).</summary>
    public const string InstallationContextId = "machine:winget";

    /// <summary>
    /// True when <paramref name="operation"/> has the exact route shape: the current schema, <see cref="UpdateRouteContract.WinGetMachineSilentV1"/>,
    /// WinGet from the <c>winget</c> source, machine scope, a channel other than Preview, the Silent mode, an observed x86, x64 or
    /// arm64 architecture, and parsed <c>wgm1</c> evidence. The architecture and installer type come back as WinGet's argument text.
    /// </summary>
    public static bool TryGetShape(ReviewedUpdateOperation? operation, out WinGetMachineEvidence? evidence, out string architecture,
        out string installerType)
    {
        evidence = null;
        architecture = installerType = string.Empty;
        if (operation is null) return false;
        ReviewedUpdateSelection selection = operation.Selection;
        if (!operation.HasCurrentSchema || operation.RouteContract != UpdateRouteContract.WinGetMachineSilentV1 ||
            selection.Provider != UpdateProvider.WinGet || selection.Source != UpdateSource.WinGet || selection.Scope != UpdateScope.Machine ||
            selection.Channel == UpdateChannel.Preview || operation.Mode != ReviewedInstallerMode.Selected(InstallerMode.Silent) ||
            selection.Architecture.Binding != ReviewedArchitectureBinding.Observed ||
            !ReviewedUpdateOperation.TryGetWinGetMachineEvidence(operation.EvidenceId, out WinGetMachineEvidence? parsed) || parsed is null)
            return false;
        string? text = selection.Architecture.Value switch
        {
            UpdateArchitecture.X86 => "x86",
            UpdateArchitecture.X64 => "x64",
            UpdateArchitecture.Arm64 => "arm64",
            _ => null,
        };
        if (text is null) return false;
        evidence = parsed;
        architecture = text;
        installerType = WinGetMachineEvidence.InstallerTypeText(parsed.InstallerType);
        return true;
    }

    /// <summary>The route shape and the review gate (<see cref="ReviewedUpdateReview.HasSupportedMode"/>), which admits it from stage 3.</summary>
    public static bool Admits(ReviewedUpdateOperation? operation) =>
        TryGetShape(operation, out _, out _, out _) && ReviewedUpdateReview.HasSupportedMode(operation!);

    /// <summary>Shown when a whole-PC selection is outside the route (source, channel or architecture). Nothing was approved or started.</summary>
    public const string NotOnThisRouteMessage =
        "PC Steward has no reviewed way to update this app for every user on this PC, so it can't be updated here. Nothing was started.";

    /// <summary>Shown when the app's one whole-PC registration could not be found in fresh inventory. Nothing was approved or started.</summary>
    public const string RegistrationNotFoundMessage =
        "PC Steward couldn't find exactly one whole-PC registration for this app, so it can't be updated here. Nothing was started.";

    /// <summary>Shown when the whole-PC registration changed between the review and Confirm. Nothing was approved or started.</summary>
    public const string RegistrationChangedMessage =
        "This app's whole-PC registration changed after you reviewed the update. Review it again; no update was started.";

    /// <summary>
    /// D4b stage 2b: the installer mode a review of a whole-PC WinGet <paramref name="selection"/> binds: <c>Selected(Silent)</c> when
    /// the selection has the route's shape (WinGet, the <c>winget</c> source, machine scope, not Preview, an x86, x64 or arm64
    /// architecture); null otherwise. Binding a mode admits nothing: the review gate still decides.
    /// </summary>
    public static ReviewedInstallerMode? BindMode(UpdateJobSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return TryGetSelectionArchitecture(selection, out _) ? ReviewedInstallerMode.Selected(InstallerMode.Silent) : null;
    }

    /// <summary>WinGet's architecture text for a selection with the route's shape (see <see cref="BindMode"/>).</summary>
    public static bool TryGetSelectionArchitecture(UpdateJobSelection selection, out string architecture)
    {
        ArgumentNullException.ThrowIfNull(selection);
        architecture = string.Empty;
        if (selection.Provider != UpdateProvider.WinGet || selection.Source != UpdateSource.WinGet || selection.Scope != UpdateScope.Machine ||
            selection.Channel == UpdateChannel.Preview) return false;
        string? text = selection.Architecture switch
        {
            UpdateArchitecture.X86 => "x86",
            UpdateArchitecture.X64 => "x64",
            UpdateArchitecture.Arm64 => "arm64",
            _ => null,
        };
        if (text is null) return false;
        architecture = text;
        return true;
    }

    /// <summary>
    /// D4b design §5 H6 at review: the registration digest of the one HKLM uninstall registration of <paramref name="selection"/>'s
    /// product in <paramref name="observation"/> (fresh inventory): exactly one product with the selection's key, and exactly one of its
    /// instances read from the Windows uninstall registry for the machine with the selection's architecture (x86 is the 32-bit view,
    /// x64 the 64-bit view). The digest binds that view, the key name and the version the registry showed; the helper finds the
    /// registration again by it. Null when there is no such single registration or a field is out of bounds.
    /// </summary>
    public static string? RegistrationDigest(UpdateJobSelection selection, LiveInventoryObservation? observation)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (observation is null || !TryGetSelectionArchitecture(selection, out string architecture)) return null;
        WinGetMachineRegistrationView? view = architecture switch
        {
            "x86" => WinGetMachineRegistrationView.Machine32,
            "x64" => WinGetMachineRegistrationView.Machine64,
            _ => null,
        };
        if (view is null) return null;
        CanonicalProduct[] products = [.. observation.Products.Where(product => string.Equals(product.StableKey, selection.StableProductKey, StringComparison.Ordinal))];
        if (products.Length != 1) return null;
        InstallationInstance[] registrations = [.. observation.Instances.Where(instance => instance.ProductId == products[0].Id &&
            string.Equals(instance.Provider, "registry", StringComparison.Ordinal) &&
            string.Equals(instance.Scope, "Machine", StringComparison.Ordinal) &&
            string.Equals(instance.Architecture, architecture, StringComparison.Ordinal))];
        return registrations.Length == 1
            ? WinGetMachineRegistration.Digest(view.Value, registrations[0].ProviderIdentity, registrations[0].Version)
            : null;
    }

    /// <summary>
    /// D4b stage 2b: freezes the approved <paramref name="job"/> with the review's mode and <c>wgm1</c> evidence into the whole-PC
    /// reviewed operation, or null when the review bound no mode or evidence, the mode no longer applies, the requester is unknown,
    /// any value is outside its bounded format, or the review gate does not admit the route (until stage 3, always).
    /// </summary>
    public static ReviewedUpdateOperation? Freeze(ImmutableUpdateJob job, ReviewedInstallerMode? reviewedMode, string? requestingUserSid,
        string? evidenceId, DateTimeOffset? evidenceObservedAt)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (reviewedMode is null || BindMode(job.Selection) != reviewedMode || string.IsNullOrWhiteSpace(requestingUserSid) ||
            evidenceId is null || evidenceObservedAt is not { } observedAt ||
            !ReviewedUpdateOperation.TryGetWinGetMachineEvidence(evidenceId, out _)) return null;
        try
        {
            ReviewedUpdateOperation operation = new(job, requestingUserSid, job.Selection.StableProductKey, InstallationContextId, evidenceId,
                observedAt, UpdateRouteContract.WinGetMachineSilentV1, reviewedMode);
            return Admits(operation) && operation.BindsJob(job) ? operation : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}

/// <summary>
/// D4b stage 2b (design §7): the installer evidence a whole-PC review reads from its own <c>winget download</c> of the reviewed
/// update: the installer type WinGet chose, the exported manifest's digest and the installer's SHA-256 (64 uppercase hex each).
/// Comparison data only; the helper downloads and checks again before it runs anything.
/// </summary>
public sealed record WingetMachineInstallerEvidence(WinGetMachineInstallerType InstallerType, string ManifestDigest, string InstallerSha256);

/// <summary>The outcome of a review-time check: evidence, or the owner-facing reason the update is not offered (never both).</summary>
public sealed record WingetMachineInstallerCheck(WingetMachineInstallerEvidence? Evidence, string Message);

/// <summary>D4b stage 2b: reads a whole-PC update's installer evidence at review, without elevation. Must not throw for what it finds.</summary>
public interface IWingetMachineInstallerEvidenceSource
{
    Task<WingetMachineInstallerCheck> CheckAsync(UpdateJobSelection selection, CancellationToken cancellationToken);
}

/// <summary>
/// D4b stage 2b: the folders a whole-PC review downloads its installer into, under PC Steward's own download cache. Creating and
/// deleting them belongs to storage, whose cache deletion is canonical and handle-bound and refuses every reparse point from the
/// volume down (AGENTS.md); a provider never deletes through a path itself.
/// </summary>
public interface IReviewDownloadFolders
{
    /// <summary>A new, empty folder under the review area of the download cache; null when that can't be done safely.</summary>
    string? CreateFolder();

    /// <summary>
    /// Deletes a folder <see cref="CreateFolder"/> returned, and what is in it. False leaves what remains for Clear downloaded files;
    /// any other path is refused.
    /// </summary>
    bool DeleteFolder(string folder);
}
