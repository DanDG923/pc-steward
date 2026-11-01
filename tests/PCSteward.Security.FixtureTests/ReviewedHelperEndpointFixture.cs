using System.IO.Pipes;
using System.Text;
using PCSteward.Domain;
using PCSteward.Execution;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// Fakes and a real pipe-pair harness for the protocol-2 helper stage machine (Task 50 design rev 6 §8 N4a). The desktop
/// end runs the real <see cref="ReviewedHelperExchangeSession"/>; the helper end runs <see cref="ReviewedHelperEndpoint"/>
/// over a fake port, item, peer, capability check and clock that return values only. Pipes use the production 16 KiB
/// buffers (a zero-size Windows pipe buffer blocks every write until read) and every wait is bounded.
/// </summary>
internal static class EndpointFixture
{
    internal const string Installed = Protocol2InterpreterFixture.Installed;
    internal const string Candidate = Protocol2InterpreterFixture.Candidate;
    internal const string OtherInstalled = Protocol2InterpreterFixture.OtherInstalled;
    internal const string OtherCandidate = Protocol2InterpreterFixture.OtherCandidate;
    internal const string PeerImage = @"C:\Program Files\PC Steward\PCSteward.Desktop.exe";
    internal static readonly string Fingerprint = Protocol2InterpreterFixture.Fingerprint;
    internal static readonly string OtherFingerprint = Protocol2InterpreterFixture.OtherFingerprint;
    internal static readonly string Nonce = Protocol2InterpreterFixture.Nonce;
    internal static readonly DateTimeOffset Start = Protocol2InterpreterFixture.ReviewedAt.AddMinutes(1);
    internal static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);
    internal static readonly ReviewedHelperBudgets Desktop = new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));

    internal static ReviewedUpdateOperation Operation(string packageId = "git")
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.Parse("71000000-0000-0000-0000-000000000001"),
            Guid.Parse("71000000-0000-0000-0000-000000000002"), new UpdateJobSelection("choco:" + packageId, UpdateProvider.Chocolatey,
                UpdateSource.ChocolateyCommunity, packageId, Installed, Candidate, UpdateScope.Machine, UpdateArchitecture.Neutral,
                UpdateChannel.Stable, "mapping-v2")).Confirm(Protocol2InterpreterFixture.ReviewedAt);
        return new(job, Protocol2InterpreterFixture.User, "choco:" + packageId, "machine:choco", "choco-profile-v2:" + Fingerprint,
            Protocol2InterpreterFixture.ReviewedAt.AddSeconds(-1), UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3,
            ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled), ReviewedUpdateArchitecture.ProviderScriptSelected());
    }

    internal static byte[] Request(ReviewedUpdateOperation operation, DateTimeOffset issuedAt, string? nonce = null) =>
        ReviewedUpdateWireCodec.CreateRequest(operation.GetCanonicalBytes(), operation.Digest, nonce ?? Nonce, issuedAt);

    /// <summary>A codec-valid request for an arbitrary payload under its own digest.</summary>
    internal static byte[] RequestFor(byte[] payload) =>
        ReviewedUpdateWireCodec.CreateRequest(payload, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload)), Nonce, Start);

    internal static NativeProcessObservation Started(int exit = 0, bool settled = true, bool cancelled = false, bool timedOut = false,
        bool outputLimit = false) =>
        new(NativeInvocation.Started, exit, true, true, true, false, settled, cancelled, timedOut, outputLimit, false, "", "");

    internal static NativeProcessObservation NotStarted(bool settled = true, bool terminationFailed = false, bool cancelled = false, bool timedOut = false) =>
        new(NativeInvocation.NotStarted, null, false, false, false, terminationFailed, settled, cancelled, timedOut, false, false, "", "");

    internal static NativeProcessObservation UnknownLaunch(bool terminationFailed = false) =>
        new(NativeInvocation.Unknown, null, false, false, false, terminationFailed, false, false, false, false, false, "", "");

    internal static ChocolateyStageValues ObservedB() => new ChocolateyStageValues.Observed(Installed, Fingerprint);
}

