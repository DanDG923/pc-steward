using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Win32 = PCSteward.Provider.Chocolatey.Native.LaunchJobNativeMethods;

namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>Test-only fault injection for the N0 launcher. An architecture test bans every <c>src</c> use.</summary>
internal enum LaunchJobFault
{
    None,
    /// <summary>AssignProcessToJobObject is treated as failed and is not called.</summary>
    AssignFails,
    /// <summary>ResumeThread is treated as failed and is not called.</summary>
    ResumeFails,
    /// <summary>TerminateJobObject is treated as failed and is not called.</summary>
    TeardownFails,
    /// <summary>AssignProcessToJobObject is treated as failed, and the bounded termination wait is treated as unconfirmed
    /// (TerminateProcess is still called, so no process is left behind).</summary>
    AssignFailsTerminationUnconfirmed
}

/// <summary>
/// N0 job-object launcher (Task 50 design rev 5 §4.1, H4; FR-UPD-15, TM-03/07/13). Each launch owns a fresh job with
/// exactly <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c> (no breakaway) and its own completion port. The process is created
/// suspended with an explicit handle list (NUL stdin and the two pipe write ends) and an environment block built only
/// from the spec, assigned to the job, then resumed. Settlement is
/// <c>QueryInformationJobObject(JobObjectBasicAccountingInformation).ActiveProcesses == 0</c> observed while the job
/// handle is open; completion-port messages only wake the observer. Closing the job handle is a backstop, never the
/// settlement step.
/// </summary>
internal sealed class LaunchJob : IDisposable
{
    internal const int OutputLimitCharacters = 1_000_000;
    /// <summary>Exit code given to every process ended by the launcher's teardown.</summary>
    internal const uint TerminationExitCode = 1;
    internal static readonly TimeSpan SettleWindow = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan TerminationBudget = TimeSpan.FromSeconds(5);
    private const int PollMilliseconds = 50;
    private const int ReadBufferBytes = 16 * 1024;
    private const int MaximumConsecutiveEmptyReads = 64;
    private static readonly TimeSpan PipeConnectTimeout = TimeSpan.FromSeconds(5);
    /// <summary>
    /// Serializes the window in which this launcher's inheritable handles exist (creation through ReleaseChildEnd), so
    /// two launches never inherit each other's pipe ends (review L3). Residual: an unrelated Process.Start elsewhere in
    /// the same process during that window could still inherit them; the helper runs one item at a time and starts
    /// processes only through this launcher (design §4.1).
    /// </summary>
    private static readonly object InheritableHandleGate = new();

    private readonly SafeKernelHandle job;
    private readonly SafeKernelHandle completionPort;

    private LaunchJob(SafeKernelHandle job, SafeKernelHandle completionPort)
    {
        this.job = job;
        this.completionPort = completionPort;
    }

    /// <summary>
    /// Launches <paramref name="spec"/> in a fresh job and observes it until settlement, cancellation, the deadline or an
    /// output overflow. The returned task does not fault for launch or supervision outcomes; it faults only for an
    /// unexpected internal error, which a caller must treat as an unknown invocation.
    /// </summary>
    internal static Task<NativeProcessObservation> RunCoreAsync(NativeLaunchSpec spec, TimeSpan deadline,
        CancellationToken cancellationToken) => StartAsync(spec, deadline, LaunchJobFault.None, null, null, cancellationToken);

    /// <summary>
    /// N3 (§4.1, rev-2 M4): the typed launcher entry. It takes no path or argument string: the spec comes only from
    /// <see cref="NativeLaunchSpec.FromBinding"/> over the held binding and the sealed command. The launch deadline is
    /// <see cref="CapDeadline"/>: the command's existing item budget capped by <paramref name="remaining"/> (§4 M5). An
    /// already-cancelled token, or nothing left of the budget, creates no process and reports a settled
    /// <see cref="NativeInvocation.NotStarted"/> with the matching flag. Called only by the three
    /// <c>ChocolateyClientQueries</c> methods (architecture test); this is the only <c>src</c> caller of
    /// <see cref="RunCoreAsync"/>.
    /// </summary>
    internal static Task<NativeProcessObservation> RunAsync(ChocolateyHeldClientBinding client, ChocolateyCompiledCommand command,
        TimeSpan remaining, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(command);
        NativeLaunchSpec spec = NativeLaunchSpec.FromBinding(client, command);
        if (cancellationToken.IsCancellationRequested) return Task.FromResult(NotStarted(cancelled: true));
        TimeSpan deadline = CapDeadline(command, remaining);
        if (deadline <= TimeSpan.Zero) return Task.FromResult(NotStarted(cancelled: false, timedOut: true));
        return RunCoreAsync(spec, deadline, cancellationToken);
    }

