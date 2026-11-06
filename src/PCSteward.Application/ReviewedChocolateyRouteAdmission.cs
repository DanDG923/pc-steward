using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, TM-03/04/07/13, AC-UPD-07; ADR-025, ADR-031 owner decision 7 (activation), Task 50 Part C step
/// C-A: the compiled review-gate admission of the two Chocolatey routes, read only as the second term of
/// <see cref="ReviewedUpdateReview.HasSupportedMode"/>. The recorded gates and their evidence are listed in
/// <c>docs/review/TASK_50_ACTIVATION_CHANGE.md</c>; the remaining ones (the Part C qualification probe on the activated
/// deployment and the owner's final admission approval) are procedure: under owner decision D4 = G-0 no code guard exists, and
/// the owner confirms no Chocolatey review until a recorded probe PASS on the activated deployment.
/// <para>
/// Shape only, from the frozen operation (it reads no file, registry, environment, network or database value): the current
/// schema, the Chocolatey Community machine registration on the stable channel, provider-script-controlled architecture and
/// mode, the exact <c>choco-profile-v2:</c> evidence form, and the route's own guard contract and candidate provenance.
/// Admission never qualifies a candidate or grants execution: fresh F′ evidence (<see cref="FreshUpdateDispatchEvidence.Matches"/>),
/// the protected deployment capability, the route dispatcher's <c>Supports</c>, the one-use permit and the elevated helper's own
/// observer, compiled release list and runtime watchdog still decide whether anything runs, and verification still decides
/// Success. The A-Q1 real-launch residuals stay open (owner decision G9): each blocks any real Chocolatey update until it is
/// exercised.
/// </para>
/// </summary>
internal static class ReviewedChocolateyRouteAdmission
{
    /// <summary>True only for the exact current-schema shape of one of the two admitted Chocolatey routes.</summary>
    internal static bool Admits(ReviewedUpdateOperation operation) =>
        operation is not null && operation.HasCurrentSchema && HasCommonShape(operation) &&
        (IsRuntimeLimitsShape(operation) || IsClientSelfUpdateShape(operation));

    /// <summary>
    /// Adapter gates §A.2/§A.6: the runtime-limit package route. The ADR-031 "client target refuses" rule stays for package jobs:
    /// the package ID <c>chocolatey</c> (in any case) is never admitted on this route.
    /// </summary>
    private static bool IsRuntimeLimitsShape(ReviewedUpdateOperation operation) =>
        operation.RouteContract == UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3 &&
        operation.ProviderGuardContract == ReviewedProviderGuardContract.CommunityScriptRuntimeLimits &&
        operation.CandidateProvenance == ReviewedCandidateProvenance.UserCacheIntent &&
        !string.Equals(operation.Selection.PackageId, ReviewedUpdateOperation.ChocolateyClientPackageId, StringComparison.OrdinalIgnoreCase);

    /// <summary>ADR-031 (Option 2): the client self-update route, package ID exactly <c>chocolatey</c> (ordinal).</summary>
    private static bool IsClientSelfUpdateShape(ReviewedUpdateOperation operation) =>
        operation.RouteContract == UpdateRouteContract.ChocolateyClientSelfUpdateV1 &&
        operation.ProviderGuardContract == ReviewedProviderGuardContract.ChocolateyClientSelfUpdate &&
        operation.CandidateProvenance == ReviewedCandidateProvenance.TrustedDesktopEvidenceRequired &&
        string.Equals(operation.Selection.PackageId, ReviewedUpdateOperation.ChocolateyClientPackageId, StringComparison.Ordinal);

    private static bool HasCommonShape(ReviewedUpdateOperation operation) =>
        operation.Selection.Provider == UpdateProvider.Chocolatey && operation.Selection.Source == UpdateSource.ChocolateyCommunity &&
        operation.Selection.Scope == UpdateScope.Machine && operation.Selection.Channel == UpdateChannel.Stable &&
        operation.Selection.Architecture.Binding == ReviewedArchitectureBinding.ProviderScriptSelected &&
        operation.Mode == ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled) &&
        ReviewedUpdateOperation.TryGetProfileEvidenceDigest(operation.EvidenceId, out _);
}
