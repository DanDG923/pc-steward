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
        "This is the AI test build. It only runs the one AI test, so it doesn't update apps, change the scheduled check, reset PC Steward or update itself. Close it and use the installed PC Steward for that.";
}