    /// <summary>min(the command's existing budget, <paramref name="remaining"/>); never longer than the budget (§4 M5).</summary>
    internal static TimeSpan CapDeadline(ChocolateyCompiledCommand command, TimeSpan remaining)
    {
        ArgumentNullException.ThrowIfNull(command);
        return remaining < command.Budget ? remaining : command.Budget;
    }

    /// <summary>Test-only entry with an injected fault. Any <c>src</c> caller fails the architecture tests.</summary>
    internal static Task<NativeProcessObservation> RunCoreWithFaultForTestsAsync(NativeLaunchSpec spec, TimeSpan deadline,
        LaunchJobFault fault, CancellationToken cancellationToken) => StartAsync(spec, deadline, fault, null, null, cancellationToken);

    /// <summary>
    /// Test-only entry (any <c>src</c> caller fails the architecture tests): launches <paramref name="spec"/> under the ADR-031
    /// R5 watchdog over <paramref name="watch"/> (fixture directories and test keys) and reports the watchdog's fault to
    /// <paramref name="stopped"/> after the watchdog was disposed, exactly where a client self-update spec records it.
    /// </summary>
    internal static Task<NativeProcessObservation> RunCoreWithWatchdogForTestsAsync(NativeLaunchSpec spec, TimeSpan deadline,
        ChocolateyClientWatchTargets watch, Action<ChocolateyClientWatchdogFault> stopped, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(watch);
        ArgumentNullException.ThrowIfNull(stopped);
        return StartAsync(spec, deadline, LaunchJobFault.None, watch, stopped, cancellationToken);
    }

    public void Dispose()
    {
        job.Dispose();
        completionPort.Dispose();
    }

