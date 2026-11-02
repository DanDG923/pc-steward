using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>
/// ADR-031 design §2.1 R5 runtime watchdog: why the client self-update job was terminated while it ran. <see cref="None"/>
/// means the watchdog never stopped the job. Every other value forces the postcheck to a not-verified verdict.
/// </summary>
internal enum ChocolateyClientWatchdogFault
{
    None,
    /// <summary>A setup override name pinned setup.psm1 honours appeared in the user or machine environment key.</summary>
    SetupOverrideDuringRun,
    /// <summary>The requester could modify the Chocolatey root, its parent or a listed release directory.</summary>
    RootWritableDuringRun,
    /// <summary>A watch could not be re-armed, a key could not be read, or a directory's access could not be evaluated (fail closed).</summary>
    WatchFailedDuringRun,
}

/// <summary>What woke the watchdog. Every wake re-checks both the environment names and the requester's access.</summary>
internal enum ChocolateyClientWatchWake
{
    /// <summary>The check immediately before <c>CreateProcessW</c>, with every watch already armed.</summary>
    Armed,
    /// <summary>A key or directory notification fired and was re-armed.</summary>
    Notification,
    /// <summary>The backstop poll interval elapsed without a notification.</summary>
    Poll,
    /// <summary>A notification fired but its watch could not be re-armed.</summary>
    RearmFailed,
}

/// <summary>
/// ADR-031 design §2.1 R5 (pure, deterministic): the watchdog's decision for one wake. Order: a visible override name
/// (<see cref="ChocolateyClientWatchdogFault.SetupOverrideDuringRun"/>), then any <see cref="RequesterModification.Granted"/>
/// verdict (<see cref="ChocolateyClientWatchdogFault.RootWritableDuringRun"/>), then anything that leaves the run
/// unwatched — a failed re-arm, an incomplete reading, no access verdict or an <see cref="RequesterModification.Unreadable"/>
/// one (<see cref="ChocolateyClientWatchdogFault.WatchFailedDuringRun"/>). Only a complete, clean reading with every
/// verdict <see cref="RequesterModification.Denied"/> continues.
/// </summary>
internal static class ChocolateyClientWatchdogRule
{
    internal static ChocolateyClientWatchdogFault Decide(ChocolateyClientWatchWake wake, ChocolateySetupEnvironmentReading environment,
        IReadOnlyList<RequesterModification> access)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(access);
        if (!Enum.IsDefined(wake)) return ChocolateyClientWatchdogFault.WatchFailedDuringRun;
        if (environment.UserNames.Count != 0 || environment.MachineNames.Count != 0) return ChocolateyClientWatchdogFault.SetupOverrideDuringRun;
        if (access.Contains(RequesterModification.Granted)) return ChocolateyClientWatchdogFault.RootWritableDuringRun;
        if (wake == ChocolateyClientWatchWake.RearmFailed || !environment.Complete || access.Count == 0 ||
            access.Any(verdict => verdict != RequesterModification.Denied))
            return ChocolateyClientWatchdogFault.WatchFailedDuringRun;
        return ChocolateyClientWatchdogFault.None;
    }
}

/// <summary>
/// ADR-031 design §2.1 R5: the watchdog's decision state, independent of how wakes are produced (portable; the native
/// <see cref="ChocolateyClientUpdateWatchdog"/> feeds it, tests feed it fakes). <see cref="Evaluate"/> only decides;
/// <see cref="Observe"/> decides, records the first fault (first wins) and asks the attached terminator to end the job,
/// repeating the request on later wakes until the terminator reports success. A probe that throws is a failed check.
/// </summary>
internal sealed class ChocolateyClientWatchMonitor
{
    private readonly Func<ChocolateySetupEnvironmentReading> readEnvironment;
    private readonly Func<IReadOnlyList<RequesterModification>> readAccess;
    private Func<bool>? terminate;
    private int fault;
    private int terminated;

    internal ChocolateyClientWatchMonitor(Func<ChocolateySetupEnvironmentReading> readEnvironment, Func<IReadOnlyList<RequesterModification>> readAccess)
    {
        ArgumentNullException.ThrowIfNull(readEnvironment);
        ArgumentNullException.ThrowIfNull(readAccess);
        this.readEnvironment = readEnvironment;
        this.readAccess = readAccess;
    }

