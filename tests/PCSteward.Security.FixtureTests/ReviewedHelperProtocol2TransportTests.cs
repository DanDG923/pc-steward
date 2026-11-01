using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Execution;
using Status = PCSteward.Execution.HelperProtocolCapabilityStatus;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01, TM-03/04/07/13: the protocol-2 exchange session, the Windows transport's pre-launch order and
/// binding lifetime (adapter gates §A.4 step 3, A-M2, review-16 L2) and the Chocolatey runtime-limits route dispatcher
/// (§A.2/§A.4 step 1, reviewed fact extension rev 3 §4.1/§6). Exchange-session tests use a real named-pipe pair and run on
/// every platform; Windows-transport tests that need the restricted pipe or a held binding are Windows-only.
/// </summary>
[TestClass]
public sealed class ReviewedHelperProtocol2TransportTests
{
    private static readonly string Nonce = Protocol2InterpreterFixture.Nonce;
    private static readonly ReviewedHelperBudgets Fast = new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));

    // ---- Exchange session (portable) ----

    [TestMethod]
    public async Task OneFrameThenEndOfFileAndObservedExitIsAReceivedResponseWithH()
    {
        ReviewedUpdateOperation operation = Protocol2InterpreterFixture.Operation();
        byte[] response = Protocol2InterpreterFixture.Frame(operation, Protocol2InterpreterFixture.Started(), Protocol2InterpreterFixture.Matched());
        await using Harness harness = new();
        Task helper = harness.RunHelper(async client =>
        {
            harness.ReceivedRequest = await PipeMessageFraming.ReadAsync(client, CancellationToken.None);
            await PipeMessageFraming.WriteAsync(client, response, CancellationToken.None);
        }, exitAfter: true);

        ReviewedHelperExchange exchange = await harness.RunAsync(operation, Fast, CancellationToken.None);
        await helper;

        Assert.AreEqual(ReviewedHelperTransportOutcome.ResponseFrameReceived, exchange.Outcome);
        CollectionAssert.AreEqual(response, exchange.ResponseFrame);
        Assert.IsTrue(exchange.Desktop.H);
        Assert.IsTrue(exchange.Desktop.OwnedIoComplete && exchange.Desktop.PipeEndOfFile);
        Assert.IsTrue(exchange.Desktop.OwnedRootSnapshot.IsCompletedSuccessfully);
        Assert.AreEqual(0, harness.Terminations);
        Assert.AreEqual(1, harness.Disposals, "ownership completes exactly once after exit and seal");
        Assert.IsTrue(ReviewedUpdateWireCodec.TryReadRequest(harness.ReceivedRequest!, out FramedUpdateRequest? request));
        CollectionAssert.AreEqual(operation.GetCanonicalBytes(), request!.GetPayload());
        Assert.AreEqual(operation.Digest, request.Digest);
        Assert.AreEqual(Nonce, request.Nonce);
        ReviewedUpdateProtocol2Interpretation interpreted = Interpret(operation, exchange);
        Assert.AreEqual(ReviewedHelperCandidateCheckKind.Confirmed, interpreted.CandidateCheck.Kind);
        Assert.IsTrue(Protocol2InterpreterFixture.IsSettled(interpreted.Observation));
    }

    [TestMethod]
    [DataRow("trailing-byte")]
    [DataRow("second-frame")]
    [DataRow("closed-without-response")]
    [DataRow("oversized-length")]
    [DataRow("truncated-frame")]
    public async Task AnythingOtherThanExactlyOneFrameThenEndOfFileIsRequestWrittenWithoutValidResponse(string behaviour)
    {
        ReviewedUpdateOperation operation = Protocol2InterpreterFixture.Operation();
        byte[] response = Protocol2InterpreterFixture.Frame(operation, Protocol2InterpreterFixture.Started(), Protocol2InterpreterFixture.Matched());
        await using Harness harness = new();
        Task helper = harness.RunHelper(async client =>
        {
            harness.ReceivedRequest = await PipeMessageFraming.ReadAsync(client, CancellationToken.None);
            switch (behaviour)
            {
                case "trailing-byte":
                    await PipeMessageFraming.WriteAsync(client, response, CancellationToken.None);
                    await client.WriteAsync(new byte[] { 0 });
                    break;
                case "second-frame":
                    await PipeMessageFraming.WriteAsync(client, response, CancellationToken.None);
                    await PipeMessageFraming.WriteAsync(client, response, CancellationToken.None);
                    break;
                case "oversized-length":
                    byte[] header = new byte[4];
                    BinaryPrimitives.WriteInt32LittleEndian(header, ReviewedUpdateWireCodec.MaximumFrameBytes + 1);
                    await client.WriteAsync(header);
                    break;
                case "truncated-frame":
                    byte[] partial = new byte[4];
                    BinaryPrimitives.WriteInt32LittleEndian(partial, response.Length);
                    await client.WriteAsync(partial);
                    await client.WriteAsync(response.AsMemory(0, response.Length / 2));
                    break;
            }
        }, exitAfter: true);

        ReviewedHelperExchange exchange = await harness.RunAsync(operation, Fast, CancellationToken.None);
        await helper;

        Assert.AreEqual(ReviewedHelperTransportOutcome.RequestWrittenNoValidResponse, exchange.Outcome, behaviour);
        Assert.AreEqual(0, exchange.ResponseFrame.Length);
        Assert.IsNotNull(harness.ReceivedRequest, "the request was written");
        Assert.AreEqual(1, harness.Terminations);
        Protocol2InterpreterFixture.AssertRejected(Interpret(operation, exchange));
    }

    [TestMethod]
    public async Task ExecutionBudgetLapseAfterWritingTerminatesAndIsRequestWrittenWithoutValidResponse()
    {
        ReviewedUpdateOperation operation = Protocol2InterpreterFixture.Operation();
        await using Harness harness = new();
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task helper = harness.RunHelper(async client =>
        {
            harness.ReceivedRequest = await PipeMessageFraming.ReadAsync(client, CancellationToken.None);
            await release.Task;
        });

        ReviewedHelperExchange exchange = await harness.RunAsync(operation, Fast with { Execution = TimeSpan.FromMilliseconds(300) }, CancellationToken.None);
        release.SetResult();
        await helper;

        Assert.AreEqual(ReviewedHelperTransportOutcome.RequestWrittenNoValidResponse, exchange.Outcome);
        Assert.AreEqual(1, harness.Terminations);
        Assert.IsTrue(exchange.Desktop.HelperExitObserved, "termination ended the helper");
        Assert.IsFalse(exchange.Desktop.OwnedIoComplete || exchange.Desktop.PipeEndOfFile);
        Protocol2InterpreterFixture.AssertRejected(Interpret(operation, exchange));
    }

    [TestMethod]
    public async Task ValidFrameWithoutObservedHelperExitLeavesHFalseSoTheInterpreterRejectsIt()
    {
        ReviewedUpdateOperation operation = Protocol2InterpreterFixture.Operation();
        byte[] response = Protocol2InterpreterFixture.Frame(operation, Protocol2InterpreterFixture.Started(), Protocol2InterpreterFixture.Matched());
        await using Harness harness = new() { TerminateCompletesExit = false };
        Task helper = harness.RunHelper(async client =>
        {
            harness.ReceivedRequest = await PipeMessageFraming.ReadAsync(client, CancellationToken.None);
            await PipeMessageFraming.WriteAsync(client, response, CancellationToken.None);
        });

        ReviewedHelperExchange exchange = await harness.RunAsync(operation, Fast with { Exit = TimeSpan.FromMilliseconds(300) }, CancellationToken.None);
        await helper;

        Assert.AreEqual(ReviewedHelperTransportOutcome.ResponseFrameReceived, exchange.Outcome);
        Assert.IsFalse(exchange.Desktop.HelperExitObserved);
        Assert.IsFalse(exchange.Desktop.H);
        Assert.AreEqual(1, harness.Terminations, "a missing exit is torn down");
        Assert.AreEqual(0, harness.Disposals, "ownership never completes without an observed exit");
        Assert.IsFalse(exchange.Desktop.OwnedRootSnapshot.IsCompleted);
        Protocol2InterpreterFixture.AssertRejected(Interpret(operation, exchange));
    }

    [TestMethod]
    [DataRow("peer-refused")]
    [DataRow("peer-throws")]
    [DataRow("cancelled-after-peer-check")]
    [DataRow("request-build-throws")]
    public async Task NothingIsWrittenWhenThePeerCheckFailsOrTheCallerCancelsBeforeWriting(string scenario)
    {
        ReviewedUpdateOperation operation = Protocol2InterpreterFixture.Operation();
        using CancellationTokenSource cancellation = new();
        await using Harness harness = new();
        harness.Channel.Verify = scenario switch
        {
            "peer-refused" => () => false,
            "peer-throws" => () => throw new InvalidOperationException("synthetic"),
            "cancelled-after-peer-check" => () => { cancellation.Cancel(); return true; },
            _ => () => true,
        };
        if (scenario == "request-build-throws") harness.CreateRequest = () => throw new ArgumentException("synthetic");
        int bytesSeenByHelper = -1;
        Task helper = harness.RunHelper(async client =>
        {
            await harness.Exit.Task;
            bytesSeenByHelper = await client.ReadAsync(new byte[1]);
        });

        ReviewedHelperExchange exchange = await harness.RunAsync(operation, Fast, cancellation.Token);
        await harness.DisposeServerAsync();
        await helper;

        Assert.AreEqual(ReviewedHelperTransportOutcome.HelperStartedRequestNotWritten, exchange.Outcome, scenario);
        Assert.AreEqual(1, harness.Channel.VerifyCalls);
        Assert.AreEqual(0, bytesSeenByHelper, "the helper saw end of file: not one request byte was written");
        Assert.AreEqual(1, harness.Terminations);
        Assert.IsTrue(exchange.Desktop.H);
        ReviewedUpdateDispatchObservation observation = Interpret(operation, exchange).Observation;
        Assert.AreEqual(ReviewedUpdateInvocation.NotStarted, observation.Invocation);
        Assert.AreEqual(ReviewedUpdateProtocol2Interpreter.RequestNotWrittenDiagnostic, observation.Diagnostic);
        Assert.IsTrue(Protocol2InterpreterFixture.IsSettled(observation));
    }

    [TestMethod]
    public async Task HelperExitBeforeConnectionNeverVerifiesOrWritesAndSettlesAsNotStarted()
    {
        ReviewedUpdateOperation operation = Protocol2InterpreterFixture.Operation();
        await using Harness harness = new();
        harness.Exit.SetResult();

        ReviewedHelperExchange exchange = await harness.RunAsync(operation, Fast, CancellationToken.None);

        Assert.AreEqual(ReviewedHelperTransportOutcome.HelperStartedRequestNotWritten, exchange.Outcome);
        Assert.AreEqual(0, harness.Channel.VerifyCalls);
        Assert.IsTrue(exchange.Desktop.H);
        Assert.AreEqual(ReviewedUpdateInvocation.NotStarted, Interpret(operation, exchange).Observation.Invocation);
    }

    [TestMethod]
    public async Task ConnectionTimeoutTerminatesTheHelperAndWritesNothing()
    {
        ReviewedUpdateOperation operation = Protocol2InterpreterFixture.Operation();
        await using Harness harness = new();

        ReviewedHelperExchange exchange = await harness.RunAsync(operation, Fast with { Connection = TimeSpan.FromMilliseconds(200) }, CancellationToken.None);

        Assert.AreEqual(ReviewedHelperTransportOutcome.HelperStartedRequestNotWritten, exchange.Outcome);
        Assert.AreEqual(1, harness.Terminations);
        Assert.AreEqual(0, harness.Channel.VerifyCalls);
        Assert.IsTrue(exchange.Desktop.HelperExitObserved);
    }

    [TestMethod]
    public async Task TerminationFailureWithoutObservedExitIsUncertainEvenThoughNothingWasWritten()
    {
        ReviewedUpdateOperation operation = Protocol2InterpreterFixture.Operation();
        await using Harness harness = new() { TerminateSucceeds = false, TerminateCompletesExit = false };

        ReviewedHelperExchange exchange = await harness.RunAsync(operation,
            Fast with { Connection = TimeSpan.FromMilliseconds(200), Exit = TimeSpan.FromMilliseconds(200) }, CancellationToken.None);

        Assert.AreEqual(ReviewedHelperTransportOutcome.HelperStartedRequestNotWritten, exchange.Outcome);
        Assert.IsTrue(exchange.Desktop.TerminationFailed);
        Assert.IsFalse(exchange.Desktop.HelperExitObserved);
        Protocol2InterpreterFixture.AssertRejected(Interpret(operation, exchange));
    }

    // ---- Windows transport ----

    [TestMethod]
    public async Task OffRouteOperationIsRefusedBeforeAnyCapabilityPathOrLaunchWork()
    {
        int resolves = 0, launches = 0;
        CountingCapability capability = new(_ => HelperProtocolCapabilityResult.For(Status.Supported));
        WindowsReviewedHelperTransport transport = new(capability, () => { resolves++; return null; }, _ => new(true, string.Empty),
            () => true, (string _, string _, string _, out bool declined) => { launches++; declined = false; return null; }, Fast);

        foreach (ReviewedUpdateOperation operation in new[] { WinGetOperation(),
            Protocol2InterpreterFixture.Operation(route: UpdateRouteContract.ChocolateyMachineProvisionalV3) })
        {
            ReviewedHelperExchange exchange = await transport.ExchangeAsync(operation, Nonce, CancellationToken.None);
            Assert.AreEqual(ReviewedHelperTransportOutcome.LaunchRefusedBeforeElevation, exchange.Outcome);
            Assert.AreEqual(0, exchange.ResponseFrame.Length);
        }
        Assert.AreEqual(0, resolves + launches + capability.EvaluateCalls + capability.HoldCalls);
    }

    [TestMethod]
    [DataRow("caller-untrusted")]
    [DataRow("paths-unavailable")]
    [DataRow("initial-trust-rejected")]
    [DataRow("hold-refused")]
    [DataRow("hold-throws")]
    public async Task PreHoldFailuresRefuseWithoutLaunching(string scenario)
    {
        RequireWindows("restricted pipe");
        using DeploymentCapabilityReaderTests.Fixture fixture = new();
        DeploymentCapabilityReaderTests.DeploymentTree tree = DeploymentCapabilityReaderTests.DeploymentTree.Create(fixture);
        HelperLaunchPaths paths = Paths(tree);
        int validations = 0, launches = 0;
        CountingCapability capability = new(_ => scenario == "hold-throws"
            ? throw new IOException("synthetic")
            : HelperProtocolCapabilityResult.For(Status.Mismatch));
        WindowsReviewedHelperTransport transport = new(capability, () => scenario == "paths-unavailable" ? null : paths,
            _ => { validations++; return new(scenario != "initial-trust-rejected", "synthetic"); },
            () => scenario != "caller-untrusted",
            (string _, string _, string _, out bool declined) => { launches++; declined = false; return null; }, Fast);

        ReviewedHelperExchange exchange = await transport.ExchangeAsync(Protocol2InterpreterFixture.Operation(), Nonce, CancellationToken.None);

        Assert.AreEqual(ReviewedHelperTransportOutcome.LaunchRefusedBeforeElevation, exchange.Outcome, scenario);
        Assert.AreEqual(0, launches);
        Assert.AreEqual(scenario is "hold-refused" or "hold-throws" ? 1 : 0, capability.HoldCalls, scenario);
        Assert.IsTrue(validations <= 1, "final trust never runs without a held binding");
    }

    [TestMethod]
    [DataRow("final-trust-rejected", "LaunchRefusedBeforeElevation", 0)]
    [DataRow("helper-path-differs", "LaunchRefusedBeforeElevation", 0)]
    [DataRow("cancelled-before-launch", "LaunchRefusedBeforeElevation", 0)]
    [DataRow("uac-declined", "ElevationDeclined", 1)]
    [DataRow("no-process", "LaunchRefusedBeforeElevation", 1)]
    [DataRow("launcher-throws", "LaunchRefusedBeforeElevation", 1)]
    public async Task HeldBindingIsReleasedWhenNoHelperProcessExists(string scenario, string expectedOutcome, int expectedLaunches)
    {
        ReviewedHelperTransportOutcome expected = Enum.Parse<ReviewedHelperTransportOutcome>(expectedOutcome);
        RequireWindows("restricted pipe and held binding");
        using DeploymentCapabilityReaderTests.Fixture fixture = new();
        DeploymentCapabilityReaderTests.DeploymentTree tree = DeploymentCapabilityReaderTests.DeploymentTree.Create(fixture);
        HelperLaunchPaths paths = Paths(tree);
        if (scenario == "helper-path-differs") paths = paths with { HelperPath = tree.Leaf("PCSteward.Desktop.dll") };
        CapturingCapability capability = new(tree);
        using CancellationTokenSource cancellation = new();
        int validations = 0, launches = 0;
        WindowsReviewedHelperTransport transport = new(capability, () => paths,
            _ =>
            {
                validations++;
                if (validations == 2 && scenario == "cancelled-before-launch") cancellation.Cancel();
                return new(!(validations == 2 && scenario == "final-trust-rejected"), "synthetic");
            },
            () => true,
            (string helperPath, string pipeName, string nonce, out bool declined) =>
            {
                launches++;
                // Launch happens only after the hold and the final trust check, with the binding still held.
                Assert.AreEqual(2, validations);
                Assert.IsTrue(capability.Binding!.IsHolding);
                Assert.AreEqual(paths.HelperPath, helperPath);
                StringAssert.StartsWith(pipeName, WindowsReviewedHelperTransport.PipeNamePrefix);
                Assert.AreEqual(Nonce, nonce);
                declined = scenario == "uac-declined";
                return scenario == "launcher-throws" ? throw new InvalidOperationException("synthetic") : null;
            }, Fast);

        ReviewedHelperExchange exchange = await transport.ExchangeAsync(Protocol2InterpreterFixture.Operation(), Nonce, cancellation.Token);

        Assert.AreEqual(expected, exchange.Outcome, scenario);
        Assert.AreEqual(expectedLaunches, launches, scenario);
        Assert.AreEqual(1, capability.HoldCalls);
        Assert.IsNotNull(capability.Binding);
        Assert.IsTrue(capability.Binding.IsDisposed, "no helper process exists, so the binding is released");
        ReviewedUpdateDispatchObservation observation = Interpret(Protocol2InterpreterFixture.Operation(), exchange).Observation;
        Assert.AreEqual(ReviewedUpdateInvocation.NotStarted, observation.Invocation);
        Assert.AreEqual(expected == ReviewedHelperTransportOutcome.ElevationDeclined
            ? ReviewedUpdateProviderStatus.ElevationDenied : ReviewedUpdateProviderStatus.Failed, observation.Status);
    }

    [TestMethod]
    public async Task HeldBindingStaysHeldWhileTheHelperRunsAndIsReleasedOnlyAfterExitAndSeal()
    {
        RequireWindows("restricted pipe, held binding and a real child process");
        using DeploymentCapabilityReaderTests.Fixture fixture = new();
        DeploymentCapabilityReaderTests.DeploymentTree tree = DeploymentCapabilityReaderTests.DeploymentTree.Create(fixture);
        HelperLaunchPaths paths = Paths(tree);
        CapturingCapability capability = new(tree);
        bool heldAtLaunch = false;
        WindowsReviewedHelperTransport transport = new(capability, () => paths, _ => new(true, string.Empty), () => true,
            (string _, string _, string _, out bool declined) =>
            {
                declined = false;
                heldAtLaunch = capability.Binding!.IsHolding;
                // A stand-in child that never connects and exits on its own: the exchange must not write anything.
                ProcessStartInfo start = new(Path.Combine(Environment.SystemDirectory, "cmd.exe")) { UseShellExecute = false, CreateNoWindow = true };
                start.ArgumentList.Add("/c");
                start.ArgumentList.Add("exit 0");
                return Process.Start(start);
            }, Fast);

        ReviewedHelperExchange exchange = await transport.ExchangeAsync(Protocol2InterpreterFixture.Operation(), Nonce, CancellationToken.None);

        Assert.IsTrue(heldAtLaunch);
        Assert.AreEqual(ReviewedHelperTransportOutcome.HelperStartedRequestNotWritten, exchange.Outcome);
        Assert.IsTrue(exchange.Desktop.H);
        await exchange.Desktop.OwnedRootSnapshot.WaitAsync(TimeSpan.FromSeconds(10));
        for (int attempt = 0; attempt < 100 && !capability.Binding!.IsDisposed; attempt++) await Task.Delay(50);
        Assert.IsTrue(capability.Binding!.IsDisposed, "released after the ownership task completed");
        Assert.AreEqual(ReviewedUpdateInvocation.NotStarted, Interpret(Protocol2InterpreterFixture.Operation(), exchange).Observation.Invocation);
    }

    // ---- Route dispatcher (portable) ----

    [TestMethod]
    public void SupportsChecksTheRouteAndEvidenceBeforeAnyCapabilityRead()
    {
        CountingCapability capability = new(_ => HelperProtocolCapabilityResult.For(Status.Supported));
        ChocolateyRuntimeLimitsRouteDispatcher dispatcher = new(capability, new ScriptedTransport(_ => throw new AssertFailedException("no dispatch")));

        Assert.IsFalse(dispatcher.Supports(WinGetOperation()));
        Assert.IsFalse(dispatcher.Supports(Protocol2InterpreterFixture.Operation(route: UpdateRouteContract.ChocolateyMachineProvisionalV3)));
        Assert.IsFalse(dispatcher.Supports(null!));
        Assert.AreEqual(0, capability.EvaluateCalls);
        Assert.IsTrue(dispatcher.Supports(Protocol2InterpreterFixture.Operation()));
        Assert.AreEqual(1, capability.EvaluateCalls);
        Assert.AreEqual(UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3, capability.LastRoute);
    }

    [TestMethod]
    [DataRow("Absent")]
    [DataRow("Incomplete")]
    [DataRow("Unreadable")]
    [DataRow("Mismatch")]
    public void SupportsIsFalseForEveryNonSupportedCapabilityStatus(string statusName)
    {
        Status status = Enum.Parse<Status>(statusName);
        ChocolateyRuntimeLimitsRouteDispatcher dispatcher = new(new CountingCapability(_ => HelperProtocolCapabilityResult.For(status)),
            new ScriptedTransport(_ => throw new AssertFailedException("no dispatch")));
        Assert.IsFalse(dispatcher.Supports(Protocol2InterpreterFixture.Operation()));
    }

    [TestMethod]
    public void SupportsNeverThrows()
    {
        ChocolateyRuntimeLimitsRouteDispatcher dispatcher = new(new CountingCapability(_ => throw new UnauthorizedAccessException("synthetic")),
            new ScriptedTransport(_ => throw new AssertFailedException("no dispatch")));
        Assert.IsFalse(dispatcher.Supports(Protocol2InterpreterFixture.Operation()));
    }

    [TestMethod]
    public async Task DispatchRunsOneExchangeWithAFreshNonceAndReturnsTheInterpretedWorkflowObservation()
    {
        ReviewedUpdateOperation operation = Protocol2InterpreterFixture.Operation();
        byte[] frame = Protocol2InterpreterFixture.Frame(operation, Protocol2InterpreterFixture.Started(), Protocol2InterpreterFixture.Matched());
        ScriptedTransport transport = new(_ => new(ReviewedHelperTransportOutcome.ResponseFrameReceived, frame, Protocol2InterpreterFixture.SettledDesktop));
        ChocolateyRuntimeLimitsRouteDispatcher dispatcher = new(new CountingCapability(_ => HelperProtocolCapabilityResult.For(Status.Supported)),
            transport, () => Nonce);

        ReviewedUpdateDispatchObservation observation = await dispatcher.DispatchAsync(await Ticket(operation), CancellationToken.None);

        Assert.AreEqual(1, transport.Calls);
        Assert.AreSame(operation, transport.LastOperation);
        Assert.AreEqual(Nonce, transport.LastNonce);
        ReviewedUpdateDispatchObservation expected = Protocol2InterpreterFixture.Interpret(operation, frame).WorkflowObservation;
        Assert.AreEqual(expected.Invocation, observation.Invocation);
        Assert.AreEqual(expected.Status, observation.Status);
        Assert.AreEqual(expected.ExitCode, observation.ExitCode);
        Assert.AreEqual(expected.Diagnostic, observation.Diagnostic);
        Assert.AreEqual(RecordedHelperCheckKind.Confirmed, observation.HelperCheck!.Kind);
        Assert.IsTrue(Protocol2InterpreterFixture.IsSettled(observation));
    }

    [TestMethod]
    public async Task AFrameCorrelatedToAnotherNonceIsRejected()
    {
        ReviewedUpdateOperation operation = Protocol2InterpreterFixture.Operation();
        byte[] frame = Protocol2InterpreterFixture.Frame(operation, Protocol2InterpreterFixture.Started(), Protocol2InterpreterFixture.Matched());
        ChocolateyRuntimeLimitsRouteDispatcher dispatcher = new(new CountingCapability(_ => HelperProtocolCapabilityResult.For(Status.Supported)),
            new ScriptedTransport(_ => new(ReviewedHelperTransportOutcome.ResponseFrameReceived, frame, Protocol2InterpreterFixture.SettledDesktop)),
            () => new string('E', 64));

        ReviewedUpdateDispatchObservation observation = await dispatcher.DispatchAsync(await Ticket(operation), CancellationToken.None);

        Assert.AreEqual(ReviewedUpdateInvocation.Unknown, observation.Invocation);
        Assert.IsTrue(observation.ExecutionUncertain);
        Assert.AreEqual(RecordedHelperCheckKind.NotObserved, observation.HelperCheck!.Kind);
    }

    [TestMethod]
    [DataRow("LaunchRefusedBeforeElevation", ReviewedUpdateInvocation.NotStarted, ReviewedUpdateProviderStatus.Failed)]
    [DataRow("ElevationDeclined", ReviewedUpdateInvocation.NotStarted, ReviewedUpdateProviderStatus.ElevationDenied)]
    [DataRow("RequestWrittenNoValidResponse", ReviewedUpdateInvocation.Unknown, ReviewedUpdateProviderStatus.Failed)]
    public async Task PreRequestAndLostResponseOutcomesMapThroughTheInterpreter(string outcomeName,
        ReviewedUpdateInvocation invocation, ReviewedUpdateProviderStatus status)
    {
        ReviewedHelperTransportOutcome outcome = Enum.Parse<ReviewedHelperTransportOutcome>(outcomeName);
        ReviewedHelperDesktopObservation quiet = new(Task.CompletedTask, false, true, false, false, false);
        ChocolateyRuntimeLimitsRouteDispatcher dispatcher = new(new CountingCapability(_ => HelperProtocolCapabilityResult.For(Status.Supported)),
            new ScriptedTransport(_ => new(outcome, [], quiet)), () => Nonce);

        ReviewedUpdateDispatchObservation observation = await dispatcher.DispatchAsync(await Ticket(Protocol2InterpreterFixture.Operation()), CancellationToken.None);

        Assert.AreEqual(invocation, observation.Invocation);
        Assert.AreEqual(status, observation.Status);
        Assert.AreEqual(invocation == ReviewedUpdateInvocation.Unknown, observation.ExecutionUncertain);
    }

    [TestMethod]
    public async Task DispatchOfAnOffRouteTicketStartsNothing()
    {
        ScriptedTransport transport = new(_ => throw new AssertFailedException("no exchange"));
        ChocolateyRuntimeLimitsRouteDispatcher dispatcher = new(new CountingCapability(_ => HelperProtocolCapabilityResult.For(Status.Supported)), transport);

        ReviewedUpdateDispatchObservation observation = await dispatcher.DispatchAsync(await Ticket(WinGetOperation()), CancellationToken.None);

        Assert.AreEqual(ReviewedUpdateInvocation.NotStarted, observation.Invocation);
        Assert.AreEqual(ChocolateyRuntimeLimitsRouteDispatcher.RouteUnsupportedDiagnostic, observation.Diagnostic);
        Assert.AreEqual(0, transport.Calls);
    }

    // ---- Helpers ----

    private static void RequireWindows(string evidence)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException($"Windows-native test ({evidence}): it runs on Windows CI and is reported as failed on other platforms.");
    }

    private static HelperLaunchPaths Paths(DeploymentCapabilityReaderTests.DeploymentTree tree) =>
        new(tree.Leaf(InstalledHelperLaunchTrust.DesktopExecutableName), tree.Leaf(InstalledHelperLaunchTrust.HelperExecutableName));

    private static ReviewedUpdateProtocol2Interpretation Interpret(ReviewedUpdateOperation operation, ReviewedHelperExchange exchange) =>
        ReviewedUpdateProtocol2Interpreter.Interpret(operation, exchange.Outcome, exchange.ResponseFrame, Nonce, exchange.Desktop);

    private static ReviewedUpdateOperation WinGetOperation()
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new("winget:contoso.widget", UpdateProvider.WinGet, UpdateSource.WinGet,
            "Contoso.Widget", Protocol2InterpreterFixture.Installed, Protocol2InterpreterFixture.Candidate, UpdateScope.User, UpdateArchitecture.X64,
            UpdateChannel.Stable, "mapping-v1")).Confirm(Protocol2InterpreterFixture.ReviewedAt);
        return new(job, Protocol2InterpreterFixture.User, "native:1", "context:1", "choco-profile-v2:" + Protocol2InterpreterFixture.Fingerprint,
            Protocol2InterpreterFixture.ReviewedAt.AddSeconds(-1), UpdateRouteContract.WinGetUserExactV3, ReviewedInstallerMode.Selected(InstallerMode.Silent));
    }

    /// <summary>A ticket over a passive claim for the frozen operation (the ticket constructor is visible to this test assembly).</summary>
    private static Task<ReviewedUpdateDispatchTicket> Ticket(ReviewedUpdateOperation operation)
    {
        UpdateDispatchClaim claim = new(Guid.NewGuid(), Guid.NewGuid(), new ReviewedUpdateDraft(operation, Guid.NewGuid()),
            new FreshUpdateDispatchEvidence(operation.Selection, operation.RequestingUserSid, operation.NativeInstanceId,
                operation.InstallationContextId, "fresh:1", operation.ReviewedAt, operation.RouteContract, operation.Mode, true, true),
            operation.ReviewedAt);
        return Task.FromResult(new ReviewedUpdateDispatchTicket(claim));
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly string name = $"pcsteward-test-{Guid.NewGuid():N}";
        private readonly NamedPipeServerStream server;
        private int disposals;

        internal Harness()
        {
            server = new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            Channel = new(server);
            Owned = new(Exit.Task, () => Interlocked.Increment(ref disposals));
        }

        internal TaskCompletionSource Exit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal HelperProcessWork Owned { get; }
        internal TestChannel Channel { get; }
        internal byte[]? ReceivedRequest { get; set; }
        internal Func<byte[]>? CreateRequest { get; set; }
        internal bool TerminateSucceeds { get; init; } = true;
        internal bool TerminateCompletesExit { get; init; } = true;
        internal int Terminations { get; private set; }
        internal int Disposals => Volatile.Read(ref disposals);

        internal Task RunHelper(Func<NamedPipeClientStream, Task> behaviour, bool exitAfter = false) => Task.Run(async () =>
        {
            using (NamedPipeClientStream client = new(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                await client.ConnectAsync(10_000);
                await behaviour(client);
            }
            if (exitAfter) Exit.TrySetResult();
        });

        internal Task<ReviewedHelperExchange> RunAsync(ReviewedUpdateOperation operation, ReviewedHelperBudgets budgets, CancellationToken token) =>
            ReviewedHelperExchangeSession.RunAsync(Channel, Owned, Terminate,
                CreateRequest ?? (() => ReviewedUpdateWireCodec.CreateRequest(operation.GetCanonicalBytes(), operation.Digest, Nonce, DateTimeOffset.UtcNow)),
                budgets, token);

        internal async Task DisposeServerAsync() => await server.DisposeAsync();

        private bool Terminate()
        {
            Terminations++;
            if (TerminateCompletesExit) Exit.TrySetResult();
            return TerminateSucceeds;
        }

        public async ValueTask DisposeAsync()
        {
            Exit.TrySetResult();
            await server.DisposeAsync();
        }
    }

    private sealed class TestChannel(NamedPipeServerStream server) : IReviewedHelperChannel
    {
        internal Func<bool> Verify { get; set; } = () => true;
        internal int VerifyCalls { get; private set; }
        public Stream Stream => server;
        public Task WaitForConnectionAsync(CancellationToken cancellationToken) => server.WaitForConnectionAsync(cancellationToken);
        public bool VerifyPeer()
        {
            VerifyCalls++;
            return Verify();
        }
    }

    private sealed class CountingCapability(Func<UpdateRouteContract, HelperProtocolCapabilityResult> result) : IHelperProtocolCapability
    {
        internal int EvaluateCalls { get; private set; }
        internal int HoldCalls { get; private set; }
        internal UpdateRouteContract? LastRoute { get; private set; }

        public HelperProtocolCapabilityResult Evaluate(UpdateRouteContract route)
        {
            EvaluateCalls++;
            LastRoute = route;
            return result(route);
        }

        public HelperProtocolCapabilityResult TryHold(UpdateRouteContract route, out HeldHelperBinding? binding)
        {
            HoldCalls++;
            binding = null;
            return result(route);
        }
    }

    /// <summary>The real Windows reader over a fixture tree, recording the binding it hands out.</summary>
    private sealed class CapturingCapability(DeploymentCapabilityReaderTests.DeploymentTree tree) : IHelperProtocolCapability
    {
        private readonly OwnerOnlyDeploymentCapabilityReader reader = new(() => tree.Layout, (_, _) => true);
        internal HeldHelperBinding? Binding { get; private set; }
        internal int HoldCalls { get; private set; }

        public HelperProtocolCapabilityResult Evaluate(UpdateRouteContract route) => reader.Evaluate(route);

        public HelperProtocolCapabilityResult TryHold(UpdateRouteContract route, out HeldHelperBinding? binding)
        {
            HoldCalls++;
            HelperProtocolCapabilityResult result = reader.TryHold(route, out binding);
            Assert.AreEqual(Status.Supported, result.Status, "fixture tree must be supported");
            Binding = binding;
            return result;
        }
    }

    private sealed class ScriptedTransport(Func<ReviewedUpdateOperation, ReviewedHelperExchange> exchange) : IReviewedHelperTransport
    {
        internal int Calls { get; private set; }
        internal ReviewedUpdateOperation? LastOperation { get; private set; }
        internal string? LastNonce { get; private set; }

        public Task<ReviewedHelperExchange> ExchangeAsync(ReviewedUpdateOperation operation, string nonce, CancellationToken cancellationToken)
        {
            Calls++;
            LastOperation = operation;
            LastNonce = nonce;
            return Task.FromResult(exchange(operation));
        }
    }
}
