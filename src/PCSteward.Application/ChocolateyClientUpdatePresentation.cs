using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>Presentation-safe review sheet for a Chocolatey client self-update. Every string is fixed or a validated frozen version.</summary>
public sealed record ChocolateyClientUpdateReviewView(
    string Title,
    string Versions,
    string WhatHappens,
    IReadOnlyList<string> Disclosures,
    string Verification,
    string? UnavailableReason);

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, FR-HIS-01, TM-07/13; ADR-031 design mapping §2 (R4), §3.6, §3.7 and §3a: the review sheet,
/// Verify and history texts for <see cref="UpdateRouteContract.ChocolateyClientSelfUpdateV1"/>. Display only: nothing here
/// decides trust, admission or an outcome, and versions come only from the frozen review after a strict format check. The
/// route stays unavailable while <c>HasSupportedMode</c> does not admit it.
/// </summary>
public static class ChocolateyClientUpdatePresentation
{
    public const string HistoryRouteText =
        "Chocolatey client update to a release PC Steward has reviewed, through PC Steward's administrator helper.";

    public const string WhatHappensText =
        "After you approve the administrator prompt, the Chocolatey version that is installed now (already checked against PC Steward's " +
        "reviewed list) runs one fixed command that updates only the chocolatey package to exactly this version from the Chocolatey " +
        "Community repository.";

    public const string VerificationText =
        "Afterwards PC Steward checks every Chocolatey client file and the installed package against the reviewed release. Anything " +
        "else is shown as installed, not verified, and Chocolatey updates stay unavailable until it is resolved.";

    public const string UnavailableText = "Not available yet: this kind of update is waiting for checks on this computer.";

    /// <summary>Design §2 R4: the native side effects of Chocolatey's own setup, disclosed before review.</summary>
    public static IReadOnlyList<string> Disclosures { get; } = Array.AsReadOnly(new[]
    {
        "Chocolatey's setup runs with administrator rights and resets the access rules of its own folder to Chocolatey's defaults.",
        "It may add Chocolatey to the system PATH and add a Chocolatey line to your existing PowerShell profile.",
        "It replaces its own program files. The previous version can leave a file ending in .old behind, which Chocolatey removes the next " +
            "time it runs a package script.",
        "The new Chocolatey version runs once at the end of the update.",
        ReviewedUpdateLingeringProcesses.ReviewDisclosure,
    });

    public const string VerifiedLeftoverNote =
        @"A leftover from the previous version, helpers\Chocolatey.PowerShell.dll.old, matches a release PC Steward has reviewed. " +
        "Chocolatey removes it the next time it runs a package script; PC Steward never deletes files in Chocolatey's folder.";

    public const string NotVerifiedLeftoverMessage =
        @"Installed, not verified: Chocolatey's folder contains helpers\Chocolatey.PowerShell.dll.old, which does not match any release " +
        "PC Steward has reviewed. PC Steward never deletes files in Chocolatey's folder. Next step: run any Chocolatey package install or " +
        "upgrade from an administrator prompt (Chocolatey removes the leftover itself), then check again.";

    public const string NotVerifiedReleaseMessage =
        "Installed, not verified: Chocolatey's files do not exactly match the reviewed release that was approved, so Chocolatey updates " +
        "stay unavailable until its files match a reviewed release again.";

    public const string NotVerifiedUnreadableMessage =
        "Installed, not verified: PC Steward could not finish checking Chocolatey's files, so Chocolatey updates stay unavailable.";

    /// <summary>"Update Chocolatey to X" for a well-formed frozen candidate; otherwise a fixed generic title.</summary>
    public static string Title(string? targetVersion) =>
        IsReleaseVersion(targetVersion) ? $"Update Chocolatey to {targetVersion}" : "Update Chocolatey";

    /// <summary>
    /// The review sheet for a frozen client self-update, or null when the operation is not that route's exact shape (or its
    /// versions are malformed, or the target is not newer by the frozen data). The newer-version check here is display
    /// hygiene; the compiled reviewed list alone decides what can be offered.
    /// </summary>
    public static ChocolateyClientUpdateReviewView? Review(ReviewedUpdateOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!operation.HasCurrentSchema || operation.RouteContract != UpdateRouteContract.ChocolateyClientSelfUpdateV1 ||
            operation.ProviderGuardContract != ReviewedProviderGuardContract.ChocolateyClientSelfUpdate ||
            !string.Equals(operation.Selection.PackageId, ReviewedUpdateOperation.ChocolateyClientPackageId, StringComparison.Ordinal))
            return null;
        string installed = operation.Selection.InstalledVersion, target = operation.Selection.CandidateVersion;
        if (!IsReleaseVersion(installed) || !IsReleaseVersion(target) ||
            VersionComparison.Compare(new VersionComparisonRequest(installed, target)).Outcome != VersionComparisonOutcome.Newer)
            return null;
        return new ChocolateyClientUpdateReviewView(Title(target), $"Chocolatey {installed} → {target}", WhatHappensText, Disclosures,
            VerificationText, ReviewedUpdateReview.HasSupportedMode(operation) ? null : UnavailableText);
    }

    /// <summary>The Verify/history message for a client self-update verification detail that is not plain Success.</summary>
    public static string NotVerifiedMessage(PostUpdateVerificationDetail detail) => detail switch
    {
        PostUpdateVerificationDetail.ClientLeftoverNotListed => NotVerifiedLeftoverMessage,
        PostUpdateVerificationDetail.ClientChanged or PostUpdateVerificationDetail.None => NotVerifiedReleaseMessage,
        _ => NotVerifiedUnreadableMessage,
    };

    /// <summary>A stable normalized release version: 3 or 4 dot-separated decimal parts without leading zeros, at most 32 characters.</summary>
    internal static bool IsReleaseVersion(string? value) =>
        value is { Length: > 0 and <= 32 } && value.Split('.') is { Length: 3 or 4 } parts &&
        parts.All(part => part.Length is > 0 and <= 9 && part.All(char.IsAsciiDigit) && (part == "0" || part[0] != '0'));
}
