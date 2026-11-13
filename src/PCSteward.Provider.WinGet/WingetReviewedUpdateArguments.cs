using System.Collections.ObjectModel;
using PCSteward.Domain;

namespace PCSteward.Provider.WinGet;

/// <summary>
/// Formats the fixed reviewed User mappings only. Canonical data is not approval, native evidence or a dispatch permit.
/// The consuming runner must independently establish all three before invoking a trusted, bound client.
/// <list type="bullet">
/// <item><see cref="UpdateRouteContract.WinGetUserSilentV1"/> (stage C4, Task 54 R1; owner decision 2026-11-12 "Silent for all
/// per-user apps"): the desktop's per-user route. Any installer type, the <c>winget</c> or <c>msstore</c> source, any channel label
/// (no channel argument is ever added) and an observed architecture, ending <c>--disable-interactivity --silent</c>. D7 (per-user
/// vendor installers): when the frozen evidence is <c>wgu1</c>, <c>--installer-type &lt;reviewed type&gt;</c> follows.</item>
/// <item><see cref="UpdateRouteContract.WinGetUserExactV3"/> (Task 50 mapping revision 2, F6): the narrower MSIX mapping, unchanged and
/// still without a production caller.</item>
/// </list>
/// </summary>
internal static class WingetReviewedUpdateArguments
{
    internal static IReadOnlyList<string> Build(ReviewedUpdateOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return operation.RouteContract == UpdateRouteContract.WinGetUserSilentV1 ? BuildUserSilent(operation) : BuildUserMsix(operation);
    }

    private static ReadOnlyCollection<string> BuildUserSilent(ReviewedUpdateOperation operation)
    {
        ReviewedUpdateSelection selection = operation.Selection;
        if (!operation.HasCurrentSchema || operation.RouteContract != UpdateRouteContract.WinGetUserSilentV1 ||
            selection.Provider != UpdateProvider.WinGet || selection.Scope != UpdateScope.User ||
            operation.Mode != ReviewedInstallerMode.Selected(InstallerMode.Silent) ||
            selection.Architecture.Binding != ReviewedArchitectureBinding.Observed)
            throw new ArgumentException("The reviewed binding is unavailable for the per-user silent WinGet mapping.", nameof(operation));

        string source = selection.Source switch
        {
            UpdateSource.WinGet => "winget",
            UpdateSource.MicrosoftStore => "msstore",
            _ => throw new ArgumentException("The per-user silent mapping requires the winget or msstore source.", nameof(operation))
        };
        string architecture = selection.Architecture.Value switch
        {
            UpdateArchitecture.X86 => "x86",
            UpdateArchitecture.X64 => "x64",
            UpdateArchitecture.Arm64 => "arm64",
            UpdateArchitecture.Neutral => "neutral",
            _ => throw new ArgumentException("The per-user silent mapping requires an observed architecture.", nameof(operation))
        };
        string[] arguments =
        [
            "upgrade", "--id", selection.PackageId, "--version", selection.CandidateVersion, "--source", source, "--exact",
            "--scope", "user", "--architecture", architecture, "--disable-interactivity", "--silent"
        ];
        // D7 design §3 item 5: a vendor installer bound at review (wgu1) pins WinGet to the reviewed installer type.
        if (WinGetUserEvidence.TryParse(operation.EvidenceId, out WinGetUserEvidence? evidence) && evidence is not null)
        {
            if (selection.Source != UpdateSource.WinGet)
                throw new ArgumentException("Per-user vendor-installer evidence is limited to the winget source.", nameof(operation));
            arguments = [.. arguments, "--installer-type", WinGetUserEvidence.InstallerTypeText(evidence.InstallerType)];
        }
        return Array.AsReadOnly(arguments);
    }

    private static ReadOnlyCollection<string> BuildUserMsix(ReviewedUpdateOperation operation)
    {
        ReviewedUpdateSelection selection = operation.Selection;
        if (!operation.HasCurrentSchema || operation.RouteContract != UpdateRouteContract.WinGetUserExactV3 ||
            selection.Provider != UpdateProvider.WinGet || selection.Source != UpdateSource.WinGet || selection.Scope != UpdateScope.User ||
            operation.Mode != ReviewedInstallerMode.Selected(InstallerMode.Silent) ||
            selection.Channel is not (UpdateChannel.Stable or UpdateChannel.Preview) ||
            selection.Architecture.Binding != ReviewedArchitectureBinding.Observed)
            throw new ArgumentException("The reviewed binding is unavailable for the fixed User WinGet mapping.", nameof(operation));

        string architecture = selection.Architecture.Value switch
        {
            UpdateArchitecture.X86 => "x86",
            UpdateArchitecture.X64 => "x64",
            UpdateArchitecture.Arm64 => "arm64",
            _ => throw new ArgumentException("The fixed User mapping requires an observed supported architecture.", nameof(operation))
        };
        return Array.AsReadOnly(new[]
        {
            "upgrade", "--id", selection.PackageId, "--version", selection.CandidateVersion, "--source", "winget", "--exact",
            "--scope", "user", "--architecture", architecture, "--disable-interactivity", "--silent", "--installer-type", "msix"
        });
    }
}
