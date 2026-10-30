namespace PCSteward.Domain;

/// <summary>Compiled mode-only mapping. It never substitutes for fresh native operation evidence.</summary>
public static class UpdateInstallerModeCapabilities
{
    public static ReviewedInstallerMode? Resolve(UpdateProvider provider, UpdateSource source, UpdateScope scope) =>
        (provider, source, scope) is (UpdateProvider.WinGet, UpdateSource.WinGet, UpdateScope.User)
            ? ReviewedInstallerMode.Selected(InstallerMode.Silent) : null;
}
