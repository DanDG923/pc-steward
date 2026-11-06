namespace PCSteward.Application;

/// <summary>
/// FR-UPD-04, FR-UI-03 (Task 73 Phase 1, ADR-030 foundation 9; audit P-H5): the one elevation story every Desktop surface tells.
/// It matches the composition: the Desktop is designed to run without administrator rights (nothing enforces that at startup, so
/// the copy does not promise it); checking for updates never asks for administrator permission;
/// Windows asks once per approved update job, after confirmation, only when a machine-wide update needs it; and machine-wide
/// updates are blocked before they start when the protected update helper is not installed. Copy only; it decides nothing.
/// </summary>
public static class ElevationStory
{
    public const string Summary =
        "PC Steward is designed to run without administrator rights. Checking for updates never asks for administrator permission. " +
        "When you confirm an approved update job that includes a machine-wide app, Windows asks for administrator permission once for that job. " +
        "PC Steward doesn't request administrator permission for current-user updates. An app's own installer can still ask Windows for permission.";

    public const string HelperRequirement =
        "Machine-wide updates run through PC Steward's protected update helper. If that helper is not installed in its protected location, " +
        "the update is blocked before it starts and nothing is changed.";

    public const string SourceConsent =
        "Turning this on starts no scan, check, download or update, and checking for updates never asks for administrator permission. " +
        "Each update still needs your review and confirmation. Windows asks for administrator permission once per approved job, " +
        "only when a machine-wide update needs it.";

    public const string HelperMissingOutcome =
        "The update was blocked before it could run. Machine-wide updates need PC Steward's protected update helper, " +
        "which is not installed in its protected location on this PC. No application was changed.";
}