/// <summary>A deterministic clock: monotonic ticks and the wall clock advance only when told to.</summary>
internal sealed class FakeClock(DateTimeOffset start) : TimeProvider
{
    private readonly Lock gate = new();
    private DateTimeOffset now = start;
    private long ticks = 1_000_000_000;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow()
    {
        lock (gate) return now;
    }

    public override long GetTimestamp()
    {
        lock (gate) return ticks;
    }

    internal void Advance(TimeSpan by)
    {
        lock (gate)
        {
            now += by;
            ticks += by.Ticks;
        }
    }
}

internal sealed class FakePeer : IChocolateyPeer
{
    internal string ImagePath { get; set; } = EndpointFixture.PeerImage;
    internal string UserSid { get; set; } = Protocol2InterpreterFixture.User;
    internal bool IdentityReadable { get; set; } = true;
    internal bool ThrowOnIdentity { get; set; }
    internal bool Exited { get; set; }
    public int ProcessId => 4242;
    public bool HasExited => Exited;

    public bool TryReadIdentity(out string imagePath, out int session, out string userSid)
    {
        if (ThrowOnIdentity) throw new InvalidOperationException("synthetic");
        imagePath = IdentityReadable ? ImagePath : string.Empty;
        session = 1;
        userSid = IdentityReadable ? UserSid : string.Empty;
        return IdentityReadable;
    }

    public void Dispose() { }
}

internal sealed class FakePort(FakeItem item) : IChocolateyHelperQualificationPort
{
    internal Func<FakeItem?> Begin { get; set; } = () => item;
    internal ChocolateyItemRefusal Refusal { get; set; } = ChocolateyItemRefusal.None;
    internal int BeginCalls { get; private set; }
    internal string? ExpectedSid { get; private set; }

    public IChocolateyPeer? OpenPeer(int pipeServerProcessId) => throw new AssertFailedException("the stage machine never opens the peer");

    public IChocolateyHelperItem? BeginItem(IChocolateyPeer peer, string expectedSid, out ChocolateyItemRefusal refusal)
    {
        BeginCalls++;
        ExpectedSid = expectedSid;
        refusal = Refusal;
        return Begin();
    }
}

/// <summary>A fake item returning scripted values; it records every call and the bound it was given.</summary>
internal sealed class FakeItem : IChocolateyHelperItem
{
    internal Func<ReviewedUpdateOperation, string, TimeSpan, CancellationToken, ChocolateyStageValues> Observe { get; set; } =
        (_, _, _, _) => EndpointFixture.ObservedB();
    internal Func<TimeSpan, CancellationToken, Task<ChocolateyCandidateValues>> Query { get; set; } =
        (_, _) => Task.FromResult<ChocolateyCandidateValues>(new ChocolateyCandidateValues.Matched(EndpointFixture.Candidate));
    internal Func<TimeSpan, CancellationToken, ChocolateyStageValues> Revalidate { get; set; } = (_, _) => EndpointFixture.ObservedB();
    internal Func<TimeSpan, CancellationToken, Task<ChocolateyUpgradeResult>> Upgrade { get; set; } =
        (_, _) => Task.FromResult<ChocolateyUpgradeResult>(new ChocolateyUpgradeResult.Observed(EndpointFixture.Started()));
    internal Func<string, IReadOnlyList<string>, bool> Protect { get; set; } = (_, _) => true;
    internal Func<bool> Settled { get; set; } = () => true;

    internal List<string> Calls { get; } = [];
    internal List<TimeSpan> Bounds { get; } = [];
    internal string? ExpectedFingerprint { get; private set; }
    internal bool Disposed { get; private set; }
    internal int UpgradeCalls => Calls.Count(call => call == "upgrade");

    public bool AggregateSettled => Settled();

    public bool RequesterCannotModify(string root, IReadOnlyList<string> files)
    {
        Calls.Add("protect");
        return Protect(root, files);
    }

