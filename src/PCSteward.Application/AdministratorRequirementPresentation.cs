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
    /// D7 design §3 items 2–3: as <see cref="ForSelection(UpdateJobSelection)"/>, for a per-user update whose review bound the vendor
    /// <paramref name="installer"/>. An installer whose manifest says it asks for administrator permission is shown as needing it, with
    /// the owner-chosen warning (Q1-B); any other vendor installer says what PC Steward can't control.
    /// </summary>
    public static AdministratorRequirementPresentation ForSelection(UpdateJobSelection selection, WinGetUserInstaller? installer)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (installer is not { IsVendor: true } || selection.Provider != UpdateProvider.WinGet || selection.Scope != UpdateScope.User) return ForSelection(selection);
        return installer.ExpectsElevation
            ? new(AdministratorRequirementStatus.Required, "Windows will ask for administrator permission", WingetUserInstallerText.ElevationWarning)
            : new(AdministratorRequirementStatus.NotExpected, "Administrator approval not expected", WingetUserInstallerText.NoElevationDisclosure);
    }

    /// <summary>
    /// SPEC §9.13 G4 (Task 73 Phase 6): the administrator requirement for a frozen batch. One app gives <see cref="ForSelection(UpdateJobSelection)"/>.
    /// The one-shot helper accepts one approved job per prompt, so each machine-wide app is elevated on its own when its update starts.
    /// </summary>
    public static AdministratorRequirementPresentation ForBatch(IReadOnlyList<UpdateJobSelection> selections) => ForBatch(selections, null);

    /// <summary>
    /// As <see cref="ForBatch(IReadOnlyList{UpdateJobSelection})"/>, with the vendor installer each per-user review bound (D7 design §3),
    /// in the same order; null entries, or a null list, mean none was bound.
    /// </summary>
    public static AdministratorRequirementPresentation ForBatch(IReadOnlyList<UpdateJobSelection> selections, IReadOnlyList<WinGetUserInstaller?>? installers)
    {
        ArgumentNullException.ThrowIfNull(selections);
        if (selections.Count == 0) throw new ArgumentException("A batch needs at least one app.", nameof(selections));
        if (installers is not null && installers.Count != selections.Count) throw new ArgumentException("One installer entry per app.", nameof(installers));
        WinGetUserInstaller? InstallerAt(int index) => installers?[index];
        if (selections.Count == 1) return ForSelection(selections[0], InstallerAt(0));
        int elevatingUserApps = selections.Where((selection, index) =>
            selection.Scope == UpdateScope.User && InstallerAt(index) is { IsVendor: true, ExpectsElevation: true }).Count();
        if (elevatingUserApps > 0)
        {
            int needing = selections.Where((selection, index) => ForSelection(selection, InstallerAt(index)).Status == AdministratorRequirementStatus.Required).Count();
            return new(AdministratorRequirementStatus.Required, $"Administrator approval required for {needing} of {selections.Count} apps",
                $"After you confirm, Windows may ask for administrator permission (UAC) when each of these apps' updates starts. {elevatingUserApps} of them " +
                "are current-user apps whose own installer asks for it; PC Steward itself never asks for those and keeps running without administrator rights. " +
                "If you allow it, that installer can change things for every user of this PC. " + WingetUserInstallerText.PublisherCheck);
        }
        bool vendorUserApps = installers?.Any(installer => installer is { IsVendor: true }) == true;
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
            "These are current-user updates. PC Steward will not request administrator permission for them. An app's own installer can still ask Windows for permission." +
            (vendorUserApps ? " If Windows asks while one of them runs, choose No: these updates shouldn't need it." : string.Empty));
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
