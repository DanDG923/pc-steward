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

    public const string NotVerifiedLeftoverNotSourceMessage =
        @"Installed, not verified: Chocolatey's folder contains helpers\Chocolatey.PowerShell.dll.old from a reviewed release other than " +
        "the one this update started from. PC Steward never deletes files in Chocolatey's folder. Next step: run any Chocolatey package " +
        "install or upgrade from an administrator prompt (Chocolatey removes the leftover itself), then check again.";

    /// <summary>
    /// The closing sentence of every release-incomplete and not-reported message, kept in the combined watchdog-and-release
    /// messages too (independent review I2), so no required warning is dropped when two faults are reported together.
    /// </summary>
    public const string StaysUnavailableUntilReviewedText =
        "Chocolatey updates stay unavailable until its files match a reviewed release again.";

    /// <summary>
    /// Independent review L1: "Ran, not verified" (never "Installed"): the run did not happen under the reviewed conditions, so
    /// nothing is known about what was installed.
    /// </summary>
    public const string NotVerifiedReleaseIncompleteMessage =
        "Ran, not verified: PC Steward could not fully release its hold on Chocolatey's files after the update started, so the " +
        "update did not run under the reviewed conditions. " + StaysUnavailableUntilReviewedText;

    /// <summary>
    /// The manual recovery step after a watchdog stop that never showed Chocolatey's folder as changeable by the user
    /// (design §2.1 R5, <see cref="StoppedWatchFailedMessage"/> only). PC Steward never repairs Chocolatey itself.
    /// </summary>
    public const string WatchdogRecoveryStep =
        "PC Steward does not repair Chocolatey automatically. Next step: from an administrator prompt, run " +
        "choco upgrade chocolatey --version <the version you approved> --force (or repair Chocolatey), then check again.";

    /// <summary>
    /// Independent review H1 (design §2.2 R5): after a setup override or a user-changeable folder during the run, the folder
    /// may stay changeable by the user indefinitely, so running any Chocolatey code from it elevated could run tampered code
    /// with administrator rights. The only advice is to treat the folder as untrusted, never run choco elevated from it, and
    /// reinstall from Chocolatey's official instructions. PC Steward does not repair it and never changes its access rules.
    /// </summary>
    public const string UntrustedFolderRecoveryStep =
        "Treat the Chocolatey folder as untrusted: do not run choco from an administrator prompt, because files in that folder " +
        "may have been replaced. Remove or quarantine the Chocolatey folder and reinstall Chocolatey using Chocolatey's official " +
        "installation instructions. PC Steward will not repair it.";

    public const string StoppedSetupOverrideMessage =
        "Stopped, not verified: while Chocolatey was updating, a setting that makes Chocolatey's setup give your account write access " +
        "to its folder (ChocolateyInstallAllowCurrentUser or ChocolateyAllowInsecureRootDirectory) appeared in the environment, so " +
        "PC Steward stopped the update immediately. Chocolatey may be partly updated and its folder may now be changeable by your " +
        "account. Remove that environment variable first. " + UntrustedFolderRecoveryStep;

    public const string StoppedRootWritableMessage =
        "Stopped, not verified: while Chocolatey was updating, its folder became changeable by your account, so PC Steward stopped " +
        "the update immediately. Chocolatey may be partly updated and files in its folder may have been changed. Remove any " +
        "ChocolateyInstallAllowCurrentUser environment variable first. " + UntrustedFolderRecoveryStep;

    public const string StoppedWatchFailedMessage =
        "Stopped, not verified: PC Steward could not keep checking Chocolatey's folder and settings while the update ran, so it " +
        "stopped the update. Chocolatey may be partly updated. " + WatchdogRecoveryStep;

    /// <summary>
    /// ADR-031 design §8.3: shown after a watchdog stop message when the hold release was also incomplete, so both warnings are
    /// retained; the watchdog message keeps its own action advice (the untrusted-folder advice is never replaced).
    /// </summary>
    public const string ReleaseIncompleteAlsoNote =
        "PC Steward also could not fully release its hold on Chocolatey's files after the update started, so the update did not run " +
        "under the reviewed conditions. " + StaysUnavailableUntilReviewedText;

    public const string StoppedSetupOverrideReleaseIncompleteMessage = StoppedSetupOverrideMessage + " " + ReleaseIncompleteAlsoNote;

    public const string StoppedRootWritableReleaseIncompleteMessage = StoppedRootWritableMessage + " " + ReleaseIncompleteAlsoNote;

    public const string StoppedWatchFailedReleaseIncompleteMessage = StoppedWatchFailedMessage + " " + ReleaseIncompleteAlsoNote;

    /// <summary>
    /// ADR-031 design §8.3: a started client self-update whose accepted helper reply did not reach the workflow with a run-fault
    /// value. Never Success: verification is not attempted.
    /// </summary>
    public const string RunFaultNotReportedMessage =
        "Ran, not verified: PC Steward's administrator helper did not report whether the update ran under the reviewed " +
        "conditions, so PC Steward did not verify it. " + StaysUnavailableUntilReviewedText;

    /// <summary>
    /// Independent review M1 (ADR-031 design §8.6): appended to an Uncertain client self-update whose launch may have started
    /// (invocation Unknown or Started) while no watchdog stop or release fault was reported (run fault None or missing) — for
    /// example a helper timeout, throw or unknown result after the launch. The watchdog's verdict is then unknown, so the
    /// folder may have been left changeable by the user: the advice is the untrusted-folder step, never a choco command.
    /// The outcome stays Uncertain (never Success).
    /// </summary>
    public const string LostTrackCautionMessage =
        "PC Steward lost track of the Chocolatey update, so it cannot tell whether the Chocolatey folder was left writable. " +
        UntrustedFolderRecoveryStep;

    /// <summary>
    /// ADR-031 design §8.3: the fixed recovery message for a helper-reported client run fault, or null for
    /// <see cref="ReviewedClientRunFault.IsNone"/>. A watchdog stop selects its own message (setup override and user-changeable
    /// folder: the untrusted-folder advice, never <c>choco upgrade</c>; failed watch: the forced reinstall advice); an incomplete
    /// release alone selects <see cref="NotVerifiedReleaseIncompleteMessage"/>; both together keep the watchdog message and add
    /// <see cref="ReleaseIncompleteAlsoNote"/>.
    /// </summary>
    public static string? RunFaultMessage(ReviewedClientRunFault fault)
    {
        ArgumentNullException.ThrowIfNull(fault);
        return (fault.WatchdogStop, fault.ReleaseIncomplete) switch
        {
            (ReviewedClientWatchdogStop.None, false) => null,
            (ReviewedClientWatchdogStop.None, true) => NotVerifiedReleaseIncompleteMessage,
            (ReviewedClientWatchdogStop.SetupOverrideDuringRun, false) => StoppedSetupOverrideMessage,
            (ReviewedClientWatchdogStop.SetupOverrideDuringRun, true) => StoppedSetupOverrideReleaseIncompleteMessage,
            (ReviewedClientWatchdogStop.RootWritableDuringRun, false) => StoppedRootWritableMessage,
            (ReviewedClientWatchdogStop.RootWritableDuringRun, true) => StoppedRootWritableReleaseIncompleteMessage,
            (ReviewedClientWatchdogStop.WatchFailedDuringRun, false) => StoppedWatchFailedMessage,
            (ReviewedClientWatchdogStop.WatchFailedDuringRun, true) => StoppedWatchFailedReleaseIncompleteMessage,
            // Unreachable for a constructed value; fail closed with the strongest advice rather than none.
            _ => StoppedRootWritableReleaseIncompleteMessage,
        };
    }

    public const string VerifiedMessage = "Verified: Chocolatey's files and package match the reviewed release that was approved.";

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

    /// <summary>
    /// ADR-031 design §3a: the Verify text for a verified client self-update; the reviewed-leftover note adds
    /// <see cref="VerifiedLeftoverNote"/>. Any other note is not a verified client result (the workflow refuses it).
    /// </summary>
    public static string VerifiedText(PostUpdateVerificationNote note) => note == PostUpdateVerificationNote.ClientReviewedLeftover
        ? VerifiedMessage + " " + VerifiedLeftoverNote : VerifiedMessage;

    /// <summary>The Verify/history message for a client self-update verification detail that is not plain Success.</summary>
    public static string NotVerifiedMessage(PostUpdateVerificationDetail detail) => detail switch
    {
        PostUpdateVerificationDetail.ClientLeftoverNotListed => NotVerifiedLeftoverMessage,
        PostUpdateVerificationDetail.ClientLeftoverNotSource => NotVerifiedLeftoverNotSourceMessage,
        PostUpdateVerificationDetail.ClientReleaseIncomplete => NotVerifiedReleaseIncompleteMessage,
        PostUpdateVerificationDetail.ClientSetupOverrideDuringRun => StoppedSetupOverrideMessage,
        PostUpdateVerificationDetail.ClientRootWritableDuringRun => StoppedRootWritableMessage,
        PostUpdateVerificationDetail.ClientWatchFailedDuringRun => StoppedWatchFailedMessage,
        PostUpdateVerificationDetail.ClientChanged or PostUpdateVerificationDetail.None => NotVerifiedReleaseMessage,
        _ => NotVerifiedUnreadableMessage,
    };

    /// <summary>True for exactly one of the fixed client self-update not-verified messages (history recognition).</summary>
    internal static bool IsNotVerifiedMessage(string? message) => message is NotVerifiedLeftoverMessage or NotVerifiedLeftoverNotSourceMessage or
        NotVerifiedReleaseIncompleteMessage or NotVerifiedReleaseMessage or NotVerifiedUnreadableMessage or StoppedSetupOverrideMessage or
        StoppedRootWritableMessage or StoppedWatchFailedMessage or StoppedSetupOverrideReleaseIncompleteMessage or
        StoppedRootWritableReleaseIncompleteMessage or StoppedWatchFailedReleaseIncompleteMessage or RunFaultNotReportedMessage;

    /// <summary>
    /// Independent review L1 (ADR-031 design §8.3/§8.6, FR-HIS-01/03): the fixed leading marker the workflow writes at the start of
    /// an Uncertain client self-update's recorded message when the launch may have started but no watchdog stop or release fault
    /// was reported (<see cref="LostTrackCautionMessage"/>).
    /// </summary>
    internal const string LostTrackMarker = "client-lost-track";

    // Independent review L1: one fixed leading marker per fixed Uncertain detail. Each is matched only as a whole token at the
    // start of the recorded message (after the workflow's other leading markers), never by the message text or its ending.
    private static readonly (string Marker, string Message)[] UncertainMarkers =
    [
        ("client-run-fault-release-incomplete", NotVerifiedReleaseIncompleteMessage),
        ("client-run-fault-setup-override", StoppedSetupOverrideMessage),
        ("client-run-fault-setup-override-release-incomplete", StoppedSetupOverrideReleaseIncompleteMessage),
        ("client-run-fault-root-writable", StoppedRootWritableMessage),
        ("client-run-fault-root-writable-release-incomplete", StoppedRootWritableReleaseIncompleteMessage),
        ("client-run-fault-watch-failed", StoppedWatchFailedMessage),
        ("client-run-fault-watch-failed-release-incomplete", StoppedWatchFailedReleaseIncompleteMessage),
        (LostTrackMarker, LostTrackCautionMessage),
    ];

    /// <summary>
    /// Independent review L1: the fixed leading marker for a codec-validated client run fault (the marker of its
    /// <see cref="RunFaultMessage"/>), or null for <see cref="ReviewedClientRunFault.IsNone"/>. Written only by the workflow.
    /// </summary>
    internal static string? RunFaultMarker(ReviewedClientRunFault fault)
    {
        string? message = RunFaultMessage(fault);
        if (message is null) return null;
        foreach ((string marker, string fixedMessage) in UncertainMarkers)
            if (string.Equals(fixedMessage, message, StringComparison.Ordinal)) return marker;
        throw new InvalidOperationException("Every run-fault message has a fixed marker.");
    }

    /// <summary>
    /// Independent review L1, history recognition: when <paramref name="text"/> (an Uncertain client record's message after the
    /// workflow's other leading markers) starts with one of the fixed markers as a whole token, its fixed detail message and the
    /// token's length including one following space; otherwise null. Helper diagnostic text is never interpreted. Display only.
    /// </summary>
    internal static string? LeadingUncertainMarkerMessage(ReadOnlySpan<char> text, out int markerLength)
    {
        foreach ((string marker, string message) in UncertainMarkers)
        {
            if (!text.StartsWith(marker, StringComparison.Ordinal)) continue;
            if (text.Length == marker.Length) { markerLength = marker.Length; return message; }
            if (text[marker.Length] == ' ') { markerLength = marker.Length + 1; return message; }
        }
        markerLength = 0;
        return null;
    }

    /// <summary>A stable normalized release version: 3 or 4 dot-separated decimal parts without leading zeros, at most 32 characters.</summary>
    internal static bool IsReleaseVersion(string? value) =>
        value is { Length: > 0 and <= 32 } && value.Split('.') is { Length: 3 or 4 } parts &&
        parts.All(part => part.Length is > 0 and <= 9 && part.All(char.IsAsciiDigit) && (part == "0" || part[0] != '0'));
}