    public ChocolateyStageValues ObserveProfile(ReviewedUpdateOperation operation, string expectedFingerprint, TimeSpan remaining,
        CancellationToken cancellationToken)
    {
        Calls.Add("observe");
        Bounds.Add(remaining);
        ExpectedFingerprint = expectedFingerprint;
        return Observe(operation, expectedFingerprint, remaining, cancellationToken);
    }

    public Task<ChocolateyCandidateValues> QueryCandidateAsync(TimeSpan remaining, CancellationToken cancellationToken)
    {
        Calls.Add("query");
        Bounds.Add(remaining);
        return Query(remaining, cancellationToken);
    }

    public ChocolateyStageValues RevalidateBeforeLaunch(TimeSpan remaining, CancellationToken cancellationToken)
    {
        Calls.Add("revalidate");
        Bounds.Add(remaining);
        return Revalidate(remaining, cancellationToken);
    }

    public Task<ChocolateyUpgradeResult> UpgradeAsync(TimeSpan remaining, CancellationToken cancellationToken)
    {
        Calls.Add("upgrade");
        Bounds.Add(remaining);
        return Upgrade(remaining, cancellationToken);
    }

    public void Dispose() => Disposed = true;
}

internal sealed class FakeCapability : IReviewedHelperCapabilityCheck
{
    internal const string Root = @"C:\Program Files\PC Steward";
    internal HelperProtocolCapabilityStatus Status { get; set; } = HelperProtocolCapabilityStatus.Supported;
    internal bool Throw { get; set; }
    internal string? PeerImagePath { get; private set; }
    internal int Calls { get; private set; }

    /// <summary>Uses the supplied protection principal like the Helper-role reader: any unprotected input is a mismatch.</summary>
    public HelperProtocolCapabilityResult Evaluate(UpdateRouteContract route, DeploymentLayoutProtectionCheck requesterCannotModify, string peerImagePath)
    {
        Calls++;
        PeerImagePath = peerImagePath;
        if (Throw) throw new IOException("synthetic");
        if (!requesterCannotModify(Root, ["PCSteward.ElevatedHelper.exe", "deployment.json"]))
            return HelperProtocolCapabilityResult.For(HelperProtocolCapabilityStatus.Mismatch);
        return HelperProtocolCapabilityResult.For(Status);
    }
}

/// <summary>One endpoint run: the helper result and, for exchange runs, the desktop exchange and its interpretation.</summary>
internal sealed record EndpointRun(ReviewedUpdateOperation Operation, ReviewedHelperEndpointResult Result, ReviewedHelperExchange? Exchange)
{
    internal ReviewedUpdateProtocol2Interpretation Interpretation => ReviewedUpdateProtocol2Interpreter.Interpret(Operation,
        Exchange!.Outcome, Exchange.ResponseFrame, EndpointFixture.Nonce, Exchange.Desktop);

    internal FramedUpdateResponse Response
    {
        get
        {
            Assert.AreEqual(ReviewedHelperTransportOutcome.ResponseFrameReceived, Exchange!.Outcome, Result.Diagnostic);
            Assert.IsTrue(ReviewedUpdateWireCodec.TryReadResponse(Exchange.ResponseFrame, Operation.JobId, Operation.Digest, EndpointFixture.Nonce,
                out FramedUpdateResponse? response), "the frame is a valid correlated protocol-2 response");
            return response!;
        }
    }
}

internal sealed class EndpointHarness
{
    internal FakeClock Clock { get; } = new(EndpointFixture.Start);
    internal FakePeer Peer { get; } = new();
    internal FakeItem Item { get; } = new();
    internal FakePort Port { get; }
    internal FakeCapability Capability { get; } = new();
    internal ReviewedHelperEndpointBudgets Budgets { get; set; } = ReviewedHelperEndpointBudgets.Production;
    /// <summary>Time already elapsed since the pipe connected (t0) when the endpoint starts.</summary>
    internal TimeSpan PreElapsed { get; set; }
    internal CancellationTokenSource HelperCancellation { get; } = new();

    internal EndpointHarness() => Port = new(Item);

