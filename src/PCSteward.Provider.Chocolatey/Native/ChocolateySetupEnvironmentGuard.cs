namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>
/// FR-UPD-15, TM-03/04/13; ADR-031 design mapping §2 R5 (pure rule): the client self-update may launch only while none of
/// the names pinned <c>chocolateysetup.psm1</c> uses to weaken or skip the root ACL can reach the elevated setup.
/// <list type="bullet">
/// <item>The compiled child environment is a complete explicit block: it must not carry
/// <see cref="ChocolateySetupEnvironmentReader.AllowCurrentUserName"/> or
/// <see cref="ChocolateySetupEnvironmentReader.AllowInsecureRootName"/> (ignoring case), and its <c>ChocolateyInstall</c>
/// must be exactly the observed root.</item>
/// <item>No pinned user-scope name exists in the user environment key, and no pinned machine-scope name exists in the
/// machine key; an incomplete reading refuses.</item>
/// </list>
/// This narrows but cannot close the race: a same-user process can still write the user key after this check and before
/// setup's environment refresh (design §2 R5).
/// </summary>
internal static class ChocolateySetupEnvironmentGuard
{
    internal static bool Permits(IReadOnlyDictionary<string, string> compiledEnvironment, string observedRoot, ChocolateySetupEnvironmentReading reading)
    {
        ArgumentNullException.ThrowIfNull(compiledEnvironment);
        ArgumentNullException.ThrowIfNull(observedRoot);
        ArgumentNullException.ThrowIfNull(reading);
        return CompiledEnvironmentIsClean(compiledEnvironment, observedRoot) &&
            reading.Complete && reading.UserNames.Count == 0 && reading.MachineNames.Count == 0;
    }

    /// <summary>The compiled block carries neither override name and names exactly the observed root.</summary>
    internal static bool CompiledEnvironmentIsClean(IReadOnlyDictionary<string, string> compiledEnvironment, string observedRoot)
    {
        ArgumentNullException.ThrowIfNull(compiledEnvironment);
        ArgumentNullException.ThrowIfNull(observedRoot);
        string? root = null;
        foreach ((string name, string value) in compiledEnvironment)
        {
            if (name.Equals(ChocolateySetupEnvironmentReader.AllowCurrentUserName, StringComparison.OrdinalIgnoreCase) ||
                name.Equals(ChocolateySetupEnvironmentReader.AllowInsecureRootName, StringComparison.OrdinalIgnoreCase))
                return false;
            if (name.Equals(ChocolateySetupEnvironmentReader.InstallRootName, StringComparison.OrdinalIgnoreCase))
            {
                if (root is not null) return false;
                root = value;
            }
        }
        return root is not null && string.Equals(root, observedRoot, StringComparison.Ordinal);
    }
}
