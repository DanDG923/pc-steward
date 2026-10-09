using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Execution;

namespace PCSteward.Security.FixtureTests;

/// <summary>Task 49: ADR-018 protocol v2, FR-BAT-05/06, AC-V3-05/06 — tamper rejection, ordering, cancel and interruption.</summary>
[TestClass]
public sealed class HelperProtocolV2SecurityTests
{
    private static readonly int[] ElevatedSequences = [2, 3, 4];
    private const string Hash = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

    [TestMethod]
    public void ValidBatchRoundTripsOnlyTheElevatedTailAtItsBatchPositions()
    {
        ApprovedOperationBatch batch = MixedBatch(DateTimeOffset.UtcNow);
        string nonce = ElevatedHelperProtocol.CreateNonce();
        Dictionary<Guid, string> skips = new() { [batch.ElevatedItems[1].Id] = BatchSkipReasons.RunningApplication };

        HelperBatchRequestReceiver receiver = Receive(ElevatedHelperProtocolV2.CreateBatchRequest(batch, skips, nonce, DateTimeOffset.UtcNow), nonce);

        Assert.AreEqual(HelperReceiverState.Complete, receiver.State, receiver.Rejection);
        HelperBatchRequest request = receiver.Request!;
        Assert.AreEqual(batch.Id, request.BatchId);
        CollectionAssert.AreEqual(ElevatedSequences, request.Items.Select(item => item.Sequence).ToArray());
        CollectionAssert.AreEqual(batch.ElevatedItems.Select(job => job.Digest).ToArray(), request.Items.Select(item => item.Job.Digest).ToArray());
        Assert.AreEqual(BatchSkipReasons.RunningApplication, request.Items[1].SkipReason);
        Assert.IsTrue(request.Items.All(item => item.Job.Selection.RunsElevated));
        Assert.IsTrue(ElevatedHelperProtocolV2.CreateBatchRequest(batch, null, nonce, DateTimeOffset.UtcNow).All(frame => frame.Length < 2048), "Frames stay far below the 16 KiB bound.");
    }

    [TestMethod]
    [DataRow("substitute-package", "item-integrity-invalid")]
    [DataRow("substitute-version", "item-integrity-invalid")]
    [DataRow("drop-agreement-hash", "item-integrity-invalid")]
    [DataRow("reorder", "item-out-of-order")]
    [DataRow("append", "item-appended")]
    [DataRow("truncate", "item-truncated")]
    [DataRow("foreign-item", "item-batch-mismatch")]
    [DataRow("rebound-foreign-item", "item-integrity-invalid")]
    [DataRow("forged-elevated-digest", "elevated-digest-mismatch")]
    [DataRow("forged-first-sequence", "item-out-of-order")]
    [DataRow("unelevated-item", "unelevated-item-sent-to-helper")]
    [DataRow("unknown-skip-reason", "item-skip-reason-invalid")]
    [DataRow("unknown-field", "frame-fields-invalid")]
    [DataRow("duplicate-field", "frame-duplicate-field")]
    [DataRow("protocol-one", "frame-protocol-invalid")]
    [DataRow("item-before-header", "frame-kind-unexpected")]
    [DataRow("wrong-nonce", "nonce-mismatch")]
    [DataRow("stale-request", "request-expired")]
    [DataRow("future-request", "request-expired")]
    [DataRow("oversize", "frame-size-invalid")]
    [DataRow("replayed-header", "frame-kind-unexpected")]
    [DataRow("numeric-enum", "item-field-invalid")]
    public void TamperedRequestsAreRejectedAsAWhole(string mutation, string expected)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ApprovedOperationBatch batch = MixedBatch(now);
        string nonce = ElevatedHelperProtocol.CreateNonce();
        List<byte[]> frames = [.. ElevatedHelperProtocolV2.CreateBatchRequest(batch, null, nonce, now)];
        ApprovedOperationBatch other = MixedBatch(now);
        byte[] foreignItem = ElevatedHelperProtocolV2.CreateBatchRequest(other, null, nonce, now)[1];

