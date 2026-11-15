namespace PCSteward.Desktop;

public static class AppIdentity
{
    public const string TechnicalName = "PCSteward";
    public const string DisplayName = "PC Steward";

    /// <summary>
    /// The name in the window's title and title bar. Task 71: the one-request AI test build says what it is there, so it is never
    /// mistaken for the installed release.
    /// </summary>
    public static string WindowName => PCSteward.Domain.AdvisoryAiFirstLiveTestBuild.IsTestBuild ? DisplayName + " — AI test build, not a release" : DisplayName;

    /// <summary>Task 71: why an action that changes the PC, its schedule, its data or PC Steward itself is off in the AI test build.</summary>
    public const string TestBuildActionOff =
        "This is the AI test build. It doesn't update apps, change the scheduled check, clear or reset PC Steward's data or update itself. Close it and use the installed PC Steward for that.";

    /// <summary>Task 71 (T71-01): why the AI test build stopped at start-up while a factory reset is pending.</summary>
    public const string TestBuildResetPending =
        "This is the AI test build. A factory reset that the installed PC Steward started hasn't finished, so the test build stopped without opening, changing or resetting anything. Close it and start the installed PC Steward to deal with the reset.";
}
