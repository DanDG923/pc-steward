using PCSteward.Domain;

namespace PCSteward.Execution;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 design rev 6 §5.2 A3 and §6 (G1, H1, L6): the helper-side capability check. It runs
/// the same pure <see cref="DeploymentCapabilityPolicy"/> through <see cref="OwnerOnlyDeploymentCapabilityReader"/> in the
/// <see cref="DeploymentCapabilityRole.Helper"/> role, over inputs the helper reads itself (ledger, manifest, capability
/// statement and binding-set hashes), with the requester check the endpoint supplies (the item's
/// <c>RequesterCannotModify</c>: ancestry on each root, then Node mode on the root and every file) as the only protection
/// principal. The layout is <see cref="OwnerOnlyDeploymentCapabilityReader.ResolveHelperLayout"/>: the helper must run as
/// <c>PCSteward.ElevatedHelper.exe</c> from <c>&lt;Program Files&gt;\PC Steward\</c>. The peer image path, read from the held
/// peer handle, must be that install root's <c>PCSteward.Desktop.exe</c>. Both images are binding-set leaves, so their
/// hashes (taken through read-share-only handles on exactly those paths) must equal their ledger entries. Any failure or
/// exception is not supported (the endpoint reports <c>Refused/RequestNotAdmitted</c>).
/// </summary>
internal sealed class HelperDeploymentCapabilityCheck : IReviewedHelperCapabilityCheck, ISelfUpdateInstallCheck
{
    private readonly Func<DeploymentCapabilityLayout?> resolveLayout;

    internal HelperDeploymentCapabilityCheck() : this(OwnerOnlyDeploymentCapabilityReader.ResolveHelperLayout)
    {
    }

    /// <summary>Fixture seam: a fixture layout instead of the fixed Program Files layout.</summary>
    internal HelperDeploymentCapabilityCheck(Func<DeploymentCapabilityLayout?> resolveLayout) =>
        this.resolveLayout = resolveLayout ?? throw new ArgumentNullException(nameof(resolveLayout));

    public HelperProtocolCapabilityResult Evaluate(UpdateRouteContract route, DeploymentLayoutProtectionCheck requesterCannotModify, string peerImagePath) =>
        EvaluateFor(route, requesterCannotModify, peerImagePath);

    /// <summary>ADR-032 revision 2 §6 step 2: the same Helper-role check for PC Steward's own update, with no route.</summary>
    public HelperProtocolCapabilityResult EvaluateInstalledRelease(DeploymentLayoutProtectionCheck requesterCannotModify, string peerImagePath) =>
        EvaluateFor(null, requesterCannotModify, peerImagePath);

    private HelperProtocolCapabilityResult EvaluateFor(UpdateRouteContract? route, DeploymentLayoutProtectionCheck requesterCannotModify, string peerImagePath)
    {
        ArgumentNullException.ThrowIfNull(requesterCannotModify);
        try
        {
            DeploymentCapabilityLayout? layout = resolveLayout();
            if (layout is null || string.IsNullOrEmpty(layout.InstallRoot) || string.IsNullOrEmpty(peerImagePath) ||
                !Path.IsPathFullyQualified(peerImagePath) ||
                !string.Equals(Path.GetFullPath(peerImagePath),
                    Path.Combine(Path.GetFullPath(layout.InstallRoot), InstalledHelperLaunchTrust.DesktopExecutableName), StringComparison.OrdinalIgnoreCase))
                return HelperProtocolCapabilityResult.For(HelperProtocolCapabilityStatus.Unreadable);
            OwnerOnlyDeploymentCapabilityReader reader = new(DeploymentCapabilityRole.Helper, () => layout, requesterCannotModify);
            return route is { } named ? reader.Evaluate(named) : reader.EvaluateInstalledRelease();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return HelperProtocolCapabilityResult.For(HelperProtocolCapabilityStatus.Unreadable);
        }
    }
}

/// <summary>
/// ADR-032 revision 2 §6 step 2: the self-update session's install check. PC Steward updates only an intact MSI release
/// install whose helper and peer desktop are its protected, pinned binding set; the requester check is the protection
/// principal, as for a package route.
/// </summary>
internal interface ISelfUpdateInstallCheck
{
    HelperProtocolCapabilityResult EvaluateInstalledRelease(DeploymentLayoutProtectionCheck requesterCannotModify, string peerImagePath);
}