        switch (mutation)
        {
            case "substitute-package": frames[1] = Edit(frames[1], item => item["packageId"] = "Fabrikam.Other"); break;
            case "substitute-version": frames[2] = Edit(frames[2], item => item["targetVersion"] = "9.9.9"); break;
            case "drop-agreement-hash": frames[1] = Edit(frames[1], item => item["agreementHash"] = null); break;
            case "reorder": (frames[1], frames[2]) = (frames[2], frames[1]); break;
            case "append": frames.Insert(4, Edit(foreignItem, item => { item["batchId"] = batch.Id.ToString("D"); item["sequence"] = 5; })); break;
            case "truncate": frames.RemoveAt(3); break;
            case "foreign-item": frames[1] = foreignItem; break;
            case "rebound-foreign-item": frames[1] = Edit(foreignItem, item => item["batchId"] = batch.Id.ToString("D")); break;
            case "forged-elevated-digest": frames[0] = Edit(frames[0], header => header["elevatedDigest"] = new string('A', 64)); break;
            case "forged-first-sequence": frames[0] = Edit(frames[0], header => header["firstSequence"] = 0); break;
            case "unelevated-item": frames[1] = Edit(frames[1], item => item["runsElevated"] = false); break;
            case "unknown-skip-reason": frames[1] = Edit(frames[1], item => item["skipReason"] = "run-anyway"); break;
            case "unknown-field": frames[1] = Edit(frames[1], item => item["arguments"] = "--override x"); break;
            case "duplicate-field": frames[1] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(frames[1]).Replace("\"packageId\"", "\"packageId\":\"Fabrikam.Other\",\"packageId\"", StringComparison.Ordinal)); break;
            case "protocol-one": frames[1] = Edit(frames[1], item => item["protocolVersion"] = "1"); break;
            case "item-before-header": frames.RemoveAt(0); break;
            case "wrong-nonce": nonce = ElevatedHelperProtocol.CreateNonce(); break;
            case "stale-request": frames[0] = Edit(frames[0], header => header["issuedAtUtc"] = now.AddMinutes(-3).ToString("O")); break;
            case "future-request": frames[0] = Edit(frames[0], header => header["issuedAtUtc"] = now.AddMinutes(1).ToString("O")); break;
            case "oversize": frames[1] = new byte[ElevatedHelperProtocolV2.MaximumFrameBytes + 1]; break;
            case "replayed-header": frames.Insert(1, frames[0]); break;
            case "numeric-enum": frames[1] = Edit(frames[1], item => item["provider"] = "0"); break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        HelperBatchRequestReceiver receiver = Receive(frames, nonce, () => now);

