using PCSteward.Domain;

namespace PCSteward.Application;

public enum AdministratorRequirementStatus
{
    Required,
    NotExpected,
    Unknown
}

/// <summary>Presentation-only explanation derived from the scope already used by update execution.</summary>
public sealed record AdministratorRequirementPresentation(
    AdministratorRequirementStatus Status,
    string Label,
    string Explanation);

public static class AdministratorRequirementPresenter
{
    public static AdministratorRequirementPresentation BeforeReview() => Unknown();

    public static AdministratorRequirementPresentation ForSelection(UpdateJobSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return (selection.Provider, selection.Scope) switch
        {
            (UpdateProvider.WinGet, UpdateScope.User) => NotExpected(),
            (UpdateProvider.WinGet or UpdateProvider.Chocolatey, UpdateScope.Machine) => Required(),
            _ => Unknown()
        };
    }

    private static AdministratorRequirementPresentation Required() => new(
        AdministratorRequirementStatus.Required,
        "Administrator approval required",
        "Windows will show an administrator approval (UAC) prompt only after you confirm the reviewed update.");

    private static AdministratorRequirementPresentation NotExpected() => new(
        AdministratorRequirementStatus.NotExpected,
        "Administrator approval not expected",
        "PC Steward will use the confirmed user-scope update path and will not request administrator approval.");

    private static AdministratorRequirementPresentation Unknown() => new(
        AdministratorRequirementStatus.Unknown,
        "Administrator requirement unknown",
        "PC Steward will confirm the installation scope during fresh update review. No update or administrator prompt starts from this page.");
}