    private static Task<NativeProcessObservation> StartAsync(NativeLaunchSpec spec, TimeSpan deadline, LaunchJobFault fault,
        ChocolateyClientWatchTargets? testWatch, Action<ChocolateyClientWatchdogFault>? testStopped, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(deadline, TimeSpan.Zero);
        if (!Enum.IsDefined(fault)) throw new ArgumentOutOfRangeException(nameof(fault));
        Stopwatch clock = Stopwatch.StartNew();
        // The observer blocks on kernel waits for the whole launch, so it gets its own thread.
        return Task.Factory.StartNew(() => Run(spec, deadline, fault, clock, testWatch, testStopped, cancellationToken), CancellationToken.None,
            TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
    }

    private static NativeProcessObservation Run(NativeLaunchSpec spec, TimeSpan deadline, LaunchJobFault fault,
        Stopwatch clock, ChocolateyClientWatchTargets? testWatch, Action<ChocolateyClientWatchdogFault>? testStopped,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return NotStarted(cancelled: true);
        char[] commandLine = (NativeCommandLine.Build(spec.ImagePath, spec.Arguments) + "\0").ToCharArray();
        char[] environment = NativeCommandLine.BuildEnvironmentBlock(spec.Environment);
        LaunchJob? launch = null;
        SafeFileHandle? input = null;
        OutputPipe? outputPipe = null;
        OutputPipe? errorPipe = null;
        SafeProcessHandle? process = null;
        SafeKernelHandle? thread = null;
        OutputBudget budget = new(OutputLimitCharacters);
        OutputCapture output = new(budget);
        OutputCapture error = new(budget);
        ChocolateyClientUpdateWatchdog? watchdog = null;
        using CancellationTokenSource readerStop = new();
        try
        {
            // ADR-031 design §2.1 R5: a client self-update is watched from before its last pre-creation checks (the setup
            // override re-read and the watchdog's own check below), so no change can fall between a check and the watch. A
            // watch that cannot be armed launches nothing.
            ChocolateyClientWatchTargets? watchTargets;
            try { watchTargets = testWatch ?? spec.WatchTargets(); }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { return NotStarted(cancelled: false); }
            if (watchTargets is not null)
            {
                watchdog = ChocolateyClientUpdateWatchdog.TryArm(watchTargets);
                if (watchdog is null) return NotStarted(cancelled: false);
            }
            bool created;
            bool releaseFailed = false;
            // Review L3: every inheritable handle of this launch exists only inside the gate.
            lock (InheritableHandleGate)
            {
                try
                {
                    launch = TryCreate();
                    if (launch is null) return NotStarted(cancelled: false);
                    input = OpenNullInput();
                    if (input.IsInvalid) return NotStarted(cancelled: false);
                    outputPipe = OutputPipe.Create();
                    errorPipe = OutputPipe.Create();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                    TimeoutException or Win32Exception)
                {
                    // Nothing has been created that could run.
                    return NotStarted(cancelled: false);
                }

                // N3: a binding that no longer holds its engine (disposed, or already released by an upgrade) launches nothing.
                // ADR-031 design §2 R5: a client self-update re-checks the setup override names immediately before creation.
                created = spec.MayCreateProcess && spec.SetupEnvironmentPermitsLaunch() && (watchdog is null || watchdog.PermitsCreation()) &&
                    TryCreateSuspended(spec, commandLine, environment, input, outputPipe, errorPipe, out process, out thread);
                try
                {
                    // Drop every parent-side inheritable copy before leaving the gate; the child holds its own.
                    outputPipe.ReleaseChildEnd();
                    errorPipe.ReleaseChildEnd();
                    input.Dispose();
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    releaseFailed = true;
                }
            }
            if (!created || process is null || thread is null) return NotStarted(cancelled: false);
            // A suspended process exists from here on; it cannot have run user code until ResumeThread succeeds.
            if (releaseFailed) return Unknown(terminationFailed: !TerminateAndConfirm(process, fault));

            bool resumed;
            try
            {
                output.Start(outputPipe.Server, readerStop.Token);
                error.Start(errorPipe.Server, readerStop.Token);
                bool assigned = fault is not (LaunchJobFault.AssignFails or LaunchJobFault.AssignFailsTerminationUnconfirmed) &&
                    Win32.AssignProcessToJobObject(launch.job, process);
                resumed = assigned && fault != LaunchJobFault.ResumeFails &&
                    Win32.ResumeThread(thread) != Win32.ResumeThreadFailed;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // The resume outcome is unknown, so termination cannot make this NotStarted.
                return Unknown(terminationFailed: !TerminateAndConfirm(process, fault));
            }

            if (!resumed)
            {
                // L1: a never-resumed process is NotStarted only once its termination is confirmed.
                return TerminateAndConfirm(process, fault) ? NotStarted(cancelled: false) : Unknown(terminationFailed: true);
            }
            // ADR-031 design §2.1 R5: from here on the watchdog terminates the job on the first fault (before any hold is released).
            LaunchJob running = launch;
            watchdog?.Start(() => Win32.TerminateJobObject(running.job, TerminationExitCode));
            // §4 L2: CreateProcessW, assignment and resume all succeeded; an upgrade now releases the code holds. A failed
            // package release only keeps the holds longer (until the binding is disposed), so it never changes the outcome; a
            // failed client self-update release is recorded on its binding and forces the postcheck to Unverified (ADR-031 R3).
            try { spec.OnResumed(); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { spec.OnReleaseFaulted(); }
            return launch.Supervise(process, output, error, budget, deadline, clock, fault, cancellationToken);
        }
        finally
        {
            readerStop.Cancel();
            // R5: the watch thread is joined before the job handle can close; its fault is reported once.
            if (watchdog is not null)
            {
                watchdog.Dispose();
                if (testStopped is not null) testStopped(watchdog.Fault);
                else spec.OnWatchdogStopped(watchdog.Fault);
            }
            outputPipe?.Dispose();
            errorPipe?.Dispose();
            thread?.Dispose();
            // Settlement was already observed (or reported as failed) with the job open; this close is the backstop.
            launch?.Dispose();
            process?.Dispose();
            input?.Dispose();
        }
    }

    private NativeProcessObservation Supervise(SafeProcessHandle process, OutputCapture output, OutputCapture error,
        OutputBudget budget, TimeSpan deadline, Stopwatch clock, LaunchJobFault fault, CancellationToken cancellationToken)
    {
        int? exitCode = null;
        bool cancelled = false, timedOut = false, overflow = false, faulted = false, windowExpired = false;

        // Phase 1: the primary process.
        while (true)
        {
            if (budget.Exceeded) { overflow = true; break; }
            if (cancellationToken.IsCancellationRequested) { cancelled = true; break; }
            TimeSpan left = deadline - clock.Elapsed;
            if (left <= TimeSpan.Zero) { timedOut = true; break; }
            uint wait = Win32.WaitForSingleObject(process, Slice(left));
            if (wait == Win32.WaitObject0)
            {
                exitCode = TryReadExitCode(process);
                faulted = !exitCode.HasValue;
                break;
            }
            if (wait != Win32.WaitTimeout) { faulted = true; break; }
        }

        // Phase 2: after a normal primary exit, wait min(10 s, time left) for zero active processes and both EOFs.
        if (exitCode.HasValue && !overflow && !cancelled && !timedOut && !faulted)
        {
            TimeSpan left = deadline - clock.Elapsed;
            TimeSpan window = left >= SettleWindow ? SettleWindow : left > TimeSpan.Zero ? left : TimeSpan.Zero;
            bool clippedByDeadline = window < SettleWindow;
            Stopwatch settle = Stopwatch.StartNew();
            while (true)
            {
                if (budget.Exceeded) { overflow = true; break; }
                if (cancellationToken.IsCancellationRequested) { cancelled = true; break; }
                if (output.AtEndOfFile && error.AtEndOfFile && ActiveProcessesAreZero())
                {
                    // An over-budget final chunk can be dropped between the check above and end of file (review M2).
                    if (budget.Exceeded) { overflow = true; break; }
                    return new(NativeInvocation.Started, exitCode, DirectExitObserved: true, OwnedIoCompleted: true,
                        PipeEofObserved: true, TerminationFailed: false, AggregateSettled: true, Cancelled: false,
                        TimedOut: false, OutputLimitExceeded: false, LingeringTerminated: false, output.Snapshot(),
                        error.Snapshot());
                }
                TimeSpan windowLeft = window - settle.Elapsed;
                if (windowLeft <= TimeSpan.Zero)
                {
                    windowExpired = true;
                    timedOut = clippedByDeadline;
                    break;
                }
                WaitForWake(Slice(windowLeft));
            }
        }

        // Teardown (M2): cancellation, deadline, overflow, an unsettled window or a supervision fault.
        bool zero = TerminateAndObserveZero(fault, output, error);
        if (!exitCode.HasValue && Win32.WaitForSingleObject(process, 0) == Win32.WaitObject0)
            exitCode = TryReadExitCode(process);
        bool endOfFile = output.AtEndOfFile && error.AtEndOfFile;
        bool settled = zero && exitCode.HasValue && endOfFile;
        return new(NativeInvocation.Started, exitCode, DirectExitObserved: exitCode.HasValue,
            OwnedIoCompleted: output.Completed && error.Completed, PipeEofObserved: endOfFile, TerminationFailed: !zero,
            AggregateSettled: settled, cancelled, timedOut, overflow || budget.Exceeded, LingeringTerminated: windowExpired && settled,
            output.Snapshot(), error.Snapshot());
    }

    private bool TerminateAndObserveZero(LaunchJobFault fault, OutputCapture output, OutputCapture error)
    {
        // The call's result is not trusted either way: only the observed active-process count settles the job.
        if (fault != LaunchJobFault.TeardownFails) _ = Win32.TerminateJobObject(job, TerminationExitCode);
        Stopwatch elapsed = Stopwatch.StartNew();
        bool zero = false;
        while (true)
        {
            zero = zero || ActiveProcessesAreZero();
            // Once zero, give the pipe readers the rest of the same budget to reach end of file.
            if (zero && output.AtEndOfFile && error.AtEndOfFile) return true;
            TimeSpan left = TerminationBudget - elapsed.Elapsed;
            if (left <= TimeSpan.Zero) return zero;
            WaitForWake(Slice(left));
        }
    }

    private bool ActiveProcessesAreZero() =>
        Win32.QueryInformationJobObject(job, Win32.JobObjectBasicAccountingInformationClass,
            out Win32.JobBasicAccountingInformation accounting, (uint)Marshal.SizeOf<Win32.JobBasicAccountingInformation>(),
            IntPtr.Zero) && accounting.ActiveProcesses == 0;

    /// <summary>Wake-up only: any message (including ACTIVE_PROCESS_ZERO) or the timeout leads to a fresh query.</summary>
    private void WaitForWake(uint milliseconds) =>
        _ = Win32.GetQueuedCompletionStatus(completionPort, out _, out _, out _, milliseconds);

    private static uint Slice(TimeSpan left) =>
        (uint)Math.Clamp((long)Math.Ceiling(left.TotalMilliseconds), 1, PollMilliseconds);

    private static int? TryReadExitCode(SafeProcessHandle process) =>
        Win32.GetExitCodeProcess(process, out uint exitCode) ? unchecked((int)exitCode) : null;

    private static bool TerminateAndConfirm(SafeProcessHandle process, LaunchJobFault fault)
    {
        _ = Win32.TerminateProcess(process, TerminationExitCode);
        bool confirmed = Win32.WaitForSingleObject(process, (uint)TerminationBudget.TotalMilliseconds) == Win32.WaitObject0;
        return confirmed && fault != LaunchJobFault.AssignFailsTerminationUnconfirmed;
    }

    private static LaunchJob? TryCreate()
    {
        SafeKernelHandle job = Win32.CreateJobObject(IntPtr.Zero, IntPtr.Zero);
        SafeKernelHandle port = Win32.CreateIoCompletionPort(Win32.InvalidHandleValue, IntPtr.Zero, UIntPtr.Zero, 1);
        LaunchJob launch = new(job, port);
        if (job.IsInvalid || port.IsInvalid || !launch.TryConfigure())
        {
            launch.Dispose();
            return null;
        }
        return launch;
    }

    private bool TryConfigure()
    {
        uint limitsSize = (uint)Marshal.SizeOf<Win32.JobExtendedLimitInformation>();
        Win32.JobExtendedLimitInformation limits = default;
        limits.BasicLimitInformation.LimitFlags = Win32.JobObjectLimitKillOnJobClose;
        if (!Win32.SetInformationJobObject(job, Win32.JobObjectExtendedLimitInformationClass, ref limits, limitsSize))
            return false;
        bool added = false;
        try
        {
            completionPort.DangerousAddRef(ref added);
            Win32.JobAssociateCompletionPort association = new()
            {
                CompletionKey = IntPtr.Zero,
                CompletionPort = completionPort.DangerousGetHandle()
            };
            if (!Win32.SetInformationJobObject(job, Win32.JobObjectAssociateCompletionPortInformationClass,
                ref association, (uint)Marshal.SizeOf<Win32.JobAssociateCompletionPort>()))
                return false;
        }
        finally
        {
            if (added) completionPort.DangerousRelease();
        }
        // Read back: the limit flags must be exactly kill-on-close, so no breakaway of any kind is permitted.
        return Win32.QueryInformationJobObject(job, Win32.JobObjectExtendedLimitInformationClass,
            out Win32.JobExtendedLimitInformation applied, limitsSize, IntPtr.Zero) &&
            applied.BasicLimitInformation.LimitFlags == Win32.JobObjectLimitKillOnJobClose;
    }

    private static SafeFileHandle OpenNullInput()
    {
        Win32.SecurityAttributes attributes = new()
        {
            Length = Marshal.SizeOf<Win32.SecurityAttributes>(),
            InheritHandle = true
        };
        return Win32.CreateFile(@"\\.\NUL", Win32.GenericRead, Win32.FileShareRead | Win32.FileShareWrite, ref attributes,
            Win32.OpenExisting, 0, IntPtr.Zero);
    }

    private static bool TryCreateSuspended(NativeLaunchSpec spec, char[] commandLine, char[] environment,
        SafeFileHandle input, OutputPipe output, OutputPipe error,
        [NotNullWhen(true)] out SafeProcessHandle? process, [NotNullWhen(true)] out SafeKernelHandle? thread)
    {
        process = null;
        thread = null;
        SafeHandle[] inherited = [input, output.ChildEnd, error.ChildEnd];
        bool[] referenced = new bool[inherited.Length];
        IntPtr handleList = IntPtr.Zero;
        IntPtr attributeList = IntPtr.Zero;
        bool attributeListInitialized = false;
        try
        {
            for (int index = 0; index < inherited.Length; index++) inherited[index].DangerousAddRef(ref referenced[index]);
            int handleListBytes = IntPtr.Size * inherited.Length;
            handleList = Marshal.AllocHGlobal(handleListBytes);
            for (int index = 0; index < inherited.Length; index++)
                Marshal.WriteIntPtr(handleList, index * IntPtr.Size, inherited[index].DangerousGetHandle());

            IntPtr attributeListBytes = IntPtr.Zero;
            _ = Win32.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeListBytes);
            if (attributeListBytes == IntPtr.Zero) return false;
            attributeList = Marshal.AllocHGlobal(attributeListBytes);
            if (!Win32.InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeListBytes)) return false;
            attributeListInitialized = true;
            // Only these three handles can be inherited, whatever else in this process is inheritable.
            if (!Win32.UpdateProcThreadAttribute(attributeList, 0, Win32.ProcThreadAttributeHandleList, handleList,
                new IntPtr(handleListBytes), IntPtr.Zero, IntPtr.Zero))
                return false;

            Win32.StartupInfoEx startup = default;
            startup.StartupInfo.Size = Marshal.SizeOf<Win32.StartupInfoEx>();
            startup.StartupInfo.Flags = Win32.StartfUseStdHandles;
            startup.StartupInfo.StandardInput = inherited[0].DangerousGetHandle();
            startup.StartupInfo.StandardOutput = inherited[1].DangerousGetHandle();
            startup.StartupInfo.StandardError = inherited[2].DangerousGetHandle();
            startup.AttributeList = attributeList;
            const uint flags = Win32.CreateSuspended | Win32.CreateNoWindow | Win32.ExtendedStartupInfoPresent |
                Win32.CreateUnicodeEnvironment;
            if (!Win32.CreateProcess(spec.ImagePath, commandLine, IntPtr.Zero, IntPtr.Zero, inheritHandles: true, flags,
                environment, spec.WorkingDirectory, ref startup, out Win32.ProcessInformation information))
                return false;
            process = new SafeProcessHandle(information.Process, ownsHandle: true);
            thread = new SafeKernelHandle(information.Thread);
            return true;
        }
        finally
        {
            if (attributeListInitialized) Win32.DeleteProcThreadAttributeList(attributeList);
            if (attributeList != IntPtr.Zero) Marshal.FreeHGlobal(attributeList);
            if (handleList != IntPtr.Zero) Marshal.FreeHGlobal(handleList);
            for (int index = 0; index < inherited.Length; index++)
                if (referenced[index]) inherited[index].DangerousRelease();
        }
    }

