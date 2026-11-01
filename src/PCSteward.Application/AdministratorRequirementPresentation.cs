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
        "This is a machine-wide update. After you confirm it, Windows asks for administrator permission (UAC) once for this update job. PC Steward itself keeps running without administrator rights.");

    private static AdministratorRequirementPresentation NotExpected() => new(
        AdministratorRequirementStatus.NotExpected,
        "Administrator approval not expected",
        "This is a current-user update, so Windows will not ask for administrator permission.");

    private static AdministratorRequirementPresentation Unknown() => new(
        AdministratorRequirementStatus.Unknown,
        "Administrator requirement unknown",
        "PC Steward will confirm the installation scope during the update review. Checking for updates and opening this page never ask for administrator permission.");
}
