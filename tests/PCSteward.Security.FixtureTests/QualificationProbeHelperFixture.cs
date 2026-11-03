using System.IO.Pipes;
using PCSteward.Execution;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// Fakes and a real pipe-pair harness for the Task 50 qualification probe entry (probe spec rev 4 §3, slice S3). The desktop end
/// is a raw server stream that only reads the single frame (or sends a byte, or closes); the helper end runs
/// <see cref="ReviewedHelperProbeEndpoint"/> over a fake probe port, item, peer, capability check and clock that return values
/// only. Pipes use the production 16 KiB buffers and every wait is bounded.
/// </summary>
internal static class ProbeFixture
{
    internal static readonly string Nonce = EndpointFixture.Nonce;
    internal static readonly string Sid = Protocol2InterpreterFixture.User;
    internal static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);
    internal const string NewestListed = "2.7.5";

    internal static ChocolateyProbeValues.Observed Observed(int nodes = 1234, int ancestors = 12) => new(NewestListed, 64, nodes, ancestors);

    /// <summary>Short real-time budgets for the cap tests (the fake clock's timers run in real time).</summary>
    internal static ReviewedHelperProbeBudgets Short(int probeMilliseconds = 400, int exitMarginMilliseconds = 200, int writeMilliseconds = 2000) =>
        new(TimeSpan.FromMilliseconds(probeMilliseconds), TimeSpan.FromMilliseconds(exitMarginMilliseconds), TimeSpan.FromMilliseconds(writeMilliseconds),
            TimeSpan.FromMilliseconds(writeMilliseconds));
}

/// <summary>An ordered event log shared by the fakes and the recording pipe (H3).</summary>
internal sealed class ProbeEvents
{
    private readonly Lock gate = new();
    private readonly List<string> events = [];

    internal void Add(string name)
    {
        lock (gate) events.Add(name);
    }

    internal string[] Snapshot()
    {
        lock (gate) return [.. events];
    }
}

internal sealed class FakeProbePort(FakeProbeItem item) : IChocolateyProbePort
{
    internal Func<IChocolateyProbeItem?> Begin { get; set; } = () => item;
    internal TokenVerdict Verdict { get; set; } = TokenVerdict.Accepted;
    internal bool Throw { get; set; }
    internal int BeginCalls { get; private set; }
    internal string? ExpectedSid { get; private set; }

    public IChocolateyPeer? OpenPeer(int pipeServerProcessId) => throw new AssertFailedException("the probe entry never opens the peer");

    public IChocolateyProbeItem? BeginProbe(IChocolateyPeer peer, string expectedSid, out TokenVerdict verdict)
    {
        BeginCalls++;
        ExpectedSid = expectedSid;
        if (Throw) throw new InvalidOperationException("synthetic");
        verdict = Verdict;
        return Begin();
    }
}

/// <summary>
/// A fake probe item returning scripted values. It models the native item's binding as <see cref="BindingDisposed"/>, set
/// inside <see cref="ObserveClient"/> before an <c>Observed</c> value is returned, and records every call in order.
/// </summary>
internal sealed class FakeProbeItem(ProbeEvents? events = null) : IChocolateyProbeItem
{
    private int inFlight;

    internal ProbeEvents Events { get; } = events ?? new ProbeEvents();
    internal Func<string, IReadOnlyList<string>, bool> Protect { get; set; } = (_, _) => true;
    internal Func<TimeSpan, CancellationToken, ChocolateyProbeValues> Observe { get; set; } = (_, _) => ProbeFixture.Observed();
    internal int ProtectCalls { get; private set; }
    internal int ObserveCalls { get; private set; }
    internal TimeSpan? ObserveBound { get; private set; }
    internal bool Disposed { get; private set; }
    internal bool DisposedWhileInFlight { get; private set; }
    internal bool BindingDisposed { get; private set; }

    public bool RequesterCannotModify(string root, IReadOnlyList<string> files)
    {
        ProtectCalls++;
        Events.Add("protect");
        Interlocked.Increment(ref inFlight);
        try { return Protect(root, files); }
        finally { Interlocked.Decrement(ref inFlight); }
    }

    public ChocolateyProbeValues ObserveClient(TimeSpan remaining, CancellationToken cancellationToken)
    {
        ObserveCalls++;
        ObserveBound = remaining;
        Events.Add("observe");
        Interlocked.Increment(ref inFlight);
        try
        {
            ChocolateyProbeValues values = Observe(remaining, cancellationToken);
            if (values is ChocolateyProbeValues.Observed)
            {
                BindingDisposed = true;
                Events.Add("binding-disposed");
            }
            return values;
        }
        finally { Interlocked.Decrement(ref inFlight); }
    }

    public void Dispose()
    {
        DisposedWhileInFlight |= Volatile.Read(ref inFlight) != 0;
        Disposed = true;
        Events.Add("item-disposed");
    }
}

/// <summary>A helper pipe end that records the first write (H3: the order of disposal before the response bytes).</summary>
internal sealed class RecordingPipe(Stream inner, Action onFirstWrite) : Stream
{
    private int written;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref written, 1) == 0) onFirstWrite();
        return inner.WriteAsync(buffer, cancellationToken);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    public override void Flush() => inner.Flush();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();
}