    private static NativeProcessObservation NotStarted(bool cancelled, bool timedOut = false) =>
        new(NativeInvocation.NotStarted, ExitCode: null, DirectExitObserved: false, OwnedIoCompleted: false,
            PipeEofObserved: false, TerminationFailed: false, AggregateSettled: true, cancelled, timedOut,
            OutputLimitExceeded: false, LingeringTerminated: false, string.Empty, string.Empty);

    private static NativeProcessObservation Unknown(bool terminationFailed) =>
        new(NativeInvocation.Unknown, ExitCode: null, DirectExitObserved: false, OwnedIoCompleted: false,
            PipeEofObserved: false, terminationFailed, AggregateSettled: false, Cancelled: false, TimedOut: false,
            OutputLimitExceeded: false, LingeringTerminated: false, string.Empty, string.Empty);

    /// <summary>
    /// One output pipe: an overlapped, current-user-only, single-instance server read end owned by the launcher and a
    /// synchronous inheritable write end for the child, connected and verified before creation.
    /// </summary>
    private sealed class OutputPipe : IDisposable
    {
        private NamedPipeClientStream? childEnd;

        private OutputPipe(NamedPipeServerStream server, NamedPipeClientStream childEnd)
        {
            Server = server;
            this.childEnd = childEnd;
        }

