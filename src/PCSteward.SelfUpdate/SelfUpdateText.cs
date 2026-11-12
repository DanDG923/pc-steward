using System.Globalization;
using PCSteward.Execution;

namespace PCSteward.SelfUpdate;

/// <summary>
/// Owner-facing sentences for the update check (ADR-032 revision 2 §1): a refusal always says why, and only a release that
/// passed every check is ever described as current.
/// </summary>
public static class SelfUpdateText
{
    /// <summary>ADR-032 §8: why a package update is refused while a PC Steward update may have left an install unlike a signed release.</summary>
    public const string OtherUpdatesPaused = "PC Steward's own update did not finish as checked, so other updates are paused until you reinstall PC Steward from its release page. No update was started.";

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

    /// <summary>ADR-032 revision 2 §5: the desktop's first check of the downloaded installer.</summary>
    public static string Describe(PcStewardUpdatePrecheck precheck) => precheck switch
    {
        PcStewardUpdatePrecheck.Passed => "The installer was downloaded and passed PC Steward's first check. Installing asks Windows for permission once.",
        PcStewardUpdatePrecheck.NotPinned => Describe(SelfUpdateRefusal.MsiNotPinned) + " It was deleted.",
        PcStewardUpdatePrecheck.TablesDiffer => Describe(SelfUpdateRefusal.MsiTablesMismatch) + " It was deleted.",
        PcStewardUpdatePrecheck.StructureRefused => "The installer contains something PC Steward's installer never has (such as a custom program or a service). It was deleted.",
        _ => "The downloaded installer could not be read. It was deleted.",
    };

    /// <summary>ADR-032 revision 2 §6: how the owner's Install ended.</summary>
    public static string Describe(PcStewardInstallResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Outcome switch
        {
            PcStewardInstallOutcome.Ready or PcStewardInstallOutcome.ReadyJobNotSaved =>
                "The update is ready. PC Steward will now close so Windows can install it. Open PC Steward again in a few minutes to see the result.",
            PcStewardInstallOutcome.Refused => "The update was stopped before anything was installed. " + DescribeHelperRefusal(result.RefusalCode),
            PcStewardInstallOutcome.ElevationDeclined => "Windows permission was not given, so nothing was installed.",
            PcStewardInstallOutcome.NotStarted => "PC Steward did not ask Windows for permission because a check of its own installation failed. Nothing was installed. Reinstall PC Steward from its release page if this keeps happening.",
            _ => "PC Steward's administrator step did not answer. If Windows installed nothing, PC Steward stays as it is; reopen PC Steward to see the result.",
        };
    }

    /// <summary>The elevated helper's fixed refusal codes (ADR-032 revision 2 §6).</summary>
    public static string DescribeHelperRefusal(string? code) => code switch
    {
        "install-not-intact" => "PC Steward's own installation did not pass its check, so it cannot update itself. Reinstall it from its release page.",
        "version-not-newer" => "This release is not newer than the PC Steward you have, so nothing was installed.",
        "installer-policy-busy" => "Another PC Steward install was running, or PC Steward could not put back a Windows Installer setting it changed during an earlier update, so nothing was installed. Try again in a few minutes.",
        "per-user-registration" => "Windows lists a PC Steward installation for your account alone, which PC Steward never makes, so nothing was installed. Another program may have added it.",
        "user-installs-allowed" => "PC Steward could not turn on Windows Installer's protection against installs for your account alone for the length of the update, so nothing was installed. Your Windows settings were left as they were. Try again later.",
        "profile-unavailable" or "input-unreadable" or "input-changed" => "The downloaded files could not be read as they were checked. Download the update again.",
        "work-folder-unavailable" or "helper-copy-failed" or "job-files-failed" or "finish-not-started" => "Windows could not prepare the protected folder the update installs from.",
        "check-file-refused" or "record-not-approved" => "The release's check file did not match what you approved.",
        "run-not-genuine" => Describe(SelfUpdateRefusal.RunNotGenuine),
        "artifact-gone" => Describe(SelfUpdateRefusal.CheckArtifactGone),
        "artifact-mismatch" => Describe(SelfUpdateRefusal.CheckArtifactMismatch),
        "could-not-confirm" => "The release could not be confirmed with GitHub. Check your connection and try again.",
        "msi-not-pinned" => Describe(SelfUpdateRefusal.MsiNotPinned),
        "msi-unreadable" or "extract-failed" or "image-layout-refused" => "The installer could not be read and unpacked as expected.",
        "msi-structure-refused" => "The installer contains something PC Steward's installer never has (such as a custom program or a service).",
        "msi-tables-differ" or "content-differs" => Describe(SelfUpdateRefusal.MsiTablesMismatch),
        "executables-not-pinned" => "The programs inside the installer are not signed with one of PC Steward's release keys.",
        "timed-out" => "The checks took too long and were stopped.",
        _ => "Something unexpected happened, so the update was stopped.",
    };

    /// <summary>ADR-032 revision 2 §8: the recorded update's state when PC Steward opens.</summary>
    public static string Describe(PcStewardUpdateStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        string to = "PC Steward " + status.ToVersion;
        return status.State switch
        {
            PcStewardUpdateState.Updated => "Updated to " + status.ToVersion + ". Every installed file was checked against the release build.",
            PcStewardUpdateState.InProgress => "The update to " + status.ToVersion + " is still installing. Reopen PC Steward in a few minutes.",
            PcStewardUpdateState.RestartToFinish => "Restart your PC to finish updating to " + status.ToVersion + ", then reopen PC Steward.",
            PcStewardUpdateState.Inconclusive => "PC Steward could not find the result of the update to " + status.ToVersion + ". Check the version shown below; reinstall PC Steward from its release page if it looks wrong.",
            _ => status.Code switch
            {
                "install-failed" => "Windows could not install " + to + " and kept " + status.FromVersion + ". The install log is in " + status.InstallLogPath + " (administrators only).",
                "pc-steward-did-not-close" => to + " was not installed because PC Steward did not close in time. Nothing was changed.",
                "refused-before-install" => to + " was not installed: a check just before installing failed. Nothing was changed.",
                "mismatch-uninstalled" => "The installed files did not match the release build, so PC Steward was removed. Reinstall it from its release page.",
                "mismatch-uninstall-failed" => "The installed files did not match the release build and could not be removed. Reinstall PC Steward from its release page.",
                _ => "The update did not finish as checked. Don't start other updates until you reinstall PC Steward from its release page.",
            },
        };
    }
}