/// <summary>One probe run: the entry's result and the bytes the desktop end read (the single frame, if any).</summary>
internal sealed record ProbeRun(ReviewedHelperProbeResult Result, byte[]? Frame, int BytesAfterFrame)
{
    internal int ExitCode => ReviewedHelperProbeBootstrap.ExitCode(Result.Exit);

    internal QualificationProbeFacts Facts
    {
        get
        {
            Assert.AreEqual(ReviewedHelperProbeExit.ResponseWritten, Result.Exit, Result.Diagnostic);
            Assert.IsNotNull(Frame, "a frame was written");
            Assert.IsTrue(QualificationProbeWireCodec.TryReadProbeResponse(Frame, ProbeFixture.Nonce, out QualificationProbeFacts? facts),
                "the frame is a valid probe response");
            Assert.AreEqual(0, BytesAfterFrame, "one frame, then end of file");
            return facts!;
        }
    }

    internal void AssertNoResponse(ReviewedHelperProbeExit exit = ReviewedHelperProbeExit.NoResponse)
    {
        Assert.AreEqual(exit, Result.Exit, Result.Diagnostic);
        Assert.IsNull(Frame, "nothing is written");
        Assert.AreEqual(0, BytesAfterFrame, "nothing is written");
    }
}

internal sealed class ProbeHarness
{
    internal FakeClock Clock { get; } = new(EndpointFixture.Start);
    internal FakePeer Peer { get; } = new() { UserSid = ProbeFixture.Sid };
    internal FakeProbeItem Item { get; }
    internal FakeProbePort Port { get; }
    internal FakeCapability Capability { get; } = new();
    internal ProbeEvents Events { get; } = new();
    /// <summary>A real port (for example one returning the native probe item over its test seam) instead of <see cref="Port"/>.</summary>
    internal IChocolateyProbePort? PortOverride { get; set; }
    internal IReviewedHelperCapabilityCheck? CapabilityOverride { get; set; }
    internal ReviewedHelperProbeBudgets Budgets { get; set; } = ReviewedHelperProbeBudgets.Production;
    /// <summary>Time already elapsed since the pipe connected (t0) when the entry starts.</summary>
    internal TimeSpan PreElapsed { get; set; }
    internal Func<Stream, Stream>? WrapHelperPipe { get; set; }
    internal string ExpectedSid { get; set; } = ProbeFixture.Sid;

    internal ProbeHarness()
    {
        Item = new FakeProbeItem(Events);
        Port = new FakeProbePort(Item);
    }

    /// <summary>
    /// Runs the entry against a raw desktop end. <paramref name="desktop"/> (optional) drives the server while the entry runs,
    /// for example to send a byte or close the pipe; then the desktop reads the single frame and requires end of file.
    /// </summary>
    internal async Task<ProbeRun> RunAsync(Func<NamedPipeServerStream, Task>? desktop = null)
    {
        string name = $"pcsteward-test-{Guid.NewGuid():N}";
        await using NamedPipeServerStream server = new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
            ReviewedUpdateWireCodec.MaximumFrameBytes, ReviewedUpdateWireCodec.MaximumFrameBytes);
        Task<ReviewedHelperProbeResult> helper = Task.Run(async () =>
        {
            await using NamedPipeClientStream client = new(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(10_000);
            long connected = Clock.GetTimestamp() - PreElapsed.Ticks;
            Stream pipe = WrapHelperPipe?.Invoke(client) ?? client;
            return await ReviewedHelperProbeEndpoint.RunAsync(pipe, Peer, PortOverride ?? Port, CapabilityOverride ?? Capability, Clock, connected,
                ExpectedSid, ProbeFixture.Nonce, Budgets, CancellationToken.None);
        });
        using (CancellationTokenSource connect = new(TimeSpan.FromSeconds(10)))
            await server.WaitForConnectionAsync(connect.Token);
        bool closed = false;
        if (desktop is not null)
        {
            await desktop(server);
            closed = !server.IsConnected;
        }
        ReviewedHelperProbeResult result = await helper.WaitAsync(ProbeFixture.Wait);
        byte[]? frame = null;
        int after = 0;
        if (!closed && server.IsConnected)
        {
            byte[] all = await ReadToEndAsync(server);
            if (all.Length >= 4)
            {
                int length = BitConverter.ToInt32(all, 0);
                Assert.IsTrue(length > 0 && length <= all.Length - 4, "a complete frame");
                frame = all[4..(4 + length)];
                after = all.Length - 4 - length;
            }
            else after = all.Length;
        }
        return new ProbeRun(result, frame, after);
    }

    private static async Task<byte[]> ReadToEndAsync(NamedPipeServerStream server)
    {
        using MemoryStream buffer = new();
        using CancellationTokenSource read = new(TimeSpan.FromSeconds(10));
        byte[] chunk = new byte[4096];
        try
        {
            int count;
            while ((count = await server.ReadAsync(chunk, read.Token)) > 0) buffer.Write(chunk, 0, count);
        }
        catch (IOException)
        {
            // A closed peer may surface as an error rather than end of file.
        }
        return buffer.ToArray();
    }
}
