using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using PCSteward.Application;
using PCSteward.ProcessFixture;
using PCSteward.Provider.WinGet;

namespace PCSteward.Application.IntegrationTests;

/// <summary>Real redirected-process I/O probes; outcomes are evidence, not installer-stop claims.</summary>
[TestClass]
[TestCategory("FR-UPD-14")]
public sealed class RedirectedProcessPipeEvidenceTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task ObservedSourceQueryCarriesRealOwnedCompletionIntoTheSourceGuard()
    {
        await using OwnedFixture fixture = await OwnedFixture.StartAsync(directHolder: true, sourceQuery: true);
        StartedQueryFixtureProcess process = new(fixture.TransferParentOwnership());
        Task<WingetCliSourceObservation> query = new WingetCliSourceGuard(process).ObserveAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        await fixture.RequestExitAsync();
        WingetCliSourceObservation observation = await query.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.IsTrue(observation.IsExpected);
        Assert.IsNotNull(observation.OwnedCompletion);
        Assert.IsTrue(observation.OwnedCompletion.IsCompletedSuccessfully);
        Assert.AreEqual(0, process.LegacyCalls);
    }

    [TestMethod]
    public async Task ObservedQueryCancellationReturnsItsRootInsteadOfLosingTheObservation()
    {
        await using OwnedFixture fixture = await OwnedFixture.StartAsync(directHolder: true, sourceQuery: true);
        using CancellationTokenSource cancellation = new();
        StartedQueryFixtureProcess process = new(fixture.TransferParentOwnership());
        Task<WingetProcessResult> query = process.RunObservedQueryAsync([], TimeSpan.FromSeconds(5), cancellation.Token);
        cancellation.Cancel();
        WingetProcessResult result = await query.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.IsTrue(result.Cancelled);
        Assert.IsNotNull(result.OwnedCompletion);
        await result.OwnedCompletion.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsTrue(result.OwnedCompletion.IsCompletedSuccessfully);
        Assert.AreEqual(0, process.LegacyCalls);
    }

    [TestMethod]
    public async Task ObservedQueryDoesNotEquateCancelledReadsWithDescendantSettlement()
    {
        await using OwnedFixture fixture = await OwnedFixture.StartAsync();
        WingetProcessResult result = await WindowsWingetProcess.ObserveStartedQueryAsync(fixture.TransferParentOwnership(),
            TimeSpan.FromSeconds(2), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(8));
        Assert.IsFalse(fixture.Holder.HasExited);
        Assert.IsTrue(result.OutputIncomplete);
        Assert.IsTrue(result.ExecutionUncertain);
        Assert.IsNotNull(result.OwnedCompletion);
        await fixture.StopAndObserveAsync();
        await result.OwnedCompletion.WaitAsync(TimeSpan.FromSeconds(3));
    }

    private sealed class StartedQueryFixtureProcess(Process started) : IWingetProcess
    {
        internal int LegacyCalls;
        public Task<WingetProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct)
        {
            LegacyCalls++;
            throw new InvalidOperationException("An observed query must not fall back to the legacy reader.");
        }
        public Task<WingetProcessResult> RunObservedQueryAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct) =>
            WindowsWingetProcess.ObserveStartedQueryAsync(started, timeout, ct);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PendingRedirectedReadCancellationAndFreshReadAreMeasuredWhileHolderLives(bool errorStream)
    {
        await using OwnedFixture fixture = await OwnedFixture.StartAsync();
        StreamReader reader = errorStream ? fixture.Parent.StandardError : fixture.Parent.StandardOutput;
        using CancellationTokenSource cancellation = new();
        char[] firstBuffer = new char[1];
        Task<int> first = fixture.Track(reader.ReadAsync(firstBuffer.AsMemory(), cancellation.Token).AsTask());
        await Task.Delay(100);
        Assert.IsTrue(fixture.Parent.HasExited);
        Assert.IsFalse(fixture.Holder.HasExited);
        Assert.IsFalse(first.IsCompleted, "Probe requires a pending redirected read after direct-parent exit.");

        cancellation.Cancel();
        await Task.WhenAny(first, Task.Delay(500));
        bool completedBeforeWrite = first.IsCompleted;
        string beforeWrite = first.Status.ToString();
        await fixture.WriteByteAsync(errorStream);
        bool? freshSawByte = null;
        bool freshRejected = false;
        if (completedBeforeWrite)
        {
            char[] freshBuffer = new char[1];
            try
            {
                Task<int> fresh = fixture.Track(reader.ReadAsync(freshBuffer.AsMemory()).AsTask());
                await Task.WhenAny(fresh, Task.Delay(500));
                freshSawByte = fresh.IsCompletedSuccessfully && fresh.Result == 1 && freshBuffer[0] == 'x';
            }
            catch (InvalidOperationException) { freshRejected = true; }
        }
        Assert.IsFalse(fixture.Holder.HasExited, "Writer must remain alive during cancellation/write-after-cancel measurement.");
        string streamType = reader.BaseStream.GetType().FullName ?? "unknown";
        await fixture.StopAndObserveAsync();
        Assert.IsTrue(first.IsCompleted);
        TestContext.WriteLine($"Redirected {(errorStream ? "stderr" : "stdout")}: runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; stream={streamType}; pending before cancel=True; cancellation completed before write={completedBeforeWrite}; status before write={beforeWrite}; fresh read saw byte={freshSawByte}; fresh read rejected={freshRejected}. Direct fixture parent and holder exited; all fixture-started read tasks observed. External installer behaviour is not tested.");
        Assert.IsTrue(completedBeforeWrite, "This runtime must complete a pending redirected read on cancellation before using that teardown strategy.");
        Assert.AreEqual(true, freshSawByte, "A fresh read must receive the holder's byte: cancellation cannot hide a competing read that consumes it.");
        Assert.IsFalse(freshRejected, "The cancelled read must release StreamReader's outstanding-read ownership.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ReaderDisposalAndPendingReadCompletionAreMeasuredSeparately(bool errorStream)
    {
        await using OwnedFixture fixture = await OwnedFixture.StartAsync();
        StreamReader reader = errorStream ? fixture.Parent.StandardError : fixture.Parent.StandardOutput;
        Task<int> read = fixture.Track(reader.ReadAsync(new char[1].AsMemory()).AsTask());
        await Task.Delay(100);
        Assert.IsTrue(fixture.Parent.HasExited);
        Assert.IsFalse(fixture.Holder.HasExited);
        Assert.IsFalse(read.IsCompleted);

        Stopwatch elapsed = Stopwatch.StartNew();
        reader.Dispose();
        elapsed.Stop();
        await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(2)));
        bool completedBeforeHolderClose = read.IsCompleted;
        bool holderStillAlive = !fixture.Holder.HasExited;
        await fixture.StopAndObserveAsync();
        Assert.IsTrue(read.IsCompleted);
        TestContext.WriteLine($"Redirected {(errorStream ? "stderr" : "stdout")} disposal: synchronous dispose ms={elapsed.Elapsed.TotalMilliseconds:F3}; pending read completed within subsequent 2 s={completedBeforeHolderClose}; holder alive at measurement={holderStillAlive}; final read status={read.Status}. Completion is observed separately from handle disposal.");
    }

    [TestMethod]
    public async Task OwnedCollectorCancellationPreservesCapturedPartialOutput()
    {
        await using OwnedFixture fixture = await OwnedFixture.StartAsync(directHolder: true);
        using CancellationTokenSource caller = new();
        Task<OwnedProcessReadResult> run = OwnedProcessWork.RunStartedAsync(fixture.TransferParentOwnership(),
            TimeSpan.FromSeconds(30), caller.Token, TimeSpan.FromMilliseconds(600));
        await fixture.WriteByteAsync(false);
        caller.Cancel();
        OwnedProcessReadResult result = await run.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsTrue(result.Cancelled);
        Assert.IsFalse(result.TimedOut);
        StringAssert.Contains(result.StandardOutput, "x");
        Assert.IsTrue(result.Teardown.OwnedIoCompleted);
        Assert.IsTrue(result.Teardown.DirectProcessExitObserved);
        await result.Teardown.OwnedCompletion.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task OwnedCollectorNormalExitDrainsOutputAndReleasesOwnedTasks()
    {
        await using OwnedFixture fixture = await OwnedFixture.StartAsync(directHolder: true);
        Task<OwnedProcessReadResult> run = OwnedProcessWork.RunStartedAsync(fixture.TransferParentOwnership(),
            TimeSpan.FromSeconds(30), CancellationToken.None, TimeSpan.FromMilliseconds(600));
        await fixture.WriteByteAsync(false);
        await fixture.RequestExitAsync();
        OwnedProcessReadResult result = await run.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual("x", result.StandardOutput);
        Assert.IsFalse(result.OutputIncomplete);
        Assert.IsTrue(result.Teardown.PipeEofObserved);
        Assert.IsTrue(result.Teardown.OwnedIoCompleted);
        await result.Teardown.OwnedCompletion.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task InheritedWriterUsesOneBudgetAndDoesNotClaimDescendantExit()
    {
        await using OwnedFixture fixture = await OwnedFixture.StartAsync();
        ReadCancellationMeasurement measurement = new();
        Stopwatch elapsed = Stopwatch.StartNew();
        OwnedProcessReadResult result = await OwnedProcessWork.RunStartedAsync(fixture.TransferParentOwnership(),
            TimeSpan.FromSeconds(30), CancellationToken.None, TimeSpan.FromMilliseconds(600),
            readOperation: measurement.ReadAsync).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsTrue(elapsed.Elapsed < TimeSpan.FromSeconds(2));
        Assert.AreEqual(0, result.ExitCode);
        Assert.IsTrue(result.OutputIncomplete);
        Assert.IsTrue(result.Teardown.DirectProcessExitObserved);
        // A snapshot can be incomplete; the root below must still observe actual read completion.
        Assert.IsFalse(result.Teardown.PipeEofObserved);
        Assert.IsFalse(fixture.Holder.HasExited, "Read completion must not invent descendant termination.");
        await result.Teardown.OwnedCompletion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsFalse(fixture.Holder.HasExited, "Owned cancellation completion must be observed before the writer exits.");
        measurement.Log(TestContext, result.Teardown.OwnedIoCompleted);
    }

    [TestMethod]
    public async Task KillFailureRetainsUndisposedOwnershipUntilTheProcessReallyExits()
    {
        await using OwnedFixture fixture = await OwnedFixture.StartAsync(directHolder: true);
        Process process = fixture.TransferParentOwnership();
        StreamReader reader = process.StandardOutput;
        ReadCancellationMeasurement measurement = new();
        using CancellationTokenSource caller = new();
        Task<OwnedProcessReadResult> run = OwnedProcessWork.RunStartedAsync(process,
            TimeSpan.FromSeconds(30), caller.Token, TimeSpan.FromMilliseconds(600),
            _ => throw new System.ComponentModel.Win32Exception(5), readOperation: measurement.ReadAsync);
        await fixture.WriteByteAsync(false);
        caller.Cancel();
        OwnedProcessReadResult result = await run.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsTrue(result.Teardown.KillFailed);
        Assert.IsFalse(result.Teardown.DirectProcessExitObserved);
        Assert.IsTrue(result.OutputIncomplete);
        Assert.IsFalse(result.Teardown.OwnedCompletion.IsCompleted);
        Assert.IsFalse(process.HasExited, "The Process cannot be disposed while its owned exit wait is outstanding.");
        Assert.IsTrue(reader.BaseStream.CanRead, "Resources remain owned until the complete ownership root settles.");
        await measurement.ReadsTerminal.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsFalse(fixture.Holder.HasExited, "Observe cancelled reads while their direct process is still alive.");
        Assert.IsFalse(result.Teardown.OwnedCompletion.IsCompleted, "Reader completion alone cannot release the pending process exit.");
        measurement.Log(TestContext, result.Teardown.OwnedIoCompleted);
        await fixture.StopAndObserveAsync();
        await result.Teardown.OwnedCompletion.WaitAsync(TimeSpan.FromSeconds(2));
        bool disposed;
        try { disposed = !reader.BaseStream.CanRead; }
        catch (ObjectDisposedException) { disposed = true; }
        Assert.IsTrue(disposed);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DelayedReadersRemainOwnedPastOneTeardownBudget(bool killFails)
    {
        await using OwnedFixture fixture = await OwnedFixture.StartAsync(directHolder: killFails);
        Process process = fixture.TransferParentOwnership();
        StreamReader reader = process.StandardOutput;
        TaskCompletionSource<int> releaseReads = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource readsStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int starts = 0;
        CancellationToken readerToken = default;
        async ValueTask<int> DelayedRead(StreamReader stream, Memory<char> buffer, CancellationToken token)
        {
            readerToken = token;
            if (Interlocked.Increment(ref starts) == 2) readsStarted.TrySetResult();
            // Model an owned OS read whose cancellation has not completed by the deadline.
            await releaseReads.Task;
            throw new OperationCanceledException(token);
        }
        using CancellationTokenSource caller = new();
        UpdateExecutionLifetime lifetime = new();
        Stopwatch elapsed = Stopwatch.StartNew();
        Task<OwnedProcessReadResult> run = OwnedProcessWork.RunStartedAsync(process,
            TimeSpan.FromSeconds(30), caller.Token, TimeSpan.FromMilliseconds(600),
            terminate: killFails ? _ => throw new System.ComponentModel.Win32Exception(5) : null,
            readOperation: DelayedRead);
        OwnedProcessReadResult? result = null;
        try
        {
            await readsStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            if (killFails) caller.Cancel();
            result = await run.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.IsTrue(elapsed.Elapsed < TimeSpan.FromSeconds(2), "Both reads and exit share one teardown-attempt budget.");
            Assert.IsTrue(readerToken.IsCancellationRequested, "Reader cancellation is requested before the bounded attempt returns.");
            Assert.IsFalse(result.Teardown.OwnedIoCompleted);
            Assert.IsFalse(result.Teardown.PipeEofObserved);
            Assert.IsTrue(result.OutputIncomplete);
            Assert.AreEqual(!killFails, result.Teardown.DirectProcessExitObserved);
            Assert.AreEqual(killFails, result.Teardown.KillFailed);
            Assert.IsFalse(result.Teardown.OwnedCompletion.IsCompleted);
            Assert.IsTrue(reader.BaseStream.CanRead, "Incomplete reads keep their resources owned.");
            using (UpdateExecutionLease? admission = lifetime.TryAcquire())
            {
                Assert.IsNotNull(admission);
                admission.TrackCompletion(result.Teardown.OwnedCompletion);
            }
            using UpdateExecutionLease? conflicting = lifetime.TryAcquire();
            Assert.IsNull(conflicting, "A bounded return cannot release incomplete owned reads.");
            releaseReads.TrySetResult(0);
            if (killFails) await fixture.StopAndObserveAsync();
            await result.Teardown.OwnedCompletion.WaitAsync(TimeSpan.FromSeconds(2));
            if (!killFails) Assert.IsFalse(fixture.Holder.HasExited, "Observed owned completion cannot claim an external writer exited.");
        }
        finally
        {
            releaseReads.TrySetResult(0);
            await fixture.StopAndObserveAsync();
            result ??= await run.WaitAsync(TimeSpan.FromSeconds(3));
            await result.Teardown.OwnedCompletion.WaitAsync(TimeSpan.FromSeconds(2));
        }
        using UpdateExecutionLease? resumed = lifetime.TryAcquire();
        Assert.IsNotNull(resumed);
        bool disposed;
        try { disposed = !reader.BaseStream.CanRead; }
        catch (ObjectDisposedException) { disposed = true; }
        Assert.IsTrue(disposed, "Resources dispose only after the owned root is observed complete.");
    }

    [TestMethod]
    public async Task OwnedCollectorTimeoutIsDistinctFromCallerCancellation()
    {
        await using OwnedFixture fixture = await OwnedFixture.StartAsync(directHolder: true);
        OwnedProcessReadResult result = await OwnedProcessWork.RunStartedAsync(fixture.TransferParentOwnership(),
            TimeSpan.FromMilliseconds(100), CancellationToken.None, TimeSpan.FromMilliseconds(600)).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsTrue(result.TimedOut);
        Assert.IsFalse(result.Cancelled);
        Assert.IsTrue(result.Teardown.DirectProcessExitObserved);
        await result.Teardown.OwnedCompletion.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [TestMethod]
    public async Task OwnedCollectorOutputOverflowIsBoundedAndDistinctFromTimeout()
    {
        await using OwnedFixture fixture = await OwnedFixture.StartAsync(directHolder: true);
        Task<OwnedProcessReadResult> run = OwnedProcessWork.RunStartedAsync(fixture.TransferParentOwnership(),
            TimeSpan.FromSeconds(30), CancellationToken.None, TimeSpan.FromMilliseconds(600));
        await fixture.RequestBulkAsync();
        OwnedProcessReadResult result = await run.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsTrue(result.OutputLimitExceeded);
        Assert.IsTrue(result.StandardOutput.Length <= 1_000_000);
        Assert.IsFalse(result.TimedOut);
        Assert.IsFalse(result.Cancelled);
        await result.Teardown.OwnedCompletion.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private sealed class ReadCancellationMeasurement
    {
        private readonly TaskCompletionSource readsTerminal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private long cancellationObserved;
        private long lastReadCompleted;
        private int terminalReads;
        private int cancelledReads;
        internal Task ReadsTerminal => readsTerminal.Task;

        internal async ValueTask<int> ReadAsync(StreamReader reader, Memory<char> buffer, CancellationToken token)
        {
            using CancellationTokenRegistration registration = token.Register(() =>
                Interlocked.CompareExchange(ref cancellationObserved, Stopwatch.GetTimestamp(), 0));
            int count = -1;
            bool cancelled = false;
            try { count = await reader.ReadAsync(buffer, token); return count; }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { cancelled = true; throw; }
            finally
            {
                if (token.IsCancellationRequested && (cancelled || count == 0))
                {
                    RecordLatestCompletion();
                    if (cancelled) Interlocked.Increment(ref cancelledReads);
                    if (Interlocked.Increment(ref terminalReads) == 2) readsTerminal.TrySetResult();
                }
            }
        }

        private void RecordLatestCompletion()
        {
            long completed = Stopwatch.GetTimestamp();
            long previous = Volatile.Read(ref lastReadCompleted);
            while (completed > previous)
            {
                long observed = Interlocked.CompareExchange(ref lastReadCompleted, completed, previous);
                if (observed == previous) return;
                previous = observed;
            }
        }

        internal void Log(TestContext context, bool snapshotIoCompleted)
        {
            long start = Volatile.Read(ref cancellationObserved);
            long end = Volatile.Read(ref lastReadCompleted);
            string elapsed = start != 0 && end != 0
                ? ((end - start) * 1000d / Stopwatch.Frequency).ToString("F3", CultureInfo.InvariantCulture)
                : "unavailable";
            context.WriteLine($"Owned native reads: runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; snapshot IO completed={snapshotIoCompleted}; terminal reads={terminalReads}; cancelled reads={cancelledReads}; cancellation-callback-to-last-read completion ms={elapsed}. Callback scheduling is included; a negative delta means the observation callback followed read completion. Writer was still alive when completion was observed.");
        }
    }

    private sealed class OwnedFixture : IAsyncDisposable
    {
        private static readonly byte[] StopCommand = [(byte)'q'];
        private static readonly byte[] BulkCommand = [(byte)'b'];
        private readonly NamedPipeServerStream control;
        private readonly List<Task> reads = [];
        private Process? holder;
        private bool stopped;
        private bool started;
        private bool holderVerified;
        private bool parentTransferred;

        private OwnedFixture(bool directHolder, bool sourceQuery)
        {
            string pipeName = $"pcsteward-task49-{Guid.NewGuid():N}";
            control = new(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            string executable = Path.Combine(Path.GetDirectoryName(typeof(ProcessFixtureMarker).Assembly.Location)!,
                "PCSteward.ProcessFixture.exe");
            ProcessStartInfo start = new(executable)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
            };
            start.ArgumentList.Add(sourceQuery ? "source-query" : directHolder ? "holder" : "parent");
            start.ArgumentList.Add(pipeName);
            Parent = new() { StartInfo = start };
        }

        internal Process Parent { get; }
        internal Process Holder => holder ?? throw new InvalidOperationException("Fixture holder not connected.");

        internal static async Task<OwnedFixture> StartAsync(bool directHolder = false, bool sourceQuery = false)
        {
            OwnedFixture fixture = new(directHolder, sourceQuery);
            try
            {
                using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
                fixture.started = fixture.Parent.Start();
                Assert.IsTrue(fixture.started);
                Task connection = fixture.control.WaitForConnectionAsync(timeout.Token);
                int pid = fixture.Parent.Id;
                if (!directHolder)
                {
                    string? line = await fixture.Parent.StandardOutput.ReadLineAsync(timeout.Token);
                    Assert.IsTrue(int.TryParse(line, NumberStyles.None, CultureInfo.InvariantCulture, out pid),
                        $"The fixed fixture must report its owned holder PID; got {line}.");
                }
                await connection;
                fixture.holder = Process.GetProcessById(pid);
                Assert.AreEqual("PCSteward.ProcessFixture", fixture.holder.ProcessName);
                Assert.AreEqual(Path.GetFullPath(fixture.Parent.StartInfo.FileName),
                    Path.GetFullPath(fixture.holder.MainModule!.FileName), true, CultureInfo.InvariantCulture,
                    "Cleanup may target only the exact test-owned fixture executable.");
                fixture.holderVerified = true;
                if (!directHolder)
                {
                    await fixture.Parent.WaitForExitAsync(timeout.Token);
                    Assert.AreEqual(0, fixture.Parent.ExitCode);
                }
                Assert.IsFalse(fixture.holder.HasExited);
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        internal Task<int> Track(Task<int> read) { reads.Add(read); return read; }
        internal Process TransferParentOwnership() { parentTransferred = true; return Parent; }

        internal async Task RequestExitAsync()
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(1));
            if (control.IsConnected) await control.WriteAsync(StopCommand, timeout.Token);
        }

        internal async Task RequestBulkAsync()
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(1));
            await control.WriteAsync(BulkCommand, timeout.Token);
        }

        internal async Task WriteByteAsync(bool errorStream)
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));
            byte[] command = [(byte)(errorStream ? 'e' : 'w')];
            await control.WriteAsync(command, timeout.Token);
            byte[] acknowledgement = new byte[1];
            await control.ReadExactlyAsync(acknowledgement, timeout.Token);
            Assert.AreEqual(command[0], acknowledgement[0]);
        }

        internal async Task StopAndObserveAsync()
        {
            if (stopped) return;
            if (holderVerified && holder is not null && !holder.HasExited)
            {
                try
                {
                    using CancellationTokenSource commandTimeout = new(TimeSpan.FromSeconds(1));
                    if (control.IsConnected) await control.WriteAsync(StopCommand, commandTimeout.Token);
                }
                catch (Exception exception) when (exception is IOException or OperationCanceledException) { }
                using CancellationTokenSource exitTimeout = new(TimeSpan.FromSeconds(3));
                try { await holder.WaitForExitAsync(exitTimeout.Token); }
                catch (OperationCanceledException)
                {
                    holder.Kill(entireProcessTree: true);
                    using CancellationTokenSource killTimeout = new(TimeSpan.FromSeconds(3));
                    await holder.WaitForExitAsync(killTimeout.Token);
                }
                Assert.IsTrue(holder.HasExited);
            }
            foreach (Task read in reads)
            {
                try { await read.WaitAsync(TimeSpan.FromSeconds(2)); }
                catch (Exception exception) when (read.IsCompleted && exception is
                    OperationCanceledException or IOException or ObjectDisposedException) { }
                Assert.IsTrue(read.IsCompleted, "An owned read must be observed terminal after the controlled holder exits.");
            }
            stopped = true;
        }

        public async ValueTask DisposeAsync()
        {
            try { await StopAndObserveAsync(); }
            finally
            {
                control.Dispose();
                holder?.Dispose();
                if (!parentTransferred && started && !Parent.HasExited)
                {
                    Parent.Kill(entireProcessTree: true);
                    using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));
                    await Parent.WaitForExitAsync(timeout.Token);
                }
                if (!parentTransferred) Parent.Dispose();
                GC.SuppressFinalize(this);
            }
        }
    }
}
