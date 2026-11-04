using System.IO.Pipes;
using PCSteward.Domain;
using PCSteward.Execution;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// Fakes for the Task 50 qualification probe desktop side (probe spec rev 4 §5, slice S4): an in-memory Windows-path result
/// file system, a gate session that records its threads, a fixed clock, a capability that records its holds, the transport
/// dependencies with call counters, and a real pipe pair whose helper end is either scripted or the real S3 probe endpoint.
/// Every value is synthetic; nothing touches the machine's gate, temp folder or Program Files.
/// </summary>
internal static class DesktopProbeFixture
{
    internal static readonly string Nonce = ProbeFixture.Nonce;
    internal const string Temp = @"C:\Users\owner\AppData\Local\Temp\";
    internal const string LongTemp = @"C:\Users\owner\AppData\Local\Temp";
    internal const string Folder = LongTemp + @"\pcsteward-exact-artifact";
    internal const string ResultPath = Folder + @"\qualification-result.json";
    internal static readonly DateTimeOffset Started = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);
    internal static readonly ReviewedHelperBudgets Fast = new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));
    internal static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);

    internal static IReadOnlyList<QualificationProbeCapabilityEntry> G1(QualificationProbeCapabilityStatus status) =>
        [.. QualificationProbeContract.Compiled.G1Keys.Select(key => new QualificationProbeCapabilityEntry(key, status))];

    internal static QualificationProbeFacts Passing(int nodes = 123456, int ancestors = 12345) => new(QualificationProbeTokenVerdict.Accepted,
        G1(QualificationProbeCapabilityStatus.Supported), QualificationProbeObservation.Observed, null, QualificationProbeFailedCheck.None,
        QualificationProbeFailedAccess.None, QualificationProbeRefusalReason.None, QualificationProbeRefusalDetail.None, nodes, ancestors, "2.7.5", true, 64,
        QualificationProbeWireCodec.ObservedDiagnostic);

    internal static QualificationProbeFacts TokenRefused() => new(QualificationProbeTokenVerdict.EvaluationPrivilegeNotAllowed,
        G1(QualificationProbeCapabilityStatus.NotEvaluated), QualificationProbeObservation.NotRun, null, QualificationProbeFailedCheck.None,
        QualificationProbeFailedAccess.None, QualificationProbeRefusalReason.None, QualificationProbeRefusalDetail.None, 0, 0, null, false, 0,
        QualificationProbeWireCodec.TokenRefusedDiagnostic);

    internal static QualificationProbePins Pins(char fill = 'A') => new(new string(fill, 64), new string('B', 64), new string('C', 64), new string('9', 64),
        new string('E', 40), new DateTimeOffset(2026, 11, 13, 20, 18, 9, TimeSpan.Zero));

    internal static QualificationProbeTransportFacts AllFacts(int? exitCode = 0) => new(true, true, true, true, true, true, true, true, true, true, false, exitCode);

    internal static QualificationProbeRun Accepted(QualificationProbeFacts? facts = null) => new(QualificationProbeDesktopOutcome.ResponseAccepted,
        G1(QualificationProbeCapabilityStatus.Supported), AllFacts(), Pins(), facts ?? Passing());

    /// <summary>A result file system holding a long-name temp tree from the drive root.</summary>
    internal static FakeResultFileSystem FileSystem()
    {
        FakeResultFileSystem fs = new();
        foreach (string directory in new[] { @"C:\", @"C:\Users", @"C:\Users\owner", @"C:\Users\owner\AppData", @"C:\Users\owner\AppData\Local", LongTemp })
            fs.AddDirectory(directory);
        return fs;
    }

    internal static QualificationProbeRunner Runner(FakeResultFileSystem fs, Func<CancellationToken, Task<QualificationProbeRun>> probe, string temp = Temp) =>
        new(() => temp, fs, new FixedClock(Started), probe);
}

/// <summary>A clock that advances one second per read, so started and finished differ.</summary>
internal sealed class FixedClock(DateTimeOffset start) : TimeProvider
{
    private long reads;
    public override DateTimeOffset GetUtcNow() => start.AddSeconds(Interlocked.Increment(ref reads) - 1);
}

/// <summary>An in-memory, case-insensitive Windows-path file system with an ordered call log.</summary>
internal sealed class FakeResultFileSystem : IQualificationResultFileSystem
{
    private readonly Dictionary<string, QualificationResultNode> nodes = new(StringComparer.OrdinalIgnoreCase);

    internal Dictionary<string, string> LongPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal Dictionary<string, byte[]> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal List<string> Calls { get; } = [];
    internal ProbeEvents? Events { get; set; }
    internal bool FailOpenFolder { get; set; }
    internal bool FailWrite { get; set; }
    internal string? FolderFinalPathOverride { get; set; }
    internal bool HeldFolderDisposed { get; private set; }

    internal void AddDirectory(string path, bool reparse = false, string? finalPath = null) => nodes[Key(path)] = new(true, reparse, finalPath ?? path);

    internal void AddFile(string path) => nodes[Key(path)] = new(false, false, path);

    private static string Key(string path) => path.Length == 3 ? path : path.TrimEnd('\\');

    public string? GetLongPath(string path)
    {
        Calls.Add("long:" + path);
        return LongPaths.TryGetValue(path, out string? mapped) ? mapped : path;
    }

    public QualificationResultNode? Inspect(string path)
    {
        Calls.Add("inspect:" + path);
        return nodes.TryGetValue(Key(path), out QualificationResultNode? node) ? node : null;
    }

    public bool TryCreateDirectory(string path)
    {
        Calls.Add("mkdir:" + path);
        Events?.Add("folder-created");
        if (nodes.ContainsKey(path)) return false;
        nodes[path] = new(true, false, FolderFinalPathOverride ?? path);
        return true;
    }

    public IQualificationResultFolder? OpenFolder(string path)
    {
        Calls.Add("open:" + path);
        if (FailOpenFolder || !nodes.TryGetValue(path, out QualificationResultNode? node)) return null;
        return new Folder(this, path, node);
    }

    private sealed class Folder(FakeResultFileSystem fs, string path, QualificationResultNode node) : IQualificationResultFolder
    {
        public string Path { get; } = path;
        public QualificationResultNode Node { get; } = node;

        public bool TryWriteNew(string leaf, byte[] content)
        {
            string target = Path + "\\" + leaf;
            fs.Calls.Add("write:" + target);
            fs.Events?.Add("file-written");
            if (fs.FailWrite || fs.Files.ContainsKey(target) || fs.nodes.ContainsKey(target)) return false;
            fs.Files[target] = content;
            return true;
        }

        public void Dispose()
        {
            fs.HeldFolderDisposed = true;
            fs.Calls.Add("close:" + Path);
        }
    }
}

/// <summary>A gate session that records the acquiring and disposing threads and its reads.</summary>
internal sealed class FakeGateSession(ProbeEvents? events = null) : IQualificationProbeGateSession
{
    internal int AcquiringThread { get; } = Environment.CurrentManagedThreadId;
    internal int? DisposingThread { get; private set; }
    internal bool Recovery { get; init; }
    internal bool Settled { get; init; } = true;
    internal bool ThrowOnMarker { get; init; }
    internal int MarkerReads { get; private set; }
    internal int Disposals { get; private set; }

    public bool RecoveryRequired => Recovery;

    public bool ResetMarkerSettled()
    {
        MarkerReads++;
        if (ThrowOnMarker) throw new IOException("synthetic marker failure");
        return Settled;
    }

    public void Dispose()
    {
        Disposals++;
        DisposingThread = Environment.CurrentManagedThreadId;
        events?.Add("gate-released");
    }
}

/// <summary>A capability that records each hold and returns a scripted status with no binding (portable).</summary>
internal sealed class RecordingHoldCapability(HelperProtocolCapabilityStatus status) : IHelperProtocolCapability
{
    internal List<UpdateRouteContract> Holds { get; } = [];
    internal bool Throw { get; init; }

    public HelperProtocolCapabilityResult Evaluate(UpdateRouteContract route) => throw new AssertFailedException("the probe only holds");

    public HelperProtocolCapabilityResult TryHold(UpdateRouteContract route, out HeldHelperBinding? binding)
    {
        Holds.Add(route);
        binding = null;
        if (Throw) throw new IOException("synthetic hold failure");
        return HelperProtocolCapabilityResult.For(status);
    }
}

/// <summary>Counters and scripted values behind <see cref="ReviewedHelperProbeDependencies"/>.</summary>
internal sealed class ProbeTransportFakes
{
    internal ProbeEvents Events { get; } = new();
    internal bool Limited { get; set; } = true;
    internal Func<IReadOnlyList<(int ProcessId, string Name)>> Processes { get; set; } = () => [(4242, "PCSteward.Desktop"), (7, "explorer")];
    internal HelperLaunchPaths? Paths { get; set; } = new(@"C:\Program Files\PC Steward\PCSteward.Desktop.exe", @"C:\Program Files\PC Steward\PCSteward.ElevatedHelper.exe");
    internal Func<int, bool> Trust { get; set; } = _ => true;
    internal ReviewedHelperProbeIdentity? Identity { get; set; } = new(ProbeFixture.Sid, 1);
    internal bool PipeThrows { get; set; }
    internal IHelperProtocolCapability Capability { get; set; } = new RecordingHoldCapability(HelperProtocolCapabilityStatus.Supported);
    internal int LimitedCalls { get; private set; }
    internal int ProcessCalls { get; private set; }
    internal int PathCalls { get; private set; }
    internal int TrustCalls { get; private set; }
    internal int IdentityCalls { get; private set; }
    internal int PipeCalls { get; private set; }
    internal int Launches { get; private set; }
    internal bool Declined { get; set; }
    internal IReadOnlyList<string>? LaunchArguments { get; private set; }

    internal ReviewedHelperProbeDependencies Dependencies() => new(
        () => { LimitedCalls++; Events.Add("limited"); return Limited; },
        () => { ProcessCalls++; Events.Add("processes"); return Processes(); },
        4242,
        () => { PathCalls++; Events.Add("paths"); return Paths; },
        _ => { TrustCalls++; Events.Add("trust"); return new HelperLaunchTrustResult(Trust(TrustCalls), Trust(TrustCalls) ? string.Empty : "fixture"); },
        () => { IdentityCalls++; Events.Add("identity"); return Identity; },
        (name, _) =>
        {
            PipeCalls++;
            Events.Add("pipe");
            if (PipeThrows) throw new IOException("synthetic pipe failure");
            return new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        },
        Capability,
        (string _, IReadOnlyList<string> arguments, out bool declined) =>
        {
            Launches++;
            Events.Add("launch");
            LaunchArguments = arguments;
            declined = Declined;
            return null;
        },
        (_, _, _, _, _) => throw new AssertFailedException("no channel without a helper"),
        DesktopProbeFixture.Fast);
}

/// <summary>
/// A real pipe pair for <see cref="ReviewedHelperProbeSession"/>: the desktop end is the server the session reads; the helper
/// end is either a scripted behaviour or the real S3 <see cref="ReviewedHelperProbeEndpoint"/> over the S3 fakes. The channel
/// counts every byte the desktop writes (there must be none).
/// </summary>
internal sealed class DesktopSessionHarness : IAsyncDisposable
{
    private readonly string name = $"pcsteward-test-{Guid.NewGuid():N}";
    private readonly NamedPipeServerStream server;
    private int disposals;

    internal DesktopSessionHarness()
    {
        server = new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
            ElevatedHelperProtocol.MaximumMessageBytes, ElevatedHelperProtocol.MaximumMessageBytes);
        Channel = new(server);
        Owned = new(Exit.Task, () => Interlocked.Increment(ref disposals));
    }

    internal TaskCompletionSource Exit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal HelperProcessWork Owned { get; }
    internal CountingProbeChannel Channel { get; }
    internal int? HelperExitCode { get; set; } = 0;
    internal bool Holding { get; set; } = true;
    internal bool TerminateSucceeds { get; init; } = true;
    internal int Terminations { get; private set; }
    internal int Disposals => Volatile.Read(ref disposals);
    internal string ExpectedNonce { get; set; } = DesktopProbeFixture.Nonce;

    /// <summary>Scripted helper end; the helper "exits" after the behaviour when <paramref name="exitAfter"/> is set.</summary>
    internal Task RunHelper(Func<NamedPipeClientStream, Task> behaviour, bool exitAfter = true) => Task.Run(async () =>
    {
        using (NamedPipeClientStream client = new(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await client.ConnectAsync(10_000);
            await behaviour(client);
        }
        if (exitAfter)
        {
            await AfterAcceptAsync();
            Exit.TrySetResult();
        }
    });

    /// <summary>The real S3 probe endpoint as the helper end (the bootstrap's pipe disposal then ends the frame with EOF).</summary>
    internal Task<int> RunEndpoint(ProbeHarness fakes, string nonce) => Task.Run(async () =>
    {
        int code;
        await using (NamedPipeClientStream client = new(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await client.ConnectAsync(10_000);
            long connected = fakes.Clock.GetTimestamp();
            ReviewedHelperProbeResult result = await ReviewedHelperProbeEndpoint.RunAsync(client, fakes.Peer, fakes.Port, fakes.Capability, fakes.Clock,
                connected, ProbeFixture.Sid, nonce, fakes.Budgets, CancellationToken.None);
            code = ReviewedHelperProbeBootstrap.ExitCode(result.Exit);
        }
        HelperExitCode = code;
        await AfterAcceptAsync();
        Exit.TrySetResult();
        return code;
    });

    /// <summary>A helper process exits only after its pipe was accepted; bounded so a desktop that never accepts cannot hang the test.</summary>
    private async Task AfterAcceptAsync()
    {
        try { await Channel.Connected.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (TimeoutException) { }
    }

    internal Task<ReviewedHelperProbeSessionResult> RunAsync(ReviewedHelperBudgets? budgets = null, CancellationToken cancellationToken = default) =>
        ReviewedHelperProbeSession.RunAsync(Channel, Owned, Terminate, () => HelperExitCode, () => Holding, ExpectedNonce,
            budgets ?? DesktopProbeFixture.Fast, QualificationProbeContract.Compiled, cancellationToken);

    private bool Terminate()
    {
        Terminations++;
        Exit.TrySetResult();
        return TerminateSucceeds;
    }

    internal async Task DisposeServerAsync() => await server.DisposeAsync();

    public async ValueTask DisposeAsync()
    {
        Exit.TrySetResult();
        await server.DisposeAsync();
    }
}

/// <summary>A probe channel over the desktop end that counts written bytes and scripts both peer checks.</summary>
internal sealed class CountingProbeChannel(NamedPipeServerStream server) : IReviewedHelperProbeChannel, IDisposable
{
    private readonly CountingStream stream = new(server);

    internal Func<bool> Identity { get; set; } = () => true;
    internal Func<bool> FileId { get; set; } = () => true;
    internal int IdentityCalls { get; private set; }
    internal int FileIdCalls { get; private set; }
    internal long BytesWritten => stream.Written;

    public Stream Stream => stream;
    /// <summary>Completes once the desktop end has accepted the connection (a helper "exits" only after that, as a real process would).</summary>
    internal TaskCompletionSource Connected { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task WaitForConnectionAsync(CancellationToken cancellationToken)
    {
        await server.WaitForConnectionAsync(cancellationToken);
        Connected.TrySetResult();
    }

    public bool VerifyPeerIdentity()
    {
        IdentityCalls++;
        return Identity();
    }

    public bool PeerImageMatchesEveryBinding()
    {
        FileIdCalls++;
        return FileId();
    }

    public void Dispose() => stream.Dispose();

    private sealed class CountingStream(Stream inner) : Stream
    {
        private long written;
        internal long Written => Interlocked.Read(ref written);
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
            Interlocked.Add(ref written, buffer.Length);
            return inner.WriteAsync(buffer, cancellationToken);
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override void Write(byte[] buffer, int offset, int count)
        {
            Interlocked.Add(ref written, count);
            inner.Write(buffer, offset, count);
        }
        public override void Flush() => inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}

/// <summary>The probe runner's ProbeAsync stand-in: records its thread and returns a scripted run.</summary>
internal sealed class RecordingProbe(Func<QualificationProbeRun> run, ProbeEvents? events = null)
{
    internal int Calls { get; private set; }
    internal int? Thread { get; private set; }

    internal Task<QualificationProbeRun> ProbeAsync(CancellationToken cancellationToken)
    {
        Calls++;
        Thread = Environment.CurrentManagedThreadId;
        events?.Add("probe");
        return Task.FromResult(run());
    }
}
