using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>
/// Phase D stage D4b (design note <c>docs/review/PHASE_D4B_MACHINE_WINGET_UPDATE_DESIGN.md</c> §4 and §5.1; FR-UPD-15, FR-UPD-01/03,
/// TM-03/04/07/13): the reviewed route of a WinGet update of an app installed for every user on this PC, run by the elevated helper on
/// <see cref="UpdateRouteContract.WinGetMachineSilentV1"/>. <see cref="TryGetShape"/> is the one shape check every layer uses (the fixed
/// command lines, the helper's A1 and the desktop's reply reader); <see cref="Admits"/> adds the review gate, which does not admit the
/// route until stage 3. Nothing here is native qualification, consent or a dispatch permit.
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

    /// <summary>The route shape and the review gate (<see cref="ReviewedUpdateReview.HasSupportedMode"/>), which admits it only from stage 3.</summary>
    public static bool Admits(ReviewedUpdateOperation? operation) =>
        TryGetShape(operation, out _, out _, out _) && ReviewedUpdateReview.HasSupportedMode(operation!);
}