    /// <summary>The first recorded fault, or <see cref="ChocolateyClientWatchdogFault.None"/>.</summary>
    internal ChocolateyClientWatchdogFault Fault => (ChocolateyClientWatchdogFault)Volatile.Read(ref fault);

    /// <summary>True once the terminator reported success after a fault.</summary>
    internal bool TerminationConfirmed => Volatile.Read(ref terminated) != 0;

    /// <summary>Attaches the job terminator (after the job holds the resumed process). Before that, nothing can be terminated.</summary>
    internal void Attach(Func<bool> terminator)
    {
        ArgumentNullException.ThrowIfNull(terminator);
        Volatile.Write(ref terminate, terminator);
    }

    /// <summary>Reads both probes and decides; records nothing.</summary>
    internal ChocolateyClientWatchdogFault Evaluate(ChocolateyClientWatchWake wake)
    {
        try
        {
            return ChocolateyClientWatchdogRule.Decide(wake, readEnvironment(), readAccess());
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return ChocolateyClientWatchdogFault.WatchFailedDuringRun;
        }
    }

    /// <summary>
    /// One wake while the job runs: once a fault is recorded, only the termination request is repeated (no further
    /// evaluation); otherwise decide, and on a fault record it and request termination.
    /// </summary>
    internal ChocolateyClientWatchdogFault Observe(ChocolateyClientWatchWake wake)
    {
        if (Fault == ChocolateyClientWatchdogFault.None)
        {
            ChocolateyClientWatchdogFault decision = Evaluate(wake);
            if (decision == ChocolateyClientWatchdogFault.None) return decision;
            Interlocked.CompareExchange(ref fault, (int)decision, (int)ChocolateyClientWatchdogFault.None);
        }
        RequestTermination();
        return Fault;
    }

    /// <summary>Records <see cref="ChocolateyClientWatchdogFault.WatchFailedDuringRun"/> (unless a fault exists) and requests termination.</summary>
    internal void Fail()
    {
        Interlocked.CompareExchange(ref fault, (int)ChocolateyClientWatchdogFault.WatchFailedDuringRun, (int)ChocolateyClientWatchdogFault.None);
        RequestTermination();
    }