        internal NamedPipeServerStream Server { get; }

        internal SafePipeHandle ChildEnd =>
            (childEnd ?? throw new ObjectDisposedException(nameof(OutputPipe))).SafePipeHandle;

        internal static OutputPipe Create()
        {
            string name = $"PCSteward.Launch.{Environment.ProcessId}.{Guid.NewGuid():N}";
            NamedPipeServerStream? server = new(name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance, ReadBufferBytes, 0);
            NamedPipeClientStream? client = null;
            try
            {
                Task connected = server.WaitForConnectionAsync();
                client = new(".", name, PipeDirection.Out, PipeOptions.None, TokenImpersonationLevel.None,
                    HandleInheritability.Inheritable);
                client.Connect((int)PipeConnectTimeout.TotalMilliseconds);
                connected.WaitAsync(PipeConnectTimeout).GetAwaiter().GetResult();
                // The single instance must be connected to this process, not to anything that raced for it.
                if (!Win32.GetNamedPipeClientProcessId(server.SafePipeHandle, out uint clientProcessId) ||
                    clientProcessId != (uint)Environment.ProcessId)
                    throw new IOException("The launcher pipe is not connected to this process.");
                OutputPipe pipe = new(server, client);
                server = null;
                client = null;
                return pipe;
            }
            finally
            {
                client?.Dispose();
                server?.Dispose();
            }
        }

