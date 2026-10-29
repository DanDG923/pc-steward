using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using PCSteward.ProcessFixture;

namespace PCSteward.Application.IntegrationTests;

/// <summary>Real redirected-process I/O probes; outcomes are evidence, not installer-stop claims.</summary>
[TestClass]
[TestCategory("FR-UPD-14")]
public sealed class RedirectedProcessPipeEvidenceTests
{
    public TestContext TestContext { get; set; } = null!;

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
        TestContext.WriteLine($"Redirected {(errorStream ? "stderr" : "stdout")}: stream={streamType}; pending before cancel=True; cancellation completed before write={completedBeforeWrite}; status before write={beforeWrite}; fresh read saw byte={freshSawByte}; fresh read rejected={freshRejected}. Direct fixture parent and holder exited; all fixture-started read tasks observed. External installer behaviour is not tested.");
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

    private sealed class OwnedFixture : IAsyncDisposable
    {
        private static readonly byte[] StopCommand = [(byte)'q'];
        private readonly NamedPipeServerStream control;
        private readonly List<Task> reads = [];
        private Process? holder;
        private bool stopped;
        private bool started;
        private bool holderVerified;

        private OwnedFixture()
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
            start.ArgumentList.Add("parent");
            start.ArgumentList.Add(pipeName);
            Parent = new() { StartInfo = start };
        }

        internal Process Parent { get; }
        internal Process Holder => holder ?? throw new InvalidOperationException("Fixture holder not connected.");

        internal static async Task<OwnedFixture> StartAsync()
        {
            OwnedFixture fixture = new();
            try
            {
                using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
                fixture.started = fixture.Parent.Start();
                Assert.IsTrue(fixture.started);
                Task connection = fixture.control.WaitForConnectionAsync(timeout.Token);
                string? line = await fixture.Parent.StandardOutput.ReadLineAsync(timeout.Token);
                Assert.IsTrue(int.TryParse(line, NumberStyles.None, CultureInfo.InvariantCulture, out int pid),
                    $"The fixed fixture must report its owned holder PID; got {line}.");
                fixture.holder = Process.GetProcessById(pid);
                Assert.AreEqual("PCSteward.ProcessFixture", fixture.holder.ProcessName);
                Assert.AreEqual(Path.GetFullPath(fixture.Parent.StartInfo.FileName),
                    Path.GetFullPath(fixture.holder.MainModule!.FileName), true, CultureInfo.InvariantCulture,
                    "Cleanup may target only the exact test-owned fixture executable.");
                fixture.holderVerified = true;
                await connection;
                await fixture.Parent.WaitForExitAsync(timeout.Token);
                Assert.AreEqual(0, fixture.Parent.ExitCode);
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
                if (started && !Parent.HasExited)
                {
                    Parent.Kill(entireProcessTree: true);
                    using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));
                    await Parent.WaitForExitAsync(timeout.Token);
                }
                Parent.Dispose();
                GC.SuppressFinalize(this);
            }
        }
    }
}
