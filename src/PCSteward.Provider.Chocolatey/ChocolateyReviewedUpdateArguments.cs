using PCSteward.Domain;

namespace PCSteward.Provider.Chocolatey;

/// <summary>Fixed argument data only. No process or native binding is created; runtime guard capability remains separate.</summary>
internal static class ChocolateyReviewedUpdateArguments
{
    internal static IReadOnlyList<string> Build(ReviewedUpdateOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ReviewedUpdateSelection selection = operation.Selection;
        if (!operation.HasCurrentSchema || operation.RouteContract != UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3 ||
            operation.ProviderGuardContract != ReviewedProviderGuardContract.CommunityScriptRuntimeLimits ||
            selection.Provider != UpdateProvider.Chocolatey || selection.Source != UpdateSource.ChocolateyCommunity || selection.Scope != UpdateScope.Machine ||
            selection.Channel != UpdateChannel.Stable || selection.Architecture.Binding != ReviewedArchitectureBinding.ProviderScriptSelected ||
            operation.Mode != ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled) ||
            !ChocolateyNativePackageId.IsSupported(selection.PackageId) || ChocolateyClientReleasePolicy.IsClientTarget(selection.PackageId) ||
            !ChocolateyNativeVersion.TryParse(selection.InstalledVersion, out ChocolateyNativeVersion? installed) ||
            !ChocolateyNativeVersion.TryParse(selection.CandidateVersion, out ChocolateyNativeVersion? candidate) ||
            installed!.Normalized != selection.InstalledVersion || candidate!.Normalized != selection.CandidateVersion || candidate.IsPrerelease ||
            VersionComparison.Compare(new(selection.InstalledVersion, selection.CandidateVersion)).Outcome != VersionComparisonOutcome.Newer)
            throw new ArgumentException("The reviewed binding is unavailable for the fixed Chocolatey mapping.", nameof(operation));
        return Array.AsReadOnly(new[] { "upgrade", selection.PackageId, "--version", selection.CandidateVersion, "--source",
            ChocolateyUpdateSourceConnector.CommunitySourceUrl, "--yes", "--limit-output", "--no-color", "--execution-timeout", "300",
            "--ignore-remembered-arguments", "--fail-on-not-installed", "--require-checksums" });
    }
}
