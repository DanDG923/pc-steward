using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>
/// What <see cref="ChocolateySetupEnvironmentReader.Read"/> found: which pinned names exist in the user and machine
/// environment keys. <see cref="Complete"/> is false when any key or value could not be read; the guard then refuses.
/// Plain values; no authority.
/// </summary>
internal sealed class ChocolateySetupEnvironmentReading
{
    private ChocolateySetupEnvironmentReading(bool complete, IReadOnlyList<string> user, IReadOnlyList<string> machine)
    {
        Complete = complete;
        UserNames = new ReadOnlyCollection<string>([.. user]);
        MachineNames = new ReadOnlyCollection<string>([.. machine]);
    }

    internal bool Complete { get; }

    /// <summary>Pinned names present in <c>HKEY_CURRENT_USER\Environment</c>.</summary>
    internal IReadOnlyList<string> UserNames { get; }

    /// <summary>Pinned names present in the machine environment key.</summary>
    internal IReadOnlyList<string> MachineNames { get; }

    internal static ChocolateySetupEnvironmentReading Unreadable { get; } = new(false, [], []);

    internal static ChocolateySetupEnvironmentReading Of(IReadOnlyList<string> user, IReadOnlyList<string> machine)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(machine);
        return new(true, user, machine);
    }
}

/// <summary>
/// FR-UPD-15, TM-03/04/13; ADR-031 design mapping §2 R5: the single, pinned, <b>read-only</b> exception to the Native
/// layer's ban on environment and registry access (an architecture test names exactly this file, these keys and these value
/// names). Pinned <c>chocolateysetup.psm1</c> (blob <c>c66c1282</c>) refreshes its process environment from the machine
/// key and then <c>HKCU\Environment</c> (via <c>Set-ChocolateyInstallFolder</c> → <c>Install-ChocolateyEnvironmentVariable</c>
/// → <c>EnvironmentHelper.UpdateSession</c>) before <c>Ensure-Permissions</c> reads <c>ChocolateyInstallAllowCurrentUser</c>,
/// so a same-user value written without elevation reaches the elevated setup. This reader reports only whether those
/// pinned names <b>exist</b>; it never reads their data into anything, never expands, never enumerates, and never writes:
/// the keys are opened with <c>KEY_QUERY_VALUE</c> only. It reads the hives of the current (helper) process, which are
/// the hives the child setup refreshes from. Any failure other than "not found" makes the reading incomplete.
/// </summary>
internal static class ChocolateySetupEnvironmentReader
{
    /// <summary>setup.psm1 line 321: grants the setup account Modify on the root.</summary>
    internal const string AllowCurrentUserName = "ChocolateyInstallAllowCurrentUser";

    /// <summary>setup.psm1 line 137: admits the <c>C:\Chocolatey</c> root, for which <c>Ensure-Permissions</c> sets no ACL.</summary>
    internal const string AllowInsecureRootName = "ChocolateyAllowInsecureRootDirectory";

    /// <summary>setup.psm1 line 140: selects the root; any non-default root skips <c>Ensure-Permissions</c> entirely.</summary>
    internal const string InstallRootName = "ChocolateyInstall";

    internal const string UserKeyPath = "Environment";
    internal const string MachineKeyPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";

    /// <summary>Checked in the user key: a same-user process can write each of them without elevation.</summary>
    internal static IReadOnlyList<string> UserScopeNames { get; } = Array.AsReadOnly([AllowCurrentUserName, AllowInsecureRootName, InstallRootName]);

    /// <summary>Checked in the machine key (administrator-writable; refused anyway, fail closed). <c>ChocolateyInstall</c> is set there normally.</summary>
    internal static IReadOnlyList<string> MachineScopeNames { get; } = Array.AsReadOnly([AllowCurrentUserName, AllowInsecureRootName]);

    private const int ErrorSuccess = 0;
    private const int ErrorFileNotFound = 2;
    private const int ErrorMoreData = 234;
    private const uint KeyQueryValue = 0x0001;
    private const uint KeyWow64View64 = 0x0100;
    private const uint ReadOnlyKeyAccess = KeyQueryValue | KeyWow64View64;
    private static readonly IntPtr CurrentUserHive = new(unchecked((int)0x80000001));
    private static readonly IntPtr LocalMachineHive = new(unchecked((int)0x80000002));

    /// <summary>Reads presence of the pinned names; never throws (an unexpected failure is an incomplete reading).</summary>
    internal static ChocolateySetupEnvironmentReading Read()
    {
        if (!OperatingSystem.IsWindows()) return ChocolateySetupEnvironmentReading.Unreadable;
        try
        {
            List<string> user = [], machine = [];
            if (!Probe(CurrentUserHive, UserKeyPath, UserScopeNames, user) || !Probe(LocalMachineHive, MachineKeyPath, MachineScopeNames, machine))
                return ChocolateySetupEnvironmentReading.Unreadable;
            return ChocolateySetupEnvironmentReading.Of(user, machine);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return ChocolateySetupEnvironmentReading.Unreadable;
        }
    }

    private static bool Probe(IntPtr hive, string path, IReadOnlyList<string> names, List<string> present)
    {
        int opened = RegOpenKeyEx(hive, path, 0, ReadOnlyKeyAccess, out SafeRegistryHandle key);
        using (key)
        {
            // A missing environment key (for example on Server Core without HKCU\Environment) holds no value.
            if (opened == ErrorFileNotFound) return true;
            if (opened != ErrorSuccess || key.IsInvalid) return false;
            foreach (string name in names)
            {
                uint size = 0;
                int queried = RegQueryValueEx(key, name, IntPtr.Zero, out _, IntPtr.Zero, ref size);
                if (queried is ErrorSuccess or ErrorMoreData) present.Add(name);
                else if (queried != ErrorFileNotFound) return false;
            }
            return true;
        }
    }

    [DllImport("advapi32.dll", EntryPoint = "RegOpenKeyExW", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int RegOpenKeyEx(IntPtr hive, string subKey, uint options, uint desiredAccess, out SafeRegistryHandle key);

    [DllImport("advapi32.dll", EntryPoint = "RegQueryValueExW", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int RegQueryValueEx(SafeRegistryHandle key, string valueName, IntPtr reserved, out uint type, IntPtr data, ref uint dataSize);
}
