using System.Diagnostics;
using System.ComponentModel;
using System.Text;

namespace PCSteward.Provider.WinGet;

internal sealed record OwnedProcessTeardown(bool DirectProcessExitObserved, bool OwnedIoCompleted,
    bool PipeEofObserved, bool KillFailed, Task OwnedCompletion);
internal sealed record OwnedProcessReadResult(int ExitCode, string StandardOutput, string StandardError,
    bool TimedOut, bool Cancelled, bool OutputLimitExceeded, bool OutputIncomplete, OwnedProcessTeardown Teardown);
internal delegate ValueTask<int> OwnedProcessRead(StreamReader reader, Memory<char> buffer, CancellationToken cancellationToken);

internal static class OwnedProcessWork
{
    internal static async Task<OwnedProcessReadResult> RunStartedAsync(Process process, TimeSpan timeout,
        CancellationToken cancellationToken, TimeSpan? teardownBudget = null, Func<Process, bool>? terminate = null,
        OwnedProcessRead? readOperation = null)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        TimeSpan budget = teardownBudget ?? TimeSpan.FromSeconds(5);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(budget, TimeSpan.Zero);

        CancellationTokenSource readersCancellation = new();
        CancellationTokenSource timerCancellation = new();
        TaskCompletionSource callerCancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource overflow = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource readFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration registration = cancellationToken.Register(
            static state => ((TaskCompletionSource)state!).TrySetResult(), callerCancelled);
        OutputCapture stdout = new();
        OutputCapture stderr = new();
        OwnedProcessRead read = readOperation ?? ReadFromStream;
        Task exit = ObserveExitAsync(process);
        Task output = stdout.ReadAsync(process.StandardOutput, overflow, readFailure, read, readersCancellation.Token);
        Task error = stderr.ReadAsync(process.StandardError, overflow, readFailure, read, readersCancellation.Token);
        Task timer = Task.Delay(timeout, timerCancellation.Token);
        Task first = await Task.WhenAny(exit, timer, callerCancelled.Task, overflow.Task, readFailure.Task).ConfigureAwait(false);
        bool cancelled = cancellationToken.IsCancellationRequested;
        bool timedOut = !cancelled && first == timer;
        bool abnormal = cancelled || timedOut || overflow.Task.IsCompleted || readFailure.Task.IsCompleted || exit.IsFaulted;
        Stopwatch teardown = Stopwatch.StartNew();
        bool killFailed = false;
        if (abnormal)
        {
            try { killFailed = !(terminate ?? TryTerminate)(process); }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException)
            {
                killFailed = true;
            }
        }

        // Preserve available diagnostics/EOF first, but reserve part of the same deadline for
        // cancellation and observation of any reads still held by inherited writers.
        Task work = Task.WhenAll(exit, output, error);
        TimeSpan grace = TimeSpan.FromTicks(Math.Min(TimeSpan.FromSeconds(1).Ticks, budget.Ticks / 5));
        TimeSpan drain = Remaining(budget - grace, teardown);
        await CompletesWithinAsync(work, drain).ConfigureAwait(false);
        Task readCancellation = output.IsCompleted && error.IsCompleted
            ? Task.CompletedTask : readersCancellation.CancelAsync();
        Task observedWork = Task.WhenAll(work, readCancellation);
        await CompletesWithinAsync(observedWork, Remaining(budget, teardown)).ConfigureAwait(false);

        // Stop every operation-owned timer/registration even on ordinary process exit.
        Task timerStop = timerCancellation.CancelAsync();
        await registration.DisposeAsync().ConfigureAwait(false);
        callerCancelled.TrySetResult();
        Task observedTimer = ObserveCancellationAsync(timer);

        bool processExited = exit.IsCompletedSuccessfully;
        int exitCode = processExited ? process.ExitCode : -1;
        bool ioCompleted = output.IsCompleted && error.IsCompleted;
        OutputSnapshot capturedOutput = stdout.Snapshot();
        OutputSnapshot capturedError = stderr.Snapshot();
        bool eof = capturedOutput.Eof && capturedError.Eof;
        overflow.TrySetResult();
        readFailure.TrySetResult();
        Task ownedCompletion = FinishOwnershipAsync(process, readersCancellation, timerCancellation, exit,
            observedWork, timerStop, observedTimer, callerCancelled.Task, overflow.Task, readFailure.Task);
        return new(exitCode, capturedOutput.Text, capturedError.Text, timedOut, cancelled,
            capturedOutput.Exceeded || capturedError.Exceeded,
            !ioCompleted || !eof || capturedOutput.Failed || capturedError.Failed,
            new(processExited, ioCompleted, eof, killFailed, ownedCompletion));
    }

    private static bool TryTerminate(Process process)
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        return true;
    }

    private static TimeSpan Remaining(TimeSpan budget, Stopwatch elapsed) =>
        budget > elapsed.Elapsed ? budget - elapsed.Elapsed : TimeSpan.Zero;

    private static async Task<bool> CompletesWithinAsync(Task work, TimeSpan remaining)
    {
        if (work.IsCompleted) return true;
        if (remaining <= TimeSpan.Zero) return false;
        using CancellationTokenSource waitCancellation = new();
        Task delay = Task.Delay(remaining, waitCancellation.Token);
        await Task.WhenAny(work, delay).ConfigureAwait(false);
        await waitCancellation.CancelAsync().ConfigureAwait(false);
        await ObserveCancellationAsync(delay).ConfigureAwait(false);
        return work.IsCompleted;
    }

    private static async Task ObserveCancellationAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) when (task.IsCanceled) { }
    }

    private static async Task ObserveExitAsync(Process process) =>
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);

    private static ValueTask<int> ReadFromStream(StreamReader reader, Memory<char> buffer, CancellationToken cancellationToken) =>
        reader.ReadAsync(buffer, cancellationToken);

    private static async Task FinishOwnershipAsync(Process process, CancellationTokenSource readersCancellation,
        CancellationTokenSource timerCancellation, Task exit, params Task[] work)
    {
        try
        {
            try { await Task.WhenAll(work).ConfigureAwait(false); }
            catch (Exception) when (!exit.IsCompletedSuccessfully)
            {
                // An observation failure is not process exit. Keep a bounded session-owned
                // unresolved root rather than releasing admission or disposing live resources.
                await new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task.ConfigureAwait(false);
            }
        }
        finally
        {
            // This root remains referenced by the result/latch until every owned task settles.
            // Disposal never substitutes for completing a pending read or observing process exit.
            try { process.StandardOutput.Dispose(); }
            finally
            {
                try { process.StandardError.Dispose(); }
                finally
                {
                    process.Dispose();
                    readersCancellation.Dispose();
                    timerCancellation.Dispose();
                }
            }
        }
    }

    private sealed record OutputSnapshot(string Text, bool Eof, bool Exceeded, bool Failed);

    private sealed class OutputCapture
    {
        private const int MaximumCharacters = 1_000_000;
        private readonly object sync = new();
        private readonly StringBuilder captured = new(4096);
        private bool eof;
        private bool exceeded;
        private bool failed;

        internal OutputSnapshot Snapshot() { lock (sync) return new(captured.ToString(), eof, exceeded, failed); }

        internal async Task ReadAsync(StreamReader reader, TaskCompletionSource overflow,
            TaskCompletionSource failure, OwnedProcessRead readOperation, CancellationToken ownedToken)
        {
            char[] buffer = new char[4096];
            try
            {
                int read;
                while ((read = await readOperation(reader, buffer.AsMemory(), ownedToken).ConfigureAwait(false)) != 0)
                {
                    lock (sync)
                    {
                        int available = MaximumCharacters - captured.Length;
                        if (available > 0) captured.Append(buffer, 0, Math.Min(available, read));
                        if (read > available)
                        {
                            exceeded = true;
                            overflow.TrySetResult();
                        }
                    }
                }
                lock (sync) eof = true;
            }
            catch (OperationCanceledException) when (ownedToken.IsCancellationRequested)
            {
                // Keep the bounded partial output; cancellation is not EOF.
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                lock (sync) failed = true;
                failure.TrySetResult();
            }
        }
    }
}
