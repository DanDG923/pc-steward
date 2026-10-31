using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>
/// FR-UPD-15; TM-07/13: fixed review/history disclosure for an owner-accepted provider guard contract.
/// It describes contract limitations only; it never asserts that a parsed or restored row is trusted,
/// natively eligible, helper-qualified or approved, and it never enables confirmation or dispatch.
/// </summary>
public static class ReviewedUpdateProviderDisclosure
{
    /// <summary>Accepted Chocolatey Community script-runtime limitations (ADR-025 route-specific exception).</summary>
    public const string ChocolateyCommunityScriptRuntimeLimits =
        "Chocolatey downloads this package and any new dependencies only from the fixed official Chocolatey Community feed " +
        "(https://community.chocolatey.org/api/v2/). Dependencies already installed as local packages may be reused; their " +
        "original source is unknown. Package scripts run with administrator rights and may download vendor installers or " +
        "contact other sites; there is no network or script sandbox. If a package script refreshes its environment, " +
        "settings from your Windows account can disable checksum verification or override installer arguments, and a " +
        "downloaded file in your temporary folder could be replaced between its checksum check and its execution. " +
        "PC Steward cannot prevent these package-script behaviours; it only reports what it can verify afterwards.";

    /// <summary>
    /// Returns the fixed disclosure for the operation's frozen guard contract, or <see langword="null"/> when no
    /// accepted provider exception applies. Older provisional and legacy identities never inherit the exception.
    /// </summary>
    public static string? GetRiskText(ReviewedUpdateOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return operation.HasCurrentSchema &&
            operation.RouteContract == UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3 &&
            operation.ProviderGuardContract == ReviewedProviderGuardContract.CommunityScriptRuntimeLimits &&
            operation.Selection.Provider == UpdateProvider.Chocolatey &&
            operation.Selection.Source == UpdateSource.ChocolateyCommunity &&
            operation.Selection.Scope == UpdateScope.Machine
                ? ChocolateyCommunityScriptRuntimeLimits
                : null;
    }
}
