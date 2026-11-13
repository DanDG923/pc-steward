namespace PCSteward.Application;

/// <summary>One step of the guided first run: a short label for where the owner is, a title, one message and its buttons.</summary>
public sealed record FirstRunStep(string StepLabel, string Title, string Message, string PrimaryLabel, string PrimaryHelp, string? SecondaryLabel = null, string? SecondaryHelp = null)
{
    /// <summary>The step's heading, for example "Step 1 of 2: See what is installed"; the title alone when the step has no label.</summary>
    public string Heading => StepLabel.Length == 0 ? Title : $"{StepLabel}: {Title}";
}

/// <summary>
/// UI review 2026-11-13 (item 21; FR-UI-01/05, FR-SRC-01 consent unchanged): a two-step start for a new owner. Step 1 scans this
/// PC. Step 2 asks one question, once, straight after that first scan: look for updates online? Saying yes opens the same consent
/// dialog as Settings, which shows what is shared and asks before anything is sent; saying "Not now" changes nothing and the
/// question does not come back. Presentation only: building these values scans, saves and sends nothing.
/// </summary>
public static class FirstRunPresenter
{
    public const string NotNowLabel = "Not now";

    /// <summary>What "Not now" means, so the owner knows where the choice lives.</summary>
    public const string NotNowHelp = "Update checks stay off. You can turn them on at any time in Settings.";

    /// <summary>
    /// Step 1, shown on Home until this PC has been scanned. It is numbered only while update checks are off, because an owner
    /// who already turned them on (and then cleared the app list) has no second step to answer.
    /// </summary>
    public static FirstRunStep ScanStep(bool updateChecksOn) => new(
        updateChecksOn ? string.Empty : "Step 1 of 2",
        "See what is installed",
        "The first scan reads the list of installed apps that Windows keeps. It doesn't go online, ask for administrator permission or change any software.",
        "Scan this PC",
        "(reads the installed apps; nothing is changed)");

    /// <summary>Step 2, shown on Home straight after the first scan while update checks are off.</summary>
    public static FirstRunStep LookOnlineStep(int apps) => new(
        "Step 2 of 2",
        "Look for updates online?",
        $"PC Steward found {DisplayFormat.Plural(apps, "app")} on this PC. To see which have updates, it asks WinGet and Chocolatey Community, and those services may see which apps are installed here. You are asked to approve before anything is sent. If you approve, PC Steward looks for updates straight away. Nothing is installed.",
        "Look for updates online…",
        "(shows what is shared and asks you first)",
        NotNowLabel,
        NotNowHelp);

    /// <summary>
    /// Whether Home asks the step 2 question: only in the session where this PC was first scanned, only while update checks are
    /// off, and only until the owner has answered it either way.
    /// </summary>
    public static bool AsksAboutUpdates(bool firstScanThisSession, bool hasApps, bool updateChecksOn, bool answered) =>
        firstScanThisSession && hasApps && !updateChecksOn && !answered;

    /// <summary>Where a finished scan on the Scan page leads: the step 2 question on Home after the first scan, otherwise the app list.</summary>
    public static ShellPage AfterScan(bool asksAboutUpdates) => asksAboutUpdates ? ShellPage.Home : ShellPage.InstalledApps;

    // After a factory reset (the page shown before anything else). Plain words: no "inventory", no "source connection".
    public const string AfterResetTitle = "Start fresh with PC Steward";
    public const string AfterResetDescription = "The factory reset is complete. Nothing was scanned and nothing went online.";
    public const string AfterResetEyebrow = "After a factory reset";
    public const string AfterResetRemovedTitle = "PC Steward's data was removed";
    public const string AfterResetRemovedMessage = "The app list, update results, settings, decisions, history, downloaded files, logs and the scheduled check were all part of the reset.";
    public const string AfterResetNextTitle = "Start again with a scan";
    public const string AfterResetNextMessage = "When you continue, PC Steward confirms the reset finished and opens the scan page. It doesn't start the scan for you.";
    public const string AfterResetContinueLabel = "Continue";
    public const string AfterResetContinueHelp = "(opens the scan page; nothing is scanned yet)";
}