        Assert.AreEqual(HelperReceiverState.Rejected, receiver.State);
        Assert.AreEqual(expected, receiver.Rejection);
        Assert.IsNull(receiver.Request);
    }

    [TestMethod]
    public void ExpiredBatchAndReplayAfterCompletionAreRejected()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ApprovedOperationBatch expired = MixedBatch(now.AddMinutes(-31));
        string nonce = ElevatedHelperProtocol.CreateNonce();
        HelperBatchRequestReceiver expiredReceiver = Receive(ElevatedHelperProtocolV2.CreateBatchRequest(expired, null, nonce, now), nonce, () => now);
        Assert.AreEqual("batch-expired", expiredReceiver.Rejection);

        ApprovedOperationBatch batch = MixedBatch(now);
        IReadOnlyList<byte[]> frames = ElevatedHelperProtocolV2.CreateBatchRequest(batch, null, nonce, now);
        HelperBatchRequestReceiver receiver = Receive(frames, nonce, () => now);
        Assert.AreEqual(HelperReceiverState.Complete, receiver.State);
        Assert.IsFalse(receiver.Accept(frames[0]), "A second request on the same one-shot receiver is a replay.");
        Assert.AreEqual("frame-after-terminal-state", receiver.Rejection);
    }

    [TestMethod]
    public void DesktopCannotBuildARequestThatAddsOrRedirectsWork()
    {
        ApprovedOperationBatch batch = MixedBatch(DateTimeOffset.UtcNow);
        string nonce = ElevatedHelperProtocol.CreateNonce();
        Assert.ThrowsExactly<ArgumentException>(() => ElevatedHelperProtocolV2.CreateBatchRequest(batch, new Dictionary<Guid, string> { [batch.UnelevatedItems[0].Id] = BatchSkipReasons.RunningApplication }, nonce, DateTimeOffset.UtcNow), "skip must name an elevated item");
        Assert.ThrowsExactly<ArgumentException>(() => ElevatedHelperProtocolV2.CreateBatchRequest(batch, new Dictionary<Guid, string> { [batch.ElevatedItems[0].Id] = "force-run" }, nonce, DateTimeOffset.UtcNow));
        ApprovedOperationBatch unelevatedOnly = new OperationBatchReview(Guid.NewGuid(), Guid.NewGuid(), [Upgrade("winget:a", "Vendor.A", UpdateScope.User)]).Confirm(DateTimeOffset.UtcNow);
        Assert.ThrowsExactly<ArgumentException>(() => ElevatedHelperProtocolV2.CreateBatchRequest(unelevatedOnly, null, nonce, DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public void BootstrapAcceptsProtocolTwoAndRejectsOthers()
    {
        string nonce = ElevatedHelperProtocol.CreateNonce();
        string pipe = $"pcsteward-elevated-{Guid.NewGuid():N}";
        Assert.IsTrue(HelperBootstrap.TryParse(["--protocol", "2", "--desktop-pid", "42", "--pipe", pipe, "--nonce", nonce], out HelperBootstrap? bootstrap));
        Assert.AreEqual("2", bootstrap!.ProtocolVersion);
        Assert.IsFalse(HelperBootstrap.TryParse(["--protocol", "3", "--desktop-pid", "42", "--pipe", pipe, "--nonce", nonce], out _));
        Assert.IsFalse(HelperBootstrap.TryParse(["--protocol", "02", "--desktop-pid", "42", "--pipe", pipe, "--nonce", nonce], out _));
    }

    [TestMethod]
    public void ResponseReaderEnforcesBindingOrderAndOutcomeRules()
    {
        Guid batchId = Guid.NewGuid();
        string nonce = ElevatedHelperProtocol.CreateNonce();
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        (int, Guid)[] expected = [(3, first), (4, second)];

        HelperBatchResponseReader Reader() => new(batchId, nonce, expected);
        byte[] Started(Guid job, int sequence, string? frameNonce = null, Guid? batch = null) => ElevatedHelperProtocolV2.CreateItemStarted(batch ?? batchId, frameNonce ?? nonce, sequence, job);
        byte[] Result(Guid job, int sequence, UpdateProviderAttemptStatus status, bool restart = false) => ElevatedHelperProtocolV2.CreateItemResult(batchId, nonce, sequence, job, status, 0, restart, "C:\\Users\\owner\\x token=abc");

        HelperBatchResponseReader valid = Reader();
        Assert.IsTrue(valid.TryAccept(Started(first, 3), out _, out _));
        Assert.IsTrue(valid.TryAccept(Result(first, 3, UpdateProviderAttemptStatus.AwaitingVerification, restart: true), out HelperBatchEvent? result, out _));
        StringAssert.Contains(((HelperItemResultEvent)result!).Diagnostic, "[path]");
        Assert.IsTrue(valid.TryAccept(Result(second, 4, UpdateProviderAttemptStatus.Skipped), out _, out _));
        Assert.IsTrue(valid.TryAccept(ElevatedHelperProtocolV2.CreateBatchComplete(batchId, nonce, 2), out _, out _));
        Assert.IsTrue(valid.IsComplete);

        Assert.IsFalse(Reader().TryAccept(Started(first, 3, ElevatedHelperProtocol.CreateNonce()), out _, out string wrongNonce));
        Assert.AreEqual("response-binding-invalid", wrongNonce);
        Assert.IsFalse(Reader().TryAccept(Started(first, 3, batch: Guid.NewGuid()), out _, out string wrongBatch));
        Assert.AreEqual("response-binding-invalid", wrongBatch);
        Assert.IsFalse(Reader().TryAccept(Started(second, 4), out _, out string outOfOrder));
        Assert.AreEqual("response-item-out-of-order", outOfOrder);
        Assert.IsFalse(Reader().TryAccept(Result(first, 3, UpdateProviderAttemptStatus.AwaitingVerification), out _, out string unstartedSuccess));
        Assert.AreEqual("result-outcome-invalid", unstartedSuccess);
        Assert.IsFalse(Reader().TryAccept(ElevatedHelperProtocolV2.CreateBatchComplete(batchId, nonce, 2), out _, out string earlyComplete));
        Assert.AreEqual("complete-before-all-results", earlyComplete);

        HelperBatchResponseReader startedThenSkipped = Reader();
        Assert.IsTrue(startedThenSkipped.TryAccept(Started(first, 3), out _, out _));
        Assert.IsFalse(startedThenSkipped.TryAccept(Started(first, 3), out _, out string twice));
        Assert.AreEqual("item-started-twice", twice);
        Assert.IsFalse(startedThenSkipped.TryAccept(Result(first, 3, UpdateProviderAttemptStatus.Skipped), out _, out string skippedAfterStart));
        Assert.AreEqual("result-outcome-invalid", skippedAfterStart);
        Assert.IsFalse(startedThenSkipped.TryAccept(Result(first, 3, UpdateProviderAttemptStatus.InstallerFailed, restart: true), out _, out string restartOnFailure));
        Assert.AreEqual("result-outcome-invalid", restartOnFailure);
        Assert.IsTrue(startedThenSkipped.CurrentItemStarted);
        Assert.AreEqual(0, startedThenSkipped.NextIndex);
    }

    [TestMethod]
    public async Task SessionRevalidatesEachItemHonoursSkipsAndCancelsOnlyBetweenItems()
    {
        HelperBatchRequest request = AcceptedRequest(MixedBatch(DateTimeOffset.UtcNow), out string nonce, skipSecond: true);
        FakeRevalidator revalidator = new(job => job.Id != request.Items[2].Job.Id);
        ElevatedHelperBatchSession? session = null;
        FakeExecutor executor = new((job, _) =>
        {
            session!.RequestCancelRemaining();
            return Task.FromResult(new ProviderExitAssessment(UpdateProviderAttemptStatus.AwaitingVerification, true, "ok", 3010));
        });
        session = new(request, nonce, revalidator, executor, runningApplications: new NotRunningDetector());
        List<HelperBatchEvent> events = await RunSessionAsync(session, request, nonce);

        // Item 1 ran to completion although Cancel remaining arrived while it ran; item 2 was withheld by the
        // desktop; item 3 is cancelled before start and never revalidated or executed.
        Assert.HasCount(1, executor.Jobs);
        CollectionAssert.AreEqual(new[] { request.Items[0].Job.Id }, revalidator.Jobs.ToArray());
        HelperItemResultEvent[] results = [.. events.OfType<HelperItemResultEvent>()];
        Assert.AreEqual(UpdateProviderAttemptStatus.AwaitingVerification, results[0].Status);
        Assert.IsTrue(results[0].RestartPending);
        Assert.AreEqual(3010, results[0].ExitCode);
        Assert.AreEqual(UpdateProviderAttemptStatus.Skipped, results[1].Status);
        Assert.AreEqual(UpdateProviderAttemptStatus.Cancelled, results[2].Status);
        Assert.IsInstanceOfType<HelperBatchCompleteEvent>(events[^1]);
    }

    [TestMethod]
    public async Task CancelRemainingDuringAwaitedRevalidationNeverStartsProvider()
    {
        HelperBatchRequest request = AcceptedRequest(MixedBatch(DateTimeOffset.UtcNow), out string nonce);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        DelayedRevalidator revalidator = new(entered, release);
        FakeExecutor executor = new((_, _) => Task.FromResult(new ProviderExitAssessment(UpdateProviderAttemptStatus.AwaitingVerification, false, "ok")));
        ElevatedHelperBatchSession session = new(request, nonce, revalidator, executor, runningApplications: new NotRunningDetector());
        Task<List<HelperBatchEvent>> run = RunSessionAsync(session, request, nonce);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        session.RequestCancelRemaining();
        release.SetResult();
        List<HelperBatchEvent> events = await run.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.HasCount(0, executor.Jobs, "AC-V3-06: the revalidation await is still before provider start.");
        Assert.HasCount(0, events.OfType<HelperItemStartedEvent>().ToArray());
        Assert.HasCount(request.Items.Count, events.OfType<HelperItemResultEvent>().ToArray());
        Assert.IsTrue(events.OfType<HelperItemResultEvent>().All(result => result.Status == UpdateProviderAttemptStatus.Cancelled));
    }

    [TestMethod]
    public async Task CancelRemainingDuringFreshProcessCheckNeverStartsProvider()
    {
        HelperBatchRequest request = AcceptedRequest(MixedBatch(DateTimeOffset.UtcNow), out string nonce);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        DelayedDetector detector = new(entered, release);
        FakeExecutor executor = new((_, _) => Task.FromResult(new ProviderExitAssessment(UpdateProviderAttemptStatus.AwaitingVerification, false, "ok")));
        ElevatedHelperBatchSession session = new(request, nonce, new FakeRevalidator(_ => true), executor, runningApplications: detector);
        Task<List<HelperBatchEvent>> run = RunSessionAsync(session, request, nonce);
        // Before the gate exists the session completes without inspecting processes.
        await Task.WhenAny(entered.Task, run).WaitAsync(TimeSpan.FromSeconds(10));
        session.RequestCancelRemaining();
        release.SetResult();
        List<HelperBatchEvent> events = await run.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.HasCount(0, executor.Jobs, "The awaited process check is still before provider start.");
        Assert.HasCount(0, events.OfType<HelperItemStartedEvent>().ToArray());
        Assert.HasCount(request.Items.Count, events.OfType<HelperItemResultEvent>().ToArray());
        Assert.IsTrue(events.OfType<HelperItemResultEvent>().All(result => result.Status == UpdateProviderAttemptStatus.Cancelled));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancelRemainingBeforeUacNeverInvokesBatchLauncher(bool cancelAtFinalTrustCheck)
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Requires Windows named-pipe security APIs.");
        using CancellationTokenSource cancelRemaining = new();
        int launches = 0;
        int checks = 0;
        System.Diagnostics.Process? Launcher(string helperPath, string pipeName, string launchNonce, out ElevatedHelperResult? failure)
        {
            launches++;
            failure = new(ElevatedHelperOutcome.Failed, null, "unexpected-test-launch");
            return null;
        }
        if (!cancelAtFinalTrustCheck) cancelRemaining.Cancel();
        WindowsElevatedHelperTransport transport = new(
            () => new("C:\\Program Files\\PC Steward\\PCSteward.Desktop.exe", "C:\\Program Files\\PC Steward\\PCSteward.ElevatedHelper.exe"),
            _ =>
            {
                if (++checks == 2 && cancelAtFinalTrustCheck) cancelRemaining.Cancel();
                return new(true, string.Empty);
            },
            Launcher,
            validateProviderPreLaunchTrust: _ => null,
            launchBatchHelper: Launcher);

        ElevatedBatchOutcome result = await transport.ExecuteBatchAsync(MixedBatch(DateTimeOffset.UtcNow), new Dictionary<Guid, string>(), null, cancelRemaining.Token, CancellationToken.None);

        Assert.AreEqual(0, launches, "FR-BAT-06: cancel remaining prevents the runas launcher.");
        Assert.IsTrue(result.Items.All(item => item.Disposition == ElevatedItemDisposition.NotStarted && item.Status == UpdateProviderAttemptStatus.Cancelled));
    }

    [TestMethod]
    public async Task SessionSkipsChangedEvidenceAndContainsProviderFailures()
    {
        HelperBatchRequest request = AcceptedRequest(MixedBatch(DateTimeOffset.UtcNow), out string nonce);
        FakeRevalidator revalidator = new(job => job.Id != request.Items[0].Job.Id);
        FakeExecutor executor = new((job, _) => job.Id == request.Items[1].Job.Id
            ? throw new IOException("launch failed")
            : Task.FromResult(new ProviderExitAssessment(UpdateProviderAttemptStatus.Skipped, false, "provider claims skip")));

        List<HelperBatchEvent> events = await RunSessionAsync(new(request, nonce, revalidator, executor, runningApplications: new NotRunningDetector()), request, nonce);

        HelperItemResultEvent[] results = [.. events.OfType<HelperItemResultEvent>()];
        Assert.AreEqual(UpdateProviderAttemptStatus.Skipped, results[0].Status, "Changed evidence is skipped, never adapted.");
        StringAssert.Contains(results[0].Diagnostic, "helper-revalidation-failed");
        Assert.AreEqual(UpdateProviderAttemptStatus.ProductFailed, results[1].Status);
        Assert.AreEqual(UpdateProviderAttemptStatus.ProductFailed, results[2].Status, "A started item cannot end as Skipped.");
        CollectionAssert.DoesNotContain(executor.Jobs.Select(job => job.Id).ToArray(), request.Items[0].Job.Id);
    }

    [TestMethod]
    public async Task SessionDeadlineTimesOutTheRunningItemAndCancelsTheRest()
    {
        HelperBatchRequest request = AcceptedRequest(MixedBatch(DateTimeOffset.UtcNow), out string nonce);
        FakeExecutor executor = new(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new ProviderExitAssessment(UpdateProviderAttemptStatus.AwaitingVerification, false, "never");
        });

        List<HelperBatchEvent> events = await RunSessionAsync(new(request, nonce, new FakeRevalidator(_ => true), executor, TimeSpan.FromMilliseconds(200), new NotRunningDetector()), request, nonce);

        HelperItemResultEvent[] results = [.. events.OfType<HelperItemResultEvent>()];
        Assert.AreEqual(UpdateProviderAttemptStatus.TimedOut, results[0].Status);
        Assert.IsTrue(results.Skip(1).All(result => result.Status == UpdateProviderAttemptStatus.Cancelled));
        Assert.HasCount(1, executor.Jobs);
    }

    [TestMethod]
    public async Task EndToEndExchangeReportsEveryItemAndCancelRemainingStopsTheNext()
    {
        ApprovedOperationBatch batch = MixedBatch(DateTimeOffset.UtcNow);
        string nonce = ElevatedHelperProtocol.CreateNonce();
        using CancellationTokenSource cancelRemaining = new();
        List<Guid> started = [];
        FakeExecutor executor = new(async (job, _) =>
        {
            await cancelRemaining.CancelAsync();
            await Task.Delay(500, CancellationToken.None);
            return new ProviderExitAssessment(UpdateProviderAttemptStatus.AwaitingVerification, false, "ok", 0);
        });
        (DuplexStream desktop, DuplexStream helper) = DuplexStream.CreatePair();

        Task<int> helperRun = ServeAndCloseAsync(helper, nonce, executor);
        ElevatedBatchOutcome outcome = await WindowsElevatedHelperTransport.ExchangeBatchAsync(desktop, batch, new Dictionary<Guid, string>(), nonce, started.Add, TimeSpan.FromSeconds(30), cancelRemaining.Token, CancellationToken.None);
        desktop.Dispose();

        Assert.AreEqual(0, await helperRun);
        Assert.HasCount(1, executor.Jobs, "Cancel remaining took effect before the second elevated item.");
        CollectionAssert.AreEqual(new[] { batch.ElevatedItems[0].Id }, started.ToArray());
        Assert.AreEqual(ElevatedItemDisposition.Reported, outcome.Items[0].Disposition);
        Assert.AreEqual(UpdateProviderAttemptStatus.AwaitingVerification, outcome.Items[0].Status);
        Assert.IsTrue(outcome.Items.Skip(1).All(item => item.Disposition == ElevatedItemDisposition.Reported && item.Status == UpdateProviderAttemptStatus.Cancelled));
    }

    [TestMethod]
    public async Task InterruptedSessionMarksOnlyThePossiblyRunningItemForVerification()
    {
        ApprovedOperationBatch batch = MixedBatch(DateTimeOffset.UtcNow);
        string nonce = ElevatedHelperProtocol.CreateNonce();
        using CancellationTokenSource abort = new();
        FakeExecutor executor = new(async (_, token) =>
        {
            await abort.CancelAsync();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new ProviderExitAssessment(UpdateProviderAttemptStatus.AwaitingVerification, false, "never");
        });
        (DuplexStream desktop, DuplexStream helper) = DuplexStream.CreatePair();

        Task<int> helperRun = ServeAndCloseAsync(helper, nonce, executor);
        ElevatedBatchOutcome outcome = await WindowsElevatedHelperTransport.ExchangeBatchAsync(desktop, batch, new Dictionary<Guid, string>(), nonce, null, TimeSpan.FromSeconds(30), CancellationToken.None, abort.Token);
        desktop.Dispose();

        Assert.AreEqual(25, await helperRun, "The helper aborts the running provider when the desktop goes away.");
        Assert.AreEqual(ElevatedItemDisposition.Interrupted, outcome.Items[0].Disposition);
        Assert.IsTrue(outcome.Items.Skip(1).All(item => item.Disposition == ElevatedItemDisposition.NotStarted && item.Status == UpdateProviderAttemptStatus.Cancelled));
    }

    [TestMethod]
    public async Task HelperRejectsAMismatchedRequestAndRunsNothing()
    {
        ApprovedOperationBatch batch = MixedBatch(DateTimeOffset.UtcNow);
        FakeExecutor executor = new((_, _) => Task.FromResult(new ProviderExitAssessment(UpdateProviderAttemptStatus.AwaitingVerification, false, "ok")));
        (DuplexStream desktop, DuplexStream helper) = DuplexStream.CreatePair();

        Task<int> helperRun = ServeAndCloseAsync(helper, ElevatedHelperProtocol.CreateNonce(), executor);
        ElevatedBatchOutcome outcome = await WindowsElevatedHelperTransport.ExchangeBatchAsync(desktop, batch, new Dictionary<Guid, string>(), ElevatedHelperProtocol.CreateNonce(), null, TimeSpan.FromSeconds(30), CancellationToken.None, CancellationToken.None);
        int exit = await helperRun;
        desktop.Dispose();

        Assert.AreEqual(24, exit);
        Assert.HasCount(0, executor.Jobs);
        Assert.AreNotEqual(ElevatedItemDisposition.Reported, outcome.Items[0].Disposition);
    }

    /// <summary>The helper process exits after serving, which closes its end of the pipe.</summary>
    private static async Task<int> ServeAndCloseAsync(DuplexStream helper, string nonce, IOperationExecutor executor)
    {
        try
        {
            return await ElevatedHelperEntryPoint.ServeBatchAsync(helper, nonce, new FakeRevalidator(_ => true), executor, runningApplications: new NotRunningDetector());
        }
        finally
        {
            helper.Dispose();
        }
    }

    private static async Task<List<HelperBatchEvent>> RunSessionAsync(ElevatedHelperBatchSession session, HelperBatchRequest request, string nonce)
    {
        List<byte[]> sent = [];
        await session.RunAsync(frame => { sent.Add(frame); return Task.CompletedTask; }, CancellationToken.None);
        HelperBatchResponseReader reader = new(request.BatchId, nonce, [.. request.Items.Select(item => (item.Sequence, item.Job.Id))]);
        List<HelperBatchEvent> events = [];
        foreach (byte[] frame in sent)
        {
            Assert.IsTrue(reader.TryAccept(frame, out HelperBatchEvent? helperEvent, out string rejection), rejection);
            events.Add(helperEvent!);
        }

        Assert.IsTrue(reader.IsComplete);
        return events;
    }

    private static HelperBatchRequest AcceptedRequest(ApprovedOperationBatch batch, out string nonce, bool skipSecond = false)
    {
        nonce = ElevatedHelperProtocol.CreateNonce();
        Dictionary<Guid, string>? skips = skipSecond ? new() { [batch.ElevatedItems[1].Id] = BatchSkipReasons.DesktopRevalidationFailed } : null;
        HelperBatchRequestReceiver receiver = Receive(ElevatedHelperProtocolV2.CreateBatchRequest(batch, skips, nonce, DateTimeOffset.UtcNow), nonce);
        Assert.AreEqual(HelperReceiverState.Complete, receiver.State, receiver.Rejection);
        return receiver.Request!;
    }

    private static HelperBatchRequestReceiver Receive(IEnumerable<byte[]> frames, string nonce, Func<DateTimeOffset>? now = null)
    {
        HelperBatchRequestReceiver receiver = new(nonce, now);
        foreach (byte[] frame in frames)
        {
            if (!receiver.Accept(frame)) break;
        }

        return receiver;
    }

    private static byte[] Edit(byte[] frame, Action<JsonObject> edit)
    {
        JsonObject node = JsonNode.Parse(frame)!.AsObject();
        edit(node);
        return Encoding.UTF8.GetBytes(node.ToJsonString());
    }

    /// <summary>Two user-scope items first, then a self-elevating install, a machine upgrade and a Chocolatey install.</summary>
    private static ApprovedOperationBatch MixedBatch(DateTimeOffset approvedAt) =>
        new OperationBatchReview(Guid.NewGuid(), Guid.NewGuid(),
        [
            UpdateJobSelection.ForInstall("pcs:notepadpp", UpdateProvider.WinGet, UpdateSource.WinGet, "Notepad++.Notepad++", "8.8.3", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "winget-com-v1", Hash, installerElevates: true),
            Upgrade("winget:a", "Vendor.A", UpdateScope.User),
            Upgrade("winget:b", "Vendor.B", UpdateScope.Machine),
            UpdateJobSelection.ForInstall("pcs:firefox", UpdateProvider.Chocolatey, UpdateSource.ChocolateyCommunity, "firefox", "130.0.1", UpdateScope.Machine, UpdateArchitecture.Neutral, UpdateChannel.Stable, "chocolatey-fixed-community-v1", null, true),
            UpdateJobSelection.ForInstall("pcs:git", UpdateProvider.WinGet, UpdateSource.WinGet, "Git.Git", "2.55.0", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "winget-com-v1", null, installerElevates: false)
        ]).Confirm(approvedAt);

    private static UpdateJobSelection Upgrade(string key, string packageId, UpdateScope scope) =>
        new(key, UpdateProvider.WinGet, UpdateSource.WinGet, packageId, "1.0.0", "1.1.0", scope, UpdateArchitecture.X64, UpdateChannel.Stable, "mapping-v1");

    private sealed class NotRunningDetector : IRunningApplicationDetector
    {
        public Task<RunningApplicationDetection> DetectAsync(UpdateJobSelection selection, CancellationToken cancellationToken) =>
            Task.FromResult(RunningApplicationDetection.NotDetected);
    }

    private sealed class DelayedDetector(TaskCompletionSource entered, TaskCompletionSource release) : IRunningApplicationDetector
    {
        public async Task<RunningApplicationDetection> DetectAsync(UpdateJobSelection selection, CancellationToken cancellationToken)
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return RunningApplicationDetection.NotDetected;
        }
    }

    private sealed class DelayedRevalidator(TaskCompletionSource entered, TaskCompletionSource release) : IOperationRevalidator
    {
        public async Task<UpdateJobExecutionEvidence?> RevalidateAsync(ImmutableUpdateJob job, CancellationToken cancellationToken)
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return new(job.Selection, true, true);
        }
    }

    private sealed class FakeRevalidator(Func<ImmutableUpdateJob, bool> unchanged) : IOperationRevalidator
    {
        public List<Guid> Jobs { get; } = [];

        public Task<UpdateJobExecutionEvidence?> RevalidateAsync(ImmutableUpdateJob job, CancellationToken cancellationToken)
        {
            Jobs.Add(job.Id);
            UpdateJobSelection selection = unchanged(job)
                ? job.Selection
                : UpdateJobSelection.Restore(job.Selection.Operation, job.Selection.StableProductKey, job.Selection.Provider, job.Selection.Source, job.Selection.PackageId, job.Selection.InstalledVersion, "9.9.9", job.Selection.Scope, job.Selection.Architecture, job.Selection.Channel, job.Selection.MappingEvidenceVersion, job.Selection.AgreementHash, job.Selection.RunsElevated);
            return Task.FromResult<UpdateJobExecutionEvidence?>(new(selection, true, true));
        }
    }

    private sealed class FakeExecutor(Func<ImmutableUpdateJob, CancellationToken, Task<ProviderExitAssessment>> run) : IOperationExecutor
    {
        public List<ImmutableUpdateJob> Jobs { get; } = [];

        public Task<ProviderExitAssessment> ExecuteAsync(ImmutableUpdateJob job, UpdateJobExecutionEvidence freshEvidence, CancellationToken cancellationToken)
        {
            Jobs.Add(job);
            Assert.IsTrue(job.Matches(freshEvidence));
            return run(job, cancellationToken);
        }
    }

    /// <summary>An in-memory duplex byte stream standing in for the authenticated named pipe.</summary>
    private sealed class DuplexStream(Channel<byte[]> incoming, Channel<byte[]> outgoing) : Stream
    {
        private byte[] pending = [];
        private int offset;

        public static (DuplexStream Desktop, DuplexStream Helper) CreatePair()
        {
            Channel<byte[]> toHelper = Channel.CreateUnbounded<byte[]>();
            Channel<byte[]> toDesktop = Channel.CreateUnbounded<byte[]>();
            return (new(toDesktop, toHelper), new(toHelper, toDesktop));
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => outgoing.Writer.TryWrite(buffer.AsSpan(offset, count).ToArray());

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!outgoing.Writer.TryWrite(buffer.ToArray())) throw new IOException("The pipe is closed.");
            return ValueTask.CompletedTask;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (offset >= pending.Length)
            {
                if (!await incoming.Reader.WaitToReadAsync(cancellationToken)) return 0;
                if (!incoming.Reader.TryRead(out byte[]? next)) return 0;
                pending = next;
                offset = 0;
            }

            int count = Math.Min(buffer.Length, pending.Length - offset);
            pending.AsMemory(offset, count).CopyTo(buffer);
            offset += count;
            return count;
        }

        protected override void Dispose(bool disposing)
        {
            outgoing.Writer.TryComplete();
            incoming.Writer.TryComplete();
            base.Dispose(disposing);
        }
    }
}