    /// <summary>The real desktop exchange session against the endpoint over a real pipe pair.</summary>
    internal async Task<EndpointRun> ExchangeAsync(ReviewedUpdateOperation? operation = null, Func<byte[]>? createRequest = null)
    {
        operation ??= EndpointFixture.Operation();
        string name = $"pcsteward-test-{Guid.NewGuid():N}";
        await using NamedPipeServerStream server = Server(name);
        TaskCompletionSource exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        HelperProcessWork owned = new(exit.Task, static () => { });
        Task<ReviewedHelperEndpointResult> helper = RunHelperAsync(name, exit);
        ReviewedUpdateOperation frozen = operation;
        ReviewedHelperExchange exchange = await ReviewedHelperExchangeSession.RunAsync(new Channel(server), owned, () =>
            {
                exit.TrySetResult();
                return true;
            },
            createRequest ?? (() => EndpointFixture.Request(frozen, Clock.GetUtcNow())), EndpointFixture.Desktop, CancellationToken.None)
            .WaitAsync(EndpointFixture.Wait);
        await server.DisposeAsync();
        ReviewedHelperEndpointResult result = await helper.WaitAsync(EndpointFixture.Wait);
        return new(operation, result, exchange);
    }

    /// <summary>A raw desktop end: <paramref name="desktop"/> drives the server; then the helper result and the bytes it wrote.</summary>
    internal async Task<(ReviewedHelperEndpointResult Result, int BytesWritten)> RawAsync(Func<NamedPipeServerStream, Task> desktop, bool readBack = true)
    {
        string name = $"pcsteward-test-{Guid.NewGuid():N}";
        await using NamedPipeServerStream server = Server(name);
        TaskCompletionSource exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<ReviewedHelperEndpointResult> helper = RunHelperAsync(name, exit);
        using (CancellationTokenSource connect = new(TimeSpan.FromSeconds(10)))
            await server.WaitForConnectionAsync(connect.Token);
        await desktop(server);
        ReviewedHelperEndpointResult result = await helper.WaitAsync(EndpointFixture.Wait);
        int written = 0;
        if (readBack && server.IsConnected)
        {
            using CancellationTokenSource read = new(TimeSpan.FromSeconds(10));
            byte[] buffer = new byte[ReviewedUpdateWireCodec.MaximumFrameBytes + 8];
            int count;
            try
            {
                while ((count = await server.ReadAsync(buffer, read.Token)) > 0) written += count;
            }
            catch (IOException)
            {
                // A closed peer may surface as an error rather than end of file; nothing further was readable.
            }
        }
        return (result, written);
    }

    private Task<ReviewedHelperEndpointResult> RunHelperAsync(string name, TaskCompletionSource exit) => Task.Run(async () =>
    {
        try
        {
            await using NamedPipeClientStream client = new(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(10_000);
            long connected = Clock.GetTimestamp() - PreElapsed.Ticks;
            return await ReviewedHelperEndpoint.RunAsync(client, Peer, Port, Capability, Clock, connected, EndpointFixture.Nonce, Budgets,
                HelperCancellation.Token);
        }
        finally
        {
            exit.TrySetResult();
        }
    });

    private static NamedPipeServerStream Server(string name) =>
        new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
            ReviewedUpdateWireCodec.MaximumFrameBytes, ReviewedUpdateWireCodec.MaximumFrameBytes);

    private sealed class Channel(NamedPipeServerStream server) : IReviewedHelperChannel
    {
        public Stream Stream => server;
        public Task WaitForConnectionAsync(CancellationToken cancellationToken) => server.WaitForConnectionAsync(cancellationToken);
        public bool VerifyPeer() => true;
    }
}

/// <summary>Hand-built request frames that the codec would refuse to create.</summary>
internal static class RawRequest
{
    internal static byte[] Json(string payloadBase64, string digest, string nonce, string issuedAt) => Encoding.UTF8.GetBytes(
        "{\"protocolVersion\":\"2\",\"operation\":\"UpdatePackage\",\"operationPayload\":\"" + payloadBase64 + "\",\"operationDigest\":\"" +
        digest + "\",\"nonce\":\"" + nonce + "\",\"issuedAtUtc\":\"" + issuedAt + "\"}");
}
