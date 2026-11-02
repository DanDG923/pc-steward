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
/// <para>
/// ADR-031 design §2.1 R5 runtime watchdog: <see cref="TryWatch"/> additionally arms change notifications on exactly the
/// same two keys (<c>RegNotifyChangeKeyValue</c>, <c>REG_NOTIFY_CHANGE_NAME | REG_NOTIFY_CHANGE_LAST_SET</c>,
/// asynchronous and event-based) through handles opened with <c>KEY_NOTIFY</c> only, so the client self-update launcher
/// can re-read the pinned names whenever either key changes. Still read-only: no write, create, delete or enumerate
/// capability exists. The watched key paths are fixed; only the architecture-banned <see cref="TryWatchForTests"/> may
/// substitute two test keys below <c>HKEY_CURRENT_USER\Software\PCSteward.Tests\</c>, so CI never writes the real
/// environment keys.
/// </para>
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
    private const uint KeyNotify = 0x0010;
    private const uint NotifyKeyAccess = KeyNotify | KeyWow64View64;
    private const uint NotifyChangeName = 0x00000001;
    private const uint NotifyChangeLastSet = 0x00000004;
    /// <summary>REG_NOTIFY_THREAD_AGNOSTIC: the registration does not end with the arming thread (it is armed on the launcher thread and re-armed on the watchdog thread).</summary>
    private const uint NotifyThreadAgnostic = 0x10000000;
    private const uint NotifyFilter = NotifyChangeName | NotifyChangeLastSet | NotifyThreadAgnostic;
    private const string TestKeyPrefix = @"Software\PCSteward.Tests\";
    private static readonly IntPtr CurrentUserHive = new(unchecked((int)0x80000001));
    private static readonly IntPtr LocalMachineHive = new(unchecked((int)0x80000002));

    /// <summary>Reads presence of the pinned names; never throws (an unexpected failure is an incomplete reading).</summary>
    internal static ChocolateySetupEnvironmentReading Read() => ReadKeys(CurrentUserHive, UserKeyPath, LocalMachineHive, MachineKeyPath);

    /// <summary>
    /// R5 watchdog: arms change notifications on the two environment keys (<c>KEY_NOTIFY</c> handles only). Null when either
    /// key cannot be opened or armed; the launcher then creates no process (fail closed).
    /// </summary>
    internal static Watch? TryWatch() => ArmKeys(CurrentUserHive, UserKeyPath, LocalMachineHive, MachineKeyPath);

    /// <summary>
    /// Test-only (an architecture test bans every <c>src</c> caller): the same watch over two existing test keys, both below
    /// <c>HKEY_CURRENT_USER\Software\PCSteward.Tests\</c>, standing in for the user and the machine key.
    /// </summary>
    internal static Watch? TryWatchForTests(string userTestKey, string machineTestKey)
    {
        ArgumentNullException.ThrowIfNull(userTestKey);
        ArgumentNullException.ThrowIfNull(machineTestKey);
        if (!userTestKey.StartsWith(TestKeyPrefix, StringComparison.Ordinal) || !machineTestKey.StartsWith(TestKeyPrefix, StringComparison.Ordinal))
            throw new ArgumentException("Test keys must be below the PC Steward test key.", nameof(userTestKey));
        return ArmKeys(CurrentUserHive, userTestKey, CurrentUserHive, machineTestKey);
    }

    private static ChocolateySetupEnvironmentReading ReadKeys(IntPtr userHive, string userPath, IntPtr machineHive, string machinePath)
    {
        if (!OperatingSystem.IsWindows()) return ChocolateySetupEnvironmentReading.Unreadable;
        try
        {
            List<string> user = [], machine = [];
            if (!Probe(userHive, userPath, UserScopeNames, user) || !Probe(machineHive, machinePath, MachineScopeNames, machine))
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

    private static Watch? ArmKeys(IntPtr userHive, string userPath, IntPtr machineHive, string machinePath)
    {
        if (!OperatingSystem.IsWindows()) return null;
        Watch? watch = new(userHive, userPath, machineHive, machinePath);
        try
        {
            if (watch.TryOpen(0, userHive, userPath) && watch.TryOpen(1, machineHive, machinePath) && watch.TryRearm(0) && watch.TryRearm(1))
            {
                Watch armed = watch;
                watch = null;
                return armed;
            }
            return null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
        finally
        {
            watch?.Dispose();
        }
    }

    /// <summary>
    /// R5 watchdog: change notifications on the user key (index 0) and the machine key (index 1), each through a
    /// <c>KEY_NOTIFY</c>-only handle and its own auto-reset event. A signalled event is re-armed with <see cref="TryRearm"/>
    /// before the names are re-read, so a change between the read and the re-arm cannot be missed. Disposal closes the key
    /// handles first (which ends both registrations), then the events.
    /// </summary>
    internal sealed class Watch : IDisposable
    {
        private readonly SafeRegistryHandle?[] keys = new SafeRegistryHandle?[2];
        private readonly AutoResetEvent[] changed = [new(false), new(false)];
        private readonly IntPtr userHive;
        private readonly string userPath;
        private readonly IntPtr machineHive;
        private readonly string machinePath;
        private int disposed;

        internal Watch(IntPtr userHive, string userPath, IntPtr machineHive, string machinePath)
        {
            this.userHive = userHive;
            this.userPath = userPath;
            this.machineHive = machineHive;
            this.machinePath = machinePath;
        }

        /// <summary>The two change events (user, machine); the watchdog waits on them.</summary>
        internal IReadOnlyList<WaitHandle> Changed => changed;

        /// <summary>Re-reads the pinned names from exactly the watched keys (never throws; a failure is an incomplete reading).</summary>
        internal ChocolateySetupEnvironmentReading Read() => ReadKeys(userHive, userPath, machineHive, machinePath);

        internal bool TryOpen(int index, IntPtr hive, string path)
        {
            int opened = RegOpenKeyEx(hive, path, 0, NotifyKeyAccess, out SafeRegistryHandle key);
            if (opened != ErrorSuccess || key.IsInvalid)
            {
                key.Dispose();
                return false;
            }
            keys[index] = key;
            return true;
        }

        /// <summary>(Re-)arms the asynchronous notification of key <paramref name="index"/>; false when it cannot be armed.</summary>
        internal bool TryRearm(int index)
        {
            if (Volatile.Read(ref disposed) != 0 || keys[index] is not { IsInvalid: false, IsClosed: false } key) return false;
            try
            {
                return RegNotifyChangeKeyValue(key, false, NotifyFilter, changed[index].SafeWaitHandle, true) == ErrorSuccess;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            foreach (SafeRegistryHandle? key in keys) key?.Dispose();
            foreach (AutoResetEvent signal in changed) signal.Dispose();
        }
    }

    [DllImport("advapi32.dll", EntryPoint = "RegOpenKeyExW", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int RegOpenKeyEx(IntPtr hive, string subKey, uint options, uint desiredAccess, out SafeRegistryHandle key);

    [DllImport("advapi32.dll", EntryPoint = "RegQueryValueExW", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int RegQueryValueEx(SafeRegistryHandle key, string valueName, IntPtr reserved, out uint type, IntPtr data, ref uint dataSize);

    [DllImport("advapi32.dll", EntryPoint = "RegNotifyChangeKeyValue")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int RegNotifyChangeKeyValue(SafeRegistryHandle key, [MarshalAs(UnmanagedType.Bool)] bool watchSubtree, uint notifyFilter,
        SafeWaitHandle notifyEvent, [MarshalAs(UnmanagedType.Bool)] bool asynchronous);
}