        /// <summary>Closes this process's copy of the write end so end of file follows the last child writer.</summary>
        internal void ReleaseChildEnd()
        {
            childEnd?.Dispose();
            childEnd = null;
        }

        public void Dispose()
        {
            ReleaseChildEnd();
            Server.Dispose();
        }
    }

    /// <summary>Shared 1,000,000-character budget across standard output and standard error.</summary>
    private sealed class OutputBudget(int limit)
    {
        private int used;
        private int exceeded;

        internal bool Exceeded => Volatile.Read(ref exceeded) != 0;

        internal bool TryReserve(int count)
        {
            while (true)
            {
                if (Exceeded) return false;
                int current = Volatile.Read(ref used);
                long next = (long)current + count;
                if (next > limit)
                {
                    Volatile.Write(ref exceeded, 1);
                    return false;
                }
                if (Interlocked.CompareExchange(ref used, (int)next, current) == current) return true;
            }
        }
    }

    /// <summary>
    /// Reads one pipe to end of file. Text is decoded as UTF-8 (invalid sequences replaced). After the budget is exceeded
    /// the reader keeps draining and discarding, so end of file can still be observed after teardown.
    /// </summary>
    private sealed class OutputCapture(OutputBudget budget)
    {
        private readonly StringBuilder text = new();
        private readonly object gate = new();
        private Task<bool> completion = Task.FromResult(false);
        private bool started;

