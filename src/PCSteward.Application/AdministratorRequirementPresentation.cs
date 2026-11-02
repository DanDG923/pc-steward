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

    /// <summary>
    /// SPEC §9.13 G4 (Task 73 Phase 6): the administrator requirement for a frozen batch. One app gives <see cref="ForSelection"/>.
    /// The one-shot helper accepts one approved job per prompt, so each machine-wide app is elevated on its own when its update starts.
    /// </summary>
    public static AdministratorRequirementPresentation ForBatch(IReadOnlyList<UpdateJobSelection> selections)
    {
        ArgumentNullException.ThrowIfNull(selections);
        if (selections.Count == 0) throw new ArgumentException("A batch needs at least one app.", nameof(selections));
        if (selections.Count == 1) return ForSelection(selections[0]);
        AdministratorRequirementStatus[] statuses = [.. selections.Select(selection => ForSelection(selection).Status)];
        int required = statuses.Count(status => status == AdministratorRequirementStatus.Required);
        int unknown = statuses.Count(status => status == AdministratorRequirementStatus.Unknown);
        if (required > 0)
            return new(AdministratorRequirementStatus.Required, $"Administrator approval required for {required} of {selections.Count} apps",
                "These include machine-wide updates. After you confirm, Windows may ask for administrator permission (UAC) once for each app that needs it, when that app's update starts. " +
                "Current-user updates don't ask. PC Steward itself keeps running without administrator rights.");
        if (unknown > 0)
            return new(AdministratorRequirementStatus.Unknown, $"Administrator requirement unknown for {unknown} of {selections.Count} apps",
                "PC Steward couldn't tell from the reviewed scope whether every app needs administrator permission. It never asks during review.");
        return new(AdministratorRequirementStatus.NotExpected, "Administrator approval not expected",
            "These are current-user updates. PC Steward will not request administrator permission for them. An app's own installer can still ask Windows for permission.");
    }

    private static AdministratorRequirementPresentation Required() => new(
        AdministratorRequirementStatus.Required,
        "Administrator approval required",
        "This is a machine-wide update. After you confirm it, Windows asks for administrator permission (UAC) once for this update job. PC Steward itself keeps running without administrator rights.");

    private static AdministratorRequirementPresentation NotExpected() => new(
        AdministratorRequirementStatus.NotExpected,
        "Administrator approval not expected",
        "This is a current-user update. PC Steward will not request administrator permission for this current-user update. The app's own installer can still ask Windows for permission.");

    private static AdministratorRequirementPresentation Unknown() => new(
        AdministratorRequirementStatus.Unknown,
        "Administrator requirement unknown",
        "PC Steward will confirm the installation scope during the update review. Checking for updates and opening this page never ask for administrator permission.");
}
