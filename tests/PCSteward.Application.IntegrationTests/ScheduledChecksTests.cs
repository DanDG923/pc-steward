using System.Diagnostics;
using Microsoft.Data.Sqlite;
using PCSteward.Persistence.Sqlite;
using PCSteward.Application;
using PCSteward.Scheduling.Windows;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class ScheduledChecksTests
{
    private static readonly string[] DeleteOnly = ["/Delete"];
    [TestMethod]
    public async Task RegistrationServiceCreatesOnlyEnabledDailyOrWeeklyTasksAndRemovesManualMode()
    {
        RecordingRegistrar registrar = new();
        ScheduledCheckRegistrationService service = new(registrar);

        await service.ApplyAsync(new ScheduledCheckSettings(ScheduleCadence.Weekly, true, true, DayOfWeek.Friday, new TimeOnly(18, 30)), CancellationToken.None);
        Assert.AreEqual(1, registrar.Registered.Count);
        Assert.AreEqual(ScheduleCadence.Weekly, registrar.Registered[0].Cadence);
        Assert.AreEqual(DayOfWeek.Friday, registrar.Registered[0].WeeklyDay);

        await service.ApplyAsync(ScheduledCheckSettings.Manual with { NotificationsEnabled = true }, CancellationToken.None);
        Assert.AreEqual(1, registrar.EnsuredAbsent);
    }

    [TestMethod]
    public void NotificationPolicyAllowsAttentionOrRepeatedFailureOnly()
    {
        ScheduledCheckSettings enabled = ScheduledCheckSettings.Manual with { NotificationsEnabled = true };
        Assert.IsTrue(ScheduledCheckNotificationPolicy.ShouldNotify(enabled, true, true, 0));
        Assert.IsFalse(ScheduledCheckNotificationPolicy.ShouldNotify(enabled, false, false, 1));
        Assert.IsTrue(ScheduledCheckNotificationPolicy.ShouldNotify(enabled, false, false, 2));
        Assert.IsFalse(ScheduledCheckNotificationPolicy.ShouldNotify(enabled with { NotificationsEnabled = false }, true, false, 4));
    }

    [TestMethod]
    [Description("Review F-24 (FR-SCH-04, UJ-09 step 3): ready updates count as attention and the notification gives the counts.")]
    public void AScheduledCheckCountsReadyUpdatesAsAttention()
    {
        static PCSteward.Domain.UpdateIntelligenceSummary Summary(params PCSteward.Domain.UpdateIntelligenceStatus[] statuses) =>
            new([.. statuses.Select((status, index) => new PCSteward.Domain.UpdateIntelligenceItem($"product:{index}", status, null, "Explanation."))],
                new Dictionary<PCSteward.Domain.UpdateIntelligenceStatus, int>(), []);
        const PCSteward.Domain.UpdateIntelligenceStatus Ready = PCSteward.Domain.UpdateIntelligenceStatus.VerifiedUpdate,
            Review = PCSteward.Domain.UpdateIntelligenceStatus.ReviewRequired, Unavailable = PCSteward.Domain.UpdateIntelligenceStatus.FailedUnavailable;

        ScheduledCheckFindings nothing = ScheduledCheckFindings.From(Summary(PCSteward.Domain.UpdateIntelligenceStatus.Current,
            PCSteward.Domain.UpdateIntelligenceStatus.Pinned, PCSteward.Domain.UpdateIntelligenceStatus.Ignored, PCSteward.Domain.UpdateIntelligenceStatus.Skipped));
        Assert.IsFalse(nothing.AttentionRequired, "current, pinned, ignored and skipped apps never notify");
        Assert.AreEqual("Completed", nothing.Result);
        Assert.AreEqual(string.Empty, nothing.NotificationText);

        ScheduledCheckFindings one = ScheduledCheckFindings.From(Summary(Ready));
        Assert.IsTrue(one.AttentionRequired, "an update that is ready notifies on its own");
        Assert.AreEqual("Updates ready", one.Result);
        Assert.AreEqual("1 update is ready.", one.NotificationText);

        ScheduledCheckFindings mixed = ScheduledCheckFindings.From(Summary(Ready, Ready, Ready, Review, Unavailable, Unavailable));
        Assert.AreEqual("Attention required", mixed.Result);
        Assert.AreEqual("3 updates are ready. 1 app needs review. 2 apps couldn't be checked.", mixed.NotificationText);
        Assert.AreEqual("2 apps need review.", ScheduledCheckFindings.From(Summary(Review, Review)).NotificationText);
        Assert.AreEqual("1 app couldn't be checked.", ScheduledCheckFindings.From(Summary(Unavailable)).NotificationText);

        // Fix review RV-4: two records of one app (Windows and WinGet both list it) are one update, as on the Updates page.
        static PCSteward.Domain.UpdateIntelligenceItem ReadyFor(string key, string package, string version) => new(key, Ready,
            new PCSteward.Domain.PackageMapping("winget", package, version, PCSteward.Domain.MappingConfidence.High, PCSteward.Domain.MappingOrigin.Automatic, "Exact match.", true, [],
                new PCSteward.Domain.VersionComparisonResult(PCSteward.Domain.VersionComparisonOutcome.Newer, PCSteward.Domain.VersionComparisonExplanation.NumericDotted)), "Explanation.");
        ScheduledCheckFindings sameApp = ScheduledCheckFindings.From(new(
            [ReadyFor("registry:7zip", "7zip.7zip", "24.09"), ReadyFor("winget:7zip", "7ZIP.7zip", "24.09"), ReadyFor("registry:vlc", "VideoLAN.VLC", "3.0.21"), ReadyFor("registry:old7zip", "7zip.7zip", "24.08")],
            new Dictionary<PCSteward.Domain.UpdateIntelligenceStatus, int>(), []));
        Assert.AreEqual("3 updates are ready.", sameApp.NotificationText, "four records, three distinct package versions");

        string host = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "PCSteward.ScheduledHost", "Program.cs"));
        StringAssert.Contains(host, "ScheduledCheckFindings.From(updates)", "the host takes its attention and its text from this rule");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [TestMethod]
    public async Task ATimedOutOrFailedCheckIsRecordedAndRaisesTheFailureCount()
    {
        // FR-SCH-04 (four-model review F-09): the check's own token is spent by then, so the recorder uses its own limit.
        StatusStore store = new(new ScheduledCheckStatus(DateTimeOffset.UnixEpoch, "Completed", 0));

        ScheduledCheckStatus? first = await ScheduledCheckFailureRecorder.RecordAsync(store, ScheduledCheckFailureRecorder.TimedOutResult);
        ScheduledCheckStatus? second = await ScheduledCheckFailureRecorder.RecordAsync(store, ScheduledCheckFailureRecorder.FailedResult);

        Assert.AreEqual(1, first!.ConsecutiveFailures);
        Assert.AreEqual("Timed out", first.LastResult);
        Assert.AreEqual(2, second!.ConsecutiveFailures);
        Assert.AreEqual(second, store.Status);
        Assert.IsTrue(store.EveryCallWasLimited, "each read and write gets a cancellable token and a lock-wait limit");
        ScheduledCheckSettings notify = ScheduledCheckSettings.Manual with { NotificationsEnabled = true };
        Assert.IsFalse(ScheduledCheckNotificationPolicy.ShouldNotify(notify, false, false, first.ConsecutiveFailures));
        Assert.IsTrue(ScheduledCheckNotificationPolicy.ShouldNotify(notify, false, false, second.ConsecutiveFailures));
    }

    [TestMethod]
    public async Task AFailureThatCannotBeSavedIsReportedAsNotRecorded()
    {
        StatusStore store = new(new ScheduledCheckStatus(null, "Completed", int.MaxValue)) { FailSave = true };
        Assert.IsNull(await ScheduledCheckFailureRecorder.RecordAsync(store, ScheduledCheckFailureRecorder.FailedResult));
        store.FailSave = false;
        Assert.AreEqual(int.MaxValue, (await ScheduledCheckFailureRecorder.RecordAsync(store, ScheduledCheckFailureRecorder.FailedResult))!.ConsecutiveFailures);
    }

    [TestMethod]
    public async Task FailureRecorderReturnsWithinDeadlineWhileAnotherConnectionHoldsWriteLock()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pcsteward-status-{Guid.NewGuid():N}.db");
        try
        {
            SqliteDatabase database = new(path);
            await database.MigrateAsync(CancellationToken.None);
            using SqliteConnection locker = new(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            locker.Open();
            using SqliteTransaction transaction = locker.BeginTransaction();
            TimeSpan budget = TimeSpan.FromSeconds(2);
            Stopwatch elapsed = Stopwatch.StartNew();
            Assert.IsNull(await ScheduledCheckFailureRecorder.RecordAsync(database, ScheduledCheckFailureRecorder.FailedResult, writeLimit: budget));
            // Far below the 30 seconds an unbounded lock wait took, with room for a slow machine.
            Assert.IsTrue(elapsed.Elapsed < budget + TimeSpan.FromSeconds(5), $"Elapsed {elapsed.Elapsed}");
            transaction.Rollback();
            Assert.AreEqual(ScheduledCheckStatus.NotScheduled, await database.GetScheduledCheckStatusAsync(CancellationToken.None));
            ScheduledCheckStatus? saved = await ScheduledCheckFailureRecorder.RecordAsync(database, ScheduledCheckFailureRecorder.FailedResult, writeLimit: budget);
            Assert.AreEqual(1, saved?.ConsecutiveFailures);
            Assert.AreEqual(saved, await database.GetScheduledCheckStatusAsync(CancellationToken.None));
        }
        finally { File.Delete(path); }
    }
    private sealed class StatusStore(ScheduledCheckStatus status) : IScheduledCheckSettingsStore
    {
        public ScheduledCheckStatus Status { get; private set; } = status;
        public bool FailSave { get; set; }
        public bool EveryCallWasLimited { get; private set; } = true;
        public Task<ScheduledCheckSettings> GetScheduledCheckSettingsAsync(CancellationToken cancellationToken) => Task.FromResult(ScheduledCheckSettings.Manual);
        public Task SaveScheduledCheckSettingsAsync(ScheduledCheckSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ScheduledCheckStatus> GetScheduledCheckStatusAsync(CancellationToken cancellationToken, TimeSpan? lockWaitLimit = null) { EveryCallWasLimited &= cancellationToken.CanBeCanceled && lockWaitLimit > TimeSpan.Zero; return Task.FromResult(Status); }
        public Task SaveScheduledCheckStatusAsync(ScheduledCheckStatus status, CancellationToken cancellationToken, TimeSpan? lockWaitLimit = null)
        {
            EveryCallWasLimited &= cancellationToken.CanBeCanceled && lockWaitLimit > TimeSpan.Zero;
            if (FailSave) throw new IOException("The status could not be saved.");
            Status = status;
            return Task.CompletedTask;
        }
    }

    [TestMethod]
    public async Task WindowsAdapterBuildsFixedScanOnlyTaskWithoutUpdateArguments()
    {
        RecordingTaskSchedulerCommandRunner runner = new();
        WindowsCurrentUserScheduledTaskRegistrar registrar = new(@"C:\Program Files\PC Steward\PCSteward.ScheduledHost.exe", runner);
        await registrar.RegisterOrUpdateAsync(new ScheduledCheckTaskDefinition(ScheduleCadence.Daily, DayOfWeek.Monday, new TimeOnly(9, 5), true), CancellationToken.None);

        CollectionAssert.Contains(runner.Arguments, "/Create");
        CollectionAssert.Contains(runner.Arguments, "/IT");
        CollectionAssert.Contains(runner.Arguments, "/RL");
        CollectionAssert.Contains(runner.Arguments, "LIMITED");
        CollectionAssert.DoesNotContain(runner.Arguments, "update");
        CollectionAssert.DoesNotContain(runner.Arguments, "install");
        StringAssert.Contains(runner.Arguments[runner.Arguments.IndexOf("/TR") + 1], "PCSteward.ScheduledHost.exe");
    }

    [TestMethod]
    public async Task ExactTaskRemovalUsesLanguageIndependentExactQueryBeforeAndAfterDeletion()
    {
        SequencedTaskSchedulerCommandRunner runner = new(new TaskSchedulerCommandResult(0, "deleted", ""));
        RecordingExactTaskQuery query = new(true, false);
        WindowsCurrentUserScheduledTaskRegistrar registrar = new(@"C:\Program Files\PC Steward\PCSteward.ScheduledHost.exe", runner, query);

        await registrar.EnsureAbsentAsync(CancellationToken.None);

        CollectionAssert.AreEqual(DeleteOnly, runner.Commands);
        Assert.AreEqual(2, query.Calls);
    }

    [TestMethod]
    public async Task ExactTaskRemovalFailsWhenTheTaskRemainsPresent()
    {
        SequencedTaskSchedulerCommandRunner runner = new(new TaskSchedulerCommandResult(0, "deleted", ""));
        WindowsCurrentUserScheduledTaskRegistrar registrar = new(@"C:\Program Files\PC Steward\PCSteward.ScheduledHost.exe", runner, new RecordingExactTaskQuery(true, true));

        try { await registrar.EnsureAbsentAsync(CancellationToken.None); Assert.Fail("A residual task must fail closed."); }
        catch (InvalidOperationException) { }
        CollectionAssert.AreEqual(DeleteOnly, runner.Commands);
    }

    [TestMethod]
    public async Task ExactTaskRemovalDoesNotInvokeSchtasksWhenComQueryProvesAbsence()
    {
        SequencedTaskSchedulerCommandRunner runner = new();
        RecordingExactTaskQuery query = new(false);
        WindowsCurrentUserScheduledTaskRegistrar registrar = new(@"C:\Program Files\PC Steward\PCSteward.ScheduledHost.exe", runner, query);

        await registrar.EnsureAbsentAsync(CancellationToken.None);

        Assert.AreEqual(1, query.Calls);
        Assert.AreEqual(0, runner.Commands.Count);
    }

    [TestMethod]
    public void ExactTaskQueryRecognizesNotFoundAcrossComBinderExceptionShapes()
    {
        Assert.IsTrue(WindowsExactScheduledTaskQuery.IsTaskNotFound(new System.IO.FileNotFoundException("missing")));
        Assert.IsTrue(WindowsExactScheduledTaskQuery.IsTaskNotFound(new System.Reflection.TargetInvocationException(new System.IO.FileNotFoundException("missing"))));
        Assert.IsFalse(WindowsExactScheduledTaskQuery.IsTaskNotFound(new InvalidOperationException("scheduler unavailable")));
    }

    private sealed class RecordingRegistrar : ICurrentUserScheduledTaskRegistrar
    {
        public List<ScheduledCheckTaskDefinition> Registered { get; } = [];
        public int Removed { get; private set; }
        public int EnsuredAbsent { get; private set; }
        public Task RegisterOrUpdateAsync(ScheduledCheckTaskDefinition definition, CancellationToken cancellationToken) { Registered.Add(definition); return Task.CompletedTask; }
        public Task RemoveAsync(CancellationToken cancellationToken) { Removed++; return Task.CompletedTask; }
        public Task EnsureAbsentAsync(CancellationToken cancellationToken) { EnsuredAbsent++; return Task.CompletedTask; }
    }

    private sealed class RecordingTaskSchedulerCommandRunner : ITaskSchedulerCommandRunner
    {
        public List<string> Arguments { get; private set; } = [];
        public Task<TaskSchedulerCommandResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) { Arguments = arguments.ToList(); return Task.FromResult(new TaskSchedulerCommandResult(0, "", "")); }
    }

    private sealed class SequencedTaskSchedulerCommandRunner(params TaskSchedulerCommandResult[] results) : ITaskSchedulerCommandRunner
    {
        private readonly Queue<TaskSchedulerCommandResult> results = new(results);
        public List<string> Commands { get; } = [];
        public Task<TaskSchedulerCommandResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            Commands.Add(arguments[0]);
            return Task.FromResult(this.results.Dequeue());
        }
    }

    private sealed class RecordingExactTaskQuery(params bool[] results) : IExactScheduledTaskQuery
    {
        private readonly Queue<bool> results = new(results);
        public int Calls { get; private set; }
        public Task<bool> ExistsAsync(string taskName, CancellationToken cancellationToken) { Calls++; return Task.FromResult(results.Dequeue()); }
    }
}