        internal bool Completed => started && completion.IsCompleted;

        internal bool AtEndOfFile => started && completion.IsCompletedSuccessfully && completion.Result;

        internal void Start(Stream source, CancellationToken cancellationToken)
        {
            completion = ReadToEndAsync(source, cancellationToken);
            started = true;
        }

        internal string Snapshot()
        {
            lock (gate) return text.ToString();
        }

        private async Task<bool> ReadToEndAsync(Stream source, CancellationToken cancellationToken)
        {
            Decoder decoder = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false).GetDecoder();
            byte[] bytes = new byte[ReadBufferBytes];
            char[] characters = new char[ReadBufferBytes + 8];
            try
            {
                int consecutiveEmpty = 0;
                while (true)
                {
                    int read = await source.ReadAsync(bytes, cancellationToken).ConfigureAwait(false);
                    // Review L1: a zero-length write by the child also completes a pipe read with 0 bytes. Only a broken
                    // (disconnected) pipe is end of file. The bounded count guards against a stream that never reports the
                    // break, so the reader cannot spin.
                    if (read == 0 && source is PipeStream { IsConnected: true } && ++consecutiveEmpty < MaximumConsecutiveEmptyReads)
                        continue;
                    if (read != 0) consecutiveEmpty = 0;
                    int decoded = decoder.GetChars(bytes, 0, read, characters, 0, flush: read == 0);
                    Append(characters, decoded);
                    if (read == 0) return true;
                }
            }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException)
            {
                return false;
            }
        }

        private void Append(char[] characters, int count)
        {
            if (count == 0 || !budget.TryReserve(count)) return;
            lock (gate) text.Append(characters, 0, count);
        }
    }
}
