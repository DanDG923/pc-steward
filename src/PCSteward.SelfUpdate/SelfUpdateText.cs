using System.Globalization;

namespace PCSteward.SelfUpdate;

/// <summary>
/// Owner-facing sentences for the update check (ADR-032 revision 2 §1): a refusal always says why, and only a release that
/// passed every check is ever described as current.
/// </summary>
public static class SelfUpdateText
{
    public static string Describe(SelfUpdateRefusal refusal) => refusal switch
    {
        SelfUpdateRefusal.ReleaseUnreadable => "GitHub's description of the release is not in the expected form.",
        SelfUpdateRefusal.UnexpectedAssets => "The release does not have exactly the two PC Steward files it should have.",
        SelfUpdateRefusal.CheckFileUnreadable => "The release's check file is not in the expected form.",
        SelfUpdateRefusal.CheckFileForOtherKeys => "The release's check file names release keys this version does not use.",
        SelfUpdateRefusal.TagMismatch => "The release's tag does not match the version its check file names.",
        SelfUpdateRefusal.RunNotFound => "GitHub has no record of the build the release names.",
        SelfUpdateRefusal.RunNotGenuine => "The build the release names is not a successful release build of PC Steward on a release branch.",
        SelfUpdateRefusal.CheckArtifactGone => "GitHub no longer has the record of the release's check file (release builds keep it for 90 days). Install this release by hand.",
        SelfUpdateRefusal.CheckArtifactMismatch => "The release's check file is not the one GitHub recorded for its build.",
        SelfUpdateRefusal.MsiTooLarge => "The release's installer is larger than PC Steward accepts.",
        SelfUpdateRefusal.MsiDownloadFailed => "The installer could not be downloaded completely.",
        SelfUpdateRefusal.MsiNotPinned => "The installer is not signed with one of PC Steward's release keys.",
        SelfUpdateRefusal.MsiTablesMismatch => "The installer does not match the release build GitHub recorded.",
        SelfUpdateRefusal.UnexpectedError => "Something unexpected happened while checking the release, so it was not trusted.",
        _ => "The release could not be confirmed.",
    };

    public static string Describe(SelfUpdateCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        string at = check.CheckedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        return check.Status switch
        {
            SelfUpdateCheckStatus.NewerRelease => "PC Steward " + check.Release!.Version.Text + " is available. It was built by GitHub from a reviewed commit, and its check file is genuine.",
            SelfUpdateCheckStatus.CurrentRelease => "You have the newest published version (" + check.Release!.Version.Text + "). Checked " + at + ".",
            SelfUpdateCheckStatus.NoReleasesPublished => "No PC Steward release has been published yet. Checked " + at + ".",
            SelfUpdateCheckStatus.NotTrusted => "An update was found but failed a check. " + Describe(check.Refusal) + " Nothing was downloaded or changed.",
            _ => "Could not check for updates. GitHub could not be reached or answered unexpectedly. Nothing was changed.",
        };
    }
}
