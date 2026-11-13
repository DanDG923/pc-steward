using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// FR-DIA-01/03 (ADR-027 Q1, Task 64): the collection orchestrator with synthetic readers and a manual clock. Covers one-shot
/// consent, app history first, equal OS record slots, per-reader time shares and the overall deadline, saturation, rejected
/// records counting against the limit, denied/unsupported/failed/all-denied sources, the byte backstop, redaction before
/// keeping, and owner cancellation discarding the attempt.
/// </summary>
[TestClass]
public sealed class DiagnosticCollectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly DiagnosticSourceKind[] AllSources = Enum.GetValues<DiagnosticSourceKind>();

    [TestMethod]
    [TestCategory("FR-DIA-01")]
    public async Task ConsentIsSpentByOneCollectionAndNeverReused()
    {
        ManualTimeProvider clock = new(Now);
        DiagnosticCollectionOrchestrator orchestrator = new(Readers(clock), DiagnosticRedactionContext.None, clock);
        DiagnosticCollectionConsent consent = Consent();

        DiagnosticCollectionResult first = await orchestrator.CollectAsync(consent, CancellationToken.None);
        DiagnosticCollectionResult second = await orchestrator.CollectAsync(consent, CancellationToken.None);
        DiagnosticCollectionResult stale = await orchestrator.CollectAsync(new(AllSources, TimeSpan.FromDays(7), DiagnosticCollectionPolicy.Version + 1), CancellationToken.None);

        Assert.AreEqual(DiagnosticCollectionStatus.Completed, first.Status);
        Assert.IsTrue(consent.IsSpent);
        Assert.AreEqual(DiagnosticCollectionStatus.ConsentRejected, second.Status);
        Assert.IsNull(second.Snapshot);
        Assert.AreEqual(DiagnosticCollectionStatus.ConsentRejected, stale.Status, "consent for another policy version is not consent for this one");
        Assert.ThrowsExactly<ArgumentException>(() => new DiagnosticCollectionConsent([], TimeSpan.FromDays(1), DiagnosticCollectionPolicy.Version));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new DiagnosticCollectionConsent(AllSources, TimeSpan.FromDays(8), DiagnosticCollectionPolicy.Version));
    }

    [TestMethod]
    [TestCategory("FR-DIA-01")]
    [DataRow(0, 500)]
    [DataRow(120, 470)]
    [DataRow(500, 375)]
    public async Task AppHistoryRunsFirstAndOperatingSystemSourcesGetEqualSlots(int appRecords, int expectedSlots)
    {
        ManualTimeProvider clock = new(Now);
        List<DiagnosticReadRequest> requests = [];
        FakeReader[] readers = Readers(clock, requests);
        readers[0].Records = [.. Enumerable.Range(0, appRecords).Select(index => AppCandidate($"job-{index}", Now.AddMinutes(-index)))];

        DiagnosticSnapshot snapshot = (await new DiagnosticCollectionOrchestrator(readers, DiagnosticRedactionContext.None, clock).CollectAsync(Consent(), CancellationToken.None)).Snapshot!;

        CollectionAssert.AreEqual(AllSources, requests.Select(request => request.Source).ToArray(), "PC Steward history first, then the closed OS order");
        Assert.AreEqual(500, requests[0].MaximumMatchingRecords);
        Assert.IsTrue(requests.Skip(1).All(request => request.MaximumMatchingRecords == expectedSlots));
        Assert.IsTrue(requests.All(request => request.FromUtc == Now.AddDays(-7) && request.ToUtc == Now));
        CollectionAssert.AreEqual(AllSources, snapshot.Sources.Select(source => source.Source).ToArray(), "every source reports separately");
        Assert.AreEqual(Now.AddDays(30), snapshot.ExpiresAtUtc);
        Assert.AreEqual(Now.AddDays(-7), snapshot.LookbackFromUtc);
        Assert.AreEqual(DiagnosticCollectionPolicy.Version, snapshot.PolicyVersion);
        Assert.HasCount(appRecords, snapshot.Records);
    }

    [TestMethod]
    [TestCategory("FR-DIA-03")]
    public async Task SaturatedSourcesAreCutToTheirSlotsAndMarkedPartial()
    {
        ManualTimeProvider clock = new(Now);
        FakeReader[] readers = Readers(clock);
        readers[0].Records = [.. Enumerable.Range(0, 500).Select(index => AppCandidate($"job-{index}", Now.AddMinutes(-index)))];
        readers[1].Records = [.. Enumerable.Range(0, 600).Select(index => Os(DiagnosticSourceKind.SystemEventLog, $"System:{index}", DiagnosticEventKind.UnexpectedShutdown))];
        readers[2].Records = [.. Enumerable.Range(0, 375).Select(index => Os(DiagnosticSourceKind.ApplicationEventLog, $"Application:{index}", DiagnosticEventKind.ApplicationCrash))];
        readers[2].MoreMatchingRecordsExist = true;

        DiagnosticSnapshot snapshot = (await new DiagnosticCollectionOrchestrator(readers, DiagnosticRedactionContext.None, clock).CollectAsync(Consent(), CancellationToken.None)).Snapshot!;

        DiagnosticSourceResult system = snapshot.Sources[1];
        Assert.AreEqual(DiagnosticSourceOutcome.Partial, system.Outcome);
        Assert.AreEqual(DiagnosticSourceReason.SourceRecordLimit, system.Reason);
        Assert.AreEqual(375, system.MatchingRecordsReturned);
        Assert.AreEqual(375, system.RecordsKept);
        Assert.AreEqual(DiagnosticSourceReason.SourceRecordLimit, snapshot.Sources[2].Reason, "a reader saying more records matched is disclosed");
        Assert.AreEqual(DiagnosticSourceOutcome.Available, snapshot.Sources[0].Outcome);
        Assert.AreEqual(DiagnosticSourceOutcome.Empty, snapshot.Sources[3].Outcome);
        Assert.HasCount(1250, snapshot.Records);
        Assert.IsLessThanOrEqualTo(DiagnosticCollectionPolicy.MaximumRecordsOverall, snapshot.Records.Count);
    }

    [TestMethod]
    [TestCategory("FR-DIA-03")]
    public async Task RejectedRecordsCountAgainstTheMatchingLimitAndAreDisclosed()
    {
        ManualTimeProvider clock = new(Now);
        FakeReader[] readers = Readers(clock);
        readers[1].Records =
        [
            .. Enumerable.Range(0, 400).Select(index => index % 4 == 0
                ? Os(DiagnosticSourceKind.SystemEventLog, $"System:{index}", DiagnosticEventKind.ApplicationCrash)
                : Os(DiagnosticSourceKind.SystemEventLog, $"System:{index}", DiagnosticEventKind.UnexpectedShutdown))
        ];
        readers[3].Records = [Os(DiagnosticSourceKind.SystemEventLog, "System:9", DiagnosticEventKind.UnexpectedShutdown)];

        DiagnosticSnapshot snapshot = (await new DiagnosticCollectionOrchestrator(readers, DiagnosticRedactionContext.None, clock).CollectAsync(Consent(), CancellationToken.None)).Snapshot!;

        DiagnosticSourceResult system = snapshot.Sources[1];
        Assert.AreEqual(400, system.MatchingRecordsReturned);
        Assert.AreEqual(100, system.RecordsRejected);
        Assert.AreEqual(300, system.RecordsKept);
        Assert.AreEqual(DiagnosticSourceOutcome.Partial, system.Outcome);
        Assert.AreEqual(DiagnosticSourceReason.InvalidRecordsRejected, system.Reason);
        Assert.AreEqual(1, snapshot.Sources[3].RecordsRejected, "a record claiming another source is rejected");
        Assert.IsFalse(snapshot.Records.Any(record => record.Source == DiagnosticSourceKind.Reliability));
    }

    [TestMethod]
    [TestCategory("FR-DIA-01")]
    public async Task DeniedUnsupportedFailedAndUnselectedSourcesStayVisibleAndOthersContinue()
    {
        ManualTimeProvider clock = new(Now);
        FakeReader[] readers = Readers(clock);
        readers[1].Status = DiagnosticReaderStatus.Denied;
        readers[2].Throw = new UnauthorizedAccessException("synthetic");
        readers[3].Throw = new InvalidOperationException("synthetic reader fault");
        readers[4].Records = [Os(DiagnosticSourceKind.WindowsUpdateHistory, "wu-1", DiagnosticEventKind.WindowsUpdate)];
        DiagnosticCollectionOrchestrator orchestrator = new(readers.Where(reader => reader.Source != DiagnosticSourceKind.PcStewardHistory), DiagnosticRedactionContext.None, clock);

        DiagnosticSnapshot snapshot = (await orchestrator.CollectAsync(Consent(), CancellationToken.None)).Snapshot!;
        DiagnosticSnapshot narrowed = (await new DiagnosticCollectionOrchestrator(Readers(clock), DiagnosticRedactionContext.None, clock)
            .CollectAsync(new([DiagnosticSourceKind.PcStewardHistory], TimeSpan.FromDays(2), DiagnosticCollectionPolicy.Version), CancellationToken.None)).Snapshot!;

        Assert.AreEqual(DiagnosticSourceOutcome.Unsupported, snapshot.Sources[0].Outcome, "a missing reader is unsupported, not empty");
        Assert.AreEqual(DiagnosticSourceOutcome.Denied, snapshot.Sources[1].Outcome);
        Assert.AreEqual(DiagnosticSourceOutcome.Denied, snapshot.Sources[2].Outcome);
        Assert.AreEqual(DiagnosticSourceOutcome.Failed, snapshot.Sources[3].Outcome);
        Assert.AreEqual(DiagnosticSourceReason.ReaderFault, snapshot.Sources[3].Reason);
        Assert.AreEqual(DiagnosticSourceOutcome.Available, snapshot.Sources[4].Outcome);
        Assert.IsTrue(snapshot.HasCoverage);
        Assert.IsTrue(snapshot.IsPartial);
        Assert.IsTrue(narrowed.Sources.Skip(1).All(source => source.Outcome == DiagnosticSourceOutcome.NotSelected));
        Assert.AreEqual(Now.AddDays(-2), narrowed.LookbackFromUtc);
    }

    [TestMethod]
    [TestCategory("FR-DIA-01")]
    public async Task AllDeniedIsNoCoverageNotAHealthyResult()
    {
        ManualTimeProvider clock = new(Now);
        FakeReader[] readers = Readers(clock);
        foreach (FakeReader reader in readers) reader.Status = DiagnosticReaderStatus.Denied;

        DiagnosticSnapshot snapshot = (await new DiagnosticCollectionOrchestrator(readers, DiagnosticRedactionContext.None, clock).CollectAsync(Consent(), CancellationToken.None)).Snapshot!;

        Assert.IsFalse(snapshot.HasCoverage);
        Assert.IsTrue(snapshot.Sources.All(source => source.Outcome == DiagnosticSourceOutcome.Denied));
        Assert.IsEmpty(snapshot.Records);
    }

    [TestMethod]
    [TestCategory("FR-DIA-03")]
    public async Task SlowReadersTimeOutAndLaterReadersShareTheRemainingTime()
    {
        ManualTimeProvider clock = new(Now);
        FakeReader[] readers = Readers(clock);
        readers[0].Consumes = TimeSpan.FromSeconds(16);   // over its 15 s ceiling: timed out at 15 s
        readers[1].Consumes = TimeSpan.FromSeconds(9);    // share = (55 - 16) / 4 = 9.75 s: completes
        readers[2].Consumes = TimeSpan.FromSeconds(10);   // share = (55 - 25) / 3 = 10 s: reaches its deadline
        readers[3].Consumes = TimeSpan.FromSeconds(1);    // share = (55 - 35) / 2 = 10 s: completes
        readers[1].Records = [Os(DiagnosticSourceKind.SystemEventLog, "System:1", DiagnosticEventKind.BugCheck)];
        readers[2].Records = [Os(DiagnosticSourceKind.ApplicationEventLog, "Application:1", DiagnosticEventKind.ApplicationHang)];

        DiagnosticSnapshot snapshot = (await new DiagnosticCollectionOrchestrator(readers, DiagnosticRedactionContext.None, clock).CollectAsync(Consent(), CancellationToken.None)).Snapshot!;

        Assert.AreEqual(DiagnosticSourceOutcome.TimedOut, snapshot.Sources[0].Outcome);
        Assert.AreEqual(DiagnosticSourceReason.ReaderDeadline, snapshot.Sources[0].Reason);
        Assert.AreEqual(500, readers[1].LastRequest!.MaximumMatchingRecords, "a timed-out app reader returned no records, so OS sources get 500");
        Assert.AreEqual(DiagnosticSourceOutcome.Available, snapshot.Sources[1].Outcome);
        Assert.AreEqual(DiagnosticSourceOutcome.TimedOut, snapshot.Sources[2].Outcome);
        Assert.IsFalse(snapshot.Records.Any(record => record.Source == DiagnosticSourceKind.ApplicationEventLog), "records from a timed-out reader are discarded");
        Assert.AreEqual(DiagnosticSourceOutcome.Empty, snapshot.Sources[3].Outcome);
        Assert.AreEqual(DiagnosticSourceOutcome.Empty, snapshot.Sources[4].Outcome);
    }

    [TestMethod]
    [TestCategory("FR-DIA-03")]
    public async Task AReaderThatBlocksPastTheOverallDeadlineLeavesLaterSourcesTimedOutWithoutStarting()
    {
        ManualTimeProvider clock = new(Now);
        FakeReader[] readers = Readers(clock);
        readers[1].Consumes = TimeSpan.FromSeconds(60);
        readers[1].IgnoresCancellation = true;

        DiagnosticSnapshot snapshot = (await new DiagnosticCollectionOrchestrator(readers, DiagnosticRedactionContext.None, clock).CollectAsync(Consent(), CancellationToken.None)).Snapshot!;

        Assert.AreEqual(DiagnosticSourceOutcome.TimedOut, snapshot.Sources[1].Outcome);
        Assert.IsTrue(snapshot.Sources.Skip(2).All(source => source.Outcome == DiagnosticSourceOutcome.TimedOut && source.Reason == DiagnosticSourceReason.CollectionDeadline));
        Assert.IsTrue(readers.Skip(2).All(reader => reader.LastRequest is null), "no later reader starts after the overall deadline");
    }

    [TestMethod]
    [TestCategory("FR-DIA-03")]
    public async Task AReaderThatBlocksSynchronouslyIsAbandonedAtItsDeadline()
    {
        ManualTimeProvider clock = new(Now);
        FakeReader[] readers = Readers(clock);
        using ManualResetEventSlim release = new(false);
        using ManualResetEventSlim started = new(false);
        readers[2].BlockSynchronously = (started, release);

        try
        {
            Task<DiagnosticCollectionResult> collection = new DiagnosticCollectionOrchestrator(readers, DiagnosticRedactionContext.None, clock).CollectAsync(Consent(), CancellationToken.None);
            Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(10)), "the blocking reader was started");
            clock.Advance(TimeSpan.FromSeconds(16));
            DiagnosticSnapshot snapshot = (await collection.WaitAsync(TimeSpan.FromSeconds(10))).Snapshot!;

            Assert.AreEqual(DiagnosticSourceOutcome.TimedOut, snapshot.Sources[2].Outcome);
            Assert.AreEqual(DiagnosticSourceReason.ReaderDeadline, snapshot.Sources[2].Reason);
            Assert.AreEqual(DiagnosticSourceOutcome.Empty, snapshot.Sources[3].Outcome, "later readers still run in the remaining time");
            Assert.IsFalse(release.IsSet, "the collection finished while the reader was still blocked");
        }
        finally
        {
            release.Set();
        }
    }

    [TestMethod]
    [TestCategory("FR-DIA-03")]
    public async Task AHangingReaderIsAbandonedAtItsDeadline()
    {
        ManualTimeProvider clock = new(Now);
        FakeReader[] readers = Readers(clock);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        readers[4].Hang = started;

        Task<DiagnosticCollectionResult> collection = new DiagnosticCollectionOrchestrator(readers, DiagnosticRedactionContext.None, clock).CollectAsync(Consent(), CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        clock.Advance(TimeSpan.FromSeconds(16));
        DiagnosticSnapshot snapshot = (await collection.WaitAsync(TimeSpan.FromSeconds(10))).Snapshot!;

        Assert.AreEqual(DiagnosticSourceOutcome.TimedOut, snapshot.Sources[4].Outcome);
        Assert.AreEqual(DiagnosticSourceOutcome.Empty, snapshot.Sources[0].Outcome);
    }

    [TestMethod]
    [TestCategory("FR-DIA-01")]
    public async Task OwnerCancellationDiscardsTheWholeAttempt()
    {
        ManualTimeProvider clock = new(Now);
        FakeReader[] readers = Readers(clock);
        using CancellationTokenSource owner = new();
        readers[0].Records = [AppCandidate("job-1", Now.AddHours(-1))];
        readers[2].OnRead = owner.Cancel;

        DiagnosticCollectionResult result = await new DiagnosticCollectionOrchestrator(readers, DiagnosticRedactionContext.None, clock).CollectAsync(Consent(), owner.Token);
        using CancellationTokenSource already = new();
        await already.CancelAsync();
        DiagnosticCollectionResult before = await new DiagnosticCollectionOrchestrator(Readers(clock), DiagnosticRedactionContext.None, clock).CollectAsync(Consent(), already.Token);

        Assert.AreEqual(DiagnosticCollectionStatus.Cancelled, result.Status);
        Assert.IsNull(result.Snapshot, "cancel changes nothing: no partial snapshot is returned for saving");
        Assert.IsNull(readers[3].LastRequest);
        Assert.AreEqual(DiagnosticCollectionStatus.Cancelled, before.Status);
    }

    [TestMethod]
    [TestCategory("FR-DIA-03")]
    public async Task TheByteBackstopStopsAcceptingAndDisclosesTheLimit()
    {
        ManualTimeProvider clock = new(Now);
        FakeReader[] readers = Readers(clock);
        readers[0].Records = [.. Enumerable.Range(0, 10).Select(index => AppCandidate($"job-{index}", Now.AddMinutes(-index)))];
        readers[1].Records = [Os(DiagnosticSourceKind.SystemEventLog, "System:1", DiagnosticEventKind.BugCheck)];
        long oneRecord = DiagnosticRecordJson.ByteCount(DiagnosticRecordAdmission.Admit(AppCandidate("job-0", Now)).Record!) + 1;

        DiagnosticSnapshot snapshot = (await new DiagnosticCollectionOrchestrator(readers, DiagnosticRedactionContext.None, clock, null, (oneRecord * 3) + 2)
            .CollectAsync(Consent(), CancellationToken.None)).Snapshot!;

        Assert.AreEqual(DiagnosticSourceOutcome.Partial, snapshot.Sources[0].Outcome);
        Assert.AreEqual(DiagnosticSourceReason.CollectionByteLimit, snapshot.Sources[0].Reason);
        Assert.AreEqual(3, snapshot.Sources[0].RecordsKept);
        Assert.AreEqual(DiagnosticSourceReason.CollectionByteLimit, snapshot.Sources[1].Reason);
        Assert.IsLessThanOrEqualTo((oneRecord * 3) + 2, snapshot.PayloadBytes);
    }

    [TestMethod]
    [TestCategory("FR-DIA-03")]
    public async Task RecordsAreRedactedBeforeTheyAreKept()
    {
        ManualTimeProvider clock = new(Now);
        FakeReader[] readers = Readers(clock);
        readers[2].Records = [Os(DiagnosticSourceKind.ApplicationEventLog, "Application:1", DiagnosticEventKind.ApplicationCrash) with { Subject = @"widget.exe on DESKTOP-SYN123 in C:\Users\alice\widget.exe" }];

        DiagnosticSnapshot snapshot = (await new DiagnosticCollectionOrchestrator(readers, new(["DESKTOP-SYN123", "alice"]), clock).CollectAsync(Consent(), CancellationToken.None)).Snapshot!;

        Assert.AreEqual("widget.exe on [redacted] in [path]", snapshot.Records.Single().Subject);
    }

    [TestMethod]
    [TestCategory("FR-DIA-01")]
    public void EachSourceHasAtMostOneReader()
    {
        ManualTimeProvider clock = new(Now);
        FakeReader[] readers = Readers(clock);
        Assert.ThrowsExactly<ArgumentException>(() => new DiagnosticCollectionOrchestrator([readers[1], new FakeReader(DiagnosticSourceKind.SystemEventLog, clock, null)], DiagnosticRedactionContext.None, clock));
    }

    private static DiagnosticCollectionConsent Consent() => new(AllSources, DiagnosticCollectionPolicy.Lookback, DiagnosticCollectionPolicy.Version);

    private static FakeReader[] Readers(ManualTimeProvider clock, List<DiagnosticReadRequest>? requests = null) =>
        [.. AllSources.Select(source => new FakeReader(source, clock, requests))];

    private static DiagnosticRecordCandidate AppCandidate(string id, DateTimeOffset at) =>
        new(DiagnosticSourceKind.PcStewardHistory, id, DiagnosticTimeNormalizer.FromInstant(at, Now), DiagnosticEventKind.PcStewardUpdate, DiagnosticOutcomeKind.Succeeded, Subject: "Contoso Widget", Detail: "1.0 to 1.1");

    private static DiagnosticRecordCandidate Os(DiagnosticSourceKind source, string id, DiagnosticEventKind kind) =>
        new(source, id, DiagnosticTimeNormalizer.FromInstant(Now.AddHours(-2), Now), kind, DiagnosticOutcomeKind.NotApplicable, EventId: 41);

    private sealed class FakeReader(DiagnosticSourceKind source, ManualTimeProvider clock, List<DiagnosticReadRequest>? requests) : IDiagnosticSourceReader
    {
        public DiagnosticSourceKind Source => source;
        public IReadOnlyList<DiagnosticRecordCandidate> Records { get; set; } = [];
        public bool MoreMatchingRecordsExist { get; set; }
        public DiagnosticReaderStatus Status { get; set; } = DiagnosticReaderStatus.Completed;
        public Exception? Throw { get; set; }
        public TimeSpan Consumes { get; set; }
        public bool IgnoresCancellation { get; set; }
        public TaskCompletionSource? Hang { get; set; }
        public (ManualResetEventSlim Started, ManualResetEventSlim Release)? BlockSynchronously { get; set; }
        public Action? OnRead { get; set; }
        public DiagnosticReadRequest? LastRequest { get; private set; }

        public async Task<DiagnosticReaderResult> ReadAsync(DiagnosticReadRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            requests?.Add(request);
            OnRead?.Invoke();
            if (BlockSynchronously is { } block)
            {
                block.Started.Set();
                block.Release.Wait(TimeSpan.FromSeconds(30), CancellationToken.None);
            }
            if (Hang is not null)
            {
                Hang.TrySetResult();
                await Task.Delay(Timeout.Infinite, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
            }
            if (Consumes > TimeSpan.Zero) clock.Advance(Consumes);
            if (!IgnoresCancellation) cancellationToken.ThrowIfCancellationRequested();
            if (Throw is not null) throw Throw;
            return new(Status, Status == DiagnosticReaderStatus.Completed ? [.. Records.Take(request.MaximumMatchingRecords + 100)] : [], MoreMatchingRecordsExist);
        }
    }

    /// <summary>A manual monotonic clock whose timers fire only when the test advances it.</summary>
    internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private readonly object sync = new();
        private readonly List<ManualTimer> timers = [];
        private DateTimeOffset now = start;
        private long ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() { lock (sync) return now; }

        public override long GetTimestamp() { lock (sync) return ticks; }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ManualTimer timer = new(this, callback, state);
            lock (sync) timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(TimeSpan by)
        {
            lock (sync) { now += by; ticks += by.Ticks; }
            while (true)
            {
                ManualTimer? due;
                lock (sync) due = timers.Where(timer => timer.DueTicks is long at && at <= ticks).OrderBy(timer => timer.DueTicks).FirstOrDefault();
                if (due is null) return;
                due.Fire();
            }
        }

        internal long Ticks { get { lock (sync) return ticks; } }

        internal void Remove(ManualTimer timer) { lock (sync) timers.Remove(timer); }

        internal sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            private long period;
            public long? DueTicks { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                DueTicks = dueTime == Timeout.InfiniteTimeSpan ? null : owner.Ticks + Math.Max(0, dueTime.Ticks);
                this.period = period == Timeout.InfiniteTimeSpan ? 0 : period.Ticks;
                return true;
            }

            public void Fire()
            {
                DueTicks = period > 0 ? DueTicks + period : null;
                callback(state);
            }

            public void Dispose() { DueTicks = null; owner.Remove(this); }

            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
