using PCSteward.Domain;

namespace PCSteward.Provider.WinGet;

/// <summary>
/// Formats the fixed reviewed User mapping only. Canonical data is not approval, native evidence or a dispatch permit.
/// The consuming runner must independently establish all three before invoking a trusted, bound client.
/// </summary>
internal static class WingetReviewedUpdateArguments
{
    internal static IReadOnlyList<string> Build(ReviewedUpdateOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
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
