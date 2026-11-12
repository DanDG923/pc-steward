namespace PCSteward.Domain;

/// <summary>
/// Compiled mode-only mapping. It never substitutes for fresh native operation evidence. Stage C4 (owner decision 2026-11-12):
/// every per-user WinGet update, from the <c>winget</c> or <c>msstore</c> source, is Silent. Machine scope has no reviewed mode yet
/// (Phase D stage D4b); it runs on protocol 1 under the ADR-025 transition amendment.
/// </summary>
public static class UpdateInstallerModeCapabilities
{
    public static ReviewedInstallerMode? Resolve(UpdateProvider provider, UpdateSource source, UpdateScope scope) =>
        (provider, source, scope) is (UpdateProvider.WinGet, UpdateSource.WinGet or UpdateSource.MicrosoftStore, UpdateScope.User)
            ? ReviewedInstallerMode.Selected(InstallerMode.Silent) : null;
}
