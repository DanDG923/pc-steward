using System.ComponentModel;
using System.Diagnostics;

namespace PCSteward.Application;

/// <summary>
/// What "Close it for me" did, per process name: the apps that closed, the apps still open (they may be asking to save, or have
/// another window), and the apps with no window to close (for example one that runs from the notification area).
/// </summary>
public sealed record RunningApplicationCloseResult(IReadOnlyList<string> Closed, IReadOnlyList<string> StillOpen, IReadOnlyList<string> NoWindow)
{
    public bool AllClosed => StillOpen.Count == 0 && NoWindow.Count == 0;
}

/// <summary>
/// FR-UPD-06 (owner decision 2026-11-13, "Also offer 'Close it for me'"): asks running apps to close, only when the owner chooses it.
/// It sends the same request as closing the app's window, so the app can ask to save or refuse. It never force-quits a process.
/// </summary>
public interface IRunningApplicationCloser
{
    Task<RunningApplicationCloseResult> RequestCloseAsync(IReadOnlyCollection<string> processNames, CancellationToken cancellationToken);
}

/// <summary>One process the closer may ask to close: its id, whether it has a main window, and the two operations it may use.</summary>
internal interface IClosableProcesses
{
    /// <summary>The processes with exactly this name in the current user's Windows session.</summary>
    IReadOnlyList<int> InCurrentSession(string processName);
    /// <summary>Sends the window-close request to the process's main window. False when it has no main window or can't be reached.</summary>
    bool RequestCloseMainWindow(int processId);
    /// <summary>True when the process has exited (or can no longer be found).</summary>
    bool HasExited(int processId);
}

/// <summary>
/// Asks each named process in the current session to close its main window once, then waits up to <see cref="WaitLimit"/> for it to
/// exit. Names must be the package-derived process names the running-app detection reported. A process that doesn't exit is left
/// running and reported; nothing is ever terminated.
/// </summary>
public sealed class WindowCloseRunningApplicationCloser : IRunningApplicationCloser
{
    public static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private const int MaximumNames = 32;
    private readonly IClosableProcesses processes;
    private readonly TimeSpan waitLimit;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;

    public WindowCloseRunningApplicationCloser() : this(new SessionProcesses(), WaitLimit, Task.Delay) { }

    internal WindowCloseRunningApplicationCloser(IClosableProcesses processes, TimeSpan waitLimit, Func<TimeSpan, CancellationToken, Task> delay)
    {
        this.processes = processes ?? throw new ArgumentNullException(nameof(processes));
        this.waitLimit = waitLimit;
        this.delay = delay ?? throw new ArgumentNullException(nameof(delay));
    }

    public async Task<RunningApplicationCloseResult> RequestCloseAsync(IReadOnlyCollection<string> processNames, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(processNames);
        string[] names = [.. processNames.Where(RunningApplicationNames.IsValid).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaximumNames)];
        Dictionary<string, int[]> found = [];
        Dictionary<string, bool> asked = new(StringComparer.OrdinalIgnoreCase);
        foreach (string name in names)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int[] ids = [.. processes.InCurrentSession(name)];
            found[name] = ids;
            bool any = false;
            foreach (int id in ids) any |= processes.RequestCloseMainWindow(id);
            asked[name] = any;
        }

        DateTime deadline = DateTime.UtcNow + waitLimit;
        while (found.Values.Any(ids => ids.Any(id => !processes.HasExited(id))) && DateTime.UtcNow < deadline)
            await delay(PollInterval, cancellationToken).ConfigureAwait(false);

        List<string> closed = [], stillOpen = [], noWindow = [];
        foreach (string name in names)
        {
            if (found[name].All(processes.HasExited)) closed.Add(name);
            else if (!asked[name]) noWindow.Add(name);
            else stillOpen.Add(name);
        }
        return new(closed, stillOpen, noWindow);
    }

    /// <summary>The real processes, read with <see cref="Process"/>. Only processes in this app's own Windows session are touched.</summary>
    private sealed class SessionProcesses : IClosableProcesses
    {
        private readonly int session = CurrentSession();

        public IReadOnlyList<int> InCurrentSession(string processName)
        {
            Process[] all;
            try { all = Process.GetProcessesByName(processName); }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException) { return []; }
            List<int> ids = [];
            foreach (Process process in all)
            {
                using (process)
                {
                    try { if (session >= 0 && process.SessionId == session) ids.Add(process.Id); }
                    catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException) { }
                }
            }
            return ids;
        }

        public bool RequestCloseMainWindow(int processId)
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                if (process.SessionId != session || process.MainWindowHandle == IntPtr.Zero) return false;
                return process.CloseMainWindow();
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException) { return false; }
        }

        public bool HasExited(int processId)
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                return process.HasExited;
            }
            catch (ArgumentException) { return true; }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException) { return false; }
        }

        private static int CurrentSession()
        {
            try { using Process current = Process.GetCurrentProcess(); return current.SessionId; }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException) { return -1; }
        }
    }
}

/// <summary>The process-name rule shared by running-app detection and "Close it for me".</summary>
internal static class RunningApplicationNames
{
    public const int MaximumLength = 64;

    public static bool IsValid(string? name) => name is { Length: > 0 and <= MaximumLength } &&
        name.All(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_');
}
