using System.Collections;
using Microsoft.Win32;

namespace PCSteward.Execution;

/// <summary>
/// TM-03/04/13, FR-UPD-15; Chocolatey update security review finding H1
/// (<c>docs/review/TASK_50_CHOCOLATEY_UPDATE_SECURITY_REVIEW.md</c>): the elevated helper is a framework-dependent .NET
/// program, and an elevated process started through UAC gets an environment built from the user's own registry
/// (<c>HKCU\Environment</c> and <c>HKCU\Volatile Environment</c>), which any program running as the user can write without
/// elevation. The .NET host and runtime act on many of those names before <c>Main</c> runs (for example a profiler DLL path,
/// a startup hook, a diagnostics port or an event-trace output path), so a same-user value could run code or write files
/// with administrator rights inside the helper, before any PC Steward check.
/// <list type="bullet">
/// <item><see cref="PermitsElevatedLaunch"/> runs in the desktop immediately before each <c>runas</c> launch: a runtime
/// control name in the desktop's own environment or either user key refuses the launch, so no UAC prompt is shown.</item>
/// <item><see cref="ProcessEnvironmentIsClean"/> runs first in the helper's entry point: the same names in the helper's own
/// environment end it with the refusal exit code before it connects or reads a request.</item>
/// </list>
/// Both read value <b>names</b> only, never values, and never write. ADR-033 closes the race this guard cannot (a value
/// written after the desktop's check and before Windows builds the elevated environment): the helper is published as a Native
/// AOT image, which no such name can load code into, so a late name reaches only the helper's own check below. The guard stays
/// as a second layer, and because Chocolatey's .NET Framework children can still read such names (review §M1).
/// </summary>
internal static class ElevatedRuntimeEnvironmentGuard
{
    /// <summary>Exit code of a helper that refused its own environment (the entry point's existing pre-connection refusal).</summary>
    internal const int RefusedExitCode = 20;

    internal const string UserKeyPath = "Environment";
    internal const string VolatileKeyPath = "Volatile Environment";

    /// <summary>
    /// Prefixes the .NET host and runtime (and, for Chocolatey's .NET Framework children, the CLR 4 runtime) read as control
    /// settings. Compared ignoring case, as Windows environment names are.
    /// </summary>
    internal static IReadOnlyList<string> RuntimeControlPrefixes { get; } =
        Array.AsReadOnly(["DOTNET_", "COMPlus_", "CORECLR_", "COR_", "APPDOMAIN_MANAGER_"]);

    /// <summary>
    /// Names with a runtime prefix that only the .NET SDK command line reads (never the host or runtime of a published app).
    /// Exact names only, so a new runtime setting is never admitted by accident.
    /// </summary>
    internal static IReadOnlyList<string> SdkOnlyNames { get; } = Array.AsReadOnly(["DOTNET_CLI_TELEMETRY_OPTOUT", "DOTNET_NOLOGO"]);

    /// <summary>True when <paramref name="name"/> is a runtime control name the guard refuses.</summary>
    internal static bool IsRuntimeControlName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        string trimmed = name.Trim();
        if (SdkOnlyNames.Contains(trimmed, StringComparer.OrdinalIgnoreCase)) return false;
        return RuntimeControlPrefixes.Any(prefix => trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The pure decision: every source must have been read (null means unreadable, which refuses) and none may hold a runtime
    /// control name.
    /// </summary>
    internal static bool Permits(IEnumerable<IEnumerable<string>?> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        foreach (IEnumerable<string>? names in sources)
        {
            if (names is null) return false;
            if (names.Any(IsRuntimeControlName)) return false;
        }
        return true;
    }

    /// <summary>
    /// The desktop's check immediately before a <c>runas</c> launch: its own environment, <c>HKCU\Environment</c> and
    /// <c>HKCU\Volatile Environment</c> (with its per-session subkeys). Any read failure refuses. Never throws.
    /// </summary>
    internal static bool PermitsElevatedLaunch()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            return Permits([ProcessNames(), UserValueNames(UserKeyPath, includeSubkeys: false), UserValueNames(VolatileKeyPath, includeSubkeys: true)]);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }

    /// <summary>The helper's own check, first in its entry point: its process environment holds no runtime control name. Never throws.</summary>
    internal static bool ProcessEnvironmentIsClean()
    {
        try { return Permits([ProcessNames()]); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
    }

    private static List<string>? ProcessNames()
    {
        IDictionary variables = Environment.GetEnvironmentVariables();
        List<string> names = new(variables.Count);
        foreach (object? key in variables.Keys)
        {
            if (key is not string name) return null;
            names.Add(name);
        }
        return names;
    }

    /// <summary>
    /// Value names of a key below <c>HKEY_CURRENT_USER</c> (64-bit view), opened read-only; a missing key holds no names. With
    /// <paramref name="includeSubkeys"/> the value names of each direct subkey are added (the per-session volatile keys).
    /// </summary>
    private static List<string>? UserValueNames(string path, bool includeSubkeys)
    {
        if (!OperatingSystem.IsWindows()) return null;
        using RegistryKey hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using RegistryKey? key = hive.OpenSubKey(path, writable: false);
        List<string> names = [];
        if (key is null) return names;
        names.AddRange(key.GetValueNames());
        if (!includeSubkeys) return names;
        foreach (string child in key.GetSubKeyNames())
        {
            using RegistryKey? sub = key.OpenSubKey(child, writable: false);
            if (sub is null) return null;
            names.AddRange(sub.GetValueNames());
        }
        return names;
    }
}