    private void RequestTermination()
    {
        if (TerminationConfirmed || Volatile.Read(ref terminate) is not { } terminator) return;
        bool confirmed;
        try { confirmed = terminator(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { confirmed = false; }
        if (confirmed) Volatile.Write(ref terminated, 1);
    }
}

/// <summary>
/// ADR-031 design §2.1 R5: what the client self-update watchdog watches. Production targets come only from
/// <see cref="FromBinding"/> over a client self-update binding: its requester token (the one its observation evaluated),
/// its Chocolatey root, the root's parent and the top-level release directories of its held release files (<c>helpers</c>,
/// <c>tools</c>), with the fixed environment keys. Deeper release directories are not opened during the run: setup.psm1
/// deletes and recreates them, and they inherit from the watched ones. The test-only <see cref="CreateForTests"/> (banned
/// from every <c>src</c> caller) takes a fixture root and an environment-watch factory over test keys.
/// </summary>
internal sealed class ChocolateyClientWatchTargets
{
    private readonly Func<ChocolateySetupEnvironmentReader.Watch?> armEnvironment;

    private ChocolateyClientWatchTargets(RequesterToken requester, string root, IReadOnlyList<string> listedDirectories,
        Func<ChocolateySetupEnvironmentReader.Watch?> armEnvironment)
    {
        ArgumentNullException.ThrowIfNull(requester);
        ArgumentNullException.ThrowIfNull(listedDirectories);
        if (!ProtectedNodeReader.IsCanonicalLocalPath(root) || root.Length <= 3)
            throw new ArgumentException("A canonical root below a drive root is required.", nameof(root));
        string[] names = [.. listedDirectories];
        if (names.Length == 0 || names.Any(name => !ProtectedNodeReader.IsCanonicalName(name)) ||
            names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
            throw new ArgumentException("Distinct canonical directory names are required.", nameof(listedDirectories));
        Requester = requester;
        Root = root;
        int separator = root.LastIndexOf('\\');
        Parent = separator == 2 ? root[..3] : root[..separator];
        ListedDirectories = Array.AsReadOnly(names);
        this.armEnvironment = armEnvironment;
    }

    /// <summary>The token the requester's access is evaluated with.</summary>
    internal RequesterToken Requester { get; }

    /// <summary>The Chocolatey root (watched recursively, evaluated in Node mode).</summary>
    internal string Root { get; }

    /// <summary>The root's parent (watched non-recursively for the root's own security change, evaluated in Ancestor mode).</summary>
    internal string Parent { get; }

    /// <summary>Top-level release directory names below the root (evaluated in Node mode with the root as parent).</summary>
    internal IReadOnlyList<string> ListedDirectories { get; }

    /// <summary>Arms the environment-key watch (production: the two fixed keys).</summary>
    internal ChocolateySetupEnvironmentReader.Watch? ArmEnvironment() => armEnvironment();

    /// <summary>The client self-update binding's targets; throws for a package binding or a binding without a requester.</summary>
    internal static ChocolateyClientWatchTargets FromBinding(ChocolateyHeldClientBinding client)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (client.Purpose != ChocolateyBindingPurpose.ClientSelfUpdate || client.Requester is not { } requester)
            throw new InvalidOperationException("Only a client self-update binding is watched.");
        string[] directories = [.. client.ReleaseFiles.Select(file => file.RelativePath.Split('/'))
            .Where(parts => parts.Length > 1).Select(parts => parts[0]).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)];
        return new ChocolateyClientWatchTargets(requester, client.LaunchPaths.ChocolateyRoot, directories, ChocolateySetupEnvironmentReader.TryWatch);
    }

    /// <summary>Test-only (an architecture test bans every <c>src</c> caller): fixture root, directories and test-key watch factory.</summary>
    internal static ChocolateyClientWatchTargets CreateForTests(RequesterToken requester, string root, IReadOnlyList<string> listedDirectories,
        Func<ChocolateySetupEnvironmentReader.Watch?> armEnvironment)
    {
        ArgumentNullException.ThrowIfNull(armEnvironment);
        return new ChocolateyClientWatchTargets(requester, root, listedDirectories, armEnvironment);
    }
}

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; ADR-031 design §2.1 R5 runtime watchdog for the client self-update launch only (the
/// package route is unchanged). <see cref="TryArm"/> runs <b>before</b> the launcher's last pre-creation checks, so no
/// change can fall between the last check and the watch:
/// <list type="bullet">
/// <item>the two environment keys through <see cref="ChocolateySetupEnvironmentReader.Watch"/> (<c>KEY_NOTIFY</c> only);</item>
/// <item><c>ReadDirectoryChangesW</c> with <c>FILE_NOTIFY_CHANGE_SECURITY</c> on the root (recursive) and on its parent
/// (non-recursive, which reports the root's own descriptor). Each handle is opened <c>FILE_LIST_DIRECTORY |
/// FILE_READ_ATTRIBUTES</c> with <c>FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE</c>, <c>OPEN_EXISTING</c>,
/// backup semantics, no reparse traversal and overlapped I/O, so it never blocks setup's renames, deletes or copies; it
/// must be a non-reparse directory whose final path is the requested one.</item>
/// </list>
/// Access is re-evaluated through transient opens with <c>READ_CONTROL | FILE_READ_ATTRIBUTES</c> only (rights that take
/// no part in share checks), identity-pinned to the directories seen at arming, with <see cref="RequesterAccess.Evaluate"/>
/// and the job's requester token. After resume, <see cref="Start"/> runs a dedicated thread that wakes on any notification
/// (re-arming it first) or every <see cref="PollInterval"/> as a backstop, decides through <see cref="ChocolateyClientWatchMonitor"/>
/// and terminates the job on the first fault. Nothing here writes a key, a descriptor or a file. <see cref="Dispose"/>
/// stops and joins the thread before any handle closes; the launcher disposes the watchdog before the job handle.
/// </summary>
internal sealed class ChocolateyClientUpdateWatchdog : IDisposable
{
    /// <summary>The backstop poll interval for a missed notification.</summary>
    internal static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly ChocolateyClientWatchTargets targets;
    private readonly ChocolateySetupEnvironmentReader.Watch environment;
    private readonly DirectoryWatch rootWatch;
    private readonly DirectoryWatch parentWatch;
    private readonly NativeFileIdentity rootIdentity;
    private readonly NativeFileIdentity parentIdentity;
    private readonly NativeFileIdentity[] listedIdentities;
    private readonly ChocolateyClientWatchMonitor monitor;
    private readonly ManualResetEvent stop = new(false);
    private Thread? thread;
    private int disposed;

    private ChocolateyClientUpdateWatchdog(ChocolateyClientWatchTargets targets, ChocolateySetupEnvironmentReader.Watch environment,
        DirectoryWatch rootWatch, DirectoryWatch parentWatch, NativeFileIdentity[] listedIdentities)
    {
        this.targets = targets;
        this.environment = environment;
        this.rootWatch = rootWatch;
        this.parentWatch = parentWatch;
        rootIdentity = rootWatch.Identity;
        parentIdentity = parentWatch.Identity;
        this.listedIdentities = listedIdentities;
        monitor = new ChocolateyClientWatchMonitor(environment.Read, ReadAccess);
    }

    /// <summary>The first fault the running watchdog recorded (the launcher reports it after disposal).</summary>
    internal ChocolateyClientWatchdogFault Fault => monitor.Fault;

    /// <summary>
    /// Arms every watch. Null when any key, the root, its parent or a listed directory cannot be watched or identified; the
    /// launcher then creates no process (NotStarted).
    /// </summary>
    internal static ChocolateyClientUpdateWatchdog? TryArm(ChocolateyClientWatchTargets targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (!OperatingSystem.IsWindows()) return null;
        ChocolateySetupEnvironmentReader.Watch? environment = null;
        DirectoryWatch? rootWatch = null, parentWatch = null;
        try
        {
            environment = targets.ArmEnvironment();
            if (environment is null) return null;
            rootWatch = DirectoryWatch.TryArm(targets.Root, subtree: true);
            if (rootWatch is null) return null;
            parentWatch = DirectoryWatch.TryArm(targets.Parent, subtree: false);
            if (parentWatch is null) return null;
            NativeFileIdentity[] listed = new NativeFileIdentity[targets.ListedDirectories.Count];
            for (int index = 0; index < listed.Length; index++)
            {
                using SafeFileHandle? directory = OpenForSecurity(Child(targets.Root, targets.ListedDirectories[index]), expected: null,
                    out NativeFileIdentity identity);
                if (directory is null) return null;
                listed[index] = identity;
            }
            ChocolateyClientUpdateWatchdog watchdog = new(targets, environment, rootWatch, parentWatch, listed);
            environment = null;
            rootWatch = parentWatch = null;
            return watchdog;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
        finally
        {
            parentWatch?.Dispose();
            rootWatch?.Dispose();
            environment?.Dispose();
        }
    }

    /// <summary>The check immediately before <c>CreateProcessW</c>, with every watch armed: true only when nothing is wrong.</summary>
    internal bool PermitsCreation() => monitor.Evaluate(ChocolateyClientWatchWake.Armed) == ChocolateyClientWatchdogFault.None;

    /// <summary>
    /// Called once the job holds the resumed process: attaches <paramref name="terminateJob"/> (TerminateJobObject on the
    /// launch job; true when the call succeeded) and starts the watch thread. Notifications that fired since arming are
    /// still signalled and are handled on the first wake; the first wake also checks immediately.
    /// </summary>
    internal void Start(Func<bool> terminateJob)
    {
        ArgumentNullException.ThrowIfNull(terminateJob);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (thread is not null) throw new InvalidOperationException("The watchdog is already running.");
        monitor.Attach(terminateJob);
        try
        {
            Thread watch = new(RunWatch) { IsBackground = true, Name = "PCSteward client update watchdog" };
            watch.Start();
            thread = watch;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // An unwatched run is not permitted: record the failure and terminate the job now.
            monitor.Fail();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stop.Set();
        thread?.Join();
        parentWatch.Dispose();
        rootWatch.Dispose();
        environment.Dispose();
        stop.Dispose();
    }

    private void RunWatch()
    {
        try
        {
            WaitHandle[] handles = [stop, environment.Changed[0], environment.Changed[1], rootWatch.Completed, parentWatch.Completed];
            ChocolateyClientWatchWake wake = ChocolateyClientWatchWake.Poll;
            while (true)
            {
                if (monitor.Observe(wake) != ChocolateyClientWatchdogFault.None)
                {
                    // Faulted: nothing is evaluated any more; only the termination request repeats until it succeeds.
                    while (!monitor.TerminationConfirmed)
                    {
                        if (stop.WaitOne(PollInterval)) return;
                        monitor.Observe(ChocolateyClientWatchWake.Poll);
                    }
                    return;
                }
                int index = WaitHandle.WaitAny(handles, PollInterval);
                if (index == 0) return;
                bool rearmed = index switch
                {
                    WaitHandle.WaitTimeout => true,
                    1 => environment.TryRearm(0),
                    2 => environment.TryRearm(1),
                    3 => rootWatch.TryRearm(),
                    4 => parentWatch.TryRearm(),
                    _ => false,
                };
                wake = index == WaitHandle.WaitTimeout ? ChocolateyClientWatchWake.Poll
                    : rearmed ? ChocolateyClientWatchWake.Notification : ChocolateyClientWatchWake.RearmFailed;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            monitor.Fail();
        }
    }

    /// <summary>Parent (Ancestor mode), root (Node mode), then each listed directory (Node mode with the root as parent).</summary>
    private RequesterModification[] ReadAccess()
    {
        RequesterToken token = targets.Requester;
        RequesterModification[] verdicts = new RequesterModification[2 + listedIdentities.Length];
        using (SafeFileHandle? parent = OpenForSecurity(targets.Parent, parentIdentity, out _))
            verdicts[0] = parent is null ? RequesterModification.Unreadable
                : RequesterAccess.Evaluate(token, parent, directory: true, parentDirectory: null, AccessMode.Ancestor);
        using SafeFileHandle? root = OpenForSecurity(targets.Root, rootIdentity, out _);
        verdicts[1] = root is null ? RequesterModification.Unreadable
            : RequesterAccess.Evaluate(token, root, directory: true, parentDirectory: null, AccessMode.Node);
        for (int index = 0; index < listedIdentities.Length; index++)
        {
            using SafeFileHandle? listed = root is null ? null
                : OpenForSecurity(Child(targets.Root, targets.ListedDirectories[index]), listedIdentities[index], out _);
            verdicts[2 + index] = listed is null ? RequesterModification.Unreadable
                : RequesterAccess.Evaluate(token, listed, directory: true, root, AccessMode.Node);
        }
        return verdicts;
    }

    private static string Child(string root, string name) => root + '\\' + name;

    /// <summary>
    /// A transient <c>READ_CONTROL | FILE_READ_ATTRIBUTES</c> open with full sharing and no reparse traversal: a non-reparse
    /// directory whose final path is <paramref name="path"/> and, when given, whose identity is <paramref name="expected"/>.
    /// </summary>
    private static SafeFileHandle? OpenForSecurity(string path, NativeFileIdentity? expected, out NativeFileIdentity identity)
    {
        identity = default;
        SafeFileHandle handle = WatchdogNativeMethods.CreateFile(path, WatchdogNativeMethods.SecurityReadAccess,
            WatchdogNativeMethods.FileShareAll, IntPtr.Zero, WatchdogNativeMethods.OpenExisting,
            WatchdogNativeMethods.FileFlagBackupSemantics | WatchdogNativeMethods.FileFlagOpenReparsePoint, IntPtr.Zero);
        if (!IsDirectory(handle, path, out identity) || (expected is { } pinned && pinned != identity))
        {
            handle.Dispose();
            return null;
        }
        return handle;
    }

    private static bool IsDirectory(SafeFileHandle handle, string path, out NativeFileIdentity identity)
    {
        identity = default;
        return !handle.IsInvalid && ProtectedNodeReader.TryReadKind(handle, out bool directory, out bool reparse) && directory && !reparse &&
            ProtectedNodeReader.TryGetFinalPath(handle, out string final) && string.Equals(final, path, StringComparison.OrdinalIgnoreCase) &&
            ProtectedNodeReader.TryReadIdentity(handle, out identity);
    }

    /// <summary>
    /// One overlapped <c>ReadDirectoryChangesW(FILE_NOTIFY_CHANGE_SECURITY)</c> on a full-share directory handle. The
    /// notification contents are never parsed (any completion, including a buffer overflow, only wakes the watchdog). The
    /// OVERLAPPED block and buffer are unmanaged and freed only after the pending read was cancelled and has completed.
    /// </summary>
    private sealed class DirectoryWatch : IDisposable
    {
        private const int BufferBytes = 4096;
        private const int ErrorIoIncomplete = 996;
        private const int ErrorNotifyEnumDir = 1022;
        private readonly SafeFileHandle handle;
        private readonly bool subtree;
        private readonly ManualResetEvent completed = new(false);
        private readonly IntPtr overlapped;
        private readonly IntPtr buffer;
        private bool pending;
        private int disposed;

        private DirectoryWatch(SafeFileHandle handle, bool subtree, NativeFileIdentity identity)
        {
            this.handle = handle;
            this.subtree = subtree;
            Identity = identity;
            overlapped = Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlapped>());
            buffer = Marshal.AllocHGlobal(BufferBytes);
        }

        internal NativeFileIdentity Identity { get; }

        internal WaitHandle Completed => completed;

        internal static DirectoryWatch? TryArm(string path, bool subtree)
        {
            SafeFileHandle handle = WatchdogNativeMethods.CreateFile(path, WatchdogNativeMethods.WatchOpenAccess,
                WatchdogNativeMethods.FileShareAll, IntPtr.Zero, WatchdogNativeMethods.OpenExisting,
                WatchdogNativeMethods.FileFlagBackupSemantics | WatchdogNativeMethods.FileFlagOpenReparsePoint | WatchdogNativeMethods.FileFlagOverlapped,
                IntPtr.Zero);
            if (!IsDirectory(handle, path, out NativeFileIdentity identity))
            {
                handle.Dispose();
                return null;
            }
            DirectoryWatch watch = new(handle, subtree, identity);
            if (watch.Issue()) return watch;
            watch.Dispose();
            return null;
        }

        /// <summary>Collects the completed read and issues the next one; false when the watch cannot continue.</summary>
        internal bool TryRearm()
        {
            if (Volatile.Read(ref disposed) != 0) return false;
            if (pending)
            {
                if (!WatchdogNativeMethods.GetOverlappedResult(handle, overlapped, out _, false))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == ErrorIoIncomplete) return true;
                    pending = false;
                    if (error != ErrorNotifyEnumDir) return false;
                }
                else
                {
                    pending = false;
                }
            }
            return Issue();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            if (pending)
            {
                _ = WatchdogNativeMethods.CancelIoEx(handle, overlapped);
                // Waits for the cancelled (or already completed) read, so the kernel no longer references the memory.
                _ = WatchdogNativeMethods.GetOverlappedResult(handle, overlapped, out _, true);
                pending = false;
            }
            handle.Dispose();
            Marshal.FreeHGlobal(buffer);
            Marshal.FreeHGlobal(overlapped);
            completed.Dispose();
        }

        private bool Issue()
        {
            completed.Reset();
            Marshal.StructureToPtr(new NativeOverlapped { EventHandle = completed.SafeWaitHandle.DangerousGetHandle() }, overlapped, false);
            if (!WatchdogNativeMethods.ReadDirectoryChanges(handle, buffer, BufferBytes, subtree, WatchdogNativeMethods.FileNotifyChangeSecurity,
                    IntPtr.Zero, overlapped, IntPtr.Zero))
                return false;
            pending = true;
            return true;
        }
    }

    /// <summary>P/Invoke surface of the watchdog only: read-only directory opens and change notifications; no write right is declared.</summary>
    private static class WatchdogNativeMethods
    {
        internal const uint FileListDirectory = 0x00000001;
        internal const uint FileReadAttributes = 0x00000080;
        internal const uint ReadControl = 0x00020000;
        internal const uint WatchOpenAccess = FileListDirectory | FileReadAttributes;
        internal const uint SecurityReadAccess = ReadControl | FileReadAttributes;
        /// <summary>FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE: the watchdog never blocks another opener.</summary>
        internal const uint FileShareAll = 0x00000007;
        internal const uint OpenExisting = 3;
        internal const uint FileFlagBackupSemantics = 0x02000000;
        internal const uint FileFlagOpenReparsePoint = 0x00200000;
        internal const uint FileFlagOverlapped = 0x40000000;
        internal const uint FileNotifyChangeSecurity = 0x00000100;

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
            uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", EntryPoint = "ReadDirectoryChangesW", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ReadDirectoryChanges(SafeFileHandle directory, IntPtr buffer, uint bufferLength,
            [MarshalAs(UnmanagedType.Bool)] bool watchSubtree, uint notifyFilter, IntPtr bytesReturned, IntPtr overlapped, IntPtr completionRoutine);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetOverlappedResult(SafeFileHandle file, IntPtr overlapped, out uint bytesTransferred,
            [MarshalAs(UnmanagedType.Bool)] bool wait);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CancelIoEx(SafeFileHandle file, IntPtr overlapped);
    }
}
