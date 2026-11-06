using PCSteward.Domain;

namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, TM-03/04/13; ADR-031 design mapping §3.5: the fixed argument lists of the client self-update
/// route, built only from releases of one reviewed list. The version in the upgrade argv is the list entry's own version;
/// the frozen operation must merely name the same two versions (data equality, never a source of argv text). Fixed data
/// only: no process, binding or capability is created here.
/// </summary>
internal static class ChocolateyClientUpdateArguments
{
    /// <summary>
    /// <c>upgrade chocolatey --version &lt;target&gt;</c> with the same fixed options as the package route, when the operation is the
    /// exact client self-update shape, <paramref name="installed"/> is the release the observer matched,
    /// <paramref name="target"/> is that list's offer for it (the newest strictly newer listed release), and the frozen
    /// installed/candidate versions equal theirs. Anything else throws <see cref="ArgumentException"/>.
    /// </summary>
    internal static IReadOnlyList<string> BuildUpgrade(ReviewedUpdateOperation operation, ChocolateyReviewedRelease installed,
        ChocolateyReviewedRelease target)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(target);
        RequireShape(operation, installed);
        if (!ReferenceEquals(installed.List.OfferTarget(installed), target) ||
            !string.Equals(operation.Selection.CandidateVersion, target.Version, StringComparison.Ordinal))
            throw new ArgumentException("The target is not the reviewed list's offer for the installed release.", nameof(target));
        return Array.AsReadOnly(new[] { "upgrade", ReviewedUpdateOperation.ChocolateyClientPackageId, "--version", target.Version, "--source",
            ChocolateyUpdateSourceConnector.CommunitySourceUrl, "--yes", "--limit-output", "--no-color", "--execution-timeout", "300",
            "--ignore-remembered-arguments", "--fail-on-not-installed", "--require-checksums" });
    }

    /// <summary>The stage-C installed recheck <c>list chocolatey --exact --limit-output --no-color</c> for the same shape.</summary>
    internal static IReadOnlyList<string> BuildList(ReviewedUpdateOperation operation, ChocolateyReviewedRelease installed)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(installed);
        RequireShape(operation, installed);
        return Array.AsReadOnly(new[] { "list", ReviewedUpdateOperation.ChocolateyClientPackageId, "--exact", "--limit-output", "--no-color" });
    }

    /// <summary>
    /// ADR-031 design §3.5/§7.1: true exactly when <see cref="BuildUpgrade"/> would accept the operation with these releases
    /// (the desktop producer and verifier check the shape without building any argument list).
    /// </summary>
    internal static bool IsUpgradeShape(ReviewedUpdateOperation operation, ChocolateyReviewedRelease installed, ChocolateyReviewedRelease target)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(target);
        try { RequireShape(operation, installed); }
        catch (ArgumentException) { return false; }
        return ReferenceEquals(installed.List.OfferTarget(installed), target) &&
            string.Equals(operation.Selection.CandidateVersion, target.Version, StringComparison.Ordinal);
    }

    private static void RequireShape(ReviewedUpdateOperation operation, ChocolateyReviewedRelease installed)
    {
        ReviewedUpdateSelection selection = operation.Selection;
        if (!operation.HasCurrentSchema || operation.RouteContract != UpdateRouteContract.ChocolateyClientSelfUpdateV1 ||
            operation.ProviderGuardContract != ReviewedProviderGuardContract.ChocolateyClientSelfUpdate ||
            selection.Provider != UpdateProvider.Chocolatey || selection.Source != UpdateSource.ChocolateyCommunity || selection.Scope != UpdateScope.Machine ||
            selection.Channel != UpdateChannel.Stable || selection.Architecture.Binding != ReviewedArchitectureBinding.ProviderScriptSelected ||
            operation.Mode != ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled) ||
            !string.Equals(selection.PackageId, ReviewedUpdateOperation.ChocolateyClientPackageId, StringComparison.Ordinal) ||
            !installed.List.Contains(installed) ||
            !string.Equals(selection.InstalledVersion, installed.Version, StringComparison.Ordinal))
            throw new ArgumentException("The reviewed operation is not the exact client self-update shape for the installed release.", nameof(operation));
    }
}
