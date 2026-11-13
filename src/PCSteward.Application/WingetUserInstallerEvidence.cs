using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>
/// D7 design §3 item 1: the answer of one read of the installer WinGet would use for a per-user <c>winget</c>-source update.
/// <see cref="IsMsix"/>: a Store-type installer, which keeps the stage C4 evidence. Otherwise <see cref="Installer"/> is the installer
/// to bind (an MSIX package or a vendor installer), or null with <see cref="Message"/> saying why nothing can be reviewed.
/// </summary>
public sealed record WingetUserInstallerCheck(bool IsMsix, WinGetUserInstaller? Installer, string Message)
{
    /// <summary>A Store-type installer: no binding (C4 evidence).</summary>
    public static WingetUserInstallerCheck Msix() => new(true, null, string.Empty);

    /// <summary>A vendor installer to bind.</summary>
    public static WingetUserInstallerCheck Vendor(WinGetUserInstaller installer) =>
        new(false, installer ?? throw new ArgumentNullException(nameof(installer)), string.Empty);

    /// <summary>Nothing can be reviewed.</summary>
    public static WingetUserInstallerCheck Refused(string message) => new(false, null, message);
}

/// <summary>
/// D7 design §3 item 1: reads, without downloading anything, the installer WinGet's structured API would choose for the exact target
/// version of a per-user <c>winget</c>-source update, with user scope and the reviewed architecture, as the command line chooses it.
/// </summary>
public interface IWingetUserInstallerEvidenceSource
{
    Task<WingetUserInstallerCheck> ReadAsync(UpdateJobSelection selection, CancellationToken cancellationToken);
}

/// <summary>Fixed review texts of the per-user vendor-installer evidence (D7 design §3).</summary>
public static class WingetUserInstallerText
{
    /// <summary>The installer could not be read; nothing was reviewed or started.</summary>
    public const string UnreadableMessage =
        "PC Steward couldn't check which installer WinGet would use for this update, so it can't be reviewed. Nothing was started.";

    /// <summary>WinGet would use an installer type PC Steward has no reviewed per-user route for.</summary>
    public const string UnsupportedTypeMessage =
        "WinGet would use a kind of installer PC Steward can't review for your account, so this update isn't offered. Nothing was started.";

    /// <summary>The installer read at Confirm differs from the reviewed one; nothing was approved or started.</summary>
    public const string ChangedMessage =
        "The installer WinGet would use changed after you reviewed this update. Review it again; no update was started.";

    /// <summary>The fresh inventory had no live observation to compare with afterwards (§3 item 4); nothing was approved or started.</summary>
    public const string NoBaselineMessage =
        "PC Steward couldn't record the apps installed for every user before this update, so it can't check them afterwards. No update was started.";

    /// <summary>Review text for a vendor installer whose manifest declares no need for administrator rights (§3 item 3).</summary>
    public const string NoElevationDisclosure =
        "This update runs without administrator rights. The app's own installer decides where it writes. If Windows asks for administrator " +
        "permission while it runs, choose No: this update shouldn't need it.";

    /// <summary>
    /// Review text for a vendor installer whose manifest says it asks for administrator permission (owner answer Q1-B). Security review
    /// 2026-11-13: on this route WinGet runs unelevated, so the installer waits in the account's own Temp folder until Windows asks;
    /// a program already running as the owner could swap it there. The Windows prompt is then the last check, so the text says what to
    /// look for on it.
    /// </summary>
    public const string ElevationWarning =
        "This app's installer asks for administrator permission. Windows will ask you to allow it while the update runs; PC Steward itself " +
        "never asks for it. If you allow it, the installer can change things for every user of this PC. " + PublisherCheck;

    /// <summary>
    /// Security review 2026-11-13 (owner's choice): what to check on the Windows prompt before allowing a per-user installer that asks
    /// for administrator permission. Shown on single and batch reviews alike.
    /// </summary>
    public const string PublisherCheck =
        "Before you choose Yes, check that Windows names the app's publisher as the verified publisher. If it says Unknown publisher or " +
        "names someone else, choose No.";

    /// <summary>The review text for <paramref name="installer"/>.</summary>
    public static string Disclosure(WinGetUserInstaller installer)
    {
        ArgumentNullException.ThrowIfNull(installer);
        // The review sheet never asks this for an MSIX package: AdministratorRequirementPresenter shows the C4 text for it.
        return installer.ExpectsElevation ? ElevationWarning : NoElevationDisclosure;
    }

    /// <summary>The installer type in words, for the review.</summary>
    public static string TypeInWords(WinGetUserInstallerType type) => type switch
    {
        WinGetUserInstallerType.Msi => "Vendor installer (MSI)",
        WinGetUserInstallerType.Wix => "Vendor installer (WiX MSI)",
        WinGetUserInstallerType.Exe => "Vendor installer (EXE)",
        WinGetUserInstallerType.Inno => "Vendor installer (Inno Setup)",
        WinGetUserInstallerType.Nullsoft => "Vendor installer (Nullsoft)",
        WinGetUserInstallerType.Burn => "Vendor installer (WiX Burn bundle)",
        WinGetUserInstallerType.Portable => "Portable app placed by WinGet",
        WinGetUserInstallerType.Zip => "Zip archive unpacked by WinGet",
        WinGetUserInstallerType.Msix => "MSIX package",
        _ => "Vendor installer",
    };
}
